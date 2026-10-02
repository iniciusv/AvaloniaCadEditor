using System;
using System.Collections.Generic;
using System.Linq;
using AvaloniaCadEditor.Application.Ai;
using AvaloniaCadEditor.Application.Ai.Context;
using AvaloniaCadEditor.Application.Interfaces;
using FluentAssertions;
using Xunit;

namespace AvaloniaCadEditor.Tests.Ai.Context;

public class AiEditScopePolicyTests
{
    private static readonly IReadOnlyList<ScriptDocument> Project = ContextTestScene.Documents();

    private static CadScriptContext MultiFile() => new(
        "",
        FileName: "Cena.cs",
        ProjectDocuments: Project,
        Scope: new MultiFileScope(
            Project.Where(d => d.FilePath is "Cena.cs" or "Pecas/PecaA.cs").ToList(),
            new Dictionary<string, CadFileKind>(),
            Array.Empty<string>()));

    private static CadScriptChanges Changes(params (string? File, string Code)[] files) =>
        new(files.Select(f => new CadScriptFileEdit(f.File, f.Code)).ToList());

    [Theory]
    [InlineData("Cena.cs", CadFileKind.SceneEntry)]
    [InlineData("Pecas/PecaA.cs", CadFileKind.PartFile)]
    [InlineData("Pecas/Medidas.cs", CadFileKind.SupportFile)]
    [InlineData("Pecas/Nova.cs", CadFileKind.PartFile)]
    public void KindOf_ClassificaPeloConteudo(string file, CadFileKind expected)
    {
        AiEditScopePolicy.KindOf(file, "Cena.cs", new SceneSyntaxIndex(Project)).Should().Be(expected);
    }

    [Fact]
    public void ResolveEdits_BlocoSemFile_VaiParaOArquivoAtivo()
    {
        var (changes, error) = AiEditScopePolicy.ResolveEdits(Changes((null, "x")), MultiFile());

        error.Should().BeNull();
        changes.Single().FilePath.Should().Be("Cena.cs");
    }

    [Fact]
    public void ResolveEdits_ArquivoForaDoEscopo_Erro()
    {
        var (_, error) = AiEditScopePolicy.ResolveEdits(Changes(("Pecas/PecaC.cs", "x")), MultiFile());

        error.Should().Contain("Pecas/PecaC.cs");
    }

    [Theory]
    [InlineData("Pecas/Nova.cs", true)]
    [InlineData("Pecas/PecaC.cs", false)]
    [InlineData("Outro/Nova.cs", false)]
    [InlineData("Pecas/../Nova.cs", false)]
    public void IsNewPartFile_SoPecaNovaDentroDePecas(string file, bool expected)
    {
        AiEditScopePolicy.IsNewPartFile(file, Project).Should().Be(expected);
    }

    [Fact]
    public void ResolveEdits_ArquivoUnico_ValeOUltimoBloco()
    {
        var context = new CadScriptContext("", FileName: "Cena.cs");

        var (changes, error) = AiEditScopePolicy.ResolveEdits(Changes(("Qualquer.cs", "a"), (null, "b")), context);

        error.Should().BeNull();
        changes.Should().ContainSingle().Which.Should().Be(new ScriptDocument("Cena.cs", "b"));
    }
}
