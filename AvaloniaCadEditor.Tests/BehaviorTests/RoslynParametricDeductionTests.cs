using System.Linq;
using AvaloniaCadEditor.Application.Services;
using AvaloniaCadEditor.Domain.Entities;
using AvaloniaCadEditor.Domain.Features;
using AvaloniaCadEditor.Domain.Profiles;
using AvaloniaCadEditor.Domain.ValueObjects;
using FluentAssertions;
using Xunit;

namespace AvaloniaCadEditor.Tests.BehaviorTests;

public class RoslynParametricDeductionTests
{
    [Fact]
    public void BoxFeature_GetVertices_ShouldReturn8CorrectCorners()
    {
        // Arrange
        var box = new BoxFeature(new Point3D(0, 0, 0), width: 20, length: 30, height: 40);

        // Act
        var verts = box.GetVertices();

        // Assert
        verts.Should().HaveCount(8);
        verts[0].Should().Be(new Point3D(0, 0, 0)); // BottomLeftFront
        verts[1].Should().Be(new Point3D(20, 0, 0)); // BottomRightFront
        verts[2].Should().Be(new Point3D(20, 30, 0)); // BottomRightBack
        verts[3].Should().Be(new Point3D(0, 30, 0)); // BottomLeftBack
        verts[4].Should().Be(new Point3D(0, 0, 40)); // TopLeftFront
        verts[5].Should().Be(new Point3D(20, 0, 40)); // TopRightFront
        verts[6].Should().Be(new Point3D(20, 30, 40)); // TopRightBack
        verts[7].Should().Be(new Point3D(0, 30, 40)); // TopLeftBack
    }

    [Fact]
    public void GeometricInferenceService_ShouldInferVertexReferenceWhenSnapping()
    {
        // Arrange
        var sketch = new Sketch();
        var box = new BoxFeature(new Point3D(0, 0, 0), width: 20, length: 20, height: 20) { Name = "box_1" };
        sketch.AddFeature(box);

        var service = new GeometricInferenceService();

        // Act: Test snapping close to top right back vertex (20, 20, 20)
        var vertexRef = service.InferVertexReference(sketch, new Point3D(20.0001, 19.9999, 20.0));

        // Assert
        vertexRef.Should().NotBeNull();
        vertexRef!.ParentFeature.Should().Be(box);
        vertexRef.VertexIndex.Should().Be(6); // BoxCorner.TopRightBack = 6
        vertexRef.GetPosition().Should().Be(new Point3D(20, 20, 20));
    }

    [Fact]
    public void GeometricInferenceService_ShouldInferLinkedWorkPlaneFromFace()
    {
        // Arrange
        var sketch = new Sketch();
        var box = new BoxFeature(new Point3D(0, 0, 0), width: 20, length: 20, height: 20) { Name = "box_1" };
        sketch.AddFeature(box);

        var service = new GeometricInferenceService();
        var topPlane = new WorkPlane(new Vector3D(0, 0, 1), new Point3D(10, 10, 20));

        // Act
        var linkedPlane = service.InferLinkedPlane(sketch, topPlane);

        // Assert
        linkedPlane.Should().NotBeNull();
        linkedPlane!.TargetFace.ParentFeature.Should().Be(box);
        linkedPlane.TargetFace.FaceId.Should().Be((int)FaceType.Top);
    }

