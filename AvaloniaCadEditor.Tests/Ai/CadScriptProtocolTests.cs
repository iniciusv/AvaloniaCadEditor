using System.Collections.Generic;
using System.Linq;
using AvaloniaCadEditor.Application.Ai;
using AvaloniaCadEditor.Application.Interfaces;
using FluentAssertions;
using Xunit;

namespace AvaloniaCadEditor.Tests.Ai;

public class CadScriptProtocolTests
{
    private readonly CadScriptProtocol _protocol = new();

    [Fact]
    public void ParseResponse_ComBlocoCadScript_ExtraiCodigoETexto()
    {
        string raw = "Adicionei uma caixa.\n```cadscript\nvar box_a1 = new BoxFeature(baseCorner: new Point3D(0,0,0), width: 10, length: 10, height: 10) { Name = \"box_a1\" };\nAddFeature(box_a1);\n```";

        var result = _protocol.ParseResponse(raw);

        result.HasPayload.Should().BeTrue();
        result.Payload!.Files.Single().Code.Should().Contain("AddFeature(box_a1)");
        result.ChatMessage.Should().Contain("Adicionei uma caixa.");
        result.ChatMessage.Should().NotContain("```");
    }

    [Fact]
    public void ParseResponse_ComBlocoCsharpFallback_ExtraiCodigo()
    {
        string raw = "Ok.\n```csharp\nvar x_1 = new BoxFeature(baseCorner: new Point3D(0,0,0), width: 1, length: 1, height: 1) { Name = \"x_1\" };\nAddFeature(x_1);\n```";

        var result = _protocol.ParseResponse(raw);

        result.HasPayload.Should().BeTrue();
        result.Payload!.Files.Single().Code.Should().Contain("AddFeature(x_1)");
    }

    [Fact]
    public void ParseResponse_ComMultiplosBlocos_PegaOUltimo()
    {
        string raw =
            "```cadscript\n// versão antiga, ignore\n```\n" +
            "Texto no meio.\n" +
            "```cadscript\nvar final_1 = new BoxFeature(baseCorner: new Point3D(0,0,0), width: 1, length: 1, height: 1) { Name = \"final_1\" };\nAddFeature(final_1);\n```";

        var result = _protocol.ParseResponse(raw);

        result.Payload!.Files.Single().Code.Should().Contain("final_1");
        result.Payload.Files.Single().Code.Should().NotContain("versão antiga");
    }

    [Fact]
    public void ParseResponse_SemBlocoDeCodigo_RetornaApenasTexto()
    {
        string raw = "O eixo Z sempre aponta para cima neste editor.";

        var result = _protocol.ParseResponse(raw);

        result.HasPayload.Should().BeFalse();
        result.Payload.Should().BeNull();
        result.ChatMessage.Should().Be(raw);
    }

    [Fact]
    public void BuildUserMessage_ContemScriptAtualEErro()
    {
        var context = new CadScriptContext(
            CurrentCode: "var box_1 = new BoxFeature(...);",
            SelectedFeatureName: "box_1",
            ErrorFeedback: "CS0103: nome não existe");

        string message = _protocol.BuildUserMessage(context, "Adicione um furo");

        message.Should().Contain("Adicione um furo");
        message.Should().Contain("var box_1 = new BoxFeature(...);");
        message.Should().Contain("box_1");
        message.Should().Contain("CS0103: nome não existe");
    }

    [Fact]
    public void GetSystemPrompt_ContemReferenciaDeApiEExemplos()
    {
        string prompt = _protocol.GetSystemPrompt();

        prompt.Should().Contain("BoxFeature");
        prompt.Should().Contain("AddFeature");
        prompt.Should().Contain("cadscript");
    }

    [Fact]
    public void GetSystemPrompt_ContemRegrasDosDoisFormatos()
    {
        string prompt = _protocol.GetSystemPrompt();

        prompt.Should().Contain("FORMATO \"script\"");
        prompt.Should().Contain("FORMATO \"peça\"");
        prompt.Should().Contain("Build()");
        prompt.Should().Contain(": Part");
    }

