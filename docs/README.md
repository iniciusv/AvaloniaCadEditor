# Documentação do AvaloniaCadEditor

Ponto de entrada de toda a documentação da solution: o que já existe, onde fica e onde guardar o
que for novo. **Planos de implementação ficam em [`planos/`](planos/)**, e não só na conversa com
a IA ou em `~/.claude/plans/`, onde se perdem.

## Documentos de referência

| Documento | Para quê |
|---|---|
| [ARCHITECTURE.md](../ARCHITECTURE.md) | Mapa do código para quem vai **alterar o app**: onde cada coisa mora, invariantes, onde intervir |
| [GEOMETRY_KERNEL_HISTORY.md](../GEOMETRY_KERNEL_HISTORY.md) | Histórico e decisões da escolha do wrapper do OpenCASCADE (OCCT) |
| [AiCore/README.md](../AiCore/README.md) | Núcleo de comunicação com a LLM (`LlmClient`, `IAiProtocol`), sem dependências externas |
| [AvaloniaCadEditor.PromptEval/README.md](../AvaloniaCadEditor.PromptEval/README.md) | Avaliação de perfis de prompt e modelos (DeepSeek) com a mesma `CadScriptChatSession` do app |
| [CadScriptRules.cs](../AvaloniaCadEditor.Application/Ai/CadScriptRules.cs) | **Fonte única** das regras do dialeto CadScript (alimenta o system prompt, o `.cad/api.md` e o `AGENTS.md`) |
| [CadScriptApiReference.cs](../AvaloniaCadEditor.Application/Ai/CadScriptApiReference.cs) | Gera por reflexão a referência da API do CadScript |
| [AiCore/.cad/api.md](../AiCore/.cad/api.md) | API do CadScript gerada automaticamente (não edite) |
| [TestScene/AGENTS.md](../TestScene/AGENTS.md) | Instruções geradas para quem edita uma **cena** (o `CLAUDE.md` da cena só o importa) |

## Referências externas (copiadas para a execução dos planos)

| Documento | Usado por |
|---|---|
| [referencias/deepseek-api.md](referencias/deepseek-api.md): tool calls, modo estrito, thinking mode e regras de `reasoning_content` | [IA com ferramentas](planos/2026-10-16-ia-ferramentas-tool-calling.md) |

## Planos

### Convenção

- Um arquivo por plano: `planos/AAAA-MM-DD-tema-curto.md`.
- Cabeçalho com **Status** (`Proposto`, `Em andamento`, `Concluído` ou `Abandonado`), data e branch.
- Seções: objetivo, situação atual, arquitetura/etapas (checklist), perguntas em aberto, riscos e
  links para as `referencias/` usadas.
- Documentação externa consultada para criar o plano é resumida em `referencias/`, com a URL e a
  data da consulta, para não depender da web na hora de executar.
- Ao terminar ou abandonar um plano, atualize o **Status** aqui e no arquivo.

### Ativos

| Data | Plano | Status |
|---|---|---|
| 2026-10-16 | [Ferramentas para a IA (tool calling) no chat de CadScript](planos/2026-10-16-ia-ferramentas-tool-calling.md) | Proposto |

### Histórico (planos do modo de planejamento do Claude Code)

Copiados de `~/.claude/plans/` em 2026-10-16, **sem revisão**: o status de cada um não foi
conferido no código, e alguns podem estar superados. A data é a da última alteração do arquivo
original. Dois planos de outros projetos (Wishper.AudioEditor e JsonOperator) ficaram de fora.

