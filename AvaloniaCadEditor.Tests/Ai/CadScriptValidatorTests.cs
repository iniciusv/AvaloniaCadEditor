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
}
