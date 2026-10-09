using System.Collections.Generic;
using System.Linq;
using AvaloniaCadEditor.Application.Services;
using AvaloniaCadEditor.Domain.Entities;
using AvaloniaCadEditor.Domain.ValueObjects;
using FluentAssertions;
using Xunit;

namespace AvaloniaCadEditor.Tests.BehaviorTests;

/// <summary>
/// Encadear arestas de esboço num caminho. O que importa é a ORDEM (a varredura percorre os pontos
/// em sequência) e as recusas: uma cadeia que se ramifica ou que vem em pedaços não é caminho, e
/// deixar passar daria um sólido errado em silêncio.
/// </summary>
public class EdgeChainServiceTests
{
    private static LineEntity Linha(double x1, double y1, double x2, double y2, WorkPlane? plane = null) =>
        new(new Point3D(x1, y1, 0), new Point3D(x2, y2, 0)) { Plane = plane ?? WorkPlane.XY };

    private static IEnumerable<(double X, double Y)> Xy(EdgeChain chain) =>
        chain.Points.Select(p => (p.X, p.Y));

    [Fact]
    public void CadeiaAberta_SaiEmOrdem_MesmoComAEntradaEmbaralhada()
    {
        // As arestas chegam na ordem em que foram marcadas (ou pegas pelo retângulo), que não tem
        // nada a ver com a ordem do caminho.
        var chain = EdgeChainService.Chain(new[]
        {
            Linha(100, 0, 100, 80),
            Linha(0, 0, 100, 0),
        });

        chain.Error.Should().BeNull();
        chain.IsClosed.Should().BeFalse();
        Xy(chain).Should().Equal((0, 0), (100, 0), (100, 80));
    }

    [Fact]
    public void InverterAOrdemDaEntrada_DaAMesmaSequencia()
    {
        // O rumo inicial do caminho decide a orientação da seção varrida: se a ponta de partida
        // mudasse com a ordem da entrada, o mesmo desenho daria sólidos espelhados.
        var arestas = new[] { Linha(0, 0, 100, 0), Linha(100, 0, 100, 80) };

        var direta = EdgeChainService.Chain(arestas);
        var invertida = EdgeChainService.Chain(arestas.Reverse().ToArray());

        Xy(invertida).Should().Equal(Xy(direta));
    }

    [Fact]
    public void CadeiaFechada_NaoRepeteOPrimeiroPonto()
    {
        // A aresta de volta é implícita, como o SweepPath.FromPolyline com closed: true espera.
        var chain = EdgeChainService.Chain(new[]
        {
            Linha(0, 0, 100, 0),
            Linha(100, 0, 100, 100),
            Linha(100, 100, 0, 100),
            Linha(0, 100, 0, 0),
        });

        chain.Error.Should().BeNull();
        chain.IsClosed.Should().BeTrue();
        chain.Points.Should().HaveCount(4);
        chain.Points[0].DistanceTo(chain.Points[^1]).Should().BeGreaterThan(1.0);
    }

    [Fact]
    public void UmaLinhaSo_EhUmCaminhoAbertoDeDoisPontos()
    {
        var chain = EdgeChainService.Chain(new[] { Linha(0, 0, 50, 0) });

        chain.Error.Should().BeNull();
        chain.IsClosed.Should().BeFalse();
        Xy(chain).Should().Equal((0, 0), (50, 0));
    }

    [Fact]
    public void LinhasQueSeRamificam_SaoRecusadas()
    {
        // Um "T": três arestas no mesmo nó. Não há caminho único a seguir.
        var chain = EdgeChainService.Chain(new[]
        {
            Linha(0, 0, 50, 0),
            Linha(50, 0, 100, 0),
            Linha(50, 0, 50, 50),
        });

        chain.Error.Should().NotBeNull().And.Contain("ramific");
        chain.Points.Should().BeEmpty();
    }

    [Fact]
    public void LinhasSoltas_EmPedacosSeparados_SaoRecusadas()
    {
        var chain = EdgeChainService.Chain(new[]
        {
            Linha(0, 0, 10, 0),
            Linha(100, 0, 110, 0),
        });

        chain.Error.Should().NotBeNull().And.Contain("cadeia contínua");
    }

    [Fact]
    public void ArestasEmPlanosDiferentes_EncadeiamPelaPontaEmComum()
    {
        // O encadeamento é em coordenadas de MUNDO, então uma cadeia que passa de um plano para
        // outro é um caminho 3D legítimo. Em coordenadas locais as duas pareceriam desconexas.
        var chain = EdgeChainService.Chain(new[]
        {
            Linha(0, 0, 10, 0, WorkPlane.XY),      // mundo: (0,0,0) -> (10,0,0)
            Linha(10, 0, 20, 0, WorkPlane.XZ),     // mundo: (10,0,0) -> (20,0,0)
        });

        chain.Error.Should().BeNull();
        chain.Points.Should().HaveCount(3);
    }

    [Fact]
    public void CadeiaQueSobeDeUmPlanoParaOutro_SaiEmMundo()
    {
        // XY depois XZ: o caminho vira um L que sai do plano do chão.
        var chain = EdgeChainService.Chain(new[]
        {
            Linha(0, 0, 100, 0, WorkPlane.XY),     // mundo: (0,0,0) -> (100,0,0)
            Linha(100, 0, 100, 80, WorkPlane.XZ),  // mundo: (100,0,0) -> (100,0,80)
        });

        chain.Error.Should().BeNull();
        chain.Points.Select(p => (p.X, p.Y, p.Z))
             .Should().Equal((0, 0, 0), (100, 0, 0), (100, 0, 80));
    }

    [Fact]
    public void DuasLinhasComAsDuasPontasEmComum_NaoFormamCaminhoFechado()
    {
        // Grau 2 em todo nó, um componente só - parece ciclo, mas é um "bico" de duas arestas, e
        // uma varredura precisa de pelo menos três para fechar.
        var chain = EdgeChainService.Chain(new[]
        {
            Linha(0, 0, 100, 0),
            Linha(100, 0, 0, 0),
        });

        chain.Error.Should().NotBeNull().And.Contain("três arestas");
    }

    [Fact]
    public void PontasQuaseJuntas_EncadeiamDentroDaTolerancia()
    {
        var chain = EdgeChainService.Chain(new[]
        {
            Linha(0, 0, 100, 0),
            Linha(100.00005, 0, 200, 0),
        });

        chain.Error.Should().BeNull();
        chain.Points.Should().HaveCount(3);
    }

    [Fact]
    public void PontasLongeDaTolerancia_NaoEncadeiam()
    {
        var chain = EdgeChainService.Chain(new[]
        {
            Linha(0, 0, 100, 0),
            Linha(100.001, 0, 200, 0),
        });

        chain.Error.Should().NotBeNull().And.Contain("cadeia contínua");
    }

    [Fact]
    public void ArestaDeComprimentoZero_ERecusada()
    {
        var chain = EdgeChainService.Chain(new[]
        {
            Linha(0, 0, 100, 0),
            Linha(100, 0, 100, 0),
        });

        chain.Error.Should().NotBeNull().And.Contain("comprimento zero");
    }

    [Fact]
    public void NenhumaAresta_ERecusada()
    {
        EdgeChainService.Chain(System.Array.Empty<LineEntity>())
            .Error.Should().NotBeNull().And.Contain("nenhuma aresta");
    }
}
