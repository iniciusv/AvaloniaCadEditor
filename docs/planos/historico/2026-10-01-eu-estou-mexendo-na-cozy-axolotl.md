# Plano: mover objeto na cena sem esperar 5–10 s (com análise de riscos)

## Contexto
Ao mover uma peça (MoveTool, 2º clique, ou campos de centro no Inspector), o fluxo é:
`CadViewModel.MoveTool_FeaturesMoved` → reescreve `Placement.At(...)` no código → `SceneScriptViewModel.RunCodeAsync()` →
recompila a cena inteira com Roslyn → executa → `MeshManager.ClearCache()` (→ `kernel.InvalidateAll()`) →
**reconstrói no OCCT todas as definições de peça** (TrilhoMgn9 = caixa − 24 prismas, TrilhoMgn15 = − 10, …) + tesselação.
Tudo síncrono na UI thread. Mesmo sem o `ClearCache`, nada seria reaproveitado: os caches são por `IFeature.Id`
(`Guid.NewGuid()` em [FeatureBase.cs:9](AvaloniaCadEditor.Domain/Features/FeatureBase.cs#L9)), e cada execução cria features novas.

Uma translação não muda a geometria local de nenhuma peça — só o `Placement` da instância. Objetivo: o custo de um move virar
~compilação + transformar malhas já prontas, sem mudar o resultado visível nem o código gerado.

Cada passo abaixo é independente, entra em commit próprio e pode ser revertido sozinho. A ordem é por (ganho ÷ risco).

---

## Passo 0 — Medir antes de mexer
**O quê:** ampliar o log `[Run]` ([SceneScriptViewModel.cs:547-575](AvaloniaCadEditor.UI/ViewModels/SceneScriptViewModel.cs#L547-L575)):
parse/compile/emit vs. execução (Stopwatch em `RoslynSceneEvaluator.EvaluateCoreAsync`), tempo acumulado em `BuildBoolean` vs `TessellateShape`
no kernel, e o tempo total do move em `MoveTool_FeaturesMoved` (reescrita + run + restauração de seleção).

**Riscos:** praticamente nulos (só `Console.WriteLine`). Único cuidado: não deixar Stopwatch em caminho por-frame.
**Decisão:** os números dizem se o Passo 1 (geometria) ou o Passo 4 (Roslyn) é o gargalo real. Se geometria for < 20% do tempo, o Passo 1 cai de prioridade.

---

## Passo 1 — Cache de malhas de peça entre execuções (maior ganho esperado)
**O quê:**
1. `FeatureFingerprint.Compute(IFeature)` (novo, em `AvaloniaCadEditor.Domain/Features/`): chave estrutural recursiva da **geometria gerada**
   (não do record `Part`) — tipo + todos os parâmetros geométricos + `IsEnabled` + filhos. Doubles serializados pelos bits exatos (`BitConverter.DoubleToInt64Bits`).
   Tipos não reconhecidos e `ImportedMeshFeature` → `null` = não cacheia (comportamento atual).
2. Kernel ([OpenCascadeGeometryKernel.cs:17-56](AvaloniaCadEditor.Infrastructure/OpenCascade/OpenCascadeGeometryKernel.cs#L17-L56)): `_definitionMeshCache` passa a ser por
   fingerprint + parâmetros de tesselação relevantes (deflexão, `ProfileSegments`).
3. Novo `IGeometryKernel.BeginSceneRebuild()` (nome a definir): descarta `_shapeCache` (por Id) mas mantém as malhas por fingerprint,
   removendo as não usadas na execução anterior. `SceneScriptViewModel.RunCodeAsync` (linha 567) passa a chamar
   um novo `MeshManager.ResetForNewScene()` que usa isso. **`ClearCache()` continua com a semântica atual (limpa tudo)** —
   é usado ao mudar `ProfileSegments` ([CadViewModel.cs:524](AvaloniaCadEditor.UI/ViewModels/CadViewModel.cs#L524)) e no Inspector (585).

**Riscos e mitigação:**
| Risco | Efeito | Mitigação |
|---|---|---|
| Fingerprint esquece um parâmetro (ex.: `WorkPlane` do prisma, direções do Box, arestas do fillet, ou um campo novo adicionado no futuro) | **Pior bug possível: geometria velha exibida sem erro nenhum**, silenciosamente | Lista explícita por tipo + **teste de guarda por reflexão**: para cada tipo de feature, enumera propriedades públicas e falha se alguma não for coberta pelo fingerprint (ou listada como ignorada: `Name`, `IsSelected`, `IsVisible`, `IsTransientTool`). Tipo novo sem suporte → `null` → sem cache (falha segura). |
| Mutação in-place de uma feature dentro da definição (Inspector edita a feature e chama `InvalidateFeatureAndDependents`) | Malha errada se o fingerprint fosse guardado no objeto | Fingerprint é **calculado na consulta**, nunca memorizado no objeto; mudou o valor → chave nova → reconstrói. |
| Mudar configurações de tesselação | Malhas na resolução antiga | Settings entram na chave; e `ClearCache()` continua limpando tudo nesse caminho. |
| Colisão de hash | Geometria trocada entre peças | Usar a string canônica completa (ou SHA-256 dela) como chave. Custo desprezível frente ao OCCT. |
| Doubles quase iguais (0.1+0.2 vs 0.3) | Só cache miss (reconstrói) — nunca resultado errado | Aceito: bits exatos garantem que só reaproveita o que é idêntico. |
| Memória crescendo com edições sucessivas | Vazamento lento | Eviction por geração (mantém só o usado na última execução). `OcctShape` (nativo) continua sendo descartado a cada execução, como hoje. |
| Booleana com operandos `PartInstance` girados (`BuildInstanceShape`) | — | Não muda: shapes continuam por Id e são refeitos; só a malha final da definição é reaproveitada. |
| `PureMathGeometryKernel` / testes que implementam `IGeometryKernel` | Quebra de compilação | Método novo com implementação vazia/default nesses kernels. |

---

## Passo 2 — Leitura de configurações por frame
**O quê:** [SolidFeatureRenderPass.cs:247](AvaloniaCadEditor.UI/Controls/Rendering/SolidFeatureRenderPass.cs#L247) cria um `JsonTessellationSettingsProvider`
(com `Directory.CreateDirectory`) e lê/deserializa o JSON **por feature, por frame**. Mudança: ler uma vez por chamada de `Render` (fora do loop de features),
usando o provider injetado.

**Riscos:** baixos. A cor de aresta alterada nas configurações passa a valer no frame seguinte, igual hoje.
Não proponho cache global/estático no `JsonTessellationSettingsProvider`: existem 3+ instâncias (App, CadViewModel, render pass) e um cache por
instância deixaria umas desatualizadas em relação às outras após `SaveSettings` — risco que não compensa. Ler 1×/frame já remove o grosso.

---

## Passo 3 — Feedback imediato no move (otimista)
**O quê:** em `MoveTool_FeaturesMoved` ([CadViewModel.cs:212-238](AvaloniaCadEditor.UI/ViewModels/CadViewModel.cs#L212-L238)), antes do `RunCodeAsync`,
trocar no `Sketch` cada **`PartInstance`** movida por uma instância nova com `Placement` + translação (sem tornar `Placement` mutável), invalidar só a malha
dela e redesenhar. Em seguida o run completo roda como hoje e substitui tudo. Features soltas (`Transformed`, booleanas) ficam fora: só esperam o run.

**Riscos e mitigação:**
| Risco | Efeito | Mitigação |
|---|---|---|
| Cena com posições derivadas (ex.: hotend posicionado por `carrinhoX.World(...)`) | Peça movida aparece na hora, dependente só "pula" quando o run termina | Aceitável (o run corrige). Documentar. |
| Run falha após a reescrita (erro de compilação/execução) | Tela mostra posição nova; erro aparece no editor | O código já foi reescrito com a posição nova, então tela e código coincidem. Undo funciona como hoje (restaura texto e roda). |
| Ganho real depende da UI conseguir pintar antes do run síncrono | Sem `await Dispatcher.Yield`/`Task.Yield` o frame otimista nunca é desenhado | Ceder um frame (`await Dispatcher.UIThread.InvokeAsync(..., Render)`) antes do run. Verificar no app. |
| Mais um caminho de mutação do `Sketch` | Seleção/árvore inconsistentes por instantes | Run completo sempre roda depois e reconstrói Sketch + árvore; seleção é restaurada por nome como hoje. |

**Observação:** se o Passo 1 sozinho levar o move para < ~0,5 s, este passo pode ser dispensado — é o de maior superfície de efeitos colaterais.

---

## Passo 4 — Roslyn incremental (só se o Passo 0 mostrar que compilação domina)
**O quê:** em [RoslynSceneEvaluator.cs:83-109](AvaloniaCadEditor.Infrastructure/Scripting/RoslynSceneEvaluator.cs#L83-L109), guardar a última `CSharpCompilation`
e usar `ReplaceSyntaxTree`/`AddSyntaxTrees`/`RemoveSyntaxTrees` só nos documentos alterados + `WithAssemblyName(novo Guid)`.

**Riscos e mitigação:**
| Risco | Efeito | Mitigação |
|---|---|---|
| Avaliações concorrentes (cena + chat de IA validando script — já citado em `Scene.cs`) compartilhando o cache | Compilação de um contaminando a do outro | Cache por "projeto" (chave = pasta da cena) e protegido por lock; o chat usa caminho sem cache. |
| Diferença de árvore (doc renomeado, path diferente) | Diagnóstico/linha apontando para arquivo errado; `CallerFilePath` errado (usado em `SourceOrigin` para reescrever o código!) | Comparar por path + texto; qualquer divergência estrutural → recria do zero. Teste: mover peça em arquivo secundário depois de editar outro. |
| Compilação antiga retém memória | Mais RAM | Guarda só a última. |
| `OptimizationLevel.Release` | Locais otimizados ao depurar o script; comportamento idêntico | **Não mudar** nesta rodada — ganho pequeno, não vale. |

---

## Fora do escopo (riscos altos demais agora)
- Tirar a avaliação/geometria da UI thread: o evaluator documenta que depende de `await` dentro da cena; OCCT e `Sketch` não são thread-safe.
- Pular o run após o move (só reescrever o código): quebraria cenas com posições derivadas e faria tela e código divergirem.
- Coalescer runs com `CancellationTokenSource`: o run é síncrono na UI thread, então quase não há sobreposição real hoje; adicionar isso cria risco de "run perdido" sem ganho medido.

## Arquivos principais
- Novo: `AvaloniaCadEditor.Domain/Features/FeatureFingerprint.cs` + testes em `AvaloniaCadEditor.Tests`
- `AvaloniaCadEditor.Infrastructure/OpenCascade/OpenCascadeGeometryKernel.cs`, `PureMathGeometryKernel.cs`, `Application/Interfaces/IGeometryKernel.cs`
- `AvaloniaCadEditor.Application/Services/SceneMeshManager.cs`, `ISceneMeshManager.cs`
- `AvaloniaCadEditor.UI/ViewModels/SceneScriptViewModel.cs`, `CadViewModel.cs`, `Controls/Rendering/SolidFeatureRenderPass.cs`
- (Passo 4) `AvaloniaCadEditor.Infrastructure/Scripting/RoslynSceneEvaluator.cs`

## Verificação
1. `dotnet build` + `dotnet test` (existentes + novos: fingerprint estável para definições iguais; muda ao alterar cada parâmetro; teste de guarda por reflexão;
   2ª avaliação com placement diferente não chama `BuildBoolean`; mudar `Comprimento` reconstrói).
2. No app, cena AiCore: mover `trilho_mgn9_x` e comparar o `[Run]` antes/depois (esperado: geometria de segundos → dezenas de ms a partir do 2º run).
3. Regressão de geometria: alterar `Comprimento: 250` → `260`, editar um furo dentro de `TrilhoMgn9.cs`, mudar `ProfileSegments` nas configurações —
   em todos a geometria tem que mudar na tela.
4. Undo/Redo após move; mover peça definida em arquivo secundário; move pelo Inspector; cena com erro de compilação após o move.
