using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AvaloniaCadEditor.Application.Interfaces;
using AvaloniaCadEditor.Domain.Features;
using AvaloniaCadEditor.Domain.Mesh;
using AvaloniaCadEditor.Domain.Profiles;
using AvaloniaCadEditor.Domain.ValueObjects;
using AvaloniaCadEditor.Infrastructure.Engines.MultiAxis;

namespace AvaloniaCadEditor.Infrastructure.Engines;

/// <summary>
/// Deduz a peça como um projetista modelaria: UM perfil base extrudado ao longo do eixo em que
/// ele se repete (aparece igual nas duas pontas da malha) e alguns cortes (furos/bolsões)
/// subtraídos dele. Produz árvores muito mais simples (1 base + N cortes, sem aninhamento) do
/// que tentar "varrer" cada eixo com múltiplas camadas independentes.
///
/// Ideia central (do usuário): o perfil base se repete em inúmeros pontos ao longo do eixo de
/// extrusão — em particular, aparece inteiro nas duas pontas do objeto — e as diferenças no meio
/// são resultado de cortes. Um corte nunca aparece como um "furo" na fatia do seu próprio eixo de
/// varredura (ver <see cref="MeshSlicer.BuildRegions"/>: a fatia de um prisma reto não tem furo
/// interno), então basta olhar os furos detectados ao varrer cada eixo — cada um já aparece
/// naturalmente no eixo real do corte que o produziu.
///
/// Passo 1 — perfil base, por eixo A:
///   1. Fatia perto das duas pontas da malha em A. Se o contorno externo bater (área e bbox
///      dentro da tolerância) nas duas pontas, esse é o candidato a perfil base nesse eixo.
///   2. Só é válido se, extrudado pelo comprimento inteiro, contém TODOS os vértices da malha
///      projetados no plano (U, V) do eixo — um corte só remove material, nunca adiciona.
///   3. Entre os eixos válidos, todos entram como candidatos (o <see cref="IShapeReconstructionService"/>
///      escolhe pela IoU real); a ordem de prioridade interna prefere o de menor volume (área ×
///      comprimento), ou seja, o superconjunto mais justo.
///
/// Passo 2 — cortes, por eixo B (os três eixos, incluindo A):
///   Fatia cada camada entre os níveis de tampa em B e coleta os furos de cada fatia. Furos com
///   área e centróide parecidos em camadas vizinhas são o MESMO corte — junta-os numa única
///   extrusão cobrindo a faixa contígua onde aparecem. Círculo (ajuste de mínimos quadrados) vira
///   <see cref="CylinderFeature"/>; o resto vira <see cref="ExtrudeFeature"/> poligonal.
///
/// Resultado: <c>BooleanFeature.CreateDifference(base, cortes...)</c>, uma árvore de um nível só.
///
/// Limitação conhecida: um corte que abre lateralmente (um rasgo que toca a borda do perfil, não
/// um furo fechado) não aparece como "furo" numa fatia e não é capturado por este algoritmo.
/// </summary>
public class BaseProfileCutsAlgorithm : IMultiCandidateReconstructionAlgorithm
{
    private const int MaxLevelsPerAxis = 64;
    private const int MinPointsForCircle = 12;

    public string Name => "Perfil Base + Cortes";

    public async Task<ReconstructionResult?> ReconstructAsync(TriangleMesh mesh, double toleranceMultiplier = 1.0)
    {
        var candidates = await ReconstructCandidatesAsync(mesh, toleranceMultiplier);
        return candidates.Count > 0 ? candidates[0] : null;
    }

    public async Task<IReadOnlyList<ReconstructionResult>> ReconstructCandidatesAsync(TriangleMesh mesh, double toleranceMultiplier = 1.0)
    {
        return await Task.Run(() => BuildCandidates(mesh, toleranceMultiplier));
    }

