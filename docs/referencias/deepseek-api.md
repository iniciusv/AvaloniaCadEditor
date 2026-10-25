# DeepSeek API: tool calls e thinking mode

> **Consultado em 2026-10-16.** Isto é um **resumo** das páginas oficiais (a ferramenta de busca
> não devolve o texto literal), feito para executar o plano
> [IA com ferramentas](../planos/2026-10-16-ia-ferramentas-tool-calling.md). Em caso de dúvida,
> e antes de implementar, confira as fontes:
>
> - Tool Calls: <https://api-docs.deepseek.com/guides/tool_calls>
> - Thinking Mode: <https://api-docs.deepseek.com/guides/thinking_mode>
> - Visão geral e modelos: <https://api-docs.deepseek.com/>

## Endpoints e modelos

| | |
|---|---|
| OpenAI-compatível | `https://api.deepseek.com` (o app usa `v1/chat/completions`) |
| OpenAI-compatível, **beta** (modo estrito) | `https://api.deepseek.com/beta` |
| Anthropic-compatível | `https://api.deepseek.com/anthropic` |
| Modelos | `deepseek-v4-pro`, `deepseek-flash` |

## Tool calls (function calling)

- O modelo **só pede** a chamada. Quem executa a função e devolve o resultado é a aplicação.
- Fluxo:
  1. A requisição leva `tools`.
  2. A resposta traz `message.tool_calls`.
  3. A aplicação executa e adiciona uma mensagem `role: "tool"` com o `tool_call_id` correspondente.
  4. A aplicação reenvia, e o modelo responde em linguagem natural ou pede outra ferramenta.
- Tool calls com **thinking mode** são suportadas a partir da DeepSeek-V3.2.
- **Chamadas inseridas no meio da conversa:** a **Chat Completion API não suporta** inserir
  chamadas de ferramenta e seus resultados no meio do histórico (aceita mensagens `system`). A
  Anthropic API e a Responses API suportam.

Formato no padrão OpenAI-compatível (as chaves seguem a convenção OpenAI; confira a seção Chat
Completion da API Reference):

```jsonc
// requisição
{
  "model": "deepseek-v4-pro",
  "messages": [...],
  "tools": [{
    "type": "function",
    "function": {
      "name": "rotate_instance",
      "description": "Gira uma instância de peça em torno de um eixo.",
      "parameters": { "type": "object", "properties": { ... }, "required": [ ... ] }
    }
  }]
}

// resposta: choices[0]
{
  "finish_reason": "tool_calls",
  "message": {
    "role": "assistant",
    "content": "",
    "reasoning_content": "...",
    "tool_calls": [{
      "id": "call_abc",
      "type": "function",
      "function": { "name": "rotate_instance", "arguments": "{\"instance\":\"suporte_a\", ...}" }
    }]
  }
}

// resultado devolvido pelo app
{ "role": "tool", "tool_call_id": "call_abc", "content": "ok, nova caixa: ..." }
```

`function.arguments` chega como **string** com JSON dentro e precisa de um segundo parse.

### Modo estrito (beta)

- Faz a saída da chamada seguir o JSON Schema da função. Funciona com e sem thinking mode.
- Requisitos: `base_url = https://api.deepseek.com/beta` e `"strict": true` em **todas** as funções.
- O servidor valida o schema e devolve erro se ele for inválido ou usar tipos não suportados.

Tipos suportados no modo estrito:

| Tipo | Regras |
|---|---|
| `object` | **todas** as propriedades em `required`; `additionalProperties: false` |
| `string` | `pattern`; formatos `email`, `hostname`, `ipv4`, `ipv6`, `uuid`. **Não** suporta `minLength`/`maxLength` |
| `number` / `integer` | `const`, `default`, `minimum`, `maximum`, `exclusiveMinimum`, `exclusiveMaximum`, `multipleOf` |
| `array` | **não** suporta `minItems`/`maxItems` |
| `boolean` | listado, sem detalhes na página |
| `enum` | restringe a valores fixos |
| `anyOf` | o campo segue um de vários schemas |
| `$ref` / `$def` | módulos reutilizáveis e estruturas recursivas |

Não estão na página: limite de ferramentas por requisição, chamadas paralelas, `tool_choice`.

## Thinking mode

- A saída tem duas partes: `reasoning_content` (cadeia de raciocínio) e `content` (resposta final).
- **Ligado por padrão**, com esforço `high`. Para desligar, envie `"thinking": {"type": "disabled"}`.
  O app já faz isso: [LlmClient.BuildRequestBody](../../AiCore/Services/LlmClient.cs).
  O esforço é ajustável (`reasoning_effort`, ou `output_config` no estilo Anthropic). Os níveis
  pedidos são agrupados: minimal/low → low; medium/high/xhigh → high; max/ultra → max.
- No SDK da OpenAI, `thinking` vai em `extra_body`.
- **Parâmetros sem efeito com thinking ligado:** `temperature`, `presence_penalty` e
  `frequency_penalty`. Não dão erro, só são ignorados.
- `top_p`: no `deepseek-flash`, vale só com thinking ligado (piso 0.95); no `deepseek-v4-pro`, é
  ignorado com thinking ligado e vale com ele desligado.

### Regras de múltiplos turnos (importante para o plano)

| Requisição | O que fazer com o `reasoning_content` anterior |
|---|---|
| **sem** `tools` | Não precisa reenviar. Se enviar, é ignorado e não entra no contexto. É o que o app faz hoje. |
| **com** `tools` | **Obrigatório** reenviar o de **todos** os turnos anteriores, inclusive dos turnos sem chamada de ferramenta. Sem isso: **HTTP 400**. |

Dentro de um turno com ferramentas: adicione a mensagem `assistant` **inteira** (`content` +
`reasoning_content` + `tool_calls`) ao histórico, depois cada resultado `tool` com o seu id, e
continue enviando o `reasoning_content` nos turnos seguintes do usuário.
