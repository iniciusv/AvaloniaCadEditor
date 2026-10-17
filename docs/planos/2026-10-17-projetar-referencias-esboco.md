# Projetar face / volume como referência do esboço (+ desligar chanfros/fillets)

**Status:** Em andamento · **Data:** 2026-10-17 · **Branch:** feature/varredura-por-caminho


## Contexto
No modo de esboço, hoje só vira referência o que **está no plano**: o contorno das faces coplanares e o corte
dos sólidos que atravessam o plano. Isso é feito por `PlaneSection.Compute`, com cache em
`CadViewModel.BuildPlaneReferences`. Para desenhar alinhado a algo **fora** do plano (um furo atrás, a lateral
de outra peça) não há referência nenhuma.

O pedido: com o botão direito sobre uma face ou um sólido, um submenu "Projetar" põe no plano ativo a projeção
ortogonal daquilo, como referência que é desenhada e tem snap.

Decisões do usuário:
- **Face**: a projeção das arestas da face.
- **Volume**: por padrão o **contorno** (sombra mais furos passantes). Um **botão** alterna para **todas as
  arestas** (vivas e silhuetas, inclusive as ocultas).
- **Duração**: ficam no plano de trabalho **durante a sessão**.
  - Saem e voltam com o esboço.
  - Acompanham o sólido quando ele muda.
  - Não vão para o CadScript.
- **Interação**: clique direito na viewport no esboço → **Projetar ▸ Face / Volume**.
- No mesmo menu, **desligar temporariamente chanfros e fillets**: eles arredondam as quinas, e aí não sobra
  ponta do sólido base para marcar.
- **Organização**: DDD e clean code. Cada camada com uma responsabilidade, classes pequenas e coesas, o
  `CadViewModel` (já enorme) só delega e nada de regra de negócio na UI.

Toda a geometria é feita na malha, como o `PlaneSection`. O wrapper OCCT não expõe HLR nem projeção.

## Organização por camada

| Camada | Novo / alterado | Responsabilidade |
|---|---|---|
| **Domain** `ValueObjects/` | `ProjectionSource` (record), `ProjectionTarget` (enum `Face`, `Body`), `VolumeProjectionMode` (enum `Outline`, `AllEdges`), `PlaneReferenceKind.Projected`, `RaycastHit.TriangleIndex` | Conceitos imutáveis, sem dependências |
| **Domain** `Mesh/` | `ReferencePolylines` (extraído de `PlaneSection`), `MeshFaces`, `MeshEdgeGraph`, `PlaneProjection` | Algoritmos puros de malha. **Nenhum pacote externo** |
| **Domain** `Mesh/` (porta) | `IPolygonUnion` | Abstração da união de polígonos 2D que o contorno precisa |
| **Application** `Services/` | `ISketchProjectionService` / `SketchProjectionService` | Caso de uso: guarda as fontes por plano, monta as referências com cache, poda features apagadas |
| **Application** `Services/` | `ISceneMeshManager.SuppressFillets`, `IGeometryKernel.SuppressFillets` | Supressão temporária de fillets, como estado da sessão de geometria |
| **Infrastructure** | `Geometry/ClipperPolygonUnion` (NuGet `Clipper2`), ajustes no `OpenCascadeGeometryKernel` | Detalhes técnicos: a biblioteca de polígonos e as chaves de cache do kernel |
| **UI** | `CadViewModel` (só delegação), `SketchContextMenuBuilder`, `PlaneReferenceRenderPass`, `MainWindow.axaml`, `Strings` | Montar o menu, desenhar, ligar os comandos. Sem cálculo |

Regras seguidas em todas as camadas:
- Dependência só por interface. O serviço novo entra por injeção no `CadViewModel`, como `_osnapService`.
- O registro no contêiner de DI fica no mesmo lugar onde o `OSnapService` é registrado.
- Métodos curtos, nomes de domínio, nenhum número mágico solto: as tolerâncias viram `const` com comentário,
  como em `PlaneSection`.
- Nenhuma duplicação. A solda de vértices, o encadeamento e a detecção de círculo existem num lugar só.

## 1. Domain

