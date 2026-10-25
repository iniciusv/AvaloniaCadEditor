# Pré-prompt da IA: suportar script standalone E arquivos de peça (Part)

## Context
O chat de IA do app só conhece o modo "script standalone". Ao pedir "crie um nema17" com `Pecas/Nema17.cs` aberto,
a IA respondeu com um script solto (`var ...; AddFeature(...)`), que é o formato errado para um arquivo de peça.
Causas encontradas (todas no fluxo do chat in-app; o AGENTS.md para IAs externas já descreve peças corretamente):

1. `CadScriptProtocol.GetSystemPrompt()` ([CadScriptProtocol.cs:58-96](AvaloniaCadEditor.Application/Ai/CadScriptProtocol.cs)) diz
   "NUNCA use class/struct/record", "o script contém APENAS statements top-level", exige `AddFeature` e "SCRIPT COMPLETO da cena inteira".
   Isso proíbe exatamente o que um arquivo de peça precisa (`record ... : Part`). A seção PEÇAS do
   `CadScriptApiReference` ([:74-87](AvaloniaCadEditor.Application/Ai/CadScriptApiReference.cs)) contradiz essas regras, e as 4 `Examples` são todas standalone.
2. `CadScriptContext(CurrentCode, SelectedFeatureName, ErrorFeedback)` não diz qual arquivo está ativo nem se é cena ou peça,
   e a IA não vê os demais arquivos (tipos de `Pecas/`, `Cena.cs`). `AiChatViewModel.SendAsync` ([:244](AvaloniaCadEditor.UI/ViewModels/AiChatViewModel.cs)) passa só `_scriptEditor.GeneratedCSharpCode` (arquivo ativo).
3. `CadScriptValidator.Validate` ([CadScriptValidator.cs:45-56](AvaloniaCadEditor.Application/Ai/CadScriptValidator.cs)) rejeita `TypeDeclaration` e exige `AddFeature`;
   e `CadScriptChatSession` ([:113-115](AvaloniaCadEditor.Application/Ai/CadScriptChatSession.cs)) valida/avalia o código isolado com `_evaluator.EvaluateAsync(code)`.
   Um arquivo de peça válido seria rejeitado ou não avaliável sozinho → o auto-fix empurra a IA de volta para o formato script.

## Mudanças propostas

### 1. Contexto com o "tipo de arquivo"
- `CadScriptContext`: adicionar `CadFileKind Kind` (`StandaloneScript`, `SceneEntry`, `PartFile`), `string? FileName` e
  `IReadOnlyList<string>? PartSummaries` (assinaturas dos `Part` existentes, ex.: `Placa(double Largura, double Comprimento, double Espessura = 5)`).
- `AiChatViewModel.SendAsync`: preencher a partir de `SceneScriptViewModel` (`ActiveFile`, se há `_project`, se é o `_entryFile`;
  arquivo em `Pecas/` ou sem top-level statements ⇒ `PartFile`). Sem projeto ⇒ `StandaloneScript`.

### 2. System prompt em dois modos (`CadScriptProtocol`)
- Tornar `GetSystemPrompt()` dependente do modo (ou separar em base comum + bloco do modo), mantendo a API gerada (`CadScriptApiReference.Build()`).
  Como o histórico é iniciado com o system prompt (`ResetHistory`), a orientação de modo vai na MENSAGEM DO USUÁRIO (`BuildUserMessage`: cabeçalho
  `### ARQUIVO ATIVO` com nome + tipo + regra do tipo), pois o modo muda entre turnos.
- Regras do modo **Script/Cena**: as atuais (statements, `AddFeature`, sem tipos), mais: "para usar uma peça existente chame `Add(new Peca(...), at: Placement.At(...))`; não redefina a peça aqui".
- Regras do modo **Peça**: "o arquivo contém apenas `public sealed record Nome(params...) : Part` (pode ter `using`? não — usings são globais), `Build()` em coordenadas locais com origem 0,0,0,
  NÃO chama `AddFeature`, devolve UMA IFeature (união/diferença final); parâmetros com valor padrão e nomes significativos em PT; sem top-level statements."
  Responder com o ARQUIVO COMPLETO.
- Reescrever a abertura e o bloco "REGRAS OBRIGATÓRIAS" para não afirmar que o script é sempre top-level; o formato de resposta passa a dizer "arquivo completo".
- Adicionar few-shots de peça (ex.: Placa; peça composta com furos via `CreateDifference` + `IsTransientTool`; e uma cena usando `Add`). Manter `Examples` standalone.
- Escolher: decidir se a IA deve pedir/criar peça nova quando o usuário está na cena e pede "crie um nema17" (recomendado: no modo Cena, responder
  criando o record para um arquivo `Pecas/X.cs` em bloco separado — ver item 4; primeira versão: apenas orientar a pedir para abrir/criar o arquivo da peça).

