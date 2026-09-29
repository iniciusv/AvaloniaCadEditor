using System.Collections.Generic;
using System.Linq;
using AvaloniaCadEditor.Application.Commands;
using AvaloniaCadEditor.Domain.Entities;
using AvaloniaCadEditor.Domain.Features;
using AvaloniaCadEditor.Domain.ValueObjects;
using FluentAssertions;
using Xunit;

namespace AvaloniaCadEditor.Tests.BehaviorTests;

/// <summary>RotateTool: 3 cliques (pivô, referência, ângulo), eixo pelas setas, VCB e cancelamento.</summary>
public class RotateBehaviorTests
{
    private static (Sketch sketch, BoxFeature feature) CreateSketchComFeatureSelecionada()
    {
        var sketch = new Sketch();
        var feature = new BoxFeature(Point3D.Origin, 10, 10, 10) { IsSelected = true };
        sketch.AddFeature(feature);
        return (sketch, feature);
    }

    [Fact]
    public void RotateTool_TresCliques_Gira90GrausEmZ()
    {
        var (sketch, feature) = CreateSketchComFeatureSelecionada();
        var rotateTool = new RotateTool();

        Point3D? pivotRecebido = null;
        Vector3D? axisRecebido = null;
        double? degreesRecebido = null;
        List<IFeature>? featuresRecebidas = null;
        rotateTool.FeaturesRotated += (pivot, axis, degrees, features) =>
        {
            pivotRecebido = pivot;
            axisRecebido = axis;
            degreesRecebido = degrees;
            featuresRecebidas = features.ToList();
        };

        rotateTool.OnMouseDown(sketch, new Point3D(0, 0, 0));   // pivô
        rotateTool.OnMouseDown(sketch, new Point3D(10, 0, 0));  // referência (0°)
        rotateTool.OnMouseDown(sketch, new Point3D(0, 10, 0));  // 90° em Z (regra da mão direita)

        pivotRecebido.Should().Be(new Point3D(0, 0, 0));
        axisRecebido.Should().Be(Vector3D.UnitZ);
        degreesRecebido.Should().BeApproximately(90, 0.01);
        featuresRecebidas.Should().ContainSingle().Which.Should().Be(feature);
    }

    [Fact]
    public void RotateTool_SelecaoSoCapturaNoPrimeiroClique()
    {
        var sketch = new Sketch();
        var feature = new BoxFeature(Point3D.Origin, 10, 10, 10) { IsSelected = true };
        sketch.AddFeature(feature);
        var rotateTool = new RotateTool();

        rotateTool.OnMouseDown(sketch, new Point3D(0, 0, 0)); // pivô: captura a seleção atual

        rotateTool.DraggedFeatures.Should().ContainSingle().Which.Should().Be(feature);
    }

    [Fact]
    public void RotateTool_SetaDireitaTrocaOEixoParaX()
    {
        var (sketch, _) = CreateSketchComFeatureSelecionada();
        var rotateTool = new RotateTool();

        Vector3D? axisRecebido = null;
        rotateTool.FeaturesRotated += (pivot, axis, degrees, features) => axisRecebido = axis;

        rotateTool.OnKeyDown(sketch, CadKey.ArrowRight); // eixo X

        rotateTool.OnMouseDown(sketch, new Point3D(0, 0, 0));   // pivô
        rotateTool.OnMouseDown(sketch, new Point3D(0, 10, 0));  // referência, perpendicular ao eixo X
        rotateTool.OnMouseDown(sketch, new Point3D(0, 0, 10));  // 90° em torno de X

        axisRecebido.Should().Be(Vector3D.UnitX);
    }

    [Fact]
    public void RotateTool_VcbConfirmaAnguloExatoIndependenteDoMouse()
    {
        var (sketch, _) = CreateSketchComFeatureSelecionada();
        var rotateTool = new RotateTool();

        double? degreesRecebido = null;
        rotateTool.FeaturesRotated += (pivot, axis, degrees, features) => degreesRecebido = degrees;

        rotateTool.OnMouseDown(sketch, new Point3D(0, 0, 0));   // pivô
        rotateTool.OnMouseDown(sketch, new Point3D(10, 0, 0));  // referência
        rotateTool.OnMouseMove(sketch, new Point3D(10, 1, 0));  // mouse quase na referência (ângulo pequeno)
        rotateTool.OnValueEntered(sketch, 45.0);

        degreesRecebido.Should().Be(45.0);
    }

