# Esboço 2D como código: uma aba temporária por plano de trabalho

**Status:** Planejado · **Data:** 2026-10-21 · **Branch sugerida:** `feature/esboco-como-codigo`

## Contexto

O 3D segue a ideia central do app: a cena é um programa C#, e a UI reescreve o fonte. O 2D não segue.
As linhas e círculos do esboço são objetos `LineEntity`/`CircleEntity` guardados em `Sketch.Entities`, em
coordenadas locais do plano (`Entity.Plane`). As ferramentas mexem nesses objetos direto:

| Onde | O que faz hoje |
|---|---|
| `DrawLineCommand`, `DrawCircleCommand`, `DrawPolygonCommand` | `sketch.AddEntity` |
| `TrimCommand`, `ExtendCommand` | `RemoveEntity` + `AddEntity` (troca uma entidade por pedaços) |
| `OffsetCommand`, `MoveTool` com cópia | `AddEntity` |
| `MoveTool` sem cópia | `entity.Translate` **no lugar** |
| `PropertyInspectorViewModel` (entidade 2D) | altera `Center`/`Radius`/pontos **no lugar** |
| `SweepCommand` | consome o caminho com `RemoveEntity` |
| `SceneDeletionService` | `RemoveEntity` (direto, por seleção ou pelas `SourceEntityIds` de um perfil) |
| `CadViewModel.RefreshProfiles` | `PlanarTopologyService.ComputePlanarArrangement` **reescreve** a lista, quebrando as linhas nas interseções |

Os planos de trabalho derivados de face também só existem na sessão (`Sketch.WorkPlanes`, criados no
setter de `CadViewModel.ActiveWorkPlane`), e nada disso vai para o CadScript.

## Pedido e decisões do usuário

As linhas 2D de um plano passam a ser **código**. Ao contrário do modelo 3D, elas **não são gravadas no
disco**, e cada esboço pertence a um plano de trabalho.

- **Onde:** uma **aba temporária** no editor por plano com esboço ("✎ Plano 1"), só em memória. Editar o
  texto redesenha as linhas. As ferramentas reescrevem o texto. Undo/redo e a navegação seleção ↔ linha
  passam a valer para o 2D.
- **Duração:** a **sessão do app**, como as projeções (`ISketchProjectionService`). O esboço sobrevive a
  reexecutar a cena e a sair e voltar do plano. Some ao fechar o app ou trocar de projeto.
- **Escopo:** compila **isolado**, com uma API 2D mínima em coordenadas locais (u, v) do plano. **O
  próprio código declara o plano de trabalho em coordenadas globais**, para que dê para saber onde o
  esboço está na cena só lendo o texto.
- **IA:** fora da primeira entrega. O chat e o harness continuam vendo só a cena.

## O dialeto do esboço

```csharp
// Esboço · Plano 1. Vive só nesta sessão e não é gravado no disco.
// Coordenadas (u, v) locais do plano declarado abaixo.
Plane(new WorkPlane(new Point3D(152.111, -178.5, 4.9), new Vector3D(-1, 0, 0), new Vector3D(0, -1, 0)));

var l1 = Line((0, 0), (40, 0));
var l2 = Line(l1.EndPoint, (40, 20));
var l3 = Line((40, 20), (0, 20));
var l4 = Line((0, 20), (0, 0));
var c1 = Circle((20, 10), 4);
```

Regras:

1. **`Plane(...)` uma vez, antes de qualquer entidade.** Recebe um `WorkPlane`, o mesmo tipo que o
   `Cena.cs` já usa (`new WorkPlane(origin, normal, xAxis)`), ou `WorkPlane.XY`/`XZ`/`YZ` nos planos
   de origem. Faltar ou repetir é erro de diagnóstico, como um erro de compilação.
2. **O código manda no plano.** Se o usuário editar o `Plane(...)`, o `WorkPlaneFeature` do esboço vai
   para o plano novo e as entidades vão junto. A aba é a identidade do esboço, e o plano é um dado dela.
3. `Line(a, b)` e `Circle(centro, raio)` aceitam tuplas `(u, v)` ou `Point3D` local. O `Point3D` é o que
   permite `l1.EndPoint`. Cada chamada devolve a entidade.
4. Texto gerado pelas ferramentas: **uma declaração por entidade**, `var l<n>`/`var c<n>` com nome
   único, números em `InvariantCulture` no formato `0.###`, como no `FeatureCodeGenerator`.
5. A API 2D não enxerga a cena: `AddFeature` e `Add` **não compilam** num esboço.

A regra de sólidos rastreáveis (§5.2) não vale aqui. Laços e LINQ são permitidos, mas uma entidade criada
por eles **não é editável pelas ferramentas**, como um `PartArgument` com `IsEditable = false` (ver
"Edição pontual").

## Fluxos

### Desenhar ou alterar com uma ferramenta

