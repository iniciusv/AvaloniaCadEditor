using System;
using System.Linq;
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
        Assert.Single(featuresGroup.Children);
        Assert.Equal("🥫", featuresGroup.Children[0].Icon);
        Assert.Equal(cylinderFeature.Id, featuresGroup.Children[0].Id);

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
        var featureNode = treeVm.RootNodes[0].Children[0];
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
}
