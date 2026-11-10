# Selecionar uma cota: o que ela mede, o que a move e mudar o valor por ela

**Status:** Em andamento · **Data:** 2026-11-10 · **Branch:** `feature/modelar-na-peca-isolada`

## Objetivo

No desenho da peça isolada (H + vista padrão), clicar no texto de uma cota:

1. destaca **o que a cota mede** (as primitivas das duas pontas, ou o furo do diâmetro);
2. lista as **alavancas**: os números do código que mudam aquela medida, cada uma dizendo **o que
   se move** junto;
3. propõe **opções conforme o contexto**: mover junto o que é coaxial ou simétrico ao elemento movido,
   para não quebrar a relação;
4. com um valor novo digitado, calcula quanto cada número precisa mudar, confere numa execução e
   grava no fonte com uma edição pontual de texto (Ctrl+Z desfaz).

Funciona com **literais**, que é o estilo que a IA gera por padrão (plano
[código editável por padrão](2026-11-01-codigo-editavel-por-padrao.md)), e também com parâmetros do
record: o argumento passado na cena ou o valor padrão.

## Ideia central: sondar em vez de adivinhar

O rastreio estático (`RoslynPartSolidTracer`) diz o que **alimenta** um sólido, não o que **muda uma
medida**. Exemplo: um furo em `Largura - 10`. `Largura` alimenta o furo, mas não muda a cota de 10 até
a borda.

Por isso a cena é **instrumentada** em memória: cada número literal relevante vira
`__CadLevers.L(id, valor)`. Compila-se **uma vez** e executa-se uma vez por alavanca, com aquele número
acrescido de um passo pequeno. A comparação dos fatos (`FactExtractor`) pelo nome estável diz o que
mudou, e a projeção da cota diz quanto ela mudou. Executar a cena só cria features: sem OCCT, sem
malha.

Que números entram:

| Tipo | Onde | Como |
|---|---|---|
| `Literal` | corpo do record da peça (`Build()`, membros) | o literal vira `L(id, v)`; `-5` é uma alavanca só |
| `Argument` | argumentos de `new Peca(...)` nos arquivos de entrada (cena) | idem |
| `Default` | valor padrão de um parâmetro do record (`double Largura = 80`) | o padrão é constante e não pode virar chamada: cada `new Peca(...)` que **não** passa o parâmetro ganha `Largura: L(id, 80)`. Editar o padrão é exatamente isso |

Ficam de fora: literal que não é `double` depois da conversão (contagem `int`, divisão inteira),
`const`, atributos e literais de outras peças (sub-peça é caixa-preta).

## Arquitetura

Cada peça faz uma coisa só, é sem estado e testável isoladamente. A seta das dependências não muda:
Application não conhece Drafting nem Infrastructure, e a UI compõe.

| Camada | Tipo | Papel |
|---|---|---|
| Analysis | `AnalysisSubject.For(feature, worldToFrame)` | raiz e posicionamento de uma feature de topo (antes, só dentro de `DraftingInput.ForIsolated`) |
| Analysis | `FactMotion.Compare(antes, depois)` | puro: o que cada fato fez (translação, mudança de tamanho), pelo nome estável |
| Drafting | `DimensionGauge` | puro: resolve as âncoras de uma cota numa folha e a **remede** sobre outro conjunto de fatos |
| Application | `Levers/CodeLever` | um número do código que pode mudar: tipo, local, valor, rótulo legível |
| Application | `Interfaces/ILeverProbe(Factory)` | porta: executa a cena com valores trocados |
| Application | `Levers/LeverStudy` | fatos de base e o efeito de cada alavanca (uma execução cada) |
| Application | `Levers/LeverOptionFinder` | puro: alavancas de uma medida, agrupadas pelas relações (coaxial, simetria), ordenadas |
| Application | `Levers/LeverSolver` | puro: valor-alvo → valores novos (secante, conferida pela sonda) |
| Application | `Levers/LeverSourceEditor` | puro: grava os valores novos com edição pontual; recusa se o texto mudou desde a sonda |
| Infrastructure | `Scripting/RoslynLeverInstrumenter` | puro: árvores → árvores instrumentadas + alavancas |
| Infrastructure | `Scripting/RoslynLeverProbeFactory` | compila a cena instrumentada, carrega num contexto colecionável e executa |
| UI | `Drafting/DimensionLeversViewModel` | seleção da cota, estudo em segundo plano, opções e aplicação |

## Etapas

- [x] Plano e índice
- [x] `AnalysisSubject.For` e `FactMotion` (+ testes)
- [x] `DimensionGauge` (+ testes, inclusive todas as cotas do desenho real da `PlacaSuporteCarrinhoY`)
- [x] `CodeLever`, porta, `LeverStudy`, `LeverOptionFinder`, `LeverSolver`, `LeverSourceEditor` (+ testes)
- [x] `RoslynLeverInstrumenter` e `RoslynLeverProbeFactory` (+ testes com cena pequena e com cada peça da `AiCore`:
  sem trocar nada, a sonda dá os mesmos fatos que a execução normal; ~0,5 a 1 s por peça)
- [x] UI: seleção pelo texto da cota, destaque (ciano: o que mede; magenta: o que a opção move), painel de
  opções na legenda, valor novo + Aplicar (Enter), ESC tira a seleção
- [x] ARCHITECTURE.md §4
- [ ] Verificar no app (com permissão)

Medido na `PlacaSuporteCarrinhoY` (vista de topo): todas as cotas são remedidas pela sonda com o valor do
desenho; as que ficam sem opção são o diâmetro de uma sub-peça (caixa-preta) e uma medida que vem de conta.

Próximos passos, fora desta entrega:
- fantasma tracejado da posição nova antes de aplicar;
- sólido solto da cena (os literais ficam na `Cena.cs`; a sonda é a mesma, só muda o alvo);
- literais em variáveis locais da cena que alimentam `new Peca(...)`.

## Riscos

- **Custo:** uma execução da cena por alavanca. O estudo roda em segundo plano, uma vez por versão do
  código, e é descartado quando o código muda: se ainda estiver rodando, é cancelado
  (`DimensionLeversViewModel.DropStudy`), e o `LeverStudy.Run` cancelado descarta a própria sonda.
- **Não linearidade** (`if`, ângulos): a taxa vale em torno do valor atual. O `LeverSolver` itera e
  confere numa execução real antes de gravar.
- **Código mudou desde a sonda:** o `LeverSourceEditor` confere o texto de cada literal e recusa.
- **`var x = 2.5;` vira `int` se gravado como `3`:** literal que era `double` no texto continua com
  ponto decimal (`3.0`).
