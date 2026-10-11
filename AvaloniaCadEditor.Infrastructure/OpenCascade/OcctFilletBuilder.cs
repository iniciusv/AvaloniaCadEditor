using System;
using System.Collections.Generic;
using System.Linq;
using AvaloniaCadEditor.Domain.Features;
using AvaloniaCadEditor.Domain.Mesh;
using AvaloniaCadEditor.Domain.Settings;
using AvaloniaCadEditor.Domain.ValueObjects;
using OcctNet.Wrapper;

namespace AvaloniaCadEditor.Infrastructure.OpenCascade;

/// <summary>
/// Aplica um <see cref="FilletFeature"/> a um sólido já construído, cortando a quina de cada
/// aresta escolhida (chanfro ou arredondamento).
/// <para>
/// Não constrói o sólido base nem conhece o cache: recebe a forma pronta de
/// <see cref="OpenCascadeGeometryKernel"/> e devolve a forma cortada. São três caminhos distintos,
/// e é por isso que isto não cabia junto com os construtores de primitivos:
/// </para>
/// <list type="bullet">
/// <item><b>cilindro</b>: corte exato em B-Rep, por um anel de revolução no topo e/ou na base
/// (<see cref="CreateCylinderCutter"/>);</item>
/// <item><b>caixa</b>: corte exato em B-Rep, usando a geometria de aresta que a própria
/// <c>BoxFeature</c> sabe informar;</item>
/// <item><b>qualquer outra feature</b> (em especial booleanas): caminho genérico baseado em
/// MALHA — tessela o sólido base, reclassifica as arestas com <see cref="EdgeDetector"/> e monta
/// um cortador por aresta convexa. É um algoritmo de malha, não de B-Rep, e por isso depende de
/// <see cref="OcctTessellator"/>.</item>
/// </list>
/// </summary>
internal static class OcctFilletBuilder
{
    /// <summary>
    /// Corta as arestas de <paramref name="baseShape"/> conforme <paramref name="fillet"/>.
    /// <para>
    /// Pode devolver <paramref name="baseShape"/> sem cópia (raio desprezível, ou nenhuma aresta
    /// elegível), então o chamador NÃO deve descartar o que entrou achando que saiu outra coisa —
    /// é o mesmo contrato que valia quando isto era <c>OpenCascadeGeometryKernel.BuildFillet</c>,
    /// onde a forma de entrada vinha do cache por Id.
    /// </para>
    /// </summary>
    public static OcctShape Build(FilletFeature fillet, OcctShape baseShape, TessellationSettings settings)
    {
        var currentShape = baseShape;
        if (currentShape == null || currentShape.IsNull) return null;

        if (fillet.BaseFeature is CylinderFeature cyl)
        {
            var rad = fillet.Radius;
            if (rad <= 0.001) return currentShape;
            
            bool doTop = fillet.ApplyToAllEdges || fillet.Faces.Any(f => f.FaceId == (int)FaceType.Top);
            bool doBottom = fillet.ApplyToAllEdges || fillet.Faces.Any(f => f.FaceId == (int)FaceType.Bottom);
            
            bool isRound = fillet.Mode == FilletMode.Round;
            var resultShape = currentShape;
            
            if (doTop)
            {
                var cutter = CreateCylinderCutter(cyl, rad, true, isRound, settings);
                if (cutter != null && !cutter.IsNull) resultShape = resultShape.Cut(cutter);
            }
            if (doBottom)
            {
                var cutter = CreateCylinderCutter(cyl, rad, false, isRound, settings);
                if (cutter != null && !cutter.IsNull) resultShape = resultShape.Cut(cutter);
            }
            
            return resultShape;
        }

        var allEdges = new HashSet<EdgeReference>();
        var r = fillet.Radius;
        if (r <= 0.001) return currentShape;
        int segments = settings.ProfileSegments > 0 ? settings.ProfileSegments / 4 : 8;
        if (segments < 2) segments = 2;
        bool isIntermediate = false;
        OcctBoundingBox? currentBox = null; // caixa de currentShape, para não recalcular a cada aresta
        double? currentVolume = null; // idem, volume

        // --- MESH-BASED GENERALIZED FILLET FOR ANY FEATURE (ESPECIALLY BOOLEANS) ---
        if (!(fillet.BaseFeature is BoxFeature))
        {
            var mesh = OcctTessellator.Tessellate(currentShape, settings);
            if (mesh != null && mesh.FeatureEdges.Length > 0)
            {
                // Recalcula a convexidade de cada feature edge (mesma entrada e mesma ordem de
                // EdgeDetector.ClassifyEdges usada dentro de OcctTessellator.Tessellate, então os índices
                // batem com mesh.FeatureEdges par a par).
                var (_, _, _, _, edgeConvex) = EdgeDetector.ClassifyEdges(mesh.Positions, mesh.Indices, settings.CreaseAngle);

                // A face alvo é o PLANO (normal + ponto) quando a referência traz um ponto: só arestas
                // sobre ele são cortadas. Só a normal casaria também faces paralelas — o fundo de um
                // rebaixo, de um vão — que não fazem parte da face selecionada.
                var targetFaces = fillet.ApplyToAllEdges
                    ? new List<(Vector3D Normal, WorkPlane? Plane)>()
                    : fillet.Faces.Select(f => (f.NormalHint, f.PointOnFace.HasValue ? f.GetWorkPlane() : (WorkPlane?)null)).ToList();
                var candidates = new List<(Point3D Start, Point3D End, Vector3D N1, Vector3D N2)>();
                for (int i = 0; i < mesh.FeatureEdges.Length; i += 2)
                {
                    // O cutter deste fillet só remove material (chanfro/arredondamento de quina).
                    // Numa aresta côncava (ex.: o canto interno de um degrau) isso abriria um
                    // entalhe em vez de cortar a quina, então essas arestas ficam de fora.
                    if (!edgeConvex[i / 2]) continue;

                    uint idx1 = mesh.FeatureEdges[i];
                    uint idx2 = mesh.FeatureEdges[i + 1];
                    var start = new Point3D(mesh.Positions[idx1 * 3], mesh.Positions[idx1 * 3 + 1], mesh.Positions[idx1 * 3 + 2]);
                    var end = new Point3D(mesh.Positions[idx2 * 3], mesh.Positions[idx2 * 3 + 1], mesh.Positions[idx2 * 3 + 2]);

                    var n1 = new Vector3D(mesh.FeatureNormals1[(i/2)*3], mesh.FeatureNormals1[(i/2)*3+1], mesh.FeatureNormals1[(i/2)*3+2]);
                    var n2 = new Vector3D(mesh.FeatureNormals2[(i/2)*3], mesh.FeatureNormals2[(i/2)*3+1], mesh.FeatureNormals2[(i/2)*3+2]);

                    // Sem quina para cortar: emenda entre faces coplanares (ex.: o topo de dois operandos
                    // de uma união na mesma altura) ou normal indefinida. Com n1 ≈ n2 os pontos (-r, 0) e
                    // (0, -r) do perfil coincidem, o OCCT recusa a aresta ("edge creation failed") e,
                    // sem esta guarda, a exceção derrubava o sólido inteiro.
                    if (n1.Length < 0.5 || n2.Length < 0.5 || n1.Normalize().DotProduct(n2.Normalize()) > 0.99) continue;

                    // Filter by selected faces if applicable
                    if (!fillet.ApplyToAllEdges && targetFaces.Count > 0)
                    {
                        bool match = false;
                        foreach (var (tn, plane) in targetFaces)
                        {
                            bool normalMatches = tn.DotProduct(n1) > 0.99 || tn.DotProduct(n2) > 0.99;
                            if (normalMatches && (plane == null || (plane.Contains(start) && plane.Contains(end))))
                            {
                                match = true;
                                break;
                            }
                        }
                        if (!match) continue;
                    }

                    if (start.DistanceTo(end) < 1e-5) continue;
                    candidates.Add((start, end, n1, n2));
                }

                foreach (var (start, end, n1, n2) in MergeCollinearEdges(candidates))
                {
                    // Um corte que o OCCT erra (forma vazia, ou só um caco do sólido) costuma acertar com
                    // o cortador um pouco mais curto ou mais longo: a falha vem de faces quase
                    // coincidentes com o chanfro já feito na aresta vizinha, e mudar a sobra nas pontas
                    // desfaz a coincidência.
                    foreach (var overhang in CutterOverhangs)
                    {
                        if (BuildEdgeCutter(start, end, n1, n2, r, overhang * r, fillet.Mode, segments) is not var (cutter, cutterVolume)) continue;

                        var cutShape = TryCutKeepingSolid(currentShape, ref currentBox, ref currentVolume, cutter, cutterVolume, r, settings);
                        cutter.Dispose();
                        if (cutShape == null) continue;

                        if (isIntermediate) currentShape.Dispose();
                        currentShape = cutShape;
                        isIntermediate = true;
                        break;
                    }
                }
            }
        }
        else
        {
            // --- BOX FEATURE EXACT FILLET ---
            if (fillet.ApplyToAllEdges)
            {
                if (fillet.BaseFeature is BoxFeature box)
                {
                    foreach (var eType in box.GetAllEdges()) allEdges.Add(new EdgeReference(box, eType));
                }
            }
            else
            {
                foreach (var edge in fillet.Edges) allEdges.Add(edge);
                foreach (var face in fillet.Faces)
                {
                    if (face.ParentFeature is BoxFeature box && Enum.IsDefined(typeof(FaceType), face.FaceId))
                    {
                        var fType = (FaceType)face.FaceId;
                        foreach (var eType in box.GetEdgesForFace(fType)) allEdges.Add(new EdgeReference(box, eType));
                    }
                }
            }

            foreach (var edgeRef in allEdges)
            {
                if (edgeRef.ParentFeature is BoxFeature box)
                {
                    var geom = box.GetEdgeGeometry(edgeRef.EdgeId);
                    var start = geom.Start;
                    var end = geom.End;
                    var n1 = geom.Normal1;
                    var n2 = geom.Normal2;

                    if (start.DistanceTo(end) < 1e-5) continue;
                    if (BuildEdgeCutter(start, end, n1, n2, r, 0.1 * r, fillet.Mode, segments) is not var (cutter, cutterVolume)) continue;

                    if (TryCutKeepingSolid(currentShape, ref currentBox, ref currentVolume, cutter, cutterVolume, r, settings) is { } cutShape)
                    {
                        if (isIntermediate) currentShape.Dispose();
                        currentShape = cutShape;
                        isIntermediate = true;
                    }
                    cutter.Dispose();
                }
            }
        }

        return currentShape;
    }

