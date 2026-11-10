# Selecionar uma cota: o que ela mede, o que a move e mudar o valor por ela

**Status:** Congelado (ARCHITECTURE.md §9.1): funciona e fica no app, sem extensão até haver controle sobre o que a cota arrasta · **Data:** 2026-11-10 · **Branch:** `feature/modelar-na-peca-isolada`

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
atributos e literais de outras peças (sub-peça é caixa-preta). O `const double` entrava nessa lista e
saiu dela na segunda rodada (abaixo).

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
| UI | `Drafting/DimensionLeversViewModel` | seleção da cota, estudo em segundo plano, opções, prévia e aplicação |
| Application | `Levers/LeverVertexRuns` (2ª rodada) | puro: os pontos seguidos de um contorno com a mesma coordenada (uma aresta) |
| UI | `Drafting/LeverOptionItem`, `LeverPreviewItem` (2ª rodada) | puro: os textos de cada opção (alcance, avisos) e a prévia montada da solução da sonda |

## Por que está congelado (2026-11-10)

No uso, a tela agradou, mas mudar uma dimensão não dá controle sobre **o que se move junto**: as opções vêm
de sondar os literais do código, e o que cada número arrasta depende de como o modelo foi escrito. E o
formato do código gerado pelos modelos pode mudar, o que mudaria as respostas da sonda. Até haver um jeito
de dizer o que a dimensão deve arrastar (e não só escolher entre o que a sonda achou), a funcionalidade fica
como está: funciona, não é estendida, e quem trabalha em outra coisa não a lê (regra em ARCHITECTURE.md §9).

Para retomar, os pontos de partida são o "Mapa técnico" abaixo e os "Próximos passos" da segunda rodada.

## Mapa técnico (era a linha da §4 do ARCHITECTURE.md)

a resposta vem de **sondar**, não do rastreio estático: cada literal `double` da peça (corpo do record, argumentos de `new Peca(...)` na cena, valor padrão) vira `L(id, valor)` numa compilação em memória, executada uma vez por número (`FactMotion` diz o que mudou). Literal e parâmetro são a mesma coisa para a sonda. O `const double` do record também entra: **só na cópia** ele deixa de ser `const` (local vira variável, campo privado vira propriedade estática), menos quando algum uso exige constante; se a cópia não compilar, a sonda cai para a versão sem isso. **Nem todo número é medida.** O instrumentador dá o papel pela sintaxe (`CodeLeverRole`: divisor, componente de direção) e o estudo dá a resposta da peça (`LeverEffect.Gain`: uma medida mexe 1, 2 ou 0,5 por unidade; um multiplicador, o valor do que multiplica). O que não é medida sai como opção **indireta** (`LeverCaveat`), só a pedido e nunca escolhida de antemão. Opções que quebrariam uma relação (coaxial, simetria) ganham um grupo que move o parceiro junto, e os vértices seguidos de um contorno com a mesma coordenada andam como aresta, medida numa execução própria (`LeverStudy.Runs`), não somada dos pontos. Numa peça simétrica, a aresta de um lado anda com a do outro para o corpo continuar no plano (`Centered`), e o par espelhado precisa ter o mesmo tamanho, não só a posição espelhada. O parceiro pode precisar de dois números (largura e canto de uma caixa). O grupo é executado de verdade, e o que ele ainda quebra é o que a execução mostra. O painel avisa quantas instâncias mudam, calcula a **prévia** na sonda antes de gravar (`LeverPreviewItem`: números, outras cotas, o que some, fantasma tracejado) e reencontra a cota depois de aplicar pelo que ela mede, porque a chave pode mudar. Gravar é edição pontual do token e recusa se o texto mudou desde a sonda. A sonda é uma cena compilada e carregada: o `LeverStudy` só fica dono dela quando `Run` o devolve (em qualquer outra saída, `Run` a descarta), e o ViewModel **cancela** o estudo que descarta, em vez de deixá-lo rodar até o fim.

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
- [x] Segunda rodada: medidas e indiretas, `const`, arestas, alcance, prévia, escolha lembrada (+ testes
  pelo ViewModel com a cena executada)
- [x] Terceira rodada: a largura de uma peça simétrica anda dos dois lados; par espelhado com o mesmo tamanho (+ testes)
- [ ] Verificar no app (com permissão): o painel, o fantasma tracejado e o botão "no código"

Medido na `PlacaSuporteCarrinhoY` (vista de topo): todas as cotas são remedidas pela sonda com o valor do
desenho; as que ficam sem opção são o diâmetro de uma sub-peça (caixa-preta) e uma medida que vem de conta.

## Segunda rodada (2026-11-10): que números oferecer, e ver antes de gravar

Medido na `PlacaSuporteCarrinhoY` depois da primeira entrega: 27 de 29 cotas tinham opções, mas a
primeira, que vinha escolhida e o Enter aplicava, podia ser um número estrutural, e a medida com
nome ficava de fora.

