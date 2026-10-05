using AvaloniaCadEditor.Application.Ai;
using FluentAssertions;
using Xunit;

namespace AvaloniaCadEditor.Tests.Ai;

public class CadScriptApiReferenceTests
{
    [Fact]
    public void Build_ContemConstrutoresDasFeaturesPrincipais()
    {
        string reference = CadScriptApiReference.Build();

        reference.Should().Contain("new BoxFeature(");
        reference.Should().Contain("new CylinderFeature(");
        reference.Should().Contain("new ConeFeature(");
        reference.Should().Contain("new PrismFeature(");
        reference.Should().Contain("new SweepFeature(");
        reference.Should().Contain("new FilletFeature(");
    }

    [Fact]
    public void Build_ContemFactoryMethodsEstaticosDoBooleanFeature()
    {
        string reference = CadScriptApiReference.Build();

        reference.Should().Contain("BooleanFeature.CreateUnion");
        reference.Should().Contain("BooleanFeature.CreateDifference");
        reference.Should().Contain("BooleanFeature.CreateIntersection");
    }

    [Fact]
    public void Build_ContemEnumsEPropriedadesComuns()
    {
        string reference = CadScriptApiReference.Build();

        reference.Should().Contain("enum FaceType");
        reference.Should().Contain("enum BooleanOperationType");
        reference.Should().Contain("enum FilletMode");
        reference.Should().Contain("IsTransientTool");
        reference.Should().Contain("AddFeature(IFeature feature)");
    }
}
