using System.Collections.Generic;
using AvaloniaCadEditor.Domain.ValueObjects;

namespace AvaloniaCadEditor.Domain.Mesh;

/// <summary>
/// União de polígonos 2D (X/Y; Z é ignorado), usada pelo contorno de <see cref="PlaneProjection.Outline"/>.
/// O Domain só declara a necessidade; a implementação, com uma biblioteca de recorte de polígonos, fica na
/// Infrastructure.
/// </summary>
public interface IPolygonUnion
{
    /// <summary>
    /// Laços fechados (sem repetir o primeiro ponto no fim) do contorno da união: os externos e os furos.
    /// </summary>
    IReadOnlyList<IReadOnlyList<Point3D>> Union(IEnumerable<IReadOnlyList<Point3D>> polygons);
}