    [Fact]
    public void RoslynCadScriptOptimizer_ShouldDeduplicateRepeatedPlanesAndFormat()
    {
        // Arrange
        string unoptimizedScript = @"
var face_top = box_1.FindFace(0, new Vector3D(0, 0, 1), ""face_top"");
var prism_1_plane = new LinkedWorkPlane(face_top);
var prism_1 = new PrismFeature(verts1, height: 10, plane: prism_1_plane) { Name = ""prism_1"" };
AddFeature(prism_1);

var face_top = box_1.FindFace(0, new Vector3D(0, 0, 1), ""face_top"");
var prism_2_plane = new LinkedWorkPlane(face_top);
var prism_2 = new PrismFeature(verts2, height: 20, plane: prism_2_plane) { Name = ""prism_2"" };
AddFeature(prism_2);
";

        // Act
        var result = RoslynCadScriptOptimizer.OptimizeAndFormat(unoptimizedScript);

        // Assert
        // The second FindFace and second LinkedWorkPlane should be deduplicated
        result.Should().Contain("plane: prism_1_plane");
        // Count occurrences of "FindFace"
        int countFindFace = result.Split("FindFace(").Length - 1;
        countFindFace.Should().Be(1);
    }

    [Fact]
    public void FeatureCodeGenerator_WhenPolygonDrawnOnBoxFace_ShouldEmitParametricLinkedWorkPlane()
    {
        // Arrange
        var sketch = new Sketch();
        var box = new BoxFeature(new Point3D(0, 0, 0), width: 20, length: 20, height: 20) { Name = "box_base" };
        sketch.AddFeature(box);

        // A polygon on the top face Z=20
        var topFacePlane = new WorkPlane(new Vector3D(0, 0, 1), new Point3D(0, 0, 20));
        var poly = new PolygonProfile(new[]
        {
            new Point3D(0, 0, 0),
            new Point3D(10, 0, 0),
            new Point3D(10, 10, 0)
        }, plane: topFacePlane);

        // Act
        var code = FeatureCodeGenerator.GenerateExtrusionCode(poly, height: 15, sketch);

        // Assert
        code.Should().Contain("FindFace");
        code.Should().Contain("new LinkedWorkPlane(");
        code.Should().Contain("box_base");
    }

    [Fact]
    public void FeatureCodeGenerator_CompoundProfile_ShouldExtractSubVariablesUsingRoslyn()
    {
        // Arrange
        var sketch = new Sketch();
        var outerRect = new RectangleProfile(new Point3D(50, 50, 0), 100, 100, 0.0, plane: WorkPlane.XY);
        var innerHole = new CircleProfile(new Point3D(50, 50, 0), 20.0, plane: WorkPlane.XY);
        
        var compound = new CompoundProfile(new IProfile[] { outerRect, innerHole });
        
        // Act
        var code = FeatureCodeGenerator.GenerateExtrusionCode(compound, height: 15, sketch);

        // Assert
        // We verify that the compound logic successfully extracted the box and cylinder variables from the sub-scripts
        code.Should().Contain("BooleanFeature.CreateUnion(");
        code.Should().Contain("box_");
        code.Should().Contain("cylinder_");
        code.Should().NotContain("AddFeature(box_"); // because they should be filtered out by the Roslyn traversal
    }

    [Fact]
    public void RoslynDependencyAnalyzer_ShouldFindRootsCorrectly()
    {
        // Arrange
        string script = @"
var base_box = new BoxFeature(new Point3D(0, 0, 0), width: 20, length: 20, height: 20) { Name = ""base_box"" };
var tool_cyl = new CylinderFeature(new Point3D(0, 0, 0), new Vector3D(0, 0, 1), 10, 20) { Name = ""tool_cyl"" };
var uniao = BooleanFeature.CreateUnion(""uniao"", base_box, tool_cyl);
AddFeature(uniao);
";

        var analyzer = new RoslynDependencyAnalyzer();

        // Act
        var roots = analyzer.GetRootVariableNames(script, "uniao").ToList();

        // Assert
        roots.Should().HaveCount(2);
        roots.Should().Contain("base_box");
        roots.Should().Contain("tool_cyl");
    }

