# Ferramentas 2D: Extend (X), Polígono (G) e Offset (O)

## Context
No modo Sketch 2D existem Line, Circle e Trim. Faltam:
- **Extend** — prolongar linhas até se encontrarem.
- **Polígono** — desenhar polígono regular de N lados.
- **Offset** — criar um contorno paralelo de uma **região** (perfil fechado reconhecido), para dentro ou para fora.

Todas seguem o padrão `ICommandTool` existente (ex.: [TrimCommand.cs](AvaloniaCadEditor.Application/Commands/TrimCommand.cs), [DrawCircleCommand.cs](AvaloniaCadEditor.Application/Commands/DrawCircleCommand.cs)). Entidades criadas recebem `Plane = sketch.Plane` (ou o plano da entidade de origem). Após cada ação, o `CadViewModel` chama `RefreshProfiles()` → arranjo planar, reconhecimento de perfis e regeneração do código C# (undo por script continua funcionando).

---

## 1. Extend (X)
Comportamento escolhido: **AutoCAD + canto**.
1. Clique perto de uma ponta → prolonga essa ponta até a *primeira* entidade (linha ou círculo) na direção.
2. Se nada for atingido, a linha fica pendente (destacada); clique em outra linha → ambas vão até o cruzamento das suas retas infinitas (canto).

**`IGeometryEngine`** ([IGeometryEngine.cs](AvaloniaCadEditor.Domain/Interfaces/IGeometryEngine.cs)) — adicionar:
- `Entity? ExtendEntity(Entity entity, IEnumerable<Entity> boundaries, Point3D clickPoint)`
- `Point3D? GetApparentIntersection(Entity a, Entity b)`

**`MathGeometryEngine`**:
- `ExtendEntity`: ponta mais próxima do clique; raio = ponta − outra ponta. Raio×segmento (`t > 1e-6`, `u ∈ [0,1]` c/ eps) e raio×círculo (quadrática). Menor `t` vence; retorna nova `LineEntity` com `Plane` da original. `t > 1e-6` ignora segmentos já conectados na ponta.
- `GetApparentIntersection`: extrair de `IntersectLines` (l.142) um helper `IntersectInfiniteLines` sem o teste `[0,1]`; `IntersectLines` passa a usá-lo.
- `DummyGeometryEngine`: stubs `null`.

**`ExtendCommand.cs`** (novo): estado `_pendingLine`, `_preview`.
- `OnMouseDown`: `HitTest(..., 5.0)`; sem pendente → `ExtendEntity`, substitui (Remove/Add) ou vira pendente (`IsSelected = true`). Com pendente e outra linha → `GetApparentIntersection`, move a ponta mais próxima de cada linha até `p`. Vazio/mesma linha → cancela.
- `OnMouseMove`: preview do trecho de extensão (renderizado genericamente via `GetPreviewEntities`).
- `GetMeasurementText`: `"Selecione a 2ª linha"` quando pendente.

## 2. Polígono (G)
**`DrawPolygonCommand.cs`** (novo; estrutura igual ao `DrawCircleCommand`): estados `WaitingForCenter` → `WaitingForRadius`, `_sides = 6`.
- 1º clique: centro. Mouse define raio **e rotação** (primeiro vértice aponta para o cursor) — polígono inscrito.
- 2º clique: cria N `LineEntity` fechadas (`Plane = sketch.Plane`), volta a `WaitingForCenter`.
- `OnValueEntered`: em `WaitingForCenter` → define nº de lados (clamp 3..64, inteiro); em `WaitingForRadius` → raio exato, mantendo a rotação atual do mouse.
- Preview: as N linhas. `GetMeasurementText`: `"Lados: 6 (digite p/ mudar)"` / `"Lados: 6  Raio: 12.50"`.
- Geometria dos vértices num helper estático `RegularPolygon.Vertices(center, radius, sides, angle)` para ser testável.

## 3. Offset em regiões (O)
**`IGeometryEngine`** — adicionar `IEnumerable<Entity> OffsetProfile(IProfile profile, double distance)` (distância > 0 = para fora, < 0 = para dentro; vazio se inválido).