| Data | Plano |
|---|---|
| 2026-09-27 | [Balões do chat de IA responsivos + ícone minimalista](planos/historico/2026-09-27-no-meio-dos-arquivos-splendid-shamir.md) |
| 2026-09-27 | [Botões de vista padrão (Topo / Frente / Lateral / Iso) + Perspectiva/Ortográfica](planos/historico/2026-09-27-planeje-alguns-bot-es-no-splendid-cloud.md) |
| 2026-09-27 | [Corrigir falso-positivo e deixar clara a detecção de quase-alinhamento (vermelho)](planos/historico/2026-09-27-seria-muito-dif-cil-ter-sharded-pixel.md) |
| 2026-09-28 | [Reconstrução "Perfil Base + Cortes" (substitui a Extrusão Multi-eixo)](planos/historico/2026-09-28-a-ferramenta-possui-uma-quirky-yeti.md) |
| 2026-09-28 | [Seleção precisa de linhas e regiões no esboço 2D](planos/historico/2026-09-28-ao-fazer-desenhos-dois-optimized-gem.md) |
| 2026-09-28 | [Ferramentas 2D: Extend (X), Polígono (G) e Offset (O)](planos/historico/2026-09-28-estou-no-modo-de-curious-knuth.md) |
| 2026-09-29 | [Transição suave entre níveis do grid (crossfade)](planos/historico/2026-09-29-ferramenta-atualmente-ela-tem-snazzy-wave.md) |
| 2026-09-29 | [Copiar, rotacionar e mover features soltas com `Transformed`](planos/historico/2026-09-29-quando-eu-movo-esse-resilient-thunder.md) |
| 2026-09-30 | [Canonizar cadeias de Placement + arquitetura de simplificação de código](planos/historico/2026-09-30-agora-quando-eu-movo-quiet-hamster.md) |
| 2026-09-30 | [Mover/girar uma ferramenta de booleana deve mover o corte, não criar um sólido novo](planos/historico/2026-09-30-as-opera-es-booleanas-est-o-smooth-simon.md) |
| 2026-09-30 | [Sweep (extrusão ao longo de caminho) — retas + arcos](planos/historico/2026-09-30-atualmente-fazemos-solidos-apenas-tranquil-milner.md) |
| 2026-09-30 | [Análise de desempenho: Run e Ctrl+Z / Ctrl+U (código → modelo renderizado)](planos/historico/2026-09-30-investigue-se-poss-vel-agile-cookie.md) |
| 2026-09-30 | [Pré-prompt da IA: suportar script standalone E arquivos de peça (Part)](planos/historico/2026-09-30-na-solution-eu-tenho-spicy-kahan.md) |
| 2026-10-01 | [mover objeto na cena sem esperar 5–10 s (com análise de riscos)](planos/historico/2026-10-01-eu-estou-mexendo-na-cozy-axolotl.md) |
| 2026-10-02 | [Ver e clicar nos sólidos-ferramenta (vazios) de uma peça](planos/historico/2026-10-02-quando-pe-o-a-ia-dazzling-penguin.md) |
| 2026-10-02 | [TAB para alternar entre sólidos sobrepostos + realce correto em modo Face](planos/historico/2026-10-02-quando-vou-fazer-uma-soft-graham.md) |
| 2026-10-02 | [Revisão da implementação "cortes antes do bevel" + correção do teste ineficaz](planos/historico/2026-10-02-tenho-o-seguinte-codigo-joyful-music.md) |
| 2026-10-03 | [Fix: Nome de propriedade cortado pelo campo numérico](planos/historico/2026-10-03-algumas-propriedades-est-o-ficando-binary-shell.md) |
| 2026-10-04 | [Remover alojamentos de porca órfãos em PlacaSuporteCarrinhoY](planos/historico/2026-10-04-analise-a-cena-de-graceful-liskov.md) |
| 2026-10-04 | [Análise do protocolo de IA + pré-prompt — e correções propostas](planos/historico/2026-10-04-analise-para-mim-o-frolicking-pillow.md) |
| 2026-10-04 | [Cantoneira de ligação: placa_suporte_carrinho_y_a ↔ extrusao_2020_sob_trilho_x](planos/historico/2026-10-04-preciso-fazer-um-novo-cached-meerkat.md) |
| 2026-10-04 | [Chat IA: toggles de histórico (multi-round) e reasoning](planos/historico/2026-10-04-quando-estou-usando-o-misty-lemur.md) |
| 2026-10-04 | [Rastrear e destacar as variáveis que alimentam um sub-sólido de peça](planos/historico/2026-10-04-seria-poss-vel-ao-tentar-snuggly-key.md) |
| 2026-10-04 | [ConeFeature — primitivo de tronco de cone](planos/historico/2026-10-04-una-ferramenta-eu-tenho-nifty-melody.md) |
| 2026-10-05 | [Cilindro → cone pelas propriedades](planos/historico/2026-10-05-a-funcionalidade-de-cone-cheeky-alpaca.md) |
| 2026-10-06 | [Conexão em Y para mangueira 5 mm — transição suave por varredura](planos/historico/2026-10-06-pedi-a-ia-para-cuddly-breeze.md) |
| 2026-10-07 | [Nova aba: "Gravação Automática" (auto-record + auto-avançar)](planos/historico/2026-10-07-analise-a-aplica-o-gostaria-adaptive-sedgewick.md) |
| 2026-10-07 | [Revisão do modo wireframe — problemas encontrados e correções](planos/historico/2026-10-07-planeje-um-modo-de-expressive-fairy.md) |
| 2026-10-08 | [Controle de densidade das cotas + peças aninhadas como componentes](planos/historico/2026-10-08-na-ferramenta-s-o-usadas-dreamy-oasis.md) |
| 2026-10-08 | [Furo com porca como peça reutilizável (sub-peça usada como vazio)](planos/historico/2026-10-08-no-projeto-exemplo-temos-partitioned-church.md) |
| 2026-10-09 | [Sweep por pré-seleção: região como seção, arestas encadeadas como caminho](planos/historico/2026-10-09-ao-usar-a-ferramenta-sharded-harp.md) |
| 2026-10-09 | [Converter sólido solto em peça (`record : Part`)](planos/historico/2026-10-09-n-o-preciso-transformar-bright-crystal.md) |
| 2026-10-09 | [Correção arquitetural do Sweep](planos/historico/2026-10-09-sweep-correcao-arquitetural.md) |
| 2026-10-11 | [Roslyn como RAG para código geral: análise e protótipo opcional](planos/historico/2026-10-11-a-aplica-o-atualmente-usa-robust-lake.md) |
| 2026-10-11 | [Mover não grava a posição de algumas instâncias (bloco_conexao_placa_carrinho_y_a, bloco_ficacao_belt_vao_correia)](planos/historico/2026-10-11-ao-tentar-movimentar-o-snoopy-harp.md) |
| 2026-10-11 | [Realçar ocorrências do texto selecionado no editor de código](planos/historico/2026-10-11-no-editor-de-codigo-effervescent-badger.md) |
| 2026-10-11 | [Face = plano: bevel só na face selecionada, e osnap no plano da face](planos/historico/2026-10-11-quando-eu-mandei-aplicar-buzzing-cascade.md) |
| 2026-10-11 | [Bevel incompleto no topo do BlocoFicacaoBeltVaoCorreia](planos/historico/2026-10-11-tentei-re-aplicar-bevel-robust-thimble.md) |
| 2026-10-16 | [Referências automáticas no plano de trabalho (faces coplanares + corte dos sólidos)](planos/historico/2026-10-16-muitas-vezes-quando-eu-woolly-summit.md) |
