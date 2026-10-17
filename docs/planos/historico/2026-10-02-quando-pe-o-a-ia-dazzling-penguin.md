# Ver e clicar nos sólidos-ferramenta (vazios) de uma peça

## Context
Hoje o duplo-clique só abre o modo "grupo booleano" para uma `BooleanFeature` solta. Num `PartInstance`, como o `BlocoFixacaoBelt`, ele não faz nada: os sólidos que geram os vazios só existem em `instance.Definition`, em coordenadas locais, fora de `Sketch.Features`.

O que se quer:
1. duplo-clique na peça mostra os sólidos-ferramenta em vermelho, no lugar certo;
2. clicar num sólido vermelho mostra o **nome** dele e **realça a declaração no código** (`var furo_vertical = ...` em `Pecas/BlocoFixacaoBelt.cs`).

Fica de fora: editar valores, renomear, árvore e propriedades no inspetor.

## Abordagem

**1. Grupo ativo pode ser uma peça** ([CadViewModel.cs:845-864](AvaloniaCadEditor.UI/ViewModels/CadViewModel.cs#L845-L864))
- O `_activeBooleanGroupName` continua guardando um nome, mas agora pode ser o nome de uma booleana solta **ou de uma instância de peça**.
- Nova propriedade `IReadOnlyList<IFeature> ActiveToolFeatures`:
  - **Booleana solta:** `ToolFeatures`, como hoje.
  - **`PartInstance`:** desce a `Definition` recursivamente e junta as `ToolFeatures` de todas as booleanas encontradas. Entra pelas bases das booleanas e pelos `Dependencies` de fillet e transformed. Cada ferramenta é envolvida em `new TransformedFeature(tool, instance.Placement, tool.Name)` ([TransformedFeature.cs](AvaloniaCadEditor.Domain/Features/TransformedFeature.cs), que o kernel já monta, girado ou não).
  - Os envoltórios ficam em cache enquanto a mesma instância (mesma referência) estiver na cena. Isso mantém os Ids estáveis para o cache de malha e para o hover. Quando a cena re-roda, a instância nova reconstrói o cache.
  - Guardar também um mapa `Id do envoltório → (PartInstance dona, ferramenta local)`.
- `ActiveBooleanGroup` continua existindo para quem já o usa.
- Esc ([CadViewModel.cs:1287](AvaloniaCadEditor.UI/ViewModels/CadViewModel.cs#L1287)) e `Raycast3D`/`CycleHoveredFeature` ([:878](AvaloniaCadEditor.UI/ViewModels/CadViewModel.cs#L878), [:899](AvaloniaCadEditor.UI/ViewModels/CadViewModel.cs#L899)) passam a usar `IsEditingBooleanGroup` e `ActiveToolFeatures`.
- `GetFeatureMesh(id)` ([:838](AvaloniaCadEditor.UI/ViewModels/CadViewModel.cs#L838)): se o id for de um envoltório ativo, chama `MeshManager.GetMeshFor(envoltório)`.

**2. Malha e raycast de features fora do Sketch** ([ISceneMeshManager.cs](AvaloniaCadEditor.Application/Services/ISceneMeshManager.cs), [SceneMeshManager.cs](AvaloniaCadEditor.Application/Services/SceneMeshManager.cs))
- `GetMeshFor(IFeature feature)`: monta com `_geometryKernel.BuildSolidMesh` e guarda no `_featureMeshCache` pelo Id.
- `Raycast3D` e `RaycastAll3D` ganham o parâmetro opcional `IEnumerable<IFeature>? extraFeatures`, testado depois de `sketch.Features` com o mesmo código. Extrair o laço atual num helper local para não duplicar.
- `BuildCompositeMesh` ([:184](AvaloniaCadEditor.Application/Services/SceneMeshManager.cs#L184)) remove do cache Ids que não estão no Sketch. Isso só significa remontar a malha do envoltório se ele for despejado, então não quebra nada.

**3. Duplo-clique** ([ViewportInputHandler.cs:81-103](AvaloniaCadEditor.UI/Controls/Input/ViewportInputHandler.cs#L81-L103))
- `feature is BooleanFeature or PartInstance` (que tenha pelo menos uma ferramenta) entra no grupo pelo nome.
- A busca do alvo também olha `ActiveToolFeatures`.

**4. Render** ([SolidFeatureRenderPass.cs:94-100](AvaloniaCadEditor.UI/Controls/Rendering/SolidFeatureRenderPass.cs#L94-L100))
- Iterar `vm.ActiveToolFeatures`. O hover já funciona, porque compara `vm.HoveredFeatureId == feature.Id`.
- A ferramenta clicada ganha um realce: guardar `HighlightedToolId` no VM e desenhá-la com o vermelho mais forte/opaco que já é usado na seleção.

**5. Clique simples num sólido vermelho** ([ViewportInputHandler.cs:~195](AvaloniaCadEditor.UI/Controls/Input/ViewportInputHandler.cs#L195))
- Antes de `SelectById`, se o id atingido estiver em `ActiveToolFeatures`, chamar `viewModel.RevealToolFeature(feature)` e retornar.
- Isso evita `SelectById`, que hoje limparia a seleção porque o envoltório não está no Sketch.
- `CadViewModel.RevealToolFeature`:
  - define `HighlightedToolId`;
  - escreve no `StatusText`: `"furo_vertical (CylinderFeature) — BlocoFixacaoBelt"`;
  - **peça:** chama `ScriptEditor.RevealPartTool(instance.PartTypeName, tool.Name)`;
  - **booleana solta:** a ferramenta está no Sketch; chama `ScriptEditor.RevealFeature(tool)`, que também passa a rolar até `origin.Line` para features soltas (hoje só faz isso para `PartInstance`, em [SceneScriptViewModel.cs:430](AvaloniaCadEditor.UI/ViewModels/SceneScriptViewModel.cs#L430)).

**6. Localizar a declaração no código da peça**
- Novo `PartSourceEditor.FindToolDeclaration(IEnumerable<ScriptDocument> docs, string typeName, string toolName) → CodeLocation?`, em [PartSourceEditor.cs](AvaloniaCadEditor.Application/Scenes/PartSourceEditor.cs), com Roslyn.
  - Acha o `record typeName` (mesma busca de `FindPartTypes`, [:913](AvaloniaCadEditor.Application/Scenes/PartSourceEditor.cs#L913)).
  - Dentro dele, procura primeiro um `VariableDeclarator` com identificador `== toolName`. É o padrão novo `var furo_vertical = ...`.
  - Se não achar, procura um literal string `"toolName"`, que cobre `Name = "..."` e `name: "..."`.
  - Devolve um `CodeLocation` ([ISceneCodeNavigator.cs:12](AvaloniaCadEditor.Application/Interfaces/ISceneCodeNavigator.cs#L12)) com linha, coluna e tamanho do identificador.
- `SceneScriptViewModel.RevealPartTool(typeName, toolName)`:
  - chama o localizador e usa o `NavigateTo(CodeLocation)` que já existe ([SceneScriptViewModel.cs:1092](AvaloniaCadEditor.UI/ViewModels/SceneScriptViewModel.cs#L1092)). Ele troca o arquivo, abre a aba de código e seleciona o trecho;
  - se não achar (nome interpolado, por exemplo), cai em `GoToPartDefinition(typeName)` e avisa no status.

## Arquivos
- `AvaloniaCadEditor.Application/Services/ISceneMeshManager.cs`, `SceneMeshManager.cs`: já têm alterações suas não commitadas. Edito por cima, sem desfazer nada.
- `AvaloniaCadEditor.Application/Scenes/PartSourceEditor.cs`
- `AvaloniaCadEditor.UI/ViewModels/CadViewModel.cs`: também já tem alterações suas.
- `AvaloniaCadEditor.UI/ViewModels/SceneScriptViewModel.cs`
- `AvaloniaCadEditor.UI/Controls/Input/ViewportInputHandler.cs`: também já tem alterações suas.
- `AvaloniaCadEditor.UI/Controls/Rendering/SolidFeatureRenderPass.cs`
- Testes em `AvaloniaCadEditor.Tests`.

## Verificação
- **Testes unitários:**
  - `FindToolDeclaration` acha `var furo_vertical` no `BlocoFixacaoBelt`;
  - acha o literal quando não há variável com o nome;
  - devolve null para nome inexistente;
  - `SceneMeshManager.Raycast3D` com `extraFeatures` acerta um `TransformedFeature`.
- `dotnet test`. Com o app aberto, usar `--artifacts-path` no scratchpad, como antes.
- **Manual,** na cena `AiCore`:
  - duplo-clique no `bloco_fixacao_belt`: aparecem em vermelho `vao_correia`, os furos e o prisma da porca, no lugar certo. Repetir no `carrinho_mgn9c_x`, que está girado -90°;
  - passar o mouse realça o sólido; clicar mostra o nome no status e seleciona `furo_vertical` em `Pecas/BlocoFixacaoBelt.cs`;
  - Esc sai do modo;
  - uma booleana solta continua funcionando como antes e agora também rola o código até a ferramenta.
