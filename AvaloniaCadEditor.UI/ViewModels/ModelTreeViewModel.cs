using System;
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

        // 1. Features 3D Group
        var featuresGroup = new TreeItemNode($"Volumes 3D ({CurrentSketch.Features.Count})", "🧊", TreeItemCategory.FeatureGroup);
        foreach (var feature in CurrentSketch.Features)
        {
            string icon = "🧊";
            string name = feature.Name;
            if (feature is ExtrudeFeature ef)
            {
                if (ef.Profile is CircleProfile cp)
                {
                    icon = "🥫"; // Cylinder
                    name = $"Cilindro (R={cp.Radius:F1}, H={ef.Depth:F1})";
                }
                else
                {
                    name = $"Prisma (H={ef.Depth:F1})";
                }
            }

            var node = new TreeItemNode(name, icon, TreeItemCategory.FeatureItem, feature.Id, feature);
            featuresGroup.Children.Add(node);
        }
        RootNodes.Add(featuresGroup);

        // 2. Profiles 2D Group
        var profilesGroup = new TreeItemNode($"Perfis 2D ({CurrentSketch.Profiles.Count})", "📐", TreeItemCategory.ProfileGroup);
        foreach (var profile in CurrentSketch.Profiles)
        {
            string icon = profile.Classification switch
            {
                ProfileClassification.Circle => "⭕",
                ProfileClassification.Rectangle => "🔲",
                _ => "📐"
            };
            string name = $"Perfil {profile.Classification} (Área: {profile.Area:F1})";
            var node = new TreeItemNode(name, icon, TreeItemCategory.ProfileItem, profile.Id, profile);
            profilesGroup.Children.Add(node);
        }
        RootNodes.Add(profilesGroup);

        // 3. Sketch Entities 2D Group
        var entitiesGroup = new TreeItemNode($"Linhas e Entidades ({CurrentSketch.Entities.Count})", "✏️", TreeItemCategory.EntityGroup);
        int lineCount = 1;
        int circleCount = 1;
        foreach (var entity in CurrentSketch.Entities)
        {
            string icon = "📏";
            string name = "Entidade";
            if (entity is LineEntity l)
            {
                icon = "📏";
                name = $"Linha {lineCount++} ({l.Length:F1}mm)";
            }
            else if (entity is CircleEntity c)
            {
                icon = "⭕";
                name = $"Círculo {circleCount++} (R={c.Radius:F1}mm)";
            }

            var node = new TreeItemNode(name, icon, TreeItemCategory.EntityItem, entity.Id, entity);
            entitiesGroup.Children.Add(node);
        }
        RootNodes.Add(entitiesGroup);

        SyncSelectionFromService();
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
                return;
            }

            // Find matching node in children
            foreach (var group in RootNodes)
            {
                foreach (var item in group.Children)
                {
                    if (item.Id == selectedId.Value)
                    {
                        item.IsSelected = true;
                        SelectedNode = item;
                        return;
                    }
                    else
                    {
                        item.IsSelected = false;
                    }
                }
            }
        }
        finally
        {
            _isUpdatingSelectionInternally = false;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
