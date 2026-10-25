# Skills do harness: modos de trabalho que se ligam e desligam

- **Status:** Implementado (v1)
- **Data:** 2026-10-18
- **Branch:** `feature/harness-ia`
- **Depende de:** [Harness de ferramentas para a IA](2026-10-16-ia-ferramentas-tool-calling.md)

## Objetivo

Dar ao Editor do harness **modos de trabalho** que o usuário liga e desliga, como as skills do Claude
Code. O primeiro é um "grill-me" ao contrário: antes de editar, a IA confere o pedido contra a geometria
e, se faltar uma decisão, pergunta em vez de chutar.

## Por que

Um pedido real ("motor NEMA do eixo Z, com fuso até uma porca no `tubo_travessa`, a base no chão") foi
rodado no DeepSeek Flash com ferramentas, sobre a cena de referência inteira e sem seleção:

| Variante | Resultado |
|---|---|
| Harness como está (2×) | Gastou os 15 passos abrindo vistas (6 vistas de cada extrusão reta) e terminou sem editar, pedindo confirmação de forma solta |
| Com as instruções de "questionar" no pedido (2×) | Parou antes de editar e respondeu com 6 perguntas citando nomes e cotas da cena |

As perguntas tinham a forma certa, mas metade das premissas estava errada. Uma rodada sugeriu apoiar o
motor no próprio tubo, que se move com o gantry. Nenhuma das duas achou o chão em z −280,7 (base das
extrusões deitadas `*_copia1`): propuseram −20, 0 ou −260,7. Daí as regras que entraram no texto da
skill: poucas consultas (`list_targets` já traz todas as caixas), separar o que é fixo do que se move, e
sugestões coerentes entre si.

## Desenho

- `Harness/Skills/HarnessSkill.cs` é um `record` com id, título, descrição e instruções: **só texto**,
  sem ferramenta nem código.
- `Harness/Skills/SkillRegistry.cs` traz o registro (`Default()` com `questionar` e `revisar`), a resolução
  de ids e a leitura do comando `/id` no começo do pedido.
- **Ligada** (`HarnessSettings.Skills`, gravada no `harness_settings.json`): as instruções vão no system
  prompt, depois do `HarnessPrompt.Tools`, e valem para a conversa toda.
- **Num pedido só:** `/questionar põe um motor...`. O comando sai do texto e as instruções vão na
  mensagem daquele turno, depois das notas do Inspetor. O Inspetor não as recebe.
- `HarnessChatSession.LastTurnSkills` diz o que valeu no turno, e o rastro do balão mostra
  "skills: questionar".
- **UI:** checkboxes no painel do harness (`SkillToggleViewModel`), desabilitados com o harness desligado.
- **PromptEval:** `skills` no `case.json`, `--skills` na linha de comando, `filesFrom` (variantes do mesmo
  pedido sem duplicar a cena) e o check `chatMustMatch` (regex no texto da resposta).

## Etapas

- [x] `HarnessSkill` + `SkillRegistry` (`questionar`, `revisar`, `ParseCommands`)
- [x] Sessão: skills ligadas no system prompt, `/id` por turno, `LastTurnSkills`
- [x] `HarnessSettings.Skills` + fábrica (ids desconhecidos ignorados, rótulo "skills: ...")
- [x] UI: checkboxes no painel e linha no rastro
- [x] PromptEval: `skills`, `--skills`, `filesFrom`, `chatMustMatch`
- [x] Casos `motor-z-gantry` (cena congelada + seleção) e `motor-z-gantry-questionar`, conferidos com
      `--dry-run` sobre uma resposta certa (base em −280,7: passa) e uma errada (−20: falha)
- [x] Testes: `Harness.Tests/Skills/SkillsTests.cs` e dois em `RolesTests`
- [ ] Medir os dois casos com mais modelos (DeepSeek Pro, um modelo local)

## Medição (2026-10-18, DeepSeek Flash, perfil `simples`, 2 repetições, com a seleção do caso)

