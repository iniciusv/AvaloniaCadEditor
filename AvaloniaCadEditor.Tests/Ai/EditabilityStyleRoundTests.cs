using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AiCore.Models;
using AiCore.Services;
using AvaloniaCadEditor.Application.Ai;
using AvaloniaCadEditor.Application.Ai.Prompts;
using AvaloniaCadEditor.Application.Interfaces;
using AvaloniaCadEditor.Infrastructure.Scripting;
using FluentAssertions;
using Xunit;

namespace AvaloniaCadEditor.Tests.Ai;

/// <summary>
/// O que muda na sessão de chat com o linter de editabilidade: a regra dos sólidos rastreáveis derruba a
/// resposta em qualquer perfil (auto-fix), e o perfil "editavel" devolve os avisos de estilo à IA uma vez.
/// </summary>
public class EditabilityStyleRoundTests
{
    private static LlmOptions FakeOptions() => new() { ApiKey = "fake-key" };

    private const string LiteralScript = """
        var chapa_1 = new BoxFeature(Point3D.Origin, 80, 50, 6) { Name = "chapa_1" };
        AddFeature(chapa_1);
        var furo_1 = new CylinderFeature(new Point3D(10, 10, 0), Vector3D.UnitZ, 2, 6) { Name = "furo_1", IsTransientTool = true };
        AddFeature(furo_1);
        """;

    private const string ComputedScript = """
        var largura = 80.0;
        var chapa_1 = new BoxFeature(Point3D.Origin, largura, 50, 6) { Name = "chapa_1" };
        AddFeature(chapa_1);
        var furo_1 = new CylinderFeature(new Point3D(largura / 8, 10, 0), Vector3D.UnitZ, 2, 6) { Name = "furo_1", IsTransientTool = true };
        AddFeature(furo_1);
        """;

    private const string LoopScript = """
        var chapa_1 = new BoxFeature(Point3D.Origin, 80, 50, 6) { Name = "chapa_1" };
        AddFeature(chapa_1);
        for (var i = 0; i < 2; i++)
        {
            var furo = new CylinderFeature(new Point3D(10 + i * 60, 10, 0), Vector3D.UnitZ, 2, 6) { Name = $"furo_{i}" };
            AddFeature(furo);
        }
        """;

    private static (CadScriptChatSession Session, List<string> Requests) Session(ICadScriptPromptProfile profile, params string[] replies)
    {
        var requests = new List<string>();
        int call = 0;
        var session = new CadScriptChatSession(FakeOptions(), new CadScriptProtocol(profile), new RoslynSceneEvaluator(),
            send: (messages, options, ct) =>
            {
                requests.Add(messages.Last().Content);
                string code = replies[System.Math.Min(call++, replies.Length - 1)];
                string content = code.StartsWith("TEXTO:") ? code[6..] : $"Feito.\n```cadscript\n{code}\n```";
                return Task.FromResult(new LlmResult { Content = content });
            });
        return (session, requests);
    }

    [Fact]
    public async Task SolidoEmLaco_VoltaParaAIACorrigir_EmQualquerPerfil()
    {
        var (session, requests) = Session(CadScriptPromptProfiles.Default, LoopScript, LiteralScript);

        var result = await session.SendAsync("Faça uma chapa com furos", new CadScriptContext(""));

        result.IsValid.Should().BeTrue();
        result.Attempts.Should().Be(2);
        requests[1].Should().Contain("Name literal").And.Contain("laço");
    }

    [Fact]
    public async Task PerfilAtual_NaoFazRodadaDeEstilo()
    {
        var (session, requests) = Session(CadScriptPromptProfiles.Default, ComputedScript);

        var result = await session.SendAsync("Faça uma chapa", new CadScriptContext(""));

        result.IsValid.Should().BeTrue();
        result.Code.Should().Contain("largura / 8");
        requests.Should().HaveCount(1);
    }

    [Fact]
    public async Task PerfilEditavel_DevolveOsAvisos_EAceitaAReescrita()
    {
        var (session, requests) = Session(new EditablePromptProfile(), ComputedScript, LiteralScript);

        var result = await session.SendAsync("Faça uma chapa", new CadScriptContext(""));

        result.IsValid.Should().BeTrue();
        result.Code.Should().Be(LiteralScript);
        result.Attempts.Should().Be(2);
        requests.Should().HaveCount(2);
        requests[1].Should().Contain("estilo editável à mão").And.Contain("largura / 8");
    }

    [Fact]
    public async Task PerfilEditavel_SeAReescritaFalha_FicaARespostaQueFuncionava()
    {
        const string broken = "var x = ;";
        var (session, _) = Session(new EditablePromptProfile(), ComputedScript, broken);

        var result = await session.SendAsync("Faça uma chapa", new CadScriptContext(""));

        result.IsValid.Should().BeTrue();
        result.Code.Should().Be(ComputedScript);
        session.History.Should().NotContain(m => m.Content.Contains("estilo editável à mão"),
            "a rodada de estilo que não serviu sai do histórico");
    }

    [Fact]
    public async Task PerfilEditavel_SeAIARecusaPorquePediramParametrico_FicaARespostaOriginal()
    {
        var (session, requests) = Session(new EditablePromptProfile(), ComputedScript, "TEXTO:O pedido pediu código paramétrico.");

        var result = await session.SendAsync("Faça uma chapa paramétrica", new CadScriptContext(""));

        result.IsValid.Should().BeTrue();
        result.Code.Should().Be(ComputedScript);
        requests.Should().HaveCount(2);
    }

    [Fact]
    public async Task PerfilEditavel_AvisoQueOArquivoJaTinha_NaoVoltaParaAIA()
    {
        string edited = ComputedScript + "\nvar furo_2 = new CylinderFeature(new Point3D(70, 10, 0), Vector3D.UnitZ, 2, 6) { Name = \"furo_2\", IsTransientTool = true };\nAddFeature(furo_2);";
        var (session, requests) = Session(new EditablePromptProfile(), edited);

        var result = await session.SendAsync("Acrescente um furo", new CadScriptContext(ComputedScript));

        result.IsValid.Should().BeTrue();
        requests.Should().HaveCount(1, "o largura / 8 já estava no arquivo; o furo novo está no estilo");
    }

    [Fact]
    public void NewWarnings_IgnoraALinha_EContaRepeticoes()
    {
        string original = "var a = 1.0;\nvar p_1 = new Point3D(a / 2, 0, 0);";
        string changed = "// comentário novo\nvar a = 1.0;\nvar p_1 = new Point3D(a / 2, 0, 0);\nvar p_2 = new Point3D(a / 2, 0, 0);";

        EditabilityStyleFeedback.NewWarnings(original, changed).Should().ContainSingle()
            .Which.Line.Should().Be(4);
    }
}
