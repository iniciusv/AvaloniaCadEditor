using System;
using System.Collections.Generic;
using System.IO;
using AvaloniaCadEditor.Application.Ai;
using AvaloniaCadEditor.Application.Ai.Recording;
using AvaloniaCadEditor.Application.Interfaces;
using FluentAssertions;
using Xunit;

namespace AvaloniaCadEditor.Tests.Ai;

public class AiConversationRecordTests
{
    private static CadScriptContext MultiFileContext()
    {
        var docs = new[]
        {
            new ScriptDocument("Cena.cs", "var placa_1 = Add(new Placa());"),
            new ScriptDocument("Pecas/Placa.cs", "public sealed record Placa(double Largura = 100) : Part { }")
        };
        return new CadScriptContext(
            docs[0].Text,
            "placa_1",
            ErrorFeedback: null,
            CadFileKind.SceneEntry,
            "Cena.cs",
            PartSummaries: new[] { "Suporte(double Altura = 40)" },
            ProjectDocuments: docs,
            Scope: new MultiFileScope(
                docs,
                new Dictionary<string, CadFileKind> { ["Cena.cs"] = CadFileKind.SceneEntry, ["Pecas/Placa.cs"] = CadFileKind.PartFile },
                new[] { "placa_1 (peça Placa) — Cena.cs, linha 1" }));
    }

    private static CadScriptTurnResult Result(string code) => new()
    {
        ChatText = "Feito.",
        Changes = new[] { new ScriptDocument("Cena.cs", code) },
        IsValid = true,
        Attempts = 2,
        PromptTokens = 100,
        CompletionTokens = 20
    };

    /// <summary>O replay só funciona se o contexto voltar do JSON gerando exatamente a mesma mensagem.</summary>
    [Fact]
    public void ContextoMultiArquivo_IdaEVoltaPeloJson_GeraAMesmaMensagemDeUsuario()
    {
        var context = MultiFileContext();
        var recorder = new AiConversationRecorder();
        recorder.Record(0, "mais uma placa", context, "enviada", Result("x"), "bruta");

        string json = AiConversationRecordStore.Serialize(recorder.ToRecord("deepseek-flash", "enxuto", DateTimeOffset.Now));
        var loaded = AiConversationRecordStore.Deserialize(json);

        var protocol = new CadScriptProtocol();
        var restored = loaded.Rounds[0].Context.ToContext();
        protocol.BuildUserMessage(restored, "mais uma placa").Should().Be(protocol.BuildUserMessage(context, "mais uma placa"));
        restored.KindOf("PECAS/PLACA.CS").Should().Be(CadFileKind.PartFile, "os caminhos continuam sem diferenciar maiúsculas");

        loaded.ModelId.Should().Be("deepseek-flash");
        loaded.ProfileId.Should().Be("enxuto");
        loaded.Rounds[0].Response.RawContent.Should().Be("bruta");
        loaded.Rounds[0].Response.Changes.Should().Equal(new ScriptDocument("Cena.cs", "x"));
        loaded.Rounds[0].Response.Attempts.Should().Be(2);
    }

    [Fact]
    public void ContextoDeArquivoUnico_IdaEVolta_PreservaCodigoEFormato()
    {
        var context = new CadScriptContext("var a = 1;", Kind: CadFileKind.StandaloneScript, FileName: "Cena.cs");

        var restored = RecordedContext.From(context).ToContext();

        restored.Should().BeEquivalentTo(context);
        restored.IsMultiFile.Should().BeFalse();
    }

    [Fact]
    public void Recorder_AcompanhaOChat_TurnoApagadoSaiENovoChatLimpa()
    {
        var recorder = new AiConversationRecorder();
        var context = MultiFileContext();
        recorder.Record(0, "primeiro", context, "", Result("a"), "");
        recorder.Record(1, "segundo", context, "", Result("b"), "");
        recorder.Record(2, "terceiro", context, "", Result("c"), "");

        recorder.Remove(1);
        recorder.ToRecord("", "", DateTimeOffset.Now).Rounds.Should().HaveCount(2)
            .And.SatisfyRespectively(
                r => r.Instruction.Should().Be("primeiro"),
                r => r.Instruction.Should().Be("terceiro"));

        recorder.Clear();
        recorder.Count.Should().Be(0);
    }

    [Fact]
    public void Save_NomeiaPeloPrimeiroPedido_ECarregaDeVolta()
    {
        string folder = Path.Combine(Path.GetTempPath(), "ai-cases-" + Guid.NewGuid().ToString("N"));
        try
        {
            var recorder = new AiConversationRecorder();
            recorder.Record(0, "Crie um suporte em L, com 2 furos!", MultiFileContext(), "", Result("a"), "");
            var record = recorder.ToRecord("m", "atual", new DateTimeOffset(2026, 10, 11, 14, 30, 5, TimeSpan.Zero));

            string path = AiConversationRecordStore.Save(record, folder);

            Path.GetFileName(path).Should().Be("20261011-143005-crie-um-suporte-em-l-com-2-furos.json");
            File.ReadAllText(path).Should().Contain("Crie um suporte em L", "o JSON fica legível, sem escapar acentos e código");
            AiConversationRecordStore.Load(path).Rounds.Should().ContainSingle();
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
    }
}
