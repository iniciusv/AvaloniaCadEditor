# Corrigir falso-positivo e deixar clara a detecção de quase-alinhamento (vermelho)

## Contexto
Testando só a extrusão do exemplo (sem furos), o usuário reporta **dois** problemas com a detecção vermelha de "quase-alinhamento" (`NearMiss`):

1. **`4.857` vs `4.858`: falso positivo.** Esses dois valores são os centros dos dois arcos arredondados do perfil (topo em Y=21, base em Y=-9) — ambos deveriam ser exatamente X=4.858. A diferença de 0.001 não é um erro de modelagem, é **ruído numérico do meu próprio algoritmo**: `DetectArcs`/`FinalizeRun` (em `ReferenceGuideService.cs`) calcula o centro do arco fazendo a **média dos circuncentros de cada trinca consecutiva de vértices**. Esse cálculo é numericamente instável perto das pontas do arco, onde a trinca de vértices é quase colinear (a curva é quase tangente à parede reta ali), e os vértices do perfil só têm 3 casas decimais — pequeno o suficiente para o resultado final "vazar" pelo `AlignTolerance` (1e-3) e virar dois clusters em vez de um.
2. **`10.858` vs `11`: real, mas incompreensível.** Aqui a causa é o design da UI, não um bug de cálculo: as duas coordenadas já recebem cada uma sua própria linha guia de cor normal (laranja, `Wall`) cobrindo toda a peça; o único sinal "vermelho" é um traço minúsculo de 0.142 unidades de comprimento conectando os dois pontos — invisível numa peça com dezenas de unidades. E o rótulo (`"10.858 ≠ 11"`) não diz quantas arestas sustentam cada valor, então o usuário não tem como julgar qual das duas é a "estranha" — daí a pergunta "não está claro qual medida está errada".

## Correção

### 1. Centro de arco mais estável (elimina o falso positivo 4.857/4.858)
Trocar, em `FinalizeRun`, a média dos circuncentros por um **ajuste de círculo por mínimos quadrados (método de Kása)** sobre todos os vértices do trecho do arco (não só os circuncentros individuais). A detecção de quais vértices pertencem ao arco continua igual (comparação tolerante de circuncentros consecutivos, só usada para achar a extensão do trecho); só o cálculo do centro final muda, ficando bem menos sensível ao ruído de arredondamento dos vértices.
- Novo helper privado `FitCircleLeastSquares(IReadOnlyList<Point3D> points)`: resolve o sistema normal 3×3 de `x²+y²+Dx+Ey+F=0` e devolve `(cx, cy, r) = (-D/2, -E/2, sqrt((D²+E²)/4 - F))`.
- `FinalizeRun` passa a receber também `local` (lista de vértices) e usar `local[start-1 .. end+1]` (o trecho real de vértices do arco, não só os índices com circuncentro calculado) como entrada do fit.
- Como margem de segurança adicional (e não como correção principal), subir `ReferenceGuideOptions.AlignTolerance` de `1e-3` para algo como `3e-3` — ainda 150× mais apertado que `NearMissTolerance` (0.5), então não deve gerar novos falsos "alinhamentos".

### 2. Deixar claro o que está em conflito (caso 10.858 vs 11)
Sem tentar adivinhar qual valor é o "certo" (isso depende da intenção do usuário, e adivinhar errado seria pior que não opinar) — só expor a informação de forma legível:
- **As duas linhas conflitantes ficam vermelhas, cobrindo toda a peça**, em vez de uma laranja "normal" + um traço vermelho de 0.142 de comprimento. Em `AnalyzeGroup`, identificar os `TightCluster` que caem em alguma vizinhança ambígua (near-miss) **antes** de chamar `EmitAxisLines`, e excluí-los dali; `EmitNearMisses` passa a desenhar, para **cada** cluster da vizinhança, uma linha tracejada vermelha de extensão completa (mesmo cálculo de span que uma linha normal usaria).
- **Rótulo com contagem de suporte**, sem veredito: `"X: 10.858 (4 arestas) vs 11 (2 arestas) — Δ0.142"`. Isso exige guardar em `TightCluster` a contagem bruta de candidatos (vértices/arestas) por cluster, não só a lista de features distintas.
- `GuideLabelsOverlay` e `ReferenceGuideRenderPass` não mudam — já leem `Label` e já colorem por `GuideKind.NearMiss` (vermelho); só o conteúdo do texto e a origem das linhas mudam.

### Arquivos
Só `AvaloniaCadEditor.Application/ReferenceGuides/ReferenceGuideService.cs` (novo helper de fit de círculo, `FinalizeRun`, `TightCluster`, `EmitNearMisses`, `AnalyzeGroup`) e `ReferenceGuideOptions.cs` (tolerância). Nenhuma mudança de UI/render/assinatura pública.

