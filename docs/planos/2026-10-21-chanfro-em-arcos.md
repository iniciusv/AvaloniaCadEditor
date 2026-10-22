# Chanfro e arredondamento em arcos: um corte por arco, não por segmento

**Status:** Implementado (v1), falta medir no app · **Data:** 2026-10-21 (implementado em 2026-10-22) · **Branch:** `feature/referencial-qualquer-rotacao`

## Objetivo

Um `FilletFeature { ApplyToAllEdges = true }` sobre um corpo com furos (ou qualquer aresta em arco) tem de
sair em segundos, não em minutos. Para isso:

1. arestas em **arco de círculo** (boca de furo cilíndrico, topo de ressalto, canto arredondado de um perfil
   extrudado) são cortadas por **um anel de revolução por arco**, exato, em vez de um prisma por segmento da
   tesselação;
2. o usuário pode dizer que **os furos ficam sem chanfro** (furo de parafuso), sem precisar reordenar o código;
3. o que não for reconhecido continua no caminho atual, mas com um **teto**: a cena nunca mais fica 8 minutos
   construindo um sólido sem dizer nada.

## Situação atual

- [`OcctFilletBuilder`](../../AvaloniaCadEditor.Infrastructure/OpenCascade/OcctFilletBuilder.cs) tem três
  caminhos: cilindro (anel exato por revolução, `CreateCylinderCutter`), caixa (arestas exatas da
  `BoxFeature`) e **genérico por malha**: tessela o sólido, pega cada aresta viva convexa
  (`EdgeDetector.ClassifyEdges`) e faz **um `Cut` no sólido inteiro por aresta**, com até 3 tentativas
  (`CutterOverhangs`) e uma triangulação a cada corte aceito para conferir o volume (`TryCutKeepingSolid`).
- A boca de um furo vira `ProfileSegments` arestas (51 no app hoje). Medido em 2026-10-21 na peça de
  `AiCore/Cena.cs` (união de 2 prismas e uma caixa, 4 furos Ø4, chanfro de 2 mm, configuração do app):
  - chanfro **antes** dos furos: **1,5 s**;
  - chanfro **depois** dos furos: **não terminou em 8 min** (cerca de 4 × 2 × 51 = 408 segmentos de boca,
    a maioria falhando 3 vezes porque o prisma de cada segmento cruza os vizinhos).
- O `OcctNet.Wrapper` 0.1.1 **não tem chanfro/fillet B-Rep nem exploração de topologia**: exporta só
  primitivas, `Cut`/`Fuse`/`Common`, extrusão, revolução de 360°, translação e malha (conferido nos
  exports de `OcctNetNative.dll`). Por isso a solução é fazer cortadores melhores, não chamar o OCCT.
- Paliativos já feitos (2026-10-21): a `FilletTool` põe o chanfro antes da subtração
  (`RoslynFilletBeforeCutInserter`), e a geometria é construída fora da thread da UI, com exibição
  progressiva e cancelamento. Código escrito à mão ou pela IA com chanfro depois dos furos continua lento.

## Arquitetura

```
OcctFilletBuilder (caminho genérico)
  malha → arestas vivas convexas
    ├─ ArcEdgeFinder (Domain/Mesh, puro)  → arcos: centro, eixo, raio, lado (furo|ressalto), ângulos, segmentos
    │     └─ OcctFilletBuilder.CreateRingCutter → anel revolvido (∩ cunha se o arco não for completo)
    └─ o resto (retas) → prismas por aresta, como hoje (MergeCollinearEdges)
```

### 1. Reconhecer arcos na malha: `ArcEdgeFinder` (Domain/Mesh)

Algoritmo puro sobre `TriangleMesh` + `EdgeClassification`, ao lado do `EdgeDetector`, testável sem OCCT.

- Encadeia arestas vivas convexas que compartilham vértice (tolerância de posição) e têm a mesma
  **face plana** de um lado (normal constante ao longo da cadeia).
- Uma cadeia é arco se:
  - os pontos são coplanares e o plano é perpendicular à normal da face plana (o eixo do arco é essa normal);
  - todos os pontos estão à mesma distância do centro ajustado (tolerância da ordem de `LinearDeflection`);
  - o passo angular de cada segmento é de **tesselação** (≤ ~1,5 × 360°/`ProfileSegments`), o que separa um
    círculo de um polígono de verdade (um prisma sextavado tem passo de 60° e continua no caminho de retas);
  - a normal do outro lado é radial (parede cilíndrica paralela ao eixo).
