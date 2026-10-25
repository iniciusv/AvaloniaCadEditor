using System.Collections.Generic;
using System.Numerics;
using AvaloniaCadEditor.Domain.Entities;
using AvaloniaCadEditor.Domain.Mesh;
using AvaloniaCadEditor.Domain.Profiles;
using AvaloniaCadEditor.Domain.ValueObjects;
using FluentAssertions;
using Xunit;

namespace AvaloniaCadEditor.Tests.Isolation;

/// <summary>
/// Em ortho a origem do raio fica a ~3·far atrás do olho (<see cref="CameraState.ScreenPointToRay"/>), e
/// <c>Origin + Direction·t</c> com t ≈ 30000 tem erro de alguns milésimos ao longo do raio. O ponto
/// atingido vira a origem do plano da face, e o <see cref="FaceProfile"/> só aceita triângulos a até
/// 1e-3 desse plano: com o ponto fora dele, a face sumia do realce, embora o plano de trabalho
/// (que tolera a diferença) saísse certo.
/// </summary>
public class OrthoFacePickTests
{
    [Theory]
    [InlineData(StandardView.Front)]
    [InlineData(StandardView.Top)]
    [InlineData(StandardView.Right)]
    [InlineData(StandardView.Isometric)]
    public void PontoAtingido_EmOrtho_FicaNoPlanoDaFace(StandardView view)
    {
        var min = new Vector3(-41.37f, 23.91f, 7.13f);
        var max = new Vector3(-11.37f, 43.91f, 17.13f);
        var mesh = Box(min, max);
        var camera = new CameraState { Target = (min + max) / 2, Distance = 120, IsOrthographic = true, AspectRatio = 1.6f };
        camera.SetStandardView(view);
        var viewport = new Vector2(1600, 1000);

        var random = new System.Random(3);
        int hits = 0;
        for (int k = 0; k < 400; k++)
        {
            var screen = new Vector2(600 + (float)random.NextDouble() * 400, 350 + (float)random.NextDouble() * 300);
            var hit = camera.ScreenPointToRay(screen, viewport).IntersectMesh(mesh);
            if (hit is not { } h) continue;
            hits++;

            var face = new FaceProfile(new AvaloniaCadEditor.Domain.Features.BoxFeature(Point3D.Origin, 1, 1, 1), new WorkPlane(h.Normal, h.Point), mesh);
            face.GetTessellatedFace().Indices.Should().NotBeEmpty($"a face sob ({screen.X:0}, {screen.Y:0}) tem que realçar");
        }
        hits.Should().BeGreaterThan(30);
    }

    private static TriangleMesh Box(Vector3 a, Vector3 b)
    {
        var positions = new List<float>();
        var normals = new List<float>();
        var indices = new List<uint>();
        void Quad(Vector3 v0, Vector3 v1, Vector3 v2, Vector3 v3, Vector3 n)
        {
            uint start = (uint)(positions.Count / 3);
            foreach (var v in new[] { v0, v1, v2, v3 })
            {
                positions.AddRange(new[] { v.X, v.Y, v.Z });
                normals.AddRange(new[] { n.X, n.Y, n.Z });
            }
            indices.AddRange(new[] { start, start + 1, start + 2, start, start + 2, start + 3 });
        }
        Quad(new(a.X, a.Y, a.Z), new(a.X, b.Y, a.Z), new(b.X, b.Y, a.Z), new(b.X, a.Y, a.Z), -Vector3.UnitZ);
        Quad(new(a.X, a.Y, b.Z), new(b.X, a.Y, b.Z), new(b.X, b.Y, b.Z), new(a.X, b.Y, b.Z), Vector3.UnitZ);
        Quad(new(a.X, a.Y, a.Z), new(b.X, a.Y, a.Z), new(b.X, a.Y, b.Z), new(a.X, a.Y, b.Z), -Vector3.UnitY);
        Quad(new(a.X, b.Y, a.Z), new(a.X, b.Y, b.Z), new(b.X, b.Y, b.Z), new(b.X, b.Y, a.Z), Vector3.UnitY);
        Quad(new(a.X, a.Y, a.Z), new(a.X, a.Y, b.Z), new(a.X, b.Y, b.Z), new(a.X, b.Y, a.Z), -Vector3.UnitX);
        Quad(new(b.X, a.Y, a.Z), new(b.X, b.Y, a.Z), new(b.X, b.Y, b.Z), new(b.X, a.Y, b.Z), Vector3.UnitX);
        return new TriangleMesh(positions.ToArray(), normals.ToArray(), indices.ToArray());
    }
}
