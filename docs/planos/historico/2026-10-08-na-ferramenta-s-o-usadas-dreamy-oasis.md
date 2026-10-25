# Controle de densidade das cotas + peças aninhadas como componentes

## Contexto

Dois problemas apareceram no uso do desenho de cotas.

**1. Pernas que cruzam outras cotas.** Linhas de chamada de cotas de fileiras externas atravessam linhas de cota mais internas.

A causa principal está em [DimensionLayout.cs](AvaloniaCadEditor.Drafting/DimensionLayout.cs): dentro de um nível, os grupos são ordenados pelo **início** do intervalo. Em pares simétricos aninhados, isso põe a cota **maior** por dentro. No `BlocoFixacaoBelt`, a ordem fica 32, 29, 19,77 e 11, e as pernas das de fora cruzam as de dentro. A regra de desenho técnico é a inversa: a menor por dentro, a maior por fora.

Além disso, o usuário quer que o serviço **decida não mostrar** níveis de cota quando a densidade ou os cruzamentos passarem do limite, com aviso e com toggles por nível numa **legenda flutuante no canto superior direito da cena 3D**, no estilo de legenda de mapa.

**2. Peças pré-definidas reutilizadas** (`new FuroPorca(...).Instance("fixacao_x_1")`, commit `081df19`). Uma `FuroPorca` é furo mais alojamento hexagonal mais canal. No desenho da hospedeira, basta cotar o **eixo central**: as medidas internas são parâmetros da sub-peça e se repetem.

Hoje o `FactExtractor` já atravessa a `PartInstance` aninhada e nomeia os fatos `fixacao_x_1/furo` e `fixacao_x_1/alojamento`. O desenho, porém, cota cada um por dentro: Ø3,4, chave 5,83, bordas do canal. Na `PlacaSuporteCarrinhoY` são 9 instâncias, o que explica boa parte da densidade.

### Decisões do usuário

- **Densidade:** esconder automaticamente por prioridade e mostrar "N cotas ocultas pela densidade".
  - A ordem é: localização (nível 2) primeiro, depois as cadeias que mais cruzam. Os totais nunca saem.
  - Há toggles para mostrar os níveis ocultos, numa legenda flutuante no canto superior direito da viewport.
- **Componentes:** **toda** peça aninhada (qualquer `PartInstance` dentro do `Build()`) vira caixa-preta no desenho da hospedeira. Ela é cotada só pela referência (o eixo), sem medidas internas.
- **Rótulo:** o ideal é um rótulo definido pela peça, mas sem mexer muito agora nem nas regras da IA.
  - Fica um **gancho opcional** na `Part`, não documentado no dialeto.
  - Na falta dele, o padrão é o tipo mais os valores dos parâmetros enum, por exemplo `FuroPorca M3 Lateral`.

---

## Parte A: layout e densidade

### A1. Menor por dentro ([DimensionLayout.cs](AvaloniaCadEditor.Drafting/DimensionLayout.cs))

Dentro de um nível, ordenar os grupos pela **extensão do intervalo** (crescente) e, em caso de empate, pelo início. O encaixe por intervalos continua igual.

Só isso elimina os cruzamentos dos pares aninhados (11 → 19,77 → 29 → 32, de dentro para fora). Também reduz os cruzamentos entre cadeias que partem da mesma borda.

### A2. Contagem de cruzamentos (novo `DimensionCrossings.cs`, Drafting)

`Count(IReadOnlyList<DraftDimension> placed, double gap)` devolve, para cada cota, quantas outras as pernas dela cruzam.

- **Perna:** o segmento do ponto do elemento até a linha de cota. Para uma cota horizontal embaixo, é o segmento vertical em u = From.U (e outro em To.U), de From.V até `LinePosition`.
- **Cruzamento:** outra cota Q do **mesmo lado** cuja linha fica estritamente entre as duas pontas da perna, e cujo intervalo contém o u da perna estritamente.
  - O intervalo de Q inclui o espaço do texto, com a mesma estimativa do `DimensionLayout.Interval`, que será extraída para reuso.
  - Uma perna que coincide com a ponta de Q (dentro da tolerância) não conta, porque é cadeia, não cruzamento.
