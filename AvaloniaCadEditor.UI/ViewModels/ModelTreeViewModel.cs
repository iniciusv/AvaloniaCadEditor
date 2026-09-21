using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using AvaloniaCadEditor.Application.Services;
using AvaloniaCadEditor.Domain.Entities;
using AvaloniaCadEditor.Domain.Features;
using AvaloniaCadEditor.Domain.Profiles;

namespace AvaloniaCadEditor.UI.ViewModels;

public class ModelTreeViewModel : INotifyPropertyChanged
{
    private readonly ISelectionService _selectionService;
    private bool _isUpdatingSelectionInternally = false;

    public ObservableCollection<TreeItemNode> RootNodes { get; } = new();

    private TreeItemNode? _selectedNode;
    public TreeItemNode? SelectedNode
    {
        get => _selectedNode;
        set
        {
            if (_selectedNode != value)
            {
                _selectedNode = value;
                OnPropertyChanged();

                if (!_isUpdatingSelectionInternally && value != null && value.Id.HasValue && CurrentSketch != null)
                {
                    _selectionService.SelectById(CurrentSketch, value.Id.Value);
                }
            }
        }
    }

    public Sketch? CurrentSketch { get; private set; }

    public ModelTreeViewModel(ISelectionService selectionService)
    {
        _selectionService = selectionService;
        _selectionService.SelectionChanged += OnGlobalSelectionChanged;
    }

    public void BindSketch(Sketch sketch)
    {
        CurrentSketch = sketch;
        RebuildTree();
    }

    public void RebuildTree()
    {
        if (CurrentSketch == null) return;

        RootNodes.Clear();

        string iconFolder = "M10,4H4C2.89,4 2,4.89 2,6V18A2,2 0 0,0 4,20H20A2,2 0 0,0 22,18V8C22,6.89 21.1,6 20,6H12L10,4Z";
        string iconCube = "M21,16.5C21,16.88 20.79,17.21 20.47,17.38L12.57,21.82C12.41,21.94 12.21,22 12,22C11.79,22 11.59,21.94 11.43,21.82L3.53,17.38C3.21,17.21 3,16.88 3,16.5V7.5C3,7.12 3.21,6.79 3.53,6.62L11.43,2.18C11.59,2.06 11.79,2 12,2C12.21,2 12.41,2.06 12.57,2.18L20.47,6.62C20.79,6.79 21,7.12 21,7.5V16.5M12,4.15L5,8.09L12,12.03L19,8.09L12,4.15M4.5,15.97L11.25,19.79V13.16L4.5,9.33V15.97M19.5,15.97V9.33L12.75,13.16V19.79L19.5,15.97Z";
        string iconCylinder = "M12,2A7,3 0 0,0 5,5C5,6.66 8.13,8 12,8C15.87,8 19,6.66 19,5C19,3.34 15.87,2 12,2M5,5V19C5,20.66 8.13,22 12,22C15.87,22 19,20.66 19,19V5C19,5 19,5 19,5C19,6.66 15.87,8 12,8C8.13,8 5,6.66 5,5Z";
        string iconCircle = "M12,2A10,10 0 0,0 2,12A10,10 0 0,0 12,22A10,10 0 0,0 22,12A10,10 0 0,0 12,2M12,20A8,8 0 0,1 4,12A8,8 0 0,1 12,4A8,8 0 0,1 20,12A8,8 0 0,1 12,20Z";
        string iconRect = "M4,6V19H20V6H4M18,17H6V8H18V17Z";
        string iconPolygon = "M12,2L22,12L17,22H7L2,12L12,2M12,4.83L4.83,12L8.27,19.17H15.73L19.17,12L12,4.83Z";
        string iconLine = "M21.71,20.29L20.29,21.71L2.29,3.71L3.71,2.29L21.71,20.29Z";

        // Find consumed features
        var consumedFeatures = new HashSet<IFeature>();
        foreach (var feature in CurrentSketch.Features)
        {
            if (feature is BooleanFeature bf)
            {
                foreach (var dep in bf.GetDependencies())
                {
                    if (dep != null) consumedFeatures.Add(dep);
                }
            }
        }

        var rootFeatures = CurrentSketch.Features.Where(f => !consumedFeatures.Contains(f)).ToList();

        // 1. Features 3D Group
        var featuresGroup = new TreeItemNode($"Volumes 3D ({CurrentSketch.Features.Count})", iconFolder, TreeItemCategory.FeatureGroup);
        foreach (var feature in rootFeatures)
        {
            var node = BuildFeatureNode(feature);
            featuresGroup.Children.Add(node);
        }
        RootNodes.Add(featuresGroup);

        // 2. Profiles 2D Group
        var profilesGroup = new TreeItemNode($"Perfis 2D ({CurrentSketch.Profiles.Count})", iconFolder, TreeItemCategory.ProfileGroup);
        foreach (var profile in CurrentSketch.Profiles)
        {
            string icon = profile.Classification switch
            {
                ProfileClassification.Circle => iconCircle,
                ProfileClassification.Rectangle => iconRect,
                _ => iconPolygon
            };
            string name = $"Perfil {profile.Classification} (Área: {profile.Area:F1})";
            var node = new TreeItemNode(name, icon, TreeItemCategory.ProfileItem, profile.Id, profile);
            node.IsVisible = profile.IsVisible;
            node.VisibilityChanged += (s, e) => OnNodeVisibilityChanged();
            profilesGroup.Children.Add(node);
        }
        RootNodes.Add(profilesGroup);

        // 3. Sketch Entities 2D Group
        var entitiesGroup = new TreeItemNode($"Linhas e Entidades ({CurrentSketch.Entities.Count})", iconFolder, TreeItemCategory.EntityGroup);
        int lineCount = 1;
        int circleCount = 1;
        foreach (var entity in CurrentSketch.Entities)
        {
            string icon = iconPolygon;
            string name = "Entidade";
            if (entity is LineEntity l)
            {
                icon = iconLine;
                name = $"Linha {lineCount++} ({l.Length:F1}mm)";
            }
            else if (entity is CircleEntity c)
            {
                icon = iconCircle;
                name = $"Círculo {circleCount++} (R={c.Radius:F1}mm)";
            }

            var node = new TreeItemNode(name, icon, TreeItemCategory.EntityItem, entity.Id, entity);
            node.IsVisible = entity.IsVisible;
            node.VisibilityChanged += (s, e) => OnNodeVisibilityChanged();
            entitiesGroup.Children.Add(node);
        }
        RootNodes.Add(entitiesGroup);

        SyncSelectionFromService();
    }

