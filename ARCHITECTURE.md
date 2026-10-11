# Arquitetura do AvaloniaCadEditor

Mapa do código para quem vai **alterar o aplicativo** — humano ou IA. Diz onde cada coisa
mora, quais invariantes não podem ser quebradas e onde intervir para cada tipo de mudança.

> **Este arquivo não é sobre o dialeto das cenas.** As regras do CadScript (como escrever
> `Cena.cs` e `Pecas/*.cs`) vivem em [`CadScriptRules.cs`](AvaloniaCadEditor.Application/Ai/CadScriptRules.cs),
> fonte única consumida pelo system prompt, pelo `.cad/api.md` e pelo `AGENTS.md` gerado
> dentro de cada pasta de cena. Se você está editando uma **cena**, leia o `AGENTS.md` dela,
> não este arquivo. Se você está editando o **app**, é aqui.

---

## 1. A ideia central

**A cena é um programa C#, e esse programa é a única fonte da verdade.**

Não existe um modelo de documento em memória que o código "exporta". É o contrário: o código
é compilado e executado, e o resultado da execução *é* a cena. Toda interação da UI que muda
geometria é uma **reescrita do código-fonte seguida de reexecução**.

Três consequências que explicam quase todo o resto do repositório:

1. **Ferramentas não constroem geometria.** `ExtrudeCommand`, `SweepCommand`, `BooleanTool`,
   `FilletTool` emitem CadScript pelo evento `CodeGenerated`; quem executa é o editor de script.
2. **O inspector de propriedades é um editor de texto.** `IPartCodeEditor` só tem métodos que
   reescrevem o fonte e reexecutam (`SetPartParameterAsync`, `MovePartInstanceAsync`, …).
   Arrastar uma peça reescreve o `at:` no `.cs`.
3. **Não há "salvar modelo".** Salvar é gravar os `.cs`. O disco é a fonte da verdade no modo
   projeto, e um `FileSystemWatcher` recarrega quando uma IA externa escreve na pasta.

`FeatureOrigin` (uma `ConditionalWeakTable` preenchida via `CallerArgumentExpression`,
`CallerFilePath` e `CallerLineNumber`) liga cada feature de volta ao arquivo/linha/variável que
a criou. É o que transforma seleção na viewport em navegação no código.

---

## 2. Projetos e regra de dependência

```
Domain  ←  Application  ←  Infrastructure  ←  UI
             ↑
          AiCore

Domain  ←  Analysis  ←  Drafting  ←  UI
              ↑
         Application
```

| Projeto | TFM | Papel |
|---|---|---|
| `AvaloniaCadEditor.Domain` | `net10.0` | Features, perfis, value objects, malha, API `Scene` dos scripts. **Zero dependências.** |
| `AvaloniaCadEditor.Application` | `net10.0` | Ferramentas, serviços de cena, mutadores Roslyn, camada de IA. **Zero referências a Infrastructure.** |
| `AvaloniaCadEditor.Infrastructure` | `net10.0-windows` x64 | OCCT (`OcctNet.Wrapper`), Roslyn evaluator/navigator, reconstrução, STL, settings JSON. |
| `AvaloniaCadEditor.UI` | `net10.0-windows` x64 | Avalonia 12 + Silk.NET OpenGL, ViewModels, render passes, CLI `check`. |
| `AiCore` | `net10.0` | Cliente LLM e `IAiProtocol`, sem dependências externas (BCL só). **Também é uma pasta de cena** (ver §7). |
| `AvaloniaCadEditor.Analysis` | `net10.0` | Análise de modelo, pura: `FactExtractor` (o **único** percurso da árvore que acumula `Placement`s → `PrimitiveFact`s com papel e nome estável) e `RelationAnalyzer` (alinhamentos, quase-alinhamentos, coaxiais, padrões com passo, planos de simetria). **Só depende do Domain.** Consumida pelo desenho e pelas guias de referência. |
| `AvaloniaCadEditor.Drafting` | `net10.0` | Desenho técnico da peça isolada: projeta os fatos numa vista padrão, escolhe as cotas com hierarquia e as encaixa em fileiras. Depende de Domain e Analysis; a UI o consome (`UI/Drafting/`) e o core não depende dele. |
| `AvaloniaCadEditor.Tests` | `net10.0-windows` | xunit + FluentAssertions + Moq. ~445 testes. |

