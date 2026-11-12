using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AvaloniaCadEditor.Application.Library;

namespace AvaloniaCadEditor.Harness.Tools.Query;

/// <summary>
/// Busca na biblioteca de peças do usuário (a pasta fora da cena): nome, categoria, uma linha de descrição, a
/// assinatura e os modelos prontos. O catálogo é o da aba Biblioteca (<see cref="PartLibraryCatalog"/>), lido só pela
/// sintaxe. Para trazer uma peça para a cena, a IA usa <see cref="Edit.ImportLibraryPartTool"/>.
/// </summary>
public sealed class SearchLibraryTool : ICadTool
{
    /// <summary>Quantas peças uma resposta lista no máximo.</summary>
    public const int MaxResults = 15;

    public string Name => "search_library";

    public string Description =>
        "Busca peças prontas na biblioteca do usuário (perfis, guias lineares, rolamentos, motores, furos de fixação...). " +
        "Devolve nome, categoria, descrição, parâmetros e modelos prontos de cada uma, e se a cena já a tem. " +
        "Todas as palavras precisam aparecer no nome, na categoria ou na descrição. Sem 'query', lista as categorias. " +
        "Para usar uma peça, chame import_library_part.";

    public bool IsReadOnly => true;

    public JsonObject ParametersSchema => ToolSchema.Object(
        ToolSchema.String("query", "Palavras a buscar (ex.: \"mgn 9\", \"rolamento\", \"m3\"). Vazio = categorias.", required: false));

    public Task<string> ExecuteAsync(JsonElement args, CadToolContext context, CancellationToken cancellationToken)
    {
        var library = context.Library;
        if (library.Count == 0) return Task.FromResult("A biblioteca de peças está vazia ou não foi configurada.");

        var items = PartLibraryCatalog.Read(library, PartLibrarySource.Library);
        var sceneTypes = PartDependencyIndex.Of(context.SceneDocuments());
        string query = new ToolArgs(args).OptionalString("query")?.Trim() ?? string.Empty;

        if (query.Length == 0) return Task.FromResult(Categories(items));

        var words = query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var found = items
            .Where(i => words.All(w => (i.Name + " " + i.Category + " " + i.Summary).Contains(w, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(i => words.Count(w => i.Name.Contains(w, StringComparison.OrdinalIgnoreCase)))
            .ThenBy(i => i.Name, StringComparer.Ordinal)
            .ToList();
        if (found.Count == 0)
            return Task.FromResult($"Nenhuma peça com \"{query}\". Tente menos palavras, ou search_library sem query para ver as categorias.");

        var text = new StringBuilder();
        text.Append(CultureInfo.InvariantCulture, $"{found.Count} peça(s) com \"{query}\"");
        text.AppendLine(found.Count > MaxResults ? $" (mostrando {MaxResults}):" : ":");
        foreach (var item in found.Take(MaxResults))
        {
            text.AppendLine();
            text.Append("- ").Append(item.Name);
            if (!string.IsNullOrEmpty(item.Category)) text.Append(" [").Append(item.Category).Append(']');
            if (item.IsCutter) text.Append(" (vazio: para subtrair dentro do Build() de outra peça)");
            if (sceneTypes.FilesDeclaring(item.Name).Count > 0) text.Append(" (já está na cena)");
            text.AppendLine();
            if (item.Summary.Length > 0) text.Append("  ").AppendLine(item.ShortSummary);
            text.Append("  ").AppendLine(Signature(item));
            if (item.Presets.Count > 0)
                text.Append("  modelos prontos: ").AppendLine(string.Join(", ", item.Presets.Select(p => $"{item.Name}.{p.Name}")));
        }
        return Task.FromResult(text.ToString().TrimEnd());
    }

    /// <summary><c>Tipo(double Largura = 40, Rosca Rosca = Rosca.M3)</c>.</summary>
    public static string Signature(PartLibraryItem item) =>
        $"{item.Name}({string.Join(", ", item.Parameters.Select(p => p.DefaultValue == null ? $"{p.TypeName} {p.Name}" : $"{p.TypeName} {p.Name} = {p.DefaultValue}"))})";

    private static string Categories(IReadOnlyList<PartLibraryItem> items)
    {
        var text = new StringBuilder();
        text.AppendLine(CultureInfo.InvariantCulture, $"{items.Count} peça(s) na biblioteca, por categoria:");
        foreach (var group in items.GroupBy(i => string.IsNullOrEmpty(i.Category) ? "(raiz)" : i.Category).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            text.Append("- ").Append(group.Key).Append(": ").AppendLine(string.Join(", ", group.Select(i => i.Name)));
        }
        return text.ToString().TrimEnd();
    }
}