    [Fact]
    public void RoslynCoordinateMutator_ShouldShiftPoint3DForVariables()
    {
        // Arrange
        string script = @"var base_box = new BoxFeature(new Point3D(10, -5, 2), width: 20, length: 20, height: 20);";
        
        // Act
        var result = RoslynCoordinateMutator.MutateCoordinates(
            script, 
            new[] { "base_box" }, 
            new Vector3D(10, 10, -2)
        );

        // Assert
        result.Should().Contain("new Point3D(20,5,0)");
    }

    // --- Regressão: mover (M) uma feature "furo" só movia o furo, não o contorno ---
    // GetRootVariableNames só devolve as variáveis-raiz do grafo (ex.: "base_z" e "cyl3" no script
    // abaixo); "poly1", que tem o contorno inteiro, depende de "base_z" e ficava de fora. O Move
    // agora usa GetMovableVariableNames, que pega toda variável do grafo com Point3D próprio.

    private const string PecaComFuroScript = @"
var base_z = 0;
var poly1 = new PolygonProfile(new System.Collections.Generic.List<Point3D> { new Point3D(-4, -3.293, base_z), new Point3D(-4, 3.293, base_z), new Point3D(4, 3.293, base_z), new Point3D(4, -3.293, base_z) });
var extrude2 = new ExtrudeFeature(poly1, 400, new Vector3D(0, 0, 100), ""Extrusão do Contorno Base"");
var cyl3 = new CylinderFeature(new Point3D(0, 0, -201), new Vector3D(0, 0, 1), 3, 402, ""Furo Cilíndrico Deduzido 1"");
var boolOp4 = BooleanFeature.CreateDifference(""Base Extrudada com Furos"", extrude2, cyl3);

AddFeature(boolOp4);
";

    [Fact]
    public void GetMovableVariableNames_IncluiVariaveisIntermediariasComPoint3DProprio()
    {
        var analyzer = new RoslynDependencyAnalyzer();

        var movable = analyzer.GetMovableVariableNames(PecaComFuroScript, "boolOp4").ToList();

        movable.Should().Contain("poly1");
        movable.Should().Contain("cyl3");
        movable.Should().NotContain("base_z", "base_z não tem nenhum new Point3D no próprio inicializador");
        movable.Should().NotContain("extrude2", "extrude2 não cria Point3D diretamente, só referencia poly1");
        movable.Should().NotContain("boolOp4");
    }

    [Fact]
    public void MoverPecaComFuro_DeslocaContornoEFuroJuntos()
    {
        var analyzer = new RoslynDependencyAnalyzer();
        var movable = analyzer.GetMovableVariableNames(PecaComFuroScript, "boolOp4");

        var moved = RoslynCoordinateMutator.MutateCoordinates(PecaComFuroScript, movable, new Vector3D(5, 0, 0));

        moved.Should().Contain("new Point3D(1,-3.293,base_z)"); // -4 + 5
        moved.Should().Contain("new Point3D(9,-3.293,base_z)"); // 4 + 5
        moved.Should().Contain("new Point3D(5,0,-201)"); // centro do cilindro: 0 + 5
        moved.Should().Contain("var base_z = 0;", "base_z não tem Point3D próprio e não deve mudar");
    }

    [Fact]
    public void MoverPecaComFuroEmZ_DesviaBaseZSemPerderAExpressao()
    {
        var analyzer = new RoslynDependencyAnalyzer();
        var movable = analyzer.GetMovableVariableNames(PecaComFuroScript, "boolOp4");

        var moved = RoslynCoordinateMutator.MutateCoordinates(PecaComFuroScript, movable, new Vector3D(0, 0, 3));
        moved = RoslynCoordinateMutator.MutateCoordinates(moved, analyzer.GetMovableVariableNames(moved, "boolOp4"), new Vector3D(0, 0, 3));

        // O Z de poly1 é a variável "base_z", não um literal: precisa virar "base_z+6", não ficar parado.
        moved.Should().Contain("base_z+6");
        // O centro do cilindro (literal -201) some direto para -195.
        moved.Should().Contain("new Point3D(0,0,-195)");
    }
}
