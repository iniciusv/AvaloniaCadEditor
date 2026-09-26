using System;
using System.Linq;
using AvaloniaCadEditor.Application.Commands;
using AvaloniaCadEditor.Application.Services;
using AvaloniaCadEditor.Domain.Entities;
using AvaloniaCadEditor.Domain.Features;
using AvaloniaCadEditor.Domain.Profiles;
using AvaloniaCadEditor.Domain.ValueObjects;
using AvaloniaCadEditor.Infrastructure.OpenCascade;
using AvaloniaCadEditor.UI.ViewModels;
using FluentAssertions;
using Moq;
using Xunit;

namespace AvaloniaCadEditor.Tests.BehaviorTests;

public class DeleteBehaviorTests
{
    [Fact]
    public void DeleteSelected_ShouldRemoveSelectedEntity_AndClearSelectionAndRefreshModelTree()
    {
        // Arrange
        var mockGeo = new Mock<AvaloniaCadEditor.Domain.Interfaces.IGeometryEngine>();
        var mockSnap = new Mock<IOSnapService>();
        var selectionService = new SelectionService();
        var vm = TestUtils.CreateCadViewModel(mockGeo.Object, mockSnap.Object, null, new PureMathGeometryKernel(), selectionService);

        var line = new LineEntity(new Point3D(0, 0, 0), new Point3D(100, 0, 0));
        var circle = new CircleEntity(new Point3D(50, 50, 0), 20.0);
        vm.Sketch.AddEntity(line);
        vm.Sketch.AddEntity(circle);
        vm.RefreshProfiles();

        vm.Sketch.Entities.Should().HaveCount(2);

        // Select line
        selectionService.SelectEntity(line);
        selectionService.SelectedId.Should().Be(line.Id);

        // Act: Delete line
        vm.DeleteSelectedCommand.Execute(null);

        // Assert
        vm.Sketch.Entities.Should().HaveCount(1);
        vm.Sketch.Entities.Should().NotContain(line);
        vm.Sketch.Entities.Should().Contain(circle);
        selectionService.SelectedId.Should().BeNull();
        selectionService.SelectedObject.Should().BeNull();
        vm.PropertyInspector.HasSelection.Should().BeFalse();
    }

    [Fact]
    public void OnKeyDown_Delete_ShouldTriggerDeletionOfSelectedEntity()
    {
        // Arrange
        var mockGeo = new Mock<AvaloniaCadEditor.Domain.Interfaces.IGeometryEngine>();
        var mockSnap = new Mock<IOSnapService>();
        var selectionService = new SelectionService();
        var vm = TestUtils.CreateCadViewModel(mockGeo.Object, mockSnap.Object, null, new PureMathGeometryKernel(), selectionService);

        var line = new LineEntity(new Point3D(0, 0, 0), new Point3D(50, 50, 0));
        vm.Sketch.AddEntity(line);
        vm.RefreshProfiles();

        selectionService.SelectEntity(line);

        // Act
        vm.OnKeyDown("Delete");

        // Assert
        vm.Sketch.Entities.Should().BeEmpty();
        selectionService.SelectedId.Should().BeNull();
    }

    [Fact]
    public void DeleteSelected_ShouldRemove3DFeature_AndRefreshSolidMesh()
    {
        // Arrange
        var kernel = new PureMathGeometryKernel();
        var mockGeo = new Mock<AvaloniaCadEditor.Domain.Interfaces.IGeometryEngine>();
        var mockSnap = new Mock<IOSnapService>();
        var selectionService = new SelectionService();
        var vm = TestUtils.CreateCadViewModel(mockGeo.Object, mockSnap.Object, null, kernel, selectionService);

        var circleProfile = new CircleProfile(new Point3D(0, 0, 0), 15.0);
        var cylinder = new ExtrudeFeature(circleProfile, 30.0);
        vm.Sketch.AddFeature(cylinder);
        vm.RefreshSolidMesh();

        vm.SolidMesh.TriangleCount.Should().BeGreaterThan(0);
        selectionService.SelectFeature(cylinder);

        // Act
        vm.DeleteSelectedCommand.Execute(null);

        // Assert
        vm.Sketch.Features.Should().BeEmpty();
        vm.Sketch.WorkPlanes.Should().HaveCount(1);
        selectionService.SelectedId.Should().BeNull();
        vm.SolidMesh.TriangleCount.Should().Be(0);
    }

