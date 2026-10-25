# Mover não grava a posição de algumas instâncias (bloco_conexao_placa_carrinho_y_a, bloco_ficacao_belt_vao_correia)

## Context
No app, com a ferramenta Mover, o preview do sólido se desloca, mas ao confirmar (valor digitado + Enter)
a peça volta para onde estava. A barra de status não muda. Acontece com algumas instâncias da cena AiCore
(`bloco_conexao_placa_carrinho_y_a`, linha 158; `bloco_ficacao_belt_vao_correia`, linha 169 de
`AiCore/Cena.cs`). Outras, como `cantoneira_placa_extrusao_a` (linha 81), movem normalmente.

Fluxo da confirmação (já lido):
`VcbInputService.ProcessKeyDown` → `MoveTool.OnValueEntered` → `ApplyMove` → evento `FeaturesMoved` →
`CadViewModel.MoveTool_FeaturesMoved` (UI/ViewModels/CadViewModel.cs:268) →
`SceneScriptViewModel.MovePartInstances` → `EditFeatureCalls` (SceneScriptViewModel.cs:996) →
`PartSourceEditor.TranslateInstance` (Application/Scenes/PartSourceEditor.cs:188) → `RunCodeAsync`.

Status parado significa que `RunCodeAsync` nem rodou: `MoveTool_FeaturesMoved` saiu com `changed == false`.
Isso só acontece por um destes caminhos SILENCIOSOS:
1. `FeatureOrigin.Of(f)` null ou `Line <= 0` (filtrado em `EditFeatureCalls`);
2. `GetDocumentText(file)` null (`continue` sem mensagem);
3. `TranslateInstance` devolveu o texto igual (deslocamento ~0);
4. a lista `moved` não tinha a `PartInstance` (ex.: `SelectionOverride` do grupo aberto devolveu outra coisa,
   ou o selecionado não era a instância).

Lendo o código, `TranslateInstance` funciona para as linhas 158/169 (`Placement.At(expr, expr, expr, ...)`
vira `expr + d`), `Placement.At`, o fingerprint e os caches de malha estão corretos, e o avaliador não
reescreve o fonte (as linhas do `CallerLineNumber` batem). Por isso o primeiro passo é reproduzir.

## Plano

### 1. Reproduzir num teste com a cena real (sem UI)
Novo teste em `AvaloniaCadEditor.Tests/Scenes/` (padrão de `ConexaoYAdaptador5mmTests`: `AiCoreFolder()`,
`SceneProject.Load`, `RoslynSceneEvaluator.EvaluateAsync(project.ReadDocuments())`), parametrizado por nome
de instância: `cantoneira_placa_extrusao_a` (controle), `bloco_conexao_placa_carrinho_y_a`,
`bloco_ficacao_belt_vao_correia`. Para cada uma:
- `FeatureOrigin.Of(instance)` não é null, `Line` é a linha do `Add` e `FilePath` resolve para `Cena.cs`;
- `PartSourceEditor.TranslateInstance(source, line, 10, 0, 0)` muda o texto;
- reavaliar o texto editado move o `Placement.Translation.X` da instância em +10.
Se o teste passar nas três, o defeito está na UI (caminhos 2 ou 4). Nesse caso, um teste de
`CadViewModel`/`SceneScriptViewModel` (padrão de `PartSolidMoveTests`/`SceneEditingTests`) com o projeto
AiCore carregado, selecionando a instância e chamando o fluxo do Mover.

### 2. Acabar com as saídas silenciosas (vale independente da causa)
- `SceneScriptViewModel.EditFeatureCalls`: quando a feature não tem origem, o documento não é achado ou a
  edição não muda nada, escrever em `_cadViewModel.StatusText` o motivo e o nome da feature
  (ex.: `✗ Mover: não achei no código de onde veio 'bloco_conexao_placa_carrinho_y_a'`), no mesmo estilo
  do `catch` que já existe ali.
- `CadViewModel.MoveTool_FeaturesMoved` (e os análogos de girar/espelhar, que usam o mesmo
  `EditFeatureCalls`): com `!changed` e nenhuma mensagem anterior, avisar em vez de `return` mudo.

### 3. Corrigir a causa encontrada no passo 1
A correção depende do caminho reproduzido. Mantê-la no ponto do defeito (ex.: resolução de arquivo em
`SourceFileOf`/`GetDocumentText`, filtro de seleção em `MoveTool.OnMouseDown`/`HighlightedPartSolidSelection`)
e cobrir com o teste do passo 1.

## Arquivos
- `AvaloniaCadEditor.UI/ViewModels/SceneScriptViewModel.cs` (`EditFeatureCalls`, `MovePartInstances`)
- `AvaloniaCadEditor.UI/ViewModels/CadViewModel.cs` (`MoveTool_FeaturesMoved`)
- `AvaloniaCadEditor.Application/Scenes/PartSourceEditor.cs` (só se a causa estiver em `TranslateInstance`/`FindAddCall`)
- novo teste em `AvaloniaCadEditor.Tests/Scenes/`

## Verificação
- `dotnet test --filter` no teste novo: as três instâncias movem (+10 em X).
- Rodar a suíte de `Scenes`/`BehaviorTests` de mover/girar/espelhar (`MoveBehaviorTests`, `PartSolidMoveTests`,
  `SceneEditingTests`, `MirrorEditingTests`). Os 2 testes de ConexaoY que já falham no HEAD não contam
  (mudanças em AiCore no disco).
- No app: mover `bloco_conexao_placa_carrinho_y_a` digitando um valor + Enter; o `at:` da linha 158 ganha
  `+ d` e a peça fica na posição nova. Repetir com `bloco_ficacao_belt_vao_correia` e com a cantoneira.
