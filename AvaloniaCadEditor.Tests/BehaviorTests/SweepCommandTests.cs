using System.Linq;
using System.Threading.Tasks;
using AvaloniaCadEditor.Application.Commands;
using AvaloniaCadEditor.Domain.Entities;
using AvaloniaCadEditor.Domain.ValueObjects;
using AvaloniaCadEditor.Infrastructure.OpenCascade;
using AvaloniaCadEditor.Infrastructure.Scripting;
using AvaloniaCadEditor.Tests.Extensions;
using FluentAssertions;
using Xunit;

namespace AvaloniaCadEditor.Tests.BehaviorTests;

/// <summary>
/// A ferramenta Sweep não constrói geometria: emite o CadScript equivalente, como as demais. Os
/// testes conferem o que ela emite (e o que NÃO emite), já que é isso que vira sólido.
/// </summary>
public class SweepCommandTests
{
    private static Sketch NovoSketch() => new();

    private static string? CapturarCodigo(SweepCommand cmd)
    {
        string? gerado = null;
        cmd.CodeGenerated += (_, code) => gerado = code;
        return gerado;
    }

    [Fact]
    public void CliquesMaisEnter_GeramOCaminhoEOTubo()
    {
        var sketch = NovoSketch();
        var cmd = new SweepCommand();
        string? codigo = null;
        cmd.CodeGenerated += (_, c) => codigo = c;

        cmd.OnValueEntered(sketch, 4);                              // raio da seção
        cmd.OnMouseDown(sketch, new Point3D(0, 0, 0));
        cmd.OnMouseDown(sketch, new Point3D(100, 0, 0));
        cmd.OnMouseDown(sketch, new Point3D(100, 80, 0));
        cmd.OnKeyDown(sketch, CadKey.Enter);

        codigo.Should().NotBeNull();
        codigo.Should().Contain("SweepPath.FromPolyline(")
              .And.Contain("new Point3D(0, 0, 0)")
              .And.Contain("new Point3D(100, 0, 0)")
              .And.Contain("new Point3D(100, 80, 0)")
              .And.Contain("SweepSection.Circle(4)")
              .And.Contain("AddFeature(");

        // O raio de canto padrão é o dobro da seção: um canto precisa de raio MAIOR que a seção,
        // senão o sólido dobra para dentro de si mesmo e nada aparece.
        codigo.Should().Contain("cornerRadius: 8");
    }

    [Fact]
    public void NomeDoSolidoBateComAVariavel()
    {
        // A regra de sólidos rastreáveis: o Name literal tem que ser igual ao nome da variável.
        var sketch = NovoSketch();
        var cmd = new SweepCommand();
        string? codigo = null;
        cmd.CodeGenerated += (_, c) => codigo = c;

        cmd.OnMouseDown(sketch, new Point3D(0, 0, 0));
        cmd.OnMouseDown(sketch, new Point3D(50, 0, 0));
        cmd.OnKeyDown(sketch, CadKey.Enter);

        string varName = codigo!.Split("var ")[2].Split(' ')[0];
        codigo.Should().Contain($"var {varName} = new SweepFeature(")
              .And.Contain($"{{ Name = \"{varName}\" }}")
              .And.Contain($"AddFeature({varName});");
    }

    [Fact]
    public void ValorDigitadoComCaminhoComecado_EhORaioDoCantoEConclui()
    {
        var sketch = NovoSketch();
        var cmd = new SweepCommand();
        string? codigo = null;
        cmd.CodeGenerated += (_, c) => codigo = c;

        cmd.OnValueEntered(sketch, 3);                              // seção
        cmd.OnMouseDown(sketch, new Point3D(0, 0, 0));
        cmd.OnMouseDown(sketch, new Point3D(100, 0, 0));
        cmd.OnMouseDown(sketch, new Point3D(100, 80, 0));
        cmd.OnValueEntered(sketch, 25);                             // canto — e conclui

        codigo.Should().Contain("SweepSection.Circle(3)").And.Contain("cornerRadius: 25");
    }

    [Fact]
    public void CaminhoDePontoUnico_NaoGeraNada()
    {
        var sketch = NovoSketch();
        var cmd = new SweepCommand();
        string? codigo = CapturarCodigo(cmd);

        cmd.OnMouseDown(sketch, new Point3D(0, 0, 0));
        cmd.OnKeyDown(sketch, CadKey.Enter);

        codigo.Should().BeNull("um ponto só não é caminho");
    }