    [Fact]
    public void DeleteSelected_ShouldRemoveProfileAndItsSourceEntities()
    {
        // Arrange
        var mockGeo = new Mock<AvaloniaCadEditor.Domain.Interfaces.IGeometryEngine>();
        var mockSnap = new Mock<IOSnapService>();
        var selectionService = new SelectionService();
        var vm = TestUtils.CreateCadViewModel(mockGeo.Object, mockSnap.Object, null, new PureMathGeometryKernel(), selectionService);

        var line1 = new LineEntity(new Point3D(0, 0), new Point3D(10, 0));
        var line2 = new LineEntity(new Point3D(10, 0), new Point3D(10, 10));
        var line3 = new LineEntity(new Point3D(10, 10), new Point3D(0, 10));
        var line4 = new LineEntity(new Point3D(0, 10), new Point3D(0, 0));

        vm.Sketch.AddEntity(line1);
        vm.Sketch.AddEntity(line2);
        vm.Sketch.AddEntity(line3);
        vm.Sketch.AddEntity(line4);

        var profile = new PolygonProfile(
            new[] { new Point3D(0, 0), new Point3D(10, 0), new Point3D(10, 10), new Point3D(0, 10) },
            new[] { line1.Id, line2.Id, line3.Id, line4.Id }
        );
        vm.Sketch.AddProfile(profile);

        selectionService.SelectProfile(profile);

        // Act
        vm.DeleteSelectedCommand.Execute(null);

        // Assert
        vm.Sketch.Profiles.Should().BeEmpty();
        vm.Sketch.Entities.Should().BeEmpty();
    }

    [Fact]
    public void OnKeyDown_P_ShouldSelectExtrudeTool()
    {
        // Arrange
        var mockGeo = new Mock<AvaloniaCadEditor.Domain.Interfaces.IGeometryEngine>();
        var mockSnap = new Mock<IOSnapService>();
        var selectionService = new SelectionService();
        var vm = TestUtils.CreateCadViewModel(mockGeo.Object, mockSnap.Object, null, null, selectionService);

        vm.CurrentTool.Should().BeOfType<SelectTool>();

        // Act: Press 'P' (Push/Pull shortcut)
        vm.OnKeyDown("P");

        // Assert
        vm.CurrentTool.Should().BeOfType<ExtrudeCommand>();
    }

    [Fact]
    public void OnKeyDown_E_ShouldAlsoSelectExtrudeTool()
    {
        // Arrange
        var mockGeo = new Mock<AvaloniaCadEditor.Domain.Interfaces.IGeometryEngine>();
        var mockSnap = new Mock<IOSnapService>();
        var selectionService = new SelectionService();
        var vm = TestUtils.CreateCadViewModel(mockGeo.Object, mockSnap.Object, null, null, selectionService);

        // Act: Press 'E'
        vm.OnKeyDown("E");

        // Assert
        vm.CurrentTool.Should().BeOfType<ExtrudeCommand>();
    }

    [Fact]
    public void DeleteSelected_BooleanFeature_ShouldRemoveSolidAndToolsCorrectly()
    {
        // Arrange
        var mockGeo = new Mock<AvaloniaCadEditor.Domain.Interfaces.IGeometryEngine>();
        var mockSnap = new Mock<IOSnapService>();
        var selectionService = new SelectionService();
        var kernel = new AvaloniaCadEditor.Infrastructure.OpenCascade.OpenCascadeGeometryKernel();
        var vm = TestUtils.CreateCadViewModel(mockGeo.Object, mockSnap.Object, null, kernel, selectionService);

        // Simulate code generation for creating volumes and a boolean operation
        var code = @"
var box_base = new BoxFeature(new Point3D(0,0,0), 20, 20, 20) { Name = ""box_base"" };
AddFeature(box_base);
var box_tool = new BoxFeature(new Point3D(5,5,5), 10, 10, 10) { Name = ""box_tool"", IsTransientTool = true };
box_tool.AlignToFace(box_base, FaceType.Top);
AddFeature(box_tool);
var bool_cut = new BooleanFeature(""Push/Pull"", BooleanOperationType.Subtract, box_base, new[] { box_tool }) { Name = ""bool_cut"" };
AddFeature(bool_cut);
";
        vm.ScriptEditor.GeneratedCSharpCode = code;
        vm.ScriptEditor.RunCodeCommand.Execute(null);

        // Wait for Roslyn to process if it's sync. TestUtils makes Evaluator sync enough.
        // Assert creation
        vm.Sketch.Features.OfType<BooleanFeature>().Should().ContainSingle();
        var boolFeature = vm.Sketch.Features.OfType<BooleanFeature>().First();

        // Act - Select the boolean feature and delete
        selectionService.SelectFeature(boolFeature);
        vm.DeleteSelectedCommand.Execute(null);

        // Assert - The boolean feature should be removed and script updated without compilation errors
        vm.Sketch.Features.OfType<BooleanFeature>().Should().BeEmpty();
        
        // Assert that the boolean tool and its AlignToFace are removed, preventing compiler error.
        vm.ScriptEditor.GeneratedCSharpCode.Should().NotContain("bool_cut");
        vm.ScriptEditor.GeneratedCSharpCode.Should().NotContain("box_tool.AlignToFace");
        
        // Because of the bug, previously this generated a compile error, 
        // causing the sketch to not update correctly on RunCode.
        // We verify that after deletion and execution, the sketch doesn't contain bool_cut.
        vm.Sketch.Features.Should().NotContain(f => f.Name == "bool_cut");
    }

