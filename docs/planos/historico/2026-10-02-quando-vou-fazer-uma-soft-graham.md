# TAB para alternar entre sólidos sobrepostos + realce correto em modo Face

## Status

A primeira parte (ciclo de sólidos sobrepostos com TAB) **já foi implementada** nesta sessão:
- `ISceneMeshManager.RaycastAll3D` / `SceneMeshManager.RaycastAll3D` ([SceneMeshManager.cs:139](AvaloniaCadEditor.Application/Services/SceneMeshManager.cs#L139)) — raycast multi-hit ordenado por distância.
- `CadViewModel.CycleHoveredFeature()` + `_pickCandidates`/`_pickOverride` + `Raycast3D` consumindo o override ([CadViewModel.cs:825](AvaloniaCadEditor.UI/ViewModels/CadViewModel.cs#L825)).
- `case "TAB"` no `OnKeyDown` ([CadViewModel.cs:1135](AvaloniaCadEditor.UI/ViewModels/CadViewModel.cs#L1135)).
- Hover 3D estendido para `SelectTool`/`SetWorkPlaneCommand` em `ViewportInputHandler.HandlePointerMoved` ([ViewportInputHandler.cs:441](AvaloniaCadEditor.UI/Controls/Input/ViewportInputHandler.cs#L441)).
- `KeyboardNavigation.TabNavigation="None"` no `SilkViewportControl` + `e.Handled` no `MainWindow.OnKeyDown`.

Build do `AvaloniaCadEditor.Application` passou limpo; `AvaloniaCadEditor.UI` não teve erro de compilação (só falha de cópia porque o app estava aberto) — build completo ainda pendente de confirmação após o usuário fechar o app.

## Novo problema reportado

Quando o modo de seleção é **Face** (`CadViewModel.CurrentSelectionMode == TargetSelectionMode.Face`, alternado pela tecla `F`, e também o modo implícito do `ExtrudeCommand` ao escolher a face base), o hover atual (`HoveredFeatureId`) ainda realça **o sólido inteiro**. O usuário quer que, nesse modo, o realce seja **da face específica** sob o cursor (como já acontece para a face *selecionada*, via `SelectedProfile`/`SelectionService.SelectedObject`), não do sólido todo. Isso vale tanto para o hover "parado" quanto para o ciclo via TAB entre sólidos sobrepostos.

## Causa raiz

- `CadViewModel.ProcessHover` sempre faz `MeshManager.Raycast3D` e seta `HoveredFeatureId`, independente do `CurrentSelectionMode` — não existe hoje nenhum "hover de face" para o modo Part/Face.
- A construção da face (`ParametricBoxFace`/`ParametricCylinderFace`/`FaceProfile` a partir de um `RaycastHit`) só existe duplicada em dois lugares de clique: `ViewportInputHandler.cs:199-271` (branch `SelectTool`/`TargetSelectionMode.Face`) e `ViewportInputHandler.cs:274-318` (branch `ExtrudeCommand`).
- `CadViewModel.HoveredProfile` existe mas é usado só para hover de região 2D em modo Sketch (`FindRegionAtPoint`, linha ~1115) — não deve ser reaproveitado para o hover 3D de face para não misturar os dois fluxos.
- `ProfileFaceRenderPass.cs:42-50` só inclui `vm.Sketch.Profiles` em modo `SketchEdit`, mais `SelectionService.SelectedObject` sempre (se for `IProfile`). Não há hoje nenhum "perfil hover" incluído fora do modo Sketch.

## Abordagem

### 1. Extrair a construção de face para um helper único

Em `ViewportInputHandler.cs`, criar um método `internal static IProfile? BuildFaceProfile(CadViewModel viewModel, RaycastHit hit)` com exatamente a lógica hoje duplicada (BoxFeature/CylinderFeature → `GetFaceTypeFromNormal` → `ParametricBoxFace`/`ParametricCylinderFace`; fallback → `FaceProfile` usando `GetStableWorkPlane` + `GetFeatureMesh`). Substituir os dois blocos duplicados (linhas 199-271 e 274-318) para chamar esse helper, mantendo intacta a lógica de acumulação com Ctrl (`CompoundProfile`) que já existe ao redor. Isso elimina a duplicação e garante que o hover reutilize exatamente a mesma regra de construção de face do clique.

### 2. Novo estado de hover de face no CadViewModel

Em `CadViewModel.cs`, junto a `HoveredProfile`/`HoveredFeatureId` (linha ~79-105):
- Nova propriedade `IProfile? HoveredFaceProfile` (mesmo padrão de `OnPropertyChanged`).
- Novo helper privado `bool IsFacePickMode() => CurrentTool is ExtrudeCommand || CurrentSelectionMode == TargetSelectionMode.Face;`

Em `ProcessHover` (linha ~110-145):
- Ao limpar o realce imediato no início, limpar também `HoveredFaceProfile = null`.
- No tick do `_hoverTimer`, após obter `hit = MeshManager.Raycast3D(...)`:
  - Se `hit?.FeatureId != null && IsFacePickMode()`: `HoveredFeatureId = null;` e `HoveredFaceProfile = ViewportInputHandler.BuildFaceProfile(this, hit.Value);`
  - Caso contrário: `HoveredFaceProfile = null;` e `HoveredFeatureId = hit?.FeatureId;` (comportamento atual).

Em `CycleHoveredFeature()` (já implementado): ao avançar para o candidato (`candidate`), aplicar a mesma bifurcação — se `IsFacePickMode()`, setar `HoveredFaceProfile = ViewportInputHandler.BuildFaceProfile(this, candidate)` e `HoveredFeatureId = null`; senão manter o comportamento atual (`HoveredFeatureId = candidate.FeatureId`). `_pickOverride` continua sendo o `RaycastHit` bruto em ambos os casos — o clique final já reconstrói a face a partir dele via `Raycast3D` + os branches existentes, então não muda.

### 3. Renderizar o hover de face fora do modo Sketch

Em `ProfileFaceRenderPass.cs`:
- Adicionar `vm.HoveredFaceProfile` a `profilesToRender` incondicionalmente (igual ao `SelectedObject`, linhas 46-50), evitando duplicar se já for o mesmo objeto do selecionado.
- Ampliar a condição de destaque ciano (linha 71) para `profile == vm.HoveredProfile || profile == vm.HoveredFaceProfile`.

Modo **Edge** não é alterado — não há realce de aresta implementado hoje; continua caindo no comportamento atual (`HoveredFeatureId`, sólido inteiro) por não ter infraestrutura própria, o que está fora do escopo pedido.

## Verificação

1. `dotnet build` nos projetos `Application` e `UI` (com o app fechado desta vez) para confirmar ausência de erros.
2. Rodar o app: entrar no `SelectTool`, apertar `F` até cair em modo Face, passar o mouse sobre um sólido e confirmar que **a face sob o cursor** fica realçada em ciano translúcido (não o sólido inteiro).
3. Com dois sólidos sobrepostos em modo Face, apertar TAB e confirmar que o realce de face alterna entre as faces do sólido da frente e as do sólido de trás, e que o clique final seleciona a face correta.
4. Repetir o teste no fluxo do `ExtrudeCommand` (tecla `P`/`E`, fase de escolha da face base).
5. Confirmar que o modo Feature (hover de sólido inteiro) continua funcionando como antes quando `CurrentSelectionMode == Feature`.
