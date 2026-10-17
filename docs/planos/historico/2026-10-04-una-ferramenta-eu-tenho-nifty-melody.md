# ConeFeature — primitivo de tronco de cone

## Context

O editor tem `CylinderFeature` como primitivo nativo, mas não tem cone. Isso bloqueia os casos
mais comuns de peça impressa: furo escareado, chanfro cônico de furo, ponta cônica, funil.

Decisões tomadas com o usuário:

1. **Tronco de cone** (`bottomRadius` + `topRadius`), não cone puro. Um tipo só cobre cone cheio
   (`topRadius = 0`), tronco e escareado. Nomes de tipo/propriedade em inglês, docs/UI em português
   (convenção do repo).
2. **Escopo: núcleo + script/IA.** Fora: picking de face no viewport, `BoxSelector`,
   `PropertyInspector`, ícone/rótulo na árvore, fillet dedicado de cone, engines de reconstrução,
   `ParametricConeFace`.
3. **Revolve exato** de um trapézio em torno do eixo — mesmo idioma de `CreateCylinderCutter`.
4. **Piso de facetas por `ProfileSegments`** em `TessellateShape`, para o cone não sair mais
   grosseiro que o cilindro ao lado dele (ver §2.5).

### O que "revolve exato" significa aqui

A malha exibida é facetada nos dois casos. A diferença é *onde* mora a aproximação:

| | B-Rep | aproximação |
|---|---|---|
| `CylinderFeature` hoje | prisma de 32 lados (`BuildCylinder:647` → `CircleProfile` → extrusão) | **no modelo** — booleana corta 32 faces planas |
| `ConeFeature` (este plano) | cone verdadeiro | **só na malha** — booleana corta a superfície cônica exata |

---

## 1. `AvaloniaCadEditor.Domain/Features/ConeFeature.cs` (novo)

Espelha [`CylinderFeature.cs`](AvaloniaCadEditor.Domain/Features/CylinderFeature.cs).

```csharp
public class ConeFeature : FeatureBase
{
    public const double ApexTolerance = 1e-9;

    public Point3D GlobalBaseCenter { get; set; }
    public Vector3D GlobalDirection { get; set; }   // normalizado no ctor
    public double BottomRadius { get; set; }
    public double TopRadius { get; set; }           // 0 = cone completo (ápice)
    public double Height { get; set; }

    public bool IsFullCone => TopRadius <= ApexTolerance;

    public ConeFeature(Point3D globalBaseCenter, Vector3D globalDirection,
                       double bottomRadius, double topRadius, double height, string? name = null)
    { /* ...; Name = name ?? $"Cone (R{bottomRadius}, r{topRadius}, H{height})"; */ }

    public Point3D GetFaceCenter(FaceType faceType)     // Bottom/Top, idêntico ao cilindro
    public Vector3D GetFaceNormal(FaceType faceType)    // idêntico ao cilindro
    public FaceType? GetFaceTypeFromNormal(Vector3D normal)
    public override IReadOnlyList<Point3D> GetVertices() // { bottomCenter, topCenter }
}
```

**Validação: nenhuma no construtor.** `CylinderFeature:21`, `BoxFeature:19` e `PrismFeature:13` não
validam nada; só `BooleanFeature:26` lança (e só para nulos estruturais). Valores degenerados são
barrados no kernel com `return null`, no estilo de `BuildExtrude:678`. **`Height` negativa é válida**
(cone para trás) — o trapézio espelha e o Newell corrige o sentido.

**Não adicionar `FromWorkPlane`.** A versão do cilindro (`CylinderFeature:35-52`) está admitidamente
quebrada ("Mocking the transformation", ignora `XAxis`/`YAxis`); como `CadScriptApiReference`
emite métodos estáticos por reflexão, copiá-la anunciaria uma fábrica errada para a IA no `api.md`.

