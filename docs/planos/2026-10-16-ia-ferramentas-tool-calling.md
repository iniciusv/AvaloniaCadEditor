# Harness de ferramentas para a IA: tool calling, perfis de endpoint (DeepSeek + local) e vista em texto

| | |
|---|---|
| **Status** | Proposto (revisado em 2026-10-17), ainda não iniciado |
| **Data** | 2026-10-16, revisão em 2026-10-17 |
| **Branch de origem** | `feature/varredura-por-caminho` (só análise, sem código) |
| **Referências externas** | [DeepSeek: tool calls e thinking mode](../referencias/deepseek-api.md) · [Servidores locais OpenAI-compatíveis](../referencias/servidores-locais-openai.md) (a preencher na etapa 0) |

## Objetivo

Hoje a IA só **gera texto**. O app extrai os blocos ```` ```cadscript ````, valida e avalia o
código. Este plano junta três frentes.

1. **Ferramentas (tool calling).** A IA passa a **consultar a geometria**, **editar de forma
   determinística** (girar, mover, espelhar uma instância) e **verificar a cena** antes de
   responder. Isso traz **precisão** (menos coordenadas chutadas) e **eficiência** (não reescrever o
   arquivo inteiro para uma rotação).
2. **Perfis de endpoint intercambiáveis.** Cada papel (Editor, Inspetor e, mais tarde, Revisor)
   aponta para um endpoint OpenAI-compatível: a DeepSeek ou um **modelo local** (Ollama, LM Studio
   ou llama.cpp; o servidor ainda não foi escolhido). O harness é o mesmo para todos. Um modelo
   local não custa nada por chamada, então pode fazer laços longos e processamento pesado, sozinho
   ou junto com a DeepSeek.
3. **"O que está na tela" em texto.** É a exportação 2D do modelo em edição nas vistas padrão,
   ortográfica e em arame: arestas visíveis e ocultas, silhuetas e círculos com coordenadas 2D. Ela
   inclui as **cotas**, reaproveitando o "Copiar" da folha de cotas. Só texto; visão por imagem
   fica para depois.

Há uma restrição de organização: **o código que só o harness usa não pode poluir o código da
ferramenta CAD.** Ele fica num módulo próprio, com dependências em um só sentido (veja
[Organização](#organização-o-harness-é-um-módulo-à-parte)).

## Como funciona a parte "harness"

O provedor **não executa nada**. Ele suporta *function calling* no formato OpenAI.

1. A requisição leva `tools`: uma lista de funções, cada uma com nome, descrição e `parameters` em
   JSON Schema.
2. O modelo responde com `message.tool_calls` e `finish_reason = "tool_calls"`.
3. **O app executa** a função e devolve o resultado numa mensagem `{ role: "tool", tool_call_id, content }`.
4. Os passos 2 e 3 se repetem até a resposta final (`finish_reason = "stop"`).

O laço que executa e devolve os resultados é o "harness", e quem o escreve somos nós. As regras da
DeepSeek estão em [referencias/deepseek-api.md](../referencias/deepseek-api.md). Os servidores locais
usam o mesmo formato, com variações: veja [Modelos locais](#modelos-locais).

## Situação atual

- **Um mini-harness fixo.** [CadScriptChatSession.cs](../../AvaloniaCadEditor.Application/Ai/CadScriptChatSession.cs)
  faz até `MaxAttempts = 3` tentativas (linha 134). Cada uma recebe uma resposta com o arquivo
  **completo**, passa por `AiEditScopePolicy.ResolveEdits` e `CadScriptValidator.Validate`, depois
  por `ISceneEvaluator.EvaluateAsync`, e reenvia o erro se houver. Isso tem três limites:
  - o modelo não enxerga a geometria;
  - toda mudança reescreve o arquivo;
  - o auto-fix é fixo, e o modelo não decide quando testar.
- **Um só endpoint.** [AiSettings.cs](../../AvaloniaCadEditor.Domain/Settings/AiSettings.cs) é plano
  (ApiKey, BaseUrl, Model). O [LlmClient.cs](../../AiCore/Services/LlmClient.cs) já monta
  `BaseUrl + v1/chat/completions`, então `localhost` funciona. Três coisas atrapalham:
  - as linhas 131-134 exigem chave, e um servidor local não tem chave;
  - `MaxTokens = 384000` por padrão;
  - `SupportsThinkingToggle` (linha 73) decide pelo nome do modelo ou pela URL.

  Na UI, `AiChatViewModel` (linhas 116-120) tem dois ids DeepSeek fixos, e um id desconhecido é
  sobrescrito ao salvar.
- **As cotas em texto já existem.** [DraftingReport.cs](../../AvaloniaCadEditor.Drafting/DraftingReport.cs)
  (`Format`, linha 21) gera o texto do "Copiar". O caminho sem GUI já está provado em
  [PlacaSuporteDrawingTests.cs](../../AvaloniaCadEditor.Tests/Drafting/PlacaSuporteDrawingTests.cs)
  (linhas 38-48): `SceneProject.Load` → `RoslynSceneEvaluator` → `OpenCascadeGeometryKernel.BuildFeatureMesh`
  → `PartFrameResolver.Resolve` → `DraftingInput.ForIsolated` → `DraftingAnalyzer.Analyze`.
- **Não há HLR em CPU.** O oculto e o visível do arame saem do depth buffer da GPU
  (`WireframeRenderPass`, só na UI), e o wrapper OCCT não expõe HLR. Algumas peças já existem:
  - [PlaneProjection.cs](../../AvaloniaCadEditor.Domain/Mesh/PlaneProjection.cs) seleciona quinas,
    bordas e silhuetas (`IsVisibleLine`, linha 56, privado), mas não marca o que está oculto;
  - `ReferencePolylines` (`internal`) encadeia, simplifica e ajusta círculos;
  - [ViewProjector.cs](../../AvaloniaCadEditor.Drafting/ViewProjector.cs) gera `ViewCircle` com o
    raio exato e o nome do fato.
- **A orientação das vistas tem uma fonte só.** `CameraState.SetStandardView` (Domain) define as
  vistas, e `ViewBasis.For` (Drafting) devolve `null` para a Iso.
- **Camadas.** Application referencia Domain, Analysis e AiCore, e **não** referencia Drafting.
  Drafting referencia só Domain e Analysis.

## Organização: o harness é um módulo à parte

### Projeto novo `AvaloniaCadEditor.Harness`

É uma class library (net10.0) com **todo** o código que só existe por causa do harness:

- o contrato e o registro de ferramentas;
- o laço de agente e os papéis;
- as ferramentas de vista e de cota;
- o protocolo de ferramenta em texto;
- a formatação para LLM.

O projeto de teste também é separado: `AvaloniaCadEditor.Harness.Tests`.

### Regras de dependência

Um **teste de arquitetura** garante estas regras verificando `Assembly.GetReferencedAssemblies()`:

```
UI (composition root) ─┐
PromptEval ────────────┼──► Harness ──► Application, Drafting, Analysis, Domain, AiCore
Harness.Tests ─────────┘
Application ──X──► Harness / Drafting      (Application continua sem conhecer nenhum dos dois)
Domain, Drafting, Analysis, AiCore ──X──► Harness
```

Quem junta cena, malha e cotas é o Harness, e o Application não ganha nenhuma referência nova.

### Regra de promoção (YAGNI)

Um algoritmo nasce no Harness, mesmo que seja genérico, como o HLR. Ele só sobe para Domain ou
Drafting quando o app ganhar um **segundo consumidor**, por exemplo uma folha 2D com linhas ocultas.
Essa regra fica escrita no README do Harness.

### O que muda nos projetos existentes

Só entram mudanças com valor próprio para o app ou extrações sem mudança de comportamento. Cada uma
vai num commit separado e é coberta pelos testes atuais.

| Projeto | Mudança | Por que não é poluição |
|---|---|---|
| AiCore | `tools`/`tool_calls` no cliente, chave opcional, `MaxTokens = 0` (omite o campo), `Thinking` explícito, timeout por requisição, `ListModelsAsync` (`/v1/models`), parse de `reasoning`/`<think>`. Tudo em **arquivos novos** (`Models/ToolDefinition.cs`, `Models/ToolCall.cs`, `Services/LlmResponseParser.cs`) | É capacidade genérica de cliente LLM, que continua só com a BCL e copiável. Um modelo local como Editor usa isso sem harness |
| Domain/Settings + Infrastructure + UI | `AiEndpointProfile`, lista de perfis com perfil ativo, migração `AiSettingsMigration.Upgrade` (pura) e UI de perfis com modelo livre | É recurso do app: trocar DeepSeek por local no chat de hoje. **Papéis não entram aqui** |
| Application | (a) extrair a interface `ICadScriptChatSession` com o `SendAsync`, `DeleteTurn`, `NewConversation`, `KeepHistory` e `UpdateOptions` atuais; (b) extrair `WorkingCopyEvaluator` do `EvaluateAsync` privado (linhas 253-294) e usá-lo na própria sessão | São refatorações sem mudança de comportamento; a sessão atual continua sendo a implementação padrão |
| Domain/Mesh | `ReferencePolylines` passa de `internal` a `public`; o predicado `IsVisibleLine` de `PlaneProjection` é extraído para um `MeshEdgeClassifier` público | Só muda visibilidade e extrai código, sem lógica nova, e evita duplicar no Harness |
| UI | O composition root (`App.axaml.cs`/`AiChatViewModel`) escolhe `CadScriptChatSession` ou `HarnessChatSession` através de `ICadScriptChatSession` | O ViewModel só delega |

**Fora do escopo**, para não mexer no app à toa: mover `StartParameterHints` do `DraftingViewModel`
(linha 267) para Application. Na v1, `get_dimensions` sai sem as dicas de parâmetro
("Ø3,4 · RaioFuroPassante"). Se depois valer a pena, isso vira uma refatoração própria.

### Estrutura do projeto

```
AvaloniaCadEditor.Harness/
  README.md                 regras de dependência, regra de promoção, como adicionar ferramenta
  HarnessChatSession.cs     implementa ICadScriptChatSession usando AgentLoop
  Agents/
    AgentLoop.cs            laço tool_calls → executa → reenvia; limites de passos e tempo
    AgentLimits.cs
    InspectorAgent.cs       (etapa 8) pré-inspeção + ferramenta inspect(question)
  Roles/
    AgentRole.cs            Editor | Inspector | Reviewer
    HarnessSettings.cs      papel → id do perfil, MaxToolSteps e orçamentos por perfil
    IHarnessSettingsStore.cs + JsonHarnessSettingsStore.cs  (%AppData%/AvaloniaCadEditor/harness_settings.json)
    RoleOptionsResolver.cs  AiSettings.Profiles + HarnessSettings → LlmOptions por papel
  Tools/
    ICadTool.cs, CadToolContext.cs (cópia de trabalho + Revision), ToolRegistry.cs, ToolSchema.cs
    Query/   ListTargetsTool, BoundingBoxTool, ViewTool, DimensionsTool
    Edit/    TranslateInstanceTool, RotateInstanceTool, ReplaceInFileTool
    Verify/  EvaluateSceneTool
  Inspection/
    SceneInspectionService.cs  avaliação com cache por Revision, malha (IGeometryKernel próprio), referencial
    TargetResolver.cs
  Views/
    ProjectionBasis.cs      vistas padrão + Iso, derivadas de CameraState.SetStandardView
    HiddenLineProjector.cs  HLR em CPU na malha
    WireframeReport.cs      texto da vista (cabeçalho no estilo do DraftingReport)
  Protocol/
    TextToolProtocol.cs     fallback ```tool {...}``` para servidores sem tool calling
    ToolSupportDetector.cs  Auto → Native | Text | cadscript
