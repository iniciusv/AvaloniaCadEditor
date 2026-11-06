using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AvaloniaCadEditor.Application.Interfaces;
using AvaloniaCadEditor.Domain.Features;
using AvaloniaCadEditor.Domain.Mesh;
using AvaloniaCadEditor.Domain.Profiles;
using AvaloniaCadEditor.Infrastructure.Engines.MultiAxis;

namespace AvaloniaCadEditor.Infrastructure.Engines;

/// <summary>
/// Deduz a peça como um projetista modelaria: UM perfil base extrudado ao longo de um eixo e
/// alguns cortes (furos, rasgos, chanfros, recortes) subtraídos dele, cada um reto ao longo do
/// próprio eixo. Produz árvores simples (1 base + N cortes, sem aninhamento).
///
/// Passo 1 — base, por eixo A: a silhueta da malha projetada no plano (U, V) de A (a união de
///   todos os triângulos projetados), extrudada pelo comprimento inteiro em A. É o menor prisma
///   em A que contém a peça: um corte só remove material, então a peça cabe nele. Cada eixo com
///   silhueta de uma ilha só vira um candidato; o <see cref="IShapeReconstructionService"/> escolhe
///   pela IoU real e, no empate, pelo menor número de operações.
///
/// Passo 2 — cortes, por eixo B (os três, incluindo A): em cada camada entre os níveis de tampa de
///   B, o material removido é a seção do prisma-base menos a fatia real (<see cref="SectionRegions.RemovedRegions"/>).
///   Isso pega tanto o furo fechado quanto o recorte que abre para a borda (o vão de um U, um
///   rasgo, um chanfro). Regiões com área e centróide parecidos em camadas vizinhas são o MESMO
///   corte, que vira uma extrusão só cobrindo a faixa contígua. Um corte só vale se a seção não
///   muda dentro da faixa (reamostragem): a superfície de um corte de OUTRO eixo, fatiada de
///   raspão, muda de largura e é descartada. Dois trechos do mesmo corte separados por um vão sem
///   material (o furo que atravessa os dois braços de um U) viram uma ferramenta só. Círculo
///   (ajuste de mínimos quadrados) vira <see cref="CylinderFeature"/>; o resto, <see cref="ExtrudeFeature"/>
///   poligonal.
///
/// As fatias nunca caem no meio exato da camada: furos centrados na espessura de uma aba são
///   comuns, e fatiar exatamente pelo eixo de um furo de outro eixo deixa a seção degenerada.
///
/// Resultado: <c>BooleanFeature.CreateDifference(base, cortes...)</c>, uma árvore de um nível só.
/// </summary>
public class BaseProfileCutsAlgorithm : IMultiCandidateReconstructionAlgorithm
{
    private const int MaxLevelsPerAxis = 64;
    private const int MinPointsForCircle = 12;

    /// <summary>Posição da fatia principal de cada camada (fração da espessura), fora do meio exato.</summary>
    private const double PrimarySample = 0.5137;

    /// <summary>Posições da reamostragem que confirma que a seção do corte não muda dentro da faixa.</summary>
    private static readonly double[] SampleFractions = { 0.113, 0.307, PrimarySample, 0.709, 0.893 };

    /// <summary>Posições, em cada camada de um vão, onde se confere que um corte atravessaria só ar.</summary>
    private static readonly double[] BridgeFractions = { 0.0213, 0.2537, PrimarySample, 0.7461, 0.9787 };

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