```
clique na viewport (modo esboço)
  → ICommandTool.OnMouseDown              (sem mudança: chama sketch.AddEntity/RemoveEntity)
  → Sketch registra o diário da ação      (Added/Removed)
  → CadViewModel: sketch.TakePendingEdits()
  → ISketchSession.CommitAsync(plano, edits)
      SketchSourceEditor: edição sintática pontual do texto da aba
        removida → apaga a declaração dela (EntityOrigin → linha)
        removida + adicionadas no mesmo lote → troca a declaração no mesmo lugar (trim, extend)
        só adicionada → acrescenta no fim
      ISketchEvaluator.EvaluateAsync(texto)
  → Sketch.ReplacePlaneEntities(plano, resultado)   (sem diário)
  → RefreshProfiles()
```

Se alguma entidade removida não for editável (laço, várias entidades numa declaração, chamada dentro de
um método), o lote inteiro é recusado. O texto não muda, as entidades voltam a ser as da última
avaliação, e a barra de status diz qual linha impede a edição.

### Editar o texto à mão

F5 com a aba de esboço ativa avalia **só aquele esboço**, e a cena não roda. F5 na cena avalia só a
cena, e os esboços continuam como estão (hoje `RunCodeAsync` já não toca em `Sketch.Entities`). Um erro
deixa as entidades da última avaliação boa e marca as linhas na aba, como acontece com a cena.

### Extrudar um perfil do esboço

Não muda. O `FeatureCodeGenerator.GenerateExtrusionCode` continua escrevendo **coordenadas literais** no
`Cena.cs`. Como o esboço não é gravado, a cena **nunca** pode citar variáveis dele.

## Organização por camada

| Camada | Novo / alterado | Responsabilidade |
|---|---|---|
| **Domain** `Scripting/SketchScript.cs` | `SketchScript` (API estática: `Plane`, `Line`, `Circle`), `SketchScriptBuilder`, `SketchScriptHost` (`AsyncLocal`, igual ao `SceneHost`), `EntityOrigin` (`ConditionalWeakTable<Entity, SourceOrigin>`, reaproveitando `SourceOrigin`) | O dialeto e a ligação entidade → arquivo/linha. Sem estado estático mutável (§5.6) |
| **Domain** `Entities/Sketch.cs` | diário de `AddEntity`/`RemoveEntity` (`TakePendingEdits`), `ReplacePlaneEntities(plano, entidades)` sem diário | O sketch continua sendo o que a viewport lê. A verdade passa a ser o texto |
| **Domain** `Services/PlanarTopologyService.cs` | cada pedaço guarda de qual entidade veio (`SourceEntityId`) | O arranjo vira dado **derivado**, e um perfil ainda consegue apontar para as linhas do código |
| **Application** `Interfaces/ISketchEvaluator.cs` | `SketchEvaluationResult(WorkPlane? Plane, IReadOnlyList<Entity> Entities, IReadOnlyList<ScriptDiagnostic> Diagnostics)` | Porta. O Roslyn fica na Infrastructure |
| **Application** `Sketching/` | `SketchDocument` (texto, `WorkPlaneFeature`, entidades, diagnósticos, histórico), `SketchSourceEditor` (Roslyn sintático: acrescentar, apagar, trocar declaração, trocar o `Plane`), `SketchCodeWriter` (entidade → declaração e plano → `Plane(...)`), `SketchEditBatch`, `ISketchSession`/`SketchSession` (um documento por plano, commit, recusa, undo/redo, `Clear`) | Caso de uso. Puro, sem I/O, testável sem UI |
| **Application** `Services/SceneDeletionService.cs`, `Commands/MoveTool.cs` | apagar um perfil apaga as **entidades de origem** dos pedaços. Mover sem cópia vira remover + adicionar (sem `Translate` no lugar) | Tudo passa pelo diário. Mudança no lugar não entra no diário e não chega ao código |
| **Infrastructure** `Scripting/RoslynSketchEvaluator.cs` | compila com as mesmas referências e o mesmo `AssemblyLoadContext` colecionável do `RoslynSceneEvaluator` (extrair o comum num helper interno), com `using static SketchScript` em vez de `Scene` | Detalhe técnico do compilador |
| **UI** `ViewModels/SceneScriptViewModel.cs` | abas de esboço na lista de arquivos ("✎ Plano 1"). `SaveDocumentsToDisk` e a avaliação da cena as ignoram. F5 e Ctrl+Z vão para a sessão quando a aba ativa é de esboço | Só delega à `ISketchSession` |
| **UI** `ViewModels/CadViewModel.cs` | depois de cada ação de ferramenta (onde hoje chama `RefreshProfiles`), commit do diário. `RefreshProfiles` usa o arranjo só para reconhecer perfis e **não** chama mais `UpdateEntities`. Seleção preservada entre avaliações | Só delega |
| **UI** `ViewModels/PropertyInspectorViewModel.cs` | editar uma entidade 2D gera um lote remover + adicionar | O inspector escreve no código, como no 3D |
| **UI** `App.axaml.cs` | compõe `RoslynSketchEvaluator` → `SketchSession` | Único ponto de composição |

