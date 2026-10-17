# Servidores locais OpenAI-compatíveis (Ollama, LM Studio, llama.cpp)

Esta página resume o que o plano [Harness de ferramentas para a IA](../planos/2026-10-16-ia-ferramentas-tool-calling.md)
precisa saber de cada servidor. Foi consultada em **2026-10-17**. Algumas fontes são secundárias e
estão marcadas como tal; confirme na versão instalada antes de depender de um detalhe.

## Resumo para o cliente (`AiCore/LlmClient`)

| Ponto | Ollama | LM Studio | llama.cpp (`llama-server`) |
|---|---|---|---|
| URL base padrão | `http://localhost:11434/v1` | `http://localhost:1234/v1` | `http://localhost:8080/v1` |
| Chave de API | O cliente OpenAI exige um valor, mas o servidor o **ignora** (exemplos usam `ollama`) | Não documentado; os exemplos usam o placeholder `lm-studio` | Opcional (`--api-key`, padrão: nenhuma) |
| `tools` / `tool_calls` | Suportado; **`tool_choice` não é suportado** | Suportado. Modelos com o "martelo" têm suporte nativo; os demais usam um formato padrão de fallback | Suportado via template Jinja (`--jinja`, ligado por padrão nas versões atuais); pode exigir `--chat-template-file` |
| `function.arguments` | (não documentado) | **String JSON** (exemplo oficial com `json.loads`) | (não documentado) |
| Chamada que o servidor não consegue parsear | (não documentado) | Vai para `message.content` | (não documentado) |
| Campo do raciocínio | `message.reasoning`, com `content` possivelmente vazio *(fonte secundária)* | `message.reasoning` desde a 0.3.23 (gpt-oss); versões mais antigas usam `reasoning_content` com uma opção de desenvolvedor, ou `<think>` no `content` | `message.reasoning_content` (`--reasoning-format auto`); com `none`, fica no `content`; com `deepseek-legacy`, aparecem os dois |
| Desligar o raciocínio | `reasoning_effort: "none"` ou `think: false` *(fonte secundária)* | `reasoning_effort` (0.4.8+) | `--reasoning-format none` / template |
| `GET /v1/models` | Sim (`/v1/models` e `/v1/models/{model}`) | Sim ("List Models") | Sim |
| Contexto | **Não dá para definir pela API OpenAI.** Use um Modelfile com `PARAMETER num_ctx <n>` e `ollama create`. O padrão é baixo: risco de **truncamento silencioso** | Definido ao carregar o modelo no app | `-c` (padrão 0 = o do modelo) |
| `max_tokens` / `stream` | Os dois são suportados | Suportados | Suportados |
| Chamadas paralelas | (não documentado) | (não documentado) | Campo `parallel_tool_calls` na requisição; depende do modelo e do template |

## Consequências para a implementação

- **Chave.** Um perfil local manda o pedido sem `Authorization` quando a chave está vazia
  (`ApiKeyRequired = false`). Os três servidores aceitam isso; o Ollama e o LM Studio ignoram a
  chave de qualquer forma.
- **Raciocínio.** O parse lê `reasoning_content` **ou** `reasoning`, e tira o bloco
  `<think>…</think>` do `content` quando ele vem embutido.
- **`thinking`.** O campo é da DeepSeek. Não pode ir para servidores locais, que podem rejeitá-lo;
  daí o `Thinking = None` nesses perfis.
- **`tool_choice`.** Não use, porque o Ollama não suporta.
- **`arguments`.** Aceite tanto string JSON quanto objeto, e gere um `id` quando faltar.
- **Degradação.** Se o JSON de uma chamada vier no `content` (fallback do LM Studio, ou modelo sem
  template de tools), trate como protocolo `Text`.
- **Contexto.** Avise quando `usage.prompt_tokens` se aproximar do `ContextWindow` do perfil. No
  Ollama, documente o `num_ctx` no README do Harness.
- **`max_tokens`.** Com `MaxTokens = 0`, o campo é omitido e vale o padrão do servidor; o limite de
  384000 da DeepSeek não serve para modelos locais.

## Fontes

- Ollama, compatibilidade OpenAI: <https://docs.ollama.com/api/openai-compatibility>
- llama.cpp, README do server: <https://github.com/ggml-org/llama.cpp/blob/master/tools/server/README.md>
- LM Studio, tool use: <https://lmstudio.ai/docs/developer/openai-compat/tools>
- LM Studio, changelog da API (0.3.23, `message.reasoning`): <https://www.lmstudio.ai/docs/developer/api-changelog>
- LM Studio 0.4.8 (`reasoning_effort`): <https://lmstudio.ai/changelog/lmstudio/lmstudio-v0.4.8>
- *Secundária:* campo `reasoning` do Ollama e `content` vazio: <https://github.com/NousResearch/hermes-agent/issues/46131>
- *Secundária:* `think=false` no Ollama: <https://github.com/tidyverse/ellmer/issues/940>