        var results = new List<ReconstructionResult>();
        foreach (var axis in AxisFrame.All)
        {
            var (rangeMin, rangeMax) = MeshSlicer.GetAxisRange(mesh, axis);
            double length = rangeMax - rangeMin;
            if (length <= 1e-6) continue;

            var profile = SectionRegions.SilhouetteOuter(mesh, axis);
            if (profile == null || profile.Count < 3) continue;

            var envelope = new PrismEnvelope(axis, profile, rangeMin, rangeMax);
            var baseProfile = ToPolygonProfile(profile, axis, rangeMin);
            IFeature baseFeature = new ExtrudeFeature(baseProfile, length, axis.Axis * length, $"Base ({axis.Name})");

            var allCuts = AxisFrame.All.SelectMany(cutAxis => FindCuts(mesh, cutAxis, envelope, tolerance)).ToList();

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

    private sealed class CutThread
    {
        public double Lo;
        public double Hi;
        public List<(double U, double V)> Points = new();
        public double Area;
        public (double U, double V) Centroid;
    }

    /// <summary>
    /// Todos os cortes detectados ao varrer o eixo dado: agrupa o material removido com
    /// área/centróide parecidos entre camadas vizinhas numa única extrusão (ou cilindro) cobrindo
    /// a faixa contígua onde aparece.
    /// </summary>
    private static List<IFeature> FindCuts(TriangleMesh mesh, AxisFrame axis, PrismEnvelope envelope, double tolerance)
    {
        var levels = MeshSlicer.FindCapLevels(mesh, axis, tolerance);
        if (levels.Count < 2 || levels.Count > MaxLevelsPerAxis) return new List<IFeature>();

        var (rangeMin, rangeMax) = MeshSlicer.GetAxisRange(mesh, axis);

        var active = new List<CutThread>();
        var finished = new List<CutThread>();

        for (int i = 0; i < levels.Count - 1; i++)
        {
            double lo = levels[i];
            double hi = levels[i + 1];
            if (hi - lo <= tolerance) continue; // camada degenerada

            var regions = SectionRegions.RemovedRegions(mesh, axis, envelope, lo + (hi - lo) * PrimarySample, tolerance);

            var matchedThreads = new HashSet<CutThread>();
            var usedRegions = new HashSet<int>();

            for (int r = 0; r < regions.Count; r++)
            {
                double area = Math.Abs(SectionRegions.Area(regions[r]));
                var centroid = SectionRegions.Centroid(regions[r]);

                CutThread? best = null;
                double bestDistance = double.MaxValue;
                foreach (var thread in active)
                {
                    if (matchedThreads.Contains(thread)) continue;
                    if (!SameSection(thread.Area, thread.Centroid, area, centroid, 0.02, tolerance * 5)) continue;

                    double distance = Distance(thread.Centroid, centroid);
                    if (distance < bestDistance) { bestDistance = distance; best = thread; }
                }

                if (best != null)
                {
                    best.Hi = hi;
                    matchedThreads.Add(best);
                    usedRegions.Add(r);
                }
            }

            var stillActive = new List<CutThread>();
            foreach (var thread in active)
            {
                if (matchedThreads.Contains(thread)) stillActive.Add(thread);
                else finished.Add(thread);
            }
            active = stillActive;

            for (int r = 0; r < regions.Count; r++)
            {
                if (usedRegions.Contains(r)) continue;
                active.Add(new CutThread
                {
                    Lo = lo,
                    Hi = hi,
                    Points = regions[r],
                    Area = Math.Abs(SectionRegions.Area(regions[r])),
                    Centroid = SectionRegions.Centroid(regions[r])
                });
            }
        }
        finished.AddRange(active);

        // Um corte genuíno é reto ao longo do eixo B: sua seção não muda dentro da faixa [Lo,Hi]
        // onde foi detectado. Uma superfície de OUTRO eixo, cortada de raspão por B (um cilindro ao
        // longo de X aparece como uma fenda larga no meio e estreita nas pontas ao varrer por Y),
        // só bate a assinatura na fatia principal — reamostrar dentro da faixa denuncia isso.
        var validated = finished.Where(t => IsConsistentAcrossRange(mesh, axis, envelope, t, tolerance)).ToList();
        var merged = MergeAcrossEmptyGaps(mesh, axis, levels, validated, tolerance);

        var cutFeatures = new List<IFeature>();
        int n = 0;
        foreach (var thread in merged.OrderBy(t => t.Lo).ThenBy(t => t.Centroid.U).ThenBy(t => t.Centroid.V))
        {
            double lo = thread.Lo;
            double hi = thread.Hi;

            // Estende até além da peça se o corte encosta numa das pontas do eixo — a mesma
            // margem que a extrusão da face inferior já usa para furos passantes.
            if (lo - rangeMin <= tolerance) lo -= 1.0;
            if (rangeMax - hi <= tolerance) hi += 1.0;
            double height = hi - lo;
            if (height <= 1e-6) continue;

            n++;
            var fit = LoopFitting.FitCircle(thread.Points);
            // Um polígono regular (hexágono, octógono...) também tem todos os vértices
            // exatamente sobre um círculo, então o erro do ajuste de mínimos quadrados por si só
            // não distingue os dois casos. O que distingue é a densidade de pontos: a tesselação
            // de um cilindro real usa dezenas de segmentos, enquanto um polígono modelado como
            // feição tem poucos vértices.
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

    private static bool IsConsistentAcrossRange(TriangleMesh mesh, AxisFrame axis, PrismEnvelope envelope, CutThread thread, double tolerance)
    {
        double range = thread.Hi - thread.Lo;
        if (range <= 1e-9) return true;

        foreach (var fraction in SampleFractions)
        {
            double w = thread.Lo + range * fraction;
            bool found = SectionRegions.RemovedRegions(mesh, axis, envelope, w, tolerance).Any(region =>
                SameSection(thread.Area, thread.Centroid,
                    Math.Abs(SectionRegions.Area(region)), SectionRegions.Centroid(region), 0.05, tolerance * 10));

            if (!found) return false;
        }
        return true;
    }

    /// <summary>
    /// Junta trechos do mesmo corte (mesma seção) separados por camadas onde essa seção não
    /// encontra material nenhum: o furo que atravessa os dois braços de um U passando pelo vão,
    /// ou o chanfro que segue pelas duas abas. Uma ferramenta só, em vez de uma por trecho.
    /// </summary>
    private static List<CutThread> MergeAcrossEmptyGaps(
        TriangleMesh mesh, AxisFrame axis, List<double> levels, List<CutThread> threads, double tolerance)
    {
        var ordered = threads.OrderBy(t => t.Lo).ToList();
        var result = new List<CutThread>();
        var consumed = new HashSet<CutThread>();

        foreach (var first in ordered)
        {
            if (consumed.Contains(first)) continue;
            consumed.Add(first);
            var current = first;

            bool extended = true;
            while (extended)
            {
                extended = false;
                foreach (var next in ordered)
                {
                    if (consumed.Contains(next)) continue;
                    if (next.Lo < current.Hi - tolerance) continue;
                    if (!SameSection(current.Area, current.Centroid, next.Area, next.Centroid, 0.02, tolerance * 5)) continue;
                    if (!GapIsEmpty(mesh, axis, levels, current.Points, current.Area, current.Hi, next.Lo)) continue;

                    current = new CutThread
                    {
                        Lo = current.Lo,
                        Hi = next.Hi,
                        Points = current.Points,
                        Area = current.Area,
                        Centroid = current.Centroid
                    };
                    consumed.Add(next);
                    extended = true;
                    break;
                }
            }

            result.Add(current);
        }

        return result;
    }

    private static bool GapIsEmpty(
        TriangleMesh mesh, AxisFrame axis, List<double> levels, List<(double U, double V)> section, double sectionArea, double from, double to)
    {
        if (to - from <= 1e-9) return true;

        // As camadas do vão são delimitadas pelos níveis de tampa (onde o material muda); amostra
        // cada uma em vários pontos.
        var bounds = levels.Where(l => l > from && l < to).Append(from).Append(to).OrderBy(l => l).ToList();
        double limit = Math.Max(sectionArea * 0.005, 1e-4);

        for (int i = 0; i < bounds.Count - 1; i++)
        {
            double lo = bounds[i], hi = bounds[i + 1];
            if (hi - lo <= 1e-9) continue;
            foreach (var fraction in BridgeFractions)
            {
                if (SectionRegions.MaterialAreaInside(mesh, axis, section, lo + (hi - lo) * fraction) > limit) return false;
            }
        }
        return true;
    }

    private static bool SameSection(
        double areaA, (double U, double V) centroidA, double areaB, (double U, double V) centroidB,
        double areaTolerance, double centroidTolerance)
    {
        if (areaA < 1e-9 || areaB < 1e-9) return false;
        if (Math.Abs(areaA - areaB) / Math.Max(areaA, areaB) > areaTolerance) return false;
        return Distance(centroidA, centroidB) <= centroidTolerance;
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