```

### Boas práticas do módulo

- **Ferramentas finas.** Validam os argumentos, delegam a um serviço e formatam o resultado. Não há
  geometria dentro de uma `ICadTool`.
- **Algoritmos puros.** `HiddenLineProjector`, `ProjectionBasis` e `WireframeReport` não têm I/O nem
  estado e são testados isoladamente.
- **Estado no lugar certo.** Fica no serviço (`SceneInspectionService`) ou no contexto do turno
  (`CadToolContext`), nunca em campo estático.
- **Convenções do código.** Um arquivo por tipo, nomes em inglês como no resto do código e
  comentários na mesma densidade do código vizinho.
- **Schemas JSON.** Saem de um único `ToolSchema`, já compatível com o
  [modo estrito](#modo-estrito-beta).

### Contrato de ferramenta

```csharp
public interface ICadTool
{
    string Name { get; }              // "get_view"
    string Description { get; }       // o modelo escolhe a ferramenta lendo isto
    bool IsReadOnly { get; }          // o Inspetor só recebe ferramentas de leitura
    JsonObject ParametersSchema { get; }
    Task<string> ExecuteAsync(JsonElement args, CadToolContext ctx, CancellationToken ct);
}
```

- **Cópia de trabalho.** `CadToolContext` guarda uma cópia dos documentos, usando o mesmo `Overlay`
  que a sessão já usa, e um `Revision` que sobe a cada edição. As ferramentas editam a cópia, e as
  mudanças só saem no fim do turno como `CadScriptTurnResult.Changes`. A UI continua recebendo um
  único diff, e o undo continua igual.
- **Retorno das edições.** As ferramentas de edição devolvem um retorno geométrico, por exemplo
  `"ok, nova caixa: min(...) max(...)"`, e não só `"ok"`.

## Vista em texto e cotas

### HLR em CPU (`Harness/Views/HiddenLineProjector`)

A entrada é a `TriangleMesh` no referencial da peça, uma `ProjectionBasis` e as opções (crease de
30°, passo de amostragem, epsilon e orçamento).

1. **Candidatas.** Montar o `MeshEdgeGraph.Build(mesh)` e filtrar com o `MeshEdgeClassifier`: uma
   aresta entra se for quina, borda ou silhueta em relação a W.
2. **Aceleração.** Na ortográfica todos os raios são paralelos a W, então basta uma grade uniforme
   2D em (U, V), com cerca de `sqrt(nTri)` células por eixo; cada triângulo projetado vai para as
   células da sua caixa. Não é preciso uma BVH.
3. **Visibilidade.** Amostrar cada aresta com `n = clamp(comprimento_projetado / passo, 2, 64)`
   pontos. Um ponto está oculto se algum triângulo da célula o contém (teste baricêntrico) e está
   mais perto do observador além de `eps = 1e-5 × diagonal`. Os **triângulos da própria aresta ficam
   excluídos**, para que a silhueta rasante de um cilindro não se oculte sozinha.
4. **Trechos.** Juntar amostras seguidas que têm o mesmo estado e bissecar 3 vezes cada transição.
   Cada trecho sai como `(A, B, Visible, Kind = Crease|Silhouette|Border)`.
5. **Ocultos coincidentes.** Na vista Frente de uma caixa, as arestas de trás caem sobre as da
   frente. Um trecho oculto que coincide com um visível é removido, e um visível duplicado também.
   O cálculo agrupa os trechos por reta quantizada e subtrai os intervalos.
6. **Polilinhas.** `ReferencePolylines.Build`, separado para visíveis e ocultas, encadeia os trechos,
   tira os pontos colineares e ajusta círculos.
7. **Orçamento.** A malha tem um limite de triângulos (padrão 300 mil; acima dele a ferramenta
   recusa com uma mensagem), e o processamento tem um limite de tempo.

A `ProjectionBasis.For(StandardView, azimuthOffset)` **deriva da câmera**. Ela aplica
`CameraState.SetStandardView` numa câmera temporária e calcula `W = normalize(Position - Target)`,
`V = Up` e `U = normalize(Up × W)`. Com isso a orientação continua tendo uma fonte só, e a Iso
passa a ter base.

### Formato do texto (`WireframeReport`)

O formato é Markdown compacto. Usa o mesmo cabeçalho de `DraftingReport` (peça, tipo, U/V ↔ eixos
da peça, origem do código na folha), para que `get_view` e `get_dimensions` falem no mesmo
referencial. Os números têm até 3 casas decimais (`0.###`), sempre em cultura invariante.

