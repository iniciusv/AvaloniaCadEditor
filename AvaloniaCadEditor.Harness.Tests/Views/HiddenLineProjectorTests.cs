using System.Numerics;
using AvaloniaCadEditor.Domain.Entities;
using AvaloniaCadEditor.Domain.Mesh;
using AvaloniaCadEditor.Drafting;
using AvaloniaCadEditor.Harness.Views;
using FluentAssertions;
using static AvaloniaCadEditor.Harness.Tests.Views.TestMeshes;

namespace AvaloniaCadEditor.Harness.Tests.Views;

/// <summary>
/// Vista Frente: U = +X, V = +Z, olhando de -Y (quem está em y menor está mais perto).
/// </summary>
public class HiddenLineProjectorTests
{
    private static readonly ProjectionBasis Front = ProjectionBasis.For(StandardView.Front);

    private static int Segments(ViewLine line) => line.Shape.Points.Count - 1 + (line.Shape.Closed ? 1 : 0);

    private static IEnumerable<ViewLine> Visible(HiddenLineResult r) => r.Lines.Where(l => l.Visible);
    private static IEnumerable<ViewLine> Hidden(HiddenLineResult r) => r.Lines.Where(l => !l.Visible);

    [Theory]
    [InlineData(StandardView.Front)]
    [InlineData(StandardView.Back)]
    [InlineData(StandardView.Right)]
    [InlineData(StandardView.Left)]
    [InlineData(StandardView.Top)]
    [InlineData(StandardView.Bottom)]
    public void BaseDasVistasOrtogonais_BateComViewBasis(StandardView view)
    {
        var basis = ProjectionBasis.For(view);
        var expected = ViewBasis.For(view)!;

        basis.U.Should().Be(expected.U);
        basis.V.Should().Be(expected.V);
        basis.W.Should().Be(expected.W);
    }

    [Fact]
    public void BaseDaIso_BateComAMatrizDeVistaDaCamera()
    {
        var camera = new CameraState { Target = Vector3.Zero, Distance = 1f };
        camera.SetStandardView(StandardView.Isometric);
        var m = camera.GetViewMatrix();

        var basis = ProjectionBasis.For(StandardView.Isometric);

        basis.U.X.Should().BeApproximately(m.M11, 1e-5); basis.U.Y.Should().BeApproximately(m.M21, 1e-5); basis.U.Z.Should().BeApproximately(m.M31, 1e-5);
        basis.V.X.Should().BeApproximately(m.M12, 1e-5); basis.V.Y.Should().BeApproximately(m.M22, 1e-5); basis.V.Z.Should().BeApproximately(m.M32, 1e-5);
        basis.W.X.Should().BeApproximately(m.M13, 1e-5); basis.W.Y.Should().BeApproximately(m.M23, 1e-5); basis.W.Z.Should().BeApproximately(m.M33, 1e-5);
    }

    [Fact]
    public void CaixaNaFrente_UmRetanguloVisivelESemOcultas()
    {
        var result = HiddenLineProjector.Project(Box(0, 0, 0, 10, 6, 8), Front);

        result.Error.Should().BeNull();
        Hidden(result).Should().BeEmpty("as arestas de trás caem em cima das da frente");
        var outline = Visible(result).Should().ContainSingle().Subject;
        outline.Shape.Closed.Should().BeTrue();
        outline.Shape.Points.Should().HaveCount(4);
        outline.Kind.Should().Be(ProjectedEdgeKind.Crease);
        result.Bounds.MinU.Should().BeApproximately(0, 1e-6);
        result.Bounds.MaxU.Should().BeApproximately(10, 1e-6);
        result.Bounds.MaxV.Should().BeApproximately(8, 1e-6);
    }