**A regra:** a seta nunca inverte. `Application` não conhece `Infrastructure` — é por isso que o
kernel geométrico pôde ser trocado três vezes sem tocar em regra de negócio nem em UI (ver
[`GEOMETRY_KERNEL_HISTORY.md`](GEOMETRY_KERNEL_HISTORY.md)). **Não quebre isso.** Se precisar de
uma capacidade nova de infraestrutura em `Application`, declare a interface em
`Application/Interfaces/` e implemente em `Infrastructure/`.

Hoje a `UI` referencia `Infrastructure` diretamente em 4 arquivos. Dois são legítimos
(`App.axaml.cs` e `Cli/CheckCommand.cs`: composição e CLI); os outros dois são a dívida
registrada em §6.2.

---

## 3. Os quatro fluxos

### 3.1 Executar a cena

```
SceneScriptViewModel.RunCodeAsync()
  → SaveDocumentsToDisk()                      (modo projeto: disco é a verdade)
  → ISceneEvaluator.EvaluateAsync(documents)
      RoslynSceneEvaluator: CSharpCompilation → AssemblyLoadContext colecionável
      → executa o entry point dentro de SceneHost.Begin(builder)
      → Scene.AddFeature/Add alimentam o SceneBuilder
  → MeshManager.ResetForNewScene()             (preserva cache por fingerprint)
  → Sketch.ClearFeatures() + AddFeature(...)
  → CadViewModel.RefreshProfiles()             (refaz malha composta + árvore)
```

### 3.2 Uma ferramenta "cria" geometria

```
clique na viewport
  → ViewportInputHandler → CadViewModel.OnPointerPressed
  → ICommandTool.OnMouseDown
  → tool emite CodeGenerated(snippet)          via FeatureCodeGenerator
  → CadViewModel.CurrentTool_CodeGenerated
  → SceneScriptViewModel.AppendCode(snippet)
  → RunCodeAsync()                             → volta ao fluxo 3.1
```

Só 4 das 14 ferramentas emitem código (Extrude, Sweep, Boolean, Fillet). As ferramentas de
sketch (linha, círculo, polígono, trim, extend, offset, move, rotate, select, workplane)
declaram o evento `CodeGenerated` mas nunca o disparam — daí os 20 avisos CS0067 no build.

### 3.3 Editar um parâmetro pelo inspector

```
PropertyInspectorViewModel
  → IPartCodeEditor.SetPartParameterAsync(instance, nome, texto)
  → PartSourceEditor (Roslyn: árvore sintática + edição pontual de texto)
  → RunCodeAsync()
```

A edição é **sintática e pontual**: formatação, comentários e lógica escrita à mão sobrevivem.
`PartArgument.IsEditable` é falso quando o valor no código não é um literal que o app possa
reescrever sem perder lógica.

### 3.4 Construir a malha (e os três caches)

```
ISceneMeshManager.GetFeatureMesh / BuildCompositeMesh
  → IGeometryKernel.BuildFeatureMesh
      ├─ _meshByGeometry[FeatureFingerprint + tesselação]   ← sobrevive à reexecução
      ├─ _definitionMeshCache[Id da definição da peça]      ← por execução
      └─ BuildShape → _shapeCache[IFeature.Id] (B-Rep OCCT) ← por execução
         → OcctTessellator.Tessellate
```

| Cache | Chave | Sobrevive a reexecução? |
|---|---|---|
| `_shapeCache` (B-Rep) | `IFeature.Id` | não |
| `_definitionMeshCache` | `Id` da definição | não |
| `_meshByGeometry` | `FeatureFingerprint` + settings | **sim** |

`FeatureFingerprint` é tipo + parâmetros geométricos + filhos, **sem Id e sem nome**. Como cada
execução cria Ids novos, é só ela que permite mover uma peça sem refazer booleana e tesselação
de todo o resto. Falha segura: fingerprint `null` = sem cache, nunca cache errado.

