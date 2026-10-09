using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using AvaloniaCadEditor.Domain.Entities;
using AvaloniaCadEditor.Domain.ValueObjects;

namespace AvaloniaCadEditor.Application.Services;

/// <summary>
/// Arestas de esboço encadeadas num caminho: os pontos em ordem, ou o porquê de não formarem um
/// caminho.
/// <para>
/// Os pontos saem em coordenadas de MUNDO. É o que permite encadear arestas de planos diferentes
/// (uma cadeia que sobe de um plano para outro é um caminho 3D legítimo, e o
/// <see cref="SweepPath.FromPolyline"/> trabalha em 3D): o (u, v) local de dois planos distintos
/// não é comparável, então encadear em local só funcionaria num esboço só.
/// </para>
/// <para>
/// Num caminho fechado o primeiro ponto NÃO se repete no fim: a aresta de volta é implícita, a
/// mesma grafia que <see cref="SweepPath.FromPolyline"/> espera com <c>closed: true</c>.
/// </para>
/// </summary>
/// <param name="Plane">
/// O plano da primeira aresta do percurso. Não é restrição nenhuma sobre as outras — serve só para
/// quem precisa de uma referência de "para onde é para cima" no caminho.
/// </param>
public sealed record EdgeChain(
    IReadOnlyList<Point3D> Points,
    bool IsClosed,
    WorkPlane Plane,
    IReadOnlyList<Guid> SourceEntityIds,
    string? Error)
{
    public static EdgeChain Failed(string error) =>
        new(Array.Empty<Point3D>(), false, WorkPlane.XY, Array.Empty<Guid>(), error);
}

/// <summary>
/// Encadeia arestas de esboço num caminho contínuo. É o irmão do
/// <see cref="ProfileRecognitionService"/>: mesmo grafo de pontas com fusão por
/// <see cref="Tolerance"/>, mas aqui o que se quer é uma CADEIA (um percurso), não os ciclos
/// mínimos de uma região — por isso é classificação de grau mais um percurso só, e não o DFS que
/// enumera ciclos.
/// </summary>
public static class EdgeChainService
{
    /// <summary>Abaixo disto duas pontas são a mesma ponta. Mesma convenção do reconhecimento de regiões.</summary>
    public const double Tolerance = 1e-4;

    private sealed class Node
    {
        public int Id;
        public Point3D Point;
        public readonly List<(int Target, Guid EntityId)> Edges = new();
    }

    /// <summary>
    /// Os pontos do caminho em ordem, ou <see cref="EdgeChain.Error"/> dizendo o que impede.
    /// <para>
    /// O percurso é DETERMINÍSTICO — começa pela ponta lexicograficamente menor — porque a
    /// orientação da seção varrida depende do rumo inicial do caminho: inverter a ponta de partida
    /// espelharia o sólido, e o mesmo desenho tem que dar sempre o mesmo sólido.
    /// </para>
    /// </summary>
    public static EdgeChain Chain(IEnumerable<LineEntity> lines)
    {
        var list = lines?.ToList() ?? new List<LineEntity>();

        if (list.Count == 0)
            return EdgeChain.Failed("nenhuma aresta selecionada para o caminho.");

        if (list.Any(l => l.Length < Tolerance))
            return EdgeChain.Failed("uma das arestas selecionadas tem comprimento zero.");

        var nodes = new List<Node>();

        Node GetOrCreateNode(Point3D point)
        {
            foreach (var node in nodes)
            {
                if (node.Point.DistanceTo(point) <= Tolerance) return node;
            }
            var created = new Node { Id = nodes.Count, Point = point };
            nodes.Add(created);
            return created;
        }

        // Em coordenadas de MUNDO: é o que faz duas arestas de planos diferentes se encontrarem
        // numa ponta comum em vez de parecerem longe uma da outra.
        foreach (var line in list)
        {
            var a = GetOrCreateNode(line.Plane.ToWorldSpace(line.StartPoint));
            var b = GetOrCreateNode(line.Plane.ToWorldSpace(line.EndPoint));
            if (a.Id == b.Id) continue;   // laço de comprimento zero já recusado acima

            a.Edges.Add((b.Id, line.Id));
            b.Edges.Add((a.Id, line.Id));
        }

        var ramificado = nodes.FirstOrDefault(n => n.Edges.Count >= 3);
        if (ramificado != null)
            return EdgeChain.Failed(
                $"as linhas se ramificam em {Fmt(ramificado.Point)}: um caminho não pode ter bifurcação.");

        int componentes = CountComponents(nodes);
        if (componentes > 1)
            return EdgeChain.Failed($"as linhas não formam uma cadeia contínua: há {componentes} trechos separados.");

        // Com todo grau <= 2 e um componente só, as pontas de grau 1 são exatamente 0 (ciclo) ou 2
        // (caminho aberto) — não há terceiro caso para conferir.
        var pontas = nodes.Where(n => n.Edges.Count == 1).OrderBy(n => n.Point, PointOrder).ToList();
        bool fechado = pontas.Count == 0;

        if (fechado && nodes.Count < 3)
            return EdgeChain.Failed("um caminho fechado precisa de pelo menos três arestas.");

        var ordered = Walk(nodes, fechado ? SmallestCycleStart(nodes) : pontas[0].Id, fechado);
        var ids = list.Select(l => l.Id).ToList();

        return new EdgeChain(ordered, fechado, PlanoDoPrimeiroTrecho(list, ordered), ids, null);
    }

