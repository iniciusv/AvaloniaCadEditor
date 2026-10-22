using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AvaloniaCadEditor.Application.Interfaces;
using AvaloniaCadEditor.Application.Services;
using AvaloniaCadEditor.Domain.Entities;
using AvaloniaCadEditor.Domain.Features;
using AvaloniaCadEditor.Domain.Settings;
using AvaloniaCadEditor.Domain.ValueObjects;
using AvaloniaCadEditor.Infrastructure.OpenCascade;
using FluentAssertions;
using Moq;
using Xunit;

namespace AvaloniaCadEditor.Tests.BehaviorTests;

/// <summary>
/// A geometria da cena é construída fora da thread da UI e pode ser cancelada: um chanfro por malha num
/// corpo furado (uma booleana por segmento da boca de cada furo) travava a janela por minutos.
/// </summary>
public class BackgroundMeshBuildTests
{
    private static SceneMeshManager MeshManager(IGeometryKernel kernel)
    {
        var settings = new Mock<ITessellationSettingsProvider>();
        settings.Setup(s => s.GetSettings()).Returns(new TessellationSettings());
        return new SceneMeshManager(kernel, settings.Object);
    }

    private static FilletFeature CaixaChanfrada(string nome = "chanfrado") =>
        new(nome, new BoxFeature(Point3D.Origin, 10, 10, 10), 1.0) { Mode = FilletMode.Bevel, ApplyToAllEdges = true };

    /// <summary>Chanfro de todas as arestas DEPOIS de 12 furos: centenas de cortes, um por aresta da malha.</summary>
    private static FilletFeature PlacaFuradaChanfrada()
    {
        var placa = new BoxFeature(Point3D.Origin, 80, 60, 10) { Name = "placa" };
        var furos = Enumerable.Range(0, 12)
            .Select(i => (IFeature)new CylinderFeature(new Point3D(10 + 20 * (i % 4), 10 + 20 * (i / 4), -1), new Vector3D(0, 0, 1), 3, 12) { Name = $"furo{i}" })
            .ToArray();
        var furada = BooleanFeature.CreateDifference("furada", placa, furos);
        return new FilletFeature("chanfro", furada, 1.0) { Mode = FilletMode.Bevel, ApplyToAllEdges = true };
    }

    [Fact]
    public void Kernel_Cancelado_LancaSemRegistrarProblemaENaoGuardaNada()
    {
        var kernel = new OpenCascadeGeometryKernel();
        var chanfrado = CaixaChanfrada();
        using var cancelado = new CancellationTokenSource();
        cancelado.Cancel();

        kernel.BuildCancellation = cancelado.Token;
        var construir = () => kernel.BuildFeatureMesh(chanfrado);
        construir.Should().Throw<OperationCanceledException>();
        kernel.BuildProblems.Should().BeEmpty("cancelar não é falha da geometria");

        kernel.BuildCancellation = default;
        kernel.BuildFeatureMesh(chanfrado).VertexCount.Should().BeGreaterThan(0, "nada pela metade ficou no cache");
    }