**`GetFaceTypeFromNormal` precisa de um ajuste que o cilindro não precisa.** Dois casos:
- `topRadius == 0` → não existe face de topo; devolver `null`, não `FaceType.Top`.
- A lateral do cone é **inclinada**. Num cone raso (`R=10, r=0, h=1`) a componente axial da normal
  lateral é `0.995`, que passa pelo `tol = 0.99` do cilindro e seria reportada como `Top`. Comparar
  a normal contra a axial da lateral (`(R-r)/slant`) e devolver `null` se estiver mais perto dela.

---

## 2. Kernel — `AvaloniaCadEditor.Infrastructure/OpenCascade/OpenCascadeGeometryKernel.cs`

### 2.1 Extrair `RevolveProfile` (passo isolado, antes do cone)

`CreateCylinderCutter:287-410` já contém todo o maquinário de revolve. Extrair as partes genéricas
num helper compartilhado:

```csharp
private static OcctShape RevolveProfile(IReadOnlyList<(double r, double z)> points2D,
                                        Point3D origin, Vector3D dir)
```

Move **verbatim**: o bloco do vetor base `u` (`:292-299`) e o bloco mapear→Newell→edges→wire→face→
`face.Revolve(axis, 2π)` (`:355-409`). Verificado: `points2D` nunca lê `u`, então hoistar `u` para
dentro do helper preserva o comportamento. `CreateCylinderCutter` fica com `:289-290` + `:301-353` +
`return RevolveProfile(points2D, origin, dir);`; a assinatura não muda (`settings` ainda é usado
em `:314`/`:338`).

Acrescentar no helper um dedup de pontos coincidentes consecutivos com **`1e-9`** — e não o `1e-5`
de `BuildExtrude:714`: num fillet de raio pequeno os pontos do cutter podem estar legitimamente a
menos de `1e-5` um do outro, e mesclá-los corromperia o chanfro.

### 2.2 O perfil `(r, z)` — precisa encostar no eixo

Um perfil que **não** encosta no eixo (o que `CreateCylinderCutter` monta: todo ponto tem `r > 0`)
revolve num **anel** — certo para um cutter de fillet, errado para um cone. Para o cone, o lado que
fecha corre **sobre** o eixo; o OCCT não gera face para uma aresta invariante pela rotação:

| aresta do perfil | vira |
|---|---|
| `(0,0) → (R,0)` | disco de baixo |
| `(R,0) → (r,h)` | superfície cônica exata |
| `(r,h) → (0,h)` | disco de cima (ausente se `r = 0`) |
| `(0,h) → (0,0)` (no eixo) | nada |

Montar condicionalmente, nunca por pós-filtro — com `topRadius = 0` a lista de 4 pontos repetiria
`(0, h)` e geraria um `OcctEdge` de comprimento zero:

```csharp
var points2D = new List<(double r, double z)> { (0, 0) };
if (bottomRadius > ConeFeature.ApexTolerance) points2D.Add((bottomRadius, 0));
if (topRadius    > ConeFeature.ApexTolerance) points2D.Add((topRadius, height));
points2D.Add((0, height));
```

Conferido à mão: com `u = x̂`, `dir = ẑ`, o Newell do triângulo do cone completo dá `(0,−1,0)` e
`cross(u, dir) = (0,−1,0)` → produto `+1 > 0` → **sem reversão**. A ordem acima já está correta
sob a regra de `:384-387`.

### 2.3 `BuildCone` e o `transform`

**O `Placement` tem de mover o eixo de revolução, não só o perfil.** O padrão de duas linhas de
`BuildExtrude:683-689` (transformar vértices + direção) é *insuficiente* para um revolve: o perfil
giraria em torno do eixo antigo, produzindo um sólido plausível na orientação errada — sem exceção,
sem malha vazia. Transformar o eixo **primeiro** e derivar tudo dele:

```csharp
var origin = cone.GlobalBaseCenter;
var dir = cone.GlobalDirection;
if (transform != null && !transform.IsIdentity)
{
    origin = transform.Apply(origin);
    dir = transform.ApplyVector(dir);
}
dir = dir.Normalize();
```

