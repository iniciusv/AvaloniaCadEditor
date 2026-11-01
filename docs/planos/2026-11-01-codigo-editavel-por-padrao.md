# Código editável por padrão: modelos simples, recursos avançados só sob pedido

**Status:** Em andamento: etapas 1 a 4 implementadas, falta medir (etapa 5) · **Data:** 2026-11-01 · **Branch:** `feature/codigo-editavel`

## Objetivo

Fazer a IA gerar, **por padrão**, o código que as ferramentas de edição manual conseguem reescrever:
números literais nas posições, poucos parâmetros por peça e um sólido por declaração, sem laços,
funções locais, LINQ, atributos e cadeias de variáveis. Esses recursos continuam disponíveis, mas só
quando o pedido os pede explicitamente ("paramétrico", "repita N vezes", "com um parâmetro para…").

O motivo: a cena é um programa e a UI edita o fonte (ARCHITECTURE §1). Cada ferramenta manual
(inspector, mover/girar sólido de peça, revelar furo, copiar, juntar à peça, esboço como código) só
funciona bem sobre literais. Sobre código calculado ela recusa, desiste para um `.Transformed(...)`
ou deixa resíduo (`expr - 12.276393`).

## Situação atual

### O que a IA gera (medido em `AiCore/`, 37 peças + `Cena.cs`, em 2026-11-01)

| Recurso | Ocorrência | Peso nas ferramentas |
|---|---|---|
| `new Point3D(...)` com expressão em vez de literal | **311 de 360 (86%)** | Alto: o principal bloqueio |
| `Add(..., at:)` com coordenadas calculadas na `Cena.cs` | **71 de 72** | Médio |
| Parâmetros por peça | média 8,7; sete peças com 18 a 20 | Alto: o inspector lista todos, sem dizer qual move o quê |
| Sólidos com nome interpolado (`$"furo_{i}"`) | 15 | Alto onde ocorre: o sólido perde a ligação com a linha |
| Laços (`for`/`foreach`) | 12, em 6 arquivos | Baixo no total, mas trava as edições onde aparece |
| Funções locais | 5 | Baixo |
| Atributos `[Range]` | 12, quase todos em `ConexaoYAdaptador5mm` | Nenhum: só validam |

A regra de sólidos rastreáveis (§5.2) é violada por `MotorNema17`, `TrilhoMgn15`, `TrilhoMgn9`,
`BlocoConexaoMotor2020` e `SuporteHotendV6`, e nada acusa.

### Como cada ferramenta reage

| Ferramenta | Código literal | Código calculado ou em laço |
|---|---|---|
| Inspector: argumento e padrão do record (`PartSourceEditor.SetArgument`, `SetPartDefault`) | edita | recusa (`PartArgument.IsEditable = false`) |
| Revelar sólido de peça (`PartSourceEditor.FindToolDeclaration`) | abre a declaração | nome interpolado: não acha e abre o record |
| Mover/girar/espelhar sólido de peça (`TranslatePartSolid` e afins) | funciona | em laço não acha; com expressão, embrulha em `.Transformed(...)` e os números ficam escondidos |
| Mover coordenada (`RoslynCoordinateMutator`) | `20` vira `30` | `expr` vira `expr + 10`; só `expr ± literal` soma limpo |
| Mover instância (`MoveInstance`, `at:`) | soma no literal | funciona (`i * 150 + 20`), mas em laço move todas as cópias |
| Copiar instância (`CopyInstance`) | nome e variável próprios | em laço ou nome calculado: sem nome |
| Juntar sólido a uma peça (`RoslynPartMerger`) | reescreve os pontos mundo→local | um valor não literal: desiste e embrulha a raiz em `.Transformed` |
| Esboço como código (plano 2026-10-21) | edita | laço ou chamada em método recusa o lote inteiro |
| Seleção → linha (`FeatureOrigin`) | uma linha por sólido | em laço, todos os sólidos na mesma linha |

### O que já existe e por que não resolve

- **Perfil de prompt** (combo "Perfil de prompt" no chat: `atual`, `enxuto`, `simples`;
  `CadScriptPromptProfiles`). Vale para o chat do app, com e sem harness
  (`HarnessChatSession` usa `CadScriptProtocol.GetSystemPrompt()`). Limites:
  - o padrão é `atual`, que ensina o estilo calculado;
  - o `simples` limita a 3 parâmetros, mas o próprio exemplo dele calcula as posições
    (`Largura / 4`, `profundidade / 2 + espessura`) e nada diz sobre literais, laços de cálculo ou
    funções locais;
  - **não chega às IAs externas.** O `AGENTS.md` e o `.cad/api.md` de cada cena vêm de
    `CadScriptRules` via `SceneProjectTemplate`, não do perfil. É lá que está escrito que "variáveis,
    const, arrays, laços e funções locais podem ser usados para CALCULAR valores", e o exemplo
    `PartExampleBlocoQuatroFuros` ensina a derivar cada posição;
  - é só texto: nada confere o resultado.
- **Regra de sólidos rastreáveis**: existe só no prompt. `CadScriptValidator` é sintático e não a
  confere.