- **Lado**: a normal da parede aponta para o eixo num **furo** (material fora do círculo) e para fora num
  **ressalto** (material dentro).
- Saída: `CircularEdge(Center, Axis, Radius, Side, StartAngle, SweepAngle, IsFullCircle, SegmentIndices)`.

### 2. Cortador anel: `CreateRingCutter` (Infrastructure)

Generaliza `CreateCylinderCutter`: o mesmo perfil (triângulo do chanfro ou quarto de círculo do
arredondamento), revolvido em torno do eixo do arco com `OcctProfileGeometry.RevolveProfile`.

- **Furo**: o perfil vai de `R` a `R + r` no plano da face e desce `r` pela parede. Nunca chega ao eixo, então
  o chanfro de 2 mm num furo de raio 2 deixa de ser degenerado.
- **Ressalto**: de `R − r` a `R`. Com `r ≥ R` o cone chega ao eixo: o arco fica sem chanfro e o motivo vai para
  `BuildProblems`.
- **Arco parcial** (canto arredondado): a revolução só faz 360°, então o anel é limitado por `Common` com uma
  **cunha** (prisma extrudado ao longo do eixo, com o setor do arco). A cunha termina exatamente nos ângulos
  de tangência; quem cobre a emenda é a sobra (`CutterOverhangs`) do prisma da reta vizinha.
- Volume do cortador pela fórmula de Pappus (área do perfil × 2π × raio do centroide × fração do setor), para
  o `TryCutKeepingSolid` continuar conferindo cada corte.

### 3. Integração no caminho genérico

- Os candidatos se dividem em arcos (anéis) e retas (prismas, como hoje). Os segmentos de um arco reconhecido
  saem da lista de retas.
- **Anéis em lote**: une os anéis (`FuseBalanced`) e faz **um** `Cut`, conferido pelo volume somado. Se o lote
  falhar, faz um anel por vez. Na peça medida, 8 anéis numa operação em vez de 408 prismas.
- Ordem: retas primeiro e anéis depois, para que a quina de um arco parcial já encontre a reta chanfrada.
  Validar com o teste do canto arredondado; se der errado, inverter.

### 4. Opção: furos sem chanfro

- `FilletFeature.SkipHoleEdges` (nome em aberto): com `true`, os arcos do lado **furo** são ignorados e só
  as arestas externas são chanfradas. Furo de parafuso não leva chanfro.
- É feature nova de verdade, então vale o checklist da §5.1 do ARCHITECTURE.md: `FeatureFingerprint` (a
  `FeatureFingerprintTests` cobra), `CadScriptRules` + exemplo, inspector (checkbox), `FilletTool` e
  `PartBuildFilletEditor` (onde o chanfro de peça é gerado). O golden do perfil `atual` muda com o texto das
  regras.

### 5. Rede de segurança: teto de cortes por aresta

Depois de tirar os arcos, se ainda sobrarem mais de **N** cortes de prisma (ex.: 200), o kernel não tenta:
registra em `BuildProblems` "*X: chanfro com N arestas sem forma reconhecida; aplique antes dos furos ou use
SkipHoleEdges*" e devolve o sólido sem o chanfro. Com isso, nenhum caso não previsto (furo inclinado, furo
que sai numa face curva, boca em superfície não plana) volta a ficar minutos construindo.

## Etapas

- [x] **Medição base**: a peça de `AiCore/Cena.cs` e uma placa com 12 furos viraram testes com limite de
      tempo (`Tests/BehaviorTests/FilletArcCutTests.cs`).
- [x] `ArcEdgeFinder` + testes (`Tests/Features/ArcEdgeFinderTests.cs`): furo passante com 32/48/51
      segmentos, ressalto, furo na borda (meia volta), sextavado (não é arco), furo inclinado (não é arco).
- [x] Cortador anel (`CreateArcCutter` + `RingProfile`) com volume de Pappus; `CreateCylinderCutter` usa o
      mesmo perfil.
- [x] Integração no caminho genérico: anéis em lote, e o lote que falha se divide ao meio até um anel só.
- [x] Testes de geometria: volume da placa (chanfro antes dos furos menos um escareado por boca), vértice em
      `R + r`, arredondamento, ressalto. O canto arredondado de perfil extrudado ficou só no teste de detecção.