Assim o `u` nasce dentro de `RevolveProfile` a partir do `dir` **já no mundo**, e `u ⟂ dir` fica
garantido por construção em vez de depender de o `Placement` ser rígido. Raios e altura são
escalares invariantes por rotação.

Guardas degeneradas no início (`return null`, que `BuildFeatureMesh:125` vira `TriangleMesh.Empty`):
raio negativo; `|height| < 1e-9`; ambos os raios ≤ `ApexTolerance`; `dir` nulo.

**Branch de despacho:** inserir logo depois do `CylinderFeature`, entre as linhas 188 e 189 de
`BuildShape`. `ConeFeature : FeatureBase`, não `: CylinderFeature`, então não há sombreamento de `is`.

### 2.4 Fillet e kernel de fallback — sem mudança

- `BuildFillet:417` trata `CylinderFeature` à parte; o cone cai no caminho genérico por malha
  (`:449-633`). **Não quebra** (`edgeConvex` filtra, e o loop não faz nada sem arestas), mas são
  ~20 `Cut` sequenciais por aresta. Fora do escopo; deixar um comentário de uma linha em `:417`.
- `PureMathGeometryKernel` só trata Extrude/Prism e já devolve `TriangleMesh.Empty` para
  `BoxFeature`, `CylinderFeature`, `BooleanFeature` e `FilletFeature`. É um fallback morto que não
  desenha nem uma caixa. **Não tocar.**

### 2.5 Piso de facetas em `TessellateShape`

