# AvaloniaCadEditor.PromptEval

Compara perfis de pré-prompt (`Application/Ai/Prompts`) e modelos reexecutando os mesmos pedidos pela
mesma `CadScriptChatSession` do app (validação e auto-fix inclusos). Só o system prompt varia: o contexto de
cada round vem pronto do caso.

```
dotnet run --project AvaloniaCadEditor.PromptEval -- --profiles atual,enxuto,simples --models deepseek-flash,deepseek-v4-pro --repeat 3
dotnet run --project AvaloniaCadEditor.PromptEval -- --dry-run      # sem IA: valida casos e pipeline
dotnet run --project AvaloniaCadEditor.PromptEval -- --local http://localhost:1234/v1 --models google/gemma-4-12b-qat --tools both --cases pino-no-furo
dotnet run --project AvaloniaCadEditor.PromptEval -- --help
```

Sem `--endpoints`, a chave vem de `DEEPSEEK_API_KEY`. O relatório sai em `eval-results/<data>/report.md`, e
cada execução completa (resposta, código, métricas, checks) em `runs.jsonl`.

## Endpoints, ferramentas e Inspetor

O PromptEval também compara provedores e modos do [harness](../AvaloniaCadEditor.Harness/README.md):

```
# DeepSeek e um modelo local (perfis do ai_settings.json), com e sem ferramentas
dotnet run --project AvaloniaCadEditor.PromptEval -- --endpoints deepseek,ollama --tools both

# o modelo do perfil escolhido por "perfil:modelo" (qwen3:8b sozinho é só o modelo, com a tag do Ollama)
dotnet run --project AvaloniaCadEditor.PromptEval -- --models ollama:qwen3:8b,deepseek:deepseek-flash --tools on

# Editor DeepSeek com um Inspetor local gratuito (pré-inspeção)
dotnet run --project AvaloniaCadEditor.PromptEval -- --endpoints deepseek --tools on --inspector ollama
```

- **`--endpoints`** usa os perfis de endpoint do `%AppData%/AvaloniaCadEditor/ai_settings.json`, os mesmos
  do chat.
- **`--tools off|on|both`** escolhe entre o chat de sempre, a `HarnessChatSession` (com ferramentas) ou
  os dois. No relatório, a variante com ferramentas aparece como `modelo@endpoint +tools`.
- **`--inspector`** liga o Inspetor, que precisa de `--tools on` ou `both`.
- **Colunas novas do relatório:** passos de ferramenta e tokens do Inspetor. O tamanho do system prompt
  inclui as instruções das ferramentas.
- **Endpoints locais** rodam uma chamada por vez, qualquer que seja o `--parallel`.

## Casos

- **Fixture** — `Cases/fixtures/<nome>/case.json` + `files/` (a cena inicial). O contexto é montado pelo
  `AiContextBuilder` real. Campos: `instruction`, `description`, `project` (false = arquivo único),
  `entryFile`, `activeFile`, `select` (sólidos selecionados, pelo nome), `pin` (arquivos fixados no
  contexto) e `checks`. Os pares `*-sem-ref` / `*-com-ref` fazem o mesmo pedido sem e com peças de
  referência na cena.
- **Geométricos** — pedidos cuja resposta certa é uma posição ou um encaixe, conferidos executando a
  cena (`solidBounds`/`solidPoints`). As peças vêm copiadas da cena de referência (`AiCore/Pecas`), e os
  pedidos são escritos como uma pessoa falaria, sem cotas mastigadas:

  | Caso | Mede |
  |---|---|
  | `pino-no-furo` | achar a posição de um furo numa peça espelhada, girada e deslocada |
  | `posicionar-carrinho-no-trilho` | montar uma peça em outra pela convenção dela (girada, centrada) |
  | `alinhar-placa-no-carrinho` | alinhar os furos de fixação de duas peças existentes |
  | `placa-base-carrinho` | peça nova com furação que bate com os M3 de outra |
  | `chapa-frontal-nema17` | peça nova com furação do motor e folga para o ressalto |
- **Gravado** — o JSON salvo pelo botão 💾 do chat (`%AppData%/AvaloniaCadEditor/ai-cases`; use `--saved`
  para incluí-los ou copie para `Cases/recorded/`). Verificações opcionais em `<nome>.checks.json` ao lado.

## Checks

`expectNoCode`, `expectNewFiles` e `onlyChangeFiles` (globs), `mustNotRedeclare` (peças),
`maxPartParameters`, `minFeatures`, `maxFeatures`, `mustMatch` e `mustNotMatch` (regex no código
devolvido), `minOccurrences` (`{ "regex": n }`). "Passou" = compilou, executou e cumpriu todos.

`solidBounds` é geométrico: executa a cena com as alterações e confere a caixa de um sólido no mundo,
pelo nome do sólido ou da variável. Serve para pedidos cuja resposta certa é uma posição:

```json
"solidBounds": [{ "name": "pino_furo_2", "min": [146, 9.5, 47.5], "max": [149, 12.5, 57.5], "tolerance": 0.1 }]
```

`null` numa coordenada de `min`/`max` = não conferir aquele lado (ex.: `"min": [null, null, 16]` só
exige a base em z = 16).

`solidPoints` confere encaixe sem impor uma solução: pontos do mundo que têm de estar no material do
sólido (`inside`) ou vazios (`outside`), como o eixo de um furo de fixação ou a folga de um ressalto.
Aceita qualquer resposta que encaixe (outra rotação, outra espessura):

```json
"solidPoints": [{ "name": "chapa_motor", "outside": [[65.5, -14.5, 41.5]], "inside": [[50, -11, 41.5]] }]
```

Antes de gravar um `solidBounds` ou `solidPoints`, confira a posição esperada executando a cena: monte uma cópia do
caso com a resposta certa já em `files/` e rode com `--dry-run --cases-dir <cópia>`. O caso
`pino-no-furo` foi conferido assim: a posição esperada é a do furo, e os erros típicos (esquecer o
espelho ou a rotação da instância) caem fora da tolerância.