```
# Vista Front — placa_suporte (PlacaSuporte) · ortográfica, arame
U=+X, V=+Z, olhar=-Y · código (0,0,0) = folha (60, 0) · caixa U[0;120] V[0;40] W[-8;0]
## Visíveis — 6 linhas, 2 círculos
- L1 quina fechada: (0,0) (120,0) (120,40) (0,40)
- C1 Ø8 c(15,20) [furo_a]
- S1 silhueta: (40,10)→(40,30)
## Ocultas — 4 linhas
- H1 quina: (10,0)→(10,40)
## Omitidos — 37 trechos < 0.5 mm (orçamento de 6000 caracteres)
```

- **Círculos.** Saem como Ø e centro. Quando um círculo ajustado casa com um `ViewCircle` do
  `ViewProjector`, o texto usa o **raio exato** e o **nome do fato**, porque na malha os furos são
  prismas de 32 lados.
- **Nome do dono por aresta.** Na v1 só aparece em círculos casados com fatos. Gerar uma malha por
  sub-feature seria caro e erraria com as booleanas.
- **Orçamento `max_chars`.** O padrão é derivado do `ContextWindow` do perfil. Os trechos menores
  são omitidos primeiro, e as ocultas são cortadas antes das visíveis.
- **Iso.** Mesma estrutura do texto. Os círculos viram elipses e saem como polilinhas, e não há
  cotas, porque `DraftingAnalyzer` devolve `null` na Iso.

