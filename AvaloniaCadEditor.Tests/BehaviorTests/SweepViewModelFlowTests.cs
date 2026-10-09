using System.Linq;
using AvaloniaCadEditor.Application.Commands;
using AvaloniaCadEditor.Application.Services;
using AvaloniaCadEditor.Domain.Entities;
using AvaloniaCadEditor.Domain.Profiles;
using AvaloniaCadEditor.Domain.ValueObjects;
using AvaloniaCadEditor.Infrastructure.Engines;
using AvaloniaCadEditor.UI.ViewModels;
using FluentAssertions;
using Xunit;

namespace AvaloniaCadEditor.Tests.BehaviorTests;

/// <summary>
/// O Sweep visto de FORA da ferramenta: pelo CadViewModel, como o clique na viewport chega. Os
/// testes do SweepCommand chamam TryPickAt direto e por isso não enxergam a plumbing — o
/// despacho por tipo de ferramenta, o modo do editor e o reconhecimento de regiões.
/// </summary>
public class SweepViewModelFlowTests
{
    private static CadViewModel NovoVm()
    {
        var engine = new MathGeometryEngine();
        var vm = TestUtils.CreateCadViewModel(
            engine,
            new OSnapService(engine),
            new ProfileRecognitionService());
        vm.WorldUnitsPerPixel = 0.1;      // tolerância de clique realista (6 px)
        return vm;
    }

    /// <summary>Desenha o quadrado da seção e a cadeia em L do caminho, e reconhece as regiões.</summary>
    private static void DesenharCena(CadViewModel vm)
    {
        // Seção: um quadrado 20x20 fechado, longe do caminho.
        vm.Sketch.AddEntity(new LineEntity(new Point3D(200, 200, 0), new Point3D(220, 200, 0)) { Plane = WorkPlane.XY });
        vm.Sketch.AddEntity(new LineEntity(new Point3D(220, 200, 0), new Point3D(220, 220, 0)) { Plane = WorkPlane.XY });
        vm.Sketch.AddEntity(new LineEntity(new Point3D(220, 220, 0), new Point3D(200, 220, 0)) { Plane = WorkPlane.XY });
        vm.Sketch.AddEntity(new LineEntity(new Point3D(200, 220, 0), new Point3D(200, 200, 0)) { Plane = WorkPlane.XY });

        // Caminho: duas linhas contínuas.
        vm.Sketch.AddEntity(new LineEntity(new Point3D(0, 0, 0), new Point3D(100, 0, 0)) { Plane = WorkPlane.XY });
        vm.Sketch.AddEntity(new LineEntity(new Point3D(100, 0, 0), new Point3D(100, 80, 0)) { Plane = WorkPlane.XY });

        vm.RefreshProfiles();
    }

    [Fact]
    public void ReconheceARegiaoDaSecao()
    {
        var vm = NovoVm();
        DesenharCena(vm);

        vm.Sketch.Profiles.Should().NotBeEmpty("sem região reconhecida não há seção para escolher");
        vm.Sketch.Profiles.Any(p => p.ContainsPoint(new Point3D(210, 210, 0)))
            .Should().BeTrue("o quadrado desenhado tem que virar região clicável");
    }

    [Fact]
    public void SequenciaDoUsuario_RegiaoEnterLinhasEnter_GeraOSolido()
    {
        // Exatamente o que o usuário fez: aciona o Sweep, clica na região, Enter, clica nas linhas,
        // Enter. O Enter do meio não pode atrapalhar.
        var vm = NovoVm();
        DesenharCena(vm);

        string? codigo = null;
        vm.SelectSweepToolCommand.Execute(null);
        ((SweepCommand)vm.CurrentTool!).CodeGenerated += (_, c) => codigo = c;

        vm.OnPointerPressed(new Point3D(210, 210, 0), false);          // dentro da região
        ((SweepCommand)vm.CurrentTool!).Section.Should().NotBeNull("o clique dentro da região é a seção");

        vm.OnKeyDown("ENTER", false);                                   // o Enter "de confirmar"
        vm.CurrentTool.Should().BeOfType<SweepCommand>("o Enter do meio não pode largar a ferramenta");

        vm.OnPointerPressed(new Point3D(50, 0, 0), false);             // primeira linha
        vm.OnPointerPressed(new Point3D(100, 40, 0), false);           // segunda linha
        ((SweepCommand)vm.CurrentTool!).HasPickedPath.Should().BeTrue("as linhas clicadas são o caminho");

        vm.OnKeyDown("ENTER", false);

        codigo.Should().NotBeNull("a sequência tem que gerar o tubo");
    }