| Caso | Passou | O que aconteceu |
|---|---:|---|
| `motor-z-gantry` | 0/2 (entendeu 1/2) | r1 editou: motor no centro, sob o tubo, com a **base em z −260,7** (topo das deitadas, onde começam as verticais) em vez de −280,7. Entendeu a montagem e errou só a cota. r2 bateu nos 15 passos sem editar e propôs o motor **em cima do tubo** (z 30), que se move com o gantry: não entendeu |
| `motor-z-gantry-questionar` | 2/2 | as duas citaram as `*_copia1` em z −280…−260 e separaram fixo de móvel. r1 fez boas perguntas, coerentes entre si. r2 levantou os fatos certos, mas sugeriu apoiar o motor no tubo (z 0 ou 30), contra o próprio levantamento |

Com a seleção, os dois modos passaram a achar as extrusões do chão, que antes ninguém achava. A
diferença que sobra está nas **sugestões**. O check `chatMustMatch` confere que as perguntas partem do
chão certo, mas não pega uma sugestão incoerente como a da r2: ainda é preciso ler a resposta.

**"Início das extrusões na parte mais baixa" admite duas leituras:** a base das deitadas (−280,7) ou
o topo delas, onde começam as verticais (−260,7, a que a r1 sem skill escolheu). O autor do pedido
confirmou a **base das deitadas, −280,7**, que é o que o `solidBounds` do caso exige. A r1 sem skill errou
ao escolher sozinha uma das duas leituras: é o tipo de decisão que o `questionar` deve levar ao usuário.

## Conceito × ajuste no PromptEval

"Passou ou não" junta erros de natureza diferente. A r1 acima montou o motor certo, no lugar certo, só
20 mm acima, e falhava igual à r2, que apoiou o motor numa peça móvel. O que importa medir primeiro é
o **entendimento do pedido e da cena**. A cota fina se corrige com um número.

- Cada check tem um nível (`CheckLevel`): `conceito` (o padrão) ou `ajuste`. Os geométricos aceitam
  `"level": "ajuste"`.
- `solidWithin` confere o conceito sem impor a cota: a caixa cabe numa região e tem um tamanho
  (orientação). No `motor-z-gantry`, "em pé, com o eixo sob o tubo, entre o chão e o tubo" é conceito, e
  "base em −280,7" é ajuste.
- O relatório mostra **Entendeu** ao lado de **Passou**, e o console mostra "entendeu (errou ajuste)".
- Conferido com `--dry-run` sobre quatro respostas: base em −280,7 passa; em −260,7 entendeu e errou
  ajuste; em −20 (extrusões Y) e em cima do tubo (z 30), não entendeu.

Ainda falta medir o conceito no **texto**: no `questionar`, uma sugestão incoerente (apoiar o motor no
tubo) passa nos checks. Isso pediria um juiz (outro modelo com uma rubrica) ou regex mais finos.

## Perguntas em aberto

- **Skill sob demanda pelo modelo:** hoje só o usuário ativa. Uma ferramenta `use_skill(id)` com a lista
  das descrições no prompt (divulgação progressiva, como no Claude Code) deixaria o modelo escolher, com
  um prompt menor. Fica para quando houver skills que não valham a pena ligar sempre.
- **Perguntas estruturadas:** a resposta do `questionar` é texto. A UI poderia mostrar cada pergunta com
  a sugestão pré-preenchida e mandar as respostas de volta num clique.
- **Excesso de vistas sem a skill:** o `HarnessPrompt` diz "olhe antes de escolher coordenadas" e o
  Flash entende "olhe tudo". A regra "as caixas do `list_targets` bastam para peças retas" está só na
  skill. Talvez valha para todos os turnos.
- **Skills sem ferramentas:** só existem no harness. O chat de sempre (`CadScriptChatSession`) não as tem.

## Riscos

- Renomear o id de uma skill desliga a skill para quem a tinha ligado (o id fica no JSON).
- Uma skill ligada sempre pesa em todo turno (tokens). O `questionar` pode atrapalhar pedidos simples,
  embora o texto mande seguir direto quando não há decisão em aberto.