**`MathGeometryEngine.OffsetProfile`**:
- `CircleProfile` → `CircleEntity(center, r + d)` (vazio se `r + d <= 0`).
- Demais (Polygon/Rectangle): vértices locais = `profile.GetVertices()` → `profile.Plane.ToLocalSpace` (mesmo padrão do `ExtrudeCommand` l.135-151). Orientação pela área com sinal; cada aresta deslocada pela normal externa × d; novo vértice = interseção das arestas deslocadas adjacentes (reusa `IntersectInfiniteLines`; paralelas → ponto deslocado). Rejeita resultado se a área com sinal inverter/zerar (offset interno maior que a região). Retorna `LineEntity`s fechadas com `Plane` do perfil.
- `FaceProfile` (faces 3D) → vazio.

**`OffsetCommand.cs`** (novo): estados `SelectingRegion` → `DefiningDistance`.
- Seleção: entre `sketch.Profiles` visíveis e não-`FaceProfile`, o de **menor área** que `ContainsPoint(point)` (mais preciso que o "centro mais próximo" do Extrude).
- Mouse: distância = distância do cursor à borda da região; sinal = fora (+) / dentro (−). Preview = `OffsetProfile(...)`.
- Clique confirma; `OnValueEntered` usa o valor digitado com o sinal do lado atual do cursor. Adiciona as entidades ao sketch (a original permanece) e volta a `SelectingRegion`.
- `GetMeasurementText`: `"Clique numa região"` / `"Offset: 5.00 (fora)"`.

Limitação conhecida: um offset interno gera um "anel", mas o reconhecimento de perfis atual não modela furos — a região interna nova é reconhecida normalmente; o anel em si não vira perfil.

---

## 4. UI / wiring (para as três)
- [CadViewModel.cs](AvaloniaCadEditor.UI/ViewModels/CadViewModel.cs):
  - `SelectExtendToolCommand`, `SelectPolygonToolCommand`, `SelectOffsetToolCommand` junto de `SelectTrimToolCommand` (l.278 / l.421), com `CurrentMode = EditorMode.SketchEdit`.
  - Incluir as três na checagem de `EnterSketchMode` (l.629) e no `RefreshProfiles()` pós-clique (l.777).
  - Atalhos no switch (l.866): `X` Extend, `G` Polígono, `O` Offset (E/P já são Extrude).
- [Strings.resx](AvaloniaCadEditor.UI/Resources/Strings.resx) + `Strings.cs`: `ExtendTool "Extend (X)"`, `PolygonTool "Polygon (G)"`, `OffsetTool "Offset (O)"`.
- [MainWindow.axaml](AvaloniaCadEditor.UI/Views/MainWindow.axaml) l.135-137: `Polygon` após `Circle`; `Extend` e `Offset` após `Trim`.

## 5. Testes (`AvaloniaCadEditor.Tests`)
- `BehaviorTests/ExtendBehaviorTests.cs` (Moq, estilo `TrimBehaviorTests`): extensão simples; fallback de canto.
- `BehaviorTests/PolygonBehaviorTests.cs`: 2 cliques geram 6 linhas fechadas; digitar `4` antes muda para quadrado; raio digitado.
- `BehaviorTests/OffsetBehaviorTests.cs`: com `MathGeometryEngine` real — quadrado 10×10 offset +2 → quadrado 14×14; −2 → 6×6; −6 → nada; círculo r=5 ±2.
- Testes de `MathGeometryEngine`: `ExtendEntity` (ponta correta, fronteira mais próxima, círculo, sem alvo → null), `GetApparentIntersection` (L aberto, paralelas → null).

## Verificação
1. `dotnet build` + `dotnet test`.
2. App: entrar no sketch.
   - **X**: linha horizontal + vertical sem se tocar → hover mostra preview, clique estende. L aberto → clique numa, depois na outra → canto.
   - **G**: digitar `5` + Enter, clicar centro, mover (gira/escala), clicar → pentágono; vira região (extrusável).
   - **O**: clicar dentro de um retângulo, mover para fora/dentro → preview; digitar `3` + Enter → contorno criado.
3. Conferir aba Code atualizada e Ctrl+Z desfazendo cada operação.
