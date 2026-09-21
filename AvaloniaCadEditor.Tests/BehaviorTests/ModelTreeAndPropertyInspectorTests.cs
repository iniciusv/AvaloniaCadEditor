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
using Xunit;

namespace AvaloniaCadEditor.Tests.BehaviorTests;

public class ModelTreeAndPropertyInspectorTests
{
    [Fact]
    public void ModelTree_ShouldRebuildNodes_WhenEntitiesProfilesAndFeaturesAreAdded()
    {
        // Arrange
        var selectionService = new SelectionService();
        var treeVm = new ModelTreeViewModel(selectionService);
        var sketch = new Sketch();

        // Act: Add an entity, a profile, and an extrude feature
        sketch.AddEntity(new LineEntity(new Point3D(0, 0, 0), new Point3D(100, 0, 0)));
        var circleProfile = new CircleProfile(new Point3D(0, 0, 0), 25.0);
        sketch.AddProfile(circleProfile);
        var cylinderFeature = new ExtrudeFeature(circleProfile, 50.0);
        sketch.AddFeature(cylinderFeature);

        treeVm.BindSketch(sketch);

        // Assert
        Assert.Equal(3, treeVm.RootNodes.Count);
        
        var featuresGroup = treeVm.RootNodes[0];
        Assert.Equal(2, featuresGroup.Children.Count); // Base plane + the new ExtrudeFeature
        Assert.NotNull(featuresGroup.Children[1].Icon);
        Assert.Equal(cylinderFeature.Id, featuresGroup.Children[1].Id);

        var profilesGroup = treeVm.RootNodes[1];
        Assert.Single(profilesGroup.Children);
        Assert.Equal(circleProfile.Id, profilesGroup.Children[0].Id);

        var entitiesGroup = treeVm.RootNodes[2];
        Assert.Single(entitiesGroup.Children);
    }

    [Fact]
    public void PropertyInspector_ShouldDisplayAccurateCylinderProperties()
    {
        // Arrange
        var selectionService = new SelectionService();
        bool modifiedCallbackInvoked = false;
        var inspectorVm = new PropertyInspectorViewModel(selectionService, () => modifiedCallbackInvoked = true);

        var circleProfile = new CircleProfile(new Point3D(10, 20, 0), 15.0);
        var cylinder = new ExtrudeFeature(circleProfile, 40.0);

        // Act: Select cylinder
        selectionService.SelectFeature(cylinder);

        // Assert
        Assert.True(inspectorVm.HasSelection);
        Assert.True(inspectorVm.IsCylinder);
        Assert.Equal(10.0, inspectorVm.CenterX);
        Assert.Equal(20.0, inspectorVm.CenterY);
        Assert.Equal(0.0, inspectorVm.CenterZ);
        Assert.Equal(15.0, inspectorVm.Radius);
        Assert.Equal(30.0, inspectorVm.Diameter);
        Assert.Equal(40.0, inspectorVm.Height);
        Assert.Equal(Math.PI * 15.0 * 15.0, inspectorVm.BaseArea, 3);
        Assert.Equal(Math.PI * 15.0 * 15.0 * 40.0, inspectorVm.Volume, 3);

        // Act: Edit Height to 60.0 mm
        inspectorVm.Height = 60.0;

        // Assert
        Assert.True(modifiedCallbackInvoked);
        Assert.Equal(60.0, cylinder.Depth);
        Assert.Equal(Math.PI * 15.0 * 15.0 * 60.0, inspectorVm.Volume, 3);
    }

    [Fact]
    public void SelectionService_ShouldSynchronizeBidirectionally()
    {
        // Arrange
        var selectionService = new SelectionService();
        var treeVm = new ModelTreeViewModel(selectionService);
        var sketch = new Sketch();

        var circleProfile = new CircleProfile(new Point3D(0, 0, 0), 10.0);
        var cylinder = new ExtrudeFeature(circleProfile, 30.0);
        sketch.AddFeature(cylinder);

        treeVm.BindSketch(sketch);

        // Act 1: Select via Tree Node
        var featureNode = treeVm.RootNodes[0].Children[1];
        treeVm.SelectedNode = featureNode;

        // Assert 1
        Assert.True(cylinder.IsSelected);
        Assert.Equal(cylinder.Id, selectionService.SelectedId);
        Assert.Equal(SelectionType.Feature, selectionService.CurrentSelectionType);

        // Act 2: Select via SelectionService directly (e.g. from 3D Viewport raycast)
        selectionService.ClearSelection(sketch);
        Assert.False(cylinder.IsSelected);
        Assert.Null(treeVm.SelectedNode);

        selectionService.SelectById(sketch, cylinder.Id);

        // Assert 2
        Assert.True(cylinder.IsSelected);
        Assert.NotNull(treeVm.SelectedNode);
        Assert.Equal(cylinder.Id, treeVm.SelectedNode.Id);
    }

