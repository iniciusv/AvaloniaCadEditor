using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AiCore.Models;
using AiCore.Services;
using AvaloniaCadEditor.Application.Ai;
using AvaloniaCadEditor.Application.Interfaces;
using AvaloniaCadEditor.Infrastructure.Scripting;
using FluentAssertions;
using Xunit;

namespace AvaloniaCadEditor.Tests.Ai;

/// <summary>Sessão no modo multi-arquivo: a IA recebe vários arquivos e pode alterar vários deles.</summary>
public class CadScriptChatSessionMultiFileTests
{
    private const string Cena = "var placa_1 = Add(new Placa());\n";

    private const string Placa =
        "public sealed record Placa(double Largura = 50) : Part\n{\n    protected override IFeature Build() => new BoxFeature(Point3D.Origin, Largura, 30, 5) { Name = \"placa\" };\n}\n";

    private const string Outra =
        "public sealed record Outra(double Largura = 10) : Part\n{\n    protected override IFeature Build() => new BoxFeature(Point3D.Origin, Largura, 10, 10) { Name = \"outra\" };\n}\n";

    private static CadScriptContext Context()
    {
        var all = new[]
        {
            new ScriptDocument("Cena.cs", Cena),
            new ScriptDocument("Pecas/Placa.cs", Placa),
            new ScriptDocument("Pecas/Outra.cs", Outra),
        };
        return new CadScriptContext(
            Cena,
            Kind: CadFileKind.SceneEntry,
            FileName: "Cena.cs",
            ProjectDocuments: all,
            Scope: new MultiFileScope(
                all.Take(2).ToList(),
                new Dictionary<string, CadFileKind>(System.StringComparer.OrdinalIgnoreCase)
                {
                    ["Cena.cs"] = CadFileKind.SceneEntry,
                    ["Pecas/Placa.cs"] = CadFileKind.PartFile,
                    ["Pecas/Outra.cs"] = CadFileKind.PartFile,
                },
                System.Array.Empty<string>()));
    }

    private static CadScriptChatSession Session(System.Func<int, string> reply)
    {
        int call = 0;
        return new CadScriptChatSession(
            new LlmOptions { ApiKey = "fake" },
            new CadScriptProtocol(),
            new RoslynSceneEvaluator(),
            send: (_, _, _) => Task.FromResult(new LlmResult { Content = reply(++call) }));
    }

    [Fact]
    public async Task RespostaQueAlteraPecaECena_ValidaOsDoisJuntos()
    {
        string novaPlaca = Placa.Replace("Largura = 50", "Largura = 80");
        string novaCena = "var placa_1 = Add(new Placa());\nvar placa_2 = Add(new Placa(), at: Placement.At(0, 100, 0));\n";
        var session = Session(_ =>
            $"Pronto.\n```cadscript file=Pecas/Placa.cs\n{novaPlaca}```\n```cadscript file=Cena.cs\n{novaCena}```");

        var result = await session.SendAsync("Aumente a placa e duplique", Context());

        result.IsValid.Should().BeTrue(because: result.Error ?? string.Empty);
        result.Changes.Select(c => c.FilePath).Should().BeEquivalentTo("Pecas/Placa.cs", "Cena.cs");
        result.FeatureCount.Should().Be(2);
    }

    [Fact]
    public async Task ArquivoForaDoContexto_GeraRoundDeCorrecao()
    {
        string outraAlterada = Outra.Replace("Largura = 10", "Largura = 20");
        string novaPlaca = Placa.Replace("Largura = 50", "Largura = 80");
        var session = Session(call => call == 1
            ? $"```cadscript file=Pecas/Outra.cs\n{outraAlterada}```"
            : $"```cadscript file=Pecas/Placa.cs\n{novaPlaca}```");

        var result = await session.SendAsync("Aumente a placa", Context());

        result.IsValid.Should().BeTrue(because: result.Error ?? string.Empty);
        result.Attempts.Should().Be(2);
        session.History.Last(m => m.Role == "user").Content.Should().Contain("Pecas/Outra.cs não estão no contexto");
    }

    [Fact]
    public async Task PecaNovaEmPecas_EhPermitida()
    {
        const string Suporte =
            "public sealed record Suporte(double Altura = 20) : Part\n{\n    protected override IFeature Build() => new BoxFeature(Point3D.Origin, 10, 10, Altura) { Name = \"suporte\" };\n}\n";
        string novaCena = Cena + "var suporte_1 = Add(new Suporte(), at: Placement.At(0, 50, 0));\n";
        var session = Session(_ =>
            $"```cadscript file=Pecas/Suporte.cs\n{Suporte}```\n```cadscript file=Cena.cs\n{novaCena}```");

        var result = await session.SendAsync("Crie um suporte", Context());

        result.IsValid.Should().BeTrue(because: result.Error ?? string.Empty);
        result.FeatureCount.Should().Be(2);
    }
}
