using System;
using System.Globalization;
using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using AvaloniaCadEditor.UI.ViewModels;

namespace AvaloniaCadEditor.UI.Controls;

/// <summary>
/// Legenda discreta no canto da viewport: um triedro X/Y/Z que gira junto com a câmera, com as
/// mesmas cores dos eixos desenhados na cena (vermelho/verde/azul). Mostra os eixos do referencial
/// que a grade usa — o do plano do sketch, o da peça isolada ou o do mundo — para saber qual é qual
/// sem ter que adivinhar pela grade.
/// </summary>
public sealed class AxisTriadOverlay : Control
{
    private const double Size = 78;
    private const double Gutter = 12;
    private const double ArmLength = 24;
    private const double FontSize = 11;

    /// <summary>Abaixo disto (px) o eixo aponta quase para o olho (ou para longe dele): vira um ponto
    /// no centro, em vez de um traço de comprimento nulo.</summary>
    private const double MinArm = 3;

    private static readonly Color[] Colors =
    {
        Color.FromRgb(235, 85, 85),
        Color.FromRgb(105, 205, 105),
        Color.FromRgb(95, 150, 245),
    };

    private static readonly string[] Names = { "X", "Y", "Z" };

    private static readonly Typeface LabelFace = new(FontFamily.Default, FontStyle.Normal, FontWeight.SemiBold);

    private SilkViewportControl? _viewport;

    public AxisTriadOverlay()
    {
        IsHitTestVisible = false;
        Width = Size;
        Height = Size;
        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right;
        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Bottom;
        Margin = new Thickness(Gutter);
    }

    public void AttachTo(SilkViewportControl viewport)
    {
        if (_viewport != null) _viewport.FrameRendered -= InvalidateVisual;
        _viewport = viewport;
        _viewport.FrameRendered += InvalidateVisual;
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (_viewport?.Camera is not { } camera) return;

        var (xAxis, yAxis, zAxis) = ResolveAxes(_viewport.ViewModel);

        // Base da tela: para onde o olho olha, o "direita" e o "cima" da imagem.
        var forward = Vector3.Normalize(camera.Target - camera.Position);
        var right = Vector3.Normalize(Vector3.Cross(forward, camera.Up));
        var up = Vector3.Cross(right, forward);

        var center = new Point(Size / 2, Size / 2);
        var axes = new[] { xAxis, yAxis, zAxis };

        // De trás para a frente: o eixo que aponta para longe do olho é desenhado primeiro e
        // fica mais apagado, para o da frente cobrir o cruzamento.
        var order = new[] { 0, 1, 2 };
        Array.Sort(order, (a, b) => Vector3.Dot(axes[b], forward).CompareTo(Vector3.Dot(axes[a], forward)));

        foreach (int i in order)
        {
            float away = Vector3.Dot(axes[i], forward);
            var tip = new Vector2(Vector3.Dot(axes[i], right), -Vector3.Dot(axes[i], up)) * (float)ArmLength;
            double alpha = away > 0.05f ? 0.45 : 0.9;
            var color = Color.FromArgb((byte)(alpha * 255), Colors[i].R, Colors[i].G, Colors[i].B);
            var brush = new SolidColorBrush(color);

            var end = new Point(center.X + tip.X, center.Y + tip.Y);
            bool degenerate = tip.Length() < MinArm;

            if (degenerate)
            {
                context.DrawEllipse(brush, null, center, 2.5, 2.5);
            }
            else
            {
                context.DrawLine(new Pen(brush, 1.75, lineCap: PenLineCap.Round), center, end);
            }

            var text = new FormattedText(Names[i], CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                LabelFace, FontSize, brush);

            // O rótulo vai um pouco além da ponta, na mesma direção; sem direção (eixo de frente),
            // fica ao lado do ponto central.
            var dir = degenerate ? new Vector2(1, 1) : Vector2.Normalize(tip);
            var label = new Point(
                end.X + dir.X * 7 - text.Width / 2,
                end.Y + dir.Y * 7 - text.Height / 2);
            context.DrawText(text, label);
        }
    }

    /// <summary>Os mesmos eixos que o <see cref="Rendering.GridRenderPass"/> desenha.</summary>
    private static (Vector3 X, Vector3 Y, Vector3 Z) ResolveAxes(CadViewModel? vm)
    {
        var wp = vm?.CurrentMode == EditorMode.SketchEdit
            ? vm.ActiveWorkPlane
            : vm?.DisplayFrame?.XY ?? AvaloniaCadEditor.Domain.ValueObjects.WorkPlane.XY;

        return (
            new Vector3((float)wp.XAxis.X, (float)wp.XAxis.Y, (float)wp.XAxis.Z),
            new Vector3((float)wp.YAxis.X, (float)wp.YAxis.Y, (float)wp.YAxis.Z),
            new Vector3((float)wp.Normal.X, (float)wp.Normal.Y, (float)wp.Normal.Z));
    }
}
