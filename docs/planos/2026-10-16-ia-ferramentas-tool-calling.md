# Ferramentas para a IA (tool calling) no chat de CadScript

| | |
|---|---|
| **Status** | Proposto, ainda não iniciado |
| **Data** | 2026-10-16 |
| **Branch de origem** | `feature/varredura-por-caminho` (só análise, sem código) |
| **Referências externas** | [DeepSeek: tool calls e thinking mode](../referencias/deepseek-api.md) |

## Objetivo

Hoje a IA só **gera texto**. O app extrai os blocos ```` ```cadscript ````, valida e avalia o
código. A ideia é dar à IA **ferramentas específicas** que ela possa chamar:

- **consultar a geometria**, para saber onde as coisas estão;
- **editar o código de forma determinística**, por exemplo girar, mover ou espelhar uma instância;
- **verificar a cena** antes de responder.

O objetivo é aumentar a **precisão** (menos coordenadas chutadas) e a **eficiência** (menos tokens,
sem reescrever o arquivo inteiro para uma rotação).

## Como funciona a parte "harness" da DeepSeek

A DeepSeek **não executa nada**. Ela suporta *function calling* no formato OpenAI:

1. A requisição leva `tools`: uma lista de funções, cada uma com nome, descrição e `parameters` em JSON Schema.
2. O modelo responde com `message.tool_calls` e `finish_reason = "tool_calls"`.
3. **O app executa** a função e devolve o resultado numa mensagem `{ role: "tool", tool_call_id, content }`.
4. Os passos 2 e 3 se repetem até o modelo dar a resposta final (`finish_reason = "stop"`).

O laço que executa e devolve os resultados é o "harness", e quem o escreve somos nós. Os detalhes
e restrições da API estão em [referencias/deepseek-api.md](../referencias/deepseek-api.md).

## Situação atual (o que já é um mini-harness)

[CadScriptChatSession.cs](../../AvaloniaCadEditor.Application/Ai/CadScriptChatSession.cs) faz
um laço fixo de até `MaxAttempts = 3` tentativas (linha 134):

1. Um envio, cuja resposta traz o arquivo **completo**.
2. `AiEditScopePolicy.ResolveEdits` e depois `CadScriptValidator.Validate`.
3. `ISceneEvaluator.EvaluateAsync`.
4. Se der erro, reenvia o erro.

Limitações:

- **O modelo não enxerga a geometria.** Não há como perguntar a caixa delimitadora, uma face ou
  uma interferência. Ele deduz tudo do código.
- **Toda mudança reescreve o arquivo inteiro.** Isso custa tokens e arrisca efeitos colaterais.
- **O auto-fix é fixo.** O modelo não decide quando testar.

## Catálogo de ferramentas candidatas

O ganho de **precisão** vem principalmente das ferramentas de **consulta**. Uma ferramenta de
rotação não corrige um pivô mal escolhido; uma consulta de caixa delimitadora feita antes, sim.

| Categoria | Ferramenta | Ganho | Base existente |
|---|---|---|---|
| Consulta | `get_bounding_box(target)` | precisão | `IGeometryKernel.BuildFeatureMesh` / `BuildSolidMesh` ([IGeometryKernel.cs](../../AvaloniaCadEditor.Application/Interfaces/IGeometryKernel.cs)) |
| Consulta | `measure_distance(a, b)` | precisão | malha + `AvaloniaCadEditor.Analysis` |
| Consulta | `list_faces(feature)` / `list_edges(feature)` | precisão | `FaceReference` / `EdgeReference`, `FactExtractor.Extract` ([FactExtractor.cs](../../AvaloniaCadEditor.Analysis/FactExtractor.cs)) |
| Consulta | `get_relations()` (alinhamentos, furos coaxiais) | precisão | `RelationAnalyzer.Analyze` ([RelationAnalyzer.cs](../../AvaloniaCadEditor.Analysis/RelationAnalyzer.cs)) |
| Consulta | `check_interference(a, b)` | precisão | booleana de interseção no kernel (a criar) |
| Consulta | `get_part_parameters(instance)` | precisão | `PartSourceEditor.ReadArguments` (linha 41) |
| Edição determinística | `rotate_instance(instance, pivot, axis, degrees)` | eficiência | `PartSourceEditor.RotateInstance` (linha 354) |
| Edição determinística | `translate_instance(instance, dx, dy, dz)` | eficiência | `PartSourceEditor.TranslateInstance` (linha 188) |
| Edição determinística | `mirror_instance(instance, point, normal)` | eficiência | `PartSourceEditor.MirrorInstance` (linha 391) |
| Edição determinística | `set_part_argument(instance, param, value)` | eficiência | `PartSourceEditor.SetArgument` (linha 65) |
| Edição determinística | `rotate_feature` / `translate_feature` / `mirror_feature` / `copy_feature` / `rename_feature` | eficiência | `PartSourceEditor.*Feature` |
| Verificação | `evaluate_scene()` | substitui o auto-fix fixo | `ISceneEvaluator` ([ISceneEvaluator.cs](../../AvaloniaCadEditor.Application/Interfaces/ISceneEvaluator.cs)) |
| Edição livre | `replace_in_file(path, old, new)` / `write_file(path, text)` | eficiência | `AiEditScopePolicy` para validar o escopo |

Os métodos de [PartSourceEditor.cs](../../AvaloniaCadEditor.Application/Scenes/PartSourceEditor.cs)
são os mesmos que a UI usa para mover, girar e espelhar. Assim, a IA e o usuário passam a editar o
código pelo mesmo caminho.

## Arquitetura proposta

### 1. `AiCore`: cliente e modelo de mensagem

- [LlmClient.cs](../../AiCore/Services/LlmClient.cs):
  - `BuildRequestBody` (linha 82) passa a aceitar `tools` (e, se for o caso, `tool_choice`).
  - `SendConversationAsync` (linha 120) passa a ler `message.tool_calls` e `choices[0].finish_reason`.
  - `LlmResult` ganha `ToolCalls` e `FinishReason`.
- [ChatMessage.cs](../../AiCore/Models/ChatMessage.cs) **já tem** `ReasoningContent`. Faltam
  `ToolCalls` (no `assistant`) e `ToolCallId` (no `tool`).
- `BuildApiMessages` (linha 58) **inverte a regra** quando há `tools`: é obrigatório reenviar o
  `reasoning_content` de **todos** os turnos anteriores, inclusive dos que não chamaram
  ferramenta. Sem isso a API responde **400**. O comentário que já está no método prevê esse caso,
  e a documentação o confirma.
- Manter o AiCore sem dependências externas (só BCL), como diz o [README do AiCore](../../AiCore/README.md).

### 2. `Application`: contrato de ferramenta e registro

```csharp
public interface ICadTool
{
    string Name { get; }              // "rotate_instance"
    string Description { get; }       // o modelo escolhe a ferramenta lendo isto
    JsonObject ParametersSchema { get; }
    Task<string> ExecuteAsync(JsonElement args, CadToolContext ctx, CancellationToken ct);
}
```

- `CadToolContext` guarda uma **cópia de trabalho** dos documentos, usando o mesmo `Overlay`
  que `CadScriptChatSession` já tem. As ferramentas editam essa cópia, e as mudanças só saem no fim
  do turno como `CadScriptTurnResult.Changes`. A UI continua recebendo um único diff e o undo
  continua igual.
- As ferramentas de edição devolvem um retorno geométrico, como `"ok, nova caixa: min(...) max(...)"`,
  e não só `"ok"`.
- Os schemas podem ser gerados por reflexão, como faz
  [CadScriptApiReference.cs](../../AvaloniaCadEditor.Application/Ai/CadScriptApiReference.cs).

### 3. O laço em `CadScriptChatSession`

```
envia (system + contexto + pedido, com tools)
enquanto finish_reason == "tool_calls" e passos < MaxToolSteps (~15):
    adiciona a mensagem assistant inteira (content + reasoning_content + tool_calls)
    para cada tool_call: executa e adiciona { role: tool, tool_call_id, content }
    reenvia
