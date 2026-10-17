using AvaloniaCadEditor.Analysis;
using AvaloniaCadEditor.Domain.Entities;
using AvaloniaCadEditor.Domain.Mesh;
using AvaloniaCadEditor.Domain.ValueObjects;
using AvaloniaCadEditor.Drafting;
using AvaloniaCadEditor.Harness.Views;
using FluentAssertions;
using static AvaloniaCadEditor.Harness.Tests.Views.TestMeshes;

namespace AvaloniaCadEditor.Harness.Tests.Views;

public class WireframeReportTests
{
    private static readonly ProjectionBasis Front = ProjectionBasis.For(StandardView.Front);
    private static readonly WireframeHeader Header = new("bloco", "Bloco", FrameIsWorld: false, CodeOrigin: new Point3D(5, 0, 0));

    /// <summary>Cilindro r = 2,5 com eixo em -Y: de frente, a tampa é um círculo de centro (4, 4).</summary>
    private static readonly TriangleMesh CylinderFacingFront = ZToMinusY(Cylinder(4, 4, 2.5, 0, 6));

    private static string Report(TriangleMesh mesh, IReadOnlyList<ViewCircle>? circles = null, bool includeHidden = true, int maxChars = 6000) =>
        WireframeReport.Format(HiddenLineProjector.Project(mesh, Front), Front, Header,
            circles ?? Array.Empty<ViewCircle>(), includeHidden, maxChars);

    [Fact]
    public void Cabecalho_DizEixosContornoEOrigemDoCodigo()
    {
        string text = Report(Box(0, 0, 0, 10, 6, 8));

        text.Should().StartWith("# Vista Front — bloco (Bloco) · ortográfica, arame");
        text.Should().Contain("U → direita = +X, V → cima = +Z, olhando de -Y");
        text.Should().Contain("# contorno: U 0..10, V 0..8 (10 × 8)");
        text.Should().Contain("origem do código (0, 0, 0) na folha: U 5, V 0");
        text.Should().Contain("## Visíveis — 1 linhas (0 círculos)");
        text.Should().Contain("- L1 quina fechada: ");
        text.Should().Contain("## Ocultas (tracejadas) — 0 linhas");
    }

    [Fact]
    public void CirculoCasadoComFuroExato_UsaODiametroEONomeDoFuro()
    {
        var exact = new ViewCircle("furo_a", PrimitiveRole.Void, new P2(4, 4), 2.5);

        Report(CylinderFacingFront, new[] { exact }).Should().Contain("C1 Ø5 centro (4, 4) [furo_a]");
    }

    [Fact]
    public void CirculoSemFuroConhecido_SaiAjustadoNaMalha()
    {
        string text = Report(CylinderFacingFront);

        text.Should().Contain("(ajustado na malha)");
        text.Should().MatchRegex(@"C1 Ø[\d.]+ centro \(4, 4\)");
    }

    [Fact]
    public void SemOcultas_NaoTemASecao()
    {
        string text = Report(Combine(Box(0, 0, 0, 10, 2, 10), Box(3, 5, 3, 7, 7, 7)), includeHidden: false);

        text.Should().Contain("sem as ocultas");
        text.Should().NotContain("## Ocultas");
    }

    [Fact]
    public void OrcamentoPequeno_CortaAsOcultasPrimeiroEAvisa()
    {
        // Uma caixa na frente e quatro escondidas atrás dela: um contorno visível e quatro ocultos.
        var mesh = Combine(Box(0, 0, 0, 20, 2, 20),
            Box(1, 5, 1, 4, 7, 4), Box(6, 5, 6, 9, 7, 9), Box(11, 5, 11, 14, 7, 14), Box(15, 5, 15, 19, 7, 19));
        string full = Report(mesh);
        int maxChars = full.Length - 60;

        string cut = Report(mesh, maxChars: maxChars);

        cut.Length.Should().BeLessThanOrEqualTo(maxChars);
        cut.Should().Contain("## Omitidas pelo orçamento");
        cut.Should().Contain("(0 visíveis, ");
        cut.Should().Contain("## Visíveis — 1 linhas");
    }

    [Fact]
    public void OrcamentoMenorQueOCabecalho_FicaSoComOCabecalhoEOAviso()
    {
        string cut = Report(Box(0, 0, 0, 10, 6, 8), maxChars: 50);

        cut.Should().Contain("## Visíveis — 0 linhas");
        cut.Should().Contain("## Omitidas pelo orçamento");
    }

    [Fact]
    public void SaidaDeterministica()
    {
        var mesh = Combine(Box(0, 0, 0, 10, 2, 10), Box(5, 5, 2, 15, 7, 8));

        Report(mesh).Should().Be(Report(mesh));
    }
}