    private IReadOnlyList<ReconstructionResult> BuildCandidates(TriangleMesh mesh, double toleranceMultiplier)
    {
        if (mesh == null || mesh.VertexCount == 0) return Array.Empty<ReconstructionResult>();

        double tolerance = 0.05 * toleranceMultiplier;
        var allVertices = CollectVertices(mesh);

        // Cortes são calculados uma vez por eixo (independem de qual eixo acabou virando a base).
        var cutsByAxis = AxisFrame.All.ToDictionary(axis => axis, axis => FindCuts(mesh, axis, tolerance));

        var baseCandidates = new List<(AxisFrame Axis, List<(double U, double V)> Profile, double Volume)>();
        foreach (var axis in AxisFrame.All)
        {
            var profile = FindBaseProfile(mesh, axis, tolerance, allVertices);
            if (profile == null) continue;

            var (rangeMin, rangeMax) = MeshSlicer.GetAxisRange(mesh, axis);
            double length = rangeMax - rangeMin;
            double area = Math.Abs(PolygonArea(profile));
            baseCandidates.Add((axis, profile, area * length));
        }

        var results = new List<ReconstructionResult>();
        foreach (var (axis, profile, _) in baseCandidates.OrderBy(b => b.Volume))
        {
            var (rangeMin, rangeMax) = MeshSlicer.GetAxisRange(mesh, axis);
            double length = rangeMax - rangeMin;

            var baseProfile = ToPolygonProfile(profile, axis, rangeMin);
            IFeature baseFeature = new ExtrudeFeature(baseProfile, length, axis.Axis * length, $"Base ({axis.Name})");

            var allCuts = AxisFrame.All.SelectMany(cutAxis => cutsByAxis[cutAxis]).ToList();

            if (allCuts.Count == 0)
            {
                results.Add(new ReconstructionResult { RootFeature = baseFeature, OperationCount = 1, ErrorMargin = 0.001 });
                continue;
            }

            var combined = BooleanFeature.CreateDifference(
                $"Peça ({axis.Name} + {allCuts.Count} corte(s))", baseFeature, allCuts.ToArray());
            results.Add(new ReconstructionResult { RootFeature = combined, OperationCount = 1 + allCuts.Count, ErrorMargin = 0.001 });
        }

        return results;
    }

    /// <summary>
    /// Procura, para o eixo dado, um contorno externo que se repete nas duas pontas da malha
    /// (mesma área e bbox) e que — extrudado pelo comprimento inteiro — contém todos os vértices
    /// da malha. Esse é o perfil da extrusão base nesse eixo.
    /// </summary>
    private static List<(double U, double V)>? FindBaseProfile(
        TriangleMesh mesh, AxisFrame axis, double tolerance, List<Point3D> allVertices)
    {
        var (rangeMin, rangeMax) = MeshSlicer.GetAxisRange(mesh, axis);
        double length = rangeMax - rangeMin;
        if (length <= 1e-6) return null;

        // A tesselação pode ficar levemente irregular bem perto da borda física da peça (fica
        // mais provável depois de um boolean, mesmo em faces que o corte nem tocou), então tenta
        // algumas margens diferentes em vez de confiar numa só sonda por ponta.
        var deltaFractions = new[] { 0.02, 0.05, 0.1, 0.005, 0.2 };
        var startOuters = new List<List<(double U, double V)>>();
        var endOuters = new List<List<(double U, double V)>>();

        foreach (var fraction in deltaFractions)
        {
            double delta = Math.Max(tolerance, length * fraction);
            if (delta * 2 >= length) continue; // peça muito curta pra ter "duas pontas" distintas nesse eixo

            if (startOuters.Count == 0) startOuters = SliceOuters(mesh, axis, rangeMin + delta);
            if (endOuters.Count == 0) endOuters = SliceOuters(mesh, axis, rangeMax - delta);
            if (startOuters.Count > 0 && endOuters.Count > 0) break;
        }
        if (startOuters.Count == 0 || endOuters.Count == 0) return null;

        List<(double U, double V)>? matched = null;
        foreach (var s in startOuters)
        {
            foreach (var e in endOuters)
            {
                if (SameSignature(s, e, tolerance)) { matched = s; break; }
            }
            if (matched != null) break;
        }
        if (matched == null) return null;

        foreach (var v in allVertices)
        {
            var (u, vv, _) = axis.ToLocal(v);
            if (!MeshSlicer.ContainsPointWithTolerance(matched, (u, vv), tolerance * 5))
                return null;
        }

        return matched;
    }

    private static List<List<(double U, double V)>> SliceOuters(TriangleMesh mesh, AxisFrame axis, double w)
    {
        var loops = MeshSlicer.Slice(mesh, axis, w);
        if (loops.Count == 0) return new List<List<(double U, double V)>>();
        return MeshSlicer.BuildRegions(loops).Select(r => r.Outer).ToList();
    }

