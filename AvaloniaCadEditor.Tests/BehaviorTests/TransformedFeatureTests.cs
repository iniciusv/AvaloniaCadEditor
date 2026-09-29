using System;
using AvaloniaCadEditor.Domain.Features;
using AvaloniaCadEditor.Domain.ValueObjects;
using FluentAssertions;
using Xunit;

namespace AvaloniaCadEditor.Tests.BehaviorTests;

/// <summary>
/// TransformedFeature/Transformed(): copiar, girar e mover uma feature solta (AddFeature, não
/// Add(...)) sem reescrever suas coordenadas originais.
/// </summary>
public class TransformedFeatureTests
{
    [Fact]
    public void GetVertices_SaiTransformadoPeloPlacement()
    {
        var box = new BoxFeature(Point3D.Origin, width: 10, length: 10, height: 10);
        var moved = box.Transformed(Placement.Translate(new Vector3D(5, 0, 0)));

        moved.GetVertices()[0].Should().Be(new Point3D(5, 0, 0));
        // A fonte não é modificada.
        box.GetVertices()[0].Should().Be(Point3D.Origin);
    }

    [Fact]
    public void Transformed_DeUmTransformed_Achata()
    {
        var box = new BoxFeature(Point3D.Origin, width: 10, length: 10, height: 10);
        var once = box.Transformed(Placement.Translate(new Vector3D(5, 0, 0)));
        var twice = once.Transformed(Placement.Translate(new Vector3D(0, 3, 0)));

        twice.Should().BeOfType<TransformedFeature>();
        var t = (TransformedFeature)twice;
        t.Source.Should().BeSameAs(box, "achatar não deve embrulhar um Transformed dentro do outro");
        t.GetVertices()[0].Should().Be(new Point3D(5, 3, 0));
    }

    [Fact]
    public void Transformed_MantemONomeDaFonteQuandoNaoInformado()
    {
        var box = new BoxFeature(Point3D.Origin, 10, 10, 10) { Name = "caixa" };
        var moved = box.Transformed(Placement.Translate(new Vector3D(1, 0, 0)));

        moved.Name.Should().Be("caixa");
    }

    [Fact]
    public void Transformed_ComNome_UsaONomeDado()
    {
        var box = new BoxFeature(Point3D.Origin, 10, 10, 10) { Name = "caixa" };
        var copy = box.Transformed(Placement.Translate(new Vector3D(1, 0, 0)), name: "caixa_copia1");

        copy.Name.Should().Be("caixa_copia1");
    }

    [Fact]
    public void BooleanFeature_AceitaTransformedGiradoSemFillet()
    {
        var box = new BoxFeature(Point3D.Origin, 10, 10, 10);
        var girada = box.Transformed(Placement.RotationAbout(Point3D.Origin, Vector3D.UnitZ, 45));
        var ferramenta = new BoxFeature(new Point3D(-100, -100, -100), 5, 5, 5);

        Action act = () => BooleanFeature.CreateDifference("corte", girada, ferramenta);

        act.Should().NotThrow();
    }

    [Fact]
    public void BooleanFeature_RecusaTransformedGiradoComFillet()
    {
        var box = new BoxFeature(Point3D.Origin, 10, 10, 10);
        var comFillet = new FilletFeature("filete", box, 1) { ApplyToAllEdges = true };
        var girada = comFillet.Transformed(Placement.RotationAbout(Point3D.Origin, Vector3D.UnitZ, 45));
        var ferramenta = new BoxFeature(new Point3D(-100, -100, -100), 5, 5, 5);

        Action act = () => BooleanFeature.CreateDifference("corte", girada, ferramenta);

        act.Should().Throw<NotSupportedException>().WithMessage("*girada*fillet*");
    }
}