---

## 4. Onde intervir

| Quero… | Mexa em | Lembre de |
|---|---|---|
| **Adicionar um tipo de feature** (ex.: `LoftFeature`) | `Domain/Features/XFeature.cs` | ver §5.1 — são **7** pontos obrigatórios |
| Adicionar uma ferramenta interativa | `Application/Commands/XCommand.cs` + comando no `CadViewModel` + botão no `MainWindow.axaml` + `Resources/Strings.cs` | a ferramenta **emite código**, não constrói geometria |
| Mudar como uma feature vira sólido | `Infrastructure/OpenCascade/OpenCascadeGeometryKernel.cs` → `BuildShape` | registre falha com `ReportProblem`, não lance para fora |
| Mudar a tesselação / arestas | `Infrastructure/OpenCascade/OcctTessellator.cs`, `Domain/Settings/TessellationSettings.cs` | settings novos entram em `GeometryKey()` |
| Mudar o código gerado pelas ferramentas | `Application/Services/FeatureCodeGenerator.cs` | respeite a regra de sólidos rastreáveis (§5.2) |
| Editar o fonte da cena a partir da UI | `Application/Scenes/PartSourceEditor.cs` ou um `Roslyn*` em `Application/Services/` | edição sintática pontual, nunca reescrita do arquivo |
| Mudar a conversão de sólido solto em peça | `Infrastructure/Scripting/RoslynPartConverter.cs` (atrás de `IPartConverter`), `Application/Scenes/PartConversionCheck.cs` | puro: não grava nem executa. O `SceneScriptViewModel.ConvertToPartAsync` avalia a cena convertida e só aplica se o `FeatureFingerprint` do sólido não mudar. Literais continuam literais; só variáveis que ficam na cena viram parâmetros |
| Mudar o que a IA vê | `Application/Ai/Context/AiContextBuilder.cs` | o motivo de cada arquivo é rastreável (`AiContextReason`) |
| Mudar o que a IA pode escrever | `Application/Ai/Context/AiEditScopePolicy.cs` | só arquivos no escopo + peça nova em `Pecas/` |
| Mudar as regras do dialeto | `Application/Ai/CadScriptRules.cs` | fonte **única**: prompt + `.cad/api.md` + `AGENTS.md`. Há teste que falha se divergirem |
| Adicionar exemplo few-shot para a IA | `Application/Ai/Prompts/CurrentPromptProfile.Examples` | há teste que compila e executa **cada** exemplo de **cada** perfil |
| Testar outra estratégia de pré-prompt | novo `ICadScriptPromptProfile` em `Application/Ai/Prompts/`, registrado em `CadScriptPromptProfiles` | o perfil `atual` é congelado por golden (`Tests/Ai/Golden`); compare perfis com `AvaloniaCadEditor.PromptEval` |
| Mudar a renderização | `UI/Controls/Rendering/*RenderPass.cs` (implemente `IRenderPass`) | registre em `SilkViewportControl._renderPasses` |
| Mudar o modo wireframe (linhas ocultas tracejadas) | `UI/Controls/Rendering/WireframeRenderPass.cs`, `WireframeEdgeShader.cs`, `WireframeGeometry.cs` | quem entra no frame e com que cor vem de `SolidRenderables`, compartilhado com o sombreado. As arestas e silhuetas são calculadas sob demanda pelo `EdgeDetector`, só para as malhas exibidas no modo: a mudança é só visual e não acrescenta nada à `TriangleMesh` |
| Mudar o referencial da peça isolada (H) | `Application/Isolation/PartFrameResolver.cs`, `Domain/ValueObjects/PartFrame.cs`, `CadViewModel.ToggleIsolateSelection`/`ReapplyIsolation` | só visual: a grade e as vistas usam o referencial, o código continua em mundo. O isolamento é guardado **por nome** e reaplicado em `RunCodeAsync`, porque cada execução recria as features |
| Mudar o que a análise enxerga (primitivas, papéis, nomes) | `AvaloniaCadEditor.Analysis/FactExtractor.cs` | é o único percurso da árvore com `Placement`: não crie outro. Feature nova → um `case` aqui |
| Mudar as relações inferidas (simetria, padrões, coaxiais, alinhamentos) | `AvaloniaCadEditor.Analysis/RelationAnalyzer.cs` | 3D e sem vista; serve ao desenho **e** às guias. Simetria exige pares de mesmo tipo e tamanho |
| Mudar quais cotas o desenho escolhe | `AvaloniaCadEditor.Drafting/DimensionPlanner.cs` (hierarquia: 1 cadeias e tamanhos, 2 localização, 3 totais) e `DimensionLayout.cs` (fileiras por intervalos) | as medidas vêm dos **parâmetros** das features, não da malha: o OCCT não expõe arestas analíticas e furos são prismas de 32 lados |
| Mudar como as cotas aparecem / o modo "Cotar" | `UI/Drafting/` (`DraftingViewModel`, `DraftingRenderPass`, `DraftingLabelsOverlay`) | cotas manuais vivem só na sessão, por Id de âncora (`furo_x.centro`); só desenham com câmera ortho na vista escolhida |
| Mudar quando o desenho esconde cotas (densidade) | `AvaloniaCadEditor.Drafting/DensityPolicy.cs` (limites em `DraftingOptions`), `DimensionCrossings.cs` | esconde por lado: localização (nível 2) primeiro, depois as cadeias que mais cruzam; totais, chamadas e níveis forçados pela legenda (`UI/Drafting/DraftingLegend`) nunca |
| Mudar como peças aninhadas aparecem no desenho | `Analysis/FactExtractor` (marca `PrimitiveFact.Component`), `RelationAnalyzer.Components`, `Drafting/DimensionPlanner` (`ComponentMarks`, `ComponentCallouts`) | toda `.Instance(...)` é caixa-preta: cotada pelo eixo, com chamada pelo `Part.DrawingLabel` (ou tipo + enums); as medidas internas são da sub-peça |
| Mudar as guias de referência | `Application/ReferenceGuides/ReferenceGuideService.cs` | é só apresentação sobre `Analysis`: a análise em si muda no `RelationAnalyzer` |
| Mudar seleção / hover / pick | `UI/ViewModels/CadViewModel.cs` (hover com timer, ciclo TAB), `UI/Controls/Input/` | |
| Mudar a árvore ou o inspector | `UI/ViewModels/ModelTreeViewModel.cs`, `PropertyInspectorViewModel.cs` | o inspector escreve no **código**, não no objeto |
| Mudar a validação headless | `UI/Cli/CheckCommand.cs`, `Application/Scenes/SceneCheckService.cs` | exit codes: 0 ok, 1 erro de cena, 2 uso/pasta inválida |
| Mexer nas configurações de tesselação | `Infrastructure/Settings/JsonTessellationSettingsProvider.cs`, `Domain/Settings/TessellationSettings.cs` | **leia §5.7 antes** — o cache validado por data não é desperdício |
| Mudar a composição / injeção | `UI/App.axaml.cs` | ciclo `CadViewModel` ↔ `SceneScriptViewModel`, ver §6.1 |

