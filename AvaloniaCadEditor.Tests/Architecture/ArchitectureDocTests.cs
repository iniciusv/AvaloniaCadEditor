using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using FluentAssertions;

namespace AvaloniaCadEditor.Tests.Architecture;

/// <summary>
/// Trava o ARCHITECTURE.md contra o código: é o mapa que o CLAUDE.md da raiz carrega em toda sessão,
/// e um mapa velho manda quem vai alterar o app para um arquivo que não existe mais.
/// </summary>
public class ArchitectureDocTests
{
    // `Application/Ai/CadScriptRules.cs`, `UI/Drafting/`: caminho entre crases com barra, de arquivo ou pasta.
    private static readonly Regex CodePath = new(@"`([\w.\-*/]+/(?:[\w.\-*]+\.(?:cs|axaml|csproj|md|slnx))?)`");

    // [texto](AvaloniaCadEditor.UI/App.axaml.cs#L47): link relativo do Markdown.
    private static readonly Regex LinkPath = new(@"\]\((?!https?:)([^)#\s]+)(?:#[^)]*)?\)");

    // XFeature.cs, XCommand.cs, XTool.cs: nomes de exemplo, não arquivos.
    private static readonly Regex Placeholder = new(@"(^|/)X[A-Z]\w*\.\w+$");

    [Fact]
    public void CaminhosCitados_Existem()
    {
        string root = RepoRoot();
        string doc = File.ReadAllText(Path.Combine(root, "ARCHITECTURE.md"));
        var files = RepoFiles(root);

        var cited = CodePath.Matches(doc).Select(m => m.Groups[1].Value)
            .Concat(LinkPath.Matches(doc).Select(m => m.Groups[1].Value))
            .Where(p => !Placeholder.IsMatch(p))
            .Distinct();

        cited.Where(p => !Resolves(p, files)).Should().BeEmpty(
            "todo caminho citado no ARCHITECTURE.md deve existir; atualize o mapa ao mover ou renomear arquivos");
    }

    [Fact]
    public void TodoProjetoDaSolution_EstaNaTabelaDeProjetos()
    {
        string root = RepoRoot();
        string doc = File.ReadAllText(Path.Combine(root, "ARCHITECTURE.md"));

        var projects = XDocument.Load(Path.Combine(root, "AvaloniaCadEditor.slnx"))
            .Descendants("Project")
            .Select(e => Path.GetFileNameWithoutExtension(e.Attribute("Path")!.Value));

        projects.Where(p => !doc.Contains($"| `{p}` |")).Should().BeEmpty(
            "todo projeto da AvaloniaCadEditor.slnx deve ter uma linha na tabela da §2 do ARCHITECTURE.md");
    }

    /// <summary>
    /// O mapa abrevia <c>AvaloniaCadEditor.Application/</c> como <c>Application/</c> e às vezes cita só o
    /// fim do caminho (<c>Cli/CheckCommand.cs</c>), então vale qualquer arquivo do repo que termine nele.
    /// </summary>
    private static bool Resolves(string cited, IReadOnlyList<string> files)
    {
        // Saída de build (a armadilha do MSB3021 cita UI/bin/) não existe num clone limpo.
        if (cited.Contains("bin/") || cited.Contains("obj/")) return true;

        // Pasta: basta um arquivo dentro dela. Arquivo: o caminho tem de terminar nele.
        string tail = cited.EndsWith('/') ? "" : "$";
        var pattern = new Regex(@"(^|/)(AvaloniaCadEditor\.)?" + Regex.Escape(cited).Replace(@"\*", "[^/]*") + tail);
        return files.Any(f => pattern.IsMatch(f));
    }

    /// <summary>Arquivos do repo com caminho relativo e barra normal, sem saídas de build.</summary>
    private static IReadOnlyList<string> RepoFiles(string root)
    {
        string[] skipped = { "/bin/", "/obj/", "/.git/", "/.vs/" };
        return Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(root, f).Replace('\\', '/'))
            .Where(f => !skipped.Any(s => ("/" + f).Contains(s)))
            .ToList();
    }

    private static string RepoRoot([CallerFilePath] string file = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, "..", ".."));
}