    [Fact]
    public void CaixaInteiramenteAtrasDeOutra_ViraRetanguloOculto()
    {
        var mesh = Combine(Box(0, 0, 0, 10, 2, 10), Box(3, 5, 3, 7, 7, 7));

        var result = HiddenLineProjector.Project(mesh, Front);

        var hidden = Hidden(result).Should().ContainSingle("as faces de frente e de trás da caixa escondida coincidem").Subject;
        hidden.Shape.Closed.Should().BeTrue();
        hidden.Shape.Points.Should().HaveCount(4);
        hidden.Shape.Points.Should().OnlyContain(p => (Math.Abs(p.X - 3) < 1e-6 || Math.Abs(p.X - 7) < 1e-6) && (Math.Abs(p.Y - 3) < 1e-6 || Math.Abs(p.Y - 7) < 1e-6));
        Visible(result).Should().ContainSingle().Which.Shape.Points.Should().HaveCount(4);
    }

    [Fact]
    public void OclusaoParcial_PartaAArestaOndeAFrenteTermina()
    {
        // A na frente (x 0..10); B atrás e deslocada (x 5..15): de B, só x > 10 aparece.
        var mesh = Combine(Box(0, 0, 0, 10, 2, 10), Box(5, 5, 2, 15, 7, 8));

        var result = HiddenLineProjector.Project(mesh, Front);

        var hidden = Hidden(result).Should().ContainSingle().Subject;
        hidden.Shape.Closed.Should().BeFalse();
        hidden.Shape.Points.Should().HaveCount(4, "topo e base escondidos + a lateral esquerda de B");
        hidden.Shape.Points.Count(p => Math.Abs(p.X - 5) < 1e-6).Should().Be(2);
        hidden.Shape.Points.Where(p => Math.Abs(p.X - 5) > 1e-3)
            .Should().OnlyContain(p => Math.Abs(p.X - 10) < 0.05, "a aresta de B fica escondida até a borda direita de A");

        Visible(result).Should().Contain(l => l.Shape.Points.Any(p => Math.Abs(p.X - 15) < 1e-6 && Math.Abs(p.Y - 8) < 1e-6));
    }

    [Fact]
    public void CilindroDeitado_DuasSilhuetasEAsTampasDeLado()
    {
        // Eixo X de 0 a 10, raio 3, no eixo y = z = 0.
        var result = HiddenLineProjector.Project(ZToX(Cylinder(0, 0, 3, 0, 10)), Front);

        Hidden(result).Should().BeEmpty();
        var silhouettes = Visible(result).Where(l => l.Kind == ProjectedEdgeKind.Silhouette).ToList();
        silhouettes.Should().HaveCount(2);
        var heights = silhouettes.Select(l => l.Shape.Points[0].Y).OrderBy(y => y).ToList();
        heights[0].Should().BeApproximately(-3, 1e-4);
        heights[1].Should().BeApproximately(3, 1e-4);
        silhouettes.Should().OnlyContain(l => Segments(l) == 1);

        var caps = Visible(result).Where(l => l.Kind == ProjectedEdgeKind.Crease).ToList();
        caps.Should().HaveCount(2, "cada tampa é vista de lado: um segmento vertical");
        caps.Should().OnlyContain(l => Segments(l) == 1);
    }

    [Fact]
    public void CaixaNaIso_NoveArestasVisiveisETresOcultas()
    {
        var result = HiddenLineProjector.Project(Box(0, 0, 0, 10, 10, 10), ProjectionBasis.For(StandardView.Isometric));

        Visible(result).Sum(Segments).Should().Be(9, "o hexágono do contorno + as 3 arestas do canto da frente");
        Hidden(result).Sum(Segments).Should().Be(3, "as 3 arestas do canto de trás");
    }

    [Fact]
    public void MalhaAcimaDoOrcamento_ERecusada()
    {
        var result = HiddenLineProjector.Project(Box(0, 0, 0, 1, 1, 1), Front, new HiddenLineOptions { MaxTriangles = 4 });

        result.Error.Should().Contain("12 triângulos");
        result.Lines.Should().BeEmpty();
    }

    [Fact]
    public void MalhaVazia_NaoTemLinhas()
    {
        var result = HiddenLineProjector.Project(TriangleMesh.Empty, Front);

        result.Error.Should().BeNull();
        result.Lines.Should().BeEmpty();
    }
}