    [Fact]
    public void DeleteSelected_MultilineFeatureDeclaration_ShouldRemoveCompletely()
    {
        // Arrange
        var mockGeo = new Mock<AvaloniaCadEditor.Domain.Interfaces.IGeometryEngine>();
        var mockSnap = new Mock<IOSnapService>();
        var selectionService = new SelectionService();
        var kernel = new AvaloniaCadEditor.Infrastructure.OpenCascade.OpenCascadeGeometryKernel();
        var vm = TestUtils.CreateCadViewModel(mockGeo.Object, mockSnap.Object, null, kernel, selectionService);

        // Simulate code generation using multiline object initializer
        var code = @"
var box_e76e = new BoxFeature(baseCorner: new Point3D(0, 0, 0), width: 20, length: 20, height: 20)
{
    Name = ""box_e76e""
};
AddFeature(box_e76e);
";
        vm.ScriptEditor.GeneratedCSharpCode = code;
        vm.ScriptEditor.RunCodeCommand.Execute(null);

        // Assert creation
        vm.Sketch.Features.Should().ContainSingle();
        var feature = vm.Sketch.Features.First();

        // Act - Select the feature and delete
        selectionService.SelectFeature(feature);
        vm.DeleteSelectedCommand.Execute(null);

        // Assert - The entire statement including the multiline object initializer should be gone
        vm.Sketch.Features.Should().BeEmpty();
        vm.ScriptEditor.GeneratedCSharpCode.Should().NotContain("Name = \"box_e76e\"");
        vm.ScriptEditor.GeneratedCSharpCode.Should().NotContain("{");
        vm.ScriptEditor.GeneratedCSharpCode.Should().NotContain("}");
    }

    [Fact]
    public void DeleteSelected_ToolFeature_ShouldCascadeAndRemoveBoolean()
    {
        var mockGeo = new Mock<AvaloniaCadEditor.Domain.Interfaces.IGeometryEngine>();
        var selectionService = new SelectionService();
        var kernel = new AvaloniaCadEditor.Infrastructure.OpenCascade.OpenCascadeGeometryKernel();
        var vm = TestUtils.CreateCadViewModel(mockGeo.Object, new Mock<IOSnapService>().Object, null, kernel, selectionService);

        var code = @"
var box_base = new BoxFeature(new Point3D(0, 0, 0), 20, 20, 20) { Name = ""box_base"" };
AddFeature(box_base);
var box_tool = new BoxFeature(new Point3D(5, 5, 5), 10, 10, 10) { Name = ""box_tool"" };
AddFeature(box_tool);
var bool_cut = new BooleanFeature(""Push/Pull"", BooleanOperationType.Subtract, box_base, new[] { box_tool }) { Name = ""bool_cut"" };
AddFeature(bool_cut);
";
        vm.ScriptEditor.GeneratedCSharpCode = code;
        vm.ScriptEditor.RunCodeCommand.Execute(null);

        var toolFeature = vm.Sketch.Features.First(f => f.Name == "box_tool");
        selectionService.SelectFeature(toolFeature);
        vm.DeleteSelectedCommand.Execute(null);

        vm.ScriptEditor.GeneratedCSharpCode.Should().NotContain("box_tool");
        vm.ScriptEditor.GeneratedCSharpCode.Should().NotContain("bool_cut");
        vm.ScriptEditor.GeneratedCSharpCode.Should().Contain("box_base");
        
        vm.Sketch.Features.Should().ContainSingle();
        vm.Sketch.Features.First().Name.Should().Be("box_base");
    }

