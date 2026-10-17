# Chat IA: toggles de histórico (multi-round) e reasoning

> **Etapa anterior (já implementada):** contexto exibido por mensagem (chips de arquivos + sólidos),
> lixeirinha que apaga o turno inteiro com sincronização do `_history`, e manutenção do modelo
> "seleção atual a cada envio". Arquivos: `AiChatMessageItem.cs`, `AiSentContextItem.cs`,
> `AiChatViewModel.cs`, `AiChatView.axaml`, `CadScriptChatSession.cs` (`DeleteTurn`/`_turnStarts`).

## Context

Dois ajustes pedidos depois da análise:

1. **Multi-round é caro e confuso neste app.** Cada mensagem de usuário (`CadScriptProtocol.BuildUserMessage`)
   re-embute o **código completo** de todos os arquivos de contexto, e o `_history` acumula os turnos.
   Resultado: versões antigas dos mesmos arquivos ficam repetidas no histórico (inchaço ~quadrático de
   tokens) e o modelo vê várias versões do mesmo arquivo. Como o estado atual dos arquivos é reenviado
   inteiro a cada turno, o ganho do histórico é pequeno. Decisão: **toggle "Manter histórico da
   conversa (multi-round)", padrão LIGADO** (preserva o comportamento atual; pode desligar p/ ficar
   stateless). O auto-fix (reenvio de erro de compilação) é multi-round **dentro do turno** e continua
   existindo nos dois modos.
2. **Reasoning (thinking mode)** hoje está sempre ligado para modelos deepseek-v4/reasoner
   (`LlmOptions.EnableThinkingMode = true`, nunca sobrescrito). Decisão: **toggle "Reasoning",
   padrão DESLIGADO**.

Ambos ficam no painel de configurações (⚙) e são persistidos, como ApiKey/Model/BaseUrl.

## Mudanças

### 1. Settings persistidas — `AvaloniaCadEditor.Domain\Settings\AiSettings.cs`
Adicionar dois campos (System.Text.Json mantém o inicializador quando o campo falta no JSON antigo):
```csharp
public bool KeepConversationHistory { get; set; } = true;   // multi-round por padrão
public bool EnableReasoning { get; set; } = false;          // reasoning desligado por padrão
```

### 2. Sessão stateless opcional + reasoning — `CadScriptChatSession.cs`
- Nova flag `public bool KeepHistory { get; set; } = true;`.
- No **início** de `SendAsync`, antes de `_turnStarts.Add(...)`: `if (!KeepHistory) ResetHistory();`
  (descarta turnos anteriores; cada envio vira system + arquivos atuais + pedido). O loop de auto-fix
  segue appenda­ndo normalmente dentro do turno.
- Em `LoadOptions(...)`, após montar `options`: `options.EnableThinkingMode = settings.EnableReasoning;`
  (o `LlmClient` já condiciona o `thinking` ao modelo; sem mudança lá). `LoadOptionsFromEnvironment`
  (uso CI) fica como está.

### 3. ViewModel — `AiChatViewModel.cs`
- Propriedades `bool KeepHistoryInput` e `bool EnableReasoningInput` (com `OnPropertyChanged`), lidas
  de `settings` no construtor.
- `InitializeSession()`: após criar/atualizar `_session`, aplicar
  `_session.KeepHistory = <settings>.KeepConversationHistory;` (ler `GetSettings()` ali).
- `SaveSettings()`: gravar os dois bools no `AiSettings`; depois do save, **resetar a conversa**
  (`_session?.NewConversation()`, `Messages.Clear()`, `_nextTurnId = 0`) para manter `Messages` e
  `_history` em sincronia ao trocar de modo — hoje o `Clear()` já ocorre, falta o `NewConversation()`.

### 4. UI — `AiChatView.axaml`
No painel de settings (`StackPanel` de linhas ~58–89), acima do botão Save, dois `CheckBox`:
- `IsChecked="{Binding EnableReasoningInput}"` — "Reasoning (thinking mode)", com dica de que deixa
  as respostas mais lentas/caras.
- `IsChecked="{Binding KeepHistoryInput}"` — "Manter histórico (multi-round)", com dica de que
  desligado cada envio é independente e mais barato.

## Arquivos
- `AvaloniaCadEditor.Domain\Settings\AiSettings.cs` — dois bools.
- `AvaloniaCadEditor.Application\Ai\CadScriptChatSession.cs` — `KeepHistory`, reset stateless, reasoning em `LoadOptions`.
- `AvaloniaCadEditor.UI\ViewModels\AiChatViewModel.cs` — inputs, `InitializeSession`, `SaveSettings`.
- `AvaloniaCadEditor.UI\Views\AiChatView.axaml` — dois checkboxes no painel de settings.

## Verificação
1. `dotnet build` (fechar o app em execução antes — ele trava os `.dll`).
2. `dotnet test` em `AvaloniaCadEditor.Tests` (há `JsonAiSettingsProviderTests` e
   `CadScriptChatSessionTests`); confirmar round-trip das settings com os novos campos. Opcional:
   novo teste de que `KeepHistory = false` deixa o `_history` com só `system + user + assistant` após
   dois envios seguidos.
3. App: ⚙ → desligar "Manter histórico", Save; enviar 2–3 pedidos encadeados e confirmar que cada um
   envia só o estado atual (sem crescer o custo com versões antigas). Religar e confirmar que volta a
   lembrar do contexto da conversa.
4. ⚙ → alternar "Reasoning" com modelo deepseek-v4-pro: ligado mostra o Expander "Reasoning" no balão;
   desligado não traz raciocínio.