### 4.1 Tipos com nome parecido — não confunda

| Tipo | Camada | É |
|---|---|---|
| `IGeometryEngine` | Domain | geometria **2D** de sketch: interseção, snap, trim, extend, offset |
| `IGeometryKernel` | Application | geometria **3D** B-Rep: constrói sólidos e devolve malha |
| `MathGeometryEngine` | Infrastructure | implementação 2D real |
| `OpenCascadeGeometryKernel` | Infrastructure | implementação 3D real (a usada) |
| `PureMathGeometryKernel` | Infrastructure | kernel 3D alternativo sem OCCT |
| `DummyGeometryEngine` | Infrastructure | stub |

---

## 5. Invariantes

### 5.1 Adicionar um tipo de feature toca 7 lugares

Esquecer qualquer um deles produz falha **silenciosa** (sólido não aparece, ou aparece com
geometria velha do cache):

1. `Domain/Features/XFeature.cs` — herda de `FeatureBase`.
2. `OpenCascadeGeometryKernel.BuildShape` — novo `else if` no dispatch.
3. `Domain/Features/FeatureFingerprint.Append` — novo `case` + `AppendX`. **Sem isso a feature
   nunca é cacheada entre execuções** (degrada desempenho, mas é seguro). `FeatureFingerprintTests`
   falha se uma propriedade pública geométrica ficar de fora.
