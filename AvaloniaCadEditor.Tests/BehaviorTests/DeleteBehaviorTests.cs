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
}