### Testes
- Novo teste: perfil `poly1` sozinho (sem furos, sem boolean) não deve gerar nenhum `GuideLine` de `Kind == NearMiss` com `Coordinate` próximo de 4.858 (cobre o caso do falso positivo).
- Atualizar `Analyze_ParedeComPequenoDesalinhamento_...`: rótulo passa a conter `"4 arestas"`/`"2 arestas"` e `"Δ0.142"`; novo assert de que **não** existe `GuideLine` `Wall`/`HoleRow` em X≈10.858 ou X≈11 (só `NearMiss`, de span completo).
- Rodar a suíte completa (`dotnet test`) ao final; as 9 asserções já existentes sobre o cenário completo (com furos) devem continuar passando.

## Contexto (legado — feature original)
Hoje, para conferir se um furo está na posição certa, só dá para olhar a coordenada absoluta no script. A proposta é um serviço isolado que analisa a cena e gera **linhas guia tracejadas + cotas** no viewport:
- **Linhas de furos:** furos com o mesmo X ou Y viram uma guia, com cota do passo entre linhas consecutivas (ex.: Y = -9, 1, 11, 21 → "3× 10").
- **Paredes:** arestas retas do perfil alinhadas aos eixos, centros de arcos e cota da linha de furos até a parede mais próxima.
- **Simetria:** pares em ±d (furos em ±6.5, paredes em ±11) geram uma linha de centro traço-ponto.
- **Quase-alinhamento:** coordenadas quase iguais mas diferentes aparecem em vermelho. No modelo de exemplo, 10.858 vs 11 no ponto `(11,-3)` parece um erro.

Exibição: um toggle global "Guias" na toolbar. Quando uma feature está selecionada, as guias e cotas ligadas a ela ficam destacadas e as demais esmaecidas.

## Princípios: isolamento e desempenho
- **Análise sob demanda.** Nada é calculado automaticamente. O cálculo só roda quando o usuário clica em "Analisar guias" (ou liga o toggle pela primeira vez). Ele não fica pendurado em `RefreshSolidMesh`.
- **Resultado em cache, marcado como desatualizado.** Quando a cena muda (script rodado, edição), o resultado **não é recalculado**. Ele só é marcado como `IsStale`: a UI mostra as guias esmaecidas, com um indicador "desatualizado — clique para reanalisar", ou as oculta.
- **Execução fora da UI thread.** O serviço é puro, então roda em `Task.Run` sobre um snapshot imutável da lista de features. Um novo pedido cancela o anterior via `CancellationToken`.
- **Custo de render próximo de zero.** O VBO das guias só é reconstruído quando o resultado muda ou o destaque de seleção muda, e não a cada frame. Em cada frame é só um `DrawArrays`. O comprimento do traço é fixo em unidades de mundo, calculado na análise, para não precisar regenerar os segmentos ao dar zoom. Os rótulos só são reposicionados quando a câmera muda e são poucos (dezenas de `TextBlock`).
- **Complexidade do algoritmo.** A coleta é O(n) em features e vértices. O clustering 1D é O(k log k), em que k é o número de coordenadas. A simetria é O(u²) sobre as coordenadas únicas de cada eixo, e u é pequeno (dezenas). Para cenas típicas o total fica bem abaixo de 1 ms, mas por segurança há limites nas opções: máximo de furos e máximo de pares de simetria.
- **Isolamento.** Todo o código fica em pastas próprias (`ReferenceGuides/`) em cada camada, com estado em um **ViewModel próprio**. `CadViewModel` e o `ViewportInputHandler` ficam intocados. Os pontos de contato com o código existente se resumem a 4 lugares, listados em "Arquivos".

## Arquitetura

### 1. Modelo de dados (Domain): `AvaloniaCadEditor.Domain/ValueObjects/ReferenceGuides.cs`
- `enum GuideKind { HoleRow, Wall, ArcCenter, Symmetry, NearMiss }`
- `record GuideLine(GuideKind Kind, Point3D Start, Point3D End, Axis2D Axis, double Coordinate, IReadOnlyList<Guid> SourceFeatureIds)`
- `record GuideDimension(Point3D From, Point3D To, double Value, string Label, IReadOnlyList<Guid> SourceFeatureIds)`
- `record ReferenceGuideSet(IReadOnlyList<GuideLine> Lines, IReadOnlyList<GuideDimension> Dimensions)` com `Empty`.

### 2. Serviço puro (Application): `Services/IReferenceGuideService.cs` + `ReferenceGuideService.cs`
Assinatura: `ReferenceGuideSet Analyze(IReadOnlyList<IFeature> features, ReferenceGuideOptions options)`. As opções são as tolerâncias: `AlignTol = 1e-3`, `NearMissTol = 0.5`, e o ângulo para considerar uma aresta "alinhada ao eixo".