- [x] Teto `MaxEdgeCuts` = 200 + aviso em `BuildProblems` (que volta na reexecução: a malha com aviso não
      vai para o cache por geometria).
- [x] `SkipHoleEdges`: `FilletFeature`, `FeatureFingerprint` (+ teste), regra no CadScript. O inspector não
      edita fillets, e `FactExtractor`/`FeatureCodeGenerator` não dependem da propriedade.
- [x] Regra do CadScript em `CadScriptRules.Common` (+ golden do perfil `atual`), ARCHITECTURE.md §4 e memória.
- [ ] Medir no app (`ProfileSegments` 51) e anotar aqui.

## Resultado (2026-10-22)

| Caso (tesselação do app) | Antes | Depois |
|---|---|---|
| Peça de `AiCore/Cena.cs`, chanfro de 2 mm depois de 4 furos Ø4 | > 8 min (não terminou) | 11,2 s |
| Idem, `SkipHoleEdges = true` | — | 3,1 s |
| Placa 80 × 60 com 12 furos, chanfro de 1 mm depois dos furos | — (centenas de cortes) | ≤ 12 s (tempo do teste, que também monta a referência) |

### O que mudou em relação ao planejado

- **Arco parcial sem cunha**: a revolução do wrapper aceita ângulo (`face.Revolve(eixo, ângulo)`), então o
  setor de anel sai direto, começando em `CircularEdge.StartDirection`.
- **Dois defeitos achados no caminho, e que valem fora do chanfro:**
  1. A conferência "a caixa não pode crescer" rejeitava todo corte válido com superfície curva: o OCCT sobe a
     tolerância do sólido (0,036 a 0,0625 mm) ao cruzar o cone com as facetas, e a caixa cresce igual nos 6
     lados. Agora a parte comum (a mediana dos 6 deslocamentos) é descontada.
  2. `OcctTessellator.FixTriangleWinding` parava a propagação da orientação em arestas não-manifold
     criadas por triângulos-lasca (dois vértices soldados num só) nessas costuras tolerantes. O miolo do furo
     saía como componente solto, orientado ao contrário: o volume conferido crescia o dobro do furo e a malha
     desenhada teria as normais do furo para dentro. Triângulo degenerado agora fica fora da vizinhança.
- Sem exemplo few-shot novo: a regra em `Common` basta, e um exemplo com chanfro depois de furos deixaria o
  teste dos exemplos lento.
- O tempo que sobra é quase todo booleana do OCCT (~450 ms por corte num sólido com muitas facetas). O lote de
  anéis já é um corte só; reduzir mais exigiria chanfro B-Rep, que o wrapper não tem.

## Perguntas em aberto

1. **Padrão de `SkipHoleEdges`**: `false` mantém o significado atual de "todas as arestas", e é o que proponho.
   Se a maioria dos furos das peças é para parafuso, `true` evita o escareado sem querer, mas muda a
   geometria das cenas que já têm chanfro depois de furo. Alguma tem hoje?
2. **Teto**: o valor de **N** e o que fazer ao passar dele. Proposta: o sólido sai sem chanfro, com aviso.
   A alternativa é chanfrar só os N primeiros, mas um chanfro pela metade engana mais que nenhum.
3. Furo que **não** é uma `CylinderFeature` (ex.: extrusão de um círculo do esboço) também é reconhecido,
   porque a detecção é pela malha e não pela árvore. Precisa de alguma exceção?

## Riscos

- **Tolerâncias da detecção**: uma tolerância frouxa toma polígono por arco (o anel corta onde não havia
  aresta); uma justa demais deixa o arco no caminho lento. Os testes do prisma sextavado e do furo com
  `ProfileSegments` 32/48/51 travam os dois lados. A memória já registra que quais cortes falham muda com
  esse valor.
- **Emenda de arco parcial com reta** (canto arredondado): a cunha e a sobra do prisma podem deixar um
  degrau ou um entalhe pequeno. Há um teste com vértice e volume.
- **Lote de anéis**: um anel ruim invalida o lote inteiro; o fallback um por um resolve, ao custo de mais
  cortes só nesse caso.
- `FilletFeature` girado continua proibido (§5.5); nada aqui muda isso.
