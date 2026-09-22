using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
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
        
        // This stops ghost drag-and-drops
        AddHandler(PointerReleasedEvent, OnPointerReleasedAnywhere, Avalonia.Interactivity.RoutingStrategies.Tunnel | Avalonia.Interactivity.RoutingStrategies.Bubble, true);
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _dragStartPoint = e.GetPosition(this);
        _dragStartEvent = e;
        var pointerUpdateKind = e.GetCurrentPoint(this).Properties.PointerUpdateKind;
        
        if (pointerUpdateKind == PointerUpdateKind.LeftButtonPressed)
        {
            var control = e.Source as Control;
            
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

        var draggedNode = e.DataTransfer.TryGetValue(NodeFormat);
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

        var draggedNode = e.DataTransfer.TryGetValue(NodeFormat);
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
                // Old base goes back to root automatically because it was unlinked
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
