using AvaloniaCadEditor.Application.Ai;
using FluentAssertions;
using Xunit;

namespace AvaloniaCadEditor.Tests.Ai;

public class CadScriptValidatorTests
{
    [Fact]
    public void Validate_ScriptValido_RetornaValido()
    {
        string code = "var box_1 = new BoxFeature(baseCorner: new Point3D(0,0,0), width: 1, length: 1, height: 1) { Name = \"box_1\" };\nAddFeature(box_1);";

        var result = CadScriptValidator.Validate(code);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_ScriptVazio_RetornaInvalido()
    {
        var result = CadScriptValidator.Validate("");

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_ComUsing_RetornaInvalido()
    {
        string code = "using System;\nvar box_1 = new BoxFeature(baseCorner: new Point3D(0,0,0), width: 1, length: 1, height: 1) { Name = \"box_1\" };\nAddFeature(box_1);";

        var result = CadScriptValidator.Validate(code);

        result.IsValid.Should().BeFalse();
        result.Error.Should().Contain("using");
    }

    [Fact]
    public void Validate_ComDeclaracaoDeClasse_RetornaInvalido()
    {
        string code = "class Hack { }\nAddFeature(null);";

        var result = CadScriptValidator.Validate(code);

        result.IsValid.Should().BeFalse();
        result.Error.Should().Contain("class");
    }

    [Fact]
    public void Validate_SemAddFeature_RetornaInvalido()
    {
        string code = "var box_1 = new BoxFeature(baseCorner: new Point3D(0,0,0), width: 1, length: 1, height: 1) { Name = \"box_1\" };";

        var result = CadScriptValidator.Validate(code);

        result.IsValid.Should().BeFalse();
        result.Error.Should().Contain("AddFeature");
    }

    [Fact]
    public void Validate_ComErroDeSintaxe_RetornaInvalido()
    {
        string code = "var box_1 = new BoxFeature(;";

        var result = CadScriptValidator.Validate(code);

        result.IsValid.Should().BeFalse();
    }

    private const string ValidPartCode = """
        public sealed record Placa(double Largura = 50, double Comprimento = 30, double Espessura = 5) : Part
        {
            protected override IFeature Build() =>
                new BoxFeature(Point3D.Origin, Largura, Comprimento, Espessura) { Name = "placa" };
        }
        """;

    [Fact]
    public void Validate_PecaValida_RetornaValido()
    {
        var result = CadScriptValidator.Validate(ValidPartCode, CadFileKind.PartFile);

        result.IsValid.Should().BeTrue(because: result.Error ?? string.Empty);
    }

    [Fact]
    public void Validate_PecaComAddFeatureDentroDoBuild_RetornaInvalido()
    {
        string code = """
            public sealed record Placa(double Largura = 50) : Part
            {
                protected override IFeature Build()
                {
                    var b = new BoxFeature(Point3D.Origin, Largura, 10, 5) { Name = "placa" };
                    AddFeature(b);
                    return b;
                }
            }
            """;

        var result = CadScriptValidator.Validate(code, CadFileKind.PartFile);

        result.IsValid.Should().BeFalse();
        result.Error.Should().Contain("AddFeature");
    }

    [Fact]
    public void Validate_PecaComStatementSolto_RetornaInvalido()
    {
        string code = "var box_1 = new BoxFeature(baseCorner: new Point3D(0,0,0), width: 1, length: 1, height: 1) { Name = \"box_1\" };\nAddFeature(box_1);";

        var result = CadScriptValidator.Validate(code, CadFileKind.PartFile);

        result.IsValid.Should().BeFalse();
        result.Error.Should().Contain("arquivo de entrada");
    }

    [Fact]
    public void Validate_PecaSemBuild_RetornaInvalido()
    {
        string code = "public sealed record Placa(double Largura = 50) : Part { }";

        var result = CadScriptValidator.Validate(code, CadFileKind.PartFile);

        result.IsValid.Should().BeFalse();
        result.Error.Should().Contain("Build");
    }

    [Fact]
    public void Validate_PecaComDuasDeclaracoes_RetornaInvalido()
    {
        string code = ValidPartCode + "\npublic sealed record Placa2(double Largura = 50) : Part\n{\n    protected override IFeature Build() => new BoxFeature(Point3D.Origin, Largura, 10, 5) { Name = \"placa2\" };\n}";

        var result = CadScriptValidator.Validate(code, CadFileKind.PartFile);

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_ScriptComRecord_ContinuaInvalidoNoFormatoScript()
    {
        var result = CadScriptValidator.Validate(ValidPartCode, CadFileKind.StandaloneScript);

        result.IsValid.Should().BeFalse();
        result.Error.Should().Contain("record");
    }
}