Algoritmo:
1. **Coleta de furos.** Percorrer as features recursivamente pelos `Dependencies`. Toda `CylinderFeature` que é tool de um `BooleanFeature` Subtract, ou que tem `IsTransientTool`, é um furo. Agrupar por direção paralela (`GlobalDirection`), montar um frame 2D com `WorkPlane` para a normal (Z → XY) e projetar `GlobalBaseCenter`.
2. **Coleta de paredes.** Para `ExtrudeFeature` (e `BoxFeature`) cuja normal é paralela à direção do grupo, pegar `Profile.GetVertices()` em coordenadas world e projetar no mesmo frame:
   - uma aresta alinhada a u ou v gera uma coordenada de parede, com a extensão do segmento;
   - uma sequência de pelo menos 3 vértices equidistantes de um centro comum (dentro da tolerância) é um arco e gera `ArcCenter`, com centro e raio. Isso pega os semicírculos centrados em (4.858, 21) e (4.858, -9), que coincidem com os furos grandes.
   - O plano da guia fica no topo do sólido ao longo da normal (máximo da extrusão), para ficar visível.
3. **Clustering 1D por eixo (u e v).** Ordenar as coordenadas e agrupar com `AlignTol`:
   - cluster com 2 ou mais furos → `HoleRow`;
   - cluster com parede → `Wall`;
   - cluster misto → uma linha só, com todos os `SourceFeatureIds`.
   - A extensão da linha vai do mínimo ao máximo dos membros no outro eixo, mais uma margem.
4. **Cotas.**
   - Entre linhas de furos consecutivas no mesmo eixo. Se os passos forem iguais, gerar um único rótulo "n× p" além das cotas individuais.
   - De cada linha de furos até a parede paralela mais próxima.
5. **Simetria.** Para cada eixo, testar os centros candidatos c = (a+b)/2 dos pares de coordenadas. Se pelo menos 2 pares (furos ou paredes) forem simétricos em torno de c, emitir `Symmetry` em c.
6. **Quase-alinhamento.** Clusters vizinhos com distância entre `AlignTol` e `NearMissTol` viram `NearMiss`, com as duas coordenadas no rótulo (ex.: "10.858 ≠ 11").

É geometria pura, sem dependência de UI, e testável isoladamente.