    /// <summary>
    /// Sobra do cortador além de cada ponta da aresta, em múltiplos do raio, na ordem em que é
    /// tentada. A primeira é a de sempre; as outras só entram se o corte com ela falhar.
    /// </summary>
    private static readonly double[] CutterOverhangs = { 0.1, 0.0, 0.3 };

    /// <summary>
    /// Prisma que corta a quina da aresta <paramref name="start"/>–<paramref name="end"/> (perfil de
    /// chanfro ou de arredondamento no plano de <paramref name="n1"/>/<paramref name="n2"/>, extrudado ao
    /// longo da aresta com <paramref name="overhang"/> de sobra em cada ponta) e o volume dele, ou null
    /// se o OCCT recusar o perfil.
    /// </summary>
    private static (OcctShape Shape, double Volume)? BuildEdgeCutter(Point3D start, Point3D end, Vector3D n1, Vector3D n2, double r, double overhang, FilletMode mode, int segments)
    {
        var vEdge = new Vector3D(end.X - start.X, end.Y - start.Y, end.Z - start.Z);
        double len = Math.Sqrt(vEdge.X * vEdge.X + vEdge.Y * vEdge.Y + vEdge.Z * vEdge.Z);
        var dEdge = new Vector3D(vEdge.X / len, vEdge.Y / len, vEdge.Z / len);
        var extStart = new Point3D(start.X - dEdge.X * overhang, start.Y - dEdge.Y * overhang, start.Z - dEdge.Z * overhang);
        double extLen = len + 2 * overhang;

        // Perfil em (x ao longo de n1, y ao longo de n2), com a quina na origem.
        var profile = new List<(double X, double Y)> { (0.1 * r, 0.1 * r), (-r, 0.1 * r), (-r, 0) };
        if (mode == FilletMode.Round)
        {
            for (int k = 1; k < segments; k++)
            {
                double theta = (Math.PI / 2.0) * (1.0 - (double)k / segments);
                profile.Add((-r + r * Math.Cos(theta), -r + r * Math.Sin(theta)));
            }
        }
        profile.Add((0, -r));
        profile.Add((0.1 * r, -r));

        // Área pela fórmula do laço, corrigida por |n1 × n2|: o perfil está na base (n1, n2), que só é
        // ortonormal numa quina de 90°. A extrusão corre ao longo da aresta, perpendicular às duas normais.
        double area = 0;
        for (int k = 0; k < profile.Count; k++)
        {
            var (x1, y1) = profile[k];
            var (x2, y2) = profile[(k + 1) % profile.Count];
            area += x1 * y2 - x2 * y1;
        }

        try
        {
            var edges = new List<OcctEdge>();
            for (int k = 0; k < profile.Count; k++)
            {
                var p1 = Get3DPoint(extStart, n1, n2, profile[k].X, profile[k].Y);
                var p2 = Get3DPoint(extStart, n1, n2, profile[(k + 1) % profile.Count].X, profile[(k + 1) % profile.Count].Y);
                edges.Add(new OcctEdge(
                    new OcctPointCoordinates(p1.X, p1.Y, p1.Z),
                    new OcctPointCoordinates(p2.X, p2.Y, p2.Z)
                ));
            }

            var wire = new OcctWire(edges.ToArray());
            var face = new OcctFace(wire);
            var cutter = face.Extrude(new OcctVector3d(dEdge.X * extLen, dEdge.Y * extLen, dEdge.Z * extLen));
            double volume = Math.Abs(area) / 2 * n1.CrossProduct(n2).Length * extLen;
            return cutter == null || cutter.IsNull ? null : (cutter, volume);
        }
        catch (OcctException)
        {
            // Cortador degenerado numa aresta de malha: essa aresta fica sem chanfro, mas o
            // sólido (e o chanfro das demais) continua — antes, a peça inteira sumia.
            return null;
        }
    }