    [Fact]
    public void Kernel_ChanfroPorMalhaLongo_ParaLogoDepoisDoCancelamento()
    {
        var kernel = new OpenCascadeGeometryKernel();
        using var cancelamento = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        kernel.BuildCancellation = cancelamento.Token;

        var relogio = Stopwatch.StartNew();
        var construir = () => kernel.BuildFeatureMesh(PlacaFuradaChanfrada());

        construir.Should().Throw<OperationCanceledException>();
        relogio.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(10), "o chanfro confere o cancelamento a cada aresta");
    }

    [Fact]
    public async Task PrepareScene_ConstroiAntes_EAMalhaCompostaSoAchaCache()
    {
        var kernel = new OpenCascadeGeometryKernel();
        var meshes = MeshManager(kernel);
        var sketch = new Sketch();
        var antigo = CaixaChanfrada("antigo");
        sketch.AddFeature(antigo);
        meshes.BuildCompositeMesh(sketch, null);

        var novo = CaixaChanfrada("novo");
        await meshes.PrepareSceneAsync(new IFeature[] { novo }, CancellationToken.None);
        int construidas = kernel.MeshBuildCount;

        // Enquanto a troca não acontece, a cena antiga continua na tela sem ir ao kernel.
        meshes.GetFeatureMesh(sketch, antigo.Id, null).VertexCount.Should().BeGreaterThan(0);

        sketch.ClearFeatures();
        sketch.AddFeature(novo);
        meshes.BuildCompositeMesh(sketch, null).VertexCount.Should().BeGreaterThan(0);
        kernel.MeshBuildCount.Should().Be(construidas, "a malha nova já estava pronta");
    }

    [Fact]
    public async Task PrepareScene_Cancelado_NaoGuardaMalhaNemConstroiNaUI_EAProximaCenaConstroi()
    {
        var kernel = new OpenCascadeGeometryKernel();
        var meshes = MeshManager(kernel);
        var chanfrado = CaixaChanfrada();
        using var cancelado = new CancellationTokenSource();
        cancelado.Cancel();

        var preparar = () => meshes.PrepareSceneAsync(new IFeature[] { chanfrado }, cancelado.Token);
        await preparar.Should().ThrowAsync<OperationCanceledException>();

        // Continua pendente: construí-la aqui, na thread da UI, é justamente o que travava a janela.
        var sketch = new Sketch();
        sketch.AddFeature(chanfrado);
        meshes.PendingCount.Should().Be(1);
        meshes.BuildCompositeMesh(sketch, null).VertexCount.Should().Be(0);
        kernel.MeshBuildCount.Should().Be(0);

        await meshes.PrepareSceneAsync(new IFeature[] { chanfrado }, CancellationToken.None);
        meshes.PendingCount.Should().Be(0);
        meshes.BuildCompositeMesh(sketch, null).VertexCount.Should().BeGreaterThan(0);
        meshes.BuildProblems.Should().BeEmpty();
    }

    [Fact]
    public async Task PrepareScene_SolidoLento_ACenaApareceSemEleEEleEntraQuandoFicaPronto()
    {
        var kernel = new KernelComSolidoLento("lento");
        var meshes = MeshManager(kernel);
        var rapido = new BoxFeature(Point3D.Origin, 10, 10, 10) { Name = "rapido" };
        var lento = new BoxFeature(new Point3D(20, 0, 0), 10, 10, 10) { Name = "lento" };
        var sketch = new Sketch();
        sketch.AddFeature(rapido);
        sketch.AddFeature(lento);

        var preparar = meshes.PrepareSceneAsync(new IFeature[] { rapido, lento }, CancellationToken.None);
        kernel.ComecouOLento.Wait(TimeSpan.FromSeconds(30)).Should().BeTrue();

        // Com o kernel ocupado pelo lento, a UI não espera: monta a cena só com o rápido.
        var semOLento = meshes.BuildCompositeMesh(sketch, null);
        semOLento.VertexCount.Should().BeGreaterThan(0);
        meshes.GetFeatureMesh(sketch, lento.Id, null).VertexCount.Should().Be(0);
        meshes.GetMeshFor(lento).VertexCount.Should().Be(0);
        meshes.PendingCount.Should().Be(1);

        kernel.Liberar.Set();
        await preparar;

        meshes.PendingCount.Should().Be(0);
        meshes.BuildCompositeMesh(sketch, null).VertexCount.Should().BeGreaterThan(semOLento.VertexCount);
    }

    /// <summary>OCCT de verdade, mas a construção da feature <c>nomeLento</c> só termina quando o teste libera.</summary>
    private sealed class KernelComSolidoLento(string nomeLento) : IGeometryKernel
    {
        private readonly OpenCascadeGeometryKernel _occt = new();
        public ManualResetEventSlim ComecouOLento { get; } = new();
        public ManualResetEventSlim Liberar { get; } = new();
        public CancellationToken BuildCancellation { get; set; }

        public Domain.Mesh.TriangleMesh BuildFeatureMesh(IFeature feature, TessellationSettings? settings = null) => _occt.BuildFeatureMesh(feature, settings);

        public Domain.Mesh.TriangleMesh BuildSolidMesh(System.Collections.Generic.IEnumerable<IFeature> features, TessellationSettings? settings = null)
        {
            var lista = features.ToList();
            if (lista.Any(f => f.Name == nomeLento))
            {
                ComecouOLento.Set();
                Liberar.Wait(BuildCancellation);
            }
            return _occt.BuildSolidMesh(lista, settings);
        }

        public void InvalidateShape(Guid featureId) => _occt.InvalidateShape(featureId);
        public void InvalidateAll() => _occt.InvalidateAll();
    }
}
