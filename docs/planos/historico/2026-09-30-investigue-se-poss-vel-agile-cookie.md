# Análise de desempenho: Run e Ctrl+Z / Ctrl+U (código → modelo renderizado)

## Contexto
Apertar **Run** às vezes demora para o modelo aparecer, e **Ctrl+Z / Ctrl+U** (desfazer/refazer) travam a UI.
Os três caminhos acabam no mesmo lugar: `SceneScriptViewModel.RunCodeAsync()`
([SceneScriptViewModel.cs:532](AvaloniaCadEditor.UI/ViewModels/SceneScriptViewModel.cs#L532)). Undo/Redo
([SceneScriptViewModel.cs:1166-1182](AvaloniaCadEditor.UI/ViewModels/SceneScriptViewModel.cs#L1166)) trocam o texto
e reexecutam a cena inteira, igual ao Run. Por isso "travar" e "demorar" têm a mesma causa.

Você pediu **só análise** (causas, opções e riscos para a robustez). Nada será implementado agora.

## O que acontece hoje em um Run (tudo na UI thread)

1. **Compilação Roslyn**: `RoslynSceneEvaluator.EvaluateCoreAsync`
   ([RoslynSceneEvaluator.cs:89](AvaloniaCadEditor.Infrastructure/Scripting/RoslynSceneEvaluator.cs#L89)): parse de todos
   os arquivos → `CSharpCompilation` → `Emit` (PE + PDB) → carrega num ALC colecionável → executa → `Unload`.
   O comentário na linha 50 confirma: roda **síncrono na thread chamadora**, ou seja, a UI congela durante
   o parse, o bind, o emit e a execução.
   - A primeira execução é "fria": JIT do Roslyn e leitura dos metadados de ~160 assemblies do runtime (o `Lazy`
     de `References`). Costuma levar de 1 a 3 s. As seguintes ficam na casa de 100 a 500 ms.
   - `OptimizationLevel.Debug` + PDB portátil: o PDB serve para achar a linha dos erros de runtime (`FindUserFrame`).
2. **Descarte total do cache geométrico**: `MeshManager.ClearCache()` → `_geometryKernel.InvalidateAll()`
   ([SceneMeshManager.cs:24](AvaloniaCadEditor.Application/Services/SceneMeshManager.cs#L24),
   [OpenCascadeGeometryKernel.cs:30](AvaloniaCadEditor.Infrastructure/OpenCascade/OpenCascadeGeometryKernel.cs#L30)).
   Como toda feature ganha um `Guid.NewGuid()` novo a cada execução
   ([FeatureBase.cs:9](AvaloniaCadEditor.Domain/Features/FeatureBase.cs#L9)), o cache por `Id` nunca serve entre runs:
   **cada Run reconstrói e tessela no OpenCascade todos os sólidos da cena**, inclusive booleanas e fillets
   (que são o trecho mais caro), mesmo quando só uma linha mudou.
3. **Trabalho duplicado depois da avaliação**
   ([SceneScriptViewModel.cs:565-567](AvaloniaCadEditor.UI/ViewModels/SceneScriptViewModel.cs#L565)):
   - `ModelTree.RebuildTree()` roda, depois `RefreshProfiles()` chama `RefreshSolidMesh()` + `RebuildTree()`
     ([CadViewModel.cs:793-803](AvaloniaCadEditor.UI/ViewModels/CadViewModel.cs#L793)), e em seguida
     `RefreshSolidMesh()` roda de novo.
   - No total são **3× RebuildTree** (a árvore é recriada do zero, com ícones e handlers) e **2× BuildCompositeMesh**.
     O segundo BuildCompositeMesh sai do cache, mas ainda copia todos os vértices e índices para novas listas e arrays.
   - `RefreshProfiles` também roda `ComputePlanarArrangement` + `RecognizeProfiles` sobre as entidades do sketch.
   - `BuildCompositeMesh` concatena as malhas com `List.AddRange` + laços item a item, sem pré-alocar.
4. `RefreshPartTypes()` faz mais um parse de todos os documentos (`PartSourceEditor.FindPartTypes`).
5. No modo projeto, `SaveDocumentsToDisk` grava os arquivos alterados, e o watcher ignora as próprias gravações.

**Ordem provável de custo** (a confirmar com medição): tessela/booleanas no OCCT (cresce com a cena) >
compilação Roslyn (principalmente a fria) > árvore ×3 + composição ×2 > o resto.

## Oportunidades, com ganho × risco

| # | Otimização | Ganho esperado | Risco para a robustez |
|---|---|---|---|
| 0 | **Medir primeiro**: `Stopwatch` por etapa (parse/emit/execução, tessela, árvore, composição) e log no console/status | Diz onde o tempo realmente vai | Nenhum |
| 1 | **Remover a duplicação** em `RunCodeAsync`: 1× RebuildTree, 1× BuildCompositeMesh (ex.: `RefreshProfiles` sem refazer malha/árvore quando chamado pelo Run) | Médio (UI), sobretudo com muitas features | **Baixo**. Único cuidado: outros chamadores de `RefreshProfiles` (ferramentas de sketch, VCB, Delete) continuam precisando da versão completa. Fazer isso com um parâmetro ou um método interno, sem mudar o contrato atual |
| 2 | **Aquecer o Roslyn na inicialização** (compilar uma cena vazia em background) | Elimina a demora do 1º Run | **Baixo**. Custa CPU/memória no startup e precisa ser thread-safe (só toca o `Lazy` estático, que já é) |
| 3 | **Pré-alocar** arrays em `BuildCompositeMesh` / `BuildSolidMesh` (somar os tamanhos e usar `Array.Copy` com offset) | Pequeno a médio em cenas grandes | **Baixo**. Lógica local, fácil de cobrir com teste |
| 4 | **Compilar/executar fora da UI thread** (`Task.Run` no evaluator) e aplicar o resultado na UI | UI não congela mais durante a compilação | **Médio**. (a) Há chamadores que não aguardam: `RunCodeCommand.Execute(null)` em Undo/Redo, Delete, `CodeGenerated`, `BooleanTool`, `ReconstructMesh`, e 7 usos em `DeleteBehaviorTests`, que leem a cena logo depois contando com término síncrono. Precisam virar `await`. (b) Runs concorrentes (Ctrl+Z repetido): é preciso cancelar/descartar os resultados antigos (token + número de geração) senão uma execução velha pode sobrescrever a nova. (c) `SceneHost.Begin` usa estado ambiente: confirmar que é `AsyncLocal`/thread-static e não global. (d) O código do usuário passa a rodar numa thread de pool: não pode tocar objetos da UI (hoje já não deveria) |
| 5 | **Tessela fora da UI thread** (BuildCompositeMesh em background, com a UI só trocando `SolidMesh`) | Grande em cenas pesadas: a UI continua respondendo | **Médio-alto**. O `OpenCascadeGeometryKernel` usa `Dictionary` sem lock e é chamado também por raycast/seleção/preview na UI → precisa de um único "dono" (fila serial/lock) ou de kernel isolado por execução. Objetos OCCT nativos com `Dispose` concorrente podem **derrubar o processo** (não é só exceção). É preciso também garantir que seleção/raycast não usem malha de uma cena já trocada |
| 6 | **Cache de geometria entre execuções por conteúdo** (chave = assinatura dos parâmetros da feature em vez do `Guid`) | O maior ganho em Run/Undo/Redo: só o que mudou é reconstruído | **Alto**. (a) Toda feature precisaria de uma assinatura estável e **completa**: se esquecer um campo, aparece geometria velha na tela (bug silencioso, o pior tipo para CAD). (b) Booleanas/fillets/transformadas dependem da assinatura dos filhos (hash recursivo). (c) Tipos `Part` vêm do assembly colecionável: usá-los como chave impede o `Unload` (vazamento), então a chave precisa ser serializada (nome do tipo + valores). (d) É preciso política de despejo para não acumular `OcctShape` nativos. Variante mais segura: cachear só `PartInstance.Definition` (records já têm igualdade por valor, e `SceneBuilder.GetOrBuildDefinition` já deduplica dentro de um run), com chave = nome completo do tipo + valores dos parâmetros |
| 7 | Pular recompilação se o texto não mudou (hash dos documentos) | Útil só em Run repetido sem edição | Baixo, mas o ganho é raro. Cuidado com cenas que dependem de estado externo (arquivos, aleatório) |
| 8 | `OptimizationLevel.Release` / sem PDB | Pequeno no emit | **Médio**: perde a linha exata dos erros de runtime (`FindUserFrame` depende do PDB). Não recomendado |
| 9 | Reusar a compilação (`compilation.ReplaceSyntaxTree` só no arquivo alterado) | Pequeno a médio em projetos com muitos arquivos | Baixo a médio; ganho limitado porque o bind/emit continua completo |

## Recomendação (quando for implementar)
- **Fase A (baixo risco, fazer primeiro)**: #0 medir → #1 remover duplicação → #2 aquecer Roslyn → #3 pré-alocar.
  Não muda a semântica, e os testes existentes cobrem.
- **Fase B (médio risco, depois de medir)**: #4 compilação em background com cancelamento por geração,
  convertendo antes os chamadores `Execute(null)` para `await RunCodeAsync()`.
- **Fase C (só se a medição mostrar OCCT dominante)**: começar pela variante segura de #6 (cache de definição de
  `Part` por valor). #5 e o #6 completo só com testes dedicados de concorrência e invalidação.

## Arquivos críticos
- [SceneScriptViewModel.cs](AvaloniaCadEditor.UI/ViewModels/SceneScriptViewModel.cs): `RunCodeAsync`, `Undo`, `Redo`
- [CadViewModel.cs](AvaloniaCadEditor.UI/ViewModels/CadViewModel.cs): `RefreshProfiles`, `RefreshSolidMesh`, chamadores de `Execute(null)`
- [RoslynSceneEvaluator.cs](AvaloniaCadEditor.Infrastructure/Scripting/RoslynSceneEvaluator.cs)
- [SceneMeshManager.cs](AvaloniaCadEditor.Application/Services/SceneMeshManager.cs), [OpenCascadeGeometryKernel.cs](AvaloniaCadEditor.Infrastructure/OpenCascade/OpenCascadeGeometryKernel.cs)
- [Scene.cs](AvaloniaCadEditor.Domain/Scripting/Scene.cs) (`SceneBuilder.GetOrBuildDefinition`)

## Verificação (para as fases futuras)
- Medições com Stopwatch antes/depois em uma cena pequena e numa pesada (muitas instâncias + booleanas), com Run frio e quente.
- `dotnet test` completo (em especial `SceneCompilationTests`, `DeleteBehaviorTests`, `PartEditingTests`, `SceneEditingTests`).
- Manual: Run, Ctrl+Z/Ctrl+U repetidos rapidamente, mover/girar/copiar, seleção e raycast logo após o Run, e erro de runtime ainda apontando a linha certa.
