using System;
using System.Collections.Generic;
using AvaloniaCadEditor.Domain.ValueObjects;
using OcctNet.Wrapper;

namespace AvaloniaCadEditor.Infrastructure.OpenCascade;

/// <summary>
/// Primitivas de "perfil → sólido" do OCCT, sem nenhum conhecimento de <c>IFeature</c>, cache ou
/// cena. Existem aqui porque são compartilhadas por mais de um construtor: a revolução serve tanto
/// ao tronco de cone (<c>BuildCone</c>, em <see cref="OpenCascadeGeometryKernel"/>) quanto ao
/// anel de corte do fillet de cilindro (em <see cref="OcctFilletBuilder"/>).
/// </summary>
internal static class OcctProfileGeometry
{
    /// <summary>
    /// Revolve 360° um perfil 2D (r = distância ao eixo, z = altura ao longo do eixo) em torno do
    /// eixo (<paramref name="origin"/>, <paramref name="dir"/>). O perfil é fechado implicitamente
    /// (o último ponto liga no primeiro) e PODE encostar no eixo (r = 0): uma aresta sobre o eixo é
    /// invariante pela rotação e o OCCT não gera face para ela — é assim que sai o ápice de um cone
    /// em <c>BuildCone</c>. Um perfil que NÃO encosta no eixo gera um anel, que é o que
    /// <c>OcctFilletBuilder.CreateCylinderCutter</c> quer.
    /// </summary>
    public static OcctShape RevolveProfile(IReadOnlyList<(double r, double z)> points2D, Point3D origin, Vector3D dir)
    {
        // Um vetor qualquer perpendicular ao eixo: é o "r" do perfil levado para o espaço.
        Vector3D u;
        if (Math.Abs(dir.X) > 0.1 || Math.Abs(dir.Y) > 0.1)
            u = new Vector3D(-dir.Y, dir.X, 0);
        else
            u = new Vector3D(1, 0, 0);

        double uLen = Math.Sqrt(u.X * u.X + u.Y * u.Y + u.Z * u.Z);
        u = new Vector3D(u.X / uLen, u.Y / uLen, u.Z / uLen);

        var vertices = new System.Collections.Generic.List<Point3D>();
        foreach (var p in points2D)
        {
            vertices.Add(new Point3D(
                origin.X + u.X * p.r + dir.X * p.z,
                origin.Y + u.Y * p.r + dir.Y * p.z,
                origin.Z + u.Z * p.r + dir.Z * p.z
            ));
        }

        // Pontos coincidentes consecutivos (ex.: o ápice, que é topo e eixo ao mesmo tempo) virariam
        // uma aresta de comprimento zero. 1e-9, e não o 1e-5 de BuildExtrude: num fillet de raio
        // pequeno os pontos do cutter podem estar legitimamente a menos de 1e-5 um do outro, e
        // mesclá-los corromperia o perfil do chanfro.
        for (int i = vertices.Count - 1; i > 0; i--)
        {
            if (vertices[i].DistanceTo(vertices[i - 1]) < 1e-9) vertices.RemoveAt(i);
        }
        if (vertices.Count > 1 && vertices[0].DistanceTo(vertices[^1]) < 1e-9) vertices.RemoveAt(vertices.Count - 1);
        if (vertices.Count < 3) return null;

        // Calculate polygon normal using Newell's method to check winding order
        double nx = 0, ny = 0, nz = 0;
        int n = vertices.Count;
        for (int i = 0; i < n; i++)
        {
            var p1 = vertices[i];
            var p2 = vertices[(i + 1) % n];
            nx += (p1.Y - p2.Y) * (p1.Z + p2.Z);
            ny += (p1.Z - p2.Z) * (p1.X + p2.X);
            nz += (p1.X - p2.X) * (p1.Y + p2.Y);
        }

        // Cross product of u and dir gives the normal of the plane
        var expectedNormal = new Vector3D(
            u.Y * dir.Z - u.Z * dir.Y,
            u.Z * dir.X - u.X * dir.Z,
            u.X * dir.Y - u.Y * dir.X
        );

        if (nx * expectedNormal.X + ny * expectedNormal.Y + nz * expectedNormal.Z < 0)
        {
            vertices.Reverse();
        }

        var edges = new System.Collections.Generic.List<OcctEdge>();
        for (int i = 0; i < vertices.Count; i++)
        {
            var p1 = vertices[i];
            var p2 = vertices[(i + 1) % vertices.Count];
            var edge = new OcctEdge(
                new OcctPointCoordinates(p1.X, p1.Y, p1.Z),
                new OcctPointCoordinates(p2.X, p2.Y, p2.Z)
            );
            edges.Add(edge);
        }

        var wire = new OcctWire(edges.ToArray());
        var face = new OcctFace(wire);

        var axis = new OcctAxis1d(
            new OcctPointCoordinates(origin.X, origin.Y, origin.Z),
            new OcctVector3d(dir.X, dir.Y, dir.Z)
        );

        return face.Revolve(axis, 2.0 * Math.PI);
    }
}
