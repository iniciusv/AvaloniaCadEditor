using System;
using System.Linq;
using System.Threading.Tasks;
using AiCore.Models;
using AiCore.Services;
using AvaloniaCadEditor.Application.Ai;
using AvaloniaCadEditor.Application.Interfaces;
using AvaloniaCadEditor.Domain.Settings;
using AvaloniaCadEditor.Infrastructure.Scripting;
using FluentAssertions;
using Xunit;

namespace AvaloniaCadEditor.Tests.Ai;

public class CadScriptChatSessionTests
{
    private static LlmOptions FakeOptions() => new() { ApiKey = "fake-key" };

    private const string ValidScript =
        "var box_1 = new BoxFeature(baseCorner: new Point3D(0,0,0), width: 1, length: 1, height: 1) { Name = \"box_1\" };\nAddFeature(box_1);";

    private const string InvalidScript_SemAddFeature =
        "var box_1 = new BoxFeature(baseCorner: new Point3D(0,0,0), width: 1, length: 1, height: 1) { Name = \"box_1\" };";

    [Fact]
    public async Task SendAsync_QuandoPrimeiraRespostaInvalida_CorrigeAutomaticamenteERetornaSucesso()
    {
        int callCount = 0;
        var session = new CadScriptChatSession(
            FakeOptions(),
            new CadScriptProtocol(),
            new RoslynSceneEvaluator(),
            send: (messages, options, ct) =>
            {
                callCount++;
                string content = callCount == 1
                    ? $"Tentando.\n```cadscript\n{InvalidScript_SemAddFeature}\n```"
                    : $"Corrigido.\n```cadscript\n{ValidScript}\n```";
                return Task.FromResult(new LlmResult { Content = content, ReasoningContent = "raciocínio" });
            });

        var result = await session.SendAsync("Crie uma caixa", new CadScriptContext(""));

        result.IsValid.Should().BeTrue();
        result.Attempts.Should().Be(2);
        result.FeatureCount.Should().Be(1);
        callCount.Should().Be(2);
    }

    [Fact]
    public async Task SendAsync_QuandoTodasAsTentativasFalham_RetornaFalhaAposLimiteDeTentativas()
    {
        int callCount = 0;
        var session = new CadScriptChatSession(
            FakeOptions(),
            new CadScriptProtocol(),
            new RoslynSceneEvaluator(),
            send: (messages, options, ct) =>
            {
                callCount++;
                return Task.FromResult(new LlmResult { Content = $"```cadscript\n{InvalidScript_SemAddFeature}\n```" });
            });

        var result = await session.SendAsync("Crie uma caixa", new CadScriptContext(""));

        result.IsValid.Should().BeFalse();
        result.Attempts.Should().Be(CadScriptChatSession.MaxAttempts);
        result.Error.Should().NotBeNullOrWhiteSpace();
        callCount.Should().Be(CadScriptChatSession.MaxAttempts);
    }

    private const string ValidPart =
        "public sealed record Placa(double Largura = 50, double Comprimento = 30, double Espessura = 5) : Part\n{\n    protected override IFeature Build() => new BoxFeature(Point3D.Origin, Largura, Comprimento, Espessura) { Name = \"placa\" };\n}";

    [Fact]
    public async Task SendAsync_ComArquivoDePeca_ValidaExecutandoNumaCenaSintetica()
    {
        var session = new CadScriptChatSession(
            FakeOptions(),
            new CadScriptProtocol(),
            new RoslynSceneEvaluator(),
            send: (messages, options, ct) => Task.FromResult(new LlmResult { Content = $"Pronto.\n```cadscript\n{ValidPart}\n```" }));

        var context = new CadScriptContext("", Kind: CadFileKind.PartFile, FileName: "Pecas/Placa.cs");
        var result = await session.SendAsync("Crie uma placa", context);

        result.IsValid.Should().BeTrue(because: result.Error ?? string.Empty);
        result.FeatureCount.Should().Be(1);
    }

    [Fact]
    public async Task SendAsync_ComArquivoDePecaEScriptSolto_RejeitaEPermiteCorrecaoAutomatica()
    {
        // O mesmo engano relatado pelo usuário: pedir uma peça e receber de volta um script solto
        // (var ...; AddFeature(...)) em vez de um record : Part.
        const string EstiloScriptSolto =
            "var motor_corpo = new BoxFeature(baseCorner: new Point3D(0, 0, 0), width: 42.3, length: 42.3, height: 48) { Name = \"motor_corpo\" };\nAddFeature(motor_corpo);";

        int callCount = 0;
        var session = new CadScriptChatSession(
            FakeOptions(),
            new CadScriptProtocol(),
            new RoslynSceneEvaluator(),
            send: (messages, options, ct) =>
            {
                callCount++;
                string content = callCount == 1
                    ? $"Tentando.\n```cadscript\n{EstiloScriptSolto}\n```"
                    : $"Corrigido.\n```cadscript\n{ValidPart}\n```";
                return Task.FromResult(new LlmResult { Content = content });
            });

        var context = new CadScriptContext("", Kind: CadFileKind.PartFile, FileName: "Pecas/Nema17.cs");
        var result = await session.SendAsync("Crie um motor nema 17 com eixo e furos", context);

        result.IsValid.Should().BeTrue(because: result.Error ?? string.Empty);
        result.Attempts.Should().Be(2);
        callCount.Should().Be(2);
    }