    /// <summary>
    /// Junta trechos de malha que são a mesma quina reta: encostam numa ponta, são colineares e têm
    /// as mesmas duas faces. Acontece quando dois operandos de uma união têm faces coplanares (ex.:
    /// a lateral do corpo e a de um extra rente a ela) — a quina sai partida na emenda. Cortada em
    /// pedaços, o cortador do segundo trecho cai sobre o chanfro que o primeiro acabou de fazer, no
    /// MESMO plano, e o OCCT devolve uma forma vazia: esse trecho ficava sem chanfro.
    /// </summary>
    private static List<(Point3D Start, Point3D End, Vector3D N1, Vector3D N2)> MergeCollinearEdges(
        List<(Point3D Start, Point3D End, Vector3D N1, Vector3D N2)> edges)
    {
        const double pointTolerance = 1e-4;
        var result = new List<(Point3D Start, Point3D End, Vector3D N1, Vector3D N2)>(edges);

        static Vector3D Direction(Point3D a, Point3D b) => new Vector3D(b.X - a.X, b.Y - a.Y, b.Z - a.Z).Normalize();
        static bool SameFaces((Point3D, Point3D, Vector3D N1, Vector3D N2) a, (Point3D, Point3D, Vector3D N1, Vector3D N2) b) =>
            (a.N1.Normalize().DotProduct(b.N1.Normalize()) > 0.999 && a.N2.Normalize().DotProduct(b.N2.Normalize()) > 0.999) ||
            (a.N1.Normalize().DotProduct(b.N2.Normalize()) > 0.999 && a.N2.Normalize().DotProduct(b.N1.Normalize()) > 0.999);

        for (int i = 0; i < result.Count; i++)
        {
            for (int j = i + 1; j < result.Count; j++)
            {
                var a = result[i];
                var b = result[j];
                if (!SameFaces(a, b)) continue;
                if (Math.Abs(Direction(a.Start, a.End).DotProduct(Direction(b.Start, b.End))) < 0.99999) continue;

                // Ponta comum: as outras duas pontas são as do trecho juntado.
                (Point3D, Point3D)? span =
                    a.End.DistanceTo(b.Start) < pointTolerance ? (a.Start, b.End) :
                    a.End.DistanceTo(b.End) < pointTolerance ? (a.Start, b.Start) :
                    a.Start.DistanceTo(b.Start) < pointTolerance ? (a.End, b.End) :
                    a.Start.DistanceTo(b.End) < pointTolerance ? (a.End, b.Start) :
                    null;
                if (span is not { } s) continue;

                result[i] = (s.Item1, s.Item2, a.N1, a.N2);
                result.RemoveAt(j);
                j = i; // o trecho cresceu: um que já foi descartado pode encostar na ponta nova
            }
        }

        return result;
    }

