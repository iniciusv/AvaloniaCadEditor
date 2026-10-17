# Converter sólido solto em peça (`record : Part`)

## Contexto

Sólidos soltos (`AddFeature` na `Cena.cs`) não ganham referencial, cotas, inspector nem cache, porque
tudo isso depende de `PartInstance`. O objetivo é um serviço que pega um sólido solto e o transforma numa
peça em `Pecas/`, de forma mecânica. Não promove literais a parâmetros e não detecta sub-peças.

Restrição principal: **não inchar o código.** `CadViewModel` (1737 linhas), `SceneScriptViewModel` (1498)
e `PartSourceEditor` (1173) já são pontos de atrito (ARCHITECTURE §6.3). A conversão fica isolada em
**um** serviço novo atrás de **uma** interface. Os god objects ganham só a ligação, e o
`PartSourceEditor` não é tocado.

## Arquitetura (o que entra e onde)

| Peça | Onde | Tamanho | Por quê aqui |
|---|---|---|---|
| `IPartConverter` + records de resultado | `Application/Interfaces/IPartConverter.cs` | ~40 linhas | Mesmo padrão de `IPartSolidTracer`: o Application declara e não conhece a Infrastructure |
| `RoslynPartConverter` | `Infrastructure/Scripting/RoslynPartConverter.cs` | ~350 linhas | Precisa do modelo semântico de `RoslynSceneEvaluator.CreateCompilation`, igual a `RoslynPartSolidTracer`. É puro (texto entra, texto sai), sem I/O, sem avaliação |
| `PartConversionCheck` | `Application/Scenes/PartConversionCheck.cs` | ~60 linhas | Verificação pura usando só o Domain (`FeatureFingerprint`). Não depende do kernel |
| Ligação | `SceneScriptViewModel` (1 método, ~25 linhas) + `IPartCodeEditor` (1 assinatura) | — | Reaproveita `ApplyExternalChangesAsync` (já grava vários arquivos, cria `Pecas/X.cs` e reexecuta) |
| Gatilho | `PropertyInspectorViewModel` + axaml do inspector: campo "Nome da peça" + botão, só para feature solta | ~30 linhas | O inspector já trata feature solta (`PropertyInspectorViewModel.cs:342`) e já chama o `IPartCodeEditor`. Nada vai para o `CadViewModel` |

O conversor não muda nada em `CadViewModel`, `PartSourceEditor`, `CadScriptRules`, kernel ou Analysis.

### Contrato

```csharp
// Application/Interfaces/IPartConverter.cs
public interface IPartConverter
{
    /// Converte o sólido solto declarado como `variableName` no arquivo de entrada em Pecas/{typeName}.cs.
    /// Devolve os documentos alterados (entrada + peça nova) ou o motivo da recusa. Não grava nem executa.
    PartConversion Convert(IReadOnlyList<ScriptDocument> documents, string entryFile, string variableName, string typeName);
}
public sealed record PartConversion(IReadOnlyList<ScriptDocument> Changes, string? Refusal);
```

## Algoritmo do `RoslynPartConverter`

1. **Pré-condições.** `typeName` precisa ser um identificador válido, não pode existir na compilação e
   `Pecas/{typeName}.cs` não pode existir. A raiz é um local declarado em statement top-level do arquivo
   de entrada (vem de `FeatureOrigin.VariableName`). Se faltar algo, devolve `Refusal`.
2. **Recorte para trás** (semântico, com `ILocalSymbol`/`IMethodSymbol`, como o tracer faz):
   - parte da declaração da raiz;
   - inclui a declaração de cada local lido;
   - inclui os statements que só mexem em locais do recorte (`x.Color = ...`, `x.IsTransientTool = true`);
   - inclui funções locais usadas, com as dependências delas.
3. **Pontos de corte** (o que **não** entra no `Build()`):
   - local que **não é feature** e é lido também fora do recorte, ou que vem de fora: vira **parâmetro do
     record**. O nome é PascalCase do local e o tipo é o do símbolo. O default é o valor constante, quando
     o Roslyn sabe calculá-lo (`GetConstantValue`); quando não sabe, fica sem default. A chamada passa o
     local: `new Nome(AlturaTopo: alturaTopo)`.
   - local que **é feature** (`IFeature`) e é usado fora do recorte, além do próprio `AddFeature(x)`:
     **recusa**, apontando a linha (`'furo_1' também é usado na linha 42`).
4. **Peça nova.** Gera `public sealed record {typeName}(...) : Part { protected override IFeature Build() { ... return raiz; } }`:
   - os statements vão na ordem original, com comentários (trivia);
   - as referências a locais que viraram parâmetro são renomeadas com o modelo semântico;
   - todo `AddFeature(...)` do recorte sai.
