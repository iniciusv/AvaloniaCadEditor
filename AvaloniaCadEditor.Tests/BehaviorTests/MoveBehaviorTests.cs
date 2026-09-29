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
        
        moveTool.OnKeyDown(sketch, CadKey.Control); // Ativa o modo cópia

        moveTool.OnMouseDown(sketch, new Point3D(5, 5)); // Target point

        // Assert
        var entities = sketch.Entities.OfType<LineEntity>().ToList();
        entities.Should().HaveCount(2); // Original + Cópia
        
        // A linha original deve permanecer intacta
        entities[0].BeEquivalentToCadLine(new LineEntity(new Point3D(0, 0), new Point3D(10, 0)));

        // A nova linha deve ter sido criada na posição transladada
        entities[1].BeEquivalentToCadLine(new LineEntity(new Point3D(5, 5), new Point3D(15, 5)));
    }

    [Fact]
    public void MoveTool_ComValorVCB_DeveMoverNaDistanciaExata()
    {
        // Arrange
        var sketch = new Sketch();
        var line = new LineEntity(new Point3D(0, 0), new Point3D(10, 0));
        line.IsSelected = true;
        sketch.AddEntity(line);

        var moveTool = new MoveTool();

        // Act
        moveTool.OnMouseDown(sketch, new Point3D(0, 0)); // Base point
        
        moveTool.OnMouseMove(sketch, new Point3D(100, 100)); // Direction (diagonal)
        
        // Aplica um VCB de 50 (a linha deve mover exatos 50 na diagonal)
        moveTool.OnValueEntered(sketch, 50.0);

        // Assert
        var entities = sketch.Entities.OfType<LineEntity>().ToList();
        entities.Should().HaveCount(1);
        
        // The expected displacement is 50 in the direction of (1, 1, 0)
        var direction = new Vector3D(100, 100, 0).Normalize();
        var expectedTranslation = direction * 50.0;

        entities[0].BeEquivalentToCadLine(new LineEntity(
            new Point3D(0 + expectedTranslation.X, 0 + expectedTranslation.Y), 
            new Point3D(10 + expectedTranslation.X, 0 + expectedTranslation.Y)));
    }

    [Fact]
    public void MoveTool_Master_CopyMaisVCB_DeveCopiarNaDistanciaExata()
    {
        var sketch = new Sketch();
        var line = new LineEntity(new Point3D(0, 0), new Point3D(10, 0)) { IsSelected = true };
        sketch.AddEntity(line);

        var moveTool = new MoveTool();

        moveTool.OnMouseDown(sketch, new Point3D(0, 0)); // Base point
        
        moveTool.OnKeyDown(sketch, CadKey.Control); // MODO COPIA
        
        moveTool.OnMouseMove(sketch, new Point3D(0, 100)); // Direction (up)
        
        moveTool.OnValueEntered(sketch, 10.0); // Distância exata 10.0

        var entities = sketch.Entities.OfType<LineEntity>().ToList();
        entities.Should().HaveCount(2);
        
        // Original intocada
        entities[0].BeEquivalentToCadLine(new LineEntity(new Point3D(0, 0), new Point3D(10, 0)));
        // Cópia transladada 10 em Y
        entities[1].BeEquivalentToCadLine(new LineEntity(new Point3D(0, 10), new Point3D(10, 10)));
    }
    [Fact]
    public void MoveTool_EmPlanoInclinado_DeslocamentoDasFeaturesSaiNoMundo()
    {
        var sketch = new Sketch();
        sketch.AddFeature(new AvaloniaCadEditor.Domain.Features.BoxFeature(Point3D.Origin, 10, 10, 10) { IsSelected = true });
        // Normal X: X local -> Y do mundo.
        var moveTool = new MoveTool { Plane = new WorkPlane(Vector3D.UnitX, new Point3D(10, 0, 0)) };

        Vector3D? recebido = null;
        moveTool.FeaturesMoved += (vector, features) => recebido = vector;

        moveTool.OnMouseDown(sketch, new Point3D(0, 0, 0));
        moveTool.OnMouseDown(sketch, new Point3D(5, 0, 0));

        recebido.Should().Be(new Vector3D(0, 5, 0));
    }

    [Fact]
    public void MoveTool_ComControl_FeaturesViramCopiaEmVezDeMover()
    {
        var sketch = new Sketch();
        sketch.AddFeature(new AvaloniaCadEditor.Domain.Features.BoxFeature(Point3D.Origin, 10, 10, 10) { IsSelected = true });
        var moveTool = new MoveTool();

        bool moveu = false;
        Vector3D? copia = null;
        moveTool.FeaturesMoved += (vector, features) => moveu = true;
        moveTool.FeaturesCopied += (vector, features) => copia = vector;

        moveTool.OnMouseDown(sketch, new Point3D(0, 0, 0));
        moveTool.OnKeyDown(sketch, CadKey.Control);
        moveTool.IsCopyPreview.Should().BeTrue();
        moveTool.OnMouseDown(sketch, new Point3D(7, 0, 0));

        moveu.Should().BeFalse();
        copia.Should().Be(new Vector3D(7, 0, 0));
    }
}