    [Fact]
    public void ParametricModification_ShouldUpdateKernelSolidMesh()
    {
        // Arrange
        var kernel = new PureMathGeometryKernel();
        var circleProfile = new CircleProfile(new Point3D(0, 0, 0), 10.0);
        var cylinder = new ExtrudeFeature(circleProfile, 20.0);

        var mesh1 = kernel.BuildSolidMesh(new[] { cylinder });
        Assert.True(mesh1.VertexCount > 0);
        
        // Find max Z in mesh1
        float maxZ1 = 0;
        for (int i = 2; i < mesh1.Positions.Length; i += 3)
        {
            if (mesh1.Positions[i] > maxZ1) maxZ1 = mesh1.Positions[i];
        }
        Assert.Equal(20.0f, maxZ1, 1e-3f);

        // Act: Update depth to 50.0 mm
        cylinder.UpdateDepth(50.0);
        var mesh2 = kernel.BuildSolidMesh(new[] { cylinder });

        // Assert: max Z in mesh2 is 50.0
        float maxZ2 = 0;
        for (int i = 2; i < mesh2.Positions.Length; i += 3)
        {
            if (mesh2.Positions[i] > maxZ2) maxZ2 = mesh2.Positions[i];
        }
        Assert.Equal(50.0f, maxZ2, 1e-3f);
    }

    [Fact]
    public void Raycast_ShouldIntersectAndSelect3DSolidFeature()
    {
        // Arrange: A cylinder centered at (0,0) with radius 10 and height 40
        var kernel = new PureMathGeometryKernel();
        var circleProfile = new CircleProfile(new Point3D(0, 0, 0), 10.0);
        var cylinder = new ExtrudeFeature(circleProfile, 40.0);

        var mockGeo = new Moq.Mock<AvaloniaCadEditor.Domain.Interfaces.IGeometryEngine>();
        var mockSnap = new Moq.Mock<AvaloniaCadEditor.Application.Services.IOSnapService>();
        var selectionService = new SelectionService();
        var vm = new CadViewModel(mockGeo.Object, mockSnap.Object, null, kernel, selectionService);
        vm.Sketch.AddFeature(cylinder);
        vm.RefreshSolidMesh();

        // Ray aimed from (0, 0, 100) pointing downwards towards the top face of the cylinder (0, 0, 40)
        var ray = new Ray(new System.Numerics.Vector3(0, 0, 100), new System.Numerics.Vector3(0, 0, -1));

        // Act
        var hit = vm.Raycast3D(ray);
        if (hit.HasValue && hit.Value.FeatureId.HasValue)
        {
            vm.SelectionService.SelectById(vm.Sketch, hit.Value.FeatureId.Value);
            vm.ModelTree.RebuildTree();
        }

        // Assert
        Assert.True(hit.HasValue);
        Assert.True(cylinder.IsSelected);
        Assert.Equal(cylinder.Id, selectionService.SelectedId);
        Assert.True(vm.PropertyInspector.HasSelection);
        Assert.True(vm.PropertyInspector.IsCylinder);
        Assert.Equal(40.0, vm.PropertyInspector.Height);
    }

    [Fact]
    public void Profile_ContainsPoint_ShouldAccuratelyDetectInsideAndOutside()
    {
        // 1. CircleProfile centered at (10, 10) with Radius = 5
        var circle = new CircleProfile(new Point3D(10, 10, 0), 5.0);
        Assert.True(circle.ContainsPoint(new Point3D(10, 10, 0))); // Center
        Assert.True(circle.ContainsPoint(new Point3D(13, 10, 0))); // Inside
        Assert.True(circle.ContainsPoint(new Point3D(15, 10, 0))); // Perimeter
        Assert.False(circle.ContainsPoint(new Point3D(16, 10, 0))); // Outside

        // 2. RectangleProfile centered at (0, 0) with Width = 20, Height = 10
        var rect = new RectangleProfile(new Point3D(0, 0, 0), 20.0, 10.0);
        Assert.True(rect.ContainsPoint(new Point3D(0, 0, 0)));
        Assert.True(rect.ContainsPoint(new Point3D(8, 4, 0)));
        Assert.False(rect.ContainsPoint(new Point3D(12, 4, 0))); // Outside X
        Assert.False(rect.ContainsPoint(new Point3D(5, 7, 0))); // Outside Y

        // 3. Tessellation
        var (circleVerts, circleIndices) = circle.GetTessellatedFace();
        Assert.True(circleVerts.Count > 0);
        Assert.True(circleIndices.Count > 0);

        var (rectVerts, rectIndices) = rect.GetTessellatedFace();
        Assert.Equal(4, rectVerts.Count);
        Assert.Equal(6, rectIndices.Count); // 2 triangles
    }

