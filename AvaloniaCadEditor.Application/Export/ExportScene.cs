using System.Collections.Generic;
using AvaloniaCadEditor.Domain.Mesh;

namespace AvaloniaCadEditor.Application.Export;

/// <summary>
/// Cena pronta para gravar num arquivo de troca, sem saber o formato: quem escreve COLLADA (ou outro)
/// só percorre esta árvore. Montada pelo <see cref="SceneExportCollector"/>.
/// </summary>
/// <param name="Nodes">Raiz da cena: pastas por tipo de peça e grupos de sólidos soltos.</param>
/// <param name="Components">Geometria das peças, escrita uma vez e referenciada pelas instâncias.</param>
/// <param name="Skipped">Nomes que deviam sair e ficaram sem malha (falha de construção).</param>
/// <param name="FilletsSuppressed">Chanfros e fillets estavam desligados: a geometria exportada não os tem.</param>
public sealed record ExportScene(
    IReadOnlyList<ExportNode> Nodes,
    IReadOnlyList<ExportComponent> Components,
    IReadOnlyList<ExportMaterial> Materials,
    IReadOnlyList<string> Skipped,
    bool FilletsSuppressed);

/// <summary>Geometria de uma peça em coordenadas locais, compartilhada pelas instâncias.</summary>
public sealed record ExportComponent(string Id, string Name, TriangleMesh Mesh, string MaterialId);

/// <summary>
/// Um nó é exatamente um destes: grupo com malha própria (<see cref="Mesh"/>), instância de componente
/// (<see cref="ComponentId"/>) ou pasta que só agrupa (<see cref="Children"/>).
/// </summary>
/// <param name="Matrix">4x4 linha a linha, levando coordenadas do nó para as do pai.</param>
public sealed record ExportNode(
    string Name,
    double[] Matrix,
    TriangleMesh? Mesh,
    string? MaterialId,
    string? ComponentId,
    IReadOnlyList<ExportNode> Children)
{
    public static double[] Identity => [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1];

    public static ExportNode Group(string name, TriangleMesh mesh, string materialId) =>
        new(name, Identity, mesh, materialId, null, []);

    public static ExportNode Instance(string name, double[] matrix, string componentId) =>
        new(name, matrix, null, null, componentId, []);

    public static ExportNode Folder(string name, IReadOnlyList<ExportNode> children) =>
        new(name, Identity, null, null, null, children);
}

/// <summary>Cor difusa (0–255) de um material.</summary>
public sealed record ExportMaterial(string Id, string Name, byte R, byte G, byte B);