### Cotas (`get_dimensions`)

A ferramenta chama `DraftingAnalyzer.Analyze` e depois `DraftingReport.Format(sheet, sheet.Dimensions, …, codeOrigin)`,
os dois usados como estão. A origem do código é `frame.ToFrame(placement.Apply(Origin))`, como em
`DraftingViewModel`. Saem só as cotas automáticas, porque as manuais e as escondidas são estado da
UI.

### Alvo, referencial e cache

- **Alvo.**
  1. O `Name` de uma feature de topo, sem diferenciar maiúsculas.
  2. Se não houver, o `PartInstance.PartTypeName`, desde que seja único.
  3. Se o alvo for omitido, a seleção ou a instância única (no modo arquivo de peça, com a entrada
     sintética).
  4. Se nada servir, a ferramenta devolve um erro com a lista de alvos válidos.
- **Referencial.** Vem de `PartFrameResolver.Resolve`. Quando ele devolve `null` (peça girada fora
  de Z), o texto usa o referencial do mundo e avisa isso no cabeçalho.
- **Oclusão.** Na v1, só o próprio alvo esconde arestas, como no isolamento (tecla H). A oclusão
  pelos outros sólidos da cena fica para a v2.
- **Cache.** O `SceneInspectionService` guarda três níveis, todos indexados pela `Revision` do
  contexto:
  - `Revision → SceneEvaluationResult`;
  - `(Revision, featureId) → malha`;
  - `(Revision, alvo, vista, flags) → texto`.
