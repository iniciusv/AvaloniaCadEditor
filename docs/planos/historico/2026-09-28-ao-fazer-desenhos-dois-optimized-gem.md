# Seleção precisa de linhas e regiões no esboço 2D

## Contexto
No modo esboço, ao clicar numa linha frequentemente outra é selecionada, como se a "faixa clicável" fosse muito mais larga que a linha. Causas encontradas no código:

1. **Tolerância não acompanha o zoom da câmera.** [CadViewModel.cs:696](AvaloniaCadEditor.UI/ViewModels/CadViewModel.cs#L696) usa `0.5 / CurrentZoom`, mas `CurrentZoom` só é atualizado pelo antigo `ViewportControl` (2D). A janela usa o `SilkViewportControl` (câmera 3D, zoom = `CameraState.Distance`), então `CurrentZoom` fica sempre `1.0` → tolerância fixa de 0.5 unidades do mundo. Com zoom aproximado isso vira dezenas de pixels.
2. **HitTest retorna o primeiro acerto, não o mais próximo.** [MathGeometryEngine.cs:10-27](AvaloniaCadEditor.Infrastructure/Engines/MathGeometryEngine.cs#L10-L27) percorre as entidades em ordem reversa e devolve a primeira dentro da tolerância. Com duas linhas próximas (faixas sobrepostas), vence a desenhada por último, mesmo que o clique esteja em cima da outra.
3. **Região: pega a primeira que contém o ponto, não a mais interna.** [CadViewModel.cs:714](AvaloniaCadEditor.UI/ViewModels/CadViewModel.cs#L714) (e linha 824) usa `FirstOrDefault(ContainsPoint)`; com regiões aninhadas (furo dentro de retângulo) pode selecionar a externa. O `OffsetCommand.FindRegionAtPoint` já faz o certo (`OrderBy(p => p.Area)`).
4. **Trim/Extend usam tolerância fixa 5.0 unidades** ([TrimCommand.cs:20](AvaloniaCadEditor.Application/Commands/TrimCommand.cs#L20), [ExtendCommand.cs:16](AvaloniaCadEditor.Application/Commands/ExtendCommand.cs#L16)) — enorme, mesmo problema.

## Mudanças

### 1. Tolerância em pixels de tela
- Em [ViewportInputHandler.cs](AvaloniaCadEditor.UI/Controls/Input/ViewportInputHandler.cs), em `HandlePointerPressed` e `HandlePointerMoved`, depois de intersectar o raio do cursor com o plano de trabalho, gerar um segundo raio a partir de `posição + (1, 0)` pixel, intersectar com o mesmo plano e medir a distância entre os dois pontos → **unidades do mundo por pixel** naquele ponto (funciona em perspectiva e ortográfica, e em qualquer orientação). Atribuir em `viewModel.WorldUnitsPerPixel` antes de chamar `OnPointerPressed/OnPointerMoved`.
- Em [CadViewModel.cs](AvaloniaCadEditor.UI/ViewModels/CadViewModel.cs): adicionar `WorldUnitsPerPixel` (default 1.0) e uma constante `HitTolerancePixels = 6`. Substituir `0.5 / CurrentZoom` por `HitTolerancePixels * WorldUnitsPerPixel`. Remover `CurrentZoom` se não tiver outro uso (só o `ViewportControl` legado o escreve — manter o setter ali ou remover a linha).
- Propagar a mesma tolerância para Trim e Extend: transformar a constante em propriedade `public double HitTolerance { get; set; }` (como já existe em `SelectTool`) e atribuí-la no ViewModel junto com a do `SelectTool` (em `OnPointerPressed` e `OnPointerMoved`).

### 2. HitTest pelo mais próximo
- `MathGeometryEngine.HitTest`: calcular a distância de cada entidade (linha via `DistancePointToLine`, círculo via `|d - r|`) e retornar a de **menor distância** dentro da tolerância; em empate, preferir a desenhada por último (mantém o comportamento atual para sobrepostas).
- `DummyGeometryEngine` não precisa mudar (não é usado na UI).

### 3. Região mais interna
- Nas linhas 714 e 824 de `CadViewModel.cs`, trocar `FirstOrDefault(...)` por `.Where(...).OrderBy(p => p.Area).FirstOrDefault()`, mesmo padrão de `OffsetCommand.FindRegionAtPoint`.

## Testes
Novo arquivo `AvaloniaCadEditor.Tests/BehaviorTests/HitTestBehaviorTests.cs` (padrão xUnit dos testes existentes):
- Duas linhas paralelas a 1 unidade, clique a 0.2 da segunda com tolerância 2 → retorna a segunda (hoje retornaria a última da lista).
- Clique fora da tolerância → `null`.
- Linha e círculo próximos → retorna o mais próximo.

## Verificação
1. `dotnet test` — todos os testes passam (incluindo `SelectBehaviorTests`, `TrimBehaviorTests`, `ExtendBehaviorTests`).
2. Rodar o app, entrar em esboço, desenhar duas linhas paralelas bem próximas e um retângulo com um círculo dentro:
   - Com zoom afastado e aproximado, clicar em cada linha seleciona exatamente a linha sob o cursor; clicar a ~10px de distância não seleciona linha.
   - Clicar dentro do círculo seleciona a região do círculo; entre o círculo e o retângulo, a região do retângulo.
   - Trim/Extend só agem na linha realmente clicada.
