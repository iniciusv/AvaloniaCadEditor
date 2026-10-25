# Rastrear e destacar as variáveis que alimentam um sub-sólido de peça

## Contexto

Hoje, ao entrar numa instância de peça com duplo-clique (`cantoneira_placa_extrusao_a`) e clicar
num sólido-ferramenta vermelho (`furo_placa_1`), o app faz **uma** coisa:
`CadViewModel.RevealToolFeature` → `SceneScriptViewModel.RevealPartTool` →
`PartSourceEditor.FindToolDeclaration` abre `AiCore/Pecas/CantoneiraPlacaExtrusao2020.cs` e
seleciona o identificador `furo_placa_1` na linha 128. Fim.

Isso responde "onde o furo nasce", mas não "o que eu mexo para movê-lo". A posição vem de
`new Point3D(entradaFurosPlacaX, -DistanciaFuroPlaca1Y, AlturaFurosPlacaZ)` com
`direcaoFurosPlaca`, `raioFuro` e `EspessuraAbaPlaca + 2` — uma cadeia que passa por locais
(`sx`, `raioFuro`, `entradaFurosPlacaX`) até chegar nos parâmetros do record, que são os botões de
verdade. Para descobrir isso o usuário lê o `Build()` na mão, e o inspector não ajuda: lista os
**18** parâmetros da cantoneira em ordem de declaração, sem dizer que só `DistanciaFuroPlaca1Y`
move esse furo isoladamente, que `AlturaFurosPlacaZ` move os dois furos da placa e que
`DiametroFuroM3` mexe nos quatro.

Resultado desejado: clicar no furo destaca, no arquivo da peça, a declaração dele **e** toda a
cadeia que o alimenta; e o inspector passa a ranquear os parâmetros por relevância àquele sólido,
dizendo quais são exclusivos dele, já editáveis. A edição grava o **valor padrão do record**,
valendo para todas as instâncias da peça.

**Decisões tomadas:** rastrear + destacar + inspector ranqueado, **sem** arrastar no 3D; destaque
numa cor só (exclusivo vs compartilhado aparece no inspector, não no código); edição no valor
padrão do record.

## Tamanho

~700 linhas, das quais ~190 de teste — ou seja ~510 de produção, em 2 arquivos novos e 6 tocados.
O tracer (~230) é a feature e não encolhe; o resto é encanamento. (Um pouco acima das ~635 que
estimei na conversa: mesmo reusando `PartParameterItem` e o XAML existente, o inspector ainda
precisa do seu próprio carregador, ~55 linhas.)

## O que reusar, e o que não

**Reusar:** `RoslynCodeNavigator` como molde do tracer (modelo semântico sobre a mesma compilação
que roda a cena, 208 linhas, é o que faz F12/Shift+F12/F2); `FeatureHighlightColorizer`
**sem nenhuma alteração** (já pinta uma `List<TextSpan>` qualquer); `PartParameterItem` e o
`ItemsControl` de `PartParameters` que já existem no inspector; `PartSourceEditor` e seus helpers
privados de edição pontual; `ActiveToolFeatures`/`RevealToolFeature`, que já são o gesto do clique
no sólido vermelho.

**Não reusar — `RoslynDependencyAnalyzer.GetFullDependencyGraph`** (que hoje alimenta o destaque de
features soltas via `RoslynFeatureHighlighter.FindFeatureSpans`). Não serve para peças:

- é sintático e global ao arquivo —
  `root.DescendantNodes().OfType<VariableDeclaratorSyntax>().ToDictionary(v => v.Identifier.Text, ...)`
  **lança `ArgumentException`** se dois escopos do arquivo repetirem um nome de variável;
- filtra dependências por `variables.ContainsKey(name)`, então **descarta justamente os parâmetros
  do record** (`DistanciaFuroPlaca1Y`, `AlturaFurosPlacaZ`…), que não são `VariableDeclaratorSyntax`.
  A trilha pararia em `entradaFurosPlacaX`;
- não segue funções locais (`CantoBaseX`, `PerfilReforco`).

Ele continua atendendo o caso de features soltas; não vamos mexer nele.

## Implementação

### 1. Tracer semântico (o núcleo)

**Contrato** — `AvaloniaCadEditor.Application/Interfaces/IPartSolidTracer.cs` (novo), ao lado de
`ISceneCodeNavigator.cs`, reusando o `CodeLocation` que já vive lá:

```csharp
public enum SolidInputKind { PartParameter, Local, LocalFunction, Constant }

/// <param name="SharedWith">Outros sólidos nomeados do mesmo Build() que também dependem dela.</param>
public sealed record SolidInput(
    string Name,
    SolidInputKind Kind,
    string? ValueCode,                     // default do record, ou a expressão do local
    IReadOnlyList<CodeLocation> Spans,     // declaração + ocorrências dentro do sólido
    IReadOnlyList<string> SharedWith);

public sealed record PartSolidInputs(
    string PartTypeName,
    string SolidName,
    CodeLocation Declaration,              // a instrução inteira que declara o sólido
    IReadOnlyList<SolidInput> Inputs);

public interface IPartSolidTracer
{
    PartSolidInputs? Trace(IReadOnlyList<ScriptDocument> documents, string partTypeName, string solidName);
}
```

**Implementação** — `AvaloniaCadEditor.Infrastructure/Scripting/RoslynPartSolidTracer.cs` (novo),
espelhando `RoslynCodeNavigator`:

- compilação via `RoslynSceneEvaluator.CreateCompilation(documents, out var trees)` — o mesmo que
  `RoslynCodeNavigator.Analyze` (linha 94) usa;
- acha o `RecordDeclarationSyntax` de `partTypeName` e o `Build()` dele; **tudo é resolvido dentro
  desse record**, o que elimina as colisões de nome do analisador antigo;
- semente: o `VariableDeclaratorSyntax` de `solidName` dentro do record, ou — na falta dele — a
  instrução que contém o literal `"solidName"` (mesmo fallback de `FindToolDeclaration`);
- fila sobre expressões; para cada `IdentifierNameSyntax`, resolve o símbolo com a mesma lógica de
  `RoslynCodeNavigator.SymbolOf` (linha 112) e despacha:
  - `ILocalSymbol` declarado no `Build()` → `Local`, enfileira o inicializador;
  - `IMethodSymbol { MethodKind: LocalFunction }` → `LocalFunction`, enfileira o corpo — é como
    `CantoBaseX`/`PerfilReforco` entram na cadeia;
  - `IParameterSymbol` do construtor primário **ou** `IPropertySymbol` do record → `PartParameter`,
    normalizando os dois para um só com o truque de `RoslynCodeNavigator.RelatedSymbols`
    (linhas 138-157), que já existe exatamente para isso;
  - `IFieldSymbol` const/estático declarado nos arquivos da cena (ex.: uma `static class Medidas`)
    → `Constant`;
  - símbolos da API (`Point3D`, `Vector3D.UnitY`, `CylinderFeature`) → ignora;
  - visitados num `HashSet<ISymbol>(SymbolEqualityComparer.Default)`, então ciclos e repetições são
    inofensivos;
- **`SharedWith`**: roda o mesmo trace para cada sólido nomeado do record (declaradores cujo
  inicializador cria uma feature) e, para cada entrada, lista quais outros sólidos a alcançam. São
  ~10 traces sobre um método, numa única compilação.

Registrar a implementação onde `ISceneCodeNavigator` é registrado hoje.

**Custo:** uma compilação por clique em sólido vermelho. É o mesmo custo que F12 já paga por
tecla, e o gesto é discreto (não dispara em `SelectionChanged`), então não precisa de cache.

### 2. Destaque no editor (uma cor, sem tocar no colorizer)