- **Kernel.** As ferramentas usam um `IGeometryKernel` próprio, para não invalidar os caches da UI.

## Perfis de endpoint e papéis

- **AiCore** (continua só com a BCL). O `LlmOptions` ganha estes campos:
  - `ApiKeyRequired`;
  - `MaxTokens = 0`, que omite o campo;
  - `RequestTimeout`;
  - `Thinking` (`Auto | DeepSeekToggle | None`), que substitui o palpite pelo nome;
  - `ToolSupport` (`Auto | Native | Text | None`).

  O `LlmClient` também muda:
  - sem chave, não manda `Authorization`;
  - aceita um `HttpMessageHandler` injetável para testes;
  - ganha `ListModelsAsync`;
  - no parse, lê `reasoning_content` **ou** `reasoning` (Ollama), tira `<think>…</think>`, aceita
    `arguments` como string ou objeto e gera o `id` quando o servidor não manda.
- **App (Domain/Settings).** `AiSettings.SchemaVersion = 2`, com `Profiles` (`AiEndpointProfile`:
  Id, nome, BaseUrl, modelo, chave ou nome da variável de ambiente, IsLocal, MaxTokens,
  ContextWindow, timeout, Thinking, ToolSupport) e `ActiveProfileId`. A migração pura cria o perfil
  `deepseek` a partir dos campos antigos. **O app não conhece papéis.**
- **Harness (Roles).** Guarda papel → id do perfil e os limites por perfil. Os padrões são: perfil
  local com 60 passos e timeout longo; DeepSeek com 15 passos. Tudo vai num
  `harness_settings.json` próprio. `RoleOptionsResolver` combina os dois arquivos e gera uma
  `LlmOptions` por papel. O `SendDelegate` não muda: a rota de cada papel é só a `LlmOptions`
  passada.
- **Inspetor** (etapa 8, depois do laço estável e medido). Tem dois modos sobre o mesmo `AgentLoop`:
  - **Pré-inspeção.** Antes do envio ao Editor, o Inspetor roda só com as ferramentas de leitura e
    produz um resumo geométrico de até cerca de 2 mil caracteres. O resumo entra no pedido como
    "Notas geométricas". Esse modo **funciona mesmo quando o Editor não tem tools**, o que serve
    tanto a um Editor local sem tools quanto a um Editor DeepSeek com um Inspetor local gratuito.
  - **`inspect(question)`.** É uma ferramenta do Editor que roda o Inspetor como sub-agente. O
    contexto do Editor só paga pelo resumo.
- **Revisor.** Fica para depois. Seu papel seria avaliar a cena final contra o pedido e devolver os
  problemas ao Editor.

## Modelos locais

- **Laços.** Os laços podem ser maiores (`MaxToolSteps` do perfil), com um orçamento de **tempo**
  por turno.
- **Chamada repetida.** A mesma ferramenta com os mesmos argumentos devolve o resultado do cache,
  com o aviso "já consultado".