    [Fact]
    public void SecaoLargaComArestasCurtas_AjustaORaioDeCantoEmVezDeRecusar()
    {
        // Era o beco sem saída: o raio derivado da seção (2x a meia-largura) não cabe entre linhas
        // curtas, e a recusa só aparecia na barra de status. Agora o raio encurta para o que cabe.
        var vm = NovoVm();

        // Seção 40x6 (meia-largura 20 -> raio derivado 40).
        vm.Sketch.AddEntity(new LineEntity(new Point3D(200, 200, 0), new Point3D(240, 200, 0)) { Plane = WorkPlane.XY });
        vm.Sketch.AddEntity(new LineEntity(new Point3D(240, 200, 0), new Point3D(240, 206, 0)) { Plane = WorkPlane.XY });
        vm.Sketch.AddEntity(new LineEntity(new Point3D(240, 206, 0), new Point3D(200, 206, 0)) { Plane = WorkPlane.XY });
        vm.Sketch.AddEntity(new LineEntity(new Point3D(200, 206, 0), new Point3D(200, 200, 0)) { Plane = WorkPlane.XY });

        // Caminho em L de trechos de 30 -> cabe raio 30, não 40.
        vm.Sketch.AddEntity(new LineEntity(new Point3D(0, 0, 0), new Point3D(30, 0, 0)) { Plane = WorkPlane.XY });
        vm.Sketch.AddEntity(new LineEntity(new Point3D(30, 0, 0), new Point3D(30, 30, 0)) { Plane = WorkPlane.XY });
        vm.RefreshProfiles();

        string? codigo = null;
        vm.SelectSweepToolCommand.Execute(null);
        var cmd = (SweepCommand)vm.CurrentTool!;
        cmd.CodeGenerated += (_, c) => codigo = c;

        vm.OnPointerPressed(new Point3D(220, 203, 0), false);   // dentro da seção
        cmd.Section.Should().NotBeNull();

        vm.OnPointerPressed(new Point3D(15, 0, 0), false);      // linha 1
        vm.OnPointerPressed(new Point3D(30, 15, 0), false);     // linha 2
        vm.OnKeyDown("ENTER", false);

        cmd.Error.Should().BeNull("o raio tinha que se ajustar, não recusar");
        codigo.Should().NotBeNull();
        codigo.Should().Contain("SweepSection.Rectangle(40, 6)");
    }

    [Fact]
    public void PreSelecaoNaoCriaSolidoSozinha_OEnterEhQueConclui()
    {
        // Acionar o atalho não pode criar geometria na hora: se houvesse sobra de seleção, o sólido
        // nasceria errado e a ferramenta já teria saído de cena.
        var vm = NovoVm();
        DesenharCena(vm);

        var caminho = vm.Sketch.Entities.OfType<LineEntity>()
            .Where(l => l.StartPoint.X < 150 && l.EndPoint.X < 150).ToList();
        caminho.Should().HaveCount(2);
        foreach (var l in caminho) l.IsSelected = true;

        string? codigo = null;
        vm.SelectSweepToolCommand.Execute(null);
        var cmd = (SweepCommand)vm.CurrentTool!;
        cmd.CodeGenerated += (_, c) => codigo = c;

        codigo.Should().BeNull("acionar não conclui");
        vm.CurrentTool.Should().BeOfType<SweepCommand>();

        vm.OnKeyDown("ENTER", false);
        codigo.Should().NotBeNull("o Enter conclui o que a pré-seleção preparou");
    }

    [Fact]
    public void CliqueNaRegiao_NaoPegaUmaLinhaDaBordaPorEngano()
    {
        // O hit-test de entidades roda ANTES do de regiões: clicar perto da borda poderia pegar a
        // linha do contorno como caminho em vez da região como seção.
        var vm = NovoVm();
        DesenharCena(vm);

        vm.SelectSweepToolCommand.Execute(null);
        vm.OnPointerPressed(new Point3D(210, 210, 0), false);           // bem no centro

        var cmd = (SweepCommand)vm.CurrentTool!;
        cmd.Section.Should().NotBeNull();
        cmd.HasPickedPath.Should().BeFalse("o centro da região não é aresta nenhuma");
    }
}