    [Fact]
    public void RotateTool_Cancel_NaoDisparaEventoEReiniciaOFluxo()
    {
        var (sketch, feature) = CreateSketchComFeatureSelecionada();
        var rotateTool = new RotateTool();

        bool disparou = false;
        rotateTool.FeaturesRotated += (pivot, axis, degrees, features) => disparou = true;

        rotateTool.OnMouseDown(sketch, new Point3D(0, 0, 0));
        rotateTool.OnMouseDown(sketch, new Point3D(10, 0, 0));
        rotateTool.Cancel();

        disparou.Should().BeFalse();

        // O fluxo reinicia normalmente depois do cancelamento.
        rotateTool.OnMouseDown(sketch, new Point3D(0, 0, 0));
        rotateTool.DraggedFeatures.Should().ContainSingle().Which.Should().Be(feature);
    }

    [Fact]
    public void RotateTool_ValorZerado_NaoDisparaCommitComAnguloIrrelevante()
    {
        // OnMouseMove sozinho (sem clique de confirmação) não deve consolidar nada.
        var (sketch, _) = CreateSketchComFeatureSelecionada();
        var rotateTool = new RotateTool();

        bool disparou = false;
        rotateTool.FeaturesRotated += (pivot, axis, degrees, features) => disparou = true;

        rotateTool.OnMouseDown(sketch, new Point3D(0, 0, 0));
        rotateTool.OnMouseDown(sketch, new Point3D(10, 0, 0));
        rotateTool.OnMouseMove(sketch, new Point3D(0, 10, 0));

        disparou.Should().BeFalse();
    }

    // --- Transferidor seguindo o cursor (antes do 1º clique) ---

    [Fact]
    public void RotateTool_SemMoverOMouse_NaoTemGuias()
    {
        var rotateTool = new RotateTool();

        rotateTool.GetGuideSegments().Should().BeEmpty();
    }

    [Fact]
    public void RotateTool_MouseMoveSemClique_TransferidorSeguemOCursorComRaioFixo()
    {
        var sketch = new Sketch();
        var rotateTool = new RotateTool { WorldUnitsPerPixel = 2.0 };
        var cursor = new Point3D(50, 30, 0);

        rotateTool.OnMouseMove(sketch, cursor);
        var segments = rotateTool.GetGuideSegments().ToList();

        segments.Should().NotBeEmpty();
        double raioEsperado = 60.0 * 2.0; // GuideRadiusPixels * WorldUnitsPerPixel
        foreach (var p in segments.Take(96).Select(s => s.From))
        {
            p.DistanceTo(cursor).Should().BeApproximately(raioEsperado, 0.01);
        }
    }

    [Fact]
    public void RotateTool_SetaAntesDoPrimeiroClique_TrocaOEixoDoTransferidor()
    {
        var sketch = new Sketch();
        var rotateTool = new RotateTool();
        var cursor = new Point3D(50, 30, 10);

        rotateTool.OnKeyDown(sketch, CadKey.ArrowRight); // eixo X
        rotateTool.OnMouseMove(sketch, cursor);
        var segments = rotateTool.GetGuideSegments().ToList();

        // Círculo perpendicular ao eixo X: todo ponto mantém o X do cursor.
        segments.Take(96).Select(s => s.From)
            .Should().OnlyContain(p => System.Math.Abs(p.X - cursor.X) < 1e-6);

        // Cor do eixo X (vermelho).
        segments[0].R.Should().Be(1f);
        segments[0].G.Should().Be(0.3f);
        segments[0].B.Should().Be(0.3f);
    }

    [Fact]
    public void RotateTool_DepoisDoPrimeiroClique_TransferidorFicaFixoNoPivoMesmoComOMouseSeMovendo()
    {
        var (sketch, _) = CreateSketchComFeatureSelecionada();
        var rotateTool = new RotateTool();
        var pivot = new Point3D(5, 5, 0);

        rotateTool.OnMouseMove(sketch, new Point3D(100, 100, 0));
        rotateTool.OnMouseDown(sketch, pivot); // fixa o pivô
        rotateTool.OnMouseMove(sketch, new Point3D(200, -50, 0)); // mouse segue se movendo

        var segments = rotateTool.GetGuideSegments().ToList();
        foreach (var p in segments.Take(96).Select(s => s.From))
        {
            p.DistanceTo(pivot).Should().BeApproximately(60.0, 0.01);
        }
    }
    // --- Plano de trabalho diferente do XY ---
    // Plano com normal X passando por (10,0,0): X local -> Y do mundo, Y local -> Z do mundo.
    private static WorkPlane PlanoLateral() => new WorkPlane(Vector3D.UnitX, new Point3D(10, 0, 0));

