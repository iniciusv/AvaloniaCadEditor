using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using System.Linq;
using AvaloniaCadEditor.UI.ViewModels;
using AvaloniaCadEditor.Domain.Features;

namespace AvaloniaCadEditor.UI.Views;

public partial class ModelTreeView : UserControl
{
    private static readonly DataFormat<TreeItemNode> NodeFormat = DataFormat.CreateInProcessFormat<TreeItemNode>("TreeItemNode");
    private Point _dragStartPoint;
    private bool _isDragging = false;
    private TreeItemNode? _draggedNode;
    private PointerPressedEventArgs? _dragStartEvent;

    public ModelTreeView()
    {
        InitializeComponent();
        AddHandler(DragDrop.DropEvent, OnDrop);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        
        // Register pointer events globally to intercept handled events from TreeViewItem
        AddHandler(PointerPressedEvent, OnPointerPressed, Avalonia.Interactivity.RoutingStrategies.Tunnel | Avalonia.Interactivity.RoutingStrategies.Bubble, true);
        AddHandler(PointerMovedEvent, OnPointerMoved, Avalonia.Interactivity.RoutingStrategies.Tunnel | Avalonia.Interactivity.RoutingStrategies.Bubble, true);
        AddHandler(PointerReleasedEvent, OnPointerReleasedAnywhere, Avalonia.Interactivity.RoutingStrategies.Tunnel | Avalonia.Interactivity.RoutingStrategies.Bubble, true);
    }
    
    private void OnDoubleTapped(object? sender, Avalonia.Input.TappedEventArgs e)
    {
        var control = e.Source as Avalonia.Controls.Control;
        var node = control?.DataContext as AvaloniaCadEditor.UI.ViewModels.TreeItemNode;
        if (node != null)
        {
            var vm = this.DataContext as AvaloniaCadEditor.UI.ViewModels.ModelTreeViewModel;
            vm?.HandleDoubleTap(node);
        }
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _dragStartPoint = e.GetPosition(this);
        _dragStartEvent = e;
        var pointerUpdateKind = e.GetCurrentPoint(this).Properties.PointerUpdateKind;
        
        if (pointerUpdateKind == PointerUpdateKind.LeftButtonPressed)
        {
            var sourceObj = e.Source;
            var control = sourceObj as Control;
            
            // Do not start drag if they clicked a button (like the expand/collapse arrow or visibility eye)
            if (control is Avalonia.Controls.Primitives.ToggleButton) return;

            var dataContext = control?.DataContext as TreeItemNode;
            
            if (dataContext != null && dataContext.Category == TreeItemCategory.FeatureItem)
            {
                _draggedNode = dataContext;
                _isDragging = true;
            }
        }
    }

    private async void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isDragging || _draggedNode == null || _dragStartEvent == null) return;

        var point = e.GetPosition(this);
        var diff = _dragStartPoint - point;

        if (System.Math.Abs(diff.X) > 3 || System.Math.Abs(diff.Y) > 3)
        {
            _isDragging = false; // Drag started
            
            var dragData = new DataTransfer();
            var item = new DataTransferItem();
            item.Set(NodeFormat, _draggedNode);
            dragData.Add(item);
            
            var result = await DragDrop.DoDragDropAsync(_dragStartEvent, dragData, DragDropEffects.Move);
            _draggedNode = null;
            _dragStartEvent = null;
        }
    }

    private void OnPointerReleasedAnywhere(object? sender, PointerReleasedEventArgs e)
    {
        _isDragging = false;
        _draggedNode = null;
        _dragStartEvent = null;
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        if (e.DataTransfer == null || !e.DataTransfer.Contains(NodeFormat))
        {
            e.DragEffects = DragDropEffects.None;
            return;
        }

        var draggedNode = e.DataTransfer.TryGetValue(NodeFormat) as TreeItemNode;
        var control = e.Source as Control;
        var targetNode = control?.DataContext as TreeItemNode;

        if (draggedNode == null || targetNode == null || draggedNode == targetNode)
        {
            e.DragEffects = DragDropEffects.None;
            return;
        }
        
        // Prevent dropping a BooleanFeature into its own descendants
        if (targetNode.Tag is BooleanFeature targetBoolFeature && draggedNode.Tag == targetBoolFeature)
        {
            e.DragEffects = DragDropEffects.None;
            return;
        }

        if (targetNode.Category == TreeItemCategory.BooleanBaseGroup ||
            targetNode.Category == TreeItemCategory.BooleanToolGroup ||
            targetNode.Category == TreeItemCategory.FeatureGroup)
        {
            e.DragEffects = DragDropEffects.Move;
        }
        else
        {
            e.DragEffects = DragDropEffects.None;
        }
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        if (e.DataTransfer == null || !e.DataTransfer.Contains(NodeFormat)) return;

        var draggedNode = e.DataTransfer.TryGetValue(NodeFormat) as TreeItemNode;
        var control = e.Source as Control;
        var targetNode = control?.DataContext as TreeItemNode;

        if (draggedNode == null || targetNode == null || draggedNode == targetNode) return;
        
        var featureToMove = draggedNode.Tag as IFeature;
        if (featureToMove == null) return;

        var viewModel = DataContext as ModelTreeViewModel;
        if (viewModel == null || viewModel.CurrentSketch == null) return;

        // 1. Remove from its current parent (if it's inside a boolean feature)
        RemoveFeatureFromParents(viewModel.CurrentSketch, featureToMove);

        // 2. Add to the new target
        if (targetNode.Category == TreeItemCategory.BooleanBaseGroup)
        {
            var boolFeature = targetNode.Tag as BooleanFeature;
            if (boolFeature != null)
            {
                var oldBase = boolFeature.ChangeBaseFeature(featureToMove);
            }
        }
        else if (targetNode.Category == TreeItemCategory.BooleanToolGroup)
        {
            var boolFeature = targetNode.Tag as BooleanFeature;
            if (boolFeature != null)
            {
                boolFeature.AddToolFeature(featureToMove);
            }
        }
        // If dropped in FeatureGroup (Volumes 3D), it's already removed from parents, so it just becomes root again.

        viewModel.RebuildTree();

        // Let's force a refresh in CadViewModel by hacking a visibility toggle so it fires the event
        featureToMove.IsVisible = !featureToMove.IsVisible;
        featureToMove.IsVisible = !featureToMove.IsVisible;
    }
    
    private void RemoveFeatureFromParents(AvaloniaCadEditor.Domain.Entities.Sketch sketch, IFeature featureToRemove)
    {
        foreach (var f in sketch.Features)
        {
            if (f is BooleanFeature bf)
            {
                // Note: we avoid removing the base feature if dropped outside of a replacement to prevent invalid states
                if (bf.ToolFeatures.Contains(featureToRemove))
                {
                    bf.RemoveToolFeature(featureToRemove);
                }
            }
        }
    }
}
