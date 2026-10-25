using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
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
    /// <para>
    /// <paramref name="cancellation"/> é conferido antes de cada aresta: o caminho por malha faz uma
    /// booleana por aresta e pode levar minutos. Cancelado, os cortes intermediários são descartados e
    /// sai <see cref="OperationCanceledException"/>.
    /// </para>
    /// <para>
    /// <paramref name="warn"/> recebe o motivo quando o sólido sai sem parte do que foi pedido (ex.: arestas
    /// demais para cortar uma a uma), para o kernel levar a <c>BuildProblems</c>.
    /// </para>
    /// </summary>
    public static OcctShape Build(FilletFeature fillet, OcctShape baseShape, TessellationSettings settings, CancellationToken cancellation = default, Action<string>? warn = null)
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
                // Quina, para o chanfro, é o que dobra mais que uma faceta de superfície curva (o mesmo
                // limite do ArcEdgeFinder), não o CreaseAngle do desenho das arestas: com os 30° dele,
                // a quina de 150° de um prisma (30° entre as normais) caía como suave e ficava sem chanfro.
                double maxStep = ArcEdgeFinder.MaxStepFor(settings.ProfileSegments);
                var creaseAngle = (float)Math.Min(settings.CreaseAngle, maxStep * 180 / Math.PI);
                var classification = EdgeDetector.ClassifyEdges(mesh.Positions, mesh.Indices, creaseAngle);
                var edgeConvex = classification.FeatureConvex;

                // A face alvo é o PLANO (normal + ponto) quando a referência traz um ponto: só arestas
                // sobre ele são cortadas. Só a normal casaria também faces paralelas — o fundo de um
                // rebaixo, de um vão — que não fazem parte da face selecionada.
                List<(Vector3D Normal, WorkPlane? Plane)> targetFaces = fillet.ApplyToAllEdges
                    ? new List<(Vector3D Normal, WorkPlane? Plane)>()
                    : fillet.Faces.Select(f => (f.NormalHint, f.PointOnFace.HasValue ? f.GetWorkPlane() : (WorkPlane?)null)).ToList();
                bool OnTargetFace(Vector3D n1, Vector3D n2, params Point3D[] points) =>
                    targetFaces.Count == 0 || targetFaces.Any(t =>
                        (t.Normal.DotProduct(n1) > 0.99 || t.Normal.DotProduct(n2) > 0.99) &&
                        (t.Plane == null || points.All(p => t.Plane.Contains(p))));

                // Arcos (boca de furo, ressalto, canto arredondado) saem da lista de retas: cada um vira um
                // anel só, em vez de um prisma por segmento da tesselação.
                var arcs = ArcEdgeFinder.Find(mesh.Positions, classification, maxStep);
                var arcEdges = new HashSet<int>(arcs.SelectMany(a => a.EdgeIndices));
                var ringArcs = arcs
                    .Where(a => !(fillet.SkipHoleEdges && a.IsHole) && OnTargetFace(a.Axis, a.Axis, a.Center))
                    .ToList();

                var candidates = new List<(Point3D Start, Point3D End, Vector3D N1, Vector3D N2)>();
                for (int i = 0; i < classification.FeatureEdges.Length; i += 2)
                {
                    // O cutter deste fillet só remove material (chanfro/arredondamento de quina).
                    // Numa aresta côncava (ex.: o canto interno de um degrau) isso abriria um
                    // entalhe em vez de cortar a quina, então essas arestas ficam de fora.
                    if (!edgeConvex[i / 2] || arcEdges.Contains(i / 2)) continue;

                    uint idx1 = classification.FeatureEdges[i];
                    uint idx2 = classification.FeatureEdges[i + 1];
                    var start = new Point3D(mesh.Positions[idx1 * 3], mesh.Positions[idx1 * 3 + 1], mesh.Positions[idx1 * 3 + 2]);
                    var end = new Point3D(mesh.Positions[idx2 * 3], mesh.Positions[idx2 * 3 + 1], mesh.Positions[idx2 * 3 + 2]);

                    var fn1 = classification.FeatureNormals1[i / 2];
                    var fn2 = classification.FeatureNormals2[i / 2];
                    var n1 = new Vector3D(fn1.X, fn1.Y, fn1.Z);
                    var n2 = new Vector3D(fn2.X, fn2.Y, fn2.Z);

                    // Sem quina para cortar: emenda entre faces coplanares (ex.: o topo de dois operandos
                    // de uma união na mesma altura) ou normal indefinida. Com n1 ≈ n2 os pontos do perfil nas
                    // duas faces coincidem, o OCCT recusa a aresta ("edge creation failed") e,
                    // sem esta guarda, a exceção derrubava o sólido inteiro.
                    if (n1.Length < 0.5 || n2.Length < 0.5 || n1.Normalize().DotProduct(n2.Normalize()) > 0.99) continue;

                    if (!OnTargetFace(n1, n2, start, end)) continue;

                    if (start.DistanceTo(end) < 1e-5) continue;
                    candidates.Add((start, end, n1, n2));
                }

                // Um prisma por aresta custa uma booleana no sólido inteiro (mais a conferência de volume).
                // Acima do teto, o que sobrou sem forma reconhecida (furo inclinado, boca numa face curva)
                // levaria minutos: o sólido sai sem o chanfro e o motivo vai para a barra de status.
                var straightEdges = MergeCollinearEdges(candidates);
                if (straightEdges.Count > MaxEdgeCuts)
                {
                    warn?.Invoke(
                        $"chanfro/arredondamento com {straightEdges.Count} arestas sem forma reconhecida (o limite é {MaxEdgeCuts}) não foi aplicado. " +
                        "Aplique-o antes dos furos, ou use SkipHoleEdges = true se forem furos de parafuso.");
                    return currentShape;
                }

                foreach (var (start, end, n1, n2) in straightEdges)
                {
                    CancelIfRequested(cancellation, currentShape, isIntermediate);
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

                // Os anéis depois das retas: num canto arredondado, a quina do arco encontra a reta vizinha já chanfrada.
                CancelIfRequested(cancellation, currentShape, isIntermediate);
                CutArcs(ref currentShape, ref isIntermediate, ref currentBox, ref currentVolume, ringArcs, r, fillet.Mode, segments, settings, cancellation);
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
                CancelIfRequested(cancellation, currentShape, isIntermediate);
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

    /// <summary>Cancelado: descarta o corte intermediário (o sólido base é do cache e fica) e lança.</summary>
    private static void CancelIfRequested(CancellationToken cancellation, OcctShape currentShape, bool isIntermediate)
    {
        if (!cancellation.IsCancellationRequested) return;
        if (isIntermediate) currentShape.Dispose();
        cancellation.ThrowIfCancellationRequested();
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

        // Perfil no plano perpendicular à aresta, com a quina na origem. t1 e t2 correm DENTRO de cada
        // face, para longe da aresta: o chanfro vai do ponto a d de distância numa face ao ponto a d na
        // outra. Numa quina de 90° elas são -n2 e -n1, exatamente; fora dela, -n2 e -n1 punham os pontos
        // dentro do material (quina de 60° ou 150°), o cortador abria um entalhe e o corte era recusado.
        // Na quina reta fica o -n2/-n1: o produto vetorial carregaria o ruído das normais da malha, e há
        // trechos (arco não reconhecido de um furo) que o OCCT só corta com o cortador de sempre.
        Vector3D t1, t2;
        if (Math.Abs(n1.Normalize().DotProduct(n2.Normalize())) < 1e-3)
        {
            t1 = n2 * -1;
            t2 = n1 * -1;
        }
        else
        {
            n1 = n1.Normalize();
            n2 = n2.Normalize();
            t1 = dEdge.CrossProduct(n1).Normalize();
            if (t1.DotProduct(n2) > 0) t1 = t1 * -1;
            t2 = dEdge.CrossProduct(n2).Normalize();
            if (t2.DotProduct(n1) > 0) t2 = t2 * -1;
        }

        // Ângulo entre as normais: 90° numa quina reta. O arco de raio r tangente às duas faces as toca a
        // r / tan(ângulo interno / 2) da quina; o chanfro corta a r em cada face.
        double alpha = Math.Acos(Math.Clamp(n1.DotProduct(n2), -1, 1));
        double d = mode == FilletMode.Round ? r * Math.Tan(alpha / 2) : r;

        var profile = new List<Vector3D> { (n1 + n2) * (0.1 * r), t2 * d + n2 * (0.1 * r), t2 * d };
        if (mode == FilletMode.Round)
        {
            // Centro do arco a r das duas faces; o raio gira de n2 (toca a face 2) para n1 (toca a face 1).
            var center = t1 * d - n1 * r;
            for (int k = 1; k < segments; k++)
            {
                double s = (double)k / segments;
                var radial = (n2 * Math.Sin((1 - s) * alpha) + n1 * Math.Sin(s * alpha)) * (1 / Math.Sin(alpha));
                profile.Add(center + radial * r);
            }
        }
        profile.Add(t1 * d);
        profile.Add(t1 * d + n1 * (0.1 * r));

        // Área pela fórmula do laço, no plano do perfil (perpendicular à aresta).
        var doubleArea = new Vector3D(0, 0, 0);
        for (int k = 0; k < profile.Count; k++)
            doubleArea += profile[k].CrossProduct(profile[(k + 1) % profile.Count]);

        try
        {
            var edges = new List<OcctEdge>();
            for (int k = 0; k < profile.Count; k++)
            {
                var p1 = extStart + profile[k];
                var p2 = extStart + profile[(k + 1) % profile.Count];
                edges.Add(new OcctEdge(
                    new OcctPointCoordinates(p1.X, p1.Y, p1.Z),
                    new OcctPointCoordinates(p2.X, p2.Y, p2.Z)
                ));
            }

            var wire = new OcctWire(edges.ToArray());
            var face = new OcctFace(wire);
            var cutter = face.Extrude(new OcctVector3d(dEdge.X * extLen, dEdge.Y * extLen, dEdge.Z * extLen));
            double volume = doubleArea.Length / 2 * extLen;
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
            // Quanto a caixa avançou em cada um dos 6 lados. A caixa do OCCT soma a tolerância do sólido, e uma
            // booleana com superfície curva (o cone do anel de um arco cruzando as facetas da parede) sobe essa
            // tolerância: a caixa cresce IGUAL nos 6 lados (0,036 mm num furo de raio 3) sem o sólido crescer. Essa
            // parte comum (a mediana) é descontada; o que denuncia a booleana corrompida é crescer de um lado só.
            const double growthTolerance = 1e-3;
            var growth = new[]
            {
                before.MinX - after.MinX, after.MaxX - before.MaxX,
                before.MinY - after.MinY, after.MaxY - before.MaxY,
                before.MinZ - after.MinZ, after.MaxZ - before.MaxZ,
            };
            var sorted = growth.OrderBy(g => g).ToArray();
            double toleranceGap = Math.Max(0, (sorted[2] + sorted[3]) / 2);
            bool grew = growth.Any(g => g - toleranceGap > growthTolerance);
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

    /// <summary>
    /// Acima disto, as arestas retas que sobram depois de tirar os arcos não são cortadas uma a uma: cada
    /// corte é uma booleana no sólido inteiro, e com centenas delas a construção levava minutos.
    /// </summary>
    internal const int MaxEdgeCuts = 200;

    /// <summary>
    /// Corta os arcos com anéis de revolução. Primeiro todos juntos (uma booleana só, conferida pelo volume
    /// somado dos anéis); se o OCCT errar o lote, um por vez, e o anel que falhar sozinho fica sem chanfro.
    /// </summary>
    private static void CutArcs(ref OcctShape currentShape, ref bool isIntermediate, ref OcctBoundingBox? currentBox, ref double? currentVolume,
        IReadOnlyList<CircularEdge> arcs, double r, FilletMode mode, int segments, TessellationSettings settings, CancellationToken cancellation)
    {
        var cutters = new List<(OcctShape Shape, double Volume)>();
        try
        {
            foreach (var arc in arcs)
            {
                if (CreateArcCutter(arc, r, mode, segments) is { } cutter) cutters.Add(cutter);
            }
            if (cutters.Count == 0) return;

            CutBatch(ref currentShape, ref isIntermediate, ref currentBox, ref currentVolume, cutters, r, settings, cancellation);
        }
        finally
        {
            foreach (var (shape, _) in cutters) shape.Dispose();
        }
    }

    private static void CutBatch(ref OcctShape currentShape, ref bool isIntermediate, ref OcctBoundingBox? currentBox, ref double? currentVolume,
        List<(OcctShape Shape, double Volume)> cutters, double r, TessellationSettings settings, CancellationToken cancellation)
    {
        CancelIfRequested(cancellation, currentShape, isIntermediate);
        OcctShape? tool = cutters.Count == 1 ? cutters[0].Shape : FuseAll(cutters.Select(c => c.Shape).ToList());
        OcctShape? cut = tool == null ? null : TryCutKeepingSolid(currentShape, ref currentBox, ref currentVolume, tool, cutters.Sum(c => c.Volume), r, settings);
        if (cutters.Count > 1) tool?.Dispose();
        if (cut != null)
        {
            if (isIntermediate) currentShape.Dispose();
            currentShape = cut;
            isIntermediate = true;
            return;
        }
        if (cutters.Count == 1) return;
        int half = cutters.Count / 2;
        CutBatch(ref currentShape, ref isIntermediate, ref currentBox, ref currentVolume, cutters.GetRange(0, half), r, settings, cancellation);
        CutBatch(ref currentShape, ref isIntermediate, ref currentBox, ref currentVolume, cutters.GetRange(half, cutters.Count - half), r, settings, cancellation);
    }

    /// <summary>União dos cortadores (sem descartar os de entrada), ou null se o OCCT recusar.</summary>
    private static OcctShape? FuseAll(List<OcctShape> shapes)
    {
        try
        {
            var current = shapes[0];
            for (int i = 1; i < shapes.Count; i++)
            {
                var next = current.Fuse(shapes[i]);
                if (i > 1) current.Dispose();
                if (next == null || next.IsNull) return null;
                current = next;
            }
            return current;
        }
        catch (OcctException)
        {
            return null;
        }
    }

    /// <summary>
    /// Anel (ou setor de anel) que chanfra/arredonda o arco: o perfil de <see cref="RingProfile"/> revolvido
    /// em torno do eixo do arco, a partir do plano da face. Null se o perfil degenera (ressalto com raio de
    /// chanfro maior que o dele) ou o OCCT recusa.
    /// </summary>
    private static (OcctShape Shape, double Volume)? CreateArcCutter(CircularEdge arc, double r, FilletMode mode, int segments)
    {
        if (RingProfile(arc.Radius, r, arc.IsHole, mode == FilletMode.Round, segments) is not { } profile) return null;
        try
        {
            var shape = OcctProfileGeometry.RevolveProfile(profile, arc.Center, arc.Axis, arc.StartDirection, arc.SweepRadians);
            return shape == null || shape.IsNull ? null : (shape, RevolvedVolume(profile, arc.SweepRadians));
        }
        catch (OcctException)
        {
            return null;
        }
    }

    /// <summary>
    /// Perfil (r = distância ao eixo, z = altura) do cortador de um arco de raio <paramref name="R"/>, com
    /// z = 0 no plano da face e z positivo para fora do material. Sobra de 0,1 do raio do chanfro para o
    /// lado do vazio, como os prismas das retas.
    /// <list type="bullet">
    /// <item><b>furo</b> (material fora do círculo): de <c>R</c> a <c>R + r</c> no plano, descendo <c>r</c>
    /// pela parede. Nunca chega ao eixo, então serve para qualquer raio de furo;</item>
    /// <item><b>ressalto</b> (material dentro): de <c>R − r</c> a <c>R</c>. Com <c>r ≥ R</c> o chanfro
    /// passaria do eixo: null.</item>
    /// </list>
    /// </summary>
    private static List<(double r, double z)>? RingProfile(double R, double r, bool isHole, bool isRound, int segments)
    {
        double o = 0.1 * r;
        var points = new List<(double r, double z)>();
        if (isHole)
        {
            double inner = Math.Max(R - o, R * 0.5);
            points.Add((inner, o));
            points.Add((R + r, o));
            points.Add((R + r, 0));
            if (isRound)
            {
                // Centro do arredondamento em (R + r, −r), dentro do material: de (R + r, 0) a (R, −r).
                for (int k = 1; k < segments; k++)
                {
                    double theta = Math.PI / 2 + (Math.PI / 2) * k / segments;
                    points.Add((R + r + r * Math.Cos(theta), -r + r * Math.Sin(theta)));
                }
            }
            points.Add((R, -r));
            points.Add((inner, -r));
        }
        else
        {
            if (r >= R) return null;
            points.Add((R - r, o));
            points.Add((R + o, o));
            points.Add((R + o, -r));
            points.Add((R, -r));
            if (isRound)
            {
                // Centro em (R − r, −r): de (R, −r) a (R − r, 0).
                for (int k = 1; k < segments; k++)
                {
                    double theta = (Math.PI / 2) * k / segments;
                    points.Add((R - r + r * Math.Cos(theta), -r + r * Math.Sin(theta)));
                }
            }
            points.Add((R - r, 0));
        }
        return points;
    }

    /// <summary>Volume do perfil revolvido <paramref name="angle"/> radianos (Pappus: área × raio do centroide × ângulo).</summary>
    private static double RevolvedVolume(IReadOnlyList<(double r, double z)> profile, double angle)
    {
        double area2 = 0, centroidR6 = 0;
        for (int k = 0; k < profile.Count; k++)
        {
            var (r1, z1) = profile[k];
            var (r2, z2) = profile[(k + 1) % profile.Count];
            double cross = r1 * z2 - r2 * z1;
            area2 += cross;
            centroidR6 += (r1 + r2) * cross;
        }
        if (Math.Abs(area2) < 1e-12) return 0;
        double area = Math.Abs(area2) / 2;
        double centroidR = centroidR6 / (3 * area2);
        return area * Math.Abs(centroidR) * angle;
    }

    private static OcctShape CreateCylinderCutter(CylinderFeature cyl, double filletRadius, bool isTop, bool isRound, TessellationSettings settings)
    {
        int segments = settings.ProfileSegments > 0 ? settings.ProfileSegments / 4 : 8;
        if (segments < 2) segments = 2;
        if (RingProfile(cyl.Radius, filletRadius, isHole: false, isRound, segments) is not { } profile) return null;

        // O topo olha para a direção do cilindro e a base para o lado oposto; o perfil é do plano para fora.
        var dir = cyl.GlobalDirection;
        var origin = isTop ? cyl.GlobalBaseCenter + dir * cyl.Height : cyl.GlobalBaseCenter;
        return OcctProfileGeometry.RevolveProfile(profile, origin, isTop ? dir : dir * -1);
    }
}
