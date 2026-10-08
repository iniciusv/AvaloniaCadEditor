using AvaloniaCadEditor.Analysis;

namespace AvaloniaCadEditor.Application.ReferenceGuides;

/// <summary>
/// Tolerâncias de <see cref="IReferenceGuideService"/>. As da análise em si vivem em
/// <see cref="AnalysisOptions"/> (a mesma usada pelo desenho de cotas); aqui fica só o que é da
/// apresentação das guias.
/// </summary>
public sealed class ReferenceGuideOptions
{
    /// <summary>Tolerâncias da análise de relações (alinhamento, quase-alinhamento, arcos, simetria).</summary>
    public AnalysisOptions Analysis { get; init; } = new();

    /// <summary>Margem (unidades do mundo) além do membro mais externo ao desenhar o alcance de uma guia.</summary>
    public double LineMargin { get; init; } = 2.0;
}
