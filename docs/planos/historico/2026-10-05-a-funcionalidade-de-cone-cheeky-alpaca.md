# Cilindro → cone pelas propriedades

## Context

`ConeFeature` entrou no commit `3b71fdf` como primitiva nativa completa (kernel exato, fingerprint,
testes de volume, API para a IA), mas **deliberadamente sem interface** — o próprio commit lista
"PropertyInspector" como fora de escopo. Hoje o cone só existe se a IA (ou o usuário) escrever
`new ConeFeature(...)` no código.

A ideia a implementar: o cone aparece na interface como uma **propriedade do cilindro**. Um cilindro
selecionado ganha um campo "Raio do topo"; dar a ele um valor diferente do raio da base reescreve o
construtor no código como `ConeFeature`, e igualar os raios de volta reverte para `CylinderFeature`.
É o caminho natural do furo escareado, que é o caso prático dominante no projeto.

Duas descobertas que moldam o plano:

1. **O inspector não trata `CylinderFeature` nativo.** `IsCylinder`
   ([PropertyInspectorViewModel.cs:366-390](AvaloniaCadEditor.UI/ViewModels/PropertyInspectorViewModel.cs#L366-L390))
   significa "`ExtrudeFeature` com `CircleProfile`". Selecionar uma primitiva CSG hoje mostra só o
   nome e valores de centro **obsoletos da seleção anterior**. Não existe painel de cilindro para o
   cone "ganhar" um campo: ele precisa ser criado, e isso corrige esse bug de passagem.
2. **Os dois contextos convergem para a mesma edição.** Um cilindro é selecionável como feature
   solta do script (`FeatureOrigin.Of` → arquivo + linha do `AddFeature`) ou como sólido do `Build()`
   de uma peça (`PartSourceEditor.FindToolDeclaration` → arquivo + linha da declaração). Nos dois
   casos a ação é "reescrever o `new XFeature(...)` daquela linha", então uma primitiva de
   `PartSourceEditor` serve aos dois.

O código é a fonte da verdade (`SceneScriptViewModel.cs:68-71`), então **nada muta a feature em
memória**: toda edição reescreve o construtor e reexecuta a cena, como `RenameFeatureAsync` e
`SetPartParameterAsync` já fazem.

## Decisões tomadas com o usuário

- **Interação:** campo "Raio do topo" implícito (sem botão nem combo). `r ≠ R` vira cone; `r = 0`
  vira cone completo; `r = R` volta a cilindro.
- **Escopo:** os dois contextos (feature solta e sólido de peça).
- **Edição:** raios + altura reescrevem o código. O centro fica somente-leitura (num furo de peça a
  posição vem da cadeia de parâmetros, não de um literal).

---

## 1. `PartSourceEditor`: reescrever o construtor da primitiva

[AvaloniaCadEditor.Application/Scenes/PartSourceEditor.cs](AvaloniaCadEditor.Application/Scenes/PartSourceEditor.cs) —
nova seção "Sólidos primitivos (cilindro/cone)", no estilo das existentes (métodos nomeados por
tarefa, reaproveitando `Replace`/`Insert`/`Apply`/`IsLiteral`/`Number`/`FindArgumentExpression`).

```csharp
/// Ordem dos parâmetros do construtor de cada primitiva, para achar um argumento posicional.
private static readonly Dictionary<string, string[]> SolidParameters = new(StringComparer.Ordinal)
{
    ["CylinderFeature"] = ["globalBaseCenter", "globalDirection", "radius", "height", "name"],
    ["ConeFeature"] = ["globalBaseCenter", "globalDirection", "bottomRadius", "topRadius", "height", "name"],
};
```

**`private static BaseObjectCreationExpressionSyntax? FindSolidCreation(SyntaxNode root, int line)`**
— acha o `new XFeature(...)` do sólido da linha, cobrindo os dois contextos:
1. `FindAddFeatureCall(root, line)` → `DecomposeAddFeatureArgument` → `FindFeatureDeclarator` →
   `Initializer.Value` (exatamente o caminho de `RenameFeature`, linhas 291-310);
2. senão, o `VariableDeclaratorSyntax` cujo identificador está nessa linha (sólido do `Build()`);
3. senão, a menor `ObjectCreationExpression` que começa nessa linha.
Desembrulha parênteses e `.Transformed(...)`; devolve null se não for uma criação de objeto.

**`public static string SetSolidDimension(string source, int line, string parameterName, double value)`**
— troca um argumento numérico (`radius`, `bottomRadius`, `topRadius`, `height`). Resolve o índice
posicional por `SolidParameters[tipo]`, reusa `FindArgumentExpression` e **recusa com
`InvalidOperationException` quando o valor atual não é literal**, com a mesma mensagem de
`SetArgument` ("…é calculado no código (…); edite essa expressão no editor.").

**`public static string ConvertCylinderToCone(string source, int line, double topRadius)`** — um
lote de edições via `Apply`:
1. o identificador do tipo → `ConeFeature`;
2. `radius:` → `bottomRadius:` (só o `NameColon`), quando nomeado;
3. insere o `topRadius` **logo depois do argumento de raio** — `, topRadius: 3` se nomeado,
   `, 3` se posicional (posicionais precedem nomeados, então a ordem continua válida).

**`public static string ConvertConeToCylinder(string source, int line)`** — inverso: tipo →
`CylinderFeature`, `bottomRadius:` → `radius:`, e remove o argumento `topRadius` junto com a vírgula
que o precede.

Ponto importante de desenho: a **conversão só insere e renomeia**, nunca sobrescreve um valor. Então
ela funciona mesmo quando o raio é uma expressão — o caso real de
[PlacaSuporteCarrinhoY.cs:38](AiCore/Pecas/PlacaSuporteCarrinhoY.cs#L38), onde o raio é `raioFuro`.
Só a *edição* de um valor já calculado é recusada.

## 2. `IPartCodeEditor` → `SceneScriptViewModel`: gravar e reexecutar

Em [PartCodeEditing.cs](AvaloniaCadEditor.UI/ViewModels/PartCodeEditing.cs):

```csharp
/// <summary>Onde o inspector vai mexer num sólido primitivo: a feature e, quando ela é um sólido do
/// Build() de uma peça, a instância dona (que diz em qual record procurar a declaração).</summary>
public sealed record SolidCodeRef(IFeature Solid, PartInstance? PartOwner);

/// <summary>Reescreve uma dimensão do construtor do sólido e reexecuta. Retorna o erro, ou null.</summary>
Task<string?> SetSolidDimensionAsync(SolidCodeRef target, string parameterName, double value);

/// <summary>Converte cilindro↔cone reescrevendo o construtor e reexecuta; topRadius null volta a
/// cilindro. Retorna o erro, ou null.</summary>
Task<string?> SetSolidTopRadiusAsync(SolidCodeRef target, double? topRadius);
```

Em [SceneScriptViewModel.cs](AvaloniaCadEditor.UI/ViewModels/SceneScriptViewModel.cs), junto dos
outros membros de `IPartCodeEditor` (~linha 1291), com um resolvedor comum:

```csharp
private (string File, int Line)? LocateSolid(SolidCodeRef target)
```
- `PartOwner != null` → `PartSourceEditor.FindToolDeclaration(CurrentDocuments(), PartOwner.PartTypeName, Solid.Name)`;
- senão → `FeatureOrigin.Of(Solid)` → `(SourceFileOf(origin), origin.Line)`.

Os dois métodos seguem o esqueleto já usado por `RenameFeatureAsync` (linhas 1314-1349):
`GetDocumentText` → chamada ao `PartSourceEditor` em `try/catch (InvalidOperationException)` →
`SetDocumentText` → `await RunCodeAsync()` → `if (HasErrors) return ErrorSummary` → reencontrar a
seleção (`_cadViewModel.RestoreSelectedPartSolid()` para sólido de peça,
`SelectFeatureByName(nome)` para feature solta) → `StatusText` com o valor e o arquivo/linha.

## 3. `PropertyInspectorViewModel`: painel da primitiva

[PropertyInspectorViewModel.cs](AvaloniaCadEditor.UI/ViewModels/PropertyInspectorViewModel.cs)

- Novos membros: `bool IsPrimitiveSolid`, `bool IsCenterEditable`,
  `ObservableCollection<PartParameterItem> SolidDimensions`, campo `SolidCodeRef? _solidRef`.
- `RefreshProperties`: limpar `IsPrimitiveSolid`/`SolidDimensions`/`_solidRef` no topo (como já é
  feito com `PartInstance`), marcar `IsCenterEditable = true` nos ramos existentes, e acrescentar um
  ramo antes do de `IProfile`:

```csharp
else if (obj is CylinderFeature or ConeFeature)
{
    ShowPrimitiveSolid((IFeature)obj, partOwner: null);
}
```

- `private void ShowPrimitiveSolid(IFeature solid, PartInstance? partOwner)`: título
  ("Cilindro (primitiva CSG)" / "Cone (primitiva CSG)"), `TypeBadge`, `IsCenterEditable = false`,
  centro a partir de `GlobalBaseCenter`, `DirectionText` de `GlobalDirection`, `BaseArea`/`Volume`,
  e monta as linhas de `SolidDimensions`.
- As linhas **reaproveitam `PartParameterItem`**
  ([PartParameterItem.cs](AvaloniaCadEditor.UI/ViewModels/PartParameterItem.cs)): ele já traz
  TextBox + Enter (`ApplyCommand`), `Hint`, `Error` e callback assíncrono — o mesmo contrato
  "Enter aplica no código" dos parâmetros de peça, e sem risco de uma reexecução por tecla digitada
  (o que um `NumericUpDown` com `Mode=TwoWay` causaria). Atualizar o doc comment da classe, que hoje
  diz "parâmetro de peça". Três linhas:
  - **Raio da base** → `SetSolidDimensionAsync(radius | bottomRadius)`
  - **Raio do topo** → `SetSolidTopRadiusAsync`: compara com o raio da base e decide
    `topRadius: null` (volta a cilindro) ou o valor (vira/continua cone). Para um cilindro, vem
    pré-preenchido com o raio da base; hint: "igual à base = cilindro · menor = cone · 0 = cone completo".
  - **Altura** → `SetSolidDimensionAsync("height")`
  O nome do parâmetro vai **capturado no closure** (o argumento `name` do callback é o rótulo
  exibido). O texto digitado é convertido por `PartSourceEditor.TryParseValue(typeof(double), …)`,
  que já aceita vírgula ou ponto e devolve a mensagem de erro.
- `ShowPartSolid` ganha um parâmetro opcional `IFeature? localSolid = null`; quando ele é
  `CylinderFeature`/`ConeFeature`, chama a mesma montagem de `SolidDimensions` com
  `partOwner: owner`. O painel de dimensões aparece junto com o de parâmetros da peça (aquele continua
  sendo o lugar de mover o furo; este, o de mudar raio/altura/forma).
- `ClearPartSolid()` já chama `RefreshProperties()`, que limpa tudo.

## 4. `PropertyInspectorView.axaml`

[PropertyInspectorView.axaml](AvaloniaCadEditor.UI/Views/PropertyInspectorView.axaml)

- Extrair o `DataTemplate` da linha de parâmetro (linhas 82-100) para um recurso do `UserControl`
  (`x:Key="ParameterRow"`) e usá-lo nos dois `ItemsControl`.
- Novo `Border` com `IsVisible="{Binding IsPrimitiveSolid}"`, depois do box de centro e antes do de
  parâmetros da peça: título "🔺 Dimensões do sólido (mm)", `ItemsControl` sobre `SolidDimensions`
  com o template compartilhado, e um `TextBlock` estático explicando a regra do raio do topo.
- Os três `NumericUpDown` do centro (linhas 58, 62, 66) ganham
  `IsEnabled="{Binding IsCenterEditable}"` — hoje editar o centro de uma primitiva é um no-op
  silencioso em `ApplyCenterModification`.

## 5. Ligações restantes

- [CadViewModel.cs:967](AvaloniaCadEditor.UI/ViewModels/CadViewModel.cs#L967):
  `PropertyInspector.ShowPartSolid(_partToolsOwner, SelectedPartSolid, local)` — `local`
  (`wrapper.Source`) já está na mão na linha 961.
- **Domínio:** `BaseArea` e `Volume` derivados em
  [CylinderFeature.cs](AvaloniaCadEditor.Domain/Features/CylinderFeature.cs) e
  [ConeFeature.cs](AvaloniaCadEditor.Domain/Features/ConeFeature.cs), no padrão de `ExtrudeFeature`
  (cone: `π·|h|/3·(R² + Rr + r²)`). Sem isso o box de propriedades físicas, sempre visível para uma
  feature solta, mostraria `0.00 mm³`. Isso **exige** acrescentar os dois nomes às linhas
  `[InlineData]` de `FingerprintCobreTodasAsPropriedadesPublicas`
  ([FeatureFingerprintTests.cs:232](AvaloniaCadEditor.Tests/Features/FeatureFingerprintTests.cs#L232)),
  como já é feito com `IsFullCone` — são derivados, não entram no fingerprint.
- **Árvore do modelo:** ramos para `CylinderFeature` e `ConeFeature` em
  [ModelTreeViewModel.cs:254](AvaloniaCadEditor.UI/ViewModels/ModelTreeViewModel.cs#L254), usando o
  `iconCylinder` que já existe (linha 249) e rótulos `Cilindro (R=…, H=…)` / `Cone (R=…, r=…, H=…)`.
  Sem isso, uma conversão não muda nada visível na árvore.

## 6. Testes

- **`AvaloniaCadEditor.Tests/Scenes/SolidConstructorEditTests.cs`** (novo, padrão de
  [PartToolRevealTests.cs](AvaloniaCadEditor.Tests/Scenes/PartToolRevealTests.cs)): unitários de
  `PartSourceEditor` — conversão com argumentos nomeados, posicionais e com inicializador
  `{ Name = …, IsTransientTool = true }`; raio como expressão (`raioFuro`) **converte**; editar um
  raio calculado **recusa**; ida-e-volta cilindro→cone→cilindro devolve o código original;
  `topRadius: 0`; sólido de peça localizado pela linha da declaração e feature solta pela linha do
  `AddFeature`.
- **`ModelTreeAndPropertyInspectorTests.cs`**: selecionar um `CylinderFeature` nativo liga
  `IsPrimitiveSolid`, desliga `IsCenterEditable`, enche `SolidDimensions` com três linhas e mostra o
  centro correto (hoje mostraria o da seleção anterior); um `ConeFeature` traz o raio do topo real.
- **`PartSolidSelectionTests.cs`**: ponta a ponta no estilo que já existe lá — abrir a cena, entrar
  no grupo booleano, `RevealToolFeature(furo)`, aplicar "Raio do topo", e verificar que o arquivo
  `Pecas/*.cs` passou a ter `new ConeFeature(` com o `topRadius`, que a cena reexecutou sem erro e
  que o sólido continua selecionado.

## Fora do escopo

- `FeatureToCSharpConverter.ProcessFeature` e `FeatureCodeGenerator` continuam sem emitir cone (só
  afetam a reconstrução de malha importada e o push/pull de sketch).
- Seleção de face de cone no viewport (`ParametricConeFace`, `BoxSelector`,
  `ViewportInputHandler`, `FaceReference.GetWorkPlane`) e fillet dedicado de cone.
- Criar um cone do zero pela interface (não há toolbar de primitivas; a criação continua por código
  ou IA).
- Um sólido de peça **sem `Name` literal** perde a seleção depois da conversão, porque o nome
  gerado pelo construtor muda (`"Cylinder (R6, H10)"` → `"Cone (…)"`) e
  `RestoreSelectedPartSolid` casa por nome. As peças que seguem a regra do repo (cada sólido com
  `Name` literal) não são afetadas; o caso sem nome deve apenas avisar no `StatusText`.

## Verificação

1. `dotnet build AvaloniaCadEditor.slnx`
2. `dotnet test AvaloniaCadEditor.Tests` — a suíte inteira, de olho em
   `FeatureFingerprintTests` (a theory de cobertura acusa as propriedades derivadas novas),
   `OpenCascadeKernelTests` (volumes de cone) e os três arquivos de teste acima.
3. Manual, com a cena `AiCore`: abrir `PlacaSuporteCarrinhoY`, entrar no grupo booleano, clicar no
   furo vermelho `furo_fixacao_carrinho_1`
   ([PlacaSuporteCarrinhoY.cs:38](AiCore/Pecas/PlacaSuporteCarrinhoY.cs#L38) — raio posicional e
   calculado, o caso mais exigente), digitar `2` em "Raio do topo" e Enter. Esperado: a linha vira
   `new ConeFeature(new Point3D(...), Vector3D.UnitZ, raioFuro, 2, 13) { Name = "furo_fixacao_carrinho_1", IsTransientTool = true }`,
   a cena reexecuta, o furo aparece escareado no viewport e o sólido continua selecionado e
   realçado no editor. Igualar o raio do topo ao `raioFuro` reverte para `CylinderFeature`.
4. `dotnet run --project AvaloniaCadEditor.UI -- check <cena>` (`Cli/CheckCommand.cs`) para
   confirmar que a cena editada compila fora do app.