### 1a. `ReferencePolylines` (extração, sem mudar comportamento)
- Tirar de [PlaneSection.cs](AvaloniaCadEditor.Domain/Mesh/PlaneSection.cs) estas funções:
  - `Chain`
  - `RemoveCollinear`
  - `IsCollinear`
  - `TryFitCircle`
  - `AddPolylines`
- Elas vão para `internal static class ReferencePolylines`, com a entrada
  `Build(PlaneReferenceKind kind, IEnumerable<(Point3D A, Point3D B)> segments)`, e uma segunda entrada
  `FromLoops(kind, loops)` para os laços que já vêm fechados do contorno.
- `PlaneSection` passa a só gerar segmentos. Os 8 testes de `PlaneSectionTests` têm de continuar passando.

### 1b. `MeshEdgeGraph`: a solda e a adjacência num lugar só
- Hoje a solda está duplicada em `EdgeDetector`, `OcctTessellator.FixTriangleWinding` e `PlaneSection.Chain`.
- A classe nova faz, para uma malha:
  - solda os vértices por posição (escala 1e4);
  - monta o mapa aresta → triângulos;
  - guarda a normal de cada triângulo.
- Serve a `MeshFaces` e a `PlaneProjection`. Os três usos antigos **não** são migrados agora; fica anotado como
  dívida, para não ampliar o escopo.

### 1c. `MeshFaces.Region(MeshEdgeGraph graph, int seedTriangle)`
- Faz uma busca em largura a partir do triângulo atingido e só atravessa aresta **suave** (ângulo menor que o
  crease de 30°, o mesmo critério das `InternalEdges`).
- Assim a face é a região limitada pelas linhas que o wireframe desenha.
- Limitação aceita: um fillet tangente se funde com a face vizinha, igual ao que aparece na tela.
- `FindSeed(graph, Point3D point, Vector3D normal)`: o triângulo mais próximo do ponto com `dot > 0.9`. É o que
  reencontra a face depois que a malha é retesselada.

### 1d. `PlaneProjection`
Classe estática; trabalha em coordenadas locais do plano e devolve `List<PlaneReference>` do tipo `Projected`.
- **`Edges(graph, IReadOnlySet<int>? region, WorkPlane plane)`**: serve à face e ao volume em todas as arestas.
  - Projeta as **arestas de contorno**: as vivas, mais as que têm um único triângulo dentro da região.
  - Projeta as **silhuetas**: arestas suaves com `sign(n1·N) ≠ sign(n2·N)`.
  - Tudo sai por `ReferencePolylines.Build`.
  - Segmentos que viram um ponto ou que se repetem somem no encadeamento.
- **`Outline(mesh, plane, IPolygonUnion union)`**: volume, modo contorno.
  - Projeta os triângulos com `n·N > ε` e faz a união deles pela porta.
  - Os laços resultantes saem por `ReferencePolylines.FromLoops`.
- Porta `IPolygonUnion`:
  `IReadOnlyList<IReadOnlyList<Point2D>> Union(IEnumerable<IReadOnlyList<Point2D>> polygons)`. Usar
  `Point3D` com Z = 0 se não houver `Point2D` no Domain.

### 1e. Value objects
- `ProjectionSource(Guid FeatureId, ProjectionTarget Target, Point3D Seed, Vector3D SeedNormal)`.
  - Tem `Matches(other)` para o menu saber se aquela face ou volume já está projetado.
  - Duas fontes `Body` são iguais quando têm a mesma feature.
  - Duas fontes `Face` são iguais quando caem na mesma região.
- `RaycastHit` ganha `int TriangleIndex = -1`, preenchido em `Ray.IntersectMesh` (Ray.cs:106) e repassado em
  `SceneMeshManager.cs:156`.

## 2. Application: `SketchProjectionService`
- Interface `ISketchProjectionService` em `Application/Services/`:
  - `Add(Guid planeId, ProjectionSource)`
  - `Remove(Guid planeId, ProjectionSource)`
  - `Clear(Guid planeId)`
  - `Contains(Guid planeId, ProjectionSource)`
  - `bool HasAny(Guid planeId)`
  - `VolumeProjectionMode VolumeMode { get; set; }`
  - `IReadOnlyList<PlaneReference> ReferencesFor(Sketch sketch, WorkPlaneFeature plane)`