    [Fact]
    public void CliqueRepetidoNoMesmoPonto_NaoViraTrechoDeComprimentoZero()
    {
        var sketch = NovoSketch();
        var cmd = new SweepCommand();

        cmd.OnMouseDown(sketch, new Point3D(10, 10, 0));
        cmd.OnMouseDown(sketch, new Point3D(10, 10, 0));

        cmd.Points.Should().HaveCount(1);
    }

    [Fact]
    public void Escape_DescartaOCaminho()
    {
        var sketch = NovoSketch();
        var cmd = new SweepCommand();
        string? codigo = CapturarCodigo(cmd);

        cmd.OnMouseDown(sketch, new Point3D(0, 0, 0));
        cmd.OnMouseDown(sketch, new Point3D(50, 0, 0));
        cmd.OnKeyDown(sketch, CadKey.Escape);
        cmd.OnKeyDown(sketch, CadKey.Enter);

        cmd.Points.Should().BeEmpty();
        codigo.Should().BeNull();
    }

    [Fact]
    public void Preview_MostraOsTrechosMaisOElasticoAteOCursor()
    {
        var sketch = NovoSketch();
        var cmd = new SweepCommand();

        cmd.OnMouseDown(sketch, new Point3D(0, 0, 0));
        cmd.OnMouseDown(sketch, new Point3D(50, 0, 0));
        cmd.OnMouseMove(sketch, new Point3D(50, 30, 0));

        cmd.GetPreviewEntities().OfType<LineEntity>().Should().HaveCount(2, "um trecho fixo e o elástico");
    }

    [Fact]
    public async Task CodigoGerado_CompilaEVIRASolidoComOVolumeCerto()
    {
        // O elo que fecha tudo: os outros testes conferem o TEXTO emitido, este confere que o texto
        // vira geometria. Sem ele, um erro de grafia no código gerado passaria despercebido.
        var sketch = NovoSketch();
        var cmd = new SweepCommand();
        string? codigo = null;
        cmd.CodeGenerated += (_, c) => codigo = c;

        cmd.OnValueEntered(sketch, 5);
        cmd.OnMouseDown(sketch, new Point3D(0, 0, 0));
        cmd.OnMouseDown(sketch, new Point3D(100, 0, 0));
        cmd.OnMouseDown(sketch, new Point3D(100, 80, 0));
        cmd.OnValueEntered(sketch, 30);

        var result = await new RoslynSceneEvaluator().EvaluateAsync(codigo!);

        result.Success.Should().BeTrue(because: result.ErrorMessage ?? "o código gerado tem que compilar");
        result.Features.Should().ContainSingle();

        // Pappus: canto de 90° com raio 30 recua 30 de cada reta, então 100 e 80 viram 70 e 50.
        double comprimento = 70 + 30 * (System.Math.PI / 2) + 50;
        double areaSecao = MeshAreaCalculator.InscribedPolygonArea(5, 32);
        double volume = MeshVolumeCalculator.ComputeVolume(
            new OpenCascadeGeometryKernel().BuildFeatureMesh(result.Features[0]));

        volume.Should().BeApproximately(areaSecao * comprimento, areaSecao * comprimento * 0.01);
    }

    [Fact]
    public void CaminhoSaiEmCoordenadasDoMundo_MesmoComWorkPlaneDeslocado()
    {
        // Os cliques chegam em coordenadas LOCAIS ao work plane; SweepPath quer o mundo. Num plano
        // deslocado e girado, emitir o ponto local poria o tubo no lugar errado em silêncio.
        var sketch = NovoSketch();
        sketch.Plane = new WorkPlane(new Point3D(0, 0, 50), Vector3D.UnitZ, Vector3D.UnitX);

        var cmd = new SweepCommand();
        string? codigo = null;
        cmd.CodeGenerated += (_, c) => codigo = c;

        cmd.OnMouseDown(sketch, new Point3D(0, 0, 0));
        cmd.OnMouseDown(sketch, new Point3D(100, 0, 0));
        cmd.OnKeyDown(sketch, CadKey.Enter);

        codigo.Should().Contain("new Point3D(0, 0, 50)").And.Contain("new Point3D(100, 0, 50)");
    }
}
