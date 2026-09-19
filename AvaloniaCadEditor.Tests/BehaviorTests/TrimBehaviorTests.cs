using AvaloniaCadEditor.Application.Commands;
using AvaloniaCadEditor.Domain.Entities;
using AvaloniaCadEditor.Domain.Interfaces;
using AvaloniaCadEditor.Domain.ValueObjects;
using FluentAssertions;
using Moq;

namespace AvaloniaCadEditor.Tests.BehaviorTests;

public class TrimBehaviorTests
{
    [Fact]
    public void Trim_DeveQuebrarUmaLinhaHorizontalComUmaVertical_ERemoverLadoClicado()
    {
        // Arrange
        var sketch = new Sketch();
        
        // Linha A (Horizontal, de x=0 a x=10 em y=5)
        var lineA = new LineEntity(new Point3D(0, 5), new Point3D(10, 5));
        // Linha B (Vertical, de y=0 a y=10 em x=5)
        var lineB = new LineEntity(new Point3D(5, 0), new Point3D(5, 10));
        
        sketch.AddEntity(lineA);
        sketch.AddEntity(lineB);

        var intersectionPt = new Point3D(5, 5);
        var clickPoint = new Point3D(2, 5); // Clicou no lado esquerdo da linha A
        
        // As a result of trim on the left side, the line A should become from x=5 to x=10.
        var lineARemaining = new LineEntity(new Point3D(5, 5), new Point3D(10, 5));

        var mockEngine = new Mock<IGeometryEngine>();
        
        // Mock hit test to return lineA
        mockEngine.Setup(e => e.HitTest(sketch.Entities, clickPoint, It.IsAny<double>()))
                  .Returns(lineA);

        // Mock intersections
        mockEngine.Setup(e => e.GetIntersections(lineA, lineB))
                  .Returns(new List<Point3D> { intersectionPt });

        // Mock trim behavior: cutting lineA at intersectionPt keeping the right side
        mockEngine.Setup(e => e.TrimEntity(lineA, It.IsAny<IEnumerable<Point3D>>(), clickPoint))
                  .Returns(new List<Entity> { lineARemaining });

        var trimCommand = new TrimCommand(mockEngine.Object);

        // Act
        trimCommand.OnMouseDown(sketch, clickPoint);

        // Assert
        sketch.Entities.Should().HaveCount(2); // lineB and lineARemaining
        sketch.Entities.Should().Contain(lineB);
        sketch.Entities.Should().Contain(lineARemaining);
        sketch.Entities.Should().NotContain(lineA);
    }
}
