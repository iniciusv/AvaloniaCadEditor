using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using AvaloniaCadEditor.Application.EditContext;
using AvaloniaCadEditor.Application.Interfaces;
using AvaloniaCadEditor.Application.Scenes;
using AvaloniaCadEditor.Application.Services;
using AvaloniaCadEditor.Domain.Features;
using AvaloniaCadEditor.Domain.Mesh;
using AvaloniaCadEditor.Domain.Profiles;
using AvaloniaCadEditor.Domain.ValueObjects;
using AvaloniaCadEditor.Infrastructure.OpenCascade;
using AvaloniaCadEditor.Infrastructure.Scripting;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace AvaloniaCadEditor.Tests.Scenes;

/// <summary>
/// Extrudar dentro de uma peça aberta sobre a cena real (AiCore), só em memória: a placa do carrinho Y
/// (girada 180° em Z) e uma extrusão vertical do eixo Z (deitada 90° em X). A face é tirada da malha local
/// da peça e levada ao mundo pelo Placement, como o clique a veria; o sólido tem de voltar ao Build() com
/// as mesmas medidas locais. O tempo de cada inserção sai no log do teste.
/// </summary>
public class PartSolidInserterAiCoreTests
{
    private readonly ITestOutputHelper _output;

    public PartSolidInserterAiCoreTests(ITestOutputHelper output) => _output = output;