- Pernas de lados diferentes não se alcançam e ficam fora da contagem.

### A3. Política de densidade (novo `DensityPolicy.cs`, Drafting)

Opções em `DraftingOptions`:

| Opção | Padrão | Significado |
|---|---|---|
| `MaxRowsPerSide` | 6 | fileiras por lado da peça |
| `MaxCrossingsPerSide` | 0 | cruzamentos tolerados por lado |
| `ForcedLevels` | vazio | níveis que o usuário mandou mostrar mesmo ocultos |

Algoritmo, **por lado** e iterativo:
1. Posicionar os grupos com `DimensionLayout.Place` e medir as fileiras e os cruzamentos (A2).
2. Se o lado excede algum limite, esconder o próximo candidato:
   - **primeiro**, todos os grupos de **nível 2** (localização) daquele lado;
   - **depois**, os grupos de **nível 1** do lado, **um por vez**, começando pelo que mais causa cruzamentos (soma das pernas dele que cruzam, mais as pernas de outros que cruzam ele).
3. Repetir até caber ou não haver mais candidatos.

Nunca saem:
- o **nível 3** (totais);
- as **chamadas** (diâmetros e componentes, que não têm perna);
- as **cotas manuais**;
- grupos de nível em `ForcedLevels`.

Devolve o que foi posicionado, mais `Suppressed` (as cotas escondidas, com nível e motivo: "fileiras" ou "cruzamentos").

### A4. Modelo e encadeamento

- `DraftingSheet` ganha `IReadOnlyList<DraftDimension> Suppressed`.
- `DraftingAnalyzer.Analyze(input, AnalysisOptions? options = null, DraftingOptions? drafting = null)`: planner → `DensityPolicy` (que chama o layout) → chamadas.
- Nomes dos níveis num lugar só, `DimensionLevels`: 1 "Cadeias e tamanhos", 2 "Localização", 3 "Totais".

### A5. Legenda flutuante (UI)

**Novo controle `UI/Drafting/DraftingLegend.axaml(.cs)`:**
- um `Border` semitransparente, arredondado, como uma legenda de mapa;
- fica no `Panel` da viewport em [MainWindow.axaml](AvaloniaCadEditor.UI/Views/MainWindow.axaml), ao lado do `DraftingLabelsOverlay`, com `HorizontalAlignment=Right`, `VerticalAlignment=Top` e margem de 8;
- visível quando `Drafting.IsVisible` e há folha;
- recebe clique: o `Border` tem fundo, então não deixa o clique passar para a viewport.

**Conteúdo:**
- uma linha por nível presente: o título e "n visíveis · m ocultas", com um `CheckBox` **"mostrar ocultas"** habilitado só quando m > 0 ou o nível está forçado;
- no topo, "N cotas ocultas pela densidade", quando N > 0.

**No `DraftingViewModel`:**
- `ObservableCollection<LevelSummary> Levels`, onde `LevelSummary` tem `Level`, `Title`, `Visible`, `Hidden` e `ForceShow`, que notifica;
- `SuppressedCount`;
- mudar `ForceShow` atualiza `ForcedLevels` e chama `Recompute()`;
- os níveis forçados são guardados por peça e vista, como as cotas escondidas manualmente, e se perdem ao sair do isolamento.

A propriedade `Dimensions` continua sendo o que se desenha, e o render pass e o overlay não mudam.

---

## Parte B: peças aninhadas como componentes

### B1. Gancho de rótulo ([Part.cs](AvaloniaCadEditor.Domain/Features/Part.cs))

`public virtual string? DrawingLabel => null;`: o texto da chamada desta peça quando ela aparece aninhada noutro desenho. Nenhuma peça o sobrescreve agora.

