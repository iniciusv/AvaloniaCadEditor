# Correção arquitetural do Sweep

## Contexto

A varredura por geometria desenhada está pronta e coberta (48 testes entre
[SweepCommandTests](AvaloniaCadEditor.Tests/BehaviorTests/SweepCommandTests.cs),
[EdgeChainServiceTests](AvaloniaCadEditor.Tests/BehaviorTests/EdgeChainServiceTests.cs) e
[SweepViewModelFlowTests](AvaloniaCadEditor.Tests/BehaviorTests/SweepViewModelFlowTests.cs)),
commits `f432ecb`, `ba8a82c`, `6f38d2f`. Nenhum invariante do ARCHITECTURE foi violado: as camadas
não invertem, a ferramenta emite CadScript, os sólidos são rastreáveis, geometria que falha devolve
malha vazia.

O problema é **forma**, e está concentrado num ponto: o `SweepCommand` acumulou ~15 membros de
estado em 543 linhas, com o caminho representado por dois campos anuláveis paralelos. Foi dessa
dispersão que saíram os três defeitos corrigidos durante a implementação — o estágio parcial que o
Enter emitia pulando checagem de plano, a recusa específica sobrescrita por uma genérica, e o número
digitado caindo no vazio. Enquanto a representação continuar espalhada, o próximo defeito tem a
mesma forma.

Esta é uma refatoração **sem mudança de comportamento** nos itens 1, 2, 4 e 5. Os 48 testes atuais
são a rede: eles têm que passar sem edição. O item 3 muda comportamento de propósito e leva teste
novo.

---

## 1. Um tipo só para o caminho (causa raiz)

