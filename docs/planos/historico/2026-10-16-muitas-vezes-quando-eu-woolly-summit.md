# Referências automáticas no plano de trabalho (faces coplanares + corte dos sólidos)

## Contexto
Ao desenhar num plano criado a partir de uma face, hoje o único apoio é o snap em pontas/pontos médios de
`FeatureEdges` com as duas pontas no plano (`OSnapService.SolidSnapPointsOnPlane`). Nada é desenhado, sólidos
que **cruzam** o plano não contribuem, e cada segmento da tesselação de um furo vira um ponto médio espúrio.

Objetivo: no modo esboço, o plano ativo ganha automaticamente **geometria de referência** transitória:
1. **Coplanar** — contornos das faces (do sólido clicado e de qualquer outro) que estão no plano;
2. **Corte** — a seção de cada sólido que atravessa o plano.

Decisões já tomadas com o usuário:
- Só sólidos **visíveis** (respeita H/isolamento e o olho da árvore).
- Referências desenhadas **sempre** no esboço, como linhas tênues, e usadas pelo snap.
- **Projeção** de sólidos atrás/à frente do plano fica para depois.

Restrição técnica: o wrapper OCCT não expõe seção nem HLR. Por isso tudo é feito sobre a malha
(`TriangleMesh`, já em coordenadas do mundo), por feature visível.

## Abordagem

### 1. Domínio: `PlaneSection` (novo, `AvaloniaCadEditor.Domain/Mesh/PlaneSection.cs`)
Classe estática pura. Fica em Domain porque Application não enxerga Infrastructure, onde mora `MeshSlicer`.
Assinatura: `Compute(TriangleMesh mesh, WorkPlane plane, double tol = 1e-3) → List<PlaneReference>`.

**`PlaneReference`** (record em Domain/ValueObjects):
- `Kind`: `Coplanar` ou `Section`;
- `Points`: em coordenadas locais do plano, Z = 0;
- `Closed`;
- `Center?` e `Radius?`, preenchidos quando o laço é circular.

**Passos:**
- **Coplanar:** pares de `FeatureEdges` com as duas pontas a menos de `tol` do plano (o mesmo critério de
  `WorkPlane.Contains` usado hoje).
- **Corte:** para cada triângulo, classificar os vértices por distância com sinal (`+`, `−` ou `0` quando |d| < tol).
  - Só gera segmento o triângulo que tem pelo menos um vértice estritamente `+` **e** um estritamente `−`.
  - Um vértice `0` desse triângulo entra como ponto de cruzamento.
  - Diferença de propósito em relação ao `MeshSlicer.Slice`, que empurra o vértice no plano por um épsilon: aqui,
    sólidos que só *encostam* no plano não geram corte duplicado. A face deles já sai como Coplanar.
- **Encadeamento:** juntar os segmentos em polilinhas abertas ou fechadas, separadas por tipo. Pontas soldadas por
  posição arredondada (dicionário, O(n)), e não por índice, porque a malha não é soldada.
  - Depois, remover vértices colineares, como em `MeshSlicer.RemoveCollinearPoints`: a diagonal da tesselação e
    os T-junctions somem, e o ponto médio volta a ser o da aresta real.
- **Detecção de círculo:** laço fechado com pelo menos 8 vértices, todos à mesma distância do centróide
  (desvio ≤ 1 %), recebe `Center` e `Radius`. Assim a boca de furo e o corte de um cilindro passam a ter snap no centro.

### 2. ViewModel: cache das referências (`AvaloniaCadEditor.UI/ViewModels/CadViewModel.cs`)
- Nova propriedade preguiçosa `PlaneReferences`, calculada só em `CurrentMode == SketchEdit`.
- Itera `Sketch.Features.Where(f => f.IsVisible && f is not WorkPlaneFeature)`, usa
  `MeshManager.GetFeatureMesh(Sketch, f.Id, null)` e chama `PlaneSection.Compute(mesh, ActiveWorkPlane)`.
  - É por feature, para que sólidos sobrepostos não misturem laços.
  - Ficam de fora o preview da extrusão e os sólidos ocultos.
- Cache por feature, com chave (referência da malha, plano):
  - `RefreshSolidMesh()` (linha ~1469) só invalida; esse método já é chamado pelo setter de `ActiveWorkPlane`
    (1587), pelo H (via `RefreshProfiles`) e pelo olho da árvore.
  - O arrasto de extrusão chama `RefreshSolidMesh` muitas vezes, mas a malha das features que não mudaram
    continua a mesma instância, e o cache por feature evita recalcular.