    /// <summary>
    /// <paramref name="shape"/> menos <paramref name="cutter"/>, ou null se a booleana estragou o sólido.
    /// O cortador de uma aresta só tira a quina dela, então o resultado tem de continuar com volume e
    /// a caixa envolvente não pode encolher mais que o raio. O OCCT às vezes devolve uma forma "não
    /// nula" porém vazia — ex.: chanfro maior que o raio da boca de um furo, cortadores do anel se
    /// cruzando no eixo — e, aceita, ela apagava o sólido inteiro (e todos os cortes seguintes).
    /// Rejeitado, só aquela aresta fica sem chanfro.
    /// <para>
    /// Pelo mesmo motivo a caixa não pode CRESCER: um corte só tira material, e uma forma que sai
    /// maior (ex.: com 0,1 mm a mais em Z, a sobra do cortador acima do topo) é uma booleana
    /// corrompida. Aceita, ela não some na hora, mas todo corte seguinte sobre ela dá forma vazia — e
    /// o resto do chanfro (até arestas longe dali) ficava faltando.
    /// </para>
    /// <para>
    /// Nem toda booleana corrompida muda a caixa, então o teste final é o volume: um corte válido
    /// remove entre zero e o volume do cortador. A forma corrompida foge disso por mm³ inteiros
    /// (sai maior, ou perde mais do que o cortador tinha), enquanto o volume pela malha de um corte
    /// válido bate com a conta até a terceira casa.
    /// </para>
    /// </summary>
    private static OcctShape TryCutKeepingSolid(OcctShape shape, ref OcctBoundingBox? shapeBox, ref double? shapeVolume, OcctShape cutter, double cutterVolume, double radius, TessellationSettings settings)
    {
        OcctShape cut = null;
        try
        {
            cut = shape.Cut(cutter);
            if (cut == null || cut.IsNull) return null;

            var before = shapeBox ??= shape.BoundingBox;
            var after = cut.BoundingBox; // forma vazia: "Bnd_Box is void" (OcctException)
            double tolerance = radius * 1.5 + 1e-3;
            bool shrankTooMuch =
                after.SizeX < before.SizeX - 2 * tolerance ||
                after.SizeY < before.SizeY - 2 * tolerance ||
                after.SizeZ < before.SizeZ - 2 * tolerance;
            const double growthTolerance = 1e-3;
            bool grew =
                after.MinX < before.MinX - growthTolerance || after.MaxX > before.MaxX + growthTolerance ||
                after.MinY < before.MinY - growthTolerance || after.MaxY > before.MaxY + growthTolerance ||
                after.MinZ < before.MinZ - growthTolerance || after.MaxZ > before.MaxZ + growthTolerance;
            if (!shrankTooMuch && !grew)
            {
                double volumeBefore = shapeVolume ??= OcctTessellator.Volume(shape, settings.LinearDeflection, settings.AngularDeflection);
                double volumeAfter = OcctTessellator.Volume(cut, settings.LinearDeflection, settings.AngularDeflection);
                double removed = volumeBefore - volumeAfter;
                double volumeTolerance = volumeBefore * 1e-4 + cutterVolume * 0.05;
                if (volumeAfter > 0 && removed > -volumeTolerance && removed < cutterVolume + volumeTolerance)
                {
                    shapeBox = after;
                    shapeVolume = volumeAfter;
                    return cut;
                }
            }
        }
        catch (OcctException)
        {
        }

        cut?.Dispose();
        return null;
    }

