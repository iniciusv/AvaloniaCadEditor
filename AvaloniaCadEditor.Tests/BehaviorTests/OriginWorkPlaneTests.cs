using System.Linq;
using AvaloniaCadEditor.Application.Services;
using AvaloniaCadEditor.Domain.Entities;
using AvaloniaCadEditor.Domain.ValueObjects;
using AvaloniaCadEditor.Infrastructure.OpenCascade;
using AvaloniaCadEditor.UI.ViewModels;
using FluentAssertions;
using Moq;
using Xunit;

namespace AvaloniaCadEditor.Tests.BehaviorTests;

/// <summary>
/// Os três planos de origem (XY, XZ, YZ) existem desde que a cena nasce, sem depender de uma face
/// para clicar. É o que permite esboçar ou varrer um caminho "em pé" numa cena vazia — sem eles, o
/// Sweep só conseguia caminhos no chão.
/// </summary>
public class OriginWorkPlaneTests
{
    private static CadViewModel NovoViewModel()
    {
        var mockGeo = new Mock<AvaloniaCadEditor.Domain.Interfaces.IGeometryEngine>();
        var mockSnap = new Mock<IOSnapService>();
        return TestUtils.CreateCadViewModel(mockGeo.Object, mockSnap.Object, null, new PureMathGeometryKernel(), new SelectionService());
    }

    [Fact]
    public void SketchNovo_JaTemOsTresPlanosDeOrigem_TodosDeBase()
    {
        var sketch = new Sketch();

        sketch.WorkPlanes.Select(w => w.Name).Should()
            .Equal("Plano Base (XY)", "Plano Base (XZ)", "Plano Base (YZ)");
        sketch.WorkPlanes.Should().OnlyContain(w => w.IsBasePlane);
        sketch.Plane.Should().Be(WorkPlane.XY);
    }

    /// <summary>
    /// O Y local dos dois planos em pé sobe no Z do mundo: é o que faz um caminho desenhado neles
    /// sair do chão, que é o motivo de eles existirem.
    /// </summary>
    [Theory]
    [InlineData("XZ", 0.0, 0.0, 1.0)]
    [InlineData("YZ", 0.0, 0.0, 1.0)]
    public void PlanosEmPe_TemYLocalSubindoNoZDoMundo(string qual, double x, double y, double z)
    {
        var plano = qual == "XZ" ? WorkPlane.XZ : WorkPlane.YZ;

        plano.YAxis.Should().Be(new Vector3D(x, y, z));
        // Base destra: X × Y = normal. Se a normal apontasse ao contrário, a face gerada por um
        // perfil desenhado no plano nasceria virada para dentro.
        plano.XAxis.CrossProduct(plano.YAxis).Should().Be(plano.Normal);
    }

    [Fact]
    public void PlanoXZ_LevaOYLocalParaOZDoMundo()
    {
        WorkPlane.XZ.ToWorldSpace(new Point3D(10, 20, 0)).Should().Be(new Point3D(10, 0, 20));
    }

    [Fact]
    public void PlanoYZ_LevaOXLocalParaOYDoMundo()
    {
        WorkPlane.YZ.ToWorldSpace(new Point3D(10, 20, 0)).Should().Be(new Point3D(0, 10, 20));
    }

    [Theory]
    [InlineData("XZ")]
    [InlineData("YZ")]
    [InlineData("XY")]
    public void BotaoDoPlanoDeOrigem_AtivaOPlanoEEntraEmEsboco(string qual)
    {
        var vm = NovoViewModel();
        var esperado = qual switch
        {
            "XZ" => WorkPlane.XZ,
            "YZ" => WorkPlane.YZ,
            _ => WorkPlane.XY
        };

        vm.SelectOriginWorkPlaneCommand.Execute(qual);

        vm.ActiveWorkPlane.Should().Be(esperado);
        // Entrar em esboço é parte do comando: a grade só gira em SketchEdit, e sem ela nada na
        // viewport diz em que plano o próximo clique cai.
        vm.CurrentMode.Should().Be(EditorMode.SketchEdit);
        vm.ActiveWorkPlaneLabel.Should().Be($"Plano Base ({qual})");
    }

    /// <summary>
    /// Reusa o plano de origem que já está na árvore em vez de criar um "Plano 1" paralelo a ele —
    /// dois WorkPlaneFeature iguais duplicariam o nó e os perfis desenhados ali.
    /// </summary>
    [Fact]
    public void AtivarPlanoDeOrigem_NaoCriaPlanoNovo()
    {
        var vm = NovoViewModel();
        var antes = vm.Sketch.WorkPlanes.Count;

        vm.SelectOriginWorkPlaneCommand.Execute("XZ");
        vm.SelectOriginWorkPlaneCommand.Execute("YZ");

        vm.Sketch.WorkPlanes.Should().HaveCount(antes);
    }

    /// <summary>
    /// O primeiro plano tirado de uma face é "Plano 1", não "Plano 3": os de origem já têm nome
    /// próprio e não entram na numeração.
    /// </summary>
    [Fact]
    public void PlanoTiradoDeFace_NumeraAPartirDeUm()
    {
        var vm = NovoViewModel();

        vm.ActiveWorkPlane = new WorkPlane(new Vector3D(0, 0, 1), new Point3D(0, 0, 50));

        vm.Sketch.WorkPlanes.Should().HaveCount(4);
        vm.ActiveWorkPlaneLabel.Should().Be("Plano 1");
    }
}
