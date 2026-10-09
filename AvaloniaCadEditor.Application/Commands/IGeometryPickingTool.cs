using AvaloniaCadEditor.Domain.Entities;
using AvaloniaCadEditor.Domain.ValueObjects;

namespace AvaloniaCadEditor.Application.Commands;

/// <summary>
/// Ferramenta cujo clique tem tolerância de acerto, em unidades de mundo. Quem converte pixels em
/// unidades é a UI (ela é a única que conhece a câmera), então o valor é empurrado para cá.
/// </summary>
public interface IHitTolerantTool
{
    double HitTolerance { get; set; }
}

/// <summary>
/// Ferramenta que ESCOLHE geometria já desenhada com o clique, em vez de desenhar.
/// <para>
/// Existe para a UI despachar por CAPACIDADE e não por tipo. Um <c>is XCommand</c> a mais em cada
/// ponto de decisão é como o <c>CadViewModel</c> virou god object (ARCHITECTURE §6.3); o codebase
/// já resolve isso assim em <c>ITransformPreviewTool</c> e <c>IGuideProvider</c>.
/// </para>
/// </summary>
public interface IGeometryPickingTool : IHitTolerantTool
{
    /// <summary>
    /// Consome o clique como escolha de geometria. Devolve true quando escolheu algo — aí a UI não
    /// leva o clique adiante.
    /// <para>
    /// Recebe o ponto CRU, sem snap: com o snap ligado, clicar dentro de uma região pularia para a
    /// ponta de uma linha vizinha e a região nunca seria escolhida.
    /// </para>
    /// </summary>
    bool TryPickAt(Sketch sketch, Point3D rawPoint);

    /// <summary>
    /// A ferramenta também escolhe pelo retângulo de seleção (clicar e arrastar), e não só pelo
    /// clique. Quem arrasta é a UI; a ferramenta colhe o que ficou marcado.
    /// </summary>
    bool UsesSelectionBox { get; }
}