4. `Application/Services/FeatureCodeGenerator` — se alguma ferramenta gera a feature.
5. `Application/Ai/CadScriptRules` + `Prompts/CurrentPromptProfile.Examples` — para a IA saber que existe.
6. `UI/ViewModels/ModelTreeViewModel` / `PropertyInspectorViewModel` — se precisa aparecer ou ser editável.
7. `Analysis/FactExtractor` — um `case` no `Walk` e o tipo em `HandledFeatureTypes`, **ou** o motivo em
   `IgnoredFeatureTypes`. Sem isso a feature some das cotas e das guias de referência sem erro;
   `FactExtractorCoverageTests` falha até a decisão ser tomada.

### 5.2 Sólidos são rastreáveis por nome

Cada sólido em uma cena precisa de **uma declaração própria**, com `Name` literal igual ao nome
da variável, único no arquivo. Nunca em laço, LINQ ou com nome interpolado. É o que liga sólido
↔ linha de código ↔ seleção ↔ árvore. Texto normativo completo em
[`CadScriptRules.TrackableSolids`](AvaloniaCadEditor.Application/Ai/CadScriptRules.cs).
Vale para o `FeatureCodeGenerator` e para qualquer código que escreva cena.

### 5.3 Geometria que falha devolve malha vazia, não exceção

O kernel **nunca** propaga exceção de construção. Registra o motivo em `BuildProblems`
(indexado por `IFeature.Id`, com o nome da feature já embutido na mensagem) e devolve
`TriangleMesh.Empty`. `CadViewModel.ReportBuildProblems` leva isso à barra de status.

A razão de `BuildProblems` viver junto do cache de formas, e não da última chamada: o cache de
malhas **guarda a malha vazia** e não chamaria o kernel de novo, então a explicação precisa
sobreviver à chamada.

### 5.4 `Part` é `record` — a igualdade por valor é a chave do cache

`Part` deve ser declarada como `record` com construtor primário. A igualdade por valor gerada
pelo compilador é usada como chave de cache (`SceneBuilder.GetOrBuildDefinition`): peças com os
mesmos parâmetros constroem geometria **uma vez** e cada `PartInstance` só acrescenta um
`Placement`. Trocar para `class` silenciosamente mata esse cache.

`Build()` deve ser **puro**: coordenadas locais, sem `AddFeature`, sem I/O.

Uma peça pode ser **operando** dentro do `Build()` de outra: `new FuroPorca(...).Instance("nome")`
(`PartExtensions.Instance`, em `Part.cs`). A definição vem do mesmo `GetOrBuildDefinition`,
portanto sub-peças iguais compartilham geometria. A instância não entra na cena. Para quem percorre a
árvore, ela é uma `PartInstance` dentro da `Definition` da hospedeira: o `FactExtractor`
qualifica os nomes como `instancia/solido`, a revelação de ferramentas a mostra como uma
ferramenta só, e o `RoslynPartSolidTracer` a trata como sólido-folha da hospedeira.

### 5.5 Rotação e `FilletFeature`

O wrapper OCCT só sabe transladar sólidos. Instância **só com translação** reaproveita o sólido
da definição e translada uma cópia; **com rotação**, o sólido é reconstruído do zero já em
coordenadas do mundo. `FilletFeature` girado é **proibido** (`NotSupportedException`): a receita
depende das arestas do sólido base, que não existem naquela orientação.

### 5.6 O `SceneBuilder` é por execução

`SceneHost` usa `AsyncLocal`. Cena e validação da IA podem rodar ao mesmo tempo sem se misturar.
Não introduza estado estático mutável em `Scene` / `SceneHost`.

### 5.7 O cache de settings é validado por data de modificação — não o simplifique