- `planeId` é o Id do `WorkPlaneFeature`. Quem resolve o plano ativo continua sendo o `CadViewModel`.
- A malha vem de `ISceneMeshManager.GetFeatureMesh` (injetado).
- O cache é por fonte: `(mesh instance, plane, mode) → referências`.
- Uma fonte cuja feature sumiu é podada. Uma feature **oculta** continua projetando: esconder o sólido para
  desenhar sobre a projeção é um uso natural.
- `IPolygonUnion` chega por injeção. A implementação `ClipperPolygonUnion` fica em Infrastructure e usa
  `Clipper.Union(PathsD, FillRule.NonZero, precision: 4)`.

## 3. Supressão temporária de chanfros/fillets
Fillet e chanfro são o mesmo `FilletFeature` (o `Mode` separa), que esconde o base (`IsVisible = false`). Por
isso `IsEnabled = false` não serve: o sólido sumiria. A troca é no kernel: com a supressão, **o fillet devolve
o sólido base**, inclusive dentro de booleanas e de `Part.Build()`.
- `IGeometryKernel.SuppressFillets { get; set; }`, com implementação padrão vazia, o que mantém os mocks como
  estão.
- Em [OpenCascadeGeometryKernel.cs](AvaloniaCadEditor.Infrastructure/OpenCascade/OpenCascadeGeometryKernel.cs):
  - O ramo do fillet (linha 228) é extraído para `BuildFillet(...)`.
  - Com a supressão, ele devolve `BuildShape(fillet.BaseFeature, settings, transform)` **sem gravar** no
    `_shapeCache`. Isso evita dar Dispose duas vezes no mesmo `OcctShape`.
  - O flag entra em `GeometryKey`, e `_shapeCache` e `_definitionMeshCache` passam a ter chave `(Guid, bool)`.
  - Ganho: **religar não refaz** os fillets (`ApplyToAllEdges` custa uma booleana por aresta, conforme a
    memória [[chanfro-antes-dos-furos]]).
- `ISceneMeshManager.SuppressFillets`: repassa ao kernel e troca entre dois `_featureMeshCache` (normal e
  suprimido). As invalidações limpam os dois.
- `CadViewModel`:
  - `ToggleSuppressFillets()` troca o flag, chama `RefreshSolidMesh(skipCodeGeneration: true)` e mostra o
    status.
  - `IsFilletSuppressed` é lido pelo item do menu.
  - `ExitSketchMode` **religa sozinho**.

## 4. UI (fina)
- **CadViewModel**: só delegação. `PlaneReferences` passa a ser "interseção + `_projections.ReferencesFor(...)`"
  e o snap não muda.
  - Fica `ProjectedPlaneReferences`, sempre desenhada.
  - Ficam os comandos, de uma linha cada:
    - `ProjectFace(hit)`
    - `ProjectBody(hit)`
    - `RemoveProjection(hit, target)`
    - `ClearProjections()`
    - `ToggleVolumeProjectionMode()` / `IsVolumeProjectionAllEdges`
    - `ToggleSuppressFillets()` / `IsFilletSuppressed`
  - Mais `RaycastForProjection(ray)`, que ignora `WorkPlaneFeature`.
  - Cada comando termina em `InvalidatePlaneReferences()`, um helper novo que hoje equivale a
    `_planeReferences = null`.
- **Menu**:
  - Em [ViewportInputHandler.cs](AvaloniaCadEditor.UI/Controls/Input/ViewportInputHandler.cs),
    `ContextMenuRequested` passa a ser `Action<Point>` (linha 484).
  - Uma classe nova, `UI/Controls/Input/SketchContextMenuBuilder.cs`, monta os itens a partir de
    `(CadViewModel, RaycastHit?)`:
    - **Projetar ▸ Face / Volume**, quando há hit;
    - **Remover projeção**, quando já está projetado;
    - **Limpar projeções do plano**;
    - um separador e **Desligar chanfros/fillets (temporário)**, como `MenuItem` marcável.
  - O `OpenContextMenu(Point)` de [SilkViewportControl.cs](AvaloniaCadEditor.UI/Controls/SilkViewportControl.cs)
    só escolhe entre o menu do esboço e o de espelhar, que é extraído para um método próprio. Se não houver
    nada a mostrar, o menu não abre.