| Problema visto na peça | O que mudou |
|---|---|
| Cota 18: a única opção era o `1.0` de `var sx = Espelhado ? -1.0 : 1.0;`. Digitar 20 gravava `1.1111` no sinal e escalava 22 elementos | Só **medidas** na lista padrão. O papel vem da sintaxe (`CodeLeverRole`: divisor, componente de direção) e da resposta da peça (`LeverEffect.Gain`: uma medida mexe 1, 2 ou 0,5 por unidade; o sinal da placa mexe 32). O resto é **indireta** (`LeverCaveat`): a pedido, com aviso, nunca escolhida de antemão |
| Cota 3,5: a primeira opção era o divisor de `passoFixacao / 2` | Divisor é `Factor` pela sintaxe |
| `3× Ø3,4` sem opção: o raio é `const double raioFuro = 1.7` (55 `const` em 14 das 41 peças) | Na cópia instrumentada o `const double` deixa de ser `const` quando nenhum uso exige constante. O fonte continua com o `const`: gravar troca só o literal |
| Cotas 32 e 48: duas opções com o mesmo rótulo, uma por vértice; aplicar uma entortava o contorno | Vértices seguidos com a mesma coordenada andam como aresta (`LeverVertexRuns`), medida numa execução própria. O rótulo ganhou "ponto N" |
| A placa tem 2 instâncias e o painel não dizia que o literal muda as duas | Cada opção diz o alcance |
| Só se via o resultado depois de gravar | Prévia na sonda: números, outras cotas (antes → depois), o que some, fantasma tracejado |
| A opção escolhida voltava para a primeira a cada reexecução, e aplicar podia fechar o painel | Escolha lembrada pela chave dos números; a cota é reencontrada depois de aplicar pelo que mede |

Depois disso, na mesma peça: `3× Ø3,4` oferece `raioFuro`; as cotas da malha de furos oferecem
`passoFixacao`; a cota 18 oferece a aresta esquerda do contorno; o sinal do espelho, os divisores e as
direções só aparecem como indiretas.

O multiplicador não se decide pela sintaxe: `16 * sx` (medida vezes sinal) e `2 * raio` (fator vezes
medida) são iguais. Quem os separa é o `Gain`, e ele erra quando o que o fator multiplica vale
exatamente 1, 2 ou 0,5: aí o fator passa por medida. É o limite de quem só sonda.

Próximos passos, fora desta entrega:
- sólido solto da cena (os literais ficam na `Cena.cs`; a sonda é a mesma, só muda o alvo);
- literais em variáveis locais da cena que alimentam `new Peca(...)`;
- conferir a geometria da peça (OCCT) na prévia: a sonda só cria features, então um furo que sai do
  corpo só aparece depois de gravar;
- o estudo só enxerga o efeito de aumentar o número: um `Math.Max` que só responde para baixo não vira
  opção.

## Terceira rodada (2026-11-10): a largura de uma peça simétrica

Antes, a aresta da largura andava de um lado só, e o grupo de simetria só conferia o deslocamento: no
`SuporteRolamentosCorreia`, `braco_inferior · width` vinha com `braco_superior · baseCorner.x` como "mantém
a simetria" (um braço engordava, o outro só andava), e o colar de cima encolhia quando o de baixo crescia.

| Caso | O que mudou |
|---|---|
| Corpo centrado no plano de simetria da peça inteira (`SymmetryPlane.IsGlobal`) e uma aresta só anda | Nova relação `Centered`: a opção de um lado avisa que a peça deixa de ser simétrica, e o grupo com a aresta do outro lado (sentido contrário) a mantém. Numa `BoxFeature`, o grupo é a largura com o canto andando metade |
| Par espelhado em que um muda de tamanho | O espelho exige o mesmo tamanho, não só a posição espelhada. O pedido ao parceiro é deslocamento e tamanho juntos |
| O parceiro precisa de dois números (a caixa espelhada cresce para fora: largura e canto) | A busca tenta um companheiro e, se nenhum serve, dois, por mínimos quadrados |
| Uma aresta inteira como companheira, ou como número principal de um grupo | Companheiro é uma aresta inteira ou um número que não é ponto de aresta |
| O grupo dizia que mantinha tudo o que pediu | O grupo é executado. A taxa, o que muda e o que ainda quebra vêm dessa execução, e o par direita+esquerda e esquerda+direita vira uma opção só |

Limite: só o plano **global** pede o centro. A simetria vem do `RelationAnalyzer`, que precisa de pelo menos um
par de elementos espelhados. Uma placa lisa sem furos não tem plano, e a largura continua andando de um lado.
Quando o parceiro depende de uma conta (o colar de cima do `SuporteRolamentosCorreia` fica em
`AlturaRolamentos`), não há grupo e a opção avisa a quebra. O padrão `FolgaColar`, que muda os dois, vem antes.

## Riscos

- **Custo:** uma execução da cena por alavanca. O estudo roda em segundo plano, uma vez por versão do
  código, e é descartado quando o código muda: se ainda estiver rodando, é cancelado
  (`DimensionLeversViewModel.DropStudy`), e o `LeverStudy.Run` cancelado descarta a própria sonda.
- **Não linearidade** (`if`, ângulos): a taxa vale em torno do valor atual. O `LeverSolver` itera e
  confere numa execução real antes de gravar.
- **Código mudou desde a sonda:** o `LeverSourceEditor` confere o texto de cada literal e recusa.
- **`var x = 2.5;` vira `int` se gravado como `3`:** literal que era `double` no texto continua com
  ponto decimal (`3.0`).