- **Skills do harness** (`SkillRegistry`): mecanismo pronto para ligar um texto no prompt por turno
  (`/id`) ou de vez. Serve para o opt-in do modo paramétrico.
- **Arquitetura de simplificação** (plano `historico/2026-09-30-agora-quando-eu-movo-quiet-hamster`,
  fase 2): desenhada e não implementada. Prevê `extract-repetition` (instruções repetidas viram laço),
  que vai **contra** esta direção.

## Decisões propostas

1. **Forma editável de uma coordenada:** um literal, ou `Parametro ± literal` quando a posição
   depende de uma dimensão do record (`new Point3D(Largura - 10, 10, 0)`). É exatamente o formato em
   que o `RoslynCoordinateMutator` soma o deslocamento no número. Nada de variáveis intermediárias
   para posição (`x1`, `entradaFurosPlacaX`).
2. **Parâmetros:** no máximo 3 por peça, só as dimensões principais. As outras medidas ficam literais.
   Peça de catálogo (MGN, NEMA, 2020) tem as medidas do catálogo literais e 1 ou 2 parâmetros
   (ex.: comprimento).
3. **Fora do padrão, liberados só sob pedido:** laços e LINQ (mesmo só para calcular), funções locais,
   pontos montados por cálculo, `[Range]`, `bool` de opção, ternários de configuração e `static class`
   de medidas. Vértices literais de `PrismFeature`/`SweepPath` continuam permitidos.
4. **Na cena:** variável só para valor usado por mais de uma instância ou por uma relação que o pedido
   pediu. Posição de uso único é `Placement.At(literais)`.
5. **Opt-in:** pedido explícito em texto e, no harness, a skill `/parametrico`.
6. **A regra de sólidos rastreáveis passa a ser conferida** (erro), não só ensinada.

## Etapas

### 1. Linter de editabilidade (Application)

- [x] `Application/Ai/CadScriptEditabilityLint.cs`: puro e sintático (Roslyn, sem compilar). Devolve
      achados (regra, linha, severidade, mensagem) e métricas.
- [x] **Erros** (a regra que já existe): sólido criado dentro de `for`/`foreach`/`while`, lambda ou
      LINQ; `Name` não literal ou interpolado; `Name` diferente da variável; nome repetido no arquivo.
- [x] **Avisos** (estilo): `Point3D`/`Vector3D` fora da forma editável; peça com mais de 3 parâmetros;
      função local; laço de cálculo; atributo; `bool` de opção.
- [x] Testes: um caso mínimo por regra, mais uma rodada sobre `AiCore/Pecas` que registra os números
      de hoje como linha de base (não como meta).
- Implementado: nome repetido em ramos exclusivos (`if`/`else`, retorno antecipado, `?:`) não conta, porque só
  um deles roda; `p.World(p => new Point3D(...))` não conta como coordenada; nome diferente da variável é aviso,
  não erro (o `FindToolDeclaration` acha o sólido pelo literal). Linha de base da `AiCore` medida pelo linter:
  377 de 394 sólidos rastreáveis, 45% das coordenadas editáveis, 12 sólidos em laço e 17 nomes calculados em
  cinco peças (`BlocoConexaoMotor2020`, `MotorNema17`, `SuporteHotendV6`, `TrilhoMgn15`, `TrilhoMgn9`).

### 2. Ligar o linter onde a IA recebe retorno

- [x] Chat: os **erros** entram no `CadScriptValidator.Validate`, e o `WorkingCopyEvaluator` já os
      devolve à IA para corrigir. Os **avisos** só voltam à IA no perfil `editavel`, para não mudar o
      comportamento dos outros perfis. Implementado em `EditabilityStyleFeedback`: uma rodada só, com os avisos
      **novos** (os que o arquivo já tinha não contam); se a reescrita falhar ou a IA recusar porque o pedido
      era paramétrico, vale a resposta anterior e a rodada sai do histórico. Vale também no harness.
- [x] `check` (`SceneCheckService`, `UI/Cli/CheckCommand.cs`): imprime erros e avisos. O exit code não
      muda com avisos; `--strict` faz erro de rastreabilidade sair com 1. É o retorno que chega às IAs
      externas, que já rodam `check . --geometry` pelo `AGENTS.md`. Mudança na implementação: por padrão os
      avisos de estilo só aparecem como contagem no resumo ("Editabilidade: ..."), porque uma cena antiga tem
      centenas e a saída do `check` é lida por IA a cada rodada; `--estilo` lista cada um (CAD0201). O CAD0200
      é sempre listado. O `AGENTS.md` gerado pede para corrigir CAD0200 nos arquivos mexidos.
- [x] PromptEval (`CodeMetrics`): % de coordenadas editáveis, sólidos rastreáveis, laços, funções
      locais e erros do linter, ao lado do `MaxPartParameters` que já existe.

### 3. Perfil `editavel`

- [x] `Application/Ai/Prompts/EditablePromptProfile.cs` + `EditablePromptRules.cs`, registrado em
      `CadScriptPromptProfiles.All`. Base: `LeanPromptRules`, mais o estilo das decisões 1 a 5.