**Não** entra no `CadScriptRules` nem nos exemplos. O `.cad/api.md`, gerado por reflexão, vai listá-lo automaticamente, e isso é esperado.

### B2. Fatos marcados com o componente ([FactExtractor.cs](AvaloniaCadEditor.Analysis/FactExtractor.cs), [Facts.cs](AvaloniaCadEditor.Analysis/Facts.cs))

- Novo `ComponentRef(string Name, string TypeName, string Label)`.
- `PrimitiveFact.Component` (nulo para sólidos do próprio `Build()`).
- No `Walk`, ao entrar numa `PartInstance` **aninhada** (dentro do sujeito), quando ainda não há um componente em curso, abre-se `ComponentRef(instance.Name, instance.PartTypeName, Label)`. Os níveis mais internos herdam o **mais externo**: a hospedeira só enxerga a instância que ela própria usou.
- `Label` = `part.DrawingLabel` ou, na falta dele, o tipo mais os valores dos parâmetros enum de `part.GetParameters()`. Exemplo: `FuroPorca M3 Lateral`.

### B3. Referência do componente ([RelationAnalyzer.cs](AvaloniaCadEditor.Analysis/RelationAnalyzer.cs), [ModelRelations.cs](AvaloniaCadEditor.Analysis/ModelRelations.cs))

**`ModelRelations.Components`.** Cada item é um `ComponentDatum(Ref, Members, Principal, AxisPoint, Axis?, BoundsMin, BoundsMax)`. A referência é escolhida assim:
- **eixo:** quando todos os membros redondos são coaxiais, o eixo é o do redondo mais longo (`Principal`). Na `FuroPorca`, é o eixo do furo;
- **sem membro redondo, ou redondos não coaxiais:** sem eixo, e a referência é o centro da caixa.

**`LinearPatterns`:**
- os padrões de furos simples **excluem** os membros de componentes (uma `FuroPorca` não forma fileira com um furo Ø3,4 avulso);
- entram **padrões de componentes**: referências com o mesmo `Label` e a mesma direção de eixo, na mesma transversal, com o mesmo cálculo de passo;
- `LinearPattern` ganha `string? ComponentLabel`, e os membros são os `Principal` (ou o primeiro membro, sem eixo). A compactação "3× 20 (=60)" do planner funciona sem mudança.

As **guias de referência continuam iguais**: os furos internos seguem sendo furos para elas.

### B4. Desenho ([DimensionPlanner.cs](AvaloniaCadEditor.Drafting/DimensionPlanner.cs))

**Entidades membro de componente** (`e.Fact.Component != null`) ficam **fora** de:
- marcas cotáveis;
- `SideSizes` (Ø de lado e chave de prisma);
- `FrontDiameters`.

Elas continuam projetadas e desenhadas: âncoras para cota manual, linhas de centro e emendas.

**Marcas do componente**, a partir da referência projetada:
- eixo perpendicular à vista: o ponto de centro, nos dois eixos;
- eixo ao longo de U: posição em V da linha de centro; ao longo de V: posição em U;
- sem eixo: o centro da caixa nos dois eixos.

A fonte de cada marca é o **nome do componente**, e o fato é o `Principal`.

**Chamada do componente.** Novo `DimensionKind.Callout`. Os componentes visíveis são agrupados por `Label`, e cada grupo gera uma chamada `"3× FuroPorca M3 Axial"`, com a contagem de posições distintas na vista.
- A chamada sai do círculo do `Principal` quando o eixo é perpendicular à vista; senão, sai da referência com um raio pequeno (`gap × 0,2`).
- No modo simétrico, o representante é o do lado mantido.
- A geometria da chamada reaproveita o ramo padrão de `DimensionGraphics.Build`.

**Clique na chamada ou na marca:** a fonte é `fixacao_x_1`, então `RevealPartTool(tipoHospedeira, "fixacao_x_1")` acha a declaração `var fixacao_x_1 = new FuroPorca(...).Instance(...)`. O tracer já reconhece `.Instance` (commit `081df19`). Os rótulos de parâmetro só valem para `Diameter`, não para `Callout`.

