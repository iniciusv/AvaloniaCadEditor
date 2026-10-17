# Mover/girar uma ferramenta de booleana deve mover o corte, não criar um sólido novo

## Contexto
Mover uma feature solta reescreve só o argumento do `AddFeature`:
`AddFeature(x)` → `AddFeature(x.Transformed(Placement.Translate(...)))`
([PartSourceEditor.EditTransformedFeature](AvaloniaCadEditor.Application/Scenes/PartSourceEditor.cs#L400)).
Isso cria um **objeto novo**. A `BooleanFeature` continua referenciando a variável `x` original, parada, então:
- o corte (o vazio) não se move;
- o objeto transformado não é operando de nenhuma booleana, fica visível e aparece como um sólido "novo" no lugar de destino.

Exemplo do usuário:
`AddFeature(cylinder_ddc7.Transformed(Placement.Translate(new Vector3D(-6.43, -6.09, 0))))`, enquanto
`boolOp_90c4` ainda usa `toolFeatures: new IFeature[] { cylinder_ddc7, … }`.

O mesmo vale para girar (`RotateFeature`) e para qualquer feature referenciada por outra, como uma booleana aninhada.

## Correção: transformar na declaração quando a variável é usada por outras features
Em `PartSourceEditor`, antes do caminho atual de `EditTransformedFeature` (usado por `TranslateFeature` e `RotateFeature`):

1. Descobrir a variável do `AddFeature`: o argumento é `x` ou `x.Transformed(P)`, com `x` identificador.
2. Verificar se `x` é **referenciada em outro lugar**, ou seja, se há algum `IdentifierName` `x` fora da própria declaração
   e fora dessa chamada `AddFeature`. Se não é, segue o caminho atual sem mudança nenhuma (feature isolada ou a booleana inteira).
3. Se é referenciada, e todas essas referências são "uso simples" (argumento, elemento de array; **não** `x.Algo`, que quebraria ao
   mudar o tipo para `TransformedFeature`), e a declaração `var x = <init>;` está no mesmo arquivo:
   - aplica o placement **no inicializador da declaração**:
     `var x = <init>.Transformed(<placement>, name: "x");`
     (`new CylinderFeature(...) { Name = "x" }.Transformed(...)` é C# válido; `name:` mantém o nome, e com ele a restauração da seleção);
   - se o inicializador já termina em `.Transformed(P, …)`, funde com a mesma lógica de hoje (`TryMergeTranslate` / `TryMergeRotation`,
     senão `.Then(...)`). Para isso, extrair de `EditTransformedFeature` um helper que recebe a expressão-alvo
     (argumento do `AddFeature` **ou** inicializador da declaração) em vez de achar o `AddFeature` sozinho;
   - **reparo do estado quebrado**: se o `AddFeature` era `x.Transformed(P)` (como no script do usuário), volta a ser `AddFeature(x)`,
     e `P` é levado para a declaração antes de aplicar o movimento novo. O resultado é o placement `P.Then(novo)` (ou somado, quando der).
     Assim, mover de novo a ferramenta que ficou "solta" conserta a cena.
4. Se há referência `x.Algo`, ou a declaração não está no arquivo: mantém o comportamento atual e põe em `StatusText` um aviso de que
   o corte não acompanha. Isso fica no `EditFeatureCalls` de [SceneScriptViewModel.cs:699](AvaloniaCadEditor.UI/ViewModels/SceneScriptViewModel.cs#L699),
   via uma exceção específica ou um retorno com aviso; o mais simples é uma `InvalidOperationException` com a mensagem, que o `catch` já exibe.
   Nesse caso **não** aplicar a edição antiga, para não criar o sólido duplicado.

Copiar com Ctrl (`CopyFeature`) não muda: uma cópia **deve** ser um sólido novo e independente.

O kernel já sabe construir `TransformedFeature` como operando de booleana (`BuildTransformedShape`), e o `BooleanFeature` aceita
`IFeature`, então a ferramenta transformada entra no corte normalmente e continua oculta (é o mesmo objeto do `AddFeature`).

## Arquivos
- [PartSourceEditor.cs](AvaloniaCadEditor.Application/Scenes/PartSourceEditor.cs): `TranslateFeature`, `RotateFeature` e
  `EditTransformedFeature` (refatorado para operar sobre uma expressão-alvo). Novos helpers privados: achar o declarador de `x`,
  contar/classificar as referências e mover o `Transformed` do `AddFeature` para a declaração.
  Reaproveitar `Parse`, `FindAddFeatureCall`, `Replace`, `TryMergeTranslate`, `TryMergeRotation`, `NeedsParentheses` e `TranslationCall`/`RotationCall`.
- Nada muda em `CadViewModel`/`SceneScriptViewModel` além, se preciso, da mensagem de aviso.

## Testes (`AvaloniaCadEditor.Tests/Scenes/PartEditingTests.cs`, junto dos `TranslateFeature_*`)
- ferramenta usada numa booleana → `TranslateFeature` edita a declaração (`var t = new …{…}.Transformed(Placement.Translate(…), name: "t");`)
  e deixa `AddFeature(t);` intacto;
- mover de novo soma no `Translate` da declaração;
- `RotateFeature` na mesma situação;
- reparo: script com `AddFeature(t.Transformed(P))` e `t` numa booleana → `AddFeature(t)` e `P` combinado na declaração;
- feature não referenciada → saída idêntica à de hoje (os testes `TranslateFeature_SomaNoTransformed…` continuam passando);
- referência `t.GetFaceCenter(...)` → lança/avisa e não altera o script.
- Kernel/cena: executar o script corrigido do exemplo (com a ferramenta transformada na declaração) e conferir que o volume ou a malha
  de `boolOp_90c4` muda em relação ao original, e que só existe um sólido visível (a booleana).

## Verificação
1. `dotnet build` e `dotnet test AvaloniaCadEditor.Tests` (fechar o app se ele travar as DLLs).
2. Pôr o script do exemplo em `TestScene/Cena.cs` e rodar `AvaloniaCadEditor.UI.exe check TestScene --geometry`; restaurar o arquivo depois.
3. No app: entrar na booleana (duplo clique), mover `cylinder_ddc7` → o furo acompanha e nenhum sólido novo aparece.
   Girar também. Na cena já quebrada, mover a ferramenta de novo deve reparar a cena.