    private static bool SameSignature(List<(double U, double V)> a, List<(double U, double V)> b, double tolerance)
    {
        double areaA = Math.Abs(PolygonArea(a));
        double areaB = Math.Abs(PolygonArea(b));
        if (areaA < 1e-9 || areaB < 1e-9) return false;
        if (Math.Abs(areaA - areaB) / Math.Max(areaA, areaB) > 0.01) return false;

        var (aMinU, aMaxU, aMinV, aMaxV) = BoundingBox(a);
        var (bMinU, bMaxU, bMinV, bMaxV) = BoundingBox(b);
        double dimTolerance = Math.Max(tolerance * 5, Math.Max(aMaxU - aMinU, bMaxU - bMinU) * 0.01);

        if (Math.Abs((aMaxU - aMinU) - (bMaxU - bMinU)) > dimTolerance) return false;
        if (Math.Abs((aMaxV - aMinV) - (bMaxV - bMinV)) > dimTolerance) return false;
        return true;
    }

    private sealed class HoleThread
    {
        public double Lo;
        public double Hi;
        public List<(double U, double V)> Points = new();
        public double Area;
        public (double U, double V) Centroid;
    }

    /// <summary>
    /// Todos os cortes detectados ao varrer o eixo dado: agrupa furos com área/centróide
    /// parecidos entre camadas vizinhas numa única extrusão (ou cilindro) cobrindo a faixa
    /// contígua onde aparecem.
    /// </summary>
    private static List<IFeature> FindCuts(TriangleMesh mesh, AxisFrame axis, double tolerance)
    {
        var levels = MeshSlicer.FindCapLevels(mesh, axis, tolerance);
        if (levels.Count < 2 || levels.Count > MaxLevelsPerAxis) return new List<IFeature>();

        var (rangeMin, rangeMax) = MeshSlicer.GetAxisRange(mesh, axis);

        var active = new List<HoleThread>();
        var finished = new List<HoleThread>();

        for (int i = 0; i < levels.Count - 1; i++)
        {
            double lo = levels[i];
            double hi = levels[i + 1];
            if (hi - lo <= tolerance) continue; // camada degenerada

            double mid = (lo + hi) / 2.0;
            var loops = MeshSlicer.Slice(mesh, axis, mid);
            var holesThisLayer = loops.Count > 0
                ? MeshSlicer.BuildRegions(loops).SelectMany(r => r.Holes).ToList()
                : new List<List<(double U, double V)>>();

            var matchedThreads = new HashSet<HoleThread>();
            var usedHoleIndices = new HashSet<int>();

            for (int h = 0; h < holesThisLayer.Count; h++)
            {
                var hole = holesThisLayer[h];
                double area = Math.Abs(PolygonArea(hole));
                var centroid = Centroid(hole);

                HoleThread? best = null;
                double bestDistance = double.MaxValue;
                foreach (var thread in active)
                {
                    if (matchedThreads.Contains(thread)) continue;
                    if (area < 1e-9 || thread.Area < 1e-9) continue;
                    if (Math.Abs(thread.Area - area) / Math.Max(thread.Area, area) > 0.02) continue;

                    double distance = Distance(thread.Centroid, centroid);
                    if (distance > tolerance * 5) continue;
                    if (distance < bestDistance) { bestDistance = distance; best = thread; }
                }

                if (best != null)
                {
                    best.Hi = hi;
                    matchedThreads.Add(best);
                    usedHoleIndices.Add(h);
                }
            }

            var stillActive = new List<HoleThread>();
            foreach (var thread in active)
            {
                if (matchedThreads.Contains(thread)) stillActive.Add(thread);
                else finished.Add(thread);
            }
            active = stillActive;

            for (int h = 0; h < holesThisLayer.Count; h++)
            {
                if (usedHoleIndices.Contains(h)) continue;
                var hole = holesThisLayer[h];
                active.Add(new HoleThread
                {
                    Lo = lo,
                    Hi = hi,
                    Points = hole,
                    Area = Math.Abs(PolygonArea(hole)),
                    Centroid = Centroid(hole)
                });
            }
        }
        finished.AddRange(active);

        // Um furo genuíno é reto ao longo do eixo B inteiro: sua seção não muda dentro da faixa
        // [Lo,Hi] onde foi detectado. Uma superfície curva de OUTRO eixo, cortada de raspão por B
        // (ex.: um cilindro ao longo de X aparece como uma "fenda" larga no meio e estreita nas
        // pontas ao varrer por Y), só bate a assinatura no ponto médio de uma camada sem cap
        // levels próprios em B — reamostrar outros pontos dentro da própria faixa denuncia isso.
        var validated = finished.Where(t => IsConsistentAcrossRange(mesh, axis, t, tolerance)).ToList();

        var cutFeatures = new List<IFeature>();
        int n = 0;
        foreach (var thread in validated)
        {
            n++;
            double lo = thread.Lo;
            double hi = thread.Hi;

            // Estende até além da peça se o corte encosta numa das pontas do eixo — a mesma
            // margem que a extrusão da face inferior já usa para furos passantes.
            if (lo - rangeMin <= tolerance) lo -= 1.0;
            if (rangeMax - hi <= tolerance) hi += 1.0;
            double height = hi - lo;
            if (height <= 1e-6) continue;

            var fit = LoopFitting.FitCircle(thread.Points);
            // Um polígono regular (hexágono, octógono...) também tem todos os vértices
            // exatamente sobre um círculo, então o erro do ajuste de mínimos quadrados por si só
            // não distingue os dois casos. O que distingue é a densidade de pontos: a tesselação
            // de um cilindro real neste kernel usa dezenas de segmentos (ex.: ~32 para um furo
            // pequeno), enquanto um polígono modelado como feição tem poucos vértices.
            bool isCircle = fit.Radius > 1e-4 && fit.Error < fit.Radius * 0.05 && thread.Points.Count >= MinPointsForCircle;

            if (isCircle)
            {
                var center = axis.ToWorld(fit.CenterU, fit.CenterV, lo);
                cutFeatures.Add(new CylinderFeature(center, axis.Axis, fit.Radius, height, $"Corte cilíndrico {axis.Name} {n}"));
            }
            else
            {
                var profile = ToPolygonProfile(thread.Points, axis, lo);
                cutFeatures.Add(new ExtrudeFeature(profile, height, axis.Axis * height, $"Corte poligonal {axis.Name} {n}"));
            }
        }

        return cutFeatures;
    }