---

## Arquivos críticos

| Arquivo | Mudança |
|---|---|
| `AvaloniaCadEditor.Drafting/DimensionLayout.cs` | ordenação pela extensão; `Interval` reutilizável |
| `AvaloniaCadEditor.Drafting/DimensionCrossings.cs`, `DensityPolicy.cs`, `DraftingOptions.cs` (novos) | cruzamentos, política, opções |
| `AvaloniaCadEditor.Drafting/DraftingModel.cs`, `DraftingAnalyzer.cs` | `Suppressed`, `Callout`, `DimensionLevels`, encadeamento |
| `AvaloniaCadEditor.Drafting/DimensionPlanner.cs` | componentes (marcas, exclusões, chamadas) |
| `AvaloniaCadEditor.Analysis/Facts.cs`, `FactExtractor.cs` | `ComponentRef` nos fatos |
| `AvaloniaCadEditor.Analysis/ModelRelations.cs`, `RelationAnalyzer.cs` | `Components`, padrões de componentes, exclusão nos padrões de furos |
| `AvaloniaCadEditor.Domain/Features/Part.cs` | `virtual string? DrawingLabel` |
| `AvaloniaCadEditor.UI/Drafting/DraftingViewModel.cs`, `DraftingLegend.axaml(.cs)` (novo), `Views/MainWindow.axaml` | legenda, níveis forçados, contagem de ocultas |
| `ARCHITECTURE.md` §4 | onde mexer em densidade, componentes e legenda |

## Verificação

`dotnet test` com o `-p:BaseOutputPath` alternativo. Ficam só as 5 falhas já conhecidas.

**Testes novos**

- **Layout:** quatro pares aninhados (spans 11, 19,77, 29 e 32) ficam com a menor por dentro e **zero** cruzamentos. `DimensionCrossings` conta 1 num caso montado (perna atravessando uma cota interna) e 0 numa cadeia que encosta na ponta.
- **Densidade:**
  - caso sintético com 8 cadeias que se cruzam: o nível 2 sai primeiro, o nível 3 permanece e as cotas manuais e as chamadas não saem;
  - com `ForcedLevels = {2}`, o nível 2 aparece mesmo acima do limite;
  - `Suppressed` lista as escondidas com o nível.
- **Componentes, na `PlacaSuporteCarrinhoY` real** (cena AiCore, como no teste do `BlocoFixacaoBelt`), vista Top:
  - não existe cota de chave 5,83, nem Ø3,4 vinda de `*/furo`, nem bordas de canal;
  - existe a chamada `3× FuroPorca M3 Axial` (porca_inferior_1/2/3);
  - os três furos `furo_fixacao_carrinho_*` continuam com Ø3,4 próprio;
  - a fonte das marcas de componente é o nome da instância (`porca_inferior_2`, ...).
- **`FactExtractor`:** fatos dentro de `.Instance(...)` têm `Component.Name` igual à instância, e o `Label` padrão traz os enums. Uma peça com `DrawingLabel` sobrescrito (record de teste) usa o rótulo dela.
- **`RelationAnalyzer`:** três `FuroPorca` alinhadas formam um padrão de componente com passo, e um furo avulso de mesmo Ø não entra nele.

**Outras verificações**

- Os 12 testes das guias continuam passando sem mudança.
- `AvaloniaCadEditor.UI.exe check AiCore --geometry` sai com código 0.
- **No app** (verificação visual com o usuário):
  1. Isolar a `placa_suporte_carrinho_y`, ir para Top, ligar Ortho e Dims. Conferir as chamadas dos componentes, a ausência de medidas internas e a legenda no canto superior direito com as contagens.
  2. Marcar "mostrar ocultas" num nível e conferir que as cotas voltam.
  3. Isolar o `bloco_fixacao_belt` (Front): os pares devem aparecer da menor (dentro) para a maior (fora), sem pernas cruzando.
