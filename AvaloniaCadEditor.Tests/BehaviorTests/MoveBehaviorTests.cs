using System.Linq;
using AvaloniaCadEditor.Application.Commands;
using AvaloniaCadEditor.Domain.Entities;
using AvaloniaCadEditor.Domain.ValueObjects;
using AvaloniaCadEditor.Tests.Extensions;
using FluentAssertions;
using Xunit;
using Moq;
using AvaloniaCadEditor.Domain.Interfaces;

namespace AvaloniaCadEditor.Tests.BehaviorTests;

public class MoveBehaviorTests
{
    [Fact]
    public void MoveTool_DeveTransladarEntidadeSelecionada()
    {
        // Arrange
        var sketch = new Sketch();
        var line = new LineEntity(new Point3D(0, 0), new Point3D(10, 0));
        line.IsSelected = true;
        sketch.AddEntity(line);

        var moveTool = new MoveTool();

        // Act - Clica em (0,0) e move para (5,5)
        moveTool.OnMouseDown(sketch, new Point3D(0, 0)); // Base point
        moveTool.OnMouseDown(sketch, new Point3D(5, 5)); // Target point

        // Assert
        var entities = sketch.Entities.OfType<LineEntity>().ToList();
        entities.Should().HaveCount(1);
        
        // A linha deve ter sido movida de (0,0)-(10,0) para (5,5)-(15,5)
        entities[0].BeEquivalentToCadLine(new LineEntity(new Point3D(5, 5), new Point3D(15, 5)));
    }

    [Fact]
    public void MoveTool_ComControl_DeveCopiarEntidadeSelecionada()
    {
        // Arrange
        var sketch = new Sketch();
        var line = new LineEntity(new Point3D(0, 0), new Point3D(10, 0));
        line.IsSelected = true;
        sketch.AddEntity(line);

        var moveTool = new MoveTool();

        // Act
        moveTool.OnMouseDown(sketch, new Point3D(0, 0)); // Base point
        
        moveTool.OnKeyDown(CadKey.Control); // Ativa o modo cópia

        moveTool.OnMouseDown(sketch, new Point3D(5, 5)); // Target point

        // Assert
        var entities = sketch.Entities.OfType<LineEntity>().ToList();
        entities.Should().HaveCount(2); // Original + Cópia
        
        // A linha original deve permanecer intacta
        entities[0].BeEquivalentToCadLine(new LineEntity(new Point3D(0, 0), new Point3D(10, 0)));

        // A nova linha deve ter sido criada na posição transladada
        entities[1].BeEquivalentToCadLine(new LineEntity(new Point3D(5, 5), new Point3D(15, 5)));
    }
}
