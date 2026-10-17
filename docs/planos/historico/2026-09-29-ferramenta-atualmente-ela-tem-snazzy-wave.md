# Transição suave entre níveis do grid (crossfade)

## Contexto
O grid adaptativo já implementado em [GridRenderPass.cs](AvaloniaCadEditor.UI/Controls/Rendering/GridRenderPass.cs) muda de escala em saltos de potência de 10 (`scale = 10^ceil(log10(distance/100))`). Isso funciona para cobrir a área visível, mas o usuário notou que a troca é abrupta: ao cruzar um limiar de zoom, o grid "pula" instantaneamente de um espaçamento para o outro. O pedido agora é que essa troca seja progressiva — o grid mais grosso (nível seguinte) vai aparecendo aos poucos enquanto o mais fino (nível atual) vai desaparecendo, à medida que o zoom muda, e isso deve valer tanto para zoom-in quanto zoom-out, em qualquer nível.

## Abordagem escolhida: crossfade linear entre os dois níveis vizinhos, via alpha já existente
Não requer geometria nova nem shader novo: o fragment shader já recebe `uAlpha` (uniform `AlphaLoc`) e o blend GL já está habilitado globalmente (`SilkViewportControl.cs`, `Enable(Blend)` + `BlendFunc(SrcAlpha, OneMinusSrcAlpha)`). A mesma malha estática de linhas (já usada para o nível único) é desenhada **duas vezes por frame** — uma vez na escala do nível "fino" atual, outra na escala do nível "grosso" seguinte — cada uma com sua própria matriz de modelo e seu próprio valor de alpha. Coeficiente de fusão linear conforme a posição fracionária dentro da década (oitava de base 10) atual. Custo extra: só mais uma matriz de modelo e mais uma chamada `DrawArrays` da mesma geometria pequena (nenhum buffer novo, nenhuma alocação por frame).

### Mudanças em `GridRenderPass.Render` (substituindo o bloco de cálculo de escala/translação atual)
1. Calcular o nível contínuo (não mais arredondado com `ceil`):
   `level = log10(max(distance, epsilon) / BaseHalfSize)`
2. Nível inferior/superior e fração de transição:
   `L0 = floor(level)`, `frac = level - L0` (0..1)
   `scaleFine = 10^L0`, `scaleCoarse = 10^(L0+1)`
3. Alphas complementares (fade linear ao longo de toda a década):
   `alphaFine = 1 - frac`, `alphaCoarse = frac`
   — em `frac = 0` fica idêntico ao comportamento atual (só o grid fino, 100% visível); em `frac → 1` o fino já sumiu e o grosso está 100% visível, empatando com o próximo `L0`.
4. Repetir o cálculo de recentralização/snap (`tx, ty`) já existente **uma vez para cada escala** (`worldStepFine = BaseStep*scaleFine`, `worldStepCoarse = BaseStep*scaleCoarse`), já que o passo de snap difere entre os dois níveis.
5. Montar `gridMatrixFine` e `gridMatrixCoarse` (mesmo padrão `Scale * Translate * planeMatrix` já usado).
6. Desenhar: `DrawArrays` do grid com `alphaFine`/`gridMatrixFine`, depois de novo com `alphaCoarse`/`gridMatrixCoarse`, e por fim os eixos RGB (inalterado — continuam desenhados 1x, sem crossfade; usar `scaleCoarse` para a matriz dos eixos, já que sempre é ≥ a escala fina e garante que a linha "infinita" dos eixos sempre atravesse a tela toda mesmo em transição).
7. Restaurar `uAlpha = 1.0` teria efeito colateral em passes seguintes só se algum outro pass não resetar o valor antes de usar — os demais passes (`ProfileFaceRenderPass`, `SketchEntityRenderPass`) já setam seu próprio `AlphaLoc` antes de desenhar, então não é necessário resetar depois do grid.

### Sem mudanças necessárias em
- `CameraState.cs` (near/far dinâmico do commit anterior já cobre a faixa maior por causa do nível "coarse").
- Shader (`ViewportShaderManager.cs`) — `uAlpha` e blend já existem e já são exatamente o mecanismo certo.

## Arquivo
- `AvaloniaCadEditor.UI/Controls/Rendering/GridRenderPass.cs` (único arquivo alterado)

## Verificação
- `dotnet build AvaloniaCadEditor.UI/AvaloniaCadEditor.UI.csproj`.
- Rodar o app e dar zoom-out/zoom-in lento com a roda do mouse: observar o grid mais grosso surgindo gradualmente (mais claro) enquanto o mais fino esmaece, sem salto perceptível, em qualquer nível de zoom (não só perto da origem).
- Conferir que em repouso (sem zoom) o grid parece igual a antes (não deve haver "grid duplo" visível de forma permanente/estranha fora da transição).
- Testar em modo de esboço com workplane não-XY, igual à verificação anterior.