    private TreeItemNode BuildFeatureNode(IFeature feature)
    {
        string iconCube = "M21,16.5C21,16.88 20.79,17.21 20.47,17.38L12.57,21.82C12.41,21.94 12.21,22 12,22C11.79,22 11.59,21.94 11.43,21.82L3.53,17.38C3.21,17.21 3,16.88 3,16.5V7.5C3,7.12 3.21,6.79 3.53,6.62L11.43,2.18C11.59,2.06 11.79,2 12,2C12.21,2 12.41,2.06 12.57,2.18L20.47,6.62C20.79,6.79 21,7.12 21,7.5V16.5M12,4.15L5,8.09L12,12.03L19,8.09L12,4.15M4.5,15.97L11.25,19.79V13.16L4.5,9.33V15.97M19.5,15.97V9.33L12.75,13.16V19.79L19.5,15.97Z";
        string iconCylinder = "M12,2A7,3 0 0,0 5,5C5,6.66 8.13,8 12,8C15.87,8 19,6.66 19,5C19,3.34 15.87,2 12,2M5,5V19C5,20.66 8.13,22 12,22C15.87,22 19,20.66 19,19V5C19,5 19,5 19,5C19,6.66 15.87,8 12,8C8.13,8 5,6.66 5,5Z";
        string iconFolder = "M10,4H4C2.89,4 2,4.89 2,6V18A2,2 0 0,0 4,20H20A2,2 0 0,0 22,18V8C22,6.89 21.1,6 20,6H12L10,4Z";

        string icon = iconCube;
        string name = feature.Name;
        if (feature is ExtrudeFeature ef)
        {
            if (ef.Profile is CircleProfile cp)
            {
                icon = iconCylinder;
                name = $"Cilindro (R={cp.Radius:F1}, H={ef.Depth:F1})";
            }
            else
            {
                name = $"Prisma (H={ef.Depth:F1})";
            }
        }
        else if (feature is BooleanFeature bf)
        {
            name = $"{bf.Operation} ({bf.Name})";
        }
        else if (feature is WorkPlaneFeature wpf)
        {
            string iconRect = "M4,6V19H20V6H4M18,17H6V8H18V17Z";
            icon = iconRect;
            name = wpf.Name;
        }

        var node = new TreeItemNode(name, icon, TreeItemCategory.FeatureItem, feature.Id, feature);
        node.IsVisible = feature.IsVisible;
        node.HasError = feature.HasError;
        node.ErrorMessage = feature.ErrorMessage;
        node.VisibilityChanged += (s, e) => OnNodeVisibilityChanged();

        if (feature is BooleanFeature booleanFeature)
        {
            // Base group
            var baseGroup = new TreeItemNode($"Base", iconFolder, TreeItemCategory.BooleanBaseGroup, null, booleanFeature);
            if (booleanFeature.BaseFeature != null)
            {
                baseGroup.Children.Add(BuildFeatureNode(booleanFeature.BaseFeature));
            }
            node.Children.Add(baseGroup);

            // Tools group
            string toolsName = booleanFeature.Operation == BooleanOperationType.Subtract ? "Ferramentas (Corte)" :
                               booleanFeature.Operation == BooleanOperationType.Union ? "Ferramentas (União)" : "Ferramentas (Interseção)";
            var toolsGroup = new TreeItemNode(toolsName, iconFolder, TreeItemCategory.BooleanToolGroup, null, booleanFeature);
            foreach (var tool in booleanFeature.ToolFeatures)
            {
                toolsGroup.Children.Add(BuildFeatureNode(tool));
            }
            node.Children.Add(toolsGroup);
        }

        return node;
    }

