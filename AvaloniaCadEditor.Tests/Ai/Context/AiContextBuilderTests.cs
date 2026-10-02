using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AvaloniaCadEditor.Application.Ai;
using AvaloniaCadEditor.Application.Ai.Context;
using AvaloniaCadEditor.Application.Interfaces;
using AvaloniaCadEditor.Domain.Features;
using FluentAssertions;
using Xunit;

namespace AvaloniaCadEditor.Tests.Ai.Context;

public class AiContextBuilderTests
{
    private readonly AiContextBuilder _builder = new();

    private static AiContextRequest Request(
        IReadOnlyList<ScriptDocument> docs,
        IEnumerable<IFeature> selected,
        string? activeFile = "Cena.cs",
        AiContextAdjustments? adjustments = null) =>
        new(docs, "Cena.cs", activeFile, selected.ToList(), Adjustments: adjustments);

    private static IEnumerable<string> Paths(AiContextScope scope) => scope.Files.Select(f => f.FilePath);

    [Fact]
    public async Task DuasInstanciasSelecionadas_IncluiCenaEOsArquivosDasDuasPecas()
    {
        var docs = ContextTestScene.Documents();
        var features = await ContextTestScene.EvaluateAsync(docs);

        var scope = _builder.ResolveScope(Request(docs, features.Where(f => f.Name is "a_1" or "c_1")));

        Paths(scope).Should().BeEquivalentTo("Cena.cs", "Pecas/PecaA.cs", "Pecas/PecaC.cs");
        scope.Files.Single(f => f.FilePath == "Pecas/PecaA.cs").Reason.Should().Be(AiContextReason.Selection);
    }

    [Fact]
    public async Task PecaQueUsaOutroTipo_IncluiArquivoDoTipoComoDependencia()
    {
        var docs = ContextTestScene.Documents();
        var features = await ContextTestScene.EvaluateAsync(docs);

        var scope = _builder.ResolveScope(Request(docs, features.Where(f => f.Name == "b_1")));

        Paths(scope).Should().BeEquivalentTo("Cena.cs", "Pecas/PecaB.cs", "Pecas/Medidas.cs");
        scope.Files.Single(f => f.FilePath == "Pecas/Medidas.cs").Should()
            .Be(new AiContextFile("Pecas/Medidas.cs", AiContextReason.Dependency, "Medidas"));
    }

    [Fact]
    public async Task BooleanaComInstancia_IncluiArquivoDaPecaOperando()
    {
        var docs = ContextTestScene.Documents();
        var features = await ContextTestScene.EvaluateAsync(docs);

        var scope = _builder.ResolveScope(Request(docs, features.Where(f => f.Name == "corte_1")));

        Paths(scope).Should().BeEquivalentTo("Cena.cs", "Pecas/PecaA.cs");
    }

    [Fact]
    public void SemSelecao_SoEntradaEArquivoAtivoComSuasDependencias()
    {
        var scope = _builder.ResolveScope(Request(ContextTestScene.Documents(), Array.Empty<IFeature>(), activeFile: "Pecas/PecaB.cs"));

        Paths(scope).Should().Equal("Cena.cs", "Pecas/PecaB.cs", "Pecas/Medidas.cs");
    }

    [Fact]
    public async Task ExcluidosEFixados_SaoRespeitados_MasAEntradaNuncaSai()
    {
        var docs = ContextTestScene.Documents();
        var features = await ContextTestScene.EvaluateAsync(docs);
        var adjustments = new AiContextAdjustments();
        adjustments.Pin("Pecas/PecaC.cs");
        adjustments.Exclude("Pecas/PecaA.cs");
        adjustments.Exclude("Cena.cs");

        var scope = _builder.ResolveScope(Request(docs, features.Where(f => f.Name == "a_1"), adjustments: adjustments));

        Paths(scope).Should().BeEquivalentTo("Cena.cs", "Pecas/PecaC.cs");
        scope.Files.Single(f => f.FilePath == "Pecas/PecaC.cs").Reason.Should().Be(AiContextReason.Pinned);
    }

    [Fact]
    public async Task DependenciaExcluida_NaoEntraNemEhVarrida()
    {
        var docs = ContextTestScene.Documents();
        var features = await ContextTestScene.EvaluateAsync(docs);
        var adjustments = new AiContextAdjustments();
        adjustments.Exclude("Pecas/Medidas.cs");

        var scope = _builder.ResolveScope(Request(docs, features.Where(f => f.Name == "b_1"), adjustments: adjustments));

        Paths(scope).Should().BeEquivalentTo("Cena.cs", "Pecas/PecaB.cs");
    }

    [Fact]
    public async Task Build_EnviaSoOsArquivosDoEscopoEAssinaturaDasDemaisPecas()
    {
        var docs = ContextTestScene.Documents();
        var features = await ContextTestScene.EvaluateAsync(docs);

        var context = _builder.Build(Request(docs, features.Where(f => f.Name == "a_1")));

        context.IsMultiFile.Should().BeTrue();
        context.Scope!.Documents.Select(d => d.FilePath).Should().BeEquivalentTo("Cena.cs", "Pecas/PecaA.cs");
        context.PartSummaries.Should().BeEquivalentTo("PecaB(double Largura = 10)", "PecaC(double Largura = 10)");
        context.ProjectDocuments.Should().HaveCount(docs.Count, "a resposta é validada com a cena inteira");
        context.Scope.SelectedFeatures.Should().ContainSingle().Which.Should().Be("a_1 (peça PecaA) — Cena.cs, linha 1");
        context.Kind.Should().Be(CadFileKind.SceneEntry);
        context.KindOf("Pecas/PecaA.cs").Should().Be(CadFileKind.PartFile);
        context.KindOf("Pecas/Medidas.cs").Should().Be(CadFileKind.SupportFile);
        context.KindOf("Pecas/Nova.cs").Should().Be(CadFileKind.PartFile, "arquivo novo só pode ser peça");
    }

    [Fact]
    public void SemProjeto_ModoDeArquivoUnico()
    {
        var request = new AiContextRequest(
            new[] { new ScriptDocument("Cena.cs", "AddFeature(x);") }, "Cena.cs", null, Array.Empty<IFeature>(), IsProjectOpen: false);

        var context = _builder.Build(request);

        context.IsMultiFile.Should().BeFalse();
        context.CurrentCode.Should().Be("AddFeature(x);");
        context.Kind.Should().Be(CadFileKind.StandaloneScript);
        _builder.ResolveScope(request).Files.Should().BeEmpty();
    }
}