O ciclo de vida da sessão acompanha o projeto. `OpenProjectAsync`, `CloseProject` e `NewSceneAsync`
chamam `ISketchSession.Clear()`, como já acontece com as projeções.

## Edição pontual

O `SketchSourceEditor` segue a regra do `PartSourceEditor`: edita a árvore sintática e troca só o trecho
necessário, então formatação, comentários e lógica escrita à mão sobrevivem. A entidade chega à
declaração pelo `EntityOrigin` (linha, via `CallerLineNumber`). Uma entidade é **editável** quando:

- a declaração está no nível de cima do arquivo (fora de laço, lambda ou método);
- a declaração cria exatamente uma entidade (`var x = Line(...);` ou `Line(...);`);
- nenhuma outra declaração usa a variável dela. Se usar (`Line(l1.EndPoint, ...)`), apagar `l1`
  quebraria a compilação, então a ferramenta recusa e diz qual linha depende dela.

## Mudanças de comportamento visíveis

1. **As linhas não são mais quebradas na lista.** Hoje, desenhar uma cruz vira quatro segmentos
   guardados. Depois da mudança o código guarda as duas linhas como foram escritas. Selecionar ou apagar
   pega a linha inteira, e para cortar usa-se o Trim. Os perfis continuam vindo do arranjo, que agora é
   calculado e não gravado.
2. **Os Ids das entidades mudam a cada avaliação.** A seleção é reaplicada por **igualdade geométrica**
   (mesmo tipo e mesmos pontos, com tolerância). Entidades que a edição não tocou continuam marcadas.
3. **Ctrl+Z no modo esboço desfaz no esboço**, com histórico próprio por aba. Hoje o
   `CodeHistoryManager` é limpo a cada troca de aba (`SwitchActiveFile`). O histórico do esboço mora no
   `SketchDocument` e não é limpo.

## Etapas

1. **Dialeto e avaliador.** `SketchScript`, `EntityOrigin`, `ISketchEvaluator`, `RoslynSketchEvaluator`.
   Testes: texto → entidades no plano declarado, origem por linha, `Plane` ausente ou repetido dá
   diagnóstico, `AddFeature` não compila. **Medir** o tempo de uma avaliação com o compilador já
   aquecido: a meta é < 100 ms. Acima disso, a etapa 3 mostra o resultado do lote na hora e avalia em
   segundo plano.
2. **Sessão e edição.** `SketchCodeWriter`, `SketchSourceEditor`, `SketchSession`, diário no `Sketch`.
   Testes sem UI: desenhar acrescenta uma declaração, trim troca no mesmo lugar, apagar remove, entidade
   de laço recusa o lote inteiro e não muda o texto, editar o `Plane` move as entidades, undo/redo.
3. **Ligação na UI.** Abas, commit depois de cada ação, arranjo derivado (`SourceEntityId`),
   `SceneDeletionService`/`SweepCommand`/`MoveTool`/inspector pelo diário, seleção preservada, F5 e
   Ctrl+Z.
4. **Navegação.** Selecionar uma entidade na viewport mostra a linha dela na aba de esboço, como o
   `RevealFeature` faz no 3D. O caminho inverso (cursor no código → entidade marcada) entra se for barato.
5. **Documentação.** ARCHITECTURE.md: fluxo novo em §3 ("Esboçar"), linha em §4 ("Mudar o dialeto ou a
   edição do esboço"), nota em §3.2 de que as ferramentas de esboço continuam sem emitir
   `CodeGenerated` (passam pelo diário). `ArchitectureDocTests` cobre os caminhos novos.

Cada etapa fecha com `dotnet test` dos dois projetos de teste. Os testes novos ficam em
`Tests/Sketching/`. Não abro o app nem rodo teste de GUI sem a sua permissão.

## Riscos e pontos em aberto

- **Plano ligado a face (`LinkedWorkPlane`).** O `Plane(...)` grava um retrato literal do plano. Se a
  face se mexer quando a cena rodar de novo, o esboço **não** acompanha. Na v1 o literal manda. Para
  seguir a face, a API 2D precisaria citar a cena, e isso contraria o escopo isolado. Fica para depois.
- **Tempo de compilação** num desenho rápido de várias linhas seguidas: ver a medição da etapa 1.
- **`EdgeChainService` e varredura:** o caminho de um sweep passa a ser feito de linhas inteiras, não de
  pedaços do arranjo. Conferir na etapa 3 se algum caminho dependia de selecionar só um pedaço.
- **Planos sem esboço** continuam sem aba. A aba nasce no primeiro commit naquele plano.
