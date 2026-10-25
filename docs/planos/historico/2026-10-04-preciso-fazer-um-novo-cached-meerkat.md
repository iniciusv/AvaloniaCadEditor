# Cantoneira de ligação: placa_suporte_carrinho_y_a ↔ extrusao_2020_sob_trilho_x

## Context

Hoje a `placa_suporte_carrinho_y_a` (parafusada no topo do `carrinho_mgn15h_a`) e a
`extrusao_2020_sob_trilho_x` são duas peças independentes: nada amarra o conjunto
carrinho+placa ao perfil de alumínio transversal. Falta uma peça impressa que faça essa
ligação, aproveitando os dois furos horizontais com porca M3 cativa já existentes na face
interna da placa (`furo_x_1` / `furo_x_2` + `alojamento_porca_x_1` / `alojamento_porca_x_2`)
e prendendo-se ao perfil 2020 por 2 furos M3 passantes no eixo Y, parafusados pela frente,
centralizados na altura do perfil (porca em T no rasgo).

Resultado esperado: uma cantoneira em L com reforço de canto, nova peça em `AiCore/Pecas/`,
instanciada uma vez na cena no lado A.

## Geometria de referência (coordenadas globais da cena, já conferidas)

| Elemento | Valor |
|---|---|
| `espacamentoTrilhosY` = `TrilhoMgn9(250).DistanciaEntreUltimosFuros` | 220 → `centroXTrilhoA` = −110 |
| Face interna da `placa_suporte_carrinho_y_a` | X = **−106,5** (placa gira 180°, então a face local x = −16 vira +16) |
| Material da placa nessa face existe em | Y ∈ [−36,5 ; −12,1] (acima disso é o recorte) · Z ∈ [16 ; 27] |
| `furo_x_1` (entrada) | (−105,5 ; **−31,5** ; **22,625**), direção −X, porca a 3–6 mm da face |
| `furo_x_2` (entrada) | (−105,5 ; **−16,5** ; **22,625**), idem |
| `extrusao_2020_sob_trilho_x` | X ∈ [−105 ; 103] · Y ∈ [**−10** ; 10] · Z ∈ [−4 ; 16] |
| Face frontal livre do perfil | Y = −10 (a face +Y está tomada pelo `bloco_conexao_motor_2020`, o topo pelo `trilho_mgn9_x`) |
| Meia-altura do perfil / centro do rasgo T | Z = **6** (rasgo aberto em Z ∈ [2 ; 10]) |

Origem local escolhida para a nova peça: **(−106,5 ; −10 ; 6)** — interseção da face da placa,
da face frontal do perfil e da meia-altura do perfil. Eixos locais paralelos aos globais
(sem rotação no `Placement`), o que torna os números do `Build()` diretamente legíveis:
X ≥ 0 = material à frente da placa, Y ≤ 0 = material à frente do perfil, Z = 0 = eixo dos
furos do perfil.

## Arquivo novo: `AiCore/Pecas/CantoneiraPlacaExtrusao2020.cs`

`public sealed record CantoneiraPlacaExtrusao2020(...) : Part`, um único record no arquivo,
sem `AddFeature`/`Add` (ver `AiCore/.cad/api.md`). Nenhuma alteração de `.csproj` é necessária:
`AiCore.csproj` já exclui `Pecas\**\*.cs` do build e o `RoslynSceneEvaluator` compila a pasta.

**Regra obrigatória do repo** (`AiCore/.cad/api.md`, "Sólidos rastreáveis por nome"): cada
sólido em sua própria declaração, `Name` literal igual ao nome da variável, **sem laços, sem
listas dinâmicas, sem nome interpolado**. Usar `PlacaSuporteCarrinhoY.cs` e
`BlocoFixacaoMgn9.cs` como template de estilo — **não** copiar `SuporteMotorNema17.cs`
nem `SuporteHotendV6.cs`, que ainda violam a regra.

### Parâmetros do record (defaults)

```
EspessuraAbaPlaca = 4            // parede contra a placa (parafuso M3x10: 4 + porca a 3..6 mm)
EspessuraAbaExtrusao = 4         // parede contra o perfil 2020
LarguraAbaPlacaY = 26.5          // Y: 0 → -26.5 (global -10 → -36.5, rente à borda da placa)
AlturaAbaPlaca = 29              // Z: -8 → +21 (global -2 → 27, rente ao topo da placa)
AlturaAbaExtrusao = 16           // Z: -8 → +8, centrada no rasgo T
PassoFurosExtrusao = 20          // pedido do usuário: parâmetro do record
DistanciaPrimeiroFuroX = 10      // X do 1º furo; aba vai até 2*10 + Passo = 40
AlturaFurosPlacaZ = 16.625       // = 22.625 - 6
DistanciaFuroPlaca1Y = 21.5      // = 31.5 - 10
DistanciaFuroPlaca2Y = 6.5       // = 16.5 - 10
DiametroFuroM3 = 3.4
EspessuraReforco = 3
AvancoReforcoX = 20
AvancoReforcoY = 20
```

Comentário no topo do arquivo deve registrar que `AlturaFurosPlacaZ`,
`DistanciaFuroPlaca*Y` derivam dos furos `furo_x_1`/`furo_x_2` de
[PlacaSuporteCarrinhoY.cs](AiCore/Pecas/PlacaSuporteCarrinhoY.cs) — se aqueles mudarem,
estes acompanham.

### Sólidos do `Build()` (um por declaração, nesta ordem)

1. `aba_placa` — `BoxFeature(new Point3D(0, -LarguraAbaPlacaY, -AlturaAbaExtrusao / 2), EspessuraAbaPlaca, LarguraAbaPlacaY, AlturaAbaPlaca)`
   → X 0..4 · Y −26,5..0 · Z −8..21.
