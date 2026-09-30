using Avalonia;
using AvaloniaCadEditor.UI.Controls.Input;
using FluentAssertions;
using Xunit;

namespace AvaloniaCadEditor.Tests.BehaviorTests;

public class BoxSelectionGeometryTests
{
    private static readonly Rect Box = new(10, 10, 100, 100); // x 10..110, y 10..110

    [Fact]
    public void DirecaoDoArraste_EsquerdaParaDireitaEhJanela_DireitaParaEsquerdaEhCrossing()
    {
        BoxSelectionGeometry.IsWindowSelection(new Point(10, 50), new Point(80, 5)).Should().BeTrue();
        BoxSelectionGeometry.IsWindowSelection(new Point(80, 50), new Point(10, 90)).Should().BeFalse();
    }

    [Fact]
    public void Linha_ParcialmenteDentro_SoEntraNoCrossing()
    {
        var pts = new Point?[] { new Point(50, 50), new Point(200, 50) };

        BoxSelectionGeometry.PolylineMatches(Box, pts, closed: false, window: true).Should().BeFalse();
        BoxSelectionGeometry.PolylineMatches(Box, pts, closed: false, window: false).Should().BeTrue();
    }

    [Fact]
    public void Linha_AtravessandoORetanguloSemPontaDentro_EntraNoCrossing()
    {
        var pts = new Point?[] { new Point(0, 50), new Point(200, 50) };

        BoxSelectionGeometry.PolylineMatches(Box, pts, closed: false, window: false).Should().BeTrue();
        BoxSelectionGeometry.PolylineMatches(Box, pts, closed: false, window: true).Should().BeFalse();
    }

    [Fact]
    public void Linha_InteiramenteDentro_EntraNosDoisModos()
    {
        var pts = new Point?[] { new Point(20, 20), new Point(90, 90) };

        BoxSelectionGeometry.PolylineMatches(Box, pts, closed: false, window: true).Should().BeTrue();
        BoxSelectionGeometry.PolylineMatches(Box, pts, closed: false, window: false).Should().BeTrue();
    }

    [Fact]
    public void Linha_Fora_NaoEntra()
    {
        var pts = new Point?[] { new Point(200, 200), new Point(300, 150) };

        BoxSelectionGeometry.PolylineMatches(Box, pts, closed: false, window: false).Should().BeFalse();
    }

    [Fact]
    public void PontoAtrasDaCamera_ImpedeAJanela()
    {
        var pts = new Point?[] { new Point(20, 20), null };

        BoxSelectionGeometry.PolylineMatches(Box, pts, closed: false, window: true).Should().BeFalse();
    }

    [Fact]
    public void Malha_ComTrianguloEnvolvendoORetangulo_EntraNoCrossing()
    {
        // Triângulo gigante que contém o retângulo inteiro, sem arestas cruzando nem vértices dentro.
        var verts = new Point?[] { new Point(-1000, -1000), new Point(1000, -1000), new Point(0, 1000) };
        var indices = new uint[] { 0, 1, 2 };

        BoxSelectionGeometry.MeshMatches(Box, verts, indices, window: false).Should().BeTrue();
        BoxSelectionGeometry.MeshMatches(Box, verts, indices, window: true).Should().BeFalse();
    }

    [Fact]
    public void Malha_InteiramenteDentro_EntraNaJanela()
    {
        var verts = new Point?[] { new Point(20, 20), new Point(80, 20), new Point(50, 80), new Point(500, 500) };
        var indices = new uint[] { 0, 1, 2 }; // o vértice 3 não é usado e não conta

        BoxSelectionGeometry.MeshMatches(Box, verts, indices, window: true).Should().BeTrue();
    }
}