### 3. Helper de projeção: `UI/ReferenceGuides/ScreenProjection.cs`
Um método estático `WorldToScreen(CameraState, Point3D, Size) : Point?`, com a mesma matemática de [ViewportInputHandler.cs:336-350](AvaloniaCadEditor.UI/Controls/Input/ViewportInputHandler.cs#L336-L350) (view*proj, divisão por W, Y invertido). Retorna null quando o ponto está atrás da câmera. Fica **dentro da feature**: `CameraState` e `ViewportInputHandler` não mudam. Unificar isso depois é opcional.

### 4. ViewModel próprio: `UI/ReferenceGuides/ReferenceGuidesViewModel.cs`
- Recebe `IReferenceGuideService`, um `Func<IReadOnlyList<IFeature>>` (snapshot de `Sketch.Features`) e `ISelectionService`.
- Estado:
  - `bool IsVisible`, persistido via settings provider, no padrão de `ShowInternalEdges`;
  - `ReferenceGuideSet? Result`;
  - `bool IsStale`;
  - `bool IsAnalyzing`;
  - `int Version`, incrementado a cada novo resultado ou mudança de destaque, para o render pass saber quando refazer o VBO.
- `AnalyzeCommand`: cancela a análise anterior, faz `await Task.Run(() => service.Analyze(snapshot, opts, ct))` e ao terminar atualiza `Result`, zera `IsStale` e incrementa `Version`.
- `MarkStale()`: só levanta a flag, sem calcular nada.
- Integração: é exposto como propriedade `CadViewModel.ReferenceGuides { get; set; }`, atribuída em `App.axaml.cs` do mesmo jeito que `ScriptEditor` e `AiChat`, então o construtor de `CadViewModel` não muda. A única linha nova em `CadViewModel` é `ReferenceGuides?.MarkStale()` no fim de `RefreshSolidMesh()`.

### 5. Render pass: `UI/ReferenceGuides/ReferenceGuideRenderPass.cs`
Segue o padrão de `GridRenderPass` e `SketchEntityRenderPass`, mas com **VBO em cache**: guarda o último `Version` e só refaz o buffer quando ele muda. Quando `IsVisible` é falso ou `Result` é null, retorna imediatamente.
- Vértices com 6 floats (posição + cor) desenhados com `DrawArrays(Lines)`.
- **Tracejado gerado na CPU**, com helper `AddDashedLine(p1, p2, dash, gap, color)`. O comprimento do traço é fixo em mundo, cerca de 1% da diagonal do bbox da cena. O padrão traço-ponto é usado para simetria.
- Cores: HoleRow ciano, Wall laranja, ArcCenter amarelo, Symmetry verde, NearMiss vermelho.
- Com seleção ativa (`SelectionService`), linhas cujos `SourceFeatureIds` não incluem a feature selecionada recebem alpha de cerca de 0.25.
- Depth test fica ligado com `DepthFunc Always` (ou `DepthMask false`), para as guias aparecerem sobre o sólido.
- As cotas são desenhadas como linha fina com setas ou ticks.
- O pass é registrado por último na lista em [SilkViewportControl.cs:36-42](AvaloniaCadEditor.UI/Controls/SilkViewportControl.cs#L36-L42), com uma linha. O pass se inscreve sozinho no `PropertyChanged` do `ReferenceGuidesViewModel` para pedir redraw, então `ViewModel_PropertyChanged` não muda. Ele recebe um callback `requestRedraw` no construtor.
- A seleção só muda alpha, então o VM incrementa `Version` e o VBO é refeito uma vez, não a cada frame.

### 6. Rótulos: `UI/ReferenceGuides/GuideLabelsOverlay.cs`
Um `Canvas` Avalonia posicionado sobre o GL control, no `Panel` do viewport em `Views/MainWindow.axaml` (~128-145):
- um `TextBlock` por cota ou NearMiss, no ponto médio projetado via `ScreenProjection`;
- os `TextBlock`s são criados só quando `Version` muda; quando a câmera muda, só `Canvas.Left/Top` é atualizado;
- `IsHitTestVisible = false`;
- colapsado quando `IsVisible` é falso.

### 7. Toolbar
Na toolbar de `MainWindow.axaml`, ao lado de `ShowInternalEdges` (~linha 65):
- CheckBox "Guias" bindado a `ReferenceGuides.IsVisible`;
- botão "Analisar", bindado a `ReferenceGuides.AnalyzeCommand`, com indicador de desatualizado (`IsStale`) e de ocupado (`IsAnalyzing`).
- Ligar o CheckBox sem nenhum resultado dispara a primeira análise.
- Os textos ficam em `Resources/Strings.resx` e `Strings.cs`.

## Arquivos
**Novos**, todos em pastas `ReferenceGuides/` próprias:
- Domain: `ReferenceGuides/ReferenceGuideSet.cs`
- Application: `ReferenceGuides/IReferenceGuideService.cs`, `ReferenceGuideService.cs` e `ReferenceGuideOptions.cs`
- UI:
  - `ReferenceGuides/ReferenceGuidesViewModel.cs`
  - `ReferenceGuideRenderPass.cs`
  - `GuideLabelsOverlay.cs`
  - `ScreenProjection.cs`
- Tests: `ReferenceGuides/ReferenceGuideServiceTests.cs`

**Pontos de contato com o código existente** (só adições, nada refatorado):
1. `CadViewModel.cs`: a propriedade `ReferenceGuides` e a linha `ReferenceGuides?.MarkStale()` em `RefreshSolidMesh()`.
2. `App.axaml.cs`: instanciar o serviço e o VM e atribuir `cadVm.ReferenceGuides`.
3. `SilkViewportControl.cs`: 1 linha registrando o render pass.
4. `MainWindow.axaml` + `Strings`: CheckBox, botão e overlay.

Para remover a feature, basta apagar as pastas `ReferenceGuides/` e desfazer esses 4 pontos.

## Verificação
1. **Testes xUnit** (`ReferenceGuideBehaviorTests`, nomes em PT, FluentAssertions). Avaliar o script de exemplo via `RoslynSceneEvaluator` e rodar `Analyze`. Asserts:
   - HoleRow em Y = 1 e Y = 11, com 2 furos cada, e em X = ±6.5;
   - rótulo de passo "3× 10" em Y;
   - Symmetry em X = 0;
   - Wall em X = -11 e X = -1.142;
   - ArcCenter em (4.858, 21) coincidindo com o furo 6;
   - NearMiss entre 10.858 e 11;
   - uma cena sem furos retorna um conjunto sem HoleRow;
   - o teste do VM confirma que `RefreshSolidMesh` só marca `IsStale` e **não** chama `Analyze` (mock do serviço com Moq, `Times.Never`);
   - o teste do VM confirma que `AnalyzeCommand` chama `Analyze` uma vez.
2. Rodar `dotnet test`.
3. **Manual** (skill `run`): abrir o app, colar o script, ligar "Guias", orbitar a câmera (rótulos acompanham), selecionar um furo (guias relacionadas destacadas) e desligar o toggle (nada é desenhado).
