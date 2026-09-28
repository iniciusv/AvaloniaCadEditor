using System.Linq;
using AvaloniaCadEditor.Application.Commands;
using AvaloniaCadEditor.Domain.Entities;
using AvaloniaCadEditor.Domain.Profiles;
using AvaloniaCadEditor.Domain.ValueObjects;
using AvaloniaCadEditor.Infrastructure.Engines;
using FluentAssertions;
using Xunit;

namespace AvaloniaCadEditor.Tests.BehaviorTests;

public class OffsetBehaviorTests
{
    private static Sketch CreateSketchWithSquareRegion()
    {
        var sketch = new Sketch();
        var profile = new PolygonProfile(new[]
        {
            new Point3D(0, 0), new Point3D(10, 0), new Point3D(10, 10), new Point3D(0, 10)
        }, plane: WorkPlane.XY);
        sketch.SetProfiles(new IProfile[] { profile });
        return sketch;
    }

    private static (double minX, double maxX, double minY, double maxY) BoundsOf(Sketch sketch)
    {
        var lines = sketch.Entities.OfType<LineEntity>().ToList();
        var xs = lines.SelectMany(l => new[] { l.StartPoint.X, l.EndPoint.X }).ToList();
        var ys = lines.SelectMany(l => new[] { l.StartPoint.Y, l.EndPoint.Y }).ToList();
        return (xs.Min(), xs.Max(), ys.Min(), ys.Max());
    }

    [Fact]
    public void Offset_CursorForaDaRegiao_DeveExpandirParaFora()
    {
        var sketch = CreateSketchWithSquareRegion();
        var command = new OffsetCommand(new MathGeometryEngine());

        command.OnMouseDown(sketch, new Point3D(5, 5));   // seleciona a região (clique dentro)
        command.OnMouseMove(sketch, new Point3D(15, 5));  // cursor fora, a 5 unidades da borda direita
        command.OnMouseDown(sketch, new Point3D(15, 5));  // confirma

        var lines = sketch.Entities.OfType<LineEntity>().ToList();
        lines.Should().HaveCount(4);

        var (minX, maxX, minY, maxY) = BoundsOf(sketch);
        minX.Should().BeApproximately(-5, 1e-6);
        maxX.Should().BeApproximately(15, 1e-6);
        minY.Should().BeApproximately(-5, 1e-6);
        maxY.Should().BeApproximately(15, 1e-6);
    }

    [Fact]
    public void Offset_CursorDentroDaRegiao_DeveContrairParaDentro()
    {
        var sketch = CreateSketchWithSquareRegion();
        var command = new OffsetCommand(new MathGeometryEngine());

        command.OnMouseDown(sketch, new Point3D(5, 5));  // seleciona a região
        command.OnMouseMove(sketch, new Point3D(2, 5));  // cursor dentro, a 2 unidades da borda esquerda
        command.OnMouseDown(sketch, new Point3D(2, 5));  // confirma

        var (minX, maxX, minY, maxY) = BoundsOf(sketch);
        minX.Should().BeApproximately(2, 1e-6);
        maxX.Should().BeApproximately(8, 1e-6);
        minY.Should().BeApproximately(2, 1e-6);
        maxY.Should().BeApproximately(8, 1e-6);
    }

    [Fact]
    public void Offset_ValorDigitado_DeveUsarMagnitudeExataMantendoOLadoDoCursor()
    {
        var sketch = CreateSketchWithSquareRegion();
        var command = new OffsetCommand(new MathGeometryEngine());

        command.OnMouseDown(sketch, new Point3D(5, 5));    // seleciona a região
        command.OnMouseMove(sketch, new Point3D(10.5, 5)); // cursor levemente fora (define o lado: "fora")
        command.OnValueEntered(sketch, 5.0);               // digita a distância exata via VCB

        var (minX, maxX, minY, maxY) = BoundsOf(sketch);
        minX.Should().BeApproximately(-5, 1e-6, "o valor digitado (5) deve prevalecer sobre a distância do cursor (0.5)");
        maxX.Should().BeApproximately(15, 1e-6);
    }

    [Fact]
    public void Offset_RegiaoOriginal_DevePermanecerNoSketch()
    {
        var sketch = CreateSketchWithSquareRegion();
        var command = new OffsetCommand(new MathGeometryEngine());

        command.OnMouseDown(sketch, new Point3D(5, 5));
        command.OnMouseMove(sketch, new Point3D(15, 5));
        command.OnMouseDown(sketch, new Point3D(15, 5));

        sketch.Profiles.Should().HaveCount(1, "a região original não deve ser removida pelo offset");
    }
}
