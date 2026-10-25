# AvaloniaCadEditor

Editor CAD em que **a cena é um programa C#** (CadScript): o código é a fonte da verdade e a UI
reescreve o fonte em vez de mexer num modelo em memória.

## Antes de mexer no código

1. Ache a linha da tarefa em **"§4 Onde intervir"** do mapa abaixo: ela diz o arquivo e a armadilha.
2. Se a mudança cruza projetos, confira a regra de dependência (§2). A seta nunca inverte, e o
   Harness só é conhecido pela UI (composição) e pelo PromptEval.
3. Feature nova toca **7 lugares** (§5.1). Esquecer um não dá erro, a feature só some.
4. O detalhe de cada projeto fica no README dele (AiCore, Harness, PromptEval). Comece pelo mapa e
   abra o README depois.

**Cena ≠ app.** Para editar uma *cena* (`AiCore/Cena.cs`, `AiCore/Pecas/**`, pastas de cena), as
regras são as do CadScript: `AGENTS.md` da cena e `.cad/api.md`. Este arquivo é para o *app*.

Planos de implementação vão em `docs/planos/` e entram no índice [docs/README.md](docs/README.md).

Ao criar, mover ou renomear um arquivo que o mapa cita, ou um projeto novo, atualize o
ARCHITECTURE.md no mesmo commit. O `ArchitectureDocTests` falha quando um caminho citado deixa de
existir ou quando um projeto da `.slnx` fica fora da §2.

## Mapa do código

@ARCHITECTURE.md
