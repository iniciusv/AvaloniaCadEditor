# Sweep por pré-seleção: região como seção, arestas encadeadas como caminho

## Contexto

Hoje o Sweep só tem um fluxo: digitar o raio da seção e clicar os pontos do caminho
([SweepCommand.cs:50-94](AvaloniaCadEditor.Application/Commands/SweepCommand.cs#L50-L94)). Isso
obriga a desenhar o caminho de novo mesmo quando ele já existe no esboço, e trava a seção em
círculo — não há como varrer um perfil livre (L, C, caixa, cantoneira).

O pedido: poder **selecionar uma região pré-feita** como seção e **selecionar arestas contínuas
já desenhadas** como caminho, inclusive arrastando o retângulo de seleção. Círculo como seção
deve dar o mesmo resultado de hoje; polígonos livres passam a funcionar.

Decisões tomadas:

1. **Só por pré-seleção.** Com o SelectTool o usuário marca a região e as arestas, aperta `S`, e a
   ferramenta não pergunta nada. O fluxo antigo (raio digitado + cliques) continua intacto quando
   não há pré-seleção utilizável.
2. **Não misturar** cliques no vazio com arestas selecionadas num mesmo caminho.
3. **Incluir** círculo como caminho (anel/toro), via uma volta inteira de 360°.

O que já existe e será reaproveitado: seleção por retângulo
([BoxSelector.cs](AvaloniaCadEditor.UI/Controls/Input/BoxSelector.cs),
[BoxSelectionGeometry.cs](AvaloniaCadEditor.UI/Controls/Input/BoxSelectionGeometry.cs)), modo de
seleção por Edge (tecla `F`), reconhecimento de regiões
([ProfileRecognitionService.cs](AvaloniaCadEditor.Application/Services/ProfileRecognitionService.cs)),
`SweepSection.Polygon` / `FromProfile`, `SweepPath.FromPolyline(..., closed:)`, e o precedente de
pré-seleção do `BooleanTool` ([CadViewModel.cs:840](AvaloniaCadEditor.UI/ViewModels/CadViewModel.cs#L840)).

**Invariantes a respeitar:** ARCHITECTURE §5.2 (um sólido = uma declaração, `Name` literal igual ao
da variável, sem laço), §5.3 (geometria que falha devolve malha vazia, nunca exceção), e a regra de
que ferramentas **emitem CadScript**, não constroem geometria.

---

## O bloqueio central

`SelectionService` começa **toda** chamada com `UnselectAllInSketch`
([SelectionService.cs:174-180](AvaloniaCadEditor.Application/Services/SelectionService.cs#L174-L180)).
Logo, **hoje é impossível ter uma região e arestas marcadas ao mesmo tempo** — e é exatamente isso
que a pré-seleção precisa. Além disso não existe `ToggleEntity` (Ctrl+clique numa linha não
acumula), e o ramo Edge de `BoxSelector.Apply` **descarta** `additiveBase?.Profile`.

A correção é cirúrgica: não alargar o significado de `SelectedObject` (que a árvore, o inspector,
`ExtrudeCommand` e `FilletTool` leem), e sim **parar de apagar**. Os flags `IsSelected` do sketch já
*são* a multi-seleção, e `PreSelectedSolids()` ([CadViewModel.cs:396](AvaloniaCadEditor.UI/ViewModels/CadViewModel.cs#L396))
já lê assim.

---

## Ordem de trabalho

### 1. `SweepPath.FullCircle` — [SweepPath.cs](AvaloniaCadEditor.Domain/ValueObjects/SweepPath.cs)

Um arco só de 360°. **Não é capacidade nova** — `SweepPath.Arc(..., 360)` já passa pelas guardas e
`Varredura360_DeSecaoDeslocada_FechaUmAnel` ([OpenCascadeKernelTests.cs:331](AvaloniaCadEditor.Tests/BehaviorTests/OpenCascadeKernelTests.cs#L331))
prova que o `Revolve` de 2π da OCCT fecha o anel. É conveniência + `IsClosed` correto.

```csharp
public static SweepPath FullCircle(Point3D center, Vector3D axis, double radius)
```

- `radius <= Tolerance` → `Invalid`; `axis.Normalize()` **depois** confere `Length < Tolerance`
  (mesma ordem de `Arc`, porque `Vector3D.Normalize` devolve zero abaixo de 1e-6).
- `start = center + new WorkPlane(a, center).XAxis * radius`; um segmento
  `SweepSegment.Arc(center, a, 2π)`; `IsClosed = true`; `Points = new[] { start }` (convenção de `Arc`).
- `ClosureError()` como defesa: a torção dá ~1e-14°, passa.
- Verificado contra o pipeline: `StartTangent` = `a × radial` (unitário, ⊥ a); `Stations` dá 2 para
  1 segmento, então a guarda `stations.Count <= segments.Count` de `BuildSweep` passa; a guarda de
  raio de curvatura ([OpenCascadeGeometryKernel.cs:564](AvaloniaCadEditor.Infrastructure/OpenCascade/OpenCascadeGeometryKernel.cs#L564))
  vira "a seção não pode ser mais larga que o anel", que é o certo.
- **Sem mudança em `FeatureFingerprint`**: `AppendSweep` já hasheia `StartPlane` + centro/eixo/ângulo
  de cada segmento, e não hasheia `IsClosed` (dois caminhos de segmentos iguais dão sólidos iguais).
- `CadScriptApiReference` reflete estáticos `DeclaredOnly` → pega `FullCircle` sozinho, sem edição.

### 2. `EdgeChainService` — **novo**, em `AvaloniaCadEditor.Application/Services/`

Em Application, não Domain: ARCHITECTURE §6.4 lista `PlanarTopologyService` em `Domain/Services/`
como *resto da arquitetura antiga*, e o análogo exato (`ProfileRecognitionService`, mesmo grafo de
pontas com fusão 1e-4) já mora aqui. O único consumidor é o `SweepCommand`.

```csharp
public sealed record EdgeChain(
    IReadOnlyList<Point3D> Points,   // LOCAIS ao plano; num fechado o 1º não se repete no fim
    bool IsClosed,
    WorkPlane Plane,
    IReadOnlyList<Guid> SourceEntityIds,
    string? Error);

public static class EdgeChainService
{
    public const double Tolerance = 1e-4;          // mesma do ProfileRecognitionService
    public static EdgeChain Chain(IEnumerable<LineEntity> lines);
}
```

Recusas, em ordem, com mensagem em pt-BR: nenhuma linha · planos diferentes (`!Plane.Equals`) ·
linha de comprimento zero · nó de grau ≥ 3 (`"as linhas se ramificam em (x, y)…"`) · mais de um
componente conexo (`"não formam uma cadeia contínua: há N trechos separados"`) · ciclo com menos de
3 arestas. Com todos os graus ≤ 2 e um componente só, o número de nós de grau 1 é
necessariamente 0 (ciclo → `IsClosed`) ou 2 (aberto) — as duas checagens bastam.

**O percurso tem que ser determinístico** (ver §4, orientação): começar pela ponta de grau 1
lexicograficamente menor em `(x, y, z)`; num ciclo, pelo nó menor andando para o vizinho menor.
Sem isso o mesmo desenho produz sólidos espelhados entre sessões.

Copiar o idioma `GetOrCreateNode` de `ProfileRecognitionService`, **não** o DFS (enumerar ciclos é
outro algoritmo).

### 3. Seleção: região + arestas ao mesmo tempo

**[ISelectionService.cs](AvaloniaCadEditor.Application/Services/ISelectionService.cs) / [SelectionService.cs](AvaloniaCadEditor.Application/Services/SelectionService.cs)** — duas adições:

```csharp
void ToggleEntity(Sketch sketch, Entity entity);
void SelectMany(Sketch sketch, IEnumerable<IFeature> features, IEnumerable<Entity> entities, IProfile? profile);
```

O `SelectMany` de 3 argumentos fica como sobrecarga delegando com `profile: null` — zero churn em
chamadores e testes. Campo `private IProfile? _profile` mantido por `SelectProfile` e pela
sobrecarga de 4, limpo por todo o resto; é ele que deixa um `CompoundProfile` (que não está em
`sketch.Profiles`) continuar primário. Prioridade de `SelectedObject`: perfil → 1ª feature → 1ª
entidade → None. Um `SelectionChanged` por chamada. Na sobrecarga de 4, se o perfil for
`CompoundProfile`, marcar também cada `SubProfile` (o `ProfileFaceRenderPass` depende disso).

**[BoxSelector.cs](AvaloniaCadEditor.UI/Controls/Input/BoxSelector.cs), ramo `Edge`:** passar
`additiveBase?.Profile` como 4º argumento, para a região escolhida antes sobreviver ao retângulo.

**[CadViewModel.cs:1382-1388](AvaloniaCadEditor.UI/ViewModels/CadViewModel.cs#L1382-L1388):**
Ctrl+clique numa entidade vai para `ToggleEntity` em vez de `SelectById`.

**[CadViewModel.cs:1394-1443](AvaloniaCadEditor.UI/ViewModels/CadViewModel.cs#L1394-L1443)**
(Ctrl+clique em região) chama `ClearSelection(Sketch)` no meio e apagaria arestas já marcadas.
Correção mínima de 3 linhas: guardar `Sketch.Entities.Where(e => e.IsSelected).ToList()` no começo
do ramo e re-marcar no fim. **Não reescrever o bloco** — ele tem o fallback de perfil-vivo por
geometria e o acumulador de `CompoundProfile` de que o `ExtrudeCommand` depende. Limpeza para
depois.

**Conveniência:** ao entrar em `EditorMode.SketchEdit`, pôr `CurrentSelectionMode = Edge`, senão o
usuário precisa apertar `F` duas vezes antes de o arrasto pegar linhas. Mudar o **modo**, nunca o
dispatch dentro do `BoxSelector` (cuja doc diz que ele depende só do tipo de seleção); se mudar,
atualizar aquela doc.

Nenhum teste existente quebra: a resolução de sobrecarga escolhe a de 3 argumentos exata, e
`DeleteSelected` despacha pelo `SelectedId` único (o Delete continua apagando só a região).

### 4. Seção vinda de um perfil — dois problemas reais

**4.1 Recentragem.** `SweepSection.GetPoints` usa `Outline.Plane.ToLocalSpace`
([SweepSection.cs:61-72](AvaloniaCadEditor.Domain/ValueObjects/SweepSection.cs#L61-L72)), então uma
região desenhada em (100, 50) daria (u,v) = (100, 50) e o tubo sairia **deslocado do caminho**.
O código emitido tem que recentrar no `Center` do perfil:

```csharp
var c = profile.Center;                                     // local ao plano do perfil
var uv = profile.GetVertices(segments)
                .Select(w => profile.Plane.ToLocalSpace(w))
                .Select(p => (u: p.X - c.X, v: p.Y - c.Y));
```

Via `GetVertices` + `ToLocalSpace`, **não** `Vertices[i] - Center`: `CircleProfile` e
`RectangleProfile` não expõem lista local.

**4.2 Orientação em torno do caminho.** Seção e caminho são coplanares, então "preservar a posição
relativa" não quer dizer nada. Convenção: emitir `upReference: <normal do plano do esboço>`.
Conferido na matemática do construtor
([SweepFeature.cs:71-84](AvaloniaCadEditor.Domain/Features/SweepFeature.cs#L71-L84)): com
`up = n` e tangente `t` no plano, `x = n × t` e `YAxis = t × (n × t) = n`. Portanto

| eixo da seção | vai para |
|---|---|
| **u** (X do plano do perfil) | `n × t` — no plano do esboço, perpendicular ao caminho |
| **v** (Y do plano do perfil) | `n` — para fora do plano do esboço |

O perfil desenhado fica "de pé": a largura horizontal vira a largura do tubo perpendicular ao
caminho, e a altura vertical vira a altura acima/abaixo do plano. A alternativa ("seção deitada no
plano") **não existe**: o plano da seção é ⊥ `t` e, como `t ⊥ n`, sempre contém `n`.

Dois bônus: num caminho que fica no plano, todo eixo de arco é `±n`, e girar `n` em torno de `±n` é
identidade — logo `v = n` em **toda** estação, e a holonomia de `ClosureError` é identicamente 0
(cadeia fechada no plano nunca falha por torção).

**A verruga:** `n × t` troca de sinal com a *direção* do caminho, então uma seção assimétrica em u
(um L, um C) sairia espelhada conforme a ponta por onde a cadeia começa. É o que o percurso
determinístico do §2 resolve.

### 5. `FeatureCodeGenerator` — [FeatureCodeGenerator.cs:32-49](AvaloniaCadEditor.Application/Services/FeatureCodeGenerator.cs#L32-L49)

Novas sobrecargas de `GenerateSweepCode` + um `SectionExpression(IProfile?, double)` privado. **A
sobrecarga antiga fica byte-idêntica** (é o que preserva os 4 testes que conferem texto).

```
CircleProfile                            → SweepSection.Circle(r)
RectangleProfile sem rotação             → SweepSection.Rectangle(w, h)      [só legibilidade]
Polygon / Rectangle girado               → SweepSection.Polygon(...)
CompoundProfile                          → RECUSA ("selecione uma região só como seção")
FaceProfile / ParametricBoxFace / …      → RECUSA ("a seção tem que ser uma região do esboço")
> ~64 vértices                           → RECUSA ("simplifique o perfil")
plano da seção ≠ plano da cadeia         → RECUSA
```

Rectangle **girado tem que ir por `Polygon`**: `SweepSection.Rectangle(w,h)` descarta
`RotationAngle` em silêncio. Começar só com `Polygon` e acrescentar o atalho `Rectangle` depois.

**A armadilha que vai estourar primeiro:** `F()` emite `-10` para `-10.0`, e
`new[] { (-10, -5), … }` infere **`(int, int)[]`**, que não converte para
`IEnumerable<(double u, double v)>` — o script gerado **não compila**, e a falha aparece na
*execução da cena*, não no build. Emitir o tipo explícito:

```csharp
SweepSection.Polygon(new (double, double)[] { (-10, -5), (10, -5), (10, 5), (-10, 5) })
```

Regra: `upReference:` só quando a seção veio de uma região pré-selecionada; `closed: true` só
quando a cadeia é ciclo. Assim o fluxo antigo sai idêntico ao de hoje.

**Não apagar as entidades consumidas** (diferente de `GenerateExtrusionCode`, que remove os
`SourceEntityIds`): manter as linhas torna a operação repetível (duas seções no mesmo caminho) e
não destrutiva.

### 6. `SweepCommand` — [SweepCommand.cs](AvaloniaCadEditor.Application/Commands/SweepCommand.cs)

```csharp
public SweepCommand(IProfile? preSelectedSection = null, IEnumerable<Entity>? preSelectedPath = null)
public bool TryStartFromSelection(Sketch sketch);   // chamar DEPOIS de CurrentTool = cmd
public IProfile? Section { get; private set; }
public string? Error { get; private set; }          // vai para a barra de status
```

Parâmetros opcionais ⇒ os 9 testes de `SweepCommandTests` continuam compilando.
`TryStartFromSelection` **não pode** ficar no construtor: o setter de `CurrentTool` só assina
`CodeGenerated` depois de atribuir, então um evento disparado no ctor se perde (mesmo motivo de
`ExtrudeCommand.StartDirectExtrusion` e do `filletCmd.OnMouseDown` serem chamados depois).

Três estados: `Clicks` (fluxo de hoje, inalterado), e — quando a pré-seleção serve — resolve e
emite na mesma chamada, ou cai em `Blocked`.

**Commit automático ao ativar**, não "aperte Enter": foi o que o usuário pediu ("não pergunta
nada"), o sweep é aditivo (um undo desfaz) ao contrário do fillet que é destrutivo e global, e um
raio de canto diferente é uma edição de um token na linha gerada — que é onde a ARCHITECTURE §1/§3.3
diz que parâmetro se edita. Sem beco sem saída: se `FromPolyline` recusar (caso dominante: o raio
de canto padrão não cabe entre duas arestas curtas), o estado vira `Blocked`, a seleção é mantida, e
`OnValueEntered(novoRaio)` re-resolve; um clique em `Blocked` abandona a pré-seleção e volta ao
fluxo de cliques.

**`Resolve()` valida antes de emitir**: montar o `SweepPath` e conferir `.Error` antes de disparar
`CodeGenerated`. Hoje a ferramenta emite código que vira malha vazia em silêncio; num fluxo sem
perguntas isso é inaceitável. Só para o caminho novo — o fluxo de cliques continua emitindo
incondicionalmente.

**Raio de canto padrão** — correção à ideia inicial de usar o circunraio. A guarda do kernel
([:564](AvaloniaCadEditor.Infrastructure/OpenCascade/OpenCascadeGeometryKernel.cs#L564)) exige
`bendRadius ≥ max (v−origin)·bendDir`, e num caminho no plano `bendDir = ±` o eixo **u** da seção.
Logo o que manda é a meia-largura em u:

```csharp
private double DefaultCornerRadius => Section is null
    ? SectionRadius * 2.0
    : 2.0 * _sectionUv.Max(p => Math.Abs(p.u));
```

Para um círculo `max|u| = R`, então isso **reduz exatamente ao `SectionRadius * 2` de hoje** (o
`cornerRadius: 8` de `CliquesMaisEnter_GeramOCaminhoEOTubo` fica preservado por construção). O
circunraio daria 100 numa seção 100×2 e falharia em toda aresta curta sem motivo.

**Reescrever a doc XML das linhas 10-25**, que hoje afirma o contrário do recurso ("por isso NÃO se
escolhe um perfil do sketch"). O texto novo: seção e caminho *são* coplanares, e é justamente por
isso que a **posição** da seção é descartada (recentrada no caminho) e a **rotação** em torno dele é
fixada por convenção. Manter o parágrafo "não constrói geometria".

Atualizar `GetStatusText` (que em `Blocked` mostra o `Error` e como sair) e `GetMeasurementText`.
`UpdateStatusText` já é chamado pelo setter de `CurrentTool` em
[:246](AvaloniaCadEditor.UI/ViewModels/CadViewModel.cs#L246), então a recusa chega à barra de status
sem encanamento novo.

### 7. Ligação e dialeto

**[CadViewModel.cs:835-839](AvaloniaCadEditor.UI/ViewModels/CadViewModel.cs#L835-L839):**

```csharp
SelectSweepToolCommand = new RelayCommand(() =>
{
    var secao = _selectionService.SelectedObject as IProfile;
    var arestas = Sketch.Entities.Where(e => e.IsSelected).ToList();
    CurrentMode = EditorMode.SketchEdit;
    var cmd = new SweepCommand(secao, arestas);
    CurrentTool = cmd;
    cmd.TryStartFromSelection(Sketch);
    UpdateStatusText(CurrentSnappedPoint);
});
```

Acrescentar `SweepCommand` à lista branca da
[linha 238](AvaloniaCadEditor.UI/ViewModels/CadViewModel.cs#L238) para o destaque sobreviver.

Um exemplo novo em [CadScriptProtocol.cs:68-84](AvaloniaCadEditor.Application/Ai/CadScriptProtocol.cs#L68-L84)
(o anel por `FullCircle`) — `CadScriptProtocolExamplesTests` compila e executa cada exemplo, então
vem com cobertura de graça (ARCHITECTURE §5.1 ponto 5). **Não mexer em `CadScriptRules.cs`**: ele só
cita `SweepFeature` genericamente, e mexer obriga `CadScriptRulesTests` a reconciliar prompt +
`api.md` + `AGENTS.md`. Ajustar o rótulo em [Strings.cs:38](AvaloniaCadEditor.UI/Resources/Strings.cs#L38).

**Desambiguação círculo seção vs. caminho — já resolvida de graça:** `OnPointerPressed` testa
entidades antes de regiões, e `MathGeometryEngine.HitTest` mede a distância à **circunferência**
(`|d − R|`, [:25](AvaloniaCadEditor.Infrastructure/Engines/MathGeometryEngine.cs#L25)). Clicar no
traço do círculo dá o `CircleEntity` (caminho); clicar dentro cai no `CircleProfile` (seção).

---

## Código emitido

**Seção circular + cadeia aberta**
```csharp
var caminho_a1b2 = SweepPath.FromPolyline(new[] { new Point3D(0, 0, 0), new Point3D(120, 0, 0), new Point3D(120, 90, 0) }, cornerRadius: 12);
var tubo_a1b2 = new SweepFeature(SweepSection.Circle(6), caminho_a1b2, upReference: new Vector3D(0, 0, 1)) { Name = "tubo_a1b2" };
AddFeature(tubo_a1b2);
```

**Seção poligonal recentrada + cadeia fechada**
```csharp
var caminho_c3d4 = SweepPath.FromPolyline(new[] { new Point3D(0, 0, 0), new Point3D(120, 0, 0), new Point3D(120, 120, 0), new Point3D(0, 120, 0) }, cornerRadius: 20, closed: true);
var tubo_c3d4 = new SweepFeature(SweepSection.Polygon(new (double, double)[] { (-10, -5), (10, -5), (10, 5), (-10, 5) }), caminho_c3d4, upReference: new Vector3D(0, 0, 1)) { Name = "tubo_c3d4" };
AddFeature(tubo_c3d4);
```

**Círculo como caminho**
```csharp
var caminho_e5f6 = SweepPath.FullCircle(center: new Point3D(50, 50, 0), axis: new Vector3D(0, 0, 1), radius: 40);
var tubo_e5f6 = new SweepFeature(SweepSection.Circle(5), caminho_e5f6, upReference: new Vector3D(0, 0, 1)) { Name = "tubo_e5f6" };
AddFeature(tubo_e5f6);
```

**Fluxo antigo — inalterado**, sem `upReference:` e sem `closed:`.

---

## Verificação

### Testes novos

**[SweepCommandTests.cs](AvaloniaCadEditor.Tests/BehaviorTests/SweepCommandTests.cs)** — o ctor
recebe perfil e entidades direto, então não é preciso `Sketch.Profiles` nem `RefreshProfiles`:

1. `SecaoDeRegiaoCircularMaisCadeiaDeLinhas_GeraCircleEPolilinha` — confere `Circle(6)`, os pontos
   em mundo, `cornerRadius:` e `upReference:`.
2. `SecaoPoligonalEhRecentradaNaOrigem` — **o teste decisivo do §4.1**: retângulo 20×10 desenhado
   centrado em (100, 50) emite `(-10, -5) … (10, 5)` e o argumento da seção não contém `100`/`50`.
3. `SecaoPoligonal_OCodigoGeradoCompila` — `RoslynSceneEvaluator().EvaluateAsync(codigo)` →
   `Success`. **Prende a armadilha de tupla `(int,int)[]` do §5 — não pular.**
4. `CirculoComoCaminho_GeraFullCircle`; 5. `CadeiaFechadaDeLinhas_EmiteClosedTrue`.
6. `RaioDeCantoDerivadoDaSecao_EhODobroDaMeiaLarguraEmU` — seção 40×6 → 40; círculo R5 → 10.
7. `RaioDeCantoQueNaoCabe_NaoGeraCodigo_EExplica` — status contém `"não cabe"`.
8. `RaioDeCantoDigitadoDepoisDaFalha_RefazEGera` — prova que `Blocked` é estado vivo.
9. `LinhasQueSeRamificam_NaoGeramCodigo` / `LinhasSoltas_NaoGeramCodigo`.
10. `VariasRegioes_SaoRecusadas`, `FaceDeSolidoComoSecao_ERecusada`,
    `SecaoEmPlanoDiferenteDoCaminho_ERecusada`.
11. `CliqueDepoisDeUmaPreSelecaoRuim_VoltaAoFluxoDeCliques`.
12. **`CodigoGerado_ComSecaoPoligonal_CompilaEViraSolidoComOVolumeCerto`** — no estilo do teste homônimo
    existente: retângulo 20×10 + cadeia em L com canto 30 → Pappus `20·10·(70 + 30·π/2 + 50)` a 1%,
    **mais** extensão em Z ≈ 10 e em Y ≈ 20 no primeiro trecho reto. Só o volume não distingue uma
    seção de pé de uma deitada — são as duas extensões que prendem a convenção do §4.2.

**[OpenCascadeKernelTests.cs](AvaloniaCadEditor.Tests/BehaviorTests/OpenCascadeKernelTests.cs)**
(volume/área, depois do bloco de caminho fechado):

13. `CaminhoDeVoltaInteira_FullCircle_FechaUmAnelSemTampa` — `FullCircle(Origin, UnitZ, 40)` +
    `Rectangle(6,6)`: volume ≈ `6·6·40·2π` a 1%, **e** área ≈ `perímetro · 2π·40` sem termo de tampa.
14. `FullCircle_Degenerado_ExplicaEDevolveMalhaVazia` — `[Theory]` com raio 0, raio −5, eixo nulo:
    `Error` não-nulo, `Segments` vazio, `VertexCount == 0` (§5.3).
15. `FullCircle_SecaoMaiorQueOAnel_ReportaProblema`;
    16. `FullCircle_ComecaNaCircunferencia_ComATangentePerpendicularAoEixo`.

**Novo `Tests/BehaviorTests/EdgeChainServiceTests.cs`** — cadeia aberta com entrada embaralhada ·
cadeia fechada (4 pontos, 1º não repetido) · linha única · grau 3 · dois pares soltos · planos
mistos · duas linhas com as duas pontas em comum · pontas a 5e-5 encadeiam e a 1e-3 não · linha de
comprimento zero · **inverter a ordem da entrada dá a mesma sequência de `Points`** (determinismo do §2).

**`SelectionServiceTests`** (localizar ou criar) — o `SelectMany` de 4 deixa perfil **e** entidades
marcados com o perfil primário · `ToggleEntity` não limpa o perfil · `ToggleEntity` duas vezes volta
ao estado anterior e promove outra entidade em vez de `None` · o `SelectMany` de 3 continua limpando
o perfil · `CompoundProfile` marca cada `SubProfile`.

### Manual

Rodar o app (`AvaloniaCadEditor.UI`), em SketchEdit:

1. Desenhar um retângulo (região) e uma polilinha de 3 linhas ao lado. Clicar dentro do retângulo,
   depois Ctrl+arrastar o retângulo de seleção sobre as linhas → **os dois têm que ficar destacados
   ao mesmo tempo** (é o §3). Apertar `S` → tubo de seção retangular, de pé, seguindo as linhas.
2. A ordem inversa (arrastar nas linhas, depois Ctrl+clicar na região) tem que dar o mesmo.
3. Desenhar um círculo; clicar **no traço** → caminho; clicar **dentro** → seção.
4. Cadeia de arestas curtas com seção larga → nada é criado e a barra de status explica; digitar um
   raio menor conclui.
5. Sem nada selecionado, `S` → fluxo antigo (digitar raio, clicar pontos, Enter) inalterado.

### Build

Fechar o app antes (ARCHITECTURE §8: `MSB3021` é lock, não erro de código).

```bash
dotnet build AvaloniaCadEditor.Infrastructure/AvaloniaCadEditor.Infrastructure.csproj   # compila sem lock
dotnet build AvaloniaCadEditor.slnx
dotnet test AvaloniaCadEditor.Tests/AvaloniaCadEditor.Tests.csproj
```