- [x] Exemplos novos, todos na forma editável (o `CadScriptProtocolExamplesTests` compila e executa
      cada um). Ex.: chapa com `furo_1` em `new Point3D(10, 10, 0)` e `furo_2` em
      `new Point3D(Largura - 10, Comprimento - 10, 0)`.
- [x] Texto do opt-in: o que muda quando o pedido diz "paramétrico" ou "repita".

### 4. Skill `/parametrico` (Harness)

- [x] `Harness/Skills/SkillRegistry.cs` (`Default`): libera laços de cálculo, funções locais, mais
      parâmetros e relações entre peças, **mantendo** os sólidos rastreáveis.
- [x] Só existe com harness. Sem harness, o pedido em texto basta. Com o perfil `editavel`, a skill também
      desliga a rodada de estilo do turno.

### 5. Medir antes de mudar o padrão

- [ ] PromptEval: `atual` × `simples` × `editavel` nos casos de `PromptEval/Cases/fixtures`, com e sem
      ferramentas, em pelo menos dois modelos (DeepSeek e um local).
- [ ] Casos novos: um pedido de peça comum, conferindo que o linter não acusa nada; e um pedido
      paramétrico explícito, conferindo que o opt-in libera os recursos.
- [ ] Critério para virar padrão: os checks geométricos não pioram (mesma taxa, ±1 caso), pelo menos
      80% das coordenadas na forma editável e nenhum erro de rastreabilidade.

### 6. Tornar padrão (depois da etapa 5)

- [ ] `CadScriptPromptProfiles.Default` passa a ser `editavel`. O `atual` continua disponível e com o
      golden intacto. Quem já gravou `atual` no `ai_settings.json` continua nele.
- [ ] IAs externas: reescrever `CadScriptRules.TrackableSolids` (tirar a licença para laços e funções
      locais) e trocar `PartExampleBlocoQuatroFuros` por uma versão literal. Ver pergunta 1.
- [ ] Regerar `.cad/api.md` e `AGENTS.md` das cenas e atualizar o ARCHITECTURE.md (§4, linha "Mudar
      as regras do dialeto"; §5.2).

### 7. Fase 2, em plano próprio: achatar o código que já existe

- [ ] Comando "Simplificar peça": desenrola laços em declarações nomeadas e assa expressões em
      literais usando os valores da execução (as features trazem os valores e `FeatureOrigin` traz a
      linha). Encaixa como regras `unroll-loop` e `bake-expressions` na arquitetura de simplificação de
      2026-09-30, com a verificação de equivalência geométrica do orquestrador. Rever
      `extract-repetition`.
- [ ] `TranslatePartSolid` e afins: somar no `Point3D` quando ele já estiver na forma editável, em vez
      de embrulhar em `.Transformed(...)`.

## Perguntas em aberto

1. **Onde mudar o texto para as IAs externas.** Alterar `CadScriptRules` muda o prompt `atual` e
   quebra o golden. Além disso, `CadScriptRulesTests.SystemPrompt_UsaOsBlocosCompartilhados` usa
   `new CadScriptProtocol()`, ou seja, o perfil **padrão**: se o padrão virar `editavel`, ele precisa
   conter os blocos compartilhados literalmente, ou o teste passa a olhar o `atual`. Opções: (a)
   atualizar o golden de propósito; (b) criar um bloco novo `CadScriptRules.EditableStyle`, consumido
   por `AGENTS.md`, `.cad/api.md` e `editavel`, deixando o `atual` congelado.
2. ~~Avisos de estilo voltam à IA só no perfil `editavel` ou em todos?~~ Só no `editavel` (implementado).
3. As peças existentes da `AiCore` ficam como estão ou passam pelo "Simplificar peça" da fase 2?
4. O limite de 3 parâmetros vale também para peças de catálogo ou elas têm regra própria (decisão 2)?

## Riscos

- **Perder a intenção do projeto:** mudar `Largura` e o furo ficar para trás. A forma
  `Parametro ± literal` cobre o caso comum. Relações mais longas ficam para o modo paramétrico.
- **Arquivos maiores:** N furos viram N linhas. Já é o que a regra de rastreabilidade manda, e é o
  preço de cada furo ser editável sozinho.
- **Falso positivo no linter:** vértices de prisma calculados por motivo legítimo. Por isso o estilo
  é aviso, e só a rastreabilidade é erro.
- **Testes de trava:** golden do `atual`, `CadScriptRulesTests`, `CadScriptPromptProfilesTests` e
  `CadScriptProtocolExamplesTests` precisam continuar passando em cada etapa.

## Verificação (2026-11-01)

Compilado e testado num ambiente Linux sem o kernel OCCT nativo: os testes novos passam (linter, validador,
rodada de estilo no chat e no harness, `check`, perfis, `ArchitectureDocTests`) e nenhum teste que passava
antes passou a falhar. Os testes que dependem do OCCT e o projeto `UI` (Avalonia) não rodam nesse ambiente:
falta rodar `dotnet build AvaloniaCadEditor.slnx` e os dois projetos de teste no Windows. O `CheckCommand.cs`
foi compilado à parte contra o Application e o Infrastructure.