`JsonTessellationSettingsProvider` guarda o último conteúdo lido, mas **consulta
`File.GetLastWriteTimeUtc` em toda chamada**. Parece desperdício e não é: seis dos oito campos de
`TessellationSettings` (as duas deflexões, `FaceSegments`, `CreaseAngle` e as duas cores de
aresta) **não têm controle na interface** — editar o JSON à mão é a única forma de ajustá-los.

Trocar isso por um cache cego quebra duas coisas, e a segunda é pior:

1. a edição manual deixa de aparecer sem reiniciar o app;
2. os setters da UI (`CadViewModel.ShowInternalEdges`, `ProfileSegments`) fazem *ler os oito
   campos → mudar um → gravar os oito*. Com cache cego, esse ciclo regrava os valores velhos por
   cima e **apaga o ajuste manual em silêncio** — um clique num checkbox destrói uma calibração
   de `LinearDeflection`.

`GetSettings` também devolve sempre uma **cópia**: `TessellationSettings` é mutável, quem chama
altera o objeto antes de salvar, e a instância cacheada não pode enxergar essa alteração (a chave
do cache de malhas do kernel, `GeometryKey`, é montada desses campos). Pela mesma razão o
snapshot é trocado por inteiro e nunca mutado no lugar: a render thread lê sem lock.

Dois testes em `Tests/Settings/JsonTessellationSettingsProviderTests.cs` travam exatamente isso
(`GetSettings_QuandoOUsuarioEditaOArquivoAMao_PassaAVerOValorNovo` e
`LerMutarGravar_DepoisDeEdicaoManual_NaoApagaOCampoEditado`): ambos passam com o código atual e
falham com um cache cego.

---

## 6. Dívida técnica conhecida

Itens verificados, com local. Não são bugs abertos pedindo correção — são o que você vai
encontrar e deve entender antes de "consertar".

### 6.1 Composição manual com ciclo e `null!`

