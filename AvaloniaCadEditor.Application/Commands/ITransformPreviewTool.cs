using System.Collections.Generic;
using System.Numerics;
using AvaloniaCadEditor.Domain.Features;

namespace AvaloniaCadEditor.Application.Commands;

/// <summary>
/// Uma ferramenta que arrasta features na tela antes de consolidar a edição (Mover, Rotacionar):
/// o render 3D usa isso para desenhar as features selecionadas já deslocadas/giradas, sem mexer
/// no sketch de verdade até o clique final.
/// </summary>
public interface ITransformPreviewTool
{
    /// <summary>Features sendo arrastadas nesse momento (vazio fora do arrasto).</summary>
    IEnumerable<IFeature> DraggedFeatures { get; }

    /// <summary>Transformação de preview a aplicar na malha dessas features (identidade fora do arrasto).</summary>
    Matrix4x4 PreviewTransform { get; }

    /// <summary>
    /// Modo cópia (Ctrl): o original fica parado e só uma cópia fantasma recebe o
    /// <see cref="PreviewTransform"/>.
    /// </summary>
    bool IsCopyPreview { get; }
}
