using System;
using System.Collections.Generic;
using System.Linq;
using AvaloniaCadEditor.Domain.Features;
using AvaloniaCadEditor.Domain.Mesh;
using AvaloniaCadEditor.Domain.ValueObjects;
using AvaloniaCadEditor.Infrastructure.OpenCascade;
using FluentAssertions;

namespace AvaloniaCadEditor.Tests.BehaviorTests;

/// <summary>
/// Chanfro em todas as arestas de um prisma pentagonal com quinas de 90°, 150° e 60° (a chapa do
/// bico pulverizador). Duas falhas deixavam faces sem chanfro: a quina de 150° (30° entre as normais,
/// o CreaseAngle padrão) era classificada como suave e nem virava candidata, e o perfil do cortador
/// supunha quina de 90°, pondo os pontos do chanfro dentro do material nas outras.
/// </summary>
public class FilletPrismaQuinasObliquasTests
{
    private static readonly Point3D[] Contorno =
    {
        new(0, 0, 0), new(27, 15.58846, 0), new(0, 31.17691, 0), new(-20, 31.17691, 0), new(-20, 0, 0)
    };

    [Fact]
    public void ChanfroEmTodasAsArestas_DePrismaComQuinasDe150e60Graus_ChanfraTodasAsArestas()
    {
        var plano = new WorkPlane(new Point3D(-9, 35.58846, 10), new Vector3D(0, 0, -1), new Vector3D(1, 0, 0));
        var prisma = new PrismFeature(Contorno, height: 5, plane: plano) { Name = "prisma" };
        var chanfro = new FilletFeature("chanfro", prisma, 2) { Mode = FilletMode.Bevel, ApplyToAllEdges = true };

        var mesh = new OpenCascadeGeometryKernel().BuildFeatureMesh(chanfro);
        var areas = AreaPorNormal(mesh);

        // Normais externas das laterais, no mundo: o contorno é anti-horário no plano, cujo eixo Y é o -Y do mundo.
        var laterais = new List<Vector3D>();
        for (int i = 0; i < Contorno.Length; i++)
        {
            var a = Contorno[i];
            var b = Contorno[(i + 1) % Contorno.Length];
            laterais.Add(new Vector3D(b.Y - a.Y, b.X - a.X, 0).Normalize());
        }
        laterais.Should().Contain(n => n.Y > 0.99, "sanidade: a lateral de y local 0 olha para +Y no mundo");

        for (int i = 0; i < laterais.Count; i++)
        {
            var lateral = laterais[i];
            AreaNaNormal(areas, lateral + Vector3D.UnitZ).Should().BeGreaterThan(5, $"o chanfro de cima da lateral {i} existe");
            AreaNaNormal(areas, lateral - Vector3D.UnitZ).Should().BeGreaterThan(5, $"o chanfro de baixo da lateral {i} existe");

            var vizinha = laterais[(i + 1) % laterais.Count];
            AreaNaNormal(areas, lateral + vizinha).Should().BeGreaterThan(0.5, $"a quina vertical entre as laterais {i} e {(i + 1) % laterais.Count} é chanfrada");
        }
    }

    private static List<(Vector3D Normal, double Area)> AreaPorNormal(TriangleMesh mesh)
    {
        var result = new List<(Vector3D, double)>();
        for (int t = 0; t < mesh.Indices.Length; t += 3)
        {
            Vector3D P(uint i) => new(mesh.Positions[i * 3], mesh.Positions[i * 3 + 1], mesh.Positions[i * 3 + 2]);
            var a = P(mesh.Indices[t]);
            var n = (P(mesh.Indices[t + 1]) - a).CrossProduct(P(mesh.Indices[t + 2]) - a);
            if (n.Length > 1e-9) result.Add((n.Normalize(), n.Length / 2));
        }
        return result;
    }

    private static double AreaNaNormal(List<(Vector3D Normal, double Area)> areas, Vector3D normal)
    {
        var alvo = normal.Normalize();
        return areas.Where(f => f.Normal.DotProduct(alvo) > 0.999).Sum(f => f.Area);
    }
}
