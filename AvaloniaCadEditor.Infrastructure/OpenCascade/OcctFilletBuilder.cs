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

                    var vEdge = new Vector3D(end.X - start.X, end.Y - start.Y, end.Z - start.Z);
                    double len = Math.Sqrt(vEdge.X * vEdge.X + vEdge.Y * vEdge.Y + vEdge.Z * vEdge.Z);
                    if (len < 1e-5) continue;
                    var dEdge = new Vector3D(vEdge.X / len, vEdge.Y / len, vEdge.Z / len);
                    var extStart = new Point3D(start.X - dEdge.X * 0.1 * r, start.Y - dEdge.Y * 0.1 * r, start.Z - dEdge.Z * 0.1 * r);
                    double extLen = len + 0.2 * r;

                    var profilePoints = new List<Point3D>();
                    profilePoints.Add(Get3DPoint(extStart, n1, n2, 0.1 * r, 0.1 * r));
                    profilePoints.Add(Get3DPoint(extStart, n1, n2, -r, 0.1 * r));
                    profilePoints.Add(Get3DPoint(extStart, n1, n2, -r, 0));

                    if (fillet.Mode == FilletMode.Round)
                    {
                        for (int k = 1; k < segments; k++)
                        {
                            double theta = (Math.PI / 2.0) * (1.0 - (double)k / segments);
                            double x = -r + r * Math.Cos(theta);
                            double y = -r + r * Math.Sin(theta);
                            profilePoints.Add(Get3DPoint(extStart, n1, n2, x, y));
                        }
                    }

                    profilePoints.Add(Get3DPoint(extStart, n1, n2, 0, -r));
                    profilePoints.Add(Get3DPoint(extStart, n1, n2, 0.1 * r, -r));

                    OcctShape cutter;
                    try
                    {
                        var edges = new List<OcctEdge>();
                        for (int k = 0; k < profilePoints.Count; k++)
                        {
                            var p1 = profilePoints[k];
                            var p2 = profilePoints[(k + 1) % profilePoints.Count];
                            edges.Add(new OcctEdge(
                                new OcctPointCoordinates(p1.X, p1.Y, p1.Z),
                                new OcctPointCoordinates(p2.X, p2.Y, p2.Z)
                            ));
                        }

                        var wire = new OcctWire(edges.ToArray());
                        var face = new OcctFace(wire);
                        cutter = face.Extrude(new OcctVector3d(dEdge.X * extLen, dEdge.Y * extLen, dEdge.Z * extLen));
                    }
                    catch (OcctException)
                    {
                        // Cortador degenerado numa aresta de malha: essa aresta fica sem chanfro, mas o
                        // sólido (e o chanfro das demais) continua — antes, a peça inteira sumia.
                        continue;
                    }

                    if (cutter != null && !cutter.IsNull)
                    {
                        if (TryCutKeepingSolid(currentShape, ref currentBox, cutter, r) is { } cutShape)
                        {
                            if (isIntermediate) currentShape.Dispose();
                            currentShape = cutShape;
                            isIntermediate = true;
                        }
                        cutter.Dispose();
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

                    var vEdge = new Vector3D(end.X - start.X, end.Y - start.Y, end.Z - start.Z);
                    double len = Math.Sqrt(vEdge.X * vEdge.X + vEdge.Y * vEdge.Y + vEdge.Z * vEdge.Z);
                    if (len < 1e-5) continue;
                    var dEdge = new Vector3D(vEdge.X / len, vEdge.Y / len, vEdge.Z / len);
                    var extStart = new Point3D(start.X - dEdge.X * 0.1 * r, start.Y - dEdge.Y * 0.1 * r, start.Z - dEdge.Z * 0.1 * r);
                    double extLen = len + 0.2 * r;

                    var profilePoints = new List<Point3D>();
                    profilePoints.Add(Get3DPoint(extStart, n1, n2, 0.1 * r, 0.1 * r));
                    profilePoints.Add(Get3DPoint(extStart, n1, n2, -r, 0.1 * r));
                    profilePoints.Add(Get3DPoint(extStart, n1, n2, -r, 0));

                    if (fillet.Mode == FilletMode.Round)
                    {
                        for (int i = 1; i < segments; i++)
                        {
                            double theta = (Math.PI / 2.0) * (1.0 - (double)i / segments);
                            double x = -r + r * Math.Cos(theta);
                            double y = -r + r * Math.Sin(theta);
                            profilePoints.Add(Get3DPoint(extStart, n1, n2, x, y));
                        }
                    }

                    profilePoints.Add(Get3DPoint(extStart, n1, n2, 0, -r));
                    profilePoints.Add(Get3DPoint(extStart, n1, n2, 0.1 * r, -r));

                    var edges = new List<OcctEdge>();
                    for (int i = 0; i < profilePoints.Count; i++)
                    {
                        var p1 = profilePoints[i];
                        var p2 = profilePoints[(i + 1) % profilePoints.Count];
                        edges.Add(new OcctEdge(
                            new OcctPointCoordinates(p1.X, p1.Y, p1.Z),
                            new OcctPointCoordinates(p2.X, p2.Y, p2.Z)
                        ));
                    }

                    var wire = new OcctWire(edges.ToArray());
                    var face = new OcctFace(wire);
                    var cutter = face.Extrude(new OcctVector3d(dEdge.X * extLen, dEdge.Y * extLen, dEdge.Z * extLen));

                    if (cutter != null && !cutter.IsNull)
                    {
                        if (TryCutKeepingSolid(currentShape, ref currentBox, cutter, r) is { } cutShape)
                        {
                            if (isIntermediate) currentShape.Dispose();
                            currentShape = cutShape;
                            isIntermediate = true;
                        }
                        cutter.Dispose();
                    }
                }
            }
        }

        return currentShape;
    }

    /// <summary>
    /// <paramref name="shape"/> menos <paramref name="cutter"/>, ou null se a booleana estragou o sólido.
    /// O cortador de uma aresta só tira a quina dela, então o resultado tem de continuar com volume e
    /// a caixa envolvente não pode encolher mais que o raio. O OCCT às vezes devolve uma forma "não
    /// nula" porém vazia — ex.: chanfro maior que o raio da boca de um furo, cortadores do anel se
    /// cruzando no eixo — e, aceita, ela apagava o sólido inteiro (e todos os cortes seguintes).
    /// Rejeitado, só aquela aresta fica sem chanfro.
    /// </summary>
    private static OcctShape TryCutKeepingSolid(OcctShape shape, ref OcctBoundingBox? shapeBox, OcctShape cutter, double radius)
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
            if (!shrankTooMuch)
            {
                shapeBox = after;
                return cut;
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
