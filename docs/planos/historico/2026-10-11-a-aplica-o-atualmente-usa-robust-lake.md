# Roslyn como RAG para código geral: análise e protótipo opcional

## Contexto
Pergunta: o rastreamento por Roslyn que a aplicação já faz (do sólido selecionado até o código que o produz) pode virar um RAG para bases de código C# quaisquer?
Resposta curta: sim. É o que se chama de *retrieval estrutural* (ou *code graph RAG*). Funciona melhor como complemento da busca por embeddings do que como substituto.

## O que já existe, em três níveis de precisão
1. **Nome, por arquivo**: [SceneSyntaxIndex.cs](AvaloniaCadEditor.Application/Ai/Context/SceneSyntaxIndex.cs) + [TypeDependencyResolver.cs](AvaloniaCadEditor.Application/Ai/Context/TypeDependencyResolver.cs). É um fecho transitivo "arquivo usa o nome X → entra o arquivo que declara X". Só sintaxe, nada compilado.
2. **Semântico, por instrução**: [SceneSlice.cs](AvaloniaCadEditor.Infrastructure/Scripting/SceneSlice.cs) e [RoslynPartSolidTracer.cs](AvaloniaCadEditor.Infrastructure/Scripting/RoslynPartSolidTracer.cs). É *program slicing* para trás com `SemanticModel`: atravessa funções locais e parâmetros do record.
3. **Montagem do contexto**: [AiContextBuilder.cs](AvaloniaCadEditor.Application/Ai/Context/AiContextBuilder.cs). Manda o código completo dos arquivos do recorte e só a assinatura dos que ficam fora (`FormatPartSignature`). É o mesmo padrão do "repo map" do Aider.

## O que muda para código geral
| Aspecto | CAD (hoje) | Código geral |
|---|---|---|
| Semente | seleção na viewport → arquivo e linha exatos | cursor, diff, stack trace, teste falhando ou **pergunta em linguagem natural** (o problema difícil) |
| Expansão | nomes / slice local | `SymbolFinder` (referências, implementações, derivadas, chamadores e chamados) via `MSBuildWorkspace` |
| Granularidade | arquivo inteiro | membro ou tipo; assinatura para nós distantes |
| Corte de hubs | o arquivo de entrada não é varrido | regra geral: não atravessar nós com fan-out alto (Program.cs, registro de DI, Utils) |
| Orçamento | implícito | ranking por distância no grafo ou PageRank, até um limite de tokens |

## Limites
- O grafo não enxerga DI, reflexão, strings de configuração, bindings de XAML nem eventos.
- Perguntas transversais ("onde tratamos autenticação?") não têm símbolo de partida. Para elas, é preciso BM25 ou embeddings para achar a semente.
- Carregar uma solução grande com `MSBuildWorkspace` custa alguns segundos. Isso se resolve mantendo o workspace em cache e atualizando um documento por vez, sem reindexar embeddings.
- Só funciona para C#. Outras linguagens precisariam de tree-sitter ou LSP.

## Protótipo sugerido (se quiser construir)
Um servidor MCP em .NET que expõe consultas Roslyn como ferramentas, para o LLM buscar o contexto sob demanda (retrieval agêntico) em vez de receber um contexto montado de antemão:
- `find_symbol(query)`: busca por nome e trecho de documentação; serve de semente.
- `get_definition(symbol)`, `find_references(symbol)`, `find_callers(method)`, `find_implementations(type)`
- `get_type_skeleton(type)`: só as assinaturas, como o `FormatPartSignature`.
- `slice_backward(file, line, variable)`: a generalização do `SceneSlice.Close` para o corpo de qualquer método.
- Reaproveitar: o padrão de fecho com fila de [TypeDependencyResolver.cs](AvaloniaCadEditor.Application/Ai/Context/TypeDependencyResolver.cs) e o passeio semântico de [RoslynPartSolidTracer.cs](AvaloniaCadEditor.Infrastructure/Scripting/RoslynPartSolidTracer.cs) (trocando os filtros de domínio `IFeature`/`AddFeature` por filtros genéricos).
- Projeto novo, separado do CAD (por exemplo `RoslynCodeRag/`), com `Microsoft.CodeAnalysis.Workspaces.MSBuild` e o SDK MCP para C#.

## Verificação (do protótipo)
- Apontar o servidor para esta própria solução e comparar o resultado de `find_callers` e `find_references` em `AiContextBuilder.Build` com o "Find All References" do VS.
- Registrar o servidor no Claude Code (`claude mcp add`) e fazer perguntas sobre o código, comparando com o uso de Grep puro.
