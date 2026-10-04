using AvaloniaCadEditor.Domain.Features;
using AvaloniaCadEditor.Domain.Settings;
using AvaloniaCadEditor.Domain.ValueObjects;
using AvaloniaCadEditor.Infrastructure.OpenCascade;
using FluentAssertions;

namespace AvaloniaCadEditor.Tests.Features;

/// <summary>
/// Malhas reaproveitadas entre execuções da cena pela geometria (não pelo Id, que muda a cada execução).
/// Cada "execução" aqui cria features novas, como o script faz.
/// </summary>
public class GeometryMeshCacheTests
{
    private sealed record TrilhoTeste(double Comprimento, int Furos) : Part
    {
        protected override IFeature Build() => Trilho(Comprimento, Furos);
    }

    private static BooleanFeature Trilho(double comprimento, int furos)
    {
        var corpo = new BoxFeature(Point3D.Origin, 15, comprimento, 10);
        var tools = Enumerable.Range(0, furos)
            .Select(i => (IFeature)new CylinderFeature(new Point3D(7.5, 10 + i * 20, -1), new Vector3D(0, 0, 1), 2, 12))
            .ToArray();
        return BooleanFeature.CreateDifference("trilho", corpo, tools);
    }

    private static PartInstance<TrilhoTeste> Instancia(double comprimento, Placement at) =>
        new(new TrilhoTeste(comprimento, 4), Trilho(comprimento, 4), at);

    [Fact]
    public void MoverInstancia_EntreExecucoes_NaoRetesselaADefinicao()
    {
        var kernel = new OpenCascadeGeometryKernel();
        var settings = new TessellationSettings();

        var primeira = kernel.BuildFeatureMesh(Instancia(100, Placement.At(0, 0, 0)), settings);
        kernel.MeshBuildCount.Should().Be(1);

        kernel.BeginSceneRebuild();
        var movida = kernel.BuildFeatureMesh(Instancia(100, Placement.At(50, 0, 0)), settings);

        kernel.MeshBuildCount.Should().Be(1, "a geometria local não mudou, só o placement");
        movida.VertexCount.Should().Be(primeira.VertexCount);
        movida.Positions[0].Should().BeApproximately(primeira.Positions[0] + 50, 1e-3f);
    }

    [Fact]
    public void MudarParametroDaPeca_Retessela()
    {
        var kernel = new OpenCascadeGeometryKernel();
        var settings = new TessellationSettings();

        kernel.BuildFeatureMesh(Instancia(100, Placement.Identity), settings);
        kernel.BeginSceneRebuild();
        kernel.BuildFeatureMesh(Instancia(120, Placement.Identity), settings);

        kernel.MeshBuildCount.Should().Be(2);
    }

    [Fact]
    public void FeatureAlteradaNoLugar_Retessela()
    {
        var kernel = new OpenCascadeGeometryKernel();
        var settings = new TessellationSettings();
        var box = new BoxFeature(Point3D.Origin, 10, 10, 10);

        kernel.BuildFeatureMesh(box, settings);
        box.Height = 20;
        kernel.InvalidateShape(box.Id); // o que o Inspector faz ao editar
        var alterada = kernel.BuildFeatureMesh(box, settings);

        kernel.MeshBuildCount.Should().Be(2);
        alterada.Positions.Where((_, i) => i % 3 == 2).Max().Should().BeApproximately(20, 1e-3f);
    }

    [Fact]
    public void ConeIgualRecriado_EntreExecucoes_NaoRetessela()
    {
        var kernel = new OpenCascadeGeometryKernel();
        var settings = new TessellationSettings();
        ConeFeature Novo() => new(Point3D.Origin, new Vector3D(0, 0, 1), 10, 4, 20);

        kernel.BuildFeatureMesh(Novo(), settings);
        kernel.BeginSceneRebuild();
        kernel.BuildFeatureMesh(Novo(), settings);

        kernel.MeshBuildCount.Should().Be(1, "o fingerprint do cone cobre toda a sua geometria");
    }