    private static OcctShape CreateCylinderCutter(CylinderFeature cyl, double filletRadius, bool isTop, bool isRound, TessellationSettings settings)
    {
        var origin = cyl.GlobalBaseCenter;
        var dir = cyl.GlobalDirection;
        
        var points2D = new System.Collections.Generic.List<(double r, double z)>();
        double R = cyl.Radius;
        double H = cyl.Height;
        double r = filletRadius;
        
        if (isTop)
        {
            points2D.Add((R - r, H + 0.1));
            points2D.Add((R + 0.1, H + 0.1));
            points2D.Add((R + 0.1, H - r));
            
            if (isRound)
            {
                int segments = settings.ProfileSegments > 0 ? settings.ProfileSegments / 4 : 8;
                if (segments < 2) segments = 2;
                for (int i = 0; i <= segments; i++)
                {
                    double theta = (Math.PI / 2.0) * (1.0 - (double)i / segments);
                    double pr = (R - r) + r * Math.Cos(theta);
                    double pz = (H - r) + r * Math.Sin(theta);
                    points2D.Add((pr, pz));
                }
            }
            else
            {
                points2D.Add((R, H - r));
                points2D.Add((R - r, H));
            }
        }
        else
        {
            points2D.Add((R - r, -0.1));
            points2D.Add((R + 0.1, -0.1));
            points2D.Add((R + 0.1, r));
            
            if (isRound)
            {
                int segments = settings.ProfileSegments > 0 ? settings.ProfileSegments / 4 : 8;
                if (segments < 2) segments = 2;
                for (int i = 0; i <= segments; i++)
                {
                    double theta = (Math.PI / 2.0) * ((double)i / segments);
                    double pr = (R - r) + r * Math.Cos(theta);
                    double pz = r - r * Math.Sin(theta);
                    points2D.Add((pr, pz));
                }
            }
            else
            {
                points2D.Add((R, r));
                points2D.Add((R - r, 0));
            }
        }
        
        return OcctProfileGeometry.RevolveProfile(points2D, origin, dir);
    }

    private static Point3D Get3DPoint(Point3D origin, Vector3D n1, Vector3D n2, double x, double y)
    {
        return new Point3D(
            origin.X + x * n1.X + y * n2.X,
            origin.Y + x * n1.Y + y * n2.Y,
            origin.Z + x * n1.Z + y * n2.Z
        );
    }
}
