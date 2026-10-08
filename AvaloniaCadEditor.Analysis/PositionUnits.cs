using System.Runtime.CompilerServices;

namespace AvaloniaCadEditor.Analysis;

/// <summary>
/// O que conta como UM elemento ao comparar posições. Uma peça aninhada (componente, ex.: uma
/// FuroPorca com furo e alojamento) é uma unidade só, e furos coaxiais (furo passante + rebaixo)
/// também. Sem isso, um único furo com rebaixo "forma uma fileira" consigo mesmo, e um único par de
/// componentes espelhados passa por "dois pares" de simetria.
/// </summary>
public sealed class PositionUnits
{
    private readonly Dictionary<PrimitiveFact, int> _unitOf;
    private readonly List<string> _names;

    private PositionUnits(Dictionary<PrimitiveFact, int> unitOf, List<string> names)
    {
        _unitOf = unitOf;
        _names = names;
    }

    public static readonly PositionUnits Empty = new(new Dictionary<PrimitiveFact, int>(), new List<string>());

    /// <summary>Agrupa por componente (do mesmo sujeito) e por conjunto coaxial; o resto é um por fato.</summary>
    public static PositionUnits Build(IReadOnlyList<PrimitiveFact> facts, IReadOnlyList<CoaxialSet> coaxial)
    {
        var coaxialOf = new Dictionary<PrimitiveFact, int>();
        for (int i = 0; i < coaxial.Count; i++)
        {
            foreach (var member in coaxial[i].Members) coaxialOf.TryAdd(member, i);
        }

        var unitOf = new Dictionary<PrimitiveFact, int>();
        var names = new List<string>();
        var byKey = new Dictionary<string, int>();
        foreach (var fact in facts)
        {
            string? key = fact.Component != null ? $"c|{fact.Subject}|{fact.Component.Name}"
                : coaxialOf.TryGetValue(fact, out int set) ? $"x|{set}"
                : null;

            if (key != null && byKey.TryGetValue(key, out int existing))
            {
                unitOf[fact] = existing;
                continue;
            }

            int unit = names.Count;
            names.Add(fact.Component != null ? ComponentName(fact)
                : key != null ? CoaxialName(coaxial[coaxialOf[fact]].Members)
                : fact.Name);
            unitOf[fact] = unit;
            if (key != null) byKey[key] = unit;
        }

        return new PositionUnits(unitOf, names);
    }

    /// <summary>A unidade do fato. Um fato que não estava na análise vira uma unidade só dele.</summary>
    public int Of(PrimitiveFact fact) => _unitOf.TryGetValue(fact, out int unit) ? unit : ~RuntimeHelpers.GetHashCode(fact);

    /// <summary>Quantas unidades distintas há entre os fatos.</summary>
    public int Count(IEnumerable<PrimitiveFact> facts) => facts.Select(Of).Distinct().Count();

    /// <summary>Nome legível da unidade do fato: o caminho do componente ("placa/porca_inferior_3"), os
    /// coaxiais juntos ("furo_passante_3+rebaixo_3") ou o nome do próprio sólido.</summary>
    public string NameOf(PrimitiveFact fact)
    {
        int unit = Of(fact);
        return unit >= 0 && unit < _names.Count ? _names[unit] : fact.Name;
    }

    /// <summary>Os nomes das unidades distintas, na ordem em que aparecem.</summary>
    public IReadOnlyList<string> Names(IEnumerable<PrimitiveFact> facts) =>
        facts.DistinctBy(Of).Select(NameOf).ToList();

    /// <summary>"peca/furo_passante_3+rebaixo_3": o caminho só no primeiro, que os outros repetiriam.</summary>
    private static string CoaxialName(IReadOnlyList<PrimitiveFact> members) =>
        string.Join("+", members.Select((m, i) => i == 0 ? m.Name : m.Name[(m.Name.LastIndexOf('/') + 1)..]));

    /// <summary>"sujeito/componente" a partir de "sujeito/componente/furo" (o componente pode ter
    /// aninhados dentro dele: corta logo depois do trecho com o nome dele).</summary>
    private static string ComponentName(PrimitiveFact fact)
    {
        var segments = fact.Name.Split('/');
        int index = Array.IndexOf(segments, fact.Component!.Name);
        if (index < 0) index = segments.Length - 2;
        return index < 0 ? fact.Name : string.Join("/", segments.Take(index + 1));
    }
}
