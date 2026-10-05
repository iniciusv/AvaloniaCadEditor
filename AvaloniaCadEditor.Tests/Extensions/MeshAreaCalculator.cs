using System;
using AvaloniaCadEditor.Domain.Mesh;

namespace AvaloniaCadEditor.Tests.Extensions;

/// <summary>
/// Área de superfície de uma malha triangular.
/// <para>
/// Existe para o que o volume NÃO consegue distinguir: um fuse que de fato fundiu dois sólidos e um
/// que devolveu um compound com os dois apenas encostados. Os dois têm exatamente o mesmo volume,
/// mas o compound guarda as duas faces coincidentes da junção, que a fusão de verdade elimina —
/// então a área denuncia. É a única verificação possível aqui: o OcctNet.Wrapper não expõe contagem
/// de sólidos nem costura.
/// </para>
/// </summary>
public static class MeshAreaCalculator
{
    public static double ComputeArea(TriangleMesh mesh)
    {
        if (mesh == null || mesh.TriangleCount == 0) return 0;

        double area = 0;
        for (int i = 0; i < mesh.Indices.Length; i += 3)
        {
            uint i0 = mesh.Indices[i] * 3;
            uint i1 = mesh.Indices[i + 1] * 3;
            uint i2 = mesh.Indices[i + 2] * 3;

            double ax = mesh.Positions[i1] - mesh.Positions[i0];
            double ay = mesh.Positions[i1 + 1] - mesh.Positions[i0 + 1];
            double az = mesh.Positions[i1 + 2] - mesh.Positions[i0 + 2];

            double bx = mesh.Positions[i2] - mesh.Positions[i0];
            double by = mesh.Positions[i2 + 1] - mesh.Positions[i0 + 1];
            double bz = mesh.Positions[i2 + 2] - mesh.Positions[i0 + 2];

            double cx = ay * bz - az * by;
            double cy = az * bx - ax * bz;
            double cz = ax * by - ay * bx;

            area += 0.5 * Math.Sqrt(cx * cx + cy * cy + cz * cz);
        }
        return area;
    }

    /// <summary>
    /// Área da seção circular COMO O KERNEL A CONSTRÓI: um polígono regular inscrito de
    /// <paramref name="segments"/> lados (ProfileSegments), não o círculo exato. É a área de uma
    /// tampa, e portanto o degrau que separa "fundiu" de "não fundiu".
    /// </summary>
    public static double InscribedPolygonArea(double radius, int segments) =>
        0.5 * segments * radius * radius * Math.Sin(2.0 * Math.PI / segments);

    /// <summary>
    /// Perímetro da mesma seção poligonal inscrita. Numa varredura de seção circular a área lateral
    /// é, por Pappus, este perímetro vezes o comprimento do caminho — o centroide do contorno anda
    /// exatamente sobre o caminho.
    /// </summary>
    public static double InscribedPolygonPerimeter(double radius, int segments) =>
        2.0 * segments * radius * Math.Sin(Math.PI / segments);
}
