using AvaloniaCadEditor.Application.Services;
using AvaloniaCadEditor.Domain.Entities;
using AvaloniaCadEditor.Domain.Interfaces;
using AvaloniaCadEditor.Domain.ValueObjects;
using FluentAssertions;
using Moq;

namespace AvaloniaCadEditor.Tests.BehaviorTests;

public class OSnapBehaviorTests
{
    [Fact]
    public void OSnap_DevePuxarOCursorParaOEndpoint_QuandoEstiverNaToleranciaDefinida()
    {
        // Arrange
        var sketch = new Sketch();
        var line = new LineEntity(new Point3D(10, 10), new Point3D(20, 20));
        sketch.AddEntity(line);

        var mockEngine = new Mock<IGeometryEngine>();
        // Mock engine to say the snap points of the line are its endpoints
        mockEngine.Setup(e => e.GetSnapPoints(line, Moq.It.IsAny<Point3D?>()))
                  .Returns(new List<(Point3D, SnapPointType)> { (line.StartPoint, SnapPointType.Endpoint), (line.EndPoint, SnapPointType.Endpoint) });

        var osnapService = new OSnapService(mockEngine.Object);

        var cursorPertoDoInicio = new Point3D(9.5, 10.2); // Bem perto de 10,10
        var cursorLonge = new Point3D(50, 50);

        // Act
        var snappedPointPerto = osnapService.GetSnappedPoint(sketch, cursorPertoDoInicio, null);
        var snappedPointLonge = osnapService.GetSnappedPoint(sketch, cursorLonge, null);

        // Assert
        snappedPointPerto.Should().Be(new Point3D(10, 10)); // O cursor sofreu atração ("snap")
        snappedPointLonge.Should().Be(cursorLonge); // Não sofreu atração porque está longe
    }
}
