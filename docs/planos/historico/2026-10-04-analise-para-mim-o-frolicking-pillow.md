# Análise do protocolo de IA + pré-prompt — e correções propostas

## Context

O usuário pediu uma análise do protocolo de comunicação com a IA e do pré-prompt.
A análise está na resposta do chat. Este arquivo registra apenas as **correções
acionáveis** que ela produziu, em ordem de severidade, caso se queira executá-las.

O protocolo hoje: `AiCore` (transporte OpenAI-compatible, zero deps) +
`IAiProtocol<CadScriptContext, CadScriptChanges>` implementado por
`CadScriptProtocol`. A IA recebe system prompt (regras + API gerada por reflexão +
few-shots testados) e, por turno, os arquivos do escopo inteiros; responde com texto
curto + blocos ```` ```cadscript file=<caminho> ```` contendo cada arquivo COMPLETO.
`CadScriptChatSession` fecha o loop: parse → `AiEditScopePolicy` → `CadScriptValidator`
→ compila e **executa** via Roslyn → em caso de erro, reenvia o erro (até 3 tentativas).

---

## Correções propostas (ordem de severidade)

### 1. Validar a regra de "sólidos rastreáveis por nome"
`AvaloniaCadEditor.Application/Ai/CadScriptValidator.cs`

A regra mais repetida do pré-prompt (`CadScriptProtocol.cs:177-183` e
`CadScriptApiReference.cs:97-106`) não tem nenhum gate automático: código que cria
sólidos em laço/LINQ ou com `Name` interpolado compila, executa e é aceito.

Adicionar ao validador (Roslyn syntax, já disponível no arquivo):
- todo `ObjectCreationExpressionSyntax` de tipo que termina em `Feature` (ou chamada
  `BooleanFeature.Create*`) deve estar em um `LocalDeclarationStatementSyntax`/
  `VariableDeclaratorSyntax` próprio;
- o inicializador `Name = ...` deve ser `LiteralExpressionSyntax` (recusar
  `InterpolatedStringExpressionSyntax` e concatenação) e igual ao nome do declarador;
- nomes únicos no arquivo;
- recusar criação de feature dentro de `ForStatement`/`ForEachStatement`/`WhileStatement`
  ou de lambda em invocação LINQ.

Mensagens de erro no mesmo estilo das existentes — elas voltam para a IA pelo auto-fix,
então viram autocorreção de graça. Cobrir em `AvaloniaCadEditor.Tests/Ai/CadScriptValidatorTests.cs`.

### 2. Corrigir `max_tokens`
`AiCore/Models/LlmOptions.cs:21`, `AiCore/Services/LlmClient.cs:87`

`384000` é janela de contexto, não limite de completion; o XML doc diz "na resposta".
Baixar o default para algo real (ex.: `8192`) e corrigir o doc. Provedor estrito
responde 400 com o valor atual.

### 3. Endurecer o parse de blocos
`AvaloniaCadEditor.Application/Ai/CadScriptProtocol.cs:22-24, 328-357`

O regex casa até a primeira ```` ``` ````. Um fence dentro de comentário/string, ou
cerca de 4 backticks, trunca o "arquivo completo" silenciosamente — e o truncado pode
compilar. Opções: exigir fence no início da linha (`(?m)^```` ), contar fences e
rejeitar número ímpar, e devolver erro explícito em vez de payload truncado.

### 4. Streaming
`AiCore/Services/LlmClient.cs:89-112, 125-153` + `AiChatViewModel`

`stream = false` com timeout de 15 min e thinking mode ligado = minutos de spinner.
Adicionar `SendConversationStreamAsync` (SSE, `IAsyncEnumerable<string>`) e consumir
incrementalmente na UI. Parse e validação continuam acontecendo no texto final.

### 5. Podar o histórico das tentativas falhas
`AvaloniaCadEditor.Application/Ai/CadScriptChatSession.cs:134-215`

Cada retry acrescenta um `BuildUserMessage` completo (todos os docs do escopo). Com
`KeepHistory = true`, os pares "resposta ruim + erro" ficam no histórico após o sucesso.
Remover do `_history` os itens de auto-fix do turno quando ele termina válido, mantendo
só (user inicial, assistant final) — `_turnStarts` já dá os limites do turno.

### 6. Exigir `file=` no modo multi-arquivo
`AvaloniaCadEditor.Application/Ai/Context/AiEditScopePolicy.cs:41`

`f.FilePath ?? activeFile` faz um bloco sem `file=` sobrescrever o arquivo ativo. Se o
ativo for uma peça e a IA omitir o `file=` de outra peça, grava a peça errada sem erro.
Em `context.Scope != null`, recusar bloco sem `file=` com mensagem para a IA.

### 7. Tirar o vendor do cliente "genérico"
`AiCore/Services/LlmClient.cs:75-79, 90`

- `modelName.Contains("deepseek-v4")` → trocar por `LlmOptions.ExtraBody`
  (`Dictionary<string, object>`) mesclado no corpo; quem decide é quem configura.
- Remover o ramo `reasoning_content` nas mensagens de saída: é inalcançável
  (`CadScriptChatSession.cs:140-142` nunca o guarda no histórico) e a API não o aceita de volta.

### 8. Fonte única das regras do pré-prompt
`CadScriptProtocol.GetSystemPrompt()`, `CadScriptApiReference.cs:72-113`,
`AvaloniaCadEditor.Application/Scenes/SceneProjectTemplate.cs:122-164`

As mesmas regras estão escritas três vezes, com redação diferente. Extrair os blocos
comuns para constantes compartilhadas e fazer `BuildAgentsFile` consumi-las, para o
`AGENTS.md` não divergir do system prompt.

### 9. Orçamento de contexto
`AvaloniaCadEditor.Application/Ai/Context/AiContextBuilder.cs:123-156`

Dependências transitivas entram sem limite e sem contagem de tokens antes do envio.
Estimar o tamanho do prompt, avisar na UI e/ou degradar dependências distantes para
assinatura (o caminho de `PartSummaries` já existe).

### 10. Chave de API em texto puro
`AvaloniaCadEditor.Infrastructure/Settings/JsonAiSettingsProvider.cs:19-23`

`%AppData%/AvaloniaCadEditor/ai_settings.json` em plaintext. No Windows, proteger com
DPAPI (`ProtectedData.Protect`, escopo CurrentUser).

---

## Verificação

- `dotnet build AvaloniaCadEditor.slnx`
- `dotnet test AvaloniaCadEditor.Tests` — os testes existentes já são a especificação
  executável do protocolo: `CadScriptProtocolTests`, `CadScriptProtocolExamplesTests`
  (compila e roda cada few-shot), `CadScriptChatSessionTests`,
  `CadScriptChatSessionMultiFileTests`, `CadScriptValidatorTests`,
  `Context/AiEditScopePolicyTests`.
- Novos testes por item: validador de nomes (1), parse com fence aninhado (3), bloco
  sem `file=` em multi-arquivo (6), histórico após auto-fix bem-sucedido (5).
- Fim a fim: rodar o app, abrir `AiCore/` como cena, pedir uma peça com N furos e
  confirmar que a violação da regra de nomes volta como erro para a IA e é corrigida
  sozinha dentro das 3 tentativas.