    [Fact]
    public void DeleteSelected_BaseFeature_ShouldCascadeAndRemoveEverything()
    {
        var mockGeo = new Mock<AvaloniaCadEditor.Domain.Interfaces.IGeometryEngine>();
        var selectionService = new SelectionService();
        var kernel = new AvaloniaCadEditor.Infrastructure.OpenCascade.OpenCascadeGeometryKernel();
        var vm = TestUtils.CreateCadViewModel(mockGeo.Object, new Mock<IOSnapService>().Object, null, kernel, selectionService);

        var code = @"
var box_base = new BoxFeature(new Point3D(0, 0, 0), 20, 20, 20) { Name = ""box_base"" };
AddFeature(box_base);
var box_tool = new BoxFeature(box_base.BaseCorner, 10, 10, 10) { Name = ""box_tool"" };
AddFeature(box_tool);
var bool_cut = new BooleanFeature(""Push/Pull"", BooleanOperationType.Subtract, box_base, new[] { box_tool }) { Name = ""bool_cut"" };
AddFeature(bool_cut);
";
        vm.ScriptEditor.GeneratedCSharpCode = code;
        vm.ScriptEditor.RunCodeCommand.Execute(null);

        var baseFeature = vm.Sketch.Features.First(f => f.Name == "box_base");
        selectionService.SelectFeature(baseFeature);
        vm.DeleteSelectedCommand.Execute(null);

        vm.Sketch.Features.Should().BeEmpty();
        string cleanCode = vm.ScriptEditor.GeneratedCSharpCode.Trim();
        cleanCode.Should().BeEmpty();
    }

    [Fact]
    public void DeleteSelected_IntermediateBoolean_ShouldCascadeAndInvalidateSubsequentOperations()
    {
        var mockGeo = new Mock<AvaloniaCadEditor.Domain.Interfaces.IGeometryEngine>();
        var selectionService = new SelectionService();
        var kernel = new AvaloniaCadEditor.Infrastructure.OpenCascade.OpenCascadeGeometryKernel();
        var vm = TestUtils.CreateCadViewModel(mockGeo.Object, new Mock<IOSnapService>().Object, null, kernel, selectionService);

        var code = @"
var box_base = new BoxFeature(new Point3D(0, 0, 0), 50, 50, 50) { Name = ""box_base"" };
AddFeature(box_base);
var tool_1 = new BoxFeature(new Point3D(10, 10, 10), 10, 10, 10) { Name = ""tool_1"", IsTransientTool = true };
AddFeature(tool_1);
var bool_1 = new BooleanFeature(""Op1"", BooleanOperationType.Subtract, box_base, new[] { tool_1 }) { Name = ""bool_1"" };
AddFeature(bool_1);

var tool_2 = new BoxFeature(new Point3D(20, 20, 20), 10, 10, 10) { Name = ""tool_2"" };
AddFeature(tool_2);
var bool_2 = new BooleanFeature(""Op2"", BooleanOperationType.Subtract, bool_1, new[] { tool_2 }) { Name = ""bool_2"" };
AddFeature(bool_2);
";
        vm.ScriptEditor.GeneratedCSharpCode = code;
        vm.ScriptEditor.RunCodeCommand.Execute(null);

        var bool1 = vm.Sketch.Features.First(f => f.Name == "bool_1");
        selectionService.SelectFeature(bool1);
        vm.DeleteSelectedCommand.Execute(null);

        // base survives
        vm.ScriptEditor.GeneratedCSharpCode.Should().Contain("box_base");
        // tool_1 deleted by CadViewModel rule
        vm.ScriptEditor.GeneratedCSharpCode.Should().NotContain("tool_1");
        // bool_1 deleted
        vm.ScriptEditor.GeneratedCSharpCode.Should().NotContain("bool_1");
        // bool_2 deleted by Roslyn cascade (it references bool_1)
        vm.ScriptEditor.GeneratedCSharpCode.Should().NotContain("bool_2");
        // tool_2 SURVIVES because it doesn't reference bool_1 or bool_2 directly in its declaration!
        vm.ScriptEditor.GeneratedCSharpCode.Should().Contain("tool_2");

        vm.Sketch.Features.Should().Contain(f => f.Name == "box_base");
        vm.Sketch.Features.Should().Contain(f => f.Name == "tool_2");
        vm.Sketch.Features.Should().HaveCount(2);
    }

