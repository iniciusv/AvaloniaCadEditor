# Face = plano: bevel só na face selecionada, e osnap no plano da face

## Context

Num chanfro aplicado a uma face do `bloco_ficacao_belt_vao_correia`, saíram chanfradas arestas que não fazem parte da face selecionada.

**Hoje a face não é identificada.** O que fica gravado é só a direção: `new FaceReference(base, -1, new Vector3D(0, 0, 1), "CustomFace")`. O caminho por malha do [OcctFilletBuilder.cs:91-126](AvaloniaCadEditor.Infrastructure/OpenCascade/OcctFilletBuilder.cs#L91-L126) chanfra **toda** aresta convexa do sólido final que encoste numa face com essa normal. Nesta peça entraram, além do topo (z=17), o fundo do rebaixo e o do vão (z=9), inclusive o anel do furo de 3,2 mm.

**Estratégia combinada com o usuário:** a face selecionada vira um **plano de trabalho** (normal + ponto da face).
- **Bevel:** é aplicado uma vez, no **sólido resultante** de todas as booleanas, só nas arestas convexas que ficam sobre esse plano. Não é aplicado em cada operando, porque isso abriria um sulco nas emendas coplanares (topo do `corpo` + topo do extra).
- **Escopo do plano:** o plano pega todas as regiões coplanares, ilhas separadas incluídas. É o mesmo critério do realce de seleção ([FaceProfile.cs:50-74](AvaloniaCadEditor.Domain/Profiles/FaceProfile.cs#L50-L74)), então o chanfro sai onde o realce mostra.
- **Osnap:** o mesmo critério serve para desenhar sobre a face. Em modo de esboço, o snap em sólidos considera só os vértices e pontos médios das arestas que ficam no plano de trabalho ativo. Hoje ([OSnapService.cs:54-70](AvaloniaCadEditor.Application/Services/OSnapService.cs#L54-L70)) ele testa todos os vértices da malha em 3D, com tolerância fixa de 5, e um vértice fora do plano pode roubar o snap.

## Mudanças

### 1. Base comum: "ponto está no plano"
- [WorkPlane.cs](AvaloniaCadEditor.Domain/ValueObjects/WorkPlane.cs): `public bool Contains(Point3D point, double tolerance = 1e-3) => Math.Abs(ToLocalSpace(point).Z) < tolerance;`. A tolerância é a mesma do `FaceProfile`.
- [FaceReference.cs](AvaloniaCadEditor.Domain/ValueObjects/FaceReference.cs):
  - nova propriedade `Point3D? PointOnFace`;
  - novo construtor `FaceReference(IFeature parent, int faceId, Vector3D normalHint, Point3D pointOnFace, string name)`. O construtor antigo continua igual, com o comportamento antigo (só normal);
  - `GetWorkPlane()`, no fallback: `new WorkPlane(NormalHint, PointOnFace ?? origem)`. Com isso o [LinkedWorkPlane.cs](AvaloniaCadEditor.Domain/ValueObjects/LinkedWorkPlane.cs) deixa de cair num plano que passa pela origem em faces genéricas.

### 2. Bevel filtrado pelo plano, em [OcctFilletBuilder.cs](AvaloniaCadEditor.Infrastructure/OpenCascade/OcctFilletBuilder.cs)
- `targetNormals` vira a lista de faces alvo: `(Vector3D Normal, WorkPlane? Plane)`, com `Plane = PointOnFace != null ? face.GetWorkPlane() : null`.
- Uma aresta casa com uma face quando:
  - a normal bate (`dot > 0.99` em `n1` ou `n2`, como hoje);
  - **e**, se houver plano, `Plane.Contains(start) && Plane.Contains(end)`.
- As guardas atuais continuam: aresta côncava, emenda coplanar (`n1 ≈ n2`) e `TryCutKeepingSolid`.

### 3. O ponto da face viaja até o código
- [FilletTool.cs](AvaloniaCadEditor.Application/Commands/FilletTool.cs):
  - `PartFilletRequested` passa `IReadOnlyList<(Vector3D Normal, Point3D Point)> WorldFaces`, montada com `faceProfile.Normal` e `faceProfile.Center` (`Plane.Origin`, que fica no plano da face);
  - o código de cena (face de feature solta) também passa a gerar o construtor com `new Point3D(...)`.
- [CadViewModel.cs](AvaloniaCadEditor.UI/ViewModels/CadViewModel.cs) `FilletTool_PartFilletRequested`: o ponto passa por `toLocal.Apply`, a normal por `ApplyVector`, e os dois são arredondados (`Round`).
- [SceneScriptViewModel.cs](AvaloniaCadEditor.UI/ViewModels/SceneScriptViewModel.cs) `AddPartFilletAsync` e [PartBuildFilletEditor.cs](AvaloniaCadEditor.Application/Scenes/PartBuildFilletEditor.cs) `AddFillet`: recebem `(Normal, Point)` e geram `new FaceReference(base, -1, new Vector3D(...), new Point3D(...), "CustomFace")`.

### 4. Corrigir o chanfro já gravado em [BlocoFicacaoBeltVaoCorreia.cs](AiCore/Pecas/BlocoFicacaoBeltVaoCorreia.cs)
- Acrescentar `new Point3D(-10.365334, 17.75, 17)` ao `FaceReference` existente. É o centro do topo do `corpo`; só o plano z=17 importa. Esse é o topo na peça, que é a face de baixo clicada no mundo, por causa do `MirrorZ`.

### 5. Osnap no plano em modo de esboço
- [IOSnapService.cs](AvaloniaCadEditor.Application/Services/IOSnapService.cs) e [OSnapService.cs](AvaloniaCadEditor.Application/Services/OSnapService.cs): `GetSnappedPoint` ganha o parâmetro `bool solidSnapOnPlaneOnly = false`.
  - Com `true`: os pontos de snap do sólido passam a ser só as pontas (Endpoint) e os pontos médios (Midpoint, se ligado) das `FeatureEdges` cujas duas pontas ficam em `sketch.Plane.Contains(...)`.
  - Com `false`: comportamento atual (todos os vértices), que continua servindo Mover/Girar/Espelhar, que dependem do snap 3D entre sólidos.
- [CadViewModel.cs](AvaloniaCadEditor.UI/ViewModels/CadViewModel.cs) `OnPointerMoved` e `OnPointerPressed` (linhas ~1600 e ~1748): passam `solidSnapOnPlaneOnly: CurrentMode == EditorMode.SketchEdit`. As ferramentas de linha, círculo e polígono já tornam o plano da face selecionada o `ActiveWorkPlane` ([CadViewModel.cs:1002-1030](AvaloniaCadEditor.UI/ViewModels/CadViewModel.cs#L1002-L1030)).
- Os mocks com `Setup(s => s.GetSnappedPoint(...4 args))` precisam do 5º `It.IsAny<bool>()`, porque árvore de expressão não aceita omitir argumento opcional. Exemplos: `MoverInstanciaPeloViewportTests`, `ModelTreeAndPropertyInspectorTests`, `BuildProblemStatusTests`, `BooleanBehaviorTests`.

### 6. Documentação da API
- `AiCore/.cad/api.md` e `TestScene/.cad/api.md`: acrescentar o construtor novo de `FaceReference` no mesmo formato, ou regenerar pelo `CadScriptApiReference`.

## Testes
- **`FilletDegenerateCutTests`:**
  - **com ponto em z=17:** nenhum triângulo inclinado perto de z=9 (o rebaixo e o vão ficam intactos), e o topo chanfrado (mais triângulos que sem chanfro);
  - **sem ponto:** o caso atual continua passando, com o comportamento antigo.
- **Caixa com degrau:** com o ponto no topo, só o topo é chanfrado e o piso do degrau, que tem a mesma normal, fica intacto. Sem o ponto, os dois são chanfrados.
- **`WorkPlane.Contains` e `FaceReference.GetWorkPlane()`:** com ponto, a origem fica no plano da face.
- **`PartFilletEditingTests`:**
  - o código gerado tem `new Point3D(...)`;
  - o `FilletTool` entrega o ponto no evento;
  - com `MirrorZ`, o ponto de mundo `(x, y, -17)` vira `(x, y, 17)` local.
- **`OSnapBehaviorTests`:** um sólido com duas faces paralelas a 2 mm de distância e um cursor no plano de cima, perto de um vértice da face de baixo. O que conferir:
  - com `solidSnapOnPlaneOnly`, o snap pega o vértice do plano e nunca o de fora;
  - o ponto médio de uma aresta do plano também é aceito;
  - sem a flag, o comportamento antigo é mantido.

## Verificação
1. `dotnet test --artifacts-path <scratchpad>`: só as falhas já conhecidas (CheckCommand ×3, ConexaoY ×2, Drafting BlocoFixacaoBelt, e a de descarregamento de assemblies, instável sob carga).
2. No app, rodando a cena de novo:
   - o `bloco_ficacao_belt_vao_correia` mostra o chanfro só na face clicada, sem chanfro no fundo do rebaixo nem no vão. O tempo deve cair, porque o anel de z=9 deixa de ser cortado;
   - selecionar essa face e abrir a ferramenta Linha: o snap gruda nos cantos e nos pontos médios das bordas da face (incluindo as bocas de furo nela) e ignora vértices de outras alturas.
