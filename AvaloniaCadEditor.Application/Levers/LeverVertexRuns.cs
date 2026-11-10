namespace AvaloniaCadEditor.Application.Levers;

/// <summary>
/// As arestas retas de um contorno, vistas pelos números do código: os pontos seguidos da mesma lista (o
/// último é vizinho do primeiro) com a mesma coordenada escrita do mesmo jeito. No retângulo
/// <c>(16, -24) (16, 24) (-16, 24) (-16, -24)</c>, o <c>x = 16</c> dos pontos 1 e 2 é uma aresta, e o
/// <c>y = -24</c> dos pontos 4 e 1 é outra.
/// <para>
/// Mover um ponto só entorta a aresta; por isso os números de um trecho andam juntos numa opção própria.
/// </para>
/// Puro e sem estado.
/// </summary>
public static class LeverVertexRuns
{
    /// <summary>
    /// Os trechos de dois ou mais pontos, cada um na ordem da lista. Todos os pontos com a mesma coordenada
    /// formam um trecho só que começa no primeiro (o plano do contorno, quando é o <c>z</c>).
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<CodeLever>> Find(IReadOnlyList<CodeLever> levers)
    {
        ArgumentNullException.ThrowIfNull(levers);

        var result = new List<IReadOnlyList<CodeLever>>();
        var sameCoordinate = levers
            .Where(l => l.Vertex != null)
            .GroupBy(l => (l.Vertex!.ListId, l.Path, l.Vertex.Coordinate));

        foreach (var group in sameCoordinate)
        {
            var byIndex = new Dictionary<int, CodeLever>();
            foreach (var lever in group) byIndex.TryAdd(lever.Vertex!.Index, lever);
            if (byIndex.Count < 2) continue;

            int count = group.First().Vertex!.Count;
            var visited = new HashSet<int>();
            foreach (int start in byIndex.Keys.OrderBy(i => i))
            {
                if (visited.Contains(start)) continue;

                // Recua até o começo do trecho (dando a volta) e depois avança enquanto houver vizinho.
                // Quando são todos os pontos da lista não há começo: fica o primeiro.
                int first = start;
                if (byIndex.Count < count)
                {
                    while (byIndex.ContainsKey(Previous(first, count)) && Previous(first, count) != start) first = Previous(first, count);
                }

                var run = new List<CodeLever>();
                for (int i = first; byIndex.ContainsKey(i) && visited.Add(i); i = (i + 1) % count) run.Add(byIndex[i]);

                if (run.Count >= 2) result.Add(run);
            }
        }
        return result;
    }

    private static int Previous(int index, int count) => (index - 1 + count) % count;
}
