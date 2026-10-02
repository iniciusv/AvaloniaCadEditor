using System;
using System.Collections.Generic;

namespace AvaloniaCadEditor.Application.Ai.Context;

/// <summary>
/// Fecho transitivo dos tipos usados por um conjunto de arquivos: se Pecas/PecaB.cs usa
/// <c>Medidas.Espessura</c>, o arquivo que declara <c>Medidas</c> também é necessário.
/// </summary>
public static class TypeDependencyResolver
{
    /// <param name="seeds">Arquivos de partida (já no contexto).</param>
    /// <param name="entryFile">
    /// Não é varrido: o arquivo de entrada instancia todas as peças da cena e traria o projeto inteiro.
    /// </param>
    /// <param name="excluded">Arquivos que não entram nem são varridos.</param>
    /// <returns>Os arquivos novos (fora de <paramref name="seeds"/>), na ordem em que foram descobertos.</returns>
    public static IReadOnlyList<ContextFileHit> Resolve(
        IEnumerable<string> seeds,
        SceneSyntaxIndex index,
        string entryFile,
        IReadOnlySet<string> excluded)
    {
        var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<string>();
        foreach (var seed in seeds)
        {
            if (known.Add(seed)) queue.Enqueue(seed);
        }

        var found = new List<ContextFileHit>();
        while (queue.Count > 0)
        {
            string path = queue.Dequeue();
            if (string.Equals(path, entryFile, StringComparison.OrdinalIgnoreCase)) continue;

            foreach (var name in index.NamesUsedIn(path))
            {
                if (index.FileDeclaring(name) is not { } file || excluded.Contains(file) || !known.Add(file)) continue;

                found.Add(new ContextFileHit(file, name));
                queue.Enqueue(file);
            }
        }
        return found;
    }
}
