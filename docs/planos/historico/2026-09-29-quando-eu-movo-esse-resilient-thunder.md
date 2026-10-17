# Copiar, rotacionar e mover features soltas com `Transformed`

## Contexto
Rotacionar com cópia (Ctrl) o `prism_60e9` mostra a projeção, mas nada é criado. O motivo é que o `prism_60e9` é uma **feature solta** do script (`AddFeature(x)`), não uma peça `Add(...)`. Hoje, para feature solta:
- o `CopyPartInstances` e o `RotateTool_FeaturesRotated` do `CadViewModel` descartam a operação (só um aviso na barra de status);
- o Mover sem cópia reescreve coordenadas (`GetMovableVariableNames` + `RoslynCoordinateMutator`) e **erra em prismas com plano**: desloca a origem do plano (`prism_60e9_plane`) e também os vértices, que são locais ao plano, então a peça anda o dobro.

Decisão do usuário: usar uma operação nova no código, `x.Transformed(Placement ...)`, para **copiar, rotacionar e mover** features soltas. O kernel já sabe construir um sólido transformado: é o caminho `BuildShape(..., transform)` feito para peças giradas.

Como o código fica:
- **cópia:** `var prism_60e9_copia1 = prism_60e9.Transformed(Placement.RotationAbout(new Point3D(..), Vector3D.UnitZ, 90), name: "prism_60e9_copia1");` + `AddFeature(prism_60e9_copia1);`, logo abaixo do `AddFeature` original;
- **rotação ou movimento sem cópia:** `AddFeature(prism_60e9.Transformed(...))`. Operações seguidas com o mesmo tipo e o mesmo pivô/eixo somam no valor que já está lá.

## 1. Domínio: `AvaloniaCadEditor.Domain/Features/TransformedFeature.cs` (novo)
- `public sealed class TransformedFeature : FeatureBase`:
  - propriedades `Source` (IFeature) e `Placement`;
  - `Dependencies => [Source]`;
  - `GetVertices()` = vértices da `Source` passados por `Placement.Apply`;
  - `Name` = o `name` informado ou `Source.Name`. Manter o nome da origem faz a seleção por nome continuar funcionando depois de reexecutar a cena;
  - não esconde a `Source`: a cópia e o original coexistem.
- Extensão `public static TransformedFeature Transformed(this IFeature feature, Placement placement, string? name = null)` num `FeatureTransformExtensions`, no mesmo namespace, que os scripts já importam. Se `feature` já é um `TransformedFeature`, achata em `new TransformedFeature(t.Source, t.Placement.Then(placement))`.
- `PartInstance.DependsOnFixedGeometry` passa a atravessar `TransformedFeature` (a `Source`). `EnsureCanBeBooleanOperand` passa a tratar também um `TransformedFeature` com rotação cuja `Source` tenha fillet ou malha importada.

## 2. Kernels
**`OpenCascadeGeometryKernel.BuildShape`**, novo caso `TransformedFeature tf`:
- a transformação efetiva é `tf.Placement`, seguida de `transform` quando houver um externo;
- **sem rotação:** `BuildShape(tf.Source, settings)`, que usa o cache, e depois `.Translate(...)` numa cópia, como já faz o `BuildInstanceShape`. Assim funciona até com fillet;
- **com rotação:** `BuildShape(tf.Source, settings, efetiva)`, reconstruindo.

**`BuildInstanceShape(instance, settings, outer)`** passa a receber o `transform` externo:
- a transformação efetiva é `instance.Placement`, seguida de `outer`;
- com rotação, reconstrói; senão, translada;
- isso cobre copiar uma booleana que tem instâncias como operandos.

**`PureMathGeometryKernel.BuildFeatureMesh`**: `TransformedFeature t` → `BuildFeatureMesh(t.Source).Transform(t.Placement)`.

## 3. Edição do código: `AvaloniaCadEditor.Application/Scenes/PartSourceEditor.cs`
Reaproveitar `TryMergeRotation`, `RotationCall`, `OffsetExpression`, `TryNumber`, `Number`, `NeedsParentheses` e `LineOf`.
- `FindAddFeatureCall(root, line)`: a chamada `AddFeature(...)` cuja linha bate com a do `FeatureOrigin` (mesma lógica do `FindAddCall`).
- `TranslateFeature(source, line, dx, dy, dz)`, com `E` = argumento do `AddFeature`:
  - `E` é `X.Transformed(P, ...)` com `P` = `Placement.Translate(new Vector3D(a, b, c))`, ou terminando em `.Then(Placement.Translate(...))` → soma nos números;
  - `E` é `X.Transformed(P, ...)` com outro `P` → `P.Then(Placement.Translate(...))`;
  - senão → `E.Transformed(Placement.Translate(new Vector3D(dx, dy, dz)))`, com `E` entre parênteses se `NeedsParentheses`.