Hoje o caminho mora em quatro campos:
[`_chain`, `_circlePath`, `_pathPlane`, `_stagedPath`](AvaloniaCadEditor.Application/Commands/SweepCommand.cs#L99-L105).
É uma união discriminada feita à mão: `Resolve`, `OnKeyDown`, `OnValueEntered` e `HasPathCandidate`
todos ramificam em `_chain != null || _circlePath != null`, e `_pathPlane` é derivável dos dois.

**Novo arquivo** `AvaloniaCadEditor.Application/Commands/SweepPathPlan.cs`:

```csharp
/// <summary>
/// O caminho de uma varredura já interpretado a partir do desenho: o que ele é, o que ele exige e
/// quais entidades o formaram. Um tipo só — com duas formas — para que o resolve não ramifique.
/// </summary>
internal abstract record SweepPathPlan(WorkPlane ReferencePlane, IReadOnlyList<Entity> Source)
{
    /// <summary>O caminho conferido pelo domínio, com o raio de canto escolhido.</summary>
    public abstract SweepPath Build(double cornerRadius);

    /// <summary>O maior raio de canto que cabe, ou infinito quando não há canto para limitar.</summary>
    public abstract double MaxCornerRadius();
}

internal sealed record PolylinePlan(
    IReadOnlyList<Point3D> World, bool IsClosed, WorkPlane ReferencePlane, IReadOnlyList<Entity> Source)
    : SweepPathPlan(ReferencePlane, Source);

internal sealed record FullCirclePlan(
    Point3D Center, Vector3D Axis, double Radius, WorkPlane ReferencePlane, IReadOnlyList<Entity> Source)
    : SweepPathPlan(ReferencePlane, Source);
```

O record fica **burro de propósito**: não chama o `FeatureCodeGenerator`. A emissão continua no
comando, num `switch` expression único — um ponto de ramificação em vez de quatro.

No `SweepCommand`: os quatro campos viram `private SweepPathPlan? _path;`. Consequências diretas:

- `StageFromEntities` passa a devolver `SweepPathPlan?` em vez de mexer em quatro campos — e o
  cuidado de "resolve em locais e publica no fim" deixa de ser necessário, porque publicar é uma
  atribuição só. O bug do estágio parcial fica **impossível por construção**, não por disciplina.
- `HasPathCandidate` e as três ramificações viram `_path != null`.
- `ConsumeSketchGeometry` lê `_path.Source` em vez de `_stagedPath`.
- `Resolve` fica linear: `_path.MaxCornerRadius()` → ajusta o raio → `_path.Build(raio)` → confere
  `.Error` → emite pelo `switch` → consome o desenho.

## 2. Despachar por capacidade, não por tipo

`SweepCommand` é verificado por tipo em quatro lugares da UI
([:241](AvaloniaCadEditor.UI/ViewModels/CadViewModel.cs#L241),
[:621](AvaloniaCadEditor.UI/ViewModels/CadViewModel.cs#L621),
[:1494](AvaloniaCadEditor.UI/ViewModels/CadViewModel.cs#L1494),
[ViewportInputHandler.cs:115](AvaloniaCadEditor.UI/Controls/Input/ViewportInputHandler.cs#L115)).
É assim que o `CadViewModel` virou god object (§6.3), e o codebase já tem o remédio no próprio
padrão: `ITransformPreviewTool` e `IGuideProvider` são contratos laterais.

**Duas interfaces pequenas**, cada uma com um motivo para existir, em
`AvaloniaCadEditor.Application/Commands/`:

```csharp
/// <summary>Ferramenta cujo clique tem tolerância de acerto em unidades de mundo.</summary>
public interface IHitTolerantTool
{
    double HitTolerance { get; set; }
}

/// <summary>
/// Ferramenta que ESCOLHE geometria já desenhada com o clique, em vez de desenhar.
/// </summary>
public interface IGeometryPickingTool : IHitTolerantTool
{
    /// <summary>
    /// Consome o clique como escolha. Recebe o ponto CRU, sem snap — com snap, clicar dentro de uma
    /// região pularia para a ponta de uma linha vizinha. True quando escolheu algo: aí a UI não
    /// leva o clique adiante.
    /// </summary>
    bool TryPickAt(Sketch sketch, Point3D rawPoint);

    /// <summary>A ferramenta usa o retângulo de seleção para escolher.</summary>
    bool UsesSelectionBox { get; }
}
```

- `SelectTool`, `TrimCommand`, `ExtendCommand` passam a declarar `IHitTolerantTool` (eles já têm a
  propriedade), e `ApplyHitTolerance` colapsa de um `switch` de quatro casos para
  `if (CurrentTool is IHitTolerantTool t) t.HitTolerance = HitTolerance;`.
- `SweepCommand` declara `IGeometryPickingTool`, e os dois despachos restantes param de nomeá-lo.

**Fora do escopo:** a lista branca da [linha 241](AvaloniaCadEditor.UI/ViewModels/CadViewModel.cs#L241)
(`!(value is SelectTool) && !(value is MoveTool) && ...`). Ela já nomeava cinco ferramentas antes
deste trabalho; trocá-la por uma capacidade `KeepsPreSelection` é dívida própria, de outro commit,
e mexer nela arrisca comportamento de Move/Rotate/Fillet/Boolean sem nada a ganhar aqui.

## 3. Não colher a seleção global às cegas (muda comportamento)

[`ColherArestas`](AvaloniaCadEditor.Application/Commands/SweepCommand.cs#L460) varre
`sketch.Entities.Where(IsSelected)`, então sobra de seleção de **antes** de acionar a ferramenta
entra no caminho sem ninguém pedir. Com o Enter obrigatório o estrago é visível, mas a entrada da
ferramenta continua sendo estado invisível.

**Correção barata:** tirar um retrato das entidades já marcadas no momento da ativação e descontá-lo
das colheitas seguintes — o que a pré-seleção entrega continua valendo (ela é intencional, vem pelo
construtor), e o que for marcado *depois* (clique ou retângulo) soma. Teste novo:
"aresta marcada antes de acionar, e não pré-selecionada, não entra no caminho".

**Correção completa, como follow-up:** assinar `ISelectionService.SelectionChanged` e receber a
notificação, como o `BooleanTool` faz ([BooleanTool.cs:45](AvaloniaCadEditor.Application/Commands/BooleanTool.cs#L45)).
Exige dar o `Sketch` à ferramenta e torná-la `IDisposable`; é o padrão certo, mas é mais superfície
do que o problema pede agora.

## 4. Tirar a duplicação de recuo no Domain

`MaxCornerRadius` recalcula `dirs`, `lens` e `tan(θ/2)` exatamente como `FromPolyline`
([SweepPath.cs](AvaloniaCadEditor.Domain/ValueObjects/SweepPath.cs)). Duas contas do mesmo recuo em
funções vizinhas derivam na primeira vez que alguém mexer numa tolerância.

Extrair um privado que os dois consomem:

```csharp
/// <summary>
/// Direções e comprimentos de cada trecho, e tan(θ/2) de cada vértice — a conta de recuo
/// (R·tan(θ/2)) de que FromPolyline e MaxCornerRadius precisam. Null quando o caminho reverte
/// 180° num vértice, que não se arredonda.
/// </summary>
private static (Vector3D[] Dirs, double[] Lens, double[] TanHalf, int Reversao)? Trims(
    IReadOnlyList<Point3D> pts, bool closed)
```

`FromPolyline` precisa saber **qual** vértice reverteu (a mensagem de erro cita o ponto), daí o
índice no retorno. Fazer este item **primeiro**: é isolado, no Domain, e os testes de
`OpenCascadeKernelTests` sobre caminho aberto, fechado e torção cobrem a conta inteira.

## 5. Um canal de erro só

- [`FitCornerRadius`](AvaloniaCadEditor.Application/Commands/SweepCommand.cs#L339) devolve o motivo
  por `out string?` enquanto todo o resto da classe usa o campo `Error` + `Fail()`. Trocar por
  `double?` (null = recusado, com `Fail` chamado dentro).
- `Fail()` devolve `bool` só para permitir `return Fail(...)`, e isso levou o `TrySetSection` a
  escrever `Fail(...); return true;` — dois booleanos de significado oposto ("consumiu o clique" e
  "deu certo") passando pelo mesmo helper. `Fail` passa a devolver `void`; os call sites ganham um
  `return false;` explícito, que é mais verboso e muito menos enganoso.

## 6. Opcionais, de baixo valor

- Mover o record `EdgeChain` para `Domain/ValueObjects/`: ele é geometria pura (`Point3D`,
  `WorkPlane`, `Guid`). O **serviço** fica em Application de propósito — §6.4 trata
  `Domain/Services` como resto da arquitetura antiga.
- `ConsumeSketchGeometry` não remove nada quando `Section.SourceEntityIds` vem vazio. Está correto
  (região que não veio de linhas de esboço não tem o que consumir), mas merece a frase no comentário
  para não parecer esquecimento.

---

## Ordem

1. **Item 4** — Domain, isolado, coberto pelos testes de kernel.
2. **Item 1** — a causa raiz. É o item grande.
3. **Item 5** — cai por gravidade junto com o 1.
4. **Item 2** — UI, mecânico.
5. **Item 3** — muda comportamento, leva teste novo.
6. **Item 6** — se sobrar vontade.

Itens 1–2 e 4–5 podem ir num commit de refatoração só (`refactor(sweep):`), porque não mudam
comportamento. O item 3 vai separado, porque muda.

## Verificação

A rede é não ter que editar teste nenhum nos itens 1, 2, 4 e 5:

```bash
# com o app FECHADO (MSB3021/MSB3027 é lock, não erro de código — ARCHITECTURE §8)
dotnet build AvaloniaCadEditor.slnx
dotnet test AvaloniaCadEditor.Tests/AvaloniaCadEditor.Tests.csproj
```

Esperado: **700 passando**, e as 5 falhas conhecidas e alheias — 3 de
`CheckCommandProcessTests` (aparecem só com `-p:BaseOutputPath=`, usado quando o app está aberto) e
2 de `ConexaoYAdaptador5mmTests`, que já falham no HEAD limpo.

Na tela, depois do build, o roteiro que tem que continuar igual:

1. Desenhar uma região e uma cadeia de linhas. Acionar Sweep, clicar dentro da região, clicar nas
   linhas, Enter → tubo, e o desenho usado desaparece.
2. Região em XY com caminho em XZ → funciona (foi o bug que motivou tudo).
3. Clicar no traço de um círculo → anel; clicar no miolo → seção.
4. Arrastar o retângulo sobre as linhas em vez de clicar uma a uma → mesmo resultado.
5. Com caminho escolhido, digitar um número → é o raio do canto e conclui.
6. Arestas curtas com seção larga → recusa com as duas medidas na barra de status.

---

## Resultado (executado)

Commits `7616281` (refatoração) e `dd655b8` (nota de doc). **700 testes passando, nenhum editado** —
a rede valeu para os itens 1, 2, 4 e 5.

| Item | Situação |
|---|---|
| 1. Um tipo para o caminho | Feito. `SweepPathPlan` (`PolylinePlan` \| `FullCirclePlan`); 4 campos → 1 |
| 2. Despacho por capacidade | Feito. `IHitTolerantTool`, `IGeometryPickingTool`; 4 verificações de tipo → 0 |
| 3. Não colher a seleção global | **Descartado** — ver abaixo |
| 4. Duplicação de recuo | Feito. `SweepPath.MeasurePolyline` |
| 5. Canal de erro único | Feito. `Fail` devolve void; `FitCornerRadius` devolve `double?` |
| 6. Opcionais | Nota de doc feita; mover `EdgeChain` para Domain descartado |

### Por que o item 3 foi descartado

A correção barata que eu propus — "tirar um retrato das entidades já marcadas na ativação e
descontá-lo das colheitas seguintes" — é **incoerente**. A pré-seleção que o
[CadViewModel entrega](AvaloniaCadEditor.UI/ViewModels/CadViewModel.cs#L840) *é* exatamente
`Sketch.Entities.Where(e => e.IsSelected)` no momento da ativação. Logo o retrato e a pré-seleção
são o mesmo conjunto, e descontar um descontaria o outro: a feature de pré-seleção deixaria de
funcionar.

A alternativa que eu considerei no lugar — "a escolha explícita substitui a pré-seleção" — troca uma
surpresa por outra: quem pré-selecionou três linhas e clica a quarta para somar perderia as três.
Pior que o problema.

O risco que sobra é "a pré-seleção pega algo que o usuário esqueceu que estava marcado". Isso é
inerente a qualquer fluxo de pré-seleção — a extrusão e a booleana têm o mesmo — e já está mitigado:
o Enter é obrigatório, e a recusa nomeia o problema ("não formam uma cadeia contínua", "se
ramificam"). Não vale trocar comportamento por isso.

Se algum dia incomodar de verdade, o caminho é o `BoxSelector` **entregar** as arestas à ferramenta
em vez de ela ler o esboço, e aí o fluxo de dados fica todo explícito. É mais superfície do que o
problema pede hoje.

### Por que mover `EdgeChain` para Domain foi descartado

O record só é produzido e consumido por código de Application. Levá-lo para Domain criaria um tipo
que ninguém em Domain usa — um tipo mora onde é usado, e reduzir "camada certa" a "geometria vai
para Domain" é cargo cult.
