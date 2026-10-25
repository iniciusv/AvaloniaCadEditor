# Reconstrução "Perfil Base + Cortes" (substitui a Extrusão Multi-eixo)

## Contexto
O `MultiAxisExtrusionAlgorithm` entregue antes gera árvores confusas (envelope + dezenas de subtrações/interseções) para peças que um projetista modelaria com 3 sólidos: **um perfil base extrudado em X ou Y, menos um hexágono e um cilindro extrudados em Z**. Objetivo: chegar na malha **com precisão e com o menor número de sólidos possível**.

Ideia do usuário: o perfil base se repete ao longo do eixo de extrusão (aparece inteiro nas duas pontas); as diferenças no meio são consequência dos cortes.

Descoberta durante a análise: os "bugs do kernel" que relatei (União perdendo material, polígono côncavo com área errada, Cut com 5–20% de erro) **não são do OCCT**. Eles vêm de `TessellateShape` ([OpenCascadeGeometryKernel.cs:692-706](AvaloniaCadEditor.Infrastructure/OpenCascade/OpenCascadeGeometryKernel.cs#L692-L706)), que desvira cada triângulo para "apontar para longe do centróide dos vértices". Isso inverte triângulos em qualquer peça côncava, furo ou corpo múltiplo. O volume por divergência sai errado, a IoU por paridade de raio não é afetada (por isso deu IoU=1 com volume errado), e o sombreamento e o STL exportado ficam com normais invertidas. Precisa ser corrigido primeiro: sem isso nenhum teste de volume é confiável.

## Parte 1 — Corrigir a orientação da tesselação
Em `TessellateShape`, trocar a heurística do centróide por orientação consistente:
1. Soldar os vértices pela posição arredondada (o `posMap` que já existe ali) e obter um id canônico por vértice.
2. Montar o mapa aresta→triângulos com os ids canônicos. Fazer uma BFS por componente conexo, virando os triângulos para que cada aresta compartilhada seja percorrida em sentidos opostos.
3. Em cada componente, calcular o volume com sinal (divergência). Se for negativo, virar o componente inteiro.
4. Normais planas e suavização continuam como hoje, calculadas depois disso.

Testes de regressão em [OpenCascadeKernelTests.cs](AvaloniaCadEditor.Tests/BehaviorTests/OpenCascadeKernelTests.cs) (usando `MeshVolumeCalculator`, tolerância ≤1%):
- União de caixas em degrau → 3000.
- União de caixas afastadas → 250.
- Caixa menos cilindro → 874,3.
- Perfil L extrudado → 1760.

## Parte 2 — Algoritmo `BaseProfileCutsAlgorithm`
Novo arquivo `Infrastructure/Engines/BaseProfileCutsAlgorithm.cs`. Ele **substitui** o `MultiAxisExtrusionAlgorithm`: remover esse arquivo e trocar o registro em [App.axaml.cs](AvaloniaCadEditor.UI/App.axaml.cs). Implementa `IMultiCandidateReconstructionAlgorithm`, com um candidato por eixo base válido. Continua usando `MeshSlicer`, `AxisFrame` e `LoopFitting`, que já existem em `Engines/MultiAxis/`.

### 2.1 Perfil base (para cada eixo A ∈ {X,Y,Z})
1. Fatiar perto das duas pontas (W = min+δ e max−δ, com δ = max(tol, 0,5% do comprimento)) e no meio de cada camada entre os `FindCapLevels`.
2. Candidatos a perfil = contornos externos dessas fatias. Priorizar o contorno que aparece nas duas pontas com a mesma assinatura (área ±1%, bbox ±tol). Isso implementa o "perfil que se repete".
3. Um candidato só é **válido** se contém todos os vértices da malha projetados em (U,V), com tolerância (ponto na borda conta como dentro). Um corte só remove material, então a base precisa ser um superconjunto da peça.
4. Entre os eixos válidos, a base preferida é a de **menor volume** (área × comprimento), ou seja, o superconjunto mais justo. Base = `ExtrudeFeature(contorno, comprimento, A)`.

### 2.2 Cortes (para cada eixo B ∈ {X,Y,Z}, inclusive B = A)
- Fato-chave: nenhuma fatia de um prisma (em qualquer eixo principal) tem furo interno. Então **todo loop de furo numa fatia da malha é um corte**, e aparece naturalmente no eixo verdadeiro do corte. Um furo em Z, visto numa fatia X, vira entalhe aberto e não furo, então não gera duplicata.
- Níveis = `FindCapLevels(mesh, B)`, porque o fundo de um bolsão é uma tampa plana. Fatiar no meio de cada camada e coletar os furos com `MeshSlicer.BuildRegions`.
- Agrupar furos iguais (área ±2%, centróide ±tol) de camadas consecutivas: cada perfil distinto vira **um** corte cobrindo a sequência máxima de camadas em que ele cabe dentro do vazio. O cilindro atravessa também as camadas do hexágono, porque o hexágono o contém.
- Descartar cortes redundantes, ou seja, perfis cobertos em todas as suas camadas por outro corte com perfil que os contém.
- Se a faixa do corte encosta no min/max da peça em B, estender 1,0 além, como o algoritmo atual já faz com furos.
- Classificação:
  - ajuste de Kåsa com erro < 5% do raio → `CylinderFeature` na direção B;
  - senão → `ExtrudeFeature` de `PolygonProfile` já sem pontos colineares, o que dá 6 vértices para o hexágono.

### 2.3 Resultado
`BooleanFeature.CreateDifference(base, cortes...)` fica numa árvore plana de um nível, e `OperationCount = 1 + nº de cortes`. Para a peça do exemplo são 3 sólidos. O serviço segue escolhendo por IoU e desempata por `OperationCount`.

**Limitação conhecida (fica para depois):** cortes que abrem na lateral da fatia (entalhe/rasgo na borda) não aparecem como furo. Tratá-los exige diferença de polígonos 2D (ex.: Clipper2, base − fatia). Isso não é necessário para o exemplo hexágono + cilindro.

## Parte 3 — Limpeza
- Remover `MultiAxisExtrusionAlgorithm.cs` e o código de envelope/silhueta/híbrido.
- Remover `GetUVBounds` se ficar sem uso.
- Reescrever [MultiAxisReconstructionTests.cs](AvaloniaCadEditor.Tests/BehaviorTests/MultiAxisReconstructionTests.cs) como `BaseProfileCutsTests.cs` e tirar os comentários que culpavam o kernel.
- Manter [MeshSlicerTests.cs](AvaloniaCadEditor.Tests/BehaviorTests/MeshSlicerTests.cs).

## Testes do novo algoritmo
Peça de referência: perfil em U/L (côncavo) extrudado em Y, menos um bolsão hexagonal em Z (profundidade parcial, a partir da base), menos um cilindro passante em Z no mesmo centro. Assertivas:
- raiz = `BooleanFeature` Subtract com base `ExtrudeFeature` na direção ±Y e exatamente 2 ferramentas: um `CylinderFeature` (±Z, raio ±2%) e um `ExtrudeFeature` com 6 vértices (±Z);
- volume reconstruído dentro de 1% do original;
- via `ShapeReconstructionService`, a mesma árvore vence.

Casos extras:
- placa com furos em Z (regressão, base em Z + 2 cilindros);
- bloco com furo transversal em X.

## Verificação
- `dotnet build` e `dotnet test`. As 2 falhas pré-existentes em `ModelTreeAndPropertyInspectorTests` continuam fora do escopo.
- Manual: `dotnet run` na UI → importar o STL do exemplo → Reconstruir → conferir na árvore/CadScript 3 sólidos (base + hexágono + cilindro) e a sobreposição visual com a malha importada.
