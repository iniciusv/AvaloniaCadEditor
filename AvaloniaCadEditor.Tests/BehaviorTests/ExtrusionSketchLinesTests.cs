using AvaloniaCadEditor.Application.Services;
using AvaloniaCadEditor.Domain.Entities;
using AvaloniaCadEditor.Domain.ValueObjects;
using FluentAssertions;
using Xunit;

namespace AvaloniaCadEditor.Tests.BehaviorTests;

public class ExtrusionSketchLinesTests
{
    [Fact]
    public void Extrudar_UmaRegiaoDoEsboco_MantemAsLinhasDeOrigem()
    {
        var sketch = new Sketch();
        sketch.AddEntity(new LineEntity(new Point3D(0, 0), new Point3D(20, 0)));
        sketch.AddEntity(new LineEntity(new Point3D(20, 0), new Point3D(20, 10)));
        sketch.AddEntity(new LineEntity(new Point3D(20, 10), new Point3D(0, 10)));
        sketch.AddEntity(new LineEntity(new Point3D(0, 10), new Point3D(0, 0)));
        var regiao = new ProfileRecognitionService().RecognizeProfiles(sketch).Single();
        regiao.SourceEntityIds.Should().HaveCount(4);

        FeatureCodeGenerator.GenerateExtrusionCode(regiao, height: 5, sketch);

        sketch.Entities.Should().HaveCount(4, "a região pode ser extrudada de novo sem redesenhar o contorno");
    }
}