Hoje o cilindro obedece `ProfileSegments = 32` e um cone revolvido obedeceria
`LinearDeflection = 0.1` / `AngularDeflection = 0.5` → ~13 facetas em `R=2` e ~23 em `R=10`.
Resultado: o cone sairia visivelmente mais grosseiro que o cilindro, e `ProfileSegments` não o
melhoraria. Unificar no **único** ponto de chamada ([`:861`](AvaloniaCadEditor.Infrastructure/OpenCascade/OpenCascadeGeometryKernel.cs#L861)):

```csharp
// ProfileSegments é o knob de "quão redondo é redondo" em todo o app: o cilindro o usa direto
// (polígono extrudado) e aqui ele vira um PISO de facetas para as superfícies curvas exatas,
// para um cone e um cilindro de mesmo raio não saírem com contagens diferentes.
var angular = Math.Min(settings.AngularDeflection,
                       2.0 * Math.PI / Math.Max(settings.ProfileSegments, 3));
var occtMesh = shape.Triangulate(settings.LinearDeflection, angular);
```

Superfície de risco pequena: faces planas ignoram deflexão, então **a única geometria existente
afetada são os fillets** (que ficam mais finos e mais precisos). `GeometryKey:86` já inclui
`ProfileSegments` na chave, então o cache continua correto.

### 2.6 Contingência (só se o revolve no eixo falhar)

O wrapper `OcctNet.Wrapper 0.1.1` não tem cone, loft nem sew — revolve é a única rota. Se o passo 3
der malha vazia, o plano B é puramente booleano, com idiomas já provados no arquivo:
`cone = cylinder(R, h) ⟍ RevolveProfile([(R,0),(R+δ,0),(R+δ,h),(r,h)])` com `δ = 0.1·max(R,1)` —
perfil que não encosta no eixo (caso do fillet cutter), ao custo de um `Cut` extra. **Não construir
preventivamente.**

---

## 3. `FeatureFingerprint` e o teste-guarda

[`FeatureFingerprint.cs`](AvaloniaCadEditor.Domain/Features/FeatureFingerprint.cs): acrescentar
`ConeFeature cone => AppendCone(sb, cone),` depois da linha 44, e o `AppendCone` depois do
`AppendCylinder` (`:70-77`) com os cinco parâmetros. O nome do tipo entra sozinho na chave via
`GetType().FullName` (`:38`), então cone e cilindro com os mesmos números nunca colidem.

> **Atenção:** `FeatureFingerprintTests.cs:208-217` **não** descobre tipos sozinho — é uma `[Theory]`
> com `[InlineData]` escritos à mão. Um tipo novo que ninguém listar simplesmente nunca é checado, e
> o teste passa verde enquanto o fingerprint ignora o tipo. O doc comment em `:202-207` promete mais
> do que o mecanismo entrega.

Linha obrigatória:

```csharp
[InlineData(typeof(ConeFeature), new[] { "GlobalBaseCenter", "GlobalDirection", "BottomRadius", "TopRadius", "Height", "IsFullCone" })]
```

Opcional (recomendado, ~10 linhas no mesmo arquivo): um `[Fact]`
`TodaFeatureConcretaDoDominio_EstaNaTheoryDeCobertura` que reflete sobre os `FeatureBase` concretos
do Domain e assere `BeSubsetOf` dos tipos listados na theory, com uma lista de exceções
(`ImportedMeshFeature`, `WorkPlaneFeature`, `PartInstance`). Se virar yak-shave, descartar — o
`[InlineData]` é a parte obrigatória.

---

## 4. Superfície de IA

| arquivo | mudança |
|---|---|
| [`CadScriptApiReference.cs:22-29`](AvaloniaCadEditor.Application/Ai/CadScriptApiReference.cs#L22) | `typeof(ConeFeature),` no array `FeatureTypes`. O resto do bloco é gerado por reflexão (`AppendType:83-116`) — ctor, propriedades e métodos saem sozinhos. |
| [`CadScriptRules.cs:33`](AvaloniaCadEditor.Application/Ai/CadScriptRules.cs#L33) | incluir `ConeFeature` na lista de sólidos rastreáveis de `TrackableSolids`. |
| [`CadScriptProtocol.cs:31-63`](AvaloniaCadEditor.Application/Ai/CadScriptProtocol.cs#L31) | nova entrada em `Examples`: furo escareado (caixa 40×40×10 menos cone `R=6, r=3, h=4` a partir da face de cima). |
| [`CadScriptApiReferenceTests.cs:15`](AvaloniaCadEditor.Tests/Ai/CadScriptApiReferenceTests.cs#L15) | `reference.Should().Contain("new ConeFeature(");` |

**O exemplo vai em `CadScriptProtocol.Examples`, não em `CadScriptRules`**, por dois motivos:
`CadScriptProtocolExamplesTests.cs:21-35` é uma `[MemberData]` theory que **compila e executa** cada
exemplo no `RoslynSceneEvaluator` — cobertura de graça, sem tocar em arquivo de teste. Já criar um
`PartExample…` novo em `CadScriptRules` **quebraria**
`CadScriptRulesTests.PartExamplesDoProtocolo_SaoOsDaFonteUnica` (`:71-79`), que assere
`Should().Equal(…)` com exatamente três constantes.

Nenhum teste quebra ao editar o texto de `TrackableSolids`: `CadScriptRulesTests.cs:15-22` monta os
blocos **por referência** e só assere que os três consumidores os *contêm*.

`AiCore/.cad/api.md` e `TestScene/.cad/api.md` se regeneram sozinhos via
`SceneProjectTemplate.RefreshGeneratedFiles:62-76`; nada assere sobre as cópias versionadas.

---

## 5. Testes

### A armadilha de tesselação

Com o piso de facetas de §2.5 em `ProfileSegments = 32`, a razão de volume do polígono inscrito é
`k(32) = 0.9968` → **0.32% abaixo**, dentro da tolerância padrão de 1% do repo. **Sem** o piso seria
1.2–3.8% abaixo e os testes de cone isolado falhariam. Se §2.5 for adiado por algum motivo, os
testes de volume de cone isolado precisam de `TessellationSettings` fina
(`LinearDeflection = 0.005`). Deixar isso num comentário no topo da região de testes, no estilo da
nota de winding em `OpenCascadeKernelTests.cs:51-56`.

### Testes a acrescentar (todos em arquivos existentes)

**`BehaviorTests/SceneAsCodeTests.cs`** — ao lado de `CylinderFeature_DeveGerarMalhaNoKernel:18-25`:
- `ConeFeature_DeveGerarMalhaNoKernel` — `R=10, r=4, h=30`, `TriangleCount > 0`.
  **Escrever e rodar primeiro: é o go/no-go do revolve no eixo.**

**`BehaviorTests/OpenCascadeKernelTests.cs`** — nova região antes de `:200`, todos com
`MeshVolumeCalculator.ComputeVolume` + `BeApproximately(esperado, esperado * 0.01)`, no molde de
`CaixaMenosCilindro_VolumeDeveSerExato:86-99`:

| teste | geometria | esperado |
|---|---|---|
| `TroncoDeCone_VolumeDeveSerExato` | `R=10, r=5, h=20` | `(πh/3)(R²+Rr+r²)` ≈ 3665,19 |
| `ConeCompleto_TopRadiusZero_VolumeDeveSerExato` | `R=10, r=0, h=20` | `πR²h/3` ≈ 2094,40 |
| `ConeComRaiosIguais_TemOVolumeDeUmCilindro` | `R=r=6, h=15` | `πR²h` ≈ 1696,46 |
| `CaixaMenosCone_FuroEscareado_VolumeDeveSerExato` | caixa 40×40×10; cone em `(20,20,10)`, dir `−Z`, `R=6, r=3, h=4` | `16000 − (4π/3)(36+18+9)` ≈ 15736,1 |
| `ConeGirado_ComoFeatureTransformada_MantemOVolumeEMudaDeLugar` | `R=8, r=0, h=20` + `Placement.RotationAbout(origin, UnitX, 90)` | volume mantido **e** ápice em `y ≈ −20` |
| `ConeDegenerado_AlturaZero_DevolveMalhaVaziaSemExcecao` | `h = 0` | `VertexCount == 0` |

O teste de rotação é o **único** que exercita o `transform` de §2.3 — sem ele, um descasamento
eixo/perfil passa despercebido. Molde: `TransformedFeature_PrismaComPlanoProprio_Girado_MantemOVolume:202-220`,
mas **com a asserção de posição**: um revolve no eixo errado pode ter o volume certo.

Começar por `ConeComRaiosIguais_…`: se o revolve tiver produzido um anel em vez de um sólido, o
número sai inconfundivelmente errado, e é o único com conferência independente (contra `BuildCylinder`).

**`Features/FeatureFingerprintTests.cs`** — no molde de `ParametrosDoCilindroEDoPrisma_MudamAChave:63-78`:
- `ParametrosDoCone_MudamAChave` — variar os 5 parâmetros, **inclusive `TopRadius` → 0** (um
  fingerprint que o esqueça faria um tronco reusar a malha de um cone completo).
- `ConeECilindroComOsMesmosNumeros_TemChavesDiferentes`.

**`Features/GeometryMeshCacheTests.cs`**:
- `ConeIgualRecriado_EntreExecucoes_NaoRetessela` — 8 linhas, e é a única coisa que prova que
  `AppendCone` está de fato ligado ao switch (sem ele `Compute` devolve `null` e `MeshBuildCount`
  seria 2, sem nenhum outro sintoma).

---

## 6. Ordem de execução

1. `ConeFeature.cs` (§1) — compila sozinho.
2. **Extração do `RevolveProfile` + reescrita do `CreateCylinderCutter` (§2.1), isolada.** Rodar os
   testes de fillet e cache **antes** de tocar em qualquer coisa de cone, para atribuir uma
   regressão de winding/`u` corretamente.
3. Piso de facetas em `TessellateShape` (§2.5), também isolado — rodar a suíte inteira e confirmar
   que só mudam (para melhor) os volumes de fillet.
4. `BuildCone` + branch no `BuildShape` (§2.2–2.3) + `ConeFeature_DeveGerarMalhaNoKernel` (§5).
   **Go/no-go**: se `TriangleCount == 0`, ir para a contingência §2.6 antes de escrever mais testes.
5. `FeatureFingerprint` (§3) + `[InlineData]` + testes de fingerprint e de cache.
6. Testes de volume (§5), começando por `ConeComRaiosIguais_…`.
7. Superfície de IA (§4) por último — é só texto, não quebra geometria.

---

## 7. Verificação

```bash
# depois do passo 2 — a extração não pode mexer no cutter de fillet
dotnet test AvaloniaCadEditor.Tests --filter "FullyQualifiedName~Fillet"
dotnet test AvaloniaCadEditor.Tests --filter "FullyQualifiedName~GeometryMeshCache"

# depois do passo 3 — o piso de facetas mexe em toda superfície curva
dotnet test AvaloniaCadEditor.Tests

# depois do passo 4 — o go/no-go
dotnet test AvaloniaCadEditor.Tests --filter "FullyQualifiedName~ConeFeature_DeveGerarMalhaNoKernel"

# passos 5-7
dotnet test AvaloniaCadEditor.Tests --filter "FullyQualifiedName~FeatureFingerprintTests"
dotnet test AvaloniaCadEditor.Tests --filter "FullyQualifiedName~Cone"
dotnet test AvaloniaCadEditor.Tests --filter "FullyQualifiedName~AvaloniaCadEditor.Tests.Ai"

# final
dotnet build AvaloniaCadEditor.slnx
dotnet test AvaloniaCadEditor.Tests
```

(Rodar de `c:\Users\vinic\source\repos\AvaloniaCadEditor`, em Windows x64 — a suíte alvo
`net10.0-windows` por causa do runtime nativo do OCCT.)

**No app:**
1. Digitar numa cena o exemplo de furo escareado de §4 e confirmar que o chanfro renderiza com
   rampa reta e contínua, não escalonada.
2. Pôr um cone e um cilindro de mesmo raio lado a lado — devem ter a mesma contagem de facetas (é o
   que §2.5 compra).
3. Pôr um cone dentro de uma peça em `Pecas/` e `Add(..., at: Placement.At(50, 0, 0, rotationZDegrees: 45))`,
   depois fazer booleana contra outra coisa — exercita `BuildInstanceShape` → rebuild girado →
   `BuildCone(transform)` no app real.
4. Clicar num cone na viewport: não deve quebrar (não será selecionável por face — fora do escopo).
5. Confirmar `### ConeFeature : FeatureBase` no `.cad/api.md` gerado após recarregar a cena.

---

## 8. Riscos

1. **O revolve que encosta no eixo é a única suposição não provada** deste plano; todo o resto é
   transcrição de código já rodando. Portão no passo 4, contingência em §2.6.
2. **O teste-guarda do fingerprint não descobre tipos sozinho** (§3). Esquecer a linha de
   `[InlineData]` faz o pior bug de cache possível passar verde.
3. **O `transform` tem de mover o eixo, não só o perfil** (§2.3). O modo de falha é um sólido
   plausível na orientação errada — sem exceção, sem malha vazia.
4. **Aresta de comprimento zero** quando `topRadius` ou `bottomRadius` é 0 (§2.2).
5. **`GetFaceTypeFromNormal` pode reportar `Top` para a lateral** de um cone raso (§1).
6. **Fillet genérico num cone são ~20 booleanas sequenciais** (§2.4) — lento e facetado, não quebra.
7. **`FeatureToCSharpConverter.ProcessFeature:73`** devolve a string literal `"null"` para qualquer
   feature não tratada, sem `default:` e sem teste. Fora do escopo (nada no caminho do cone passa
   por ali), mas é um bug latente real que o cone agora pode alcançar — vale uma linha na mensagem
   de commit.
8. **`AngularDeflection` 0,5 rad ≈ 28,6° vs `CreaseAngle` 30°**: superfícies curvas podem ganhar
   arestas de vinco espúrias. O piso de §2.5 reduz isso (2π/32 ≈ 11,25°), mas é característica
   pré-existente de qualquer superfície curva do OCCT aqui, não regressão desta mudança.