[`App.axaml.cs:47`](AvaloniaCadEditor.UI/App.axaml.cs#L47) passa `null!` para `scriptEditor` e na
linha 50 faz `viewModel.ScriptEditor = scriptEditor`: `CadViewModel` ↔ `SceneScriptViewModel` é
circular, resolvido por property setter. `_filePickerService!` e `_reconstructionService!` também
são null-forgiving — NRE em runtime para quem construir o VM de outro jeito (e os testes constroem).
Não há container de DI.

### 6.2 Defaults de construtor furam a camada

[`CadViewModel.cs:645-648`](AvaloniaCadEditor.UI/ViewModels/CadViewModel.cs#L645):
`settingsProvider ?? new JsonTessellationSettingsProvider()` e `stlService ?? new StlService()`.
O acoplamento UI→Infrastructure fica escondido num valor default, por conveniência de teste —
o oposto do que a `IGeometryKernel` conquistou.

### 6.3 Dois god objects

`CadViewModel` (1565 linhas) acumula hover/pick com timer, ciclo TAB entre candidatos, estado de
ferramenta, isolamento de seleção, modo de edição de grupo booleano, work plane, export STL e
dispatch de teclado. `SceneScriptViewModel` (1475) acumula projeto em disco, watcher, abas,
undo/redo, navegação F12/F2, parâmetros de peça e o run. Extrações candidatas:
`HoverPickController`, `SceneProjectSession`.

### 6.4 Restos da arquitetura antiga

- `GenerateCodeCommand` é um no-op — "O gerador foi castrado. O código agora é a fonte da
  verdade." ([`SceneScriptViewModel.cs:68`](AvaloniaCadEditor.UI/ViewModels/SceneScriptViewModel.cs#L68)).
  Continua sendo chamado em todo `RefreshSolidMesh` e tem botão na toolbar ("Atualizar código").
- `MainViewModel` ainda é o "Welcome to Avalonia!" do template.
- `AiCore.Avalonia/` está vazia.
- `PlanarTopologyService` é a única implementação de serviço que vive em `Domain/Services/`
  (as outras estão em `Infrastructure`).

### 6.5 Nullable declarado mas não honrado no kernel

`<Nullable>enable</Nullable>` em todos os projetos, mas `OpenCascadeGeometryKernel.cs` gera
**50 avisos CS8603** (retorno possivelmente nulo): `BuildShape` e amigos declaram `OcctShape`
não-anulável e retornam `null`. **Assuma que `BuildShape` pode devolver `null`** e teste antes
de usar. Build atual: 87 avisos, sendo 56 CS8603, 20 CS0067 (evento `CodeGenerated` nunca
disparado, ver §3.2) e 2 CA2022.

---

## 7. `AiCore` é biblioteca **e** cena ao mesmo tempo

A pasta `AiCore/` acumula dois papéis:

- **Biblioteca** compilada no build: `Abstractions/`, `Models/`, `Services/`.
- **Cena de teste real** (uma impressora 3D com 14 peças): `Cena.cs`, `Pecas/**`, `.cad/`.

Os fontes da cena saem do build por `<Compile Remove>` e voltam como `<None>`
([`AiCore.csproj`](AiCore/AiCore.csproj)) — são compilados **só pelo `RoslynSceneEvaluator` em
runtime**. Consequência: **um erro de sintaxe em `AiCore/Pecas/` passa o build inteiro.** O
`check --geometry` cobre isso, mas hoje depende de execução manual.

---

## 8. Validar uma mudança

```bash
# Build (ver armadilha abaixo)
dotnet build AvaloniaCadEditor.slnx

# Testes (~445)
dotnet test AvaloniaCadEditor.Tests/AvaloniaCadEditor.Tests.csproj

# Validar uma cena sem abrir a janela: 0 = ok, 1 = erro de cena, 2 = uso/pasta inválida
AvaloniaCadEditor.UI.exe check AiCore --geometry
```

**Armadilha de build:** com o app aberto, o build falha com `MSB3021`/`MSB3027` ("O arquivo é
bloqueado por: AvaloniaCadEditor.UI") na cópia dos DLLs para `AvaloniaCadEditor.UI/bin/`. **Isso
não é erro de código** — feche o app (e o Visual Studio, que também segura os DLLs) e rode de
novo. Para só checar compilação sem o lock, builde um projeto de biblioteca:
`dotnet build AvaloniaCadEditor.Infrastructure/AvaloniaCadEditor.Infrastructure.csproj`.

**Para compilar e testar tudo sem fechar o app**, redirecione só o `bin` (o `obj` pode continuar
no lugar, o app não o abre):

```bash
dotnet test AvaloniaCadEditor.Tests/AvaloniaCadEditor.Tests.csproj -p:BaseOutputPath=/caminho/alternativo/
```

Com isso, três testes de `CheckCommandProcessTests` falham com *"Expected dir not to be null
because os testes rodam dentro da pasta da solução"*: eles procuram o `AvaloniaCadEditor.UI.exe`
subindo a partir de `AppContext.BaseDirectory`, que agora está fora da solução. **São falhas do
atalho, não do código** — as outras 531 valem.

### Onde os testes moram

| Pasta | Cobre |
|---|---|
| `Tests/BehaviorTests/` | ferramentas e kernel ponta a ponta (volume/área da malha, não contagem de triângulos) |
| `Tests/Features/` | fingerprint, fillet, detector de arestas, cache de malha |
| `Tests/Scenes/` | projeto de cena, edição de peça, tracer, navegação, `check` |
| `Tests/Ai/` | protocolo, validador, sessão de chat, contexto e escopo |

Três testes funcionam como travas de arquitetura e devem continuar passando:

- `FeatureFingerprintTests` — falha se uma propriedade geométrica pública ficar fora do fingerprint.
- `CadScriptProtocolExamplesTests` — compila e executa **cada** exemplo few-shot de **cada** perfil de
  pré-prompt: o dialeto ensinado à IA não pode apodrecer.
- `CadScriptPromptProfilesTests` — o perfil `atual` bate com o golden; todo perfil traz o formato de resposta.
- `CadScriptRulesTests` — garante que prompt, `.cad/api.md` e `AGENTS.md` usem o mesmo texto.
