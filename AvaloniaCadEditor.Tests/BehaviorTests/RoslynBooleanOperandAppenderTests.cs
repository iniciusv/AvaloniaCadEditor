using AvaloniaCadEditor.Application.Services;
using FluentAssertions;
using Xunit;

namespace AvaloniaCadEditor.Tests.BehaviorTests;

public class RoslynBooleanOperandAppenderTests
{
    [Fact]
    public void FormaComArgumentosNomeados_ArrayExplicito_AcrescentaFerramenta()
    {
        var script =
            "var box_1 = new BoxFeature(new Point3D(0,0,0), 20, 20, 20);\r\n" +
            "AddFeature(box_1);\r\n" +
            "var a = new BoxFeature(new Point3D(2,2,-1), 4, 4, 22);\r\n" +
            "AddFeature(a);\r\n" +
            "var boolOp_1 = new BooleanFeature(name: \"boolOp_1\", operation: BooleanOperationType.Subtract, baseFeature: box_1, toolFeatures: new IFeature[] { a });\r\n" +
            "AddFeature(boolOp_1);\r\n";

        bool ok = RoslynBooleanOperandAppender.TryAppendTool(script, "boolOp_1", "b", out var result);

        ok.Should().BeTrue();
        result.Should().Contain("toolFeatures: new IFeature[] { a, b }");
        // O resto do script continua intacto.
        result.Should().Contain("AddFeature(box_1);");
        result.Should().Contain("AddFeature(boolOp_1);");
    }

    [Fact]
    public void FormaComArrayImplicito_AcrescentaFerramenta()
    {
        var script =
            "var boolOp_1 = new BooleanFeature(name: \"boolOp_1\", operation: BooleanOperationType.Subtract, baseFeature: box_1, toolFeatures: new[] { a });\r\n" +
            "AddFeature(boolOp_1);\r\n";

        bool ok = RoslynBooleanOperandAppender.TryAppendTool(script, "boolOp_1", "b", out var result);

        ok.Should().BeTrue();
        result.Should().Contain("toolFeatures: new[] { a, b }");
    }

    [Fact]
    public void FormaPosicionalSemNomesDeArgumento_AcrescentaFerramenta()
    {
        var script =
            "var boolOp_1 = new BooleanFeature(\"boolOp_1\", BooleanOperationType.Subtract, box_1, new IFeature[] { a });\r\n" +
            "AddFeature(boolOp_1);\r\n";

        bool ok = RoslynBooleanOperandAppender.TryAppendTool(script, "boolOp_1", "b", out var result);

        ok.Should().BeTrue();
        result.Should().Contain("new IFeature[] { a, b }");
    }

    [Fact]
    public void FormaDeFabrica_CreateDifference_AcrescentaArgumentoNoFinal()
    {
        var script =
            "var boolOp_1 = BooleanFeature.CreateDifference(\"boolOp_1\", box_1, a);\r\n" +
            "AddFeature(boolOp_1);\r\n";

        bool ok = RoslynBooleanOperandAppender.TryAppendTool(script, "boolOp_1", "b", out var result);

        ok.Should().BeTrue();
        result.Should().Contain("BooleanFeature.CreateDifference(\"boolOp_1\", box_1, a, b)");
    }

    [Fact]
    public void FormaDeFabrica_CreateUnion_ComVariasFerramentas_AcrescentaNoFinal()
    {
        var script = "var boolOp_1 = BooleanFeature.CreateUnion(\"boolOp_1\", box_1, a, b);\r\n";

        bool ok = RoslynBooleanOperandAppender.TryAppendTool(script, "boolOp_1", "c", out var result);

        ok.Should().BeTrue();
        result.Should().Contain("BooleanFeature.CreateUnion(\"boolOp_1\", box_1, a, b, c)");
    }

    [Fact]
    public void VariavelInexistente_DevolveFalseENaoAlteraOScript()
    {
        var script = "var boolOp_1 = BooleanFeature.CreateDifference(\"boolOp_1\", box_1, a);\r\n";

        bool ok = RoslynBooleanOperandAppender.TryAppendTool(script, "boolOp_nao_existe", "b", out var result);

        ok.Should().BeFalse();
        result.Should().Be(script);
    }

    [Fact]
    public void FormatoNaoReconhecido_ToolFeaturesComoVariavel_DevolveFalseENaoAlteraOScript()
    {
        // toolFeatures não é um array literal (é uma variável): não sabemos reescrever com segurança.
        var script =
            "var tools = new IFeature[] { a };\r\n" +
            "var boolOp_1 = new BooleanFeature(name: \"boolOp_1\", operation: BooleanOperationType.Subtract, baseFeature: box_1, toolFeatures: tools);\r\n";

        bool ok = RoslynBooleanOperandAppender.TryAppendTool(script, "boolOp_1", "b", out var result);

        ok.Should().BeFalse();
        result.Should().Be(script);
    }

    [Fact]
    public void ScriptVazio_DevolveFalse()
    {
        bool ok = RoslynBooleanOperandAppender.TryAppendTool("", "boolOp_1", "b", out var result);

        ok.Should().BeFalse();
        result.Should().Be("");
    }

    [Fact]
    public void TryAppendTools_AcrescentaVariasFerramentasDeUmaVez()
    {
        var script =
            "var boolOp_1 = new BooleanFeature(name: \"boolOp_1\", operation: BooleanOperationType.Subtract, baseFeature: box_1, toolFeatures: new IFeature[] { a });\r\n" +
            "AddFeature(boolOp_1);\r\n";

        bool ok = RoslynBooleanOperandAppender.TryAppendTools(script, "boolOp_1", new[] { "b", "c" }, out var result);

        ok.Should().BeTrue();
        result.Should().Contain("toolFeatures: new IFeature[] { a, b, c }");
    }

    [Fact]
    public void TryAppendTools_SemFerramentas_DevolveFalse()
    {
        var script = "var boolOp_1 = BooleanFeature.CreateDifference(\"boolOp_1\", box_1, a);\r\n";

        bool ok = RoslynBooleanOperandAppender.TryAppendTools(script, "boolOp_1", System.Array.Empty<string>(), out var result);

        ok.Should().BeFalse();
        result.Should().Be(script);
    }

    [Fact]
    public void TryAppendTools_QuandoFalha_DevolveOScriptOriginalIntacto()
    {
        // A variável da BooleanFeature não existe: nada é aplicado, nem parcialmente - "b" e "c"
        // nunca chegam a ser inseridos, e o script devolvido é exatamente o original.
        var script = "var boolOp_1 = BooleanFeature.CreateDifference(\"boolOp_1\", box_1, a);\r\n";

        bool ok = RoslynBooleanOperandAppender.TryAppendTools(script, "boolOp_nao_existe", new[] { "b", "c" }, out var result);

        ok.Should().BeFalse();
        result.Should().Be(script);
    }
}
