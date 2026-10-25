# Fixação do tubo travessa nos carrinhos Z (HGH15)

- **Status:** Implementado (protótipo v1), falta imprimir e montar
- **Data:** 2026-10-19
- **Branch:** `feature/referencial-qualquer-rotacao` (o protótipo entrou junto; separar se preferir)

## Objetivo

Modelar a peça que prende cada ponta do `tubo_travessa` (aço, 50 × 30, parede 2) ao
`carrinho_hgh15_z_a/b`. Ela era "modelada fora do software", e o espaço dela já estava reservado na
cena: `espessuraFixacaoCarrinhoZ` (19,61 mm, da face da extrusão Y ao topo do carrinho) e
`vaoTuboCarrinhoZ` (12 mm, da ponta do tubo ao topo do carrinho).

Esta é uma mudança de **cena**: valem as regras do CadScript (`AiCore/.cad/api.md`), não as do app.

## Situação de partida (medidas tiradas da cena)

X e Y são do mundo. Z é relativo a `zBaseExtrusoesY` (face de baixo das extrusões Y), que chamo de
**Z₀**. Os números são do lado A. O lado B é o espelho em X, com as diferenças da última coluna.

| Elemento | X | Y | Z − Z₀ | Lado B |
|---|---|---|---|---|
| Face de fixação do carrinho Z (topo, furos M4) | −151,89 | −212,95 … −178,95 | −71,4 … −10 | X = +152,11; Y −212,50 … −178,50 |
| Furos M4 × 5 do carrinho (malha 26 × 26) | −151,89 | −208,95 e −182,95 | −27,7 e −53,7 | Y −208,50 e −182,50 |
| Ponta do tubo | −139,89 | −200,95 … −150,95 | −40 … −10 | X = +140,11; Y igual |
| Face externa da extrusão Y | −132,28 | a partir de −242,95 | 0 … +20 | X = +132,50 |
| Espaçador do tubo (sob a extrusão) e furo M5 dele | −132,28 … −112,28; furo em −122,28 | −200,95 … −150,95; furos em −188,45 e −163,45 | −10 … 0 | espelho |
| Motor NEMA17 do Y (`motor_nema17_y_a`) | −175,58 … −133,28 | −246,75 … −204,45 | a partir de −9 | X 133,50 … 175,80; Y até −204,00 |
| Trilho Z (fixo na estrutura) | −179,89 … −164,89 | −203,45 … −188,45 | todo o curso | espelho |

Consequências:

1. **Os eixos não se alinham.** O centro do carrinho fica 20 mm atrás do centro do tubo (−Y) e
   15,7 mm abaixo dele. Eles só se sobrepõem num canto de 22 × 30 mm, então a peça leva a carga em
   diagonal.
2. **A fileira de cima dos furos do carrinho (Z −27,7) fica de frente para a ponta do tubo**, a 12 mm
   dela. Com o tubo no lugar, não há espaço para o parafuso nem para a chave.
3. **O motor Y fica 1 mm acima do topo do carrinho** e cobre a faixa Y < −204,45. Ali a peça não pode
   passar de Z −10.
4. **O M5 do espaçador entra por dentro do tubo**, a 17,6 mm da ponta, com a cabeça pendurada na
   parede de cima.
5. **Os lados A e B não são iguais.** A ponta da extrusão B fica 0,45 mm à frente
   (`cantoExtrusaoYA/B`), e o tubo é um só. A distância em Y do carrinho ao tubo é 12,000 mm no A e
   11,553 mm no B.
6. **Carrinho e tubo se movem juntos** (o gantry inteiro). Na peça, a folga que importa é com o que
   fica parado. Trilho Z, verticais e travessas estão todos fora da faixa X −152 … −104 e Y −213 … −142
   em qualquer altura do curso, e a mesa/chapa MDF começa em Y −119.

## Decisões (2026-10-19)

1. **Só parafusos, nunca rebites.** O tubo leva M5 passantes com porca.
2. **Sem a "torre" na extrusão Y.** A extrusão é parafusada no tubo de aço com parafusos de aço, bem
   mais rígido que qualquer peça impressa. O volume continua livre se um dia for preciso.
3. **O suporte do motor Y fica fora desta peça**, por enquanto.
4. **Material: ABS**, reforçado com parafusos.

## O que foi feito: garfo em U

Peça `AiCore/Pecas/FixacaoCarrinhoZTubo.cs`, instâncias `fixacao_carrinho_z_tubo_a` e `_b` em
`AiCore/Cena.cs` (logo depois dos espaçadores do tubo).

```
  vista de cima (lado A)                    vista pela ponta do tubo (corte em X ≈ −135)

  Y                                          Z−Z₀
  ↑  bochecha frente ┌───────────────┐        −10 ┌──┬──────────────────┬──┐ ← topo do tubo
  │ −142,6 ┌─────────┤      ○ M5   ○ │            │  │  ○ M5 (−17,5)    │  │
  │ −150,6 │░░░░░░░░░│               │            │ b│                  │b │
  │        │ parede  │    tubo       │            │ t│      tubo        │f │
  │ −178,9 ┤░ponta░░░│               │            │◖ │ ← canal M4       │  │
  │ carri- │░placa░░░│               │        −40 │  └──────────────────┘  │
  │ nho    │░░░░░░░░░│               │      −46,3 └──────── fundo ─────────┘
  │ −201,2 │░░░░░░░░░├───────────────┘
  │ −209,2 └─────────┤ bochecha trás (canal de acesso ao M4 na face externa)
  │ −212,9 ┘
  └──────────────────────────────────────→ X
       −151,9  −140,4 −139,9         −103,9
```