    [Fact]
    public async Task SendAsync_NaoReenviaReasoningContentNoHistorico()
    {
        var session = new CadScriptChatSession(
            FakeOptions(),
            new CadScriptProtocol(),
            new RoslynSceneEvaluator(),
            send: (messages, options, ct) => Task.FromResult(new LlmResult
            {
                Content = $"```cadscript\n{ValidScript}\n```",
                ReasoningContent = "raciocínio interno, não deve voltar no histórico"
            }));

        await session.SendAsync("Crie uma caixa", new CadScriptContext(""));

        session.History
            .Where(m => m.Role == "assistant")
            .Should().OnlyContain(m => string.IsNullOrEmpty(m.ReasoningContent));
    }

    [Fact]
    public void NewConversation_LimpaHistoricoMasMantemSystemPrompt()
    {
        var session = new CadScriptChatSession(
            FakeOptions(),
            new CadScriptProtocol(),
            new RoslynSceneEvaluator(),
            send: (_, _, _) => Task.FromResult(new LlmResult()));

        session.NewConversation();

        session.History.Should().HaveCount(1);
        session.History[0].Role.Should().Be("system");
    }

    [Fact]
    public void LoadOptionsFromEnvironment_SemApiKey_RetornaErro()
    {
        string? previous = System.Environment.GetEnvironmentVariable("DEEPSEEK_API_KEY");
        try
        {
            System.Environment.SetEnvironmentVariable("DEEPSEEK_API_KEY", null);

            var (options, error) = CadScriptChatSession.LoadOptionsFromEnvironment();

            options.Should().BeNull();
            error.Should().NotBeNullOrWhiteSpace();
        }
        finally
        {
            System.Environment.SetEnvironmentVariable("DEEPSEEK_API_KEY", previous);
        }
    }

    private class FakeAiSettingsProvider : IAiSettingsProvider
    {
        private AiSettings _settings;
        public FakeAiSettingsProvider(AiSettings settings) => _settings = settings;
        public AiSettings GetSettings() => _settings;
        public void SaveSettings(AiSettings settings) => _settings = settings;
    }

    [Fact]
    public void LoadOptions_ComSettingsPersistidasPreenchidas_UsaAsSettings()
    {
        var provider = new FakeAiSettingsProvider(new AiSettings { ApiKey = "k1", Model = "m1", BaseUrl = "https://example.test/" });

        var (options, error) = CadScriptChatSession.LoadOptions(provider);

        error.Should().BeNull();
        options.Should().NotBeNull();
        options!.ApiKey.Should().Be("k1");
        options.Model.Should().Be("m1");
        options.BaseUrl.Should().Be("https://example.test/");
    }

    [Fact]
    public void LoadOptions_SettingsVaziasComEnvVarDefinida_UsaEnvVarComoFallback()
    {
        string? previous = Environment.GetEnvironmentVariable("DEEPSEEK_API_KEY");
        try
        {
            Environment.SetEnvironmentVariable("DEEPSEEK_API_KEY", "env-key");
            var provider = new FakeAiSettingsProvider(new AiSettings());

            var (options, error) = CadScriptChatSession.LoadOptions(provider);

            error.Should().BeNull();
            options!.ApiKey.Should().Be("env-key");
        }
        finally
        {
            Environment.SetEnvironmentVariable("DEEPSEEK_API_KEY", previous);
        }
    }

    [Fact]
    public void LoadOptions_SemSettingsNemEnvVar_RetornaErro()
    {
        string? previous = Environment.GetEnvironmentVariable("DEEPSEEK_API_KEY");
        try
        {
            Environment.SetEnvironmentVariable("DEEPSEEK_API_KEY", null);
            var provider = new FakeAiSettingsProvider(new AiSettings());

            var (options, error) = CadScriptChatSession.LoadOptions(provider);

            options.Should().BeNull();
            error.Should().NotBeNullOrWhiteSpace();
        }
        finally
        {
            Environment.SetEnvironmentVariable("DEEPSEEK_API_KEY", previous);
        }
    }
}