    private void OnGlobalSelectionChanged()
    {
        SyncSelectionFromService();
    }

    private void SyncSelectionFromService()
    {
        _isUpdatingSelectionInternally = true;
        try
        {
            var selectedId = _selectionService.SelectedId;
            if (!selectedId.HasValue)
            {
                SelectedNode = null;
                ClearSelection(RootNodes);
                return;
            }

            ClearSelection(RootNodes);
            if (FindAndSelectNode(RootNodes, selectedId.Value, out var foundNode))
            {
                SelectedNode = foundNode;
            }
        }
        finally
        {
            _isUpdatingSelectionInternally = false;
        }
    }

    private void ClearSelection(IEnumerable<TreeItemNode> nodes)
    {
        foreach (var node in nodes)
        {
            node.IsSelected = false;
            ClearSelection(node.Children);
        }
    }

    private bool FindAndSelectNode(IEnumerable<TreeItemNode> nodes, Guid targetId, out TreeItemNode? foundNode)
    {
        foundNode = null;
        foreach (var node in nodes)
        {
            if (node.Id == targetId)
            {
                node.IsSelected = true;
                foundNode = node;
                return true;
            }
            if (FindAndSelectNode(node.Children, targetId, out foundNode))
            {
                node.IsExpanded = true;
                return true;
            }
        }
        return false;
    }

    public void MoveFeatureUp(TreeItemNode node)
    {
        if (CurrentSketch == null || node.Tag is not IFeature feature) return;
        
        var featuresList = CurrentSketch.Features.ToList();
        int currentIndex = featuresList.IndexOf(feature);
        
        if (currentIndex > 0)
        {
            CurrentSketch.ReorderFeature(feature, currentIndex - 1);
        }
    }

    public void MoveFeatureDown(TreeItemNode node)
    {
        if (CurrentSketch == null || node.Tag is not IFeature feature) return;
        
        var featuresList = CurrentSketch.Features.ToList();
        int currentIndex = featuresList.IndexOf(feature);
        
        if (currentIndex >= 0 && currentIndex < featuresList.Count - 1)
        {
            CurrentSketch.ReorderFeature(feature, currentIndex + 1);
        }
    }

    public event EventHandler? NodeVisibilityChanged;

    private void OnNodeVisibilityChanged()
    {
        NodeVisibilityChanged?.Invoke(this, EventArgs.Empty);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
