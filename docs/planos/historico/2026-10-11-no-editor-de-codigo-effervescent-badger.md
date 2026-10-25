# Realçar ocorrências do texto selecionado no editor de código

## Contexto
Ao dar duplo clique numa palavra no editor (AvaloniaEdit `TextEditor` "CodeEditor" em
`AvaloniaCadEditor.UI/Views/MainWindow.axaml:335`), destacar todas as outras ocorrências
idênticas no mesmo arquivo, como no VS Code. Não é difícil: o AvaloniaEdit já dá tudo
(`TextArea.SelectionChanged`, `IBackgroundRenderer`, `BackgroundGeometryBuilder`). ~70 linhas.

## Abordagem

1. **Novo renderer** `AvaloniaCadEditor.UI/Controls/Rendering/SelectionOccurrenceRenderer.cs`
   - Implementa `IBackgroundRenderer` (camada `KnownLayer.Selection`), não um
     `DocumentColorizingTransformer` — assim não briga com o `FeatureHighlightColorizer`
     nem com o `ErrorLineColorizer`, que pintam o fundo do texto.
   - Guarda `List<(int Start, int Length)> Occurrences`; em `Draw`, para cada ocorrência
     visível usa `BackgroundGeometryBuilder.GetRectsForSegment` e desenha um retângulo
     cinza translúcido com borda (ex.: fundo `#40ADD6FF`, borda `#80ADD6FF`).

2. **Ligação no `CodeEditorBehavior`** (`AvaloniaCadEditor.UI/Controls/CodeEditorBehavior.cs`)
   - Nova attached property `HighlightSelectionOccurrences` (bool), no mesmo padrão das
     demais (registro + `AddClassHandler` no construtor estático), e setada `True` no XAML.
   - No handler: cria/adiciona o renderer a `editor.TextArea.TextView.BackgroundRenderers`
     e assina `editor.TextArea.SelectionChanged` (e `editor.TextChanged` para limpar).
   - Ao mudar a seleção:
     - texto selecionado = `editor.SelectedText`; só realça se for de linha única,
       1–200 chars e não só espaço;
     - se a seleção for uma palavra inteira (identificador), busca só ocorrências com
       limite de palavra (regex `\b` + `Regex.Escape`) — evita realçar `x` dentro de `max`;
       caso contrário, busca literal ordinal (`IndexOf` em loop);
     - exclui a própria seleção da lista; teto de ~2000 ocorrências;
     - `TextView.InvalidateLayer(KnownLayer.Selection)`.
   - Seleção vazia ou edição de texto → limpa a lista.

3. **Opcional (barato)**: marcar as ocorrências também na barra de rolagem — fora de escopo
   agora; o AvaloniaEdit não tem isso pronto.

## Arquivos
- novo: `AvaloniaCadEditor.UI/Controls/Rendering/SelectionOccurrenceRenderer.cs`
- `AvaloniaCadEditor.UI/Controls/CodeEditorBehavior.cs`
- `AvaloniaCadEditor.UI/Views/MainWindow.axaml` (uma linha)

## Verificação
- `dotnet build` (com o app fechado, ou `--artifacts-path` no scratchpad se as DLLs
  estiverem travadas).
- Rodar o app, abrir um script, dar duplo clique num nome de variável usado várias vezes:
  as outras ocorrências ficam realçadas; `x` não acende dentro de `max`; clicar fora limpa;
  digitar limpa; realce de feature selecionada e linhas de erro continuam funcionando juntos.
