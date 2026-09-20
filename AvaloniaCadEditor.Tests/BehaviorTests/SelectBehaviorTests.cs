using System.Linq;
using AvaloniaCadEditor.Application.Commands;
using AvaloniaCadEditor.Domain.Entities;
using AvaloniaCadEditor.Domain.ValueObjects;
using AvaloniaCadEditor.Domain.Interfaces;
using Moq;
using FluentAssertions;
using Xunit;

namespace AvaloniaCadEditor.Tests.BehaviorTests;

public class SelectBehaviorTests
{
    [Fact]
    public void SelectTool_SelecaoUnica_DeveSelecionarApenasUmaEntidade()
    {
        // Arrange
        var sketch = new Sketch();
        var line1 = new LineEntity(new Point3D(0, 0), new Point3D(10, 0));
        var line2 = new LineEntity(new Point3D(0, 5), new Point3D(10, 5));
        sketch.AddEntity(line1);
        sketch.AddEntity(line2);

        var mockEngine = new Mock<IGeometryEngine>();
        // Mocking hit test to return line1 when clicked near (5, 0)
        mockEngine.Setup(e => e.HitTest(sketch.Entities, It.Is<Point3D>(p => p.Y == 0), It.IsAny<double>()))
                  .Returns(line1);

        var selectTool = new SelectTool(mockEngine.Object);

        // Act
        selectTool.OnMouseDown(sketch, new Point3D(5, 0));

        // Assert
        line1.IsSelected.Should().BeTrue();
        line2.IsSelected.Should().BeFalse();
    }

    [Fact]
    public void SelectTool_LimpezaDeSelecao_EmptyClickDeveDesmarcarTudo()
    {
        // Arrange
        var sketch = new Sketch();
        var line1 = new LineEntity(new Point3D(0, 0), new Point3D(10, 0)) { IsSelected = true };
        var line2 = new LineEntity(new Point3D(0, 5), new Point3D(10, 5)) { IsSelected = true };
        sketch.AddEntity(line1);
        sketch.AddEntity(line2);

        var mockEngine = new Mock<IGeometryEngine>();
        // Mocking hit test to return null (empty click)
        mockEngine.Setup(e => e.HitTest(sketch.Entities, It.IsAny<Point3D>(), It.IsAny<double>()))
                  .Returns((Entity)null);

        var selectTool = new SelectTool(mockEngine.Object);

        // Act
        selectTool.OnMouseDown(sketch, new Point3D(100, 100)); // Click longe de tudo

        // Assert
        line1.IsSelected.Should().BeFalse();
        line2.IsSelected.Should().BeFalse();
    }
}