### 3. Snap (`AvaloniaCadEditor.Application/Services/OSnapService.cs` e `IOSnapService.cs`)
- Novo parâmetro opcional `IReadOnlyList<PlaneReference>? planeReferences` em `GetSnappedPoint`.
- Com `solidSnapOnPlaneOnly`, o snap no sólido passa a vir das referências. Se o parâmetro vier nulo, elas são
  calculadas com `PlaneSection.Compute(solidMesh, sketch.Plane)`, o que mantém o teste atual válido.
  `SolidSnapPointsOnPlane` é substituído.
- Pontos oferecidos, respeitando os flags de `OsnapSettings`:
  - **polilinha comum:** vértices (Endpoint) e ponto médio de cada segmento já simplificado (Midpoint);
  - **laço circular:** centro (Center) e os 4 quadrantes (Endpoint), sem os vértices da tesselação.
- Chamadas em `CadViewModel.OnPointerPressed` e `OnPointerMoved` (linhas 1601 e 1749) passam `PlaneReferences`.

### 4. Desenho (`AvaloniaCadEditor.UI/Controls/Rendering/PlaneReferenceRenderPass.cs`, novo)
- Mesmo molde do `ToolGuideRenderPass`: VAO/VBO com posição e cor, depth test desligado (o corte fica dentro do
  sólido) e iluminação desligada.
- Só desenha em `SketchEdit`. Converte os pontos com `ActiveWorkPlane.ToWorldSpace`; círculo é polilinha fechada.
- Cores tênues: coplanar em ciano suave (~0.35, 0.75, 0.85) e corte em magenta suave (~0.85, 0.45, 0.75).
- Registrar em `SilkViewportControl.cs` (linhas 53–63) **antes** do `SketchEntityRenderPass`, para que as linhas do
  esboço fiquem por cima.

### Fora de escopo (próximos passos possíveis)
- Projeção de sólidos atrás/à frente do plano.
- Snap "mais próximo na aresta" e interseção linha do esboço × referência.
- Converter uma referência em linha real do esboço.
- Filtrar entidades de outros planos no snap (hoje `OSnapService` percorre todas).

## Arquivos
| Arquivo | Mudança |
|---|---|
| `AvaloniaCadEditor.Domain/Mesh/PlaneSection.cs` | novo: coplanar, corte, encadeamento, colineares, círculo |
| `AvaloniaCadEditor.Domain/ValueObjects/PlaneReference.cs` | novo: record e enum `PlaneReferenceKind` |
| `AvaloniaCadEditor.Application/Services/IOSnapService.cs`, `OSnapService.cs` | parâmetro `planeReferences`; snap a partir das referências |
| `AvaloniaCadEditor.UI/ViewModels/CadViewModel.cs` | `PlaneReferences` preguiçoso com cache; invalidação em `RefreshSolidMesh`; repassar ao snap |
| `AvaloniaCadEditor.UI/Controls/Rendering/PlaneReferenceRenderPass.cs` | novo pass |
| `AvaloniaCadEditor.UI/Controls/SilkViewportControl.cs` | registrar o pass |
| `AvaloniaCadEditor.Tests/...` | `PlaneSectionTests` (novo) e casos novos em `OSnapBehaviorTests` |

## Verificação
1. **Testes unitários** em `PlaneSectionTests`, com malhas montadas à mão como em
   `OSnapBehaviorTests.SolidoComFaceNoPlanoEArestaAbaixo`:
   - caixa com a face no plano → 1 laço Coplanar de 4 vértices e nenhum Section;
   - caixa atravessando o plano → 1 laço Section retangular, sem vértice colinear espúrio;
   - caixa só encostando por baixo → nenhum Section;
   - prisma de 32 lados atravessando → laço com `Center` e `Radius` corretos;
   - dois sólidos coplanares distintos → 2 laços Coplanar.
2. **`OSnapBehaviorTests`:** o teste existente continua passando. Casos novos: snap no centro de um furo, e snap
   no ponto médio de uma aresta de corte.
3. **Rodar** `dotnet test` com `--artifacts-path` no scratchpad, caso o app esteja aberto e trave as DLLs. Três
   falhas de CheckCommand e duas de ConexaoY já existem no HEAD.
4. **No app:**
   - clicar W numa face → entrar no esboço (L) → ver o contorno ciano da face e de outras faces coplanares;
   - ver em magenta o corte dos sólidos que atravessam o plano;
   - o cursor deve grudar nos cantos, nos pontos médios e no centro de furos;
   - apertar H isolando uma peça: as referências das outras somem.