- `RotateFeature(source, line, pivot, axis, degrees)`: mesma estrutura, usando `TryMergeRotation` no `P`.
- `CopyFeature(source, line, string placementCode)`:
  - gera um nome único `<base>_copia<n>`, em que `<base>` é o identificador de `E` (ou o que vem antes de `.Transformed`), sem colidir com identificadores já existentes;
  - insere depois da instrução do `AddFeature`: `var n = E.Transformed(placementCode, name: "n");` + `AddFeature(n);`, com a indentação e a quebra de linha da instrução.
- Extrair do `DuplicateInstance` o helper `InsertAfterStatement(source, statement, params string[] lines)`, que cuida da indentação e do `\n`/`\r\n` da própria linha, e usá-lo nos dois.
- Novo `TranslationCall(dx, dy, dz)` → `Placement.Translate(new Vector3D(...))`.

## 4. `AvaloniaCadEditor.UI/ViewModels/SceneScriptViewModel.cs`
- Novo `EditFeatureCalls(IEnumerable<IFeature> features, Func<IFeature, string, int, string> edit)`:
  - junta `(feature, arquivo, linha)` via `FeatureOrigin`, sem repetir;
  - processa **de baixo para cima**, para as inserções das cópias não deslocarem as linhas seguintes, mesmo misturando instâncias e features soltas no mesmo arquivo;
  - captura `InvalidOperationException` e mostra na barra de status.
- `MovePartInstances`, `RotatePartInstances` e `CopyPartInstances` passam a usar esse helper. A API pública fica igual, porque os testes usam.

## 5. `AvaloniaCadEditor.UI/ViewModels/CadViewModel.cs`
Cada handler escolhe a edição pelo tipo de feature, numa única chamada a `EditFeatureCalls`:
- **Mover:** instância → `TranslateInstance`; solta → `TranslateFeature`. Sai a reescrita `GetMovableVariableNames` + `MutateCoordinates`; esses métodos ficam na biblioteca, com os testes deles.
- **Rotacionar:** instância → `RotateInstance`; solta → `RotateFeature`. Sai o aviso de "ignoradas".
- **Copiar (Mover ou Rotacionar):** instância → `DuplicateInstance` + posicionar; solta → `CopyFeature(..., TranslationCall/RotationCall)`. Sai o aviso de "ignoradas".
- A seleção é restaurada como hoje: por nome para mover e rotacionar, e pelos nomes novos para cópias.

## Testes
- **Domínio:**
  - `TransformedFeature.GetVertices` sai transformado;
  - `Transformed` de um `Transformed` achata;
  - booleana com `Transformed` girado de um fillet é recusada.
- **Kernel:**
  - prisma com plano (como o `prism_60e9`), `Transformed` com 90° em Z → malha com a caixa envolvente girada e o mesmo volume;
  - `Transformed` só com translação de uma feature com fillet → malha não vazia.
- **`PartSourceEditor`:** casos de `TranslateFeature`, `RotateFeature` e `CopyFeature`: sem `Transformed`, fundindo no mesmo tipo/pivô, encadeando `Then`, nome único na segunda cópia.
- **Integração (`PartEditingTests`, com o script do usuário resumido):**
  - copiar rotacionando o `prism_60e9` → a cena roda sem erro, com +1 feature, e o original fica intacto;
  - mover o `prism_60e9` (5, 0, 0) → os vértices mundiais andam exatamente 5 em X, regressão do deslocamento dobrado.

## Verificação
- `dotnet test AvaloniaCadEditor.slnx`.
- Manual, com o script do usuário:
  1. selecionar o `prism_60e9`, apertar R e depois Ctrl, e girar 90° → aparece o `prism_60e9_copia1` girado e o original fica;
  2. R sem Ctrl → o prisma gira no lugar;
  3. M → ele se move exatamente o que foi arrastado.