    [Fact]
    public void CadViewModel_PointerMove_ShouldTriggerProfileHover()
    {
        var mockGeo = new Moq.Mock<AvaloniaCadEditor.Domain.Interfaces.IGeometryEngine>();
        var mockSnap = new Moq.Mock<AvaloniaCadEditor.Application.Services.IOSnapService>();
        mockSnap.Setup(s => s.GetSnappedPoint(Moq.It.IsAny<Sketch>(), Moq.It.IsAny<Point3D>(), Moq.It.IsAny<double>(), Moq.It.IsAny<AvaloniaCadEditor.Domain.Mesh.TriangleMesh>()))
                .Returns<Sketch, Point3D, double, AvaloniaCadEditor.Domain.Mesh.TriangleMesh>((s, p, t, m) => p);

        var selectionService = new SelectionService();
        var vm = new CadViewModel(mockGeo.Object, mockSnap.Object, null, null, selectionService);

        var circle = new CircleProfile(new Point3D(50, 50, 0), 20.0);
        vm.Sketch.AddProfile(circle);

        // Act 1: Move mouse inside circle
        vm.OnPointerMoved(new Point3D(55, 55, 0));

        // Assert 1: HoveredProfile is detected
        Assert.NotNull(vm.HoveredProfile);
        Assert.Equal(circle.Id, vm.HoveredProfile.Id);

        // Act 2: Move mouse outside circle
        vm.OnPointerMoved(new Point3D(0, 0, 0));

        // Assert 2: HoveredProfile is cleared
        Assert.Null(vm.HoveredProfile);
    }

    [Fact]
    public void ExtrudeCommand_LivePreview_ShouldUpdateSolidMeshDuringMouseMove()
    {
        var kernel = new PureMathGeometryKernel();
        var mockGeo = new Moq.Mock<AvaloniaCadEditor.Domain.Interfaces.IGeometryEngine>();
        var mockSnap = new Moq.Mock<AvaloniaCadEditor.Application.Services.IOSnapService>();
        mockSnap.Setup(s => s.GetSnappedPoint(Moq.It.IsAny<Sketch>(), Moq.It.IsAny<Point3D>(), Moq.It.IsAny<double>(), Moq.It.IsAny<AvaloniaCadEditor.Domain.Mesh.TriangleMesh>()))
                .Returns<Sketch, Point3D, double, AvaloniaCadEditor.Domain.Mesh.TriangleMesh>((s, p, t, m) => p);

        var selectionService = new SelectionService();
        var vm = new CadViewModel(mockGeo.Object, mockSnap.Object, null, kernel, selectionService);

        var circle = new CircleProfile(new Point3D(0, 0, 0), 10.0);
        vm.Sketch.AddProfile(circle);

        var extrudeCmd = new ExtrudeCommand();
        vm.CurrentTool = extrudeCmd;

        // Step 1: Click to pick profile and start defining depth
        vm.OnPointerPressed(new Point3D(0, 0, 0));

        // Step 2: Move mouse upwards to define height 35mm
        vm.OnPointerMoved(new Point3D(0, 35, 0));

        // Assert: SolidMesh now contains the live 3D preview cylinder with height 35mm!
        Assert.NotNull(vm.SolidMesh);
        Assert.True(vm.SolidMesh.TriangleCount > 0);

        float maxZ = 0;
        for (int i = 2; i < vm.SolidMesh.Positions.Length; i += 3)
        {
            if (vm.SolidMesh.Positions[i] > maxZ) maxZ = vm.SolidMesh.Positions[i];
        }
        Assert.Equal(35.0f, maxZ, 1e-2f);
    }
}
