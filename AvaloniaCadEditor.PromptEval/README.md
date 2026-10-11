# AvaloniaCadEditor.PromptEval

Compara perfis de pré-prompt (`Application/Ai/Prompts`) e modelos reexecutando os mesmos pedidos pela
mesma `CadScriptChatSession` do app (validação e auto-fix inclusos). Só o system prompt varia: o contexto de
cada round vem pronto do caso.

```
dotnet run --project AvaloniaCadEditor.PromptEval -- --profiles atual,enxuto,simples --models deepseek-flash,deepseek-v4-pro --repeat 3
dotnet run --project AvaloniaCadEditor.PromptEval -- --dry-run      # sem IA: valida casos e pipeline
dotnet run --project AvaloniaCadEditor.PromptEval -- --help
```

A chave vem de `DEEPSEEK_API_KEY`. O relatório sai em `eval-results/<data>/report.md`, e cada execução
completa (resposta, código, métricas, checks) em `runs.jsonl`.

## Casos

- **Fixture** — `Cases/fixtures/<nome>/case.json` + `files/` (a cena inicial). O contexto é montado pelo
  `AiContextBuilder` real. Campos: `instruction`, `description`, `project` (false = arquivo único),
  `entryFile`, `activeFile`, `select` (sólidos selecionados, pelo nome), `pin` (arquivos fixados no
  contexto) e `checks`. Os pares `*-sem-ref` / `*-com-ref` fazem o mesmo pedido sem e com peças de
  referência na cena.
- **Gravado** — o JSON salvo pelo botão 💾 do chat (`%AppData%/AvaloniaCadEditor/ai-cases`; use `--saved`
  para incluí-los ou copie para `Cases/recorded/`). Verificações opcionais em `<nome>.checks.json` ao lado.

## Checks

`expectNoCode`, `expectNewFiles` e `onlyChangeFiles` (globs), `mustNotRedeclare` (peças),
`maxPartParameters`, `minFeatures`, `maxFeatures`, `mustMatch` e `mustNotMatch` (regex no código
devolvido), `minOccurrences` (`{ "regex": n }`). "Passou" = compilou, executou e cumpriu todos.