    private static bool IsConsistentAcrossRange(TriangleMesh mesh, AxisFrame axis, HoleThread thread, double tolerance)
    {
        double range = thread.Hi - thread.Lo;
        if (range <= 1e-9) return true;

        foreach (var fraction in SampleFractions)
        {
            double w = thread.Lo + range * fraction;
            var loops = MeshSlicer.Slice(mesh, axis, w);
            if (loops.Count == 0) return false;

            bool found = MeshSlicer.BuildRegions(loops).SelectMany(r => r.Holes).Any(hole =>
            {
                double area = Math.Abs(PolygonArea(hole));
                if (thread.Area < 1e-9 || area < 1e-9) return false;
                if (Math.Abs(thread.Area - area) / Math.Max(thread.Area, area) > 0.05) return false;
                return Distance(Centroid(hole), thread.Centroid) <= tolerance * 10;
            });

            if (!found) return false;
        }
        return true;
    }

    private static readonly double[] SampleFractions = { 0.1, 0.3, 0.5, 0.7, 0.9 };

    private static List<Point3D> CollectVertices(TriangleMesh mesh)
    {
        var vertices = new List<Point3D>(mesh.VertexCount);
        for (int i = 0; i < mesh.Positions.Length; i += 3)
        {
            vertices.Add(new Point3D(mesh.Positions[i], mesh.Positions[i + 1], mesh.Positions[i + 2]));
        }
        return vertices;
    }

    private static (double MinU, double MaxU, double MinV, double MaxV) BoundingBox(List<(double U, double V)> loop)
    {
        double minU = double.MaxValue, maxU = double.MinValue;
        double minV = double.MaxValue, maxV = double.MinValue;
        foreach (var p in loop)
        {
            if (p.U < minU) minU = p.U; if (p.U > maxU) maxU = p.U;
            if (p.V < minV) minV = p.V; if (p.V > maxV) maxV = p.V;
        }
        return (minU, maxU, minV, maxV);
    }

    private static double PolygonArea(List<(double U, double V)> loop)
    {
        double area = 0;
        for (int i = 0; i < loop.Count; i++)
        {
            var a = loop[i];
            var b = loop[(i + 1) % loop.Count];
            area += a.U * b.V - b.U * a.V;
        }
        return area * 0.5;
    }

    private static (double U, double V) Centroid(List<(double U, double V)> loop)
    {
        double u = 0, v = 0;
        foreach (var p in loop) { u += p.U; v += p.V; }
        return (u / loop.Count, v / loop.Count);
    }

    private static double Distance((double U, double V) a, (double U, double V) b)
    {
        double du = a.U - b.U, dv = a.V - b.V;
        return Math.Sqrt(du * du + dv * dv);
    }

    private static PolygonProfile ToPolygonProfile(List<(double U, double V)> loopUV, AxisFrame axis, double baseW)
    {
        var worldPoints = loopUV.Select(p => axis.ToWorld(p.U, p.V, baseW)).ToList();
        var simplified = LoopFitting.SimplifyLoop(worldPoints);
        if (simplified.Count < 3) simplified = worldPoints;
        return new PolygonProfile(simplified);
    }
}