### 3. Validação por modo
- `CadScriptValidator.Validate(code, CadFileKind kind)`: no modo Peça exigir exatamente um `record` que herda de `Part`, com `Build()`, sem
  top-level statements, sem `AddFeature`; no modo Script manter as regras atuais (mensagem de erro sugerindo mover tipos para `Pecas/`).
- `CadScriptChatSession`: para `PartFile`, avaliar de verdade compondo o projeto: `_evaluator` com os documentos atuais do projeto, substituindo o arquivo ativo
  pelo código da IA (há `ScriptDocument`/`CurrentDocuments()` em `SceneScriptViewModel` [:844-862]) e uma cena mínima que faz `Add(new Peca())`
  (instanciar com os valores padrão) para forçar `Build()`. Se isso for grande, primeira versão: só validação sintática + checagem do `Build`.
- `AiChatViewModel`/`ApplyExternalCode`: sem mudança de semântica (substitui o arquivo ativo), mas o texto "Valid script (N feature(s))" deve ficar "Peça válida" no modo Peça.

### 4. IAs externas (AGENTS.md) — pequenos reforços em `SceneProjectTemplate.BuildAgentsFile`
- Adicionar regra explícita: "Para criar um objeto/sólido reutilizável (motor, parafuso, etc.), crie um arquivo em `Pecas/` com `record : Part`; NUNCA escreva
  `var x = new BoxFeature...; AddFeature(x)` em `Pecas/`. Arquivos em `Pecas/` não chamam `AddFeature`; só `Cena.cs` o faz/usa `Add`."
- Ao criar peça nova sem arquivo aberto, use `SceneProjectTemplate.CreatePartFile` como modelo de estrutura. Incluir pequeno exemplo de peça composta (corpo + furos + eixo).
- Observação: `.cad/api.md` é gerado de `CadScriptApiReference.Build()`; a seção PEÇAS lá deve ganhar o exemplo composto e a nota
  "Instâncias (PartInstance) não podem ser operandos de BooleanFeature; combine features DENTRO de `Build()`".
- Nota: `AGENTS.md` só é escrito em `Create`; cenas existentes (como TestScene) não são atualizadas. Decidir se `RefreshGeneratedFiles` também atualiza o bloco do AGENTS.md
  (marcadores de versão) ou se aceitamos atualização manual.

### 5. Testes (seguir padrão existente)
- `Tests/Ai/CadScriptProtocolTests`: prompt/mensagem de usuário contém as regras do modo correto e o nome do arquivo.
- `CadScriptProtocolExamplesTests`: novos exemplos de peça compilam e `Build()` executa (hoje só testa standalone via `RoslynSceneEvaluator`).
- `CadScriptValidatorTests`: peça válida aceita em modo Peça e rejeitada em modo Script; script solto rejeitado em modo Peça com mensagem orientando.
- `PartTests`/`SceneProjectTests`: AGENTS.md gerado contém a nova regra.

## Arquivos críticos
- `AvaloniaCadEditor.Application/Ai/CadScriptProtocol.cs`, `CadScriptContext.cs`, `CadScriptValidator.cs`, `CadScriptChatSession.cs`, `CadScriptApiReference.cs`
- `AvaloniaCadEditor.Application/Scenes/SceneProjectTemplate.cs`
- `AvaloniaCadEditor.UI/ViewModels/AiChatViewModel.cs`, `SceneScriptViewModel.cs` (expor tipo do arquivo ativo)
- Testes em `AvaloniaCadEditor.Tests/Ai/` e `Tests/Scenes/`

## Verificação
1. `dotnet test` (novos testes + exemplos few-shot compilam).
2. No app, abrir TestScene, abrir `Pecas/Nema17.cs`, pedir "crie um motor nema 17 com eixo e furos": a resposta deve ser um `record Nema17 : Part` com `Build()`, aprovada pela validação, e ao aplicar, `Cena.cs` com `Add(new Nema17())` funciona.
3. Em `Cena.cs` pedir "coloque dois Nema17 lado a lado": usa `Add` com `Placement`, sem redefinir a peça.
4. Sem projeto (script standalone): comportamento atual inalterado (testes existentes passam).
5. Mensagem de erro de auto-fix não empurra a IA para o formato errado (caso peça com `AddFeature`).
