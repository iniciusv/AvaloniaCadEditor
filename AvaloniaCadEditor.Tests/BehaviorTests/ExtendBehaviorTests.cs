using System.Linq;
using AvaloniaCadEditor.Application.Commands;
using AvaloniaCadEditor.Domain.Entities;
using AvaloniaCadEditor.Domain.ValueObjects;
using AvaloniaCadEditor.Infrastructure.Engines;
using AvaloniaCadEditor.Tests.Extensions;
using FluentAssertions;
using Xunit;

namespace AvaloniaCadEditor.Tests.BehaviorTests;

public class ExtendBehaviorTests
{
    [Fact]
    public void Extend_ClicandoPertoDaPonta_DevePrologarAteALinhaEncontrada()
    {
        // Arrange
        var sketch = new Sketch();
        var lineA = new LineEntity(new Point3D(0, 5), new Point3D(5, 5));    // curta demais
        var lineB = new LineEntity(new Point3D(12, 0), new Point3D(12, 10)); // fronteira vertical, fora da tolerância de clique de lineA

        sketch.AddEntity(lineA);
        sketch.AddEntity(lineB);

        var command = new ExtendCommand(new MathGeometryEngine());

        // Act - clica exatamente na ponta direita de lineA (5,5)
        command.OnMouseDown(sketch, new Point3D(5, 5));

        // Assert
        var lines = sketch.Entities.OfType<LineEntity>().ToList();
        lines.Should().HaveCount(2);
        lines.Should().Contain(lineB);

        var extended = lines.Single(l => l != lineB);
        extended.BeEquivalentToCadLine(new LineEntity(new Point3D(0, 5), new Point3D(12, 5)));
    }

    [Fact]
    public void Extend_SemFronteiraNaDirecao_DeveFicarPendenteEEsperarSegundoClique()
    {
        var sketch = new Sketch();
        var lineA = new LineEntity(new Point3D(0, 0), new Point3D(5, 0));
        var lineB = new LineEntity(new Point3D(10, 10), new Point3D(10, 5)); // fora do alcance do raio direto

        sketch.AddEntity(lineA);
        sketch.AddEntity(lineB);

        var command = new ExtendCommand(new MathGeometryEngine());

        // Primeiro clique: nada é atingido diretamente, linha fica pendente.
        command.OnMouseDown(sketch, new Point3D(5, 0));
        sketch.Entities.Should().HaveCount(2, "nada deve mudar enquanto aguarda o segundo clique");
        lineA.IsSelected.Should().BeTrue();

        // Segundo clique na outra linha: forma o canto na interseção das retas.
        command.OnMouseDown(sketch, new Point3D(10, 7));

        var lines = sketch.Entities.OfType<LineEntity>().ToList();
        lines.Should().HaveCount(2);
        lines[0].BeEquivalentToCadLine(new LineEntity(new Point3D(0, 0), new Point3D(10, 0)));
        lines[1].BeEquivalentToCadLine(new LineEntity(new Point3D(10, 10), new Point3D(10, 0)));
    }

    [Fact]
    public void Extend_Escape_DeveLimparLinhaPendente()
    {
        var sketch = new Sketch();
        var lineA = new LineEntity(new Point3D(0, 0), new Point3D(5, 0));
        sketch.AddEntity(lineA);

        var command = new ExtendCommand(new MathGeometryEngine());
        command.OnMouseDown(sketch, new Point3D(5, 0));
        lineA.IsSelected.Should().BeTrue();

        command.OnKeyDown(sketch, CadKey.Escape);

        lineA.IsSelected.Should().BeFalse();
        sketch.Entities.Should().HaveCount(1);
    }
}