- **Contexto curto (8 a 32 mil tokens).**
  - O `max_chars` das ferramentas é derivado do `ContextWindow`.
  - Resultados antigos de ferramenta viram "[resultado omitido, chame de novo se precisar]".
  - O app avisa quando `usage.prompt_tokens` chega perto do limite.
  - Na DeepSeek não se compacta nada: lá é obrigatório reenviar todo o `reasoning_content`.
- **Degradação** (`ToolSupport = Auto`). O app manda `tools`. Se o servidor responder 4xx citando
  "tool" ou "function", ou se o JSON da chamada vier dentro de `content`, o perfil passa a `Text`
  na sessão. No modo `Text`, o system prompt descreve as ferramentas e pede um bloco
  ```` ```tool {"name":…, "arguments":{…}} ````, e o resultado volta como mensagem `user`. Se nem
  isso funcionar, o app volta ao protocolo ```` ```cadscript ```` de hoje.
- **PromptEval.** O `--profiles` continua sendo o perfil de prompt. As opções novas são:
  - `--endpoints id1,id2`, que lê os perfis do `ai_settings.json`;
  - `--models perfil:modelo`;
  - `--tools off|on|both`;
  - `--inspector <perfil>`.

  O `RunOutcome` ganha `Endpoint`, `Role`, `ToolSteps` e `ToolMode`, e o `--parallel` fica limitado
  a 1 por endpoint local.

## Catálogo de ferramentas

O ganho de **precisão** vem principalmente das ferramentas de **consulta**. Uma ferramenta de rotação
não corrige um pivô mal escolhido; uma consulta de caixa delimitadora feita antes, sim.

| Categoria | Ferramenta | Base existente | Fase |
|---|---|---|---|
| Consulta | `list_targets()` | `SceneEvaluationResult.Features` | v1 |
| Consulta | `get_bounding_box(target)` | `IGeometryKernel.BuildFeatureMesh` ([IGeometryKernel.cs](../../AvaloniaCadEditor.Application/Interfaces/IGeometryKernel.cs)) | v1 |
| Consulta | `get_view(target, view, include_hidden, max_chars)` | `HiddenLineProjector` + `WireframeReport` (novos, no Harness) | v1 |
| Consulta | `get_dimensions(target, view)` | `DraftingAnalyzer` + `DraftingReport` | v1 |
| Consulta | `get_relations(target)` (alinhamentos, furos coaxiais) | `RelationAnalyzer.Analyze` ([RelationAnalyzer.cs](../../AvaloniaCadEditor.Analysis/RelationAnalyzer.cs)) | v2 |
| Consulta | `measure_distance(a, b)`, `list_faces`/`list_edges`, `check_interference(a, b)`, `get_part_parameters(instance)` | malha, `FactExtractor`, booleana de interseção (a criar), `PartSourceEditor.ReadArguments` | v2+ |
| Edição determinística | `translate_instance`, `rotate_instance` | `PartSourceEditor.TranslateInstance`/`RotateInstance` ([PartSourceEditor.cs](../../AvaloniaCadEditor.Application/Scenes/PartSourceEditor.cs)) | v1 |
| Edição determinística | `mirror_instance`, `set_part_argument`, `*_feature` | `PartSourceEditor.*` | v2 |
| Verificação | `evaluate_scene()` | `WorkingCopyEvaluator` | v1 |
| Edição livre | `replace_in_file(path, old, new)` (`write_file` na v2) | `AiEditScopePolicy` | v1 |
| Agente | `inspect(question)` | `AgentLoop` com o perfil do Inspetor | etapa 8 |

Os métodos de `PartSourceEditor` são os mesmos que a UI usa para mover, girar e espelhar. Assim, a
IA e o usuário editam o código pelo mesmo caminho.

## O laço (`Harness/Agents/AgentLoop`)

```
envia (system + contexto + pedido, com tools)
enquanto finish_reason == "tool_calls" e houver passos e tempo no orçamento:
    adiciona a mensagem assistant inteira (content + reasoning_content + tool_calls)
    para cada tool_call: executa e adiciona { role: tool, tool_call_id, content }
    reenvia
no fim: validação e avaliação de hoje sobre a cópia de trabalho (WorkingCopyEvaluator), como garantia
```

- **`DeleteTurn`.** Precisa remover também as mensagens `assistant(tool_calls)` e `tool` do turno.
  O `_turnStarts` atual já agrupa vários itens por turno, então o mecanismo serve.
- **DeepSeek com tools.** O `reasoning_content` de **todos** os turnos anteriores precisa ser
  reenviado, inclusive o dos turnos que não chamaram ferramenta. Sem isso a API responde **400**.
  Isso inverte a regra atual de `BuildApiMessages` (linha 58).
