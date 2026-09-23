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

    public void RebuildTree()
    {
        if (CurrentSketch == null) return;

        RootNodes.Clear();

        string iconPlane = "M21,16.5C21,16.88 20.79,17.21 20.47,17.38L12.57,21.82C12.41,21.94 12.21,22 12,22C11.79,22 11.59,21.94 11.43,21.82L3.53,17.38C3.21,17.21 3,16.88 3,16.5V7.5C3,7.12 3.21,6.79 3.53,6.62L11.43,2.18C11.59,2.06 11.79,2 12,2C12.21,2 12.41,2.06 12.57,2.18L20.47,6.62C20.79,6.79 21,7.12 21,7.5V16.5M12,4.15L5,8.09L12,12.03L19,8.09L12,4.15M4.5,15.97L11.25,19.79V13.16L4.5,9.33V15.97M19.5,15.97V9.33L12.75,13.16V19.79L19.5,15.97Z";

        foreach (var wp in CurrentSketch.WorkPlanes)
        {
            var wpNode = new TreeItemNode(wp.Name, iconPlane, TreeItemCategory.FeatureItem, wp.Id, wp)
            {
                IsVisible = wp.IsVisible
            };
            wpNode.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(TreeItemNode.IsVisible))
                {
                    wp.IsVisible = wpNode.IsVisible;
                    OnNodeVisibilityChanged();
                }
            };
            wpNode.PropertyChanged += OnNodePropertyChanged;

            RootNodes.Add(wpNode);
        }

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