    /// <summary>
    /// O plano da aresta que o percurso atravessa primeiro. É achado pelos dois primeiros pontos
    /// (e não pela ordem da seleção) para que a referência seja a mesma toda vez.
    /// </summary>
    private static WorkPlane PlanoDoPrimeiroTrecho(List<LineEntity> lines, List<Point3D> ordered)
    {
        if (ordered.Count < 2) return lines[0].Plane;

        foreach (var l in lines)
        {
            var a = l.Plane.ToWorldSpace(l.StartPoint);
            var b = l.Plane.ToWorldSpace(l.EndPoint);

            bool casa = (a.DistanceTo(ordered[0]) <= Tolerance && b.DistanceTo(ordered[1]) <= Tolerance)
                     || (b.DistanceTo(ordered[0]) <= Tolerance && a.DistanceTo(ordered[1]) <= Tolerance);

            if (casa) return l.Plane;
        }

        return lines[0].Plane;
    }

    /// <summary>
    /// Percorre a cadeia de nó em nó, consumindo cada aresta uma vez. Como todo nó tem grau no
    /// máximo 2, só o primeiro passo tem escolha de verdade (num ciclo) — e aí o vizinho menor
    /// decide, para o rumo inicial não depender da ordem em que as linhas foram desenhadas.
    /// </summary>
    private static List<Point3D> Walk(List<Node> nodes, int startId, bool closed)
    {
        var points = new List<Point3D>(nodes.Count);
        var usadas = new HashSet<Guid>();

        int atual = startId;

        while (true)
        {
            points.Add(nodes[atual].Point);

            var candidatas = nodes[atual].Edges.Where(e => !usadas.Contains(e.EntityId)).ToList();
            if (candidatas.Count == 0) break;

            var proxima = candidatas.OrderBy(e => nodes[e.Target].Point, PointOrder).First();
            usadas.Add(proxima.EntityId);
            atual = proxima.Target;

            // Num ciclo, a aresta de volta é implícita: para antes de repetir o primeiro ponto.
            if (closed && atual == startId) break;
        }

        return points;
    }

    /// <summary>Num ciclo, o nó lexicograficamente menor é o ponto de partida.</summary>
    private static int SmallestCycleStart(List<Node> nodes) =>
        nodes.OrderBy(n => n.Point, PointOrder).First().Id;

    private static int CountComponents(List<Node> nodes)
    {
        var visto = new bool[nodes.Count];
        int componentes = 0;

        for (int i = 0; i < nodes.Count; i++)
        {
            if (visto[i]) continue;
            componentes++;

            var pilha = new Stack<int>();
            pilha.Push(i);
            visto[i] = true;

            while (pilha.Count > 0)
            {
                foreach (var (target, _) in nodes[pilha.Pop()].Edges)
                {
                    if (visto[target]) continue;
                    visto[target] = true;
                    pilha.Push(target);
                }
            }
        }

        return componentes;
    }

    /// <summary>Ordem lexicográfica em (x, y, z) — só serve para desempatar de forma estável.</summary>
    private static readonly IComparer<Point3D> PointOrder = Comparer<Point3D>.Create((a, b) =>
    {
        int cx = a.X.CompareTo(b.X); if (cx != 0) return cx;
        int cy = a.Y.CompareTo(b.Y); if (cy != 0) return cy;
        return a.Z.CompareTo(b.Z);
    });

    private static string Fmt(Point3D p) =>
        $"({p.X.ToString("0.###", CultureInfo.InvariantCulture)}, {p.Y.ToString("0.###", CultureInfo.InvariantCulture)})";
}
