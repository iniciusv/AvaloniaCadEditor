# Sweep (extrusão ao longo de caminho) — retas + arcos

## Context
Hoje só criamos sólidos por extrusão reta (`ExtrudeFeature`/`PrismFeature`/`BoxFeature`/`CylinderFeature`, todos caindo em `BuildExtrude`). Queremos varrer um perfil ao longo de um caminho (tubos, alças, trilhos, molduras). O `OcctNet.Wrapper` 0.1.1 **não expõe `MakePipe`** — só `Edge(p1,p2)`, `Wire`, `Face`, `Extrude(vec)`, `Revolve(axis, ang)`, `Fuse/Cut/Common`, `Translate`. Então o sweep será composto: cada **reta** = `face.Extrude`, cada **arco** = `face.Revolve` parcial, tudo unido com `FuseBalanced`. Resultado é B-Rep exato para caminhos de retas+arcos. O app é code-first (cena em script C#), então a fase 1 entrega a API de script; UI interativa fica para a fase 2.

## Fase 1 — Domain
**Novo `AvaloniaCadEditor.Domain/ValueObjects/SweepPath.cs`** (imutável, fluente, amigável ao script):
- `SweepPath.Start(Point3D p)` → `.LineTo(Point3D end)` → `.ArcTo(Point3D through, Point3D end)` (arco por 3 pontos, qualquer plano 3D).
- `SweepPath.Polyline(IEnumerable<Point3D> pts, double bendRadius = 0)`: com `bendRadius > 0` insere arcos tangentes em cada canto (encurta as retas vizinhas; valida que o raio cabe).
- Segmentos internos: `record LinePathSegment(Start, End)`, `record ArcPathSegment(Start, End, Center, Axis, AngleRad)` (centro/eixo calculados do 3-pontos).
- `IReadOnlyList<SweepStation> ComputeStations()`: frame (origem, tangente T, eixos X/Y do perfil) no início de cada segmento. Transporte do frame: reta = translação; arco = rotação de Rodrigues em torno do `Axis` (exato, sem torção); entre segmentos não tangentes = rotação mínima de T_prev→T_new (canto vivo → marcado como `IsMiter`).
- `SweepPath Transform(Placement p)` para instâncias giradas (usa `Placement.Apply`/`ApplyVector`).
- `IsClosed` (fim ≈ início).

**Novo `AvaloniaCadEditor.Domain/Features/SweepFeature.cs`** (padrão de `PrismFeature`):
- `new SweepFeature(IEnumerable<Point3D> profile, SweepPath path, string? name = null)` — perfil 2D local (X,Y) centrado no caminho, plano ⟂ à tangente inicial.
- `static SweepFeature Tube(double radius, SweepPath path, string? name = null)` (perfil circular; polygonizado por `ProfileSegments` como o resto).
- `GetVertices()` override: anéis do perfil em cada estação (mundo) — mantém seleção/mover/girar funcionando.

## Fase 1 — Kernel OCCT (`AvaloniaCadEditor.Infrastructure/OpenCascade/OpenCascadeGeometryKernel.cs`)
- **Refatorar** o bloco repetido (Newell + inverter winding + remover duplicados + montar edges/wire/face) de `BuildExtrude` (≈585-649) e `CreateCylinderCutter` (≈285-322) em `private OcctFace MakePlanarFace(List<Point3D> verts, Vector3D preferredNormal)`; ambos passam a usá-lo.
- `BuildShape`: novo ramo `SweepFeature` → `BuildSweep(sweep, settings, transform)`.
- `BuildSweep`: aplica `transform` ao caminho; para cada estação posiciona o perfil no mundo e:
  - reta → `MakePlanarFace(..., T).Extrude(T * len)`;
  - arco → `MakePlanarFace(...).Revolve(new OcctAxis1d(center, axis), angle)` (orientar normal da face conforme o sentido de rotação, como faz o cutter);
  - canto vivo (`IsMiter`, `bendRadius=0`): estender os dois prismas vizinhos além da junta e `Cut` cada um por um meio-espaço (bloco grande extrudado a partir do plano bissetor) → junta em esquadria exata.
  - `FuseBalanced` de todas as peças; descartar intermediários (nunca shapes do cache).

## Fase 1 — Kernel de malha (`PureMathGeometryKernel.cs`)
- Ramo `SweepFeature`: discretiza arcos (por `ProfileSegments`), gera anéis com os frames de `ComputeStations`, costura quads + tampas (sem tampas se `IsClosed`), `EdgeDetector.ClassifyEdges` como em `ExtrudeProfile`.

## Fase 1 — API de script
- `CadScriptApiReference.cs`: adicionar `typeof(SweepFeature)` em `FeatureTypes` e `typeof(SweepPath)` em `SupportTypes` (o `.cad/api.md` é regenerado por `SceneProjectTemplate`).
- Exemplo curto em `CadScriptProtocol.cs` (prompt da IA) mostrando `SweepFeature.Tube(5, SweepPath.Polyline(pts, bendRadius: 15))`.

## Fase 2 (separada, depois) — UI
`SweepCommand : ICommandTool` no padrão de `ExtrudeCommand` (seleciona perfil, depois cadeia de `LineEntity` do sketch como caminho, raio de dobra digitado) gerando código via `FeatureCodeGenerator`. Arcos no sketch exigiriam um `ArcEntity` novo — fora do escopo inicial.

## Testes (`AvaloniaCadEditor.Tests/BehaviorTests/OpenCascadeKernelTests.cs` + novo `SweepPathTests.cs`)
- `SweepPath`: frames de reta/arco/polyline com dobra; raio de dobra grande demais → exceção; arco 3 pontos colineares → exceção.
- Kernel OCCT: sweep reto de quadrado ≡ bbox do `PrismFeature` equivalente; L com `bendRadius` → bbox esperado e malha não vazia; arco 90° de tubo → bbox de quarto de toro; L com canto vivo (miter) → bbox; sweep dentro de `BooleanFeature` (Subtract de um box); `TransformedFeature` girado de um sweep.
- `PureMathGeometryKernel`: malha não vazia, fechada, contagem de triângulos esperada.

## Verificação
1. `dotnet test AvaloniaCadEditor.Tests` (todos verdes, incluindo os existentes de extrude/fillet após o refactor de `MakePlanarFace`).
2. Rodar o app (skill `run`), numa cena escrever `AddFeature(SweepFeature.Tube(5, SweepPath.Polyline(new[]{ new Point3D(0,0,0), new Point3D(100,0,0), new Point3D(100,80,40) }, bendRadius: 20)));` e conferir visualmente o tubo, depois subtraí-lo de um `BoxFeature`.
