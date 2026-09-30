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

        public event Action<AvaloniaCadEditor.Domain.Features.WorkPlaneFeature>? WorkPlaneDoubleClicked;

    public void HandleDoubleTap(TreeItemNode node)
    {
        if (node.Tag is AvaloniaCadEditor.Domain.Features.WorkPlaneFeature wpf)
        {
            WorkPlaneDoubleClicked?.Invoke(wpf);
        }
    }

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

                if (!_isUpdatingSelectionInternally && value != null && CurrentSketch != null)
                {
                    if (value.Id.HasValue)
                        _selectionService.SelectById(CurrentSketch, value.Id.Value);
                    else
                        _selectionService.ClearSelection(CurrentSketch);
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

    /// <summary>Disparado antes/depois de a árvore ser reconstruída (a view usa para preservar o scroll).</summary>
    public event Action? TreeRebuilding;
    public event Action? TreeRebuilt;

    public void RebuildTree()
    {
        if (CurrentSketch == null) return;

        TreeRebuilding?.Invoke();
        try
        {
            RebuildTreeCore();
        }
        finally
        {
            TreeRebuilt?.Invoke();
        }
    }

    private void RebuildTreeCore()
    {

        // 1. Save expansion states before clearing
        var expandedIds = new HashSet<Guid>();
        var expandedCategories = new HashSet<string>();
        SaveExpansionState(RootNodes, expandedIds, expandedCategories);

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
                foreach (var dep in bf.Dependencies)
                {
                    if (dep != null) consumedFeatures.Add(dep);
                }
            }
        }

        var rootFeatures = CurrentSketch.Features.Where(f => !consumedFeatures.Contains(f)).ToList();

        // 1. Geometria de Referência Group
        var refGroup = new TreeItemNode($"Geometria de Referência", iconFolder, TreeItemCategory.FeatureGroup);
        refGroup.PropertyChanged += OnNodePropertyChanged;
        
        int lineCount = 1;
        int circleCount = 1;

        foreach (var wp in CurrentSketch.WorkPlanes)
        {
            var wpNode = new TreeItemNode(wp.Name, iconCube, TreeItemCategory.FeatureItem, wp.Id, wp);
            wpNode.IsVisible = wp.IsVisible;
            wpNode.PropertyChanged += (s, e) => {
                if (e.PropertyName == nameof(TreeItemNode.IsVisible)) {
                    wp.IsVisible = wpNode.IsVisible;
                    OnNodeVisibilityChanged();
                }
            };
            wpNode.PropertyChanged += OnNodePropertyChanged;

            if (CurrentSketch.Profiles != null)
            {
                var profilesOnPlane = CurrentSketch.Profiles.Where(p => p.Plane.Equals(wp.Plane));
                foreach (var profile in profilesOnPlane)
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
                    node.PropertyChanged += (s, e) =>
                    {
                        if (e.PropertyName == nameof(TreeItemNode.IsVisible))
                        {
                            profile.IsVisible = node.IsVisible;
                            OnNodeVisibilityChanged();
                        }
                    };
                    node.PropertyChanged += OnNodePropertyChanged;
                    wpNode.Children.Add(node);
                }
            }

            if (CurrentSketch.Entities != null)
            {
                var entitiesOnPlane = CurrentSketch.Entities.Where(en => en.Plane.Equals(wp.Plane));
                foreach (var entity in entitiesOnPlane)
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
                    node.PropertyChanged += OnNodePropertyChanged;
                    wpNode.Children.Add(node);
                }
            }
            
            refGroup.Children.Add(wpNode);
        }

        RootNodes.Add(refGroup);

        // 2. Features 3D Group
        var featuresGroup = new TreeItemNode($"Volumes 3D ({CurrentSketch.Features.Count})", iconFolder, TreeItemCategory.FeatureGroup);
        featuresGroup.PropertyChanged += OnNodePropertyChanged;
        foreach (var feature in rootFeatures)
        {
            var node = BuildFeatureNode(feature);
            featuresGroup.Children.Add(node);
        }
        RootNodes.Add(featuresGroup);

        // 2. Restore expansion states
        RestoreExpansionState(RootNodes, expandedIds, expandedCategories);

        SyncSelectionFromService();
    }

    private void SaveExpansionState(IEnumerable<TreeItemNode> nodes, HashSet<Guid> expandedIds, HashSet<string> expandedCategories)
    {
        foreach (var node in nodes)
        {
            if (node.IsExpanded)
            {
                if (node.Id.HasValue) expandedIds.Add(node.Id.Value);
                else expandedCategories.Add(node.Name);
            }
            SaveExpansionState(node.Children, expandedIds, expandedCategories);
        }
    }

    private void RestoreExpansionState(IEnumerable<TreeItemNode> nodes, HashSet<Guid> expandedIds, HashSet<string> expandedCategories)
    {
        foreach (var node in nodes)
        {
            if (node.Id.HasValue && expandedIds.Contains(node.Id.Value))
            {
                node.IsExpanded = true;
            }
            else if (!node.Id.HasValue && expandedCategories.Contains(node.Name))
            {
                node.IsExpanded = true;
            }
            else
            {
                node.IsExpanded = false; // Collapse by default if not previously expanded
            }
            RestoreExpansionState(node.Children, expandedIds, expandedCategories);
        }
    }

        private TreeItemNode BuildFeatureNode(IFeature feature, string? prefix = null)
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
                name = $"Cilindro (R={cp.Radius:F1}, H={ef.Height:F1})";
            }
            else
            {
                name = $"Prisma (H={ef.Height:F1})";
            }
        }
        else if (feature is BooleanFeature bf)
        {
            name = $"[{bf.Operation}] {bf.Name}";
        }
        else if (feature is PartInstance instance)
        {
            name = $"{instance.Name} ({instance.PartTypeName})";
        }
        else if (feature is WorkPlaneFeature wpf)
        {
            string iconRect = "M4,6V19H20V6H4M18,17H6V8H18V17Z";
            icon = iconRect;
            name = wpf.Name;
        }

        if (prefix != null)
        {
            name = $"[{prefix}] {name}";
        }

        var node = new TreeItemNode(name, icon, TreeItemCategory.FeatureItem, feature.Id, feature);
        node.IsVisible = feature.IsVisible;
        node.VisibilityChanged += (s, e) => OnNodeVisibilityChanged();
        node.PropertyChanged += OnNodePropertyChanged;

        if (feature is BooleanFeature booleanFeature)
        {
            foreach (var b in booleanFeature.BaseFeatures)
            {
                node.Children.Add(BuildFeatureNode(b, "Base"));
            }

            string opPrefix = booleanFeature.Operation == BooleanOperationType.Subtract ? "-" :
                              booleanFeature.Operation == BooleanOperationType.Union ? "+" : "n";

            foreach (var tool in booleanFeature.ToolFeatures)
            {
                node.Children.Add(BuildFeatureNode(tool, opPrefix));
            }
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

    public event EventHandler? NodeVisibilityChanged;

    private void OnNodeVisibilityChanged()
    {
        NodeVisibilityChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnNodePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TreeItemNode.IsSelected))
        {
            if (sender is TreeItemNode node && node.IsSelected)
            {
                SelectedNode = node;
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

