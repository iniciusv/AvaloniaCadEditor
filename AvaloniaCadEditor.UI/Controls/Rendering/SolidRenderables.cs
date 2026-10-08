using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using AvaloniaCadEditor.Application.Commands;
using AvaloniaCadEditor.Domain.Features;
using AvaloniaCadEditor.Domain.Mesh;
using AvaloniaCadEditor.UI.ViewModels;

namespace AvaloniaCadEditor.UI.Controls.Rendering;

/// <summary>Um sólido a desenhar neste frame e o papel dele na cena.</summary>
/// <param name="IsPreview">Preview de extrusão ou fantasma de cópia (Ctrl no Mover/Rotacionar).</param>
/// <param name="IsToolFeature">Ferramenta de peça (não está no sketch, é desenhada translúcida).</param>
public readonly record struct SolidRenderable(IFeature Feature, bool IsPreview, bool IsToolFeature);

/// <summary>
/// O que o modo sombreado (<see cref="SolidFeatureRenderPass"/>) e o wireframe
/// (<see cref="WireframeRenderPass"/>) decidem igual: quais sólidos entram no frame, de onde vem a
/// malha de cada um, que cor ele tem e com que transformação é desenhado.
/// </summary>
public static class SolidRenderables
{
    public static List<SolidRenderable> Collect(CadViewModel vm)
    {
        var result = new List<SolidRenderable>();
        foreach (var f in vm.Sketch.Features)
        {
            if (f.IsVisible) result.Add(new SolidRenderable(f, false, false));
        }

        foreach (var tf in vm.ActiveToolFeatures)
        {
            result.Add(new SolidRenderable(tf, false, true));
        }

        if (vm.CurrentTool is ExtrudeCommand extCmd)
        {
            var preview = extCmd.GetPreviewFeature();
            if (preview != null)
            {
                result.Add(new SolidRenderable(preview, true, false));
            }
        }

        // Modo cópia (Ctrl no Mover/Rotacionar): o original continua desenhado no lugar e uma cópia
        // fantasma (cor de preview) é que acompanha o mouse.
        if (vm.CurrentTool is ITransformPreviewTool { IsCopyPreview: true } previewTool)
        {
            foreach (var dragged in previewTool.DraggedFeatures)
            {
                result.Add(new SolidRenderable(dragged, true, false));
            }
        }

        return result;
    }

    public static TriangleMesh? GetMesh(CadViewModel vm, SolidRenderable item)
    {
        // Ferramentas de peça não estão no sketch: a malha vem do próprio objeto.
        return item.IsToolFeature
            ? vm.MeshManager.GetMeshFor(item.Feature)
            : vm.GetFeatureMesh(item.Feature.Id, forceRebuild: false);
    }

    /// <summary>Matriz de modelo: identidade, ou a do arraste em andamento do Mover/Rotacionar.</summary>
    public static Matrix4x4 GetModelTransform(CadViewModel vm, SolidRenderable item, Matrix4x4 identity)
    {
        var transform = identity;
        if (vm.CurrentTool is ITransformPreviewTool previewTool &&
            previewTool.DraggedFeatures.Contains(item.Feature) &&
            (!previewTool.IsCopyPreview || item.IsPreview))
        {
            transform = transform * previewTool.PreviewTransform;
        }
        return transform;
    }

    /// <summary>Cor da face e da aresta pelo estado do sólido (ferramenta, preview, seleção, hover, padrão).</summary>
    public static ((float R, float G, float B) Face, (float R, float G, float B) Edge) ResolveColors(CadViewModel vm, SolidRenderable item)
    {
        var feature = item.Feature;
        float faceR, faceG, faceB;
        float edgeR, edgeG, edgeB;

        if (item.IsToolFeature && (vm.HighlightedToolId == feature.Id || vm.HoveredFeatureId == feature.Id))
        {
            faceR = 1.0f; faceG = 0.55f; faceB = 0.1f; // Ferramenta clicada/sob o mouse: laranja
            edgeR = 1.0f; edgeG = 0.8f; edgeB = 0.2f;
        }
        else if (item.IsToolFeature)
        {
            faceR = 0.9f; faceG = 0.1f; faceB = 0.1f; // Transparent Red
            edgeR = 1.0f; edgeG = 0.0f; edgeB = 0.0f;
        }
        else if (item.IsPreview)
        {
            faceR = 0.20f; faceG = 0.85f; faceB = 0.60f; // Vibrant Emerald Creation Feedback
            edgeR = 0.05f; edgeG = 0.95f; edgeB = 0.65f;
        }
        else if (feature.IsSelected)
        {
            faceR = 0.10f; faceG = 0.40f; faceB = 1.00f; // Strong Vivid Blue Selection Highlight
            edgeR = 0.00f; edgeG = 0.20f; edgeB = 0.80f;

            // Com cor própria, mistura com o azul: a seleção continua visível e a cor escolhida também.
            if (feature.Color != null)
            {
                var (r, g, b, _) = ViewportShaderManager.ParseHexColor(feature.Color, (faceR, faceG, faceB, 1f));
                faceR = (faceR + r) / 2; faceG = (faceG + g) / 2; faceB = (faceB + b) / 2;
            }
        }
        else if (vm.HoveredFeatureId == feature.Id)
        {
            faceR = 1.00f; faceG = 0.65f; faceB = 0.15f; // Strong Amber/Gold for Hover 3D face
            edgeR = 1.00f; edgeG = 0.80f; edgeB = 0.20f;
        }
        else
        {
            faceR = 0.92f; faceG = 0.92f; faceB = 0.95f; // Near white / light grey Base Color
            edgeR = 0.60f; edgeG = 0.60f; edgeB = 0.65f;

            if (feature.Color != null)
            {
                (faceR, faceG, faceB, _) = ViewportShaderManager.ParseHexColor(feature.Color, (faceR, faceG, faceB, 1f));
            }

            // Dim background objects if we are in a custom workplane
            if (!vm.ActiveWorkPlane.Equals(AvaloniaCadEditor.Domain.ValueObjects.WorkPlane.XY))
            {
                faceR *= 0.6f; faceG *= 0.6f; faceB *= 0.6f;
                edgeR *= 0.6f; edgeG *= 0.6f; edgeB *= 0.6f;
            }
        }

        return ((faceR, faceG, faceB), (edgeR, edgeG, edgeB));
    }
}