    [Fact]
    public void DeleteSelected_CompoundBoolean_ShouldRemoveAllTools()
    {
        var mockGeo = new Mock<AvaloniaCadEditor.Domain.Interfaces.IGeometryEngine>();
        var selectionService = new SelectionService();
        var kernel = new AvaloniaCadEditor.Infrastructure.OpenCascade.OpenCascadeGeometryKernel();
        var vm = TestUtils.CreateCadViewModel(mockGeo.Object, new Mock<IOSnapService>().Object, null, kernel, selectionService);

        var code = @"
var box_base = new BoxFeature(new Point3D(0, 0, 0), 20, 20, 20) { Name = ""box_base"" };
AddFeature(box_base);
var tool_1 = new BoxFeature(new Point3D(5, 5, 5), 10, 10, 10) { Name = ""tool_1"", IsTransientTool = true };
AddFeature(tool_1);
var tool_2 = new BoxFeature(new Point3D(10, 10, 10), 10, 10, 10) { Name = ""tool_2"", IsTransientTool = true };
AddFeature(tool_2);
var multi = new BooleanFeature(""Multi"", BooleanOperationType.Union, box_base, new[] { tool_1, tool_2 }) { Name = ""multi"" };
AddFeature(multi);
";
        vm.ScriptEditor.GeneratedCSharpCode = code;
        vm.ScriptEditor.RunCodeCommand.Execute(null);

        var multi = vm.Sketch.Features.First(f => f.Name == "multi");
        selectionService.SelectFeature(multi);
        vm.DeleteSelectedCommand.Execute(null);

        vm.ScriptEditor.GeneratedCSharpCode.Should().NotContain("multi");
        vm.ScriptEditor.GeneratedCSharpCode.Should().NotContain("tool_1");
        vm.ScriptEditor.GeneratedCSharpCode.Should().NotContain("tool_2");
        vm.ScriptEditor.GeneratedCSharpCode.Should().Contain("box_base");

        vm.Sketch.Features.Should().ContainSingle();
        vm.Sketch.Features.First().Name.Should().Be("box_base");
    }

    [Fact]
    public void DeleteSelected_ExplicitBoolean_ShouldPreserveTools()
    {
        var mockGeo = new Mock<AvaloniaCadEditor.Domain.Interfaces.IGeometryEngine>();
        var selectionService = new SelectionService();
        var kernel = new AvaloniaCadEditor.Infrastructure.OpenCascade.OpenCascadeGeometryKernel();
        var vm = TestUtils.CreateCadViewModel(mockGeo.Object, new Mock<IOSnapService>().Object, null, kernel, selectionService);

        var code = @"
var box_base = new BoxFeature(new Point3D(0, 0, 0), 20, 20, 20) { Name = ""box_base"" };
AddFeature(box_base);
var box_tool = new BoxFeature(new Point3D(5, 5, 5), 10, 10, 10) { Name = ""box_tool"" }; // NOT transient
AddFeature(box_tool);
var bool_union = new BooleanFeature(""Explicit Union"", BooleanOperationType.Union, box_base, new[] { box_tool }) { Name = ""bool_union"" };
AddFeature(bool_union);
";
        vm.ScriptEditor.GeneratedCSharpCode = code;
        vm.ScriptEditor.RunCodeCommand.Execute(null);

        var unionFeature = vm.Sketch.Features.First(f => f.Name == "bool_union");
        selectionService.SelectFeature(unionFeature);
        vm.DeleteSelectedCommand.Execute(null);

        vm.ScriptEditor.GeneratedCSharpCode.Should().NotContain("bool_union");
        // But since it wasn't transient, it should preserve the base and the tool!
        vm.ScriptEditor.GeneratedCSharpCode.Should().Contain("box_base");
        vm.ScriptEditor.GeneratedCSharpCode.Should().Contain("box_tool");

        vm.Sketch.Features.Should().Contain(f => f.Name == "box_base");
        vm.Sketch.Features.Should().Contain(f => f.Name == "box_tool");
    }
}
