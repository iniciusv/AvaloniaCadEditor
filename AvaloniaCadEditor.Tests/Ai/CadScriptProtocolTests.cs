using AvaloniaCadEditor.Application.Ai;
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
        result.Payload.Should().Contain("AddFeature(box_a1)");
        result.ChatMessage.Should().Contain("Adicionei uma caixa.");
        result.ChatMessage.Should().NotContain("```");
    }

    [Fact]
    public void ParseResponse_ComBlocoCsharpFallback_ExtraiCodigo()
    {
        string raw = "Ok.\n```csharp\nvar x_1 = new BoxFeature(baseCorner: new Point3D(0,0,0), width: 1, length: 1, height: 1) { Name = \"x_1\" };\nAddFeature(x_1);\n```";

        var result = _protocol.ParseResponse(raw);

        result.HasPayload.Should().BeTrue();
        result.Payload.Should().Contain("AddFeature(x_1)");
    }

    [Fact]
    public void ParseResponse_ComMultiplosBlocos_PegaOUltimo()
    {
        string raw =
            "```cadscript\n// versão antiga, ignore\n```\n" +
            "Texto no meio.\n" +
            "```cadscript\nvar final_1 = new BoxFeature(baseCorner: new Point3D(0,0,0), width: 1, length: 1, height: 1) { Name = \"final_1\" };\nAddFeature(final_1);\n```";

        var result = _protocol.ParseResponse(raw);

        result.Payload.Should().Contain("final_1");
        result.Payload.Should().NotContain("versão antiga");
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
}
