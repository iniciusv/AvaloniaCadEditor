# Furo com porca como peça reutilizável (sub-peça usada como vazio)

## Contexto e resposta à pergunta

**O código atual permite isso? Em parte.** Falta uma peça pequena na API.

O que já funciona:
- **Kernel.** Uma `PartInstance` (e também uma `TransformedFeature`) pode ser operando de `BooleanFeature`, inclusive girada, desde que a definição não use fillet nem malha importada ([PartInstance.cs:49](AvaloniaCadEditor.Domain/Features/PartInstance.cs#L49), [OpenCascadeGeometryKernel.cs:244](AvaloniaCadEditor.Infrastructure/OpenCascade/OpenCascadeGeometryKernel.cs#L244), `BuildInstanceShape`).
- **Fingerprint e cache de malha.** Ambos já percorrem `PartInstance`.
- **`FactExtractor` (Analysis, em andamento).** Já gera nomes qualificados `inst/solido` para instâncias aninhadas.

O que falta:
- **Não há como criar uma `PartInstance` dentro de um `Build()`.**
  - `Scene.Add` registra a peça na cena (proibido em arquivo de peça).
  - `Part.Build()` é `protected`.
  - `Part.CreateDefinition()` é `internal`.
  - Um script em `Pecas/` não tem, portanto, como obter a definição de outra peça.
- **A regra da IA proíbe o uso.** [CadScriptRules.cs:68](AvaloniaCadEditor.Application/Ai/CadScriptRules.cs#L68) diz que "instâncias de peça ainda não podem ser operandos de BooleanFeature". A frase está desatualizada em relação ao kernel, mas na prática é verdadeira, porque não há API para isso.
- **Rastreamento por nome.** O `RoslynPartSolidTracer` só reconhece sólidos-folha declarados com `new XFeature(...)` (`LeafSolids`, [RoslynPartSolidTracer.cs:251](AvaloniaCadEditor.Infrastructure/Scripting/RoslynPartSolidTracer.cs#L251)).

**Objetivo.** Abrir essa porta com o mínimo de código no Domain e, sobre ela, criar `FuroPorca`: um record `Part` com furo + alojamento de porca. Ele cobre:
- a inserção **lateral** (porca entra perpendicular ao furo, com canal): `furo_x_*`, `furo_y_2`;
- a inserção **axial** (porca entra pelo eixo do furo, pela face): `furo_porca_*` + `alojamento_porca_1/2/3`.

Com isso, a `PlacaSuporteCarrinhoY` passa a ter uma declaração por ponto de fixação.

## Etapa 1: Domain, instanciar uma peça dentro de outra

Arquivo [Part.cs](AvaloniaCadEditor.Domain/Features/Part.cs): nova classe estática `PartExtensions` no mesmo arquivo.

```csharp
public static PartInstance<T> Instance<T>(this T part, string name, Placement? at = null) where T : Part
```

- A definição vem de `SceneHost.CurrentBuilder.GetOrBuildDefinition(part)`, que é acessível de dentro do Domain. Assim, peças iguais compartilham a definição, e fora de um host usa-se o builder `Unhosted`.
- A instância **não** é registrada na cena: só é devolvida para entrar numa booleana.
- `name` é obrigatório e deve ser um literal igual ao nome da variável (regra de rastreabilidade).
- A reentrância é segura: `GetOrBuildDefinition` da hospedeira chama `Build()`, que chama `GetOrBuildDefinition` da sub-peça. É uma inserção de outra chave, sem iteração em curso.
- Para escapar do apelido `.Instance(...)` nos scripts, basta que o namespace `AvaloniaCadEditor.Domain.Features` já esteja nos `GlobalUsings`, e está.

## Etapa 2: a peça `FuroPorca` (AiCore/Pecas/FuroPorca.cs)

```csharp
public enum Rosca { M3, M4, M5 }
public enum InsercaoPorca { Lateral, Axial }

public sealed record FuroPorca(
    Point3D Entrada,               // centro do furo, 1 mm FORA da face alvo (convenção atual da placa)
    Vector3D Direcao,              // para dentro do material
    double Profundidade,           // Entrada → face de entrada do alojamento
    Rosca Rosca = Rosca.M3,
    InsercaoPorca Insercao = InsercaoPorca.Lateral,
    Vector3D? EixoHexagono = null, // vértice do hexágono; na Lateral também é o lado do canal (padrão +Z)
    double ComprimentoCanal = 0,   // Lateral: do eixo até além da face por onde a porca entra
    double FolgaAntes = 8,         // furo antes do alojamento
    double FolgaDepois = 8,        // furo depois do alojamento
    double FolgaAlojamento = 0,    // por lado (1ª camada: 0,2)
    double? EspessuraAlojamento = null, // null = espessura da porca da tabela
    double? DiametroFuro = null,
    double? LarguraPorca = null) : Part
```

O `Build` segue a regra de sólidos explícitos: exatamente 3 declarações nomeadas.
- `furo`: `CylinderFeature`.
- `alojamento`: `PrismFeature` sobre um `WorkPlane(fundo, Direcao, eixoU)`.
  - O perfil na **Lateral** é o de `PerfilPorcaHorizontal` (hexágono + canal até `ComprimentoCanal`).
  - O perfil na **Axial** é o hexágono puro, ampliado por `FolgaAlojamento`.
  - `eixoU` é derivado de `EixoHexagono × Direcao`.
- `furo_porca`: `BooleanFeature.CreateUnion("furo_porca", furo, alojamento)`. Os dois sólidos se sobrepõem, não são tangentes, então não cai na falha silenciosa da memória.

Tabela de medidas por `Rosca`:
- **M3:** exatamente os valores atuais da placa: Ø3,4, porca 5,83 entre faces, espessura 3.
- **M4 e M5:** valores com a mesma folga de impressão: Ø4,5 / 7,33 / 3,2 e Ø5,5 / 8,33 / 4,0.

Os valores opcionais, quando informados, sobrescrevem a tabela.

Todas as contas que hoje vivem na placa migram para cá: `InicioFuroPorca`, `PlanoPorca`, `PerfilPorcaHorizontal`, `apotema/raio`.

## Etapa 3: refatorar a `PlacaSuporteCarrinhoY`

Cada par furo + alojamento vira uma declaração.

Laterais (5 em X e 1 em Y):
```csharp
var fixacao_x_6 = new FuroPorca(new Point3D(17 * sx, 2, zFuros), new Vector3D(-sx, 0, 0), profAlojamentoProximoFace,
    ComprimentoCanal: hTopoPorca).Instance("fixacao_x_6");
```

Axiais (3): `Entrada` em z = -6,5 com `Direcao` +Z, `Profundidade` 0, `EixoHexagono` +Y (vértices em ±Y, faces planas em ±X, como hoje).
- `EspessuraAlojamento: 4`.
- `FolgaAlojamento: 0.2`.
- `FolgaAntes: 8` e `FolgaDepois: 9`, que reproduz o furo atual de z -14,5 a +6,5. Com 8, o furo terminaria rente à face de cima e criaria uma face coplanar.

O `CreateDifference` passa de 22 para 12 ferramentas: 3 de fixação ao carrinho, 3 axiais e 6 laterais. O chanfro continua antes dos furos.

**Correção que a refatoração força.** Hoje `furo_porca_1` está em (5,5; 12,5) e `alojamento_porca_3` em (5,5; 12), ou seja, 0,5 mm fora de centro, contrariando o `<summary>`. Com uma instância só, furo e alojamento ficam coaxiais por construção.

Proposta: centrar em **y = 12,5**, que é o alinhamento documentado com `furo_fixacao_carrinho_2`. Isso move o alojamento 0,5 mm. Confirmar na revisão.

Atualizar o `<summary>` para refletir a peça reutilizável.

Já existem outras peças com o mesmo padrão, que podem adotar o `FuroPorca` depois, fora deste escopo: `BlocoFixacaoMgn9`, `SuporteMotorNema17` e `SuporteHotendV6`.

## Etapa 4: ferramentas do editor e regras da IA

- **[CadScriptRules.cs](AvaloniaCadEditor.Application/Ai/CadScriptRules.cs) (regras de peças).**
  - Trocar a frase "PartInstance ainda não podem ser operandos" por: uma peça reutilizável (por exemplo um furo com porca) pode entrar numa booleana dentro do `Build()` via `var x = new Peca(...).Instance("x");`, com nome literal igual ao da variável.
  - O `.cad/api.md` é gerado a partir daqui. Verificar se `CadScriptApiReference` lista `Instance` automaticamente; se não listar, acrescentar.
- **[RoslynPartSolidTracer.cs](AvaloniaCadEditor.Infrastructure/Scripting/RoslynPartSolidTracer.cs).**
  - `LeafSolids` também aceita um declarador cujo inicializador é uma invocação `.Instance(...)` sobre um `new XxxPart(...)`. A semente passa a ser a criação do record, e assim os argumentos (`Entrada`, `Profundidade`...) são rastreados como as entradas de um sólido comum.
  - `FindSolidSeed` já encontra o literal do nome.
- **Seleção e revelação de ferramentas.**
  - `CadViewModel.CollectToolFeatures` devolve a instância aninhada como **uma** ferramenta (`fixacao_x_6`), e clicar nela leva à declaração na placa.
  - Não vamos abrir a ferramenta para mostrar `furo`/`alojamento` separados agora. Pular para dentro de `FuroPorca.cs` fica para depois.
- **Atualizar [ARCHITECTURE.md](ARCHITECTURE.md)** com uma linha sobre peças aninhadas como ferramenta.

## Arquivos críticos

| Arquivo | Mudança |
|---|---|
| `AvaloniaCadEditor.Domain/Features/Part.cs` | `PartExtensions.Instance<T>` |
| `AiCore/Pecas/FuroPorca.cs` (novo) | enums `Rosca`, `InsercaoPorca` e record `FuroPorca` |
| `AiCore/Pecas/PlacaSuporteCarrinhoY.cs` | 9 pares viram instâncias |
| `AvaloniaCadEditor.Application/Ai/CadScriptRules.cs` | regra atualizada |
| `AvaloniaCadEditor.Infrastructure/Scripting/RoslynPartSolidTracer.cs` | `.Instance(...)` como sólido-folha |
| `ARCHITECTURE.md` | nota |

## Verificação

- **Testes novos:**
  - `Tests/Scenes/PartTests`: `new Sub().Instance("x")` dentro de um `Build` devolve uma `PartInstance` com a mesma definição para parâmetros iguais (`DefinitionBuildCount` não aumenta), e a instância não aparece em `SceneBuilder.Features`.
  - `Tests/BehaviorTests/OpenCascadeKernelTests`: um bloco menos uma `PartInstance` de `FuroPorca` tem o mesmo volume que o bloco menos o par cilindro + prisma declarados soltos.
  - Tracer: `TraceArgument("PlacaSuporteCarrinhoY", "fixacao_x_6", ...)` devolve os parâmetros de peça usados.
- `dotnet test` passa, com o `-p:BaseOutputPath` alternativo se o app estiver aberto. As falhas conhecidas são os 3 `CheckCommandProcessTests` (atalho) e os 2 `ConexaoYAdaptador5mm` (já falham no HEAD).
- `AvaloniaCadEditor.UI.exe check AiCore --geometry` sai com código 0.
- **Geometria equivalente:** comparar o volume e a caixa envolvente da `PlacaSuporteCarrinhoY` (normal e espelhada) antes e depois. A única diferença esperada é o deslocamento de 0,5 mm do alojamento de `furo_porca_1`.
- **No app:**
  1. Abrir a cena AiCore e conferir a placa.
  2. Clicar num furo lateral: deve revelar `fixacao_x_6` e pular para a linha da declaração.