    private static string AiCoreFolder([CallerFilePath] string? thisFile = null)
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(thisFile!)!);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "AiCore")))
        {
            dir = dir.Parent;
        }

        dir.Should().NotBeNull("este teste lê a cena AiCore do repo, então precisa achá-la");
        return Path.Combine(dir!.FullName, "AiCore");
    }

    private sealed record Box(double MinX, double MaxX, double MinY, double MaxY, double MinZ, double MaxZ);

    private static Box Bounds(TriangleMesh mesh)
    {
        var p = mesh.Positions;
        var xs = Enumerable.Range(0, mesh.VertexCount).Select(i => (double)p[3 * i]).ToArray();
        var ys = Enumerable.Range(0, mesh.VertexCount).Select(i => (double)p[3 * i + 1]).ToArray();
        var zs = Enumerable.Range(0, mesh.VertexCount).Select(i => (double)p[3 * i + 2]).ToArray();
        return new Box(xs.Min(), xs.Max(), ys.Min(), ys.Max(), zs.Min(), zs.Max());
    }

    /// <summary>Um quadrado de lado <paramref name="side"/> no centro do topo local (z máximo) da peça, no mundo.</summary>
    private static (FaceProfile Face, Box LocalSquare) TopSquareInWorld(Box local, Placement placement, double side)
    {
        double cx = (local.MinX + local.MaxX) / 2, cy = (local.MinY + local.MaxY) / 2, h = side / 2;
        var corners = new[]
        {
            new Point3D(cx - h, cy - h, local.MaxZ), new Point3D(cx + h, cy - h, local.MaxZ),
            new Point3D(cx + h, cy + h, local.MaxZ), new Point3D(cx - h, cy + h, local.MaxZ),
        }.Select(placement.Apply).ToArray();
        var normal = placement.ApplyVector(Vector3D.UnitZ).Normalize();

        var positions = corners.SelectMany(c => new[] { (float)c.X, (float)c.Y, (float)c.Z }).ToArray();
        var normals = corners.SelectMany(_ => new[] { (float)normal.X, (float)normal.Y, (float)normal.Z }).ToArray();
        var mesh = new TriangleMesh(positions, normals, new uint[] { 0, 1, 2, 0, 2, 3 });
        var center = placement.Apply(new Point3D(cx, cy, local.MaxZ));

        var face = new FaceProfile(new BoxFeature(Point3D.Origin, 1, 1, 1), new WorkPlane(normal, center), mesh);
        return (face, new Box(cx - h, cx + h, cy - h, cy + h, local.MaxZ, local.MaxZ));
    }

    private static IEnumerable<IFeature> Leaves(IFeature feature) => feature switch
    {
        BooleanFeature b => b.BaseFeatures.Concat(b.ToolFeatures).SelectMany(Leaves),
        TransformedFeature t => Leaves(t.Source),
        FilletFeature f => Leaves(f.BaseFeature),
        PartInstance => Enumerable.Empty<IFeature>(),
        _ => new[] { feature },
    };

    [Theory]
    [InlineData("placa_suporte_carrinho_y_a", 3.0)]
    [InlineData("placa_suporte_carrinho_y_a", -2.0)]
    [InlineData("extrusao_2020_trilho_z_a", 4.0)]
    [InlineData("extrusao_2020_trilho_z_a", -3.0)]
    public async Task ExtrudarNoTopoLocal_VoltaAoBuildComAsMesmasMedidasLocais(string instanceName, double height)
    {
        var project = SceneProject.Load(AiCoreFolder());
        var documents = project.ReadDocuments();
        string entry = SceneProject.FindEntryFile(documents);

        var clock = Stopwatch.StartNew();
        var before = await new RoslynSceneEvaluator().EvaluateAsync(documents);
        var plainEvaluation = clock.Elapsed;
        before.Success.Should().BeTrue(before.ErrorMessage);

        var instance = before.Features.OfType<PartInstance>().Single(i => i.Name == instanceName);
        var context = PartEditContext.For(instance, before.Features)!;
        var local = Bounds(new OpenCascadeGeometryKernel().BuildFeatureMesh(instance.Definition));
        var (face, square) = TopSquareInWorld(local, instance.Placement, side: 4);
        string code = FeatureCodeGenerator.GenerateExtrusionCode(face, height, null, ExtrusionTarget.Standalone);
        var mode = height < 0 ? PartMergeMode.SubtractAsTool : PartMergeMode.UnionAfterCuts;

        clock.Restart();
        var insertion = await new PartSolidInserter(new RoslynSceneEvaluator(), new RoslynPartMerger())
            .InsertAsync(documents, entry, code, context, mode);
        var insertTime = clock.Elapsed;

        _output.WriteLine($"{instanceName} (altura {height}): avaliação simples {plainEvaluation.TotalMilliseconds:0} ms, " +
                          $"inserção {insertTime.TotalMilliseconds:0} ms; {context.InstanceCount} instância(s) do tipo {context.PartTypeName}");
        foreach (var warning in insertion.Warnings) _output.WriteLine("aviso: " + warning);

        insertion.Refusal.Should().BeNull();
        insertion.GeometryVerified.Should().BeTrue("caixa é comparada pelos vértices");
        insertion.Changes.Should().ContainSingle().Which.FilePath.Should().NotBe(entry, "a cena não muda");

        var applied = documents.Select(d => insertion.Changes.FirstOrDefault(c => c.FilePath == d.FilePath) ?? d).ToList();
        var after = await new RoslynSceneEvaluator().EvaluateAsync(applied);
        after.Success.Should().BeTrue(after.ErrorMessage);
        var definition = after.Features.OfType<PartInstance>().Single(i => i.Name == instanceName).Definition;
        var solid = Leaves(definition).Single(f => f.Name == insertion.InsertedName);

        var vertices = solid.GetVertices();
        double zFrom = height > 0 ? square.MaxZ : square.MaxZ + height, zTo = height > 0 ? square.MaxZ + height : square.MaxZ;
        vertices.Min(v => v.X).Should().BeApproximately(square.MinX, 1e-3);
        vertices.Max(v => v.X).Should().BeApproximately(square.MaxX, 1e-3);
        vertices.Min(v => v.Y).Should().BeApproximately(square.MinY, 1e-3);
        vertices.Max(v => v.Y).Should().BeApproximately(square.MaxY, 1e-3);
        vertices.Min(v => v.Z).Should().BeApproximately(zFrom, 1e-3);
        vertices.Max(v => v.Z).Should().BeApproximately(zTo, 1e-3);
    }
}