5. **Cena.** Os statements do recorte e os `AddFeature` dos locais do recorte saem, cada um com o span
   exato e sem reformatar o resto. No lugar do `AddFeature` da raiz entra
   `var raiz = Add(new {typeName}(...), name: "raiz");`:
   - o nome da instância é o da variável antiga, então seleção, isolamento (por nome) e árvore continuam
     achando a peça;
   - se o registro era `AddFeature(raiz.Transformed(P))`, `P` vai como `at: P`;
   - as coordenadas continuam as do mundo. Ou seja, sem `.Transformed`, o `at:` fica na origem.
6. **Saída:** `PartConversion([entrada alterada, Pecas/{typeName}.cs], null)`.

## Verificação antes de gravar (`PartConversionCheck`)

No `SceneScriptViewModel.ConvertToPartAsync(feature, typeName)`:

1. Chama `converter.Convert(CurrentDocuments(), ...)`. Se recusar, devolve a mensagem para a barra de status.
2. Avalia os documentos com as mudanças aplicadas (`_sceneEvaluator.EvaluateAsync`), sem gravar.
3. Roda `PartConversionCheck.Verify(featuresAtuais, resultadoNovo, raiz)`, que exige:
   - a avaliação deu certo e existe um `PartInstance` com o nome da raiz;
   - `FeatureFingerprint` da geometria antiga é igual ao da nova, comparando `Source`/`Definition` e o
     `Placement` (o fingerprint ignora Id e nome);
   - o conjunto de nomes das outras features de topo continua o mesmo.

   Quando o fingerprint for `null` (fillet, extrusão de perfil), basta a avaliação dar certo. Nesse caso a
   barra de status avisa "geometria não verificada".
4. Só depois chama `ApplyExternalChangesAsync(changes)`.

## Limites conhecidos (documentar e não resolver)

- Só no modo projeto, porque precisa da pasta `Pecas/`.
- O Desfazer recupera a `Cena.cs`, mas o `Pecas/X.cs` continua lá sem uso. É inofensivo, e a IA de chat
  tem o mesmo comportamento.
- Depois da conversão, mover uma ferramenta da booleana pela UI **deixa de funcionar** até existir a
  "parte B" (mover ferramenta de peça no modo grupo). Duplo clique, ver as ferramentas e navegar até o
  código continuam funcionando.
- O `Build()` fica em coordenadas do mundo, então o (0,0,0) da peça não fica no canto dela.

## Arquivos

- novo `AvaloniaCadEditor.Application/Interfaces/IPartConverter.cs`
- novo `AvaloniaCadEditor.Infrastructure/Scripting/RoslynPartConverter.cs`
- novo `AvaloniaCadEditor.Application/Scenes/PartConversionCheck.cs`
- `AvaloniaCadEditor.UI/ViewModels/PartCodeEditing.cs`: +1 método em `IPartCodeEditor`
- `AvaloniaCadEditor.UI/ViewModels/SceneScriptViewModel.cs`: `ConvertToPartAsync` e o conversor no
  construtor, com default `new RoslynPartConverter()`, como o tracer já faz
- `AvaloniaCadEditor.UI/ViewModels/PropertyInspectorViewModel.cs` + a view do inspector: campo e botão
- `ARCHITECTURE.md` §4: uma linha, "Converter sólido solto em peça → `RoslynPartConverter`"
- novo `AvaloniaCadEditor.Tests/Scenes/PartConverterTests.cs`

## Verificação

Testes (`PartConverterTests`), em cenas sintéticas avaliadas pelo `RoslynSceneEvaluator` real:

- uma caixa sozinha vira peça, e o fingerprint e o nome da instância batem;
- booleana com ferramentas transitórias: os `AddFeature` das ferramentas saem e a malha não muda;
- variável externa vira parâmetro sem default e é passada na chamada;
- constante externa vira parâmetro com default;
- intermediário compartilhado gera recusa com a linha;
- raiz registrada como `AddFeature(x.Transformed(P))` vira `at: P`;
- comentários são preservados, e a função local é copiada;
- tipo já existente gera recusa;
- `PartConversionCheck` reprova quando o fingerprint difere.

Depois:

- `dotnet test AvaloniaCadEditor.Tests/AvaloniaCadEditor.Tests.csproj` (2 testes de ConexaoY já falham no HEAD);
- teste manual no app: criar um sólido solto com furo, converter pelo inspector, conferir o arquivo em
  `Pecas/`, H + Cotas e o duplo clique mostrando a ferramenta.