- **Placa** de 11,5 mm (12 de vão − 0,5 até a ponta do tubo), parafusada no carrinho com **4 M4 × 10**.
  A cabeça fica em rebaixo Ø7,5 do lado do tubo, com 5,5 mm de ABS sob ela; o parafuso pega 4,5 mm
  dos 5 de rosca do carrinho.
- **Parede na frente da ponta do tubo**, na mesma espessura da placa. Ela liga a bochecha da frente,
  que fica além da largura do carrinho, ao resto da peça.
- **Bochechas de 8 mm** nas faces ±Y do tubo (folga de 0,3 por lado), 36 mm além da ponta, com o topo
  rente ao do tubo e ao do carrinho.
- **Fundo de 6 mm** sob o tubo (folga de 0,3), ligando tudo.
- **Canal de acesso Ø8 em X** na face externa da bochecha de trás, até a ponta dela. O furo M4 de cima
  e de trás do carrinho cai na linha dessa bochecha (Y local 4, Z 43,7); sem o canal, o parafuso e a
  chave não entrariam.
- **2 M5 passantes em Y** (furo Ø5,5) a **8 e 28 mm da ponta do tubo**, 22,5 mm acima da face de
  baixo dele (Z −17,5). As posições evitam três coisas:
  - o canal de acesso, que vai até Z −23,7 (os M5 começam em −20,25);
  - a parede de cima do tubo por dentro (Z −12): o M5 vai até −15;
  - as cabeças dos M5 do espaçador, que ficam entre 13,4 e 21,9 mm da ponta.

  Parafuso **M5 × 75** com arruela e porca autotravante. A arruela Ø10 do lado de trás fica 1 mm
  acima do canal.
- **Sem chanfros.** O lado B é a peça espelhada (`Placement.MirrorX()`), e um fillet não pode ser
  espelhado.
- O lado B usa `with { DeslocamentoTuboY = ... }`: cada lado mede o próprio deslocamento do tubo a
  partir de `carrinhoZA/B.ToWorld` e `tuboTravessa.ToWorld`.

**Correção em relação ao plano inicial:** a ponta do tubo **não** fica aberta. A parede da frente é a
única ligação da bochecha da frente com a placa, então ela fica. Para chegar aos M5 do espaçador,
solte os 4 M5 passantes e levante o gantry.

### Montagem

1. Montar o gantry à parte: tubo nas extrusões Y pelos espaçadores (M5 por dentro do tubo).
2. Parafusar cada garfo no seu carrinho, já no trilho, com os 4 M4 × 10. O M4 de cima e da frente
   (dentro do U) só tem acesso agora, antes do tubo. O de cima e de trás entra pelo canal da bochecha,
   com chave longa, a qualquer hora. Os dois de baixo ficam abaixo do fundo e também têm acesso a
   qualquer hora.
3. Baixar o gantry por cima: o tubo entra entre as bochechas e apoia no fundo.
4. Com o gantry no meio do curso, depois de correr o curso inteiro com os M4 frouxos, apertar os M4.
   Furar o tubo Ø5,3 pelos furos das bochechas e passar os M5.

### Alternativas consideradas

| Alternativa | Por que não |
|---|---|
| Bujão encaixado na ponta do tubo, preso na placa | Não monta: com os dois carrinhos fixos nos trilhos, o tubo rígido não tem como entrar nos dois bujões em X ao mesmo tempo |
| Parafusos verticais (Z) atravessando o tubo perto da ponta | Sobre o tubo só há 7,6 mm livres entre a ponta e a face da extrusão. A cabeça de um M4 mal cabe e não há como passar chave |
| Bochecha de trás fina (≤ 4 mm) para fugir do furo M4 | Fraca demais em ABS. O canal de acesso só tira material numa faixa de 8 mm de altura |
| Porca rebite nas paredes do tubo | Descartada: só parafusos |

## Validação feita

- [x] `AvaloniaCadEditor.UI.exe check AiCore --geometry`: **OK, 73 features, geometria validada**.
- [x] Teste temporário sobre a cena real (já apagado):
  - [x] As duas peças constroem: 68,3 cm³ (A) e 68,0 cm³ (B).
  - [x] Os 4 furos M4 de cada carrinho caem exatamente em (0; 4 e 30; 17,7 e 43,7) no local da peça,
        nos dois lados.
  - [x] Interseção zero com todos os sólidos que aceitam booleana: carrinho, motor Y, espaçador,
        extrusões, trilhos e o resto.
  - [x] Com o tubo, que é girado e tem fillet, a interseção é zero com o envelope dele. Com os outros
        girados (placas Y, motor X, hotend), as caixas nem se sobrepõem.
- [x] Testes de `Analysis`, `Drafting`, `Scenes` e `ReferenceGuides`: 300 passam. As 5 falhas já eram
      conhecidas: 3 de `CheckCommandProcessTests` (bin fora da solução, ARCHITECTURE §8) e 2 de
      `ConexaoYAdaptador5mmTests`, que já falhavam no HEAD.
- [ ] Imprimir e montar.

## Riscos

- **Fluência do ABS sob os M4.** São 5,5 mm de material sob a cabeça. Use arruela. Se precisar
  reapertar com frequência, troque o rebaixo por menos profundidade e M4 × 12, conferindo antes se a
  ponta não encosta no fundo do furo do carrinho, que só tem 5 mm de rosca.
- **ABS empena.** A peça tem 48 × 70 × 61 mm. Imprima deitada sobre a face da placa que encosta no
  carrinho: ela fica plana e as camadas não ficam perpendiculares aos M4.
- **Alinhamento dos dois lados.** Com furação na montagem, o erro de paralelismo entre os trilhos Z
  passa para o tubo em vez de travar os carrinhos. Siga a ordem da montagem acima.