no fim: validação e avaliação de hoje sobre a cópia de trabalho, como garantia
```

- `DeleteTurn` precisa remover também as mensagens `assistant(tool_calls)` e `tool` do turno.
  Hoje `_turnStarts` já agrupa vários itens por turno, então o mecanismo serve.
- `KeepHistory = false` fica mais atraente, porque o `reasoning_content` reenviado infla o payload.
- Fallback: provedores sem tools continuam no protocolo ```` ```cadscript ````, com uma verificação
  parecida com `LlmClient.SupportsThinkingToggle`.

### 4. Modo estrito (beta)

Com a URL `https://api.deepseek.com/beta` e `strict: true` em **todas** as funções, o servidor
garante que os argumentos seguem o schema. As restrições afetam como os schemas são escritos:
todo `object` precisa ter **todas** as propriedades em `required` e `additionalProperties: false`;
`minLength`, `maxLength`, `minItems` e `maxItems` não são suportados. Veja
[referencias/deepseek-api.md](../referencias/deepseek-api.md#modo-estrito-beta).

## Etapas sugeridas (primeira versão)

- [ ] **1. Cliente.** Adicionar `tools` e o parse de `tool_calls`/`finish_reason` no `LlmClient`;
  incluir `ToolCalls`/`ToolCallId` em `ChatMessage`; reenviar `reasoning_content` quando há tools.
  Ampliar `LlmClientRequestBodyTests`.
- [ ] **2. Contrato.** Criar `ICadTool`, `CadToolContext` (com a cópia de trabalho) e o registro de ferramentas.
- [ ] **3. Laço.** Implementar o laço de ferramentas em `CadScriptChatSession`, com limite de passos e
  a validação final. Testar com o `SendDelegate` falso, que já é usado em `CadScriptChatSessionTests`.
- [ ] **4. Ferramentas iniciais:** `evaluate_scene`, `get_bounding_box`, `rotate_instance`/`translate_instance` e `replace_in_file`.
- [ ] **5. Medição.** Adicionar ao [PromptEval](../../AvaloniaCadEditor.PromptEval/README.md) a opção
  com/sem ferramentas e comparar acerto, tokens e latência.
- [ ] **6. UI.** Mostrar no chat as chamadas de ferramenta, de forma recolhível.

## Perguntas em aberto

- **Nome da instância para linha do código.** Os métodos de `PartSourceEditor` recebem `line`.
  É preciso resolver o nome (ex.: `bloco_fixacao_a`) para o arquivo e a linha. Candidatos:
  `ISceneCodeNavigator`/`RoslynCodeNavigator` e `SceneSyntaxIndex`. Lembrar do bug já visto em
  `ao-tentar-movimentar-o-snoopy-harp` (algumas instâncias não gravavam a posição).
- **Parâmetros opcionais no modo estrito.** Se tudo é `required`, como expressar um parâmetro
  opcional? `anyOf` com `null` não aparece na lista de tipos suportados. É preciso testar.
- **Ferramentas inseridas no meio da conversa.** A Chat Completion API da DeepSeek **não**
  aceita chamadas de ferramenta inseridas no histórico no meio da conversa (a Anthropic API e a
  Responses API aceitam). Não dá, por exemplo, para fabricar um `tool_call` com o resultado de
  `evaluate_scene`. Contexto extra entra como mensagem `user`.
- **Temperatura.** Com o thinking mode ligado, `temperature` não tem efeito. O
  `LlmOptions.Temperature = 0.3` hoje só vale com o raciocínio desligado.
- Não confirmados na documentação: limite de ferramentas por requisição, chamadas paralelas e `tool_choice`.

## Riscos

- **Latência e custo.** Cada chamada de ferramenta é mais uma ida e volta com o contexto inteiro.
  O cache de prefixo da DeepSeek ajuda (system prompt e arquivos se repetem), mas um turno com 5
  ferramentas sai mais caro que uma resposta direta.
- **Escolha da ferramenta.** Ferramentas demais ou mal descritas levam a uso errado ou a chamadas
  desnecessárias. Começar com poucas e medir no PromptEval.
- **Regras do CadScript.** As edições determinísticas precisam respeitar as regras de
  [CadScriptRules.cs](../../AvaloniaCadEditor.Application/Ai/CadScriptRules.cs). Exemplo: um sólido
  que depende de `FilletFeature` não pode ser girado nem espelhado como operando de booleana
  (linha 111). `rotate_*`/`mirror_*` sobre ele devem recusar com essa mensagem, em vez de deixar
  o kernel devolver uma malha vazia.
