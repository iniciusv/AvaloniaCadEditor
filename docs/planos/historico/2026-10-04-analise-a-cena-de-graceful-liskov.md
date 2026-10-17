# Remover alojamentos de porca órfãos em PlacaSuporteCarrinhoY

## Contexto

A peça `PlacaSuporteCarrinhoY` (nova, em [AiCore/Pecas/PlacaSuporteCarrinhoY.cs](AiCore/Pecas/PlacaSuporteCarrinhoY.cs))
cria alojamentos hexagonais para porcas M3 em "todos" os furos horizontais (X e Y).
Um alojamento de porca só faz sentido se o furo do parafuso associado **vazar para fora
do modelo** — ou seja, se o furo alcançar uma face externa da placa, por onde o parafuso
entra de fora e rosqueia na porca alojada no fundo do furo. Alguns furos não alcançam
nenhuma face externa: ficam inteiramente dentro do sólido (vazio interno cego), tornando
o alojamento de porca correspondente inútil.

Faces externas da placa (a partir de `perfil_base`): X = **+16** (altura toda) e **−16**
(só para y ≥ −0,406), parede do recorte em **x = −1** (para y < −0,406), Y = **+24** (topo),
**−24** (base, só x ≥ −1) e parede do recorte em **y = −0,406**.

### Diagnóstico (furo × alcança face externa?)

| Alojamento | Furo | Vão do furo | Face externa? | Veredito |
|---|---|---|---|---|
| x_1, x_2 | furo_x_1/2 | −17 → −13 | sim (−16) | mantém |
| **x_3** | furo_x_3 | −10 → −1 | **não** | **remover** |
| **x_4** | furo_x_4 | −10 → +10 | **não** | **remover** |
| x_5, x_6 | furo_x_5/6 | −1 → +10 | sim (recorte x=−1) | mantém |
| **x_7** | furo_x_7 | +2,581 → +10 | **não** | **remover** |
| x_8 | furo_x_8 | +13 → +17 | sim (+16) | mantém |
| **y_1** | furo_y_1 | +8,5 → +14 | **não** | **remover** |
| y_2 | furo_y_2 | +21,916 → +25 | sim (+24) | mantém |

Semântica confirmada em [CylinderFeature.cs:54-65](AvaloniaCadEditor.Domain/Features/CylinderFeature.cs#L54-L65):
o ponto do `CylinderFeature` é o **centro da face base** e o furo se estende a partir dele
na direção dada pelo comprimento.

Decisões do usuário: remover também `x_7` (falha a mesma regra) e remover os furos órfãos
junto com os alojamentos (são vazios internos sem função). Cada furo órfão é usado por
apenas um alojamento (pares 1:1), então a remoção é segura. Os nomes das features não são
referenciados em nenhum outro arquivo (nem testes nem cena).

## Alterações — apenas [AiCore/Pecas/PlacaSuporteCarrinhoY.cs](AiCore/Pecas/PlacaSuporteCarrinhoY.cs)

1. **Remover declarações de furos órfãos** (bloco dos furos horizontais, ~linhas 69-78):
   - `furo_x_3` (linha 71)
   - `furo_x_4` (linha 72)
   - `furo_x_7` (linha 75)
   - `furo_y_1` (linha 77)

2. **Remover declarações de alojamentos órfãos** (~linhas 115-126):
   - `alojamento_porca_x_3` (linha 117)
   - `alojamento_porca_x_4` (linha 118)
   - `alojamento_porca_x_7` (linha 121)
   - `alojamento_porca_y_1` (linha 125)

3. **Remover esses 8 nomes da lista de argumentos** de
   `BooleanFeature.CreateDifference(...)` (linhas 128-136): tirar `furo_x_3`,
   `furo_x_4`, `furo_x_7`, `furo_y_1`, `alojamento_porca_x_3`, `alojamento_porca_x_4`,
   `alojamento_porca_x_7`, `alojamento_porca_y_1`.

4. **Manter a numeração restante inalterada** (x_1,x_2,x_5,x_6,x_8 / y_2) — sem renumerar,
   para não introduzir churn nem risco. Lacunas na sequência são aceitáveis.

5. **Atualizar o comentário XML de topo** (linhas 6-9): hoje diz que há alojamentos
   "em todos os furos horizontais nas direções X e Y". Ajustar para refletir que só os
   furos que **vazam para uma face externa** recebem alojamento de porca (os furos
   internos que não saem do sólido não têm porca).

## Verificação

1. Compilar a solução (`dotnet build`) — mudança é só remoção de features locais; deve
   compilar sem erros/avisos novos.
2. Rodar os testes existentes da peça/cena: `dotnet test` (nenhum teste referencia os
   nomes removidos, então nada deve quebrar).
3. Abrir a cena de exemplo no editor e inspecionar `placa_suporte_carrinho_y_a` e
   `placa_suporte_carrinho_y_b` (versão espelhada): confirmar visualmente que
   - os 4 vazios internos (porcas/furos cegos de x_3, x_4, x_7, y_1) sumiram;
   - os alojamentos restantes continuam alinhados com furos que saem por uma face
     externa (esquerda −16, direita +16, parede do recorte x=−1, topo +24).