- **Botão** em [MainWindow.axaml](AvaloniaCadEditor.UI/Views/MainWindow.axaml), perto de `PolygonTool`
  (linha 157): `ToggleButton` "Projeção: todas as arestas".
- **Desenho**: em [PlaneReferenceRenderPass.cs](AvaloniaCadEditor.UI/Controls/Rendering/PlaneReferenceRenderPass.cs),
  desenhar também `ProjectedPlaneReferences` na cor âmbar `(0.95, 0.65, 0.2)`, que vira uma constante nomeada
  ao lado das existentes.
- **Textos** em `Resources/Strings.resx` e `Strings.cs`:
  - `ProjectMenu`
  - `ProjectFace`
  - `ProjectBody`
  - `RemoveProjection`
  - `ClearProjections`
  - `ProjectAllEdges`
  - `SuppressFilletsTemporarily`
  - `FilletsSuppressedStatus`

## 5. Testes (`AvaloniaCadEditor.Tests`)
- Os helpers `Box`, `Cylinder` e `Prism` de `PlaneSectionTests` passam para uma classe comum
  `TestMeshes`, reutilizada pelas classes novas.
- `PlaneProjectionTests`:
  - O topo da caixa acima do plano XY dá um retângulo de 4 pontos.
  - A face lateral perpendicular dá um segmento.
  - A lateral de um cilindro de eixo Z dá um círculo com centro e raio.
  - A lateral de um cilindro de eixo X dá um laço retangular.
  - A caixa projetada num plano isométrico, em todas as arestas, dá o hexágono e as 3 arestas internas.
  - O contorno da placa com furo passante dá o laço externo e um círculo. Este caso usa um `IPolygonUnion`
    falso ou o `ClipperPolygonUnion` real.
- `MeshFacesTests`:
  - A região do topo da caixa tem 2 triângulos.
  - A região da lateral do cilindro tem a lateral inteira, sem as tampas.
  - `FindSeed` reencontra a face numa malha retesselada.
- `SketchProjectionServiceTests`, com `ISceneMeshManager` falso:
  - A projeção sobrevive à troca da malha.
  - É podada quando a feature some.
  - Planos diferentes ficam isolados.
  - `VolumeMode` troca o resultado.
  - `Contains` funciona para face e para volume.
- `FilletSuppressionTests`, com o OCCT real, no estilo de `FilletFeatureTests`:
  - Caixa chanfrada suprimida tem os 8 cantos.
  - Religar devolve a mesma instância de malha, com o mesmo `MeshBuildCount`.
  - Fillet dentro de `BooleanFeature` também é suprimido.
  - `ExitSketchMode` religa.

## Verificação
1. `dotnet build` e `dotnet test`. Com o app aberto, usar `--artifacts-path` no scratchpad, conforme a memória
   [[testes-com-app-aberto]]. Os 3 CheckCommand e as falhas de cena já conhecidas no HEAD não contam.
2. No app, com o skill `run`:
   - Esboço num plano acima de uma peça. Clique direito numa face lateral ▸ Projetar ▸ Face: aparece a linha
     âmbar, e a Linha agarra as pontas e o meio dela.
   - Projetar ▸ Volume: aparece o contorno. O botão troca para todas as arestas.
   - Alterar a dimensão do sólido no inspetor: a projeção acompanha.
   - Sair e voltar ao esboço: a projeção continua. Trocar de plano: ela some. Voltar: ela reaparece.
   - Remover projeção e Limpar projeções funcionam.
   - Peça com chanfro ▸ Desligar chanfros/fillets: aparecem os cantos vivos e a Linha agarra a ponta.
     Desmarcar religa na hora, sem travar. Sair do esboço também religa.
3. Ao executar, gravar duas coisas:
   - este plano em `docs/planos/2026-10-17-projetar-referencias-esboco.md`, com entrada no índice
     `docs/README.md`, conforme [[planos-em-docs-planos]];
   - uma memória de feedback: o usuário quer DDD e clean code, camadas separadas e o ViewModel só delegando.

## Fora do escopo
- Gravar as projeções no CadScript.
- Converter uma projeção em entidade real do esboço.
- Snap de ponto mais próximo na curva e de interseção.
- Destacar a face no hover com o menu aberto.
- Migrar `EdgeDetector`, `FixTriangleWinding` e `PlaneSection.Chain` para `MeshEdgeGraph`.