- **Histórico.** `KeepHistory = false` fica mais atraente, porque o `reasoning_content` reenviado
  infla o payload.

### Modo estrito (beta)

Com a URL `https://api.deepseek.com/beta` e `strict: true` em **todas** as funções, o servidor
garante que os argumentos seguem o schema. Isso impõe regras ao escrever os schemas:
- todo `object` precisa ter **todas** as propriedades em `required` e `additionalProperties: false`;
- `minLength`, `maxLength`, `minItems` e `maxItems` não são suportados.

Veja [referencias/deepseek-api.md](../referencias/deepseek-api.md#modo-estrito-beta).

## Etapas (checklist)

- [ ] **0. Referência e esqueleto.**
  - Preencher [referencias/servidores-locais-openai.md](../referencias/servidores-locais-openai.md).
  - Criar `AvaloniaCadEditor.Harness` e `AvaloniaCadEditor.Harness.Tests` no `.slnx`, com o README.
  - Escrever o **teste de arquitetura**: Application, Domain, Drafting e AiCore não referenciam o
    Harness, e Application não referencia Drafting.
- [ ] **1. Cliente genérico (AiCore), ainda sem tools.** Novos campos de `LlmOptions`, chave
  opcional, `max_tokens` omitível, `Thinking` explícito, timeout, handler injetável,
  `ListModelsAsync` e o parse de `reasoning`/`<think>`.
  *Testes:* ampliar `LlmClientRequestBodyTests` e criar `LlmClientHttpTests` com um handler falso
  (sem `Authorization`, parse dos dois campos de reasoning).
- [ ] **2. Perfis de endpoint no app.** `AiEndpointProfile`, a migração, o
  `JsonAiSettingsProvider` e a UI de perfis (modelo livre preenchido por `/v1/models`, sem
  sobrescrever o id). Essa etapa já entrega um **modelo local no chat de hoje**.
  *Testes:* migração v1→v2 numa pasta temporária; os testes atuais do chat continuam verdes.
- [ ] **3. Refatorações preparatórias**, sem mudança de comportamento: interface
  `ICadScriptChatSession`, `WorkingCopyEvaluator`, `ReferencePolylines` público e
  `MeshEdgeClassifier`.
  *Testes:* os testes atuais de chat e projeção continuam verdes.
- [ ] **4. `Harness/Views`: `ProjectionBasis` e `HiddenLineProjector`.**
  *Testes sintéticos:*
  - caixa na Frente: 4 visíveis e 0 ocultas;
  - furo passante em Y: círculo visível;
  - furo em X: 2 linhas ocultas;
  - duas caixas com oclusão parcial: aresta partida no U certo;
  - cilindro deitado: 2 silhuetas;
  - as 6 bases batem com `ViewBasis.For` e a Iso bate com `CameraState.GetViewMatrix`;
  - uma malha acima do orçamento é recusada.
- [ ] **5. `Harness/Inspection` e `WireframeReport`.**
  *Testes sem GUI* no padrão de `PlacaSuporteDrawingTests`:
  - a vista Frente da placa lista os furos com o Ø exato e o nome do fato;
  - o texto respeita o `max_chars`;
  - a saída é determinística;
  - `get_dimensions` é igual ao `DraftingReport.Format`;
  - um alvo inexistente devolve a lista de alvos.
- [ ] **6. Tool calling no cliente (AiCore).** `tools`, `tool_calls`, `finish_reason`,
  `ChatMessage.ToolCalls`/`ToolCallId` e o reenvio do `reasoning_content` com tools.
  *Testes:* corpo da requisição com `tools`; `arguments` como string e como objeto; id gerado.
- [ ] **7. `Harness/Tools`, `AgentLoop` e `HarnessChatSession`, com as ferramentas v1.**
  *Testes* com um `SendDelegate` roteirizado (uma sequência de `tool_calls`):
  - a edição sai num único `Changes`;
  - `DeleteTurn` limpa as mensagens de tool;
  - o limite de passos encerra o laço;
  - um 400 que cita "tools" degrada para `Text`;
  - o bloco ```` ```tool ```` é executado.
- [ ] **8. Papéis e Inspetor.** `harness_settings.json`, pré-inspeção e `inspect(question)`.
  *Testes:* o Inspetor recebe o `LlmOptions` do perfil dele e não vê as ferramentas de edição.
- [ ] **9. Medição no [PromptEval](../../AvaloniaCadEditor.PromptEval/README.md).** O PromptEval
  passa a referenciar o Harness e ganha `--endpoints`, `perfil:modelo`, `--tools` e `--inspector`.
  Comparar acerto, tokens, latência e custo em três cenários: só DeepSeek, só local, e DeepSeek com
  Inspetor local.
- [ ] **10. UI do chat.** As chamadas de ferramenta aparecem de forma recolhível, com o papel e o
  perfil. O trace sai do `HarnessChatSession` num tipo próprio do Harness, e a UI só o exibe.

Os testes rodam com `--artifacts-path` no scratchpad quando o app está aberto, e nenhum deles abre a
GUI sem permissão.

## Perguntas em aberto

- **Coordenadas do texto.** Folha (como no `DraftingReport`, com a linha "código (0,0,0) = folha
  (…)") ou direto no sistema do código? A proposta é a folha, para ficar coerente com as cotas.
  Medir no PromptEval se o modelo erra a conversão.
- **Oclusão pelos outros sólidos da cena** (montagens): entra na v2?
- **Arcos.** Um círculo que fica parte visível e parte oculto sai como polilinhas. Vale ajustar arcos
  já na v1?
- **Servidor local.** Ollama, LM Studio e llama.cpp têm campos de reasoning e suporte a tools
  diferentes. A degradação `Auto` cobre os três, mas a referência precisa dos detalhes confirmados.
- **Chave de API.** Ela fica em texto puro no JSON. Vale usar o nome da variável de ambiente como
  padrão?
- **Revisor.** Qual é o escopo, e ele roda sempre ou só com um perfil local?
- **Nome da instância para linha do código.** Os métodos de `PartSourceEditor` recebem `line`, então
  é preciso resolver o nome (ex.: `bloco_fixacao_a`) para o arquivo e a linha. Os candidatos são
  `RoslynCodeNavigator` e `SceneSyntaxIndex`. Lembrar do bug `ao-tentar-movimentar-o-snoopy-harp`,
  em que algumas instâncias não gravavam a posição.
- **Parâmetros opcionais no modo estrito.** Se tudo é `required`, um `anyOf` com `null` não aparece
  na lista de tipos suportados. É preciso testar.
- **Ferramentas no meio do histórico.** A Chat Completion API da DeepSeek **não** aceita
  `tool_call` fabricado no histórico, então contexto extra entra como mensagem `user`.
- **Temperatura.** Com o thinking mode ligado, `temperature` não tem efeito.
- **Não confirmados na documentação:** o limite de ferramentas por requisição, as chamadas
  paralelas e o `tool_choice`.

## Riscos

- **Precisão do HLR na malha.** As silhuetas de cilindros tesselados saem serrilhadas, e faces
  coplanares de booleanas e fillets geram falsos ocultos. A mitigação é o `eps` e a exclusão dos
  triângulos da própria aresta. Uma opção da vista pode usar `SuppressFillets`.
- **Tamanho do texto** em peças complexas. O orçamento precisa ser agressivo quando o contexto local
  é curto.
- **Contexto local truncado em silêncio.** O `num_ctx` padrão do Ollama é baixo. Isso deve estar
  documentado na referência e gerar um aviso pelo `usage`.
- **Modelos locais fracos em tools** podem fazer laços inúteis. A mitigação é o limite de tempo, o
  cache de chamada repetida e o protocolo `Text`.
- **Threads do OCCT.** O kernel das ferramentas roda fora da UI thread, em paralelo com o da UI. É
  preciso validar isso; se não for seguro, as ferramentas de geometria rodam em série.
- **Latência e custo na DeepSeek.** Cada passo reenvia o contexto inteiro. O cache de prefixo ajuda,
  e o Inspetor local existe para tirar as consultas pesadas do Editor pago.
- **Escolha da ferramenta.** Ferramentas demais ou mal descritas levam a uso errado. Começar com
  poucas e medir no PromptEval.
- **Regras do CadScript.** As edições precisam respeitar
  [CadScriptRules.cs](../../AvaloniaCadEditor.Application/Ai/CadScriptRules.cs). Exemplo: um sólido
  que depende de `FilletFeature` não pode ser girado nem espelhado como operando de booleana
  (linha 111). Nesse caso, `rotate_*` e `mirror_*` devem recusar com essa mensagem, em vez de
  deixar o kernel devolver uma malha vazia.
- **Erosão da separação.** É a tentação de pôr no Application algo que só o harness usa. O teste de
  arquitetura e a regra de promoção existem para barrar isso na revisão.