Achado que simplifica isto: em
[ViewportInputHandler.cs:190-200](AvaloniaCadEditor.UI/Controls/Input/ViewportInputHandler.cs#L190-L200),
clicar numa ferramenta **de peça** não mexe na seleção —
`viewModel.Sketch.Features.Contains(tool)` é falso (as ferramentas da definição não estão no
sketch), então `SelectById` não roda e só `RevealToolFeature` acontece. Logo
`HandleSelectedFeatureChanged` **não dispara** nesse clique, e as duas fontes de destaque não
competem de verdade: último que escreve ganha, que é o comportamento correto.

- `SceneScriptViewModel`: nova propriedade `PartSolidSpans` (`IReadOnlyList<TextSpan>`), preenchida
  em `RevealPartTool` com os spans do trace **filtrados para o arquivo ativo** — o arquivo da peça,
  que `RevealPartTool` já abre. Os spans dos parâmetros do record ficam no topo desse mesmo
  arquivo, na lista de parâmetros, então entram naturalmente.
- `CodeEditorBehavior`: nova propriedade anexada `PartSolidSpans` que escreve em
  `colorizer.HighlightSpans` e chama `Redraw()`, dentro de um
  `Dispatcher.UIThread.Post(..., DispatcherPriority.Loaded)` — porque o pedido chega junto com a
  troca de arquivo, exatamente o motivo pelo qual `HandleRevealLineChanged` (linhas 182-207) já faz
  isso. `FeatureHighlightColorizer` fica intacto.
- Limpar (lista vazia) em `SwitchActiveFile` e ao sair do modo de edição booleana.

**Bug pré-existente que isto corrige de graça:** hoje os spans do colorizer não são limpos na troca
de arquivo. Como `HandleSelectedFeatureChanged` só roda quando `SelectedFeature` muda, depois de um
`RevealPartTool` os spans calculados contra `Cena.cs` continuam lá, pintando offsets aleatórios do
arquivo da peça. Assumir a posse dos spans na troca de arquivo resolve.

### 3. Inspector ranqueado (reusando `PartParameters`)

`PropertyInspectorViewModel` reage a `_selectionService.SelectionChanged`, mas o clique no sólido
vermelho não muda a seleção (ver acima). Então:

- `CadViewModel`: nova propriedade `SelectedPartSolid` (`PartSolidInputs?`), preenchida em
  `RevealToolFeature` junto com o `HighlightedToolId` que já existe, e zerada ao sair do modo de
  edição booleana (`ExitBooleanEditModeCommand`);
- `PropertyInspectorViewModel`: um `LoadSolidParameters(instance, inputs)` irmão do
  `LoadPartParameters(instance)` atual (linha 497), que preenche a **mesma** coleção
  `PartParameters` com os **mesmos** `PartParameterItem` — então o XAML não muda — mas:
  - **ordem**: exclusivos do sólido primeiro, depois compartilhados, depois os que não o afetam;
  - **hint**: `"move só este furo"` / `"move também furo_placa_2"` / vazio;
  - **setter**: `SetPartTypeParameterDefaultAsync` em vez de `SetPartParameterAsync`;
  - `PartNote`: `"5 dos 18 parâmetros afetam furo_placa_1."`
- Os locais e funções locais da cadeia (`sx`, `raioFuro`, `entradaFurosPlacaX`, `CantoBaseX`) **não**
  entram no inspector — eles aparecem destacados no código, que é onde se mexe neles. Essa divisão
  é o que permite reusar a lista existente sem XAML novo.
- Quando a instância inteira está selecionada, nada muda: `LoadPartParameters` continua como hoje.

### 4. Gravar o valor padrão do record

**`PartSourceEditor.SetPartTypeParameterDefault(source, partTypeName, parameterName, valueCode)`**
(novo, em `AvaloniaCadEditor.Application/Scenes/PartSourceEditor.cs`): acha o
`RecordDeclarationSyntax` e o `ParameterSyntax`, substitui `p.Default.Value.Span` pelo novo código
(ou insere ` = valor` quando não há default), usando os helpers privados que já existem no arquivo
(`Replace`, `Insert`, `IsLiteral`). Recusa com `InvalidOperationException` se o default atual não
for literal.

**`SceneScriptViewModel.SetPartTypeParameterDefaultAsync(PartInstance owner, string parameterName, string text)`**
(novo), modelado em `SetPartParameterAsync` (linhas 710-751) — a diferença é o arquivo alvo:

1. valida tipo/valor/`[Range]` com `owner.Part.GetParameters()`, `IsEditableType`, `TryParseValue`
   e `FormatValue` (idêntico ao existente);
2. localiza o arquivo da peça por `PartSourceEditor.FindPartTypes(CurrentDocuments())` →
   `PartTypeInfo.FilePath` — **não** pelo `FeatureOrigin` da instância, que aponta para `Cena.cs`;
3. `SetPartTypeParameterDefault` → `SetDocumentText` → `await RunCodeAsync()`;
4. se `HasErrors`, devolve `ErrorSummary`;
5. **restaura o estado de edição**: `ActiveBooleanGroupName` sobrevive porque é por nome, mas
   `HighlightedToolId` é um `Guid` e fica obsoleto depois do re-run (a cena reexecuta e aloca
   features novas) — re-resolver a ferramenta pelo nome em `ActiveToolFeatures`, e refazer o
   trace/destaque.

**Aviso de instâncias que sobrepõem o parâmetro:** para cada `PartInstance` daquele tipo na cena,
ler os argumentos com `PartSourceEditor.ReadArguments(...)` (como `ReadPartArguments` já faz, linha
686) e contar quantas passam esse parâmetro explicitamente. Havendo alguma, acrescentar ao
`PartNote`: *"2 de 3 instâncias passam DistanciaFuroPlaca1Y na chamada Add(...); essas não mudam."*
Sem isso a edição simplesmente não surte efeito em algumas instâncias e o app não explica por quê —
é o caso real da `cantoneira_placa_extrusao_b`, criada com `with { EspelhadoNoEixoX = true }` em
`AiCore/Cena.cs:88`.

## Arquivos

Novos:
- `AvaloniaCadEditor.Application/Interfaces/IPartSolidTracer.cs` (~45)
- `AvaloniaCadEditor.Infrastructure/Scripting/RoslynPartSolidTracer.cs` (~230)
- `AvaloniaCadEditor.Tests/Scenes/PartSolidTracerTests.cs` (~190)

Alterados:
- `AvaloniaCadEditor.Application/Scenes/PartSourceEditor.cs` — `SetPartTypeParameterDefault` (~30)
- `AvaloniaCadEditor.UI/ViewModels/SceneScriptViewModel.cs` — trace em `RevealPartTool`,
  `PartSolidSpans`, `SetPartTypeParameterDefaultAsync`, limpeza em `SwitchActiveFile` (~95)
- `AvaloniaCadEditor.UI/ViewModels/PropertyInspectorViewModel.cs` — `LoadSolidParameters` (~55)
- `AvaloniaCadEditor.UI/ViewModels/CadViewModel.cs` — `SelectedPartSolid` (~15)
- `AvaloniaCadEditor.UI/Controls/CodeEditorBehavior.cs` — propriedade anexada `PartSolidSpans` (~35)
- `AvaloniaCadEditor.UI/Views/MainWindow.axaml` — um binding
- registro de DI de `IPartSolidTracer`, onde `ISceneCodeNavigator` é registrado

Intocados de propósito: `FeatureHighlightColorizer`, `RoslynDependencyAnalyzer`,
`RoslynFeatureHighlighter`, `PropertyInspectorView.axaml`.

## Verificação

**Testes** (`dotnet test`) — xUnit + FluentAssertions, no padrão de
`AvaloniaCadEditor.Tests/Scenes/PartToolRevealTests.cs` (um `ScriptDocument` de cena + um de peça):

1. `Trace("CantoneiraPlacaExtrusao2020", "furo_placa_1")` devolve os parâmetros
   `DistanciaFuroPlaca1Y` (`SharedWith` vazio), `AlturaFurosPlacaZ` (compartilhado com
   `furo_placa_2`), `DiametroFuroM3` (4 furos), `EspessuraAbaPlaca`, `EspelhadoNoEixoX`, e os
   locais `sx`, `raioFuro`, `entradaFurosPlacaX`, `direcaoFurosPlaca`.
2. O mesmo trace **não** inclui `LarguraAbaPlacaY`, `AlturaAbaExtrusao`, `AvancoReforcoX`,
   `PassoFurosExtrusao` — é o que separa um trace de verdade de "todos os parâmetros".
3. Função local: `Trace(..., "aba_placa")` inclui `CantoBaseX` e, através dela, `sx` e
   `EspelhadoNoEixoX`.
4. Dois records em arquivos diferentes com um local de mesmo nome (`raioFuro`) → nenhum vazamento
   entre eles (o caso que quebra `RoslynDependencyAnalyzer`).
5. Sólido nomeado só por literal (sem variável) → cai no fallback e ainda rastreia.
6. `SetPartTypeParameterDefault`: com default existente, sem default, e recusa de default
   não-literal.

**Fim a fim** no app, com a cena `AiCore`:

1. abrir o app, duplo-clique em `cantoneira_placa_extrusao_a` → os furos aparecem em vermelho;
2. clicar em `furo_placa_1` → `Pecas/CantoneiraPlacaExtrusao2020.cs` abre com a linha 128, os
   locais `sx`/`raioFuro`/`entradaFurosPlacaX`/`direcaoFurosPlaca` e os parâmetros
   `DistanciaFuroPlaca1Y`/`AlturaFurosPlacaZ`/`DiametroFuroM3`/`EspessuraAbaPlaca`/`EspelhadoNoEixoX`
   destacados — e `LarguraAbaPlacaY`/`AvancoReforcoX` **sem** destaque;
3. o inspector mostra `DistanciaFuroPlaca1Y` no topo com "move só este furo", depois os
   compartilhados com o nome de quem compartilha, e a nota "5 dos 18 parâmetros afetam furo_placa_1";
4. trocar `DistanciaFuroPlaca1Y` para `19.5` → a linha 49 do record muda, a cena reexecuta, os dois
   furos de placa das **duas** cantoneiras se deslocam, e o furo segue realçado em laranja no 3D;
5. clicar em `furo_extrusao_1` → destaque e ordem do inspector trocam para
   `DistanciaPrimeiroFuroX`/`PassoFurosExtrusao`/`EspessuraAbaExtrusao`, sem resíduo do furo anterior;
6. sair do modo booleano → destaque limpo, inspector volta à lista normal.

Vale rodar também em `SuporteHotendV6.cs` / `SuporteMotorNema17.cs`, que ainda criam features em
`foreach` com nomes `$"..._{nome}"`: o trace deve degradar com a mensagem que `RevealPartTool` já
usa ("declaração não encontrada … nome gerado em laço?") em vez de estourar.
