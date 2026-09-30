using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using System.Linq;
using Avalonia.VisualTree;
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

        DataContextChanged += OnDataContextChanged;
    }

    private ModelTreeViewModel? _subscribedVm;
    private Vector _savedScrollOffset;

    private void OnDataContextChanged(object? sender, System.EventArgs e)
    {
        if (_subscribedVm != null)
        {
            _subscribedVm.TreeRebuilding -= OnTreeRebuilding;
            _subscribedVm.TreeRebuilt -= OnTreeRebuilt;
        }
        _subscribedVm = DataContext as ModelTreeViewModel;
        if (_subscribedVm != null)
        {
            _subscribedVm.TreeRebuilding += OnTreeRebuilding;
            _subscribedVm.TreeRebuilt += OnTreeRebuilt;
        }
    }

    private Avalonia.Controls.ScrollViewer? FindTreeScrollViewer() =>
        this.FindControl<TreeView>("Tree")?.GetVisualDescendants()
            .OfType<Avalonia.Controls.ScrollViewer>().FirstOrDefault();

    // A árvore é reconstruída a cada mudança (inclusive de visibilidade); sem isso o scroll voltaria ao topo.
    private void OnTreeRebuilding()
    {
        _savedScrollOffset = FindTreeScrollViewer()?.Offset ?? default;
    }

    private void OnTreeRebuilt()
    {
        var offset = _savedScrollOffset;
        if (offset == default) return;

        void Restore()
        {
            var sv = FindTreeScrollViewer();
            if (sv != null) sv.Offset = offset;
        }

        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            Restore();
            // O layout dos itens recriados pode terminar só depois; reaplica uma vez.
            Avalonia.Threading.Dispatcher.UIThread.Post(Restore, Avalonia.Threading.DispatcherPriority.Background);
        }, Avalonia.Threading.DispatcherPriority.Loaded);
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
                if (bf.ToolFeatures.Contains(featureToRemove))
                {
                    bf.RemoveToolFeature(featureToRemove);
                }
                // Uma base só nunca é removida daqui para não deixar a booleana sem base;
                // com várias bases, tirar uma ainda deixa pelo menos uma (RemoveBaseFeature garante isso).
                else if (bf.BaseFeatures.Count > 1 && bf.BaseFeatures.Contains(featureToRemove))
                {
                    bf.RemoveBaseFeature(featureToRemove);
                }
            }
        }
    }
}
