using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using AvaloniaCadEditor.Domain.Entities;
using AvaloniaCadEditor.Domain.ValueObjects;
using AvaloniaCadEditor.UI.ViewModels;
using AvaloniaCadEditor.Application.Commands;

namespace AvaloniaCadEditor.UI.Controls;

public class ViewportControl : Control
{
	private double _zoom = 1.0;
	private Point _pan = new Point(0, 0);

	private Point? _lastPanPos;
	private bool _isPanning;

	private readonly IPen _entityPen = new Pen(Brushes.White, 2.0);
	private readonly IPen _selectedPen = new Pen(Brushes.Yellow, 2.0);
	private readonly IPen _previewPen = new Pen(Brushes.LightSkyBlue, 2.0, new DashStyle(new double[] { 4, 4 }, 0));
	private readonly IPen _gridPen = new Pen(new SolidColorBrush(Color.FromArgb(50, 255, 255, 255)), 1.0);

	public CadViewModel? ViewModel => DataContext as CadViewModel;

	public ViewportControl()
	{
		ClipToBounds = true;
		Focusable = true;
	}

	protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
	{
		base.OnPointerWheelChanged(e);

		// Zoom logic
		double zoomFactor = 1.1;
		if (e.Delta.Y < 0) zoomFactor = 1.0 / zoomFactor;

		var mousePos = e.GetPosition(this);
		var worldBefore = ScreenToWorld(mousePos);

		_zoom *= zoomFactor;
		if (ViewModel != null) ViewModel.WorldUnitsPerPixel = 1.0 / _zoom;

		var worldAfter = ScreenToWorld(mousePos);
		_pan = new Point(_pan.X + (worldAfter.X - worldBefore.X) * _zoom,
						 _pan.Y - (worldAfter.Y - worldBefore.Y) * _zoom);

		InvalidateVisual();
	}

	protected override void OnPointerPressed(PointerPressedEventArgs e)
	{
		base.OnPointerPressed(e);
		this.Focus();
		var point = e.GetCurrentPoint(this);

		if (point.Properties.IsMiddleButtonPressed)
		{
			_isPanning = true;
			_lastPanPos = point.Position;
		}
		else if (point.Properties.IsLeftButtonPressed)
		{
			bool isCtrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
			var worldPt = ScreenToWorld(point.Position);
			ViewModel?.OnPointerPressed(worldPt, isCtrl);
			InvalidateVisual();
		}
	}

	protected override void OnPointerMoved(PointerEventArgs e)
	{
		base.OnPointerMoved(e);
		var point = e.GetCurrentPoint(this);

		if (_isPanning && _lastPanPos.HasValue)
		{
			var delta = point.Position - _lastPanPos.Value;
			_pan = new Point(_pan.X + delta.X, _pan.Y + delta.Y);
			_lastPanPos = point.Position;
		}
		else
		{
			var worldPt = ScreenToWorld(point.Position);
			ViewModel?.OnPointerMoved(worldPt);
		}

		InvalidateVisual();
	}

	protected override void OnPointerReleased(PointerReleasedEventArgs e)
	{
		base.OnPointerReleased(e);
		if (e.InitialPressMouseButton == MouseButton.Middle)
		{
			_isPanning = false;
			_lastPanPos = null;
		}
	}

	protected override void OnKeyDown(KeyEventArgs e)
	{
		base.OnKeyDown(e);
		ViewModel?.OnKeyDown(e.Key.ToString());
		InvalidateVisual();
	}

	protected override void OnKeyUp(KeyEventArgs e)
	{
		base.OnKeyUp(e);
		ViewModel?.OnKeyUp(e.Key.ToString());
		InvalidateVisual();
	}

	private Point3D ScreenToWorld(Point screenPt)
	{
		var center = new Point(Bounds.Width / 2, Bounds.Height / 2);
		double x = (screenPt.X - center.X - _pan.X) / _zoom;
		double y = -(screenPt.Y - center.Y - _pan.Y) / _zoom;
		return new Point3D(x, y, 0);
	}

	private Point WorldToScreen(Point3D worldPt)
	{
		var center = new Point(Bounds.Width / 2, Bounds.Height / 2);
		double x = center.X + _pan.X + (worldPt.X * _zoom);
		double y = center.Y + _pan.Y - (worldPt.Y * _zoom);
		return new Point(x, y);
	}

	public override void Render(DrawingContext context)
	{
		base.Render(context);

		// Draw background
		context.FillRectangle(new SolidColorBrush(Color.FromRgb(30, 30, 30)), Bounds);

		// Draw basic crosshair/grid (Origin)
		var origin = WorldToScreen(new Point3D(0, 0));
		context.DrawLine(_gridPen, new Point(0, origin.Y), new Point(Bounds.Width, origin.Y));
		context.DrawLine(_gridPen, new Point(origin.X, 0), new Point(origin.X, Bounds.Height));

		if (ViewModel == null) return;

		// Draw Sketch Entities
		foreach (var entity in ViewModel.Sketch.Entities)
		{
			var pen = entity.IsSelected ? _selectedPen : _entityPen;
			DrawEntity(context, entity, pen);
		}

		// Draw Previews
		var tool = ViewModel.CurrentTool;
		if (tool != null)
		{
			foreach (var preview in tool.GetPreviewEntities())
			{
				DrawEntity(context, preview, _previewPen);
			}
		}

		// Draw Snapped Point Marker
		var snapPt = WorldToScreen(ViewModel.CurrentSnappedPoint);
		context.DrawRectangle(null, new Pen(Brushes.Yellow, 1.5), new Rect(snapPt.X - 5, snapPt.Y - 5, 10, 10));

		// Draw Copy Mode Indicator (Plus sign next to cursor)
		if (tool is MoveTool moveTool && moveTool.IsCopyMode)
		{
			var plusCenter = new Point(snapPt.X + 15, snapPt.Y + 15);
			context.DrawLine(_entityPen, new Point(plusCenter.X - 4, plusCenter.Y), new Point(plusCenter.X + 4, plusCenter.Y));
			context.DrawLine(_entityPen, new Point(plusCenter.X, plusCenter.Y - 4), new Point(plusCenter.X, plusCenter.Y + 4));
		}
	}

	private void DrawEntity(DrawingContext context, Entity entity, IPen pen)
	{
		if (entity is LineEntity line)
		{
			var p1 = WorldToScreen(line.StartPoint);
			var p2 = WorldToScreen(line.EndPoint);
			context.DrawLine(pen, p1, p2);
		}
		else if (entity is CircleEntity circle)
		{
			var center = WorldToScreen(circle.Center);
			double radiusScreen = circle.Radius * _zoom;
			context.DrawEllipse(null, pen, center, radiusScreen, radiusScreen);
		}
	}
}