2. `aba_extrusao` — `BoxFeature(new Point3D(0, -EspessuraAbaExtrusao, -AlturaAbaExtrusao / 2), comprimentoAbaX, EspessuraAbaExtrusao, AlturaAbaExtrusao)`
   com `comprimentoAbaX = 2 * DistanciaPrimeiroFuroX + PassoFurosExtrusao` → X 0..40 · Y −4..0 · Z −8..8.
   (Os primeiros 1,5 mm em X ficam em voo: a ponta do perfil está em X global −105 e a face da
   placa em −106,5. Documentar em comentário.)
3. `reforco_canto_inferior` — `ExtrudeFeature(perfil_reforco_inferior, EspessuraReforco, new Vector3D(0, 0, EspessuraReforco), "reforco_canto_inferior")`,
   triângulo em Z = −8 com vértices (4, −4), (4, −24), (24, −4).
4. `reforco_canto_superior` — idem, triângulo em Z = +8 − EspessuraReforco = 5.
   Dois reforços horizontais (e não um só no meio) **porque** o canto em L tem aresta vertical:
   o gusset tem de ficar em plano horizontal, e deixar livre a faixa Z ∈ [−3 ; 3] garante
   acesso frontal da chave aos parafusos do perfil.
   Cada `PolygonProfile` pode vir de função local que só **calcula** os pontos (permitido),
   mas o `ExtrudeFeature` fica em declaração própria.
5. `corpo` — `BooleanFeature.CreateUnion("corpo", aba_placa, aba_extrusao, reforco_canto_inferior, reforco_canto_superior)`.
6. `furo_placa_1` — `CylinderFeature(new Point3D(-1, -DistanciaFuroPlaca1Y, AlturaFurosPlacaZ), Vector3D.UnitX, DiametroFuroM3 / 2, EspessuraAbaPlaca + 2)`, `IsTransientTool = true`.
7. `furo_placa_2` — idem em `-DistanciaFuroPlaca2Y`.
8. `furo_extrusao_1` — `CylinderFeature(new Point3D(DistanciaPrimeiroFuroX, -EspessuraAbaExtrusao - 1, 0), Vector3D.UnitY, DiametroFuroM3 / 2, EspessuraAbaExtrusao + 2)`, `IsTransientTool = true`.
9. `furo_extrusao_2` — idem em `DistanciaPrimeiroFuroX + PassoFurosExtrusao`.
10. `return BooleanFeature.CreateDifference("cantoneira_placa_extrusao_2020", corpo, furo_placa_1, furo_placa_2, furo_extrusao_1, furo_extrusao_2);`

Sem alojamento de porca nesta peça: todos os 4 furos são passantes simples e as porcas ficam
na placa (porca M3 cativa) e no rasgo do perfil (porca em T) — coerente com a regra de que
alojamento de porca só existe em furo que vaza para face externa.

## Alteração em `AiCore/Cena.cs`

Inserir logo após as declarações de `extrusaoSobTrilhoX` (linha ~63), reaproveitando as
variáveis já existentes `centroXTrilhoA`, `alturaTopoCarrinhosY` e `extrusaoSobTrilhoXPeca`:

```csharp
// --- Cantoneira impressa que amarra a placa do carrinho Y ao perfil 2020 transversal ---
// Origem na interseção: face interna da placa A (a placa é girada 180°, logo sua face local
// x = -16 vira +16), face frontal livre do perfil e meia-altura do perfil.
var xFaceInternaPlacaA = centroXTrilhoA - 12.5 + 16;
var yFaceFrontalExtrusaoX = -extrusaoSobTrilhoXPeca.Largura / 2;
var zMeioExtrusaoX = alturaTopoCarrinhosY - extrusaoSobTrilhoXPeca.Altura / 2;
var cantoneiraPlacaExtrusaoA = Add(new CantoneiraPlacaExtrusao2020(),
    at: Placement.At(xFaceInternaPlacaA, yFaceFrontalExtrusaoX, zMeioExtrusaoX),
    name: "cantoneira_placa_extrusao_a", color: "#00CC6A");
```

Fora de escopo (não fazer agora): a cantoneira espelhada para a `placa_suporte_carrinho_y_b` —
o pedido é só o lado A. A peça já fica pronta para ganhar um `EspelhadoNoEixoY`, no mesmo
estilo de `PlacaSuporteCarrinhoY`, quando o lado B for pedido.

## Verificação

1. `dotnet build` na solução — garante que o resto do app segue compilando (o arquivo novo não
   entra nesse build, por desenho).
2. Abrir o app (skill `/run`) com a cena `AiCore/` e conferir que **não** aparece erro de
   compilação de script; se aparecer, ler
   `AvaloniaCadEditor.UI/bin/Debug/net10.0-windows/script_compile_error.log` (o conteúdo atual
   desse arquivo é de uma execução antiga e deve ser ignorado).
3. No 3D, conferir visualmente:
   - a aba da cantoneira encosta na face interna da placa A (X = −106,5) e seus 2 furos
     coincidem com `furo_x_1` / `furo_x_2`;
   - a outra aba encosta na face frontal do perfil (Y = −10) e seus 2 furos caem dentro do
     rasgo T, na meia-altura (Z = 6);
   - nenhuma interferência com `carrinho_mgn15h_a`, `trilho_mgn9_x` nem
     `bloco_conexao_motor_2020`.
4. Selecionar cada sólido novo no 3D e confirmar que o editor pula para a linha correta do
   código (valida a regra de nome literal por sólido).