    [Fact]
    public void RotateTool_EmPlanoInclinado_GiraEmTornoDaNormalComPivoNoMundo()
    {
        var (sketch, _) = CreateSketchComFeatureSelecionada();
        var rotateTool = new RotateTool { Plane = PlanoLateral() };

        Point3D? pivotRecebido = null;
        Vector3D? axisRecebido = null;
        double? degreesRecebido = null;
        rotateTool.FeaturesRotated += (pivot, axis, degrees, features) =>
        {
            pivotRecebido = pivot;
            axisRecebido = axis;
            degreesRecebido = degrees;
        };

        // Pontos em coordenadas locais do plano, como o CadViewModel entrega.
        rotateTool.OnMouseDown(sketch, new Point3D(0, 0, 0));
        rotateTool.OnMouseDown(sketch, new Point3D(1, 0, 0));
        rotateTool.OnMouseDown(sketch, new Point3D(0, 1, 0));

        pivotRecebido.Should().Be(new Point3D(10, 0, 0));
        axisRecebido.Should().Be(Vector3D.UnitX);
        degreesRecebido.Should().BeApproximately(90, 0.01);
    }

    [Fact]
    public void RotateTool_EmPlanoInclinado_SetaTravaNoEixoGlobal()
    {
        var (sketch, _) = CreateSketchComFeatureSelecionada();
        var rotateTool = new RotateTool { Plane = PlanoLateral() };

        Vector3D? axisRecebido = null;
        rotateTool.FeaturesRotated += (pivot, axis, degrees, features) => axisRecebido = axis;

        rotateTool.OnKeyDown(sketch, CadKey.ArrowUp); // Z global
        rotateTool.OnMouseDown(sketch, new Point3D(0, 0, 0));
        rotateTool.OnMouseDown(sketch, new Point3D(1, 0, 0));
        rotateTool.OnValueEntered(sketch, 30);

        axisRecebido.Should().Be(Vector3D.UnitZ);
    }

    [Fact]
    public void RotateTool_EmPlanoInclinado_ReferenciaVoltaEmCoordenadasLocais()
    {
        var (sketch, _) = CreateSketchComFeatureSelecionada();
        var rotateTool = new RotateTool { Plane = PlanoLateral() };

        rotateTool.OnMouseDown(sketch, new Point3D(3, 4, 0));

        rotateTool.GetReferencePoint().Should().Be(new Point3D(3, 4, 0));
    }

    [Fact]
    public void RotateTool_EmPlanoInclinado_TransferidorFicaDeitadoNoPlano()
    {
        var sketch = new Sketch();
        var rotateTool = new RotateTool { Plane = PlanoLateral() };

        rotateTool.OnMouseMove(sketch, new Point3D(2, 3, 0));

        rotateTool.GetGuideSegments().Take(96).Select(s => s.From)
            .Should().OnlyContain(p => System.Math.Abs(p.X - 10) < 1e-6);
    }

    [Fact]
    public void RotateTool_ComControl_DisparaCopiaEmVezDeGirarOOriginal()
    {
        var (sketch, feature) = CreateSketchComFeatureSelecionada();
        var rotateTool = new RotateTool();

        bool girou = false;
        double? anguloCopia = null;
        rotateTool.FeaturesRotated += (pivot, axis, degrees, features) => girou = true;
        rotateTool.FeaturesCopied += (pivot, axis, degrees, features) => anguloCopia = degrees;

        rotateTool.OnKeyDown(sketch, CadKey.Control);
        rotateTool.IsCopyPreview.Should().BeTrue();

        rotateTool.OnMouseDown(sketch, new Point3D(0, 0, 0));
        rotateTool.OnMouseDown(sketch, new Point3D(10, 0, 0));
        rotateTool.OnMouseDown(sketch, new Point3D(0, 10, 0));

        girou.Should().BeFalse();
        anguloCopia.Should().BeApproximately(90, 0.01);
    }
}
