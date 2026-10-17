using System.Runtime.CompilerServices;
using System.Xml.Linq;
using FluentAssertions;

namespace AvaloniaCadEditor.Harness.Tests.Architecture;

/// <summary>
/// Fixa o sentido das dependências entre projetos: o Harness conhece o app, o app não conhece o
/// Harness. Lê os <c>ProjectReference</c> declarados nos .csproj (mais estrito que
/// <c>Assembly.GetReferencedAssemblies</c>, que omite referências declaradas e não usadas).
/// </summary>
public class ArchitectureTests
{
    private const string Harness = "AvaloniaCadEditor.Harness";

    [Theory]
    [InlineData("AiCore")]
    [InlineData("AvaloniaCadEditor.Domain")]
    [InlineData("AvaloniaCadEditor.Analysis")]
    [InlineData("AvaloniaCadEditor.Drafting")]
    [InlineData("AvaloniaCadEditor.Application")]
    [InlineData("AvaloniaCadEditor.Infrastructure")]
    public void ProjetosDoApp_NaoReferenciamOHarness(string project)
    {
        ReferencesOf(project).Should().NotContain(Harness);
    }

    [Fact]
    public void Application_NaoReferenciaDrafting()
    {
        // Quem junta cena, malha e cotas é o Harness; o Application segue sem conhecer o Drafting.
        ReferencesOf("AvaloniaCadEditor.Application").Should().NotContain("AvaloniaCadEditor.Drafting");
    }

    [Fact]
    public void Harness_NaoReferenciaInfrastructureNemUI()
    {
        // Kernel OCCT e avaliador Roslyn entram pelas portas do Application.
        ReferencesOf(Harness).Should().NotContain(new[] { "AvaloniaCadEditor.Infrastructure", "AvaloniaCadEditor.UI" });
    }

    [Fact]
    public void AiCore_NaoReferenciaNenhumProjeto()
    {
        // AiCore é copiável para outros projetos: só BCL.
        ReferencesOf("AiCore").Should().BeEmpty();
    }

    private static IReadOnlyList<string> ReferencesOf(string project)
    {
        string path = Path.Combine(RepoRoot(), project, project + ".csproj");
        File.Exists(path).Should().BeTrue($"o projeto {project} deve existir em {path}");
        return XDocument.Load(path)
            .Descendants("ProjectReference")
            .Select(e => Path.GetFileNameWithoutExtension(e.Attribute("Include")!.Value.Replace('\\', '/')))
            .ToList();
    }

    private static string RepoRoot([CallerFilePath] string file = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, "..", ".."));
}