    [Fact]
    public void MudarConfiguracaoDeTesselacao_Retessela()
    {
        var kernel = new OpenCascadeGeometryKernel();
        var cyl = new CylinderFeature(Point3D.Origin, new Vector3D(0, 0, 1), 5, 10);

        kernel.BuildFeatureMesh(cyl, new TessellationSettings { ProfileSegments = 32 });
        kernel.BuildFeatureMesh(cyl, new TessellationSettings { ProfileSegments = 64 });

        kernel.MeshBuildCount.Should().Be(2);
    }

    [Fact]
    public void GeometriaNaoUsadaNaExecucaoAnterior_SaiDoCache()
    {
        var kernel = new OpenCascadeGeometryKernel();
        var settings = new TessellationSettings();

        kernel.BuildFeatureMesh(Instancia(100, Placement.Identity), settings);   // execução 1: usa 100
        kernel.BeginSceneRebuild();
        kernel.BuildFeatureMesh(Instancia(120, Placement.Identity), settings);   // execução 2: só 120
        kernel.BeginSceneRebuild();                                               // 100 não foi usado na 2 → sai
        kernel.BuildFeatureMesh(Instancia(100, Placement.Identity), settings);

        kernel.MeshBuildCount.Should().Be(3);
    }

    [Fact]
    public void InvalidateAll_DescartaTudo()
    {
        var kernel = new OpenCascadeGeometryKernel();
        var settings = new TessellationSettings();

        kernel.BuildFeatureMesh(Instancia(100, Placement.Identity), settings);
        kernel.InvalidateAll();
        kernel.BuildFeatureMesh(Instancia(100, Placement.Identity), settings);

        kernel.MeshBuildCount.Should().Be(2);
    }

    private sealed record BlocoChanfrado(double Largura) : Part
    {
        protected override IFeature Build() => Chanfrado(Largura);
    }

    private static FilletFeature Chanfrado(double largura) =>
        new("chanfro", new BoxFeature(Point3D.Origin, largura, 10, 10), 1) { Mode = FilletMode.Bevel, ApplyToAllEdges = true };

    [Fact]
    public void MoverPecaComFillet_EntreExecucoes_NaoRetessela()
    {
        var kernel = new OpenCascadeGeometryKernel();
        var settings = new TessellationSettings();
        PartInstance<BlocoChanfrado> Instancia(double x) =>
            new(new BlocoChanfrado(20), Chanfrado(20), Placement.At(x, 0, 0));

        kernel.BuildFeatureMesh(Instancia(0), settings);
        kernel.BeginSceneRebuild();
        kernel.BuildFeatureMesh(Instancia(30), settings);

        kernel.MeshBuildCount.Should().Be(1, "fillets são cacheados pela geometria, como as booleanas");
    }

    /// <summary>Subclasse de um perfil conhecido: pode ter parâmetros a mais, então não tem chave.</summary>
    private sealed class PerfilDesconhecido : AvaloniaCadEditor.Domain.Profiles.PolygonProfile
    {
        public PerfilDesconhecido() : base(new[] { new Point3D(0, 0, 0), new Point3D(10, 0, 0), new Point3D(0, 10, 0) }) { }
    }

    private static ExtrudeFeature ExtrusaoDePerfilDesconhecido() => new(new PerfilDesconhecido(), 5);

    [Fact]
    public void FeatureSemChave_ContinuaFuncionandoSemCache()
    {
        var kernel = new OpenCascadeGeometryKernel();
        var settings = new TessellationSettings();

        kernel.BuildFeatureMesh(ExtrusaoDePerfilDesconhecido(), settings);
        kernel.BeginSceneRebuild();
        kernel.BuildFeatureMesh(ExtrusaoDePerfilDesconhecido(), settings);

        kernel.MeshBuildCount.Should().Be(2);
    }
}
