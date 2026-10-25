# Balões do chat de IA responsivos + ícone minimalista

## Context
No [AiChatView.axaml](AvaloniaCadEditor.UI/Views/AiChatView.axaml), cada balão de mensagem tem `MaxWidth="320"` fixo em pixels e é alinhado à esquerda/direita via `HorizontalAlignment` nos estilos `Border.userBubble`/`Border.assistantBubble`. Como o painel do chat agora vive dentro da aba "Code" (coluna redimensionável pelo `GridSplitter`), alargar a coluna não alarga os balões — eles ficam presos em 320px, desperdiçando o espaço. Além disso, o cabeçalho do painel usa o emoji "🤖 AI Chat", enquanto o resto do app (barra lateral de ferramentas, ícones da árvore em [ModelTreeView.axaml](AvaloniaCadEditor.UI/Views/ModelTreeView.axaml)) usa ícones vetoriais minimalistas: um único `<Path Data="...">` (glifo Material Design Icons, viewBox 24x24) com `Fill="#999999"` (ou `LightGray`), `Stretch="Uniform"`, `Width="14" Height="14"`.

Decisão já validada com o usuário: os balões passam a ocupar 100% da largura disponível (sem `MaxWidth` fixo); a cor de fundo (azul para o usuário, cinza para a IA) continua sendo o único diferenciador de quem enviou a mensagem — o alinhamento esquerda/direita é removido.

## 1. Balões ocupando a largura toda
Em `AiChatView.axaml`:
- Nos estilos `Border.userBubble` e `Border.assistantBubble` (linhas 13-20), remover o `Setter Property="HorizontalAlignment"` de cada um (ou trocar por `Stretch` explicitamente) — só resta o `Background` diferenciando as duas classes.
- No `Border` raiz do `DataTemplate` de cada mensagem (linha 98), remover `MaxWidth="320"`.
- Resultado: como o `ItemsControl` (linha 93) usa o painel padrão (StackPanel vertical), cada item já recebe a largura total disponível por padrão — bastando não mais restringi-la — então o balão passa a esticar sozinho quando a coluna é redimensionada, sem necessidade de bindings extras ou conversores.

## 2. Ícone minimalista para "AI Chat"
No cabeçalho do painel (linha 36), trocar o `TextBlock Text="🤖 AI Chat"` por um `Path` + `TextBlock Text="AI Chat"` (sem emoji), replicando exatamente o padrão de [ModelTreeView.axaml:49](AvaloniaCadEditor.UI/Views/ModelTreeView.axaml#L49) (`Fill="#999999" Stretch="Uniform" Width="14" Height="14" VerticalAlignment="Center"`).

Ícone: glifo "robot-outline" do Material Design Icons (mesma biblioteca/estilo já usada em todo o app — veja os `iconCube`/`iconCylinder`/`iconFolder` etc. em [ModelTreeViewModel.cs](AvaloniaCadEditor.UI/ViewModels/ModelTreeViewModel.cs)), path já obtido da fonte oficial (Pictogrammers/MaterialDesign, viewBox 24x24):

```
M17.5 15.5C17.5 16.61 16.61 17.5 15.5 17.5S13.5 16.61 13.5 15.5 14.4 13.5 15.5 13.5 17.5 14.4 17.5 15.5M8.5 13.5C7.4 13.5 6.5 14.4 6.5 15.5S7.4 17.5 8.5 17.5 10.5 16.61 10.5 15.5 9.61 13.5 8.5 13.5M23 15V18C23 18.55 22.55 19 22 19H21V20C21 21.11 20.11 22 19 22H5C3.9 22 3 21.11 3 20V19H2C1.45 19 1 18.55 1 18V15C1 14.45 1.45 14 2 14H3C3 10.13 6.13 7 10 7H11V5.73C10.4 5.39 10 4.74 10 4C10 2.9 10.9 2 12 2S14 2.9 14 4C14 4.74 13.6 5.39 13 5.73V7H14C17.87 7 21 10.13 21 14H22C22.55 14 23 14.45 23 15M21 16H19V14C19 11.24 16.76 9 14 9H10C7.24 9 5 11.24 5 14V16H3V17H5V20H19V17H21V16Z
```

Nenhum outro lugar do app referencia o emoji 🤖 (já confirmado por busca no repo), então essa é a única troca necessária.

## Verificação
1. `dotnet build AvaloniaCadEditor.slnx` — sem erros (XAML válido, path do ícone bem formado).
2. Rodar o app, abrir a aba "Code": o cabeçalho do chat mostra o ícone de robô minimalista (mesmo estilo/cor dos ícones da árvore) em vez do emoji; arrastar o `GridSplitter` que separa código/chat (ou o que separa a coluna lateral do viewport) e verificar que os balões de mensagem — tanto do usuário quanto da IA — acompanham a nova largura da coluna, sem ficar presos em 320px.