    [Fact]
    public void BuildUserMessage_ComArquivoDePeca_IndicaOFormatoEAsPecasDisponiveis()
    {
        var context = new CadScriptContext(
            CurrentCode: "",
            Kind: CadFileKind.PartFile,
            FileName: "Pecas/Nema17.cs",
            PartSummaries: new[] { "Placa(double Largura, double Comprimento, double Espessura = 5)" });

        string message = _protocol.BuildUserMessage(context, "Crie um motor nema 17");

        message.Should().Contain("Pecas/Nema17.cs");
        message.Should().Contain("peça");
        message.Should().Contain("Placa(double Largura, double Comprimento, double Espessura = 5)");
    }

    [Fact]
    public void BuildUserMessage_ComArquivoDeEntrada_IndicaFormatoScript()
    {
        var context = new CadScriptContext(CurrentCode: "", Kind: CadFileKind.SceneEntry, FileName: "Cena.cs");

        string message = _protocol.BuildUserMessage(context, "Adicione uma caixa");

        message.Should().Contain("Cena.cs");
        message.Should().Contain("script");
    }

    [Fact]
    public void ArquivoUnicoSemProjeto_NaoMandaRecusarPecaNova()
    {
        string prompt = _protocol.GetSystemPrompt();
        string message = _protocol.BuildUserMessage(new CadScriptContext(""), "Crie um conector de mangueira");

        prompt.Should().NotContain("pedindo para abrir ou criar o arquivo da peça");
        prompt.Should().Contain("Construa a geometria direto neste arquivo");
        message.Should().Contain("arquivo único, sem projeto");
    }

    [Fact]
    public void ParseResponse_ComVariosBlocosFile_ExtraiUmArquivoPorBloco()
    {
        string raw =
            "Aumentei a placa e reposicionei.\n" +
            "```cadscript file=Pecas/Placa.cs\npublic sealed record Placa(double Largura = 120) : Part { }\n```\n" +
            "```cadscript file=Cena.cs\nvar placa_1 = Add(new Placa());\n```";

        var result = _protocol.ParseResponse(raw);

        result.Payload!.Files.Select(f => f.FilePath).Should().Equal("Pecas/Placa.cs", "Cena.cs");
        result.Payload.Files[1].Code.Should().Be("var placa_1 = Add(new Placa());");
        result.ChatMessage.Should().Be("Aumentei a placa e reposicionei.");
    }

    [Fact]
    public void ParseResponse_BlocoSemFile_FicaSemCaminho()
    {
        var result = _protocol.ParseResponse("```cadscript\nAddFeature(x);\n```");

        result.Payload!.Files.Single().FilePath.Should().BeNull("bloco sem file= vale para o arquivo ativo");
    }

    [Fact]
    public void BuildUserMessage_MultiArquivo_EnviaCadaArquivoComCaminhoEFormato()
    {
        var context = new CadScriptContext(
            CurrentCode: "",
            Scope: new MultiFileScope(
                new[]
                {
                    new ScriptDocument("Cena.cs", "var a = Add(new Placa());"),
                    new ScriptDocument("Pecas/Placa.cs", "public sealed record Placa(double Largura = 100) : Part { protected override IFeature Build() => null!; }"),
                    new ScriptDocument("Pecas/Medidas.cs", "public static class Medidas { public const double E = 5; }"),
                },
                new Dictionary<string, CadFileKind>
                {
                    ["Cena.cs"] = CadFileKind.SceneEntry,
                    ["Pecas/Placa.cs"] = CadFileKind.PartFile,
                    ["Pecas/Medidas.cs"] = CadFileKind.SupportFile,
                },
                new[] { "Placa_1 (peça Placa) — Cena.cs, linha 1" }));

        string message = _protocol.BuildUserMessage(context, "Aumente a placa");

        message.Should().Contain("### ARQUIVOS NO CONTEXTO");
        message.Should().Contain("```cadscript file=Pecas/Placa.cs");
        message.Should().Contain("Cena.cs — formato: script");
        message.Should().Contain("Pecas/Placa.cs — formato: peça");
        message.Should().Contain("Pecas/Medidas.cs — formato: auxiliar");
        message.Should().Contain("Placa_1 (peça Placa)");
        message.Should().NotContain("### ARQUIVO ATIVO");
    }
}
