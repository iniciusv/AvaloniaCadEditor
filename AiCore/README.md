# AiCore

Núcleo reutilizável de comunicação com LLM e de protocolos de IA, extraído do
JsonOperator para ser copiado para outros projetos.

**Zero dependências externas** — usa apenas a BCL (`System.Net.Http` e
`System.Text.Json`).

## Conteúdo

| Arquivo | Responsabilidade |
|---|---|
| `Abstractions\IAiProtocol.cs` | Contrato `IAiProtocol<TContext, TResult>` + `ProtocolParseResult<TResult>` |
| `Models\ChatMessage.cs` | Modelo de mensagem (POCO puro, sem UI/Mvvm) |
| `Models\LlmOptions.cs` | Configuração da chamada (ApiKey, BaseUrl, Model, Temperature, MaxTokens, Thinking, RequestTimeout) |
| `Services\LlmClient.cs` | Cliente HTTP compatível com OpenAI (`/v1/chat/completions`, `/v1/models`) |
| `Services\LlmResponseParser.cs` | Leitura tolerante da resposta: `reasoning_content`, `reasoning` ou `<think>` no conteúdo |

## Como usar

1. Copie a pasta `AiCore` para o seu projeto.
2. Ajuste o namespace `AiCore` se desejar.
3. Chame o cliente:

```csharp
using AiCore.Models;
using AiCore.Services;

var options = new LlmOptions
{
    ApiKey = "sua-chave",
    BaseUrl = "https://api.deepseek.com/",
    Model = "deepseek-v4-pro",
    EnableThinkingMode = true
};

var client = new LlmClient();

var messages = new List<ChatMessage>
{
    new ChatMessage { Role = "system", Content = "Você é um assistente." },
    new ChatMessage { Role = "user", Content = "Olá!" }
};

LlmResult result = await client.SendConversationAsync(messages, options);

Console.WriteLine(result.Content);           // texto da resposta
Console.WriteLine(result.ReasoningContent);  // raciocínio (se houver)
Console.WriteLine(result.PromptTokens);      // tokens de entrada
Console.WriteLine(result.CompletionTokens);  // tokens de saída
```

### Servidor local (Ollama, LM Studio, llama.cpp)

Qualquer servidor OpenAI-compatível serve. Sem chave, a requisição vai sem `Authorization`; com
`MaxTokens = 0`, vale o limite do servidor; e `ThinkingControl.None` não manda o campo `thinking`,
que é da DeepSeek.

```csharp
var local = new LlmOptions
{
    BaseUrl = "http://localhost:11434/v1",   // LM Studio: 1234, llama.cpp: 8080
    Model = "qwen3:8b",
    ApiKey = "",
    ApiKeyRequired = false,
    MaxTokens = 0,
    Thinking = ThinkingControl.None,
    RequestTimeout = TimeSpan.FromMinutes(30)
};

IReadOnlyList<string> modelos = await client.ListModelsAsync(local);
LlmResult resposta = await client.SendConversationAsync(messages, local);
```

### Ferramentas (function calling)

O cliente só transporta as ferramentas, sem executar nada. Ele manda as `tools` na requisição
e devolve as chamadas pedidas em `LlmResult.ToolCalls`. Quem executa é você: devolva cada resultado
numa mensagem `role = "tool"` com o `ToolCallId` da chamada e reenvie a conversa.

```csharp
var tools = new[] { new ToolDefinition("get_view", "Vista em texto.", schema) };
var r = await client.SendConversationAsync(messages, options, tools: tools);
if (r.FinishReason == "tool_calls")
{
    messages.Add(new ChatMessage { Role = "assistant", Content = r.Content, ReasoningContent = r.ReasoningContent, ToolCalls = r.ToolCalls });
    foreach (var call in r.ToolCalls)
        messages.Add(new ChatMessage { Role = "tool", ToolCallId = call.Id, Content = Execute(call) });
}
```

Com `tools` na DeepSeek, o `reasoning_content` de todos os turnos anteriores é reenviado,
porque sem ele a API responde 400. O cliente cuida disso.

## Protocolos

Implemente `IAiProtocol<TContext, TResult>` para cada tarefa da IA
(geração de SQL, código, JSON estruturado, etc.). Cada implementação define:

- `GetSystemPrompt()` — papel da IA + formato estrito da resposta;
- `BuildUserMessage(context, instruction)` — injeta o contexto do domínio;
- `ParseResponse(rawResponse)` — extrai o payload estruturado da resposta bruta.

Exemplo mínimo:

```csharp
using AiCore.Abstractions;

public class NomeProtocol : IAiProtocol<object, string>
{
    public string GetSystemPrompt() => "Retorne APENAS o nome, sem explicação.";

    public string BuildUserMessage(object ctx, string instruction) => instruction;

    public ProtocolParseResult<string> ParseResponse(string raw) =>
        new ProtocolParseResult<string> { ChatMessage = "Nome gerado.", Payload = raw.Trim() };
}
```
