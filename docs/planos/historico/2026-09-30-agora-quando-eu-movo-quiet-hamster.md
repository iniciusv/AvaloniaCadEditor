# Canonizar cadeias de Placement + arquitetura de simplificação de código

## Contexto

Mover e girar uma feature solta hoje embrulha a expressão em `.Transformed(Placement...)`
([PartSourceEditor.cs:514-529](AvaloniaCadEditor.Application/Scenes/PartSourceEditor.cs#L514-L529)), produzindo código como:

```csharp
AddFeature(cylinder_cfb1.Transformed(Placement.Translate(new Vector3D(1.06714, 3.982619, 0))
    .Then(Placement.RotationAbout(new Point3D(11, 10, 0), Vector3D.UnitZ, -60))));
```

Essa decisão está certa e não muda: reescrever as coordenadas do sólido é impossível no caso geral
porque em boa parte das features a coordenada **não está no texto** — o próprio gerador do app emite
`baseCorner: base_1.GetFaceCorner(FaceType.Top)`, `AlignToFace(...)` depois do construtor e
`new LinkedWorkPlane(face_x)` ([FeatureCodeGenerator.cs:40-41](AvaloniaCadEditor.Application/Services/FeatureCodeGenerator.cs#L40-L41),
[:203-205](AvaloniaCadEditor.Application/Services/FeatureCodeGenerator.cs#L203-L205)) — e porque uma
declaração pode gerar muitas instâncias (laços, `Part` compartilhado pelo cache de
`SceneBuilder.GetOrBuildDefinition`). O rastreio já é resolvido por proveniência de runtime
(`[CallerArgumentExpression]` + `FeatureOrigin`, [Scene.cs:22-31](AvaloniaCadEditor.Domain/Scripting/Scene.cs#L22-L31),
[:77-91](AvaloniaCadEditor.Domain/Scripting/Scene.cs#L77-L91)), que é mais preciso do que qualquer
marcador de tipo estático.

Sobram dois problemas reais, que este plano atende:

1. **A cadeia cresce.** `TryMergeRotation` só funde quando pivô e eixo são idênticos
   ([:288-312](AvaloniaCadEditor.Application/Scenes/PartSourceEditor.cs#L288-L312)); girar em pivôs
   diferentes empilha um `.Then` a cada vez, sem limite.
2. **Falta um lugar para limpar o código depois.** Assar transformações em coordenadas literais é
   desejável quando é provável seguro — mas é só *uma* das simplificações possíveis, e não pode
   nascer como uma função solta que as próximas vão ter que contornar.

Resultado pretendido: a expressão de posição nunca passa de dois termos, e existe um ponto único
onde qualquer simplificação futura entra como regra, com verificação garantida pelo orquestrador.

---

## Fase 1 — Canonizar a cadeia (implementar agora)

Qualquer movimento rígido cabe em **no máximo dois termos**: uma rotação e uma translação. Em vez de
acrescentar um `.Then`, calcular o `Placement` resultante e reescrever a expressão inteira na forma
canônica.

### 1.1 Domínio: decompor um `Placement`

[Placement.cs](AvaloniaCadEditor.Domain/ValueObjects/Placement.cs) — a matriz é privada
(`_m00.._m22`, linha 19) e `Translation` já é pública. Acrescentar:

```csharp
/// <summary>Eixo e ângulo (graus) da rotação deste placement. False se for só translação.</summary>
public bool TryGetRotation(out Vector3D axis, out double degrees)
```

- extrair eixo/ângulo da matriz (traço → ângulo; parte antissimétrica → eixo), tratando os casos
  degenerados: identidade (`HasRotation` false, linha 38) e 180°, onde a parte antissimétrica zera e
  o eixo vem da diagonal de `R + I`;
- arredondar ângulo e componentes do eixo com a mesma tolerância do resto do tipo (`Close`, linha 136),
  e reconhecer eixo canônico (`UnitX/Y/Z`) para o gerador de texto poder emitir `Vector3D.UnitZ`.

### 1.2 Application: emitir a forma canônica

[PartSourceEditor.cs](AvaloniaCadEditor.Application/Scenes/PartSourceEditor.cs) —
`ApplyPlacement` (:514) hoje tem três caminhos: funde numericamente (`tryMerge`), encadeia `.Then`
(:524), ou embrulha do zero (:528). Inserir a canonização **antes** do `.Then`:

- `TryEvaluatePlacementExpression(ExpressionSyntax) → Placement?`: interpreta uma cadeia de
  `Placement.Translate / RotationAbout / Rotation / RotationZ / At` ligadas por `.Then`, usando os
  decodificadores que já existem — `TryPoint3D`, `TryVector3D`, `TryNumber`
  ([:314-369](AvaloniaCadEditor.Application/Scenes/PartSourceEditor.cs#L314-L369)) — e constrói o
  `Placement` real pela API do domínio. Devolve `null` se **qualquer** argumento não for literal
  (`base_z`, `Medidas.Espessura`, `i * 150`): nesse caso o comportamento atual (`.Then`) continua
  valendo, intocado.
- `Canonicalize(Placement composed, Point3D? preferredPivot) → string`: emite
  - só translação → `Placement.Translate(new Vector3D(...))`;
  - rotação sem translação residual → `Placement.RotationAbout(pivot, eixo, graus)`;
  - os dois → `Placement.RotationAbout(pivot, eixo, graus).Then(Placement.Translate(residual))`.

  **Escolha do pivô** (o que preserva a legibilidade): se a cadeia antiga já tinha um
  `RotationAbout` cujo eixo coincide com o eixo composto, reusar aquele pivô e jogar a diferença no
  `Translate`; senão usar a origem (`Placement.Rotation`). É exatamente o caso do exemplo do usuário:
  `RotationAbout(new Point3D(11, 10, 0), Vector3D.UnitZ, -60).Then(Placement.Translate(...))` — dois
  termos, pivô que o usuário reconhece, e estável sob novas edições.
- **Auto-verificação barata**: comparar `composed.Apply` com o `Placement` reconstruído a partir do
  texto canônico em ~4 pontos de prova (origem + 3 unitários), tolerância 1e-9. Divergiu (ângulo
  degenerado, ruído numérico) → não canoniza, cai no `.Then` de hoje. Reusar `Number()` (:744) para
  a escrita dos números.

Ordem dos caminhos em `ApplyPlacement` passa a ser: `tryMerge` numérico (preserva expressões do
usuário, ex. `e + 10` → `e + 15`) → canonização → `.Then` → embrulhar. A fusão numérica continua
vindo primeiro de propósito: ela é a única que mantém coordenadas simbólicas.

### 1.3 Testes

[PartEditingTests.cs](AvaloniaCadEditor.Tests/Scenes/PartEditingTests.cs) (:272-295 é a tabela
existente de features soltas) e um novo `PlacementDecompositionTests`:

- girar em pivô A, depois em pivô B → resultado tem exatamente 2 termos;
- mover → girar → mover → 2 termos;
- repetir 10 edições → o texto nunca ganha um terceiro `.Then`;
- rotação de 180° e de 360°/0° (casos degenerados da decomposição);
- cadeia com argumento não literal (`base_z`) → mantém o comportamento `.Then` atual;
- equivalência geométrica: `GetVertices()` da feature antes e depois da canonização coincidem
  (tolerância 1e-6), inclusive para uma feature usada como ferramenta de `BooleanFeature`
  (o desvio de [EditDeclarationInsteadOfAddFeature](AvaloniaCadEditor.Application/Scenes/PartSourceEditor.cs#L469-L497)
  continua valendo e não é alterado).

---

## Fase 2 — Arquitetura da simplificação (desenhar agora, implementar em seguida)

Novo diretório `AvaloniaCadEditor.Application/Simplification/`. O ponto central: **as regras só
propõem; quem aplica, verifica e desfaz é o orquestrador.** É isso que impede cada simplificação
futura de inventar sua própria garantia de correção.

```
Simplification/
  ISceneSimplification.cs      contrato da regra
  SimplificationContext.cs     documentos + features avaliadas + FeatureOrigin + escopo
  SimplificationCandidate.cs   uma oportunidade localizada, com Apply: source -> source
  SceneSimplifier.cs           orquestrador: coleta, aplica, re-avalia, compara, desfaz, relata
  SceneEquivalence.cs          duas avaliações são geometricamente iguais?
  Rules/…                      uma classe por regra
```

```csharp
public interface ISceneSimplification
{
    string Id { get; }                       // "bake-translation"
    string Title { get; }                    // texto da UI
    SimplificationKind Kind { get; }         // Cosmetic | Structural | Parametric
    bool IsEnabledByDefault { get; }
    IReadOnlyList<string> ConflictsWith { get; }
    IEnumerable<SimplificationCandidate> Find(SimplificationContext context);
}

public sealed record SimplificationCandidate(
    string RuleId, string FilePath, TextSpan Span,
    string Description,                      // "prism_60e9: translação foi para as coordenadas"
    Func<string, string> Apply);
```

**Invariantes do orquestrador** (as decisões que evitam puxadinho):

- **Verificação é do orquestrador, não da regra.** Aplica o candidato, re-avalia a cena por
  [`ISceneEvaluator.EvaluateAsync(documents)`](AvaloniaCadEditor.Application/Interfaces/ISceneEvaluator.cs#L59)
  — que já roda headless, é o que os testes usam — e compara com `SceneEquivalence`: mesmo conjunto
  de features (casadas por `FeatureOrigin.VariableName`) e `GetVertices()` equivalente dentro de
  tolerância. Divergiu ou não compilou → **descarta aquele candidato e segue**. Nenhuma regra
  precisa provar nada.
- **Aplicação em lote com bissecção.** Re-avaliar por candidato é caro; aplica o lote da regra,
  verifica uma vez, e só em caso de falha bissecta até isolar o candidato ruim.
- **Sem ponto fixo entre regras.** Cada `Id` roda no máximo uma passada por execução, e a saída de
  uma regra não realimenta outra na mesma execução. Necessário porque há regras **inversas**: assar
  coordenadas vs. reparametrizar (trocar literal por `parent.GetFaceCenter(...)`). `ConflictsWith`
  declara isso explicitamente; regras em conflito não rodam juntas.
- **Idempotência por contrato**, coberta por um teste genérico que roda toda regra registrada duas
  vezes e exige que a segunda passada não mude nada.
- **Edições pontuais, nunca reformatação.** Toda regra emite `TextChange` sobre o `SourceText`, como
  o `PartSourceEditor` já faz ([:26-30](AvaloniaCadEditor.Application/Scenes/PartSourceEditor.cs#L26-L30)) —
  preserva formatação e comentários do usuário. `NormalizeWhitespace()` é proibido no caminho de
  código existente.
- **Escopo declarado**: `SimplificationContext` cobre a cena inteira, um arquivo, ou uma única
  feature (a selecionada). A UI expõe "simplificar seleção" e "simplificar cena", e o relatório diz
  o que foi aplicado e o que foi recusado, por candidato.

### Regras previstas (para o contrato nascer com o formato certo)

| Id | Kind | O que faz |
|---|---|---|
| `bake-translation` | Cosmetic | Tira o `Placement.Translate` somando nas coordenadas literais do construtor. Ganho puro: mesmos números, deslocados. Reusa `OffsetExpression` ([:692-716](AvaloniaCadEditor.Application/Scenes/PartSourceEditor.cs#L692-L716)). |
| `bake-rotation` | Cosmetic | Tira o `RotationAbout` **só quando os eixos já estão explícitos no texto** (box com `DirectionX/Y/Z` escrito, prisma com `WorkPlane` literal). Fora disso, assar *piora*: um box axis-aligned é emitido sem direções ([FeatureCodeGenerator.cs:137-144](AvaloniaCadEditor.Application/Services/FeatureCodeGenerator.cs#L137-L144)) e assar 60° obrigaria a escrever nove componentes com dízimas no lugar de um `RotationAbout` que declara a intenção. |
| `dedupe-findface` | Structural | O que o [RoslynCadScriptOptimizer](AvaloniaCadEditor.Application/Services/RoslynCadScriptOptimizer.cs) já faz, migrado para regra — hoje ele roda embutido no gerador com `NormalizeWhitespace()`, inaceitável sobre código do usuário. |
| `inline-single-use` | Cosmetic | `var p_verts = new[]{…}; new PrismFeature(p_verts, …)` → inline, quando usado uma vez. |
| `merge-nested-boolean` | Structural | Diferenças/uniões aninhadas do mesmo tipo → uma só, com várias ferramentas (`BooleanFeature.ToolFeatures`/`BaseFeatures` já são listas). |
| `drop-orphan-feature` | Structural | Declaração de feature que ninguém usa nem registra. |
| `parametrize-coordinates` | Parametric | Literal que coincide com um ponto notável do pai → `parent.GetFaceCenter(...)`. **Inversa de `bake-*`** — o caso que justifica `ConflictsWith`. |
| `extract-repetition` | Parametric | Instruções repetidas → laço. |

Nenhuma dessas exige um tipo-marcador no código do usuário: as condições que impedem assar
(coordenada não literal, `LinkedWorkPlane`, `AlignToFace` pós-construtor, declaração dentro de
laço/lambda, `Part` compartilhado) são todas detectáveis pelo modelo semântico do Roslyn mais as
features já avaliadas no contexto.

### Limpeza que entra junto

`RoslynCoordinateMutator` (o mutador de coordenadas antigo), `SceneScriptViewModel.MutateCoordinates`
(:1128, sem chamadores) e `RoslynDependencyAnalyzer.GetMovableVariableNames` (:81-104, só testes)
estão órfãos e são exatamente o caminho que este plano descarta — remover, para ninguém reintroduzir
por engano. Os testes correspondentes em `RoslynParametricDeductionTests.cs:172-238` saem com eles.

---

## Verificação

**Fase 1:**
1. `dotnet test` — `PartEditingTests`, o novo `PlacementDecompositionTests`, `TransformedFeatureTests`
   e `OpenCascadeKernelTests` (rotação de sólido passa pelo caminho de reconstrução do B-Rep,
   [OpenCascadeGeometryKernel.cs:228-242](AvaloniaCadEditor.Infrastructure/OpenCascade/OpenCascadeGeometryKernel.cs#L228-L242)).
2. No app, com uma cena real: criar um cilindro, mover, girar em torno de um canto, girar em torno de
   outro, mover de novo — conferir que o código final tem **dois** termos e que o sólido está onde a
   viewport mostrava antes de cada reexecução.
3. Repetir com a feature sendo ferramenta de uma booleana: a edição deve continuar indo para a
   declaração `var x = ...;` e o corte deve acompanhar.
4. Repetir com uma coordenada vinda de variável (`var base_z = 0;`): deve manter o `.Then` e a
   geometria correta — a canonização recusa e não quebra nada.
5. `Placement` ganha membro público → conferir se `CadScriptApiReference.Build()` o expõe e se o
   `.cad/api.md` regenerado passa a listar a decomposição (o arquivo em disco já está defasado: não
   lista `RotationAbout` nem `Transformed`).

**Fase 2:** além dos testes por regra, o teste genérico de idempotência sobre o registro de regras, e
um teste de que um candidato deliberadamente errado (regra de mentira que desloca sem somar) é
**recusado** pelo `SceneSimplifier` — é a prova de que a rede de segurança funciona.
