using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AvaloniaCadEditor.Application.Export;
using AvaloniaCadEditor.Application.Interfaces;
using AvaloniaCadEditor.Application.Services;
using AvaloniaCadEditor.Domain.Features;
using AvaloniaCadEditor.Domain.Mesh;
using AvaloniaCadEditor.Domain.ValueObjects;
using FluentAssertions;
using Moq;
using Xunit;

namespace AvaloniaCadEditor.Tests.Export;

public class SceneExportCollectorTests
{
    private sealed record Suporte : Part
    {
        protected override IFeature Build() => throw new NotSupportedException();
    }

    private sealed record Bloco : Part
    {
        protected override IFeature Build() => throw new NotSupportedException();
    }

    private static BoxFeature Caixa(string name = "caixa") => new(Point3D.Origin, 10, 10, 10) { Name = name };

    /// <summary>Malha cúbica para tudo, como o kernel faria: a da instância já em mundo, a da definição local.</summary>
    private static Mock<ISceneMeshManager> Malhas(Func<IFeature, TriangleMesh>? meshFor = null, Dictionary<Guid, string>? problems = null)
    {
        var mock = new Mock<ISceneMeshManager>();
        mock.Setup(m => m.GetMeshFor(It.IsAny<IFeature>())).Returns((IFeature f) =>
            meshFor?.Invoke(f) ?? (f is PartInstance i ? ExportTestMeshes.Cube().Transform(i.Placement) : ExportTestMeshes.Cube()));
        mock.SetupGet(m => m.BuildProblems).Returns(problems ?? new Dictionary<Guid, string>());
        return mock;
    }

    private static ExportScene Coletar(params IFeature[] features) =>
        new SceneExportCollector(Malhas().Object).Collect(features);

    private static IEnumerable<ExportNode> Todos(IEnumerable<ExportNode> nodes) =>
        nodes.SelectMany(n => Todos(n.Children).Prepend(n));

    [Fact]
    public void DuasInstanciasDaMesmaDefinicao_UmComponente_DuasInstanciasNaPastaDoTipo()
    {
        var definicao = Caixa();
        var esquerdo = new PartInstance<Suporte>(new Suporte(), definicao, Placement.At(0, 0, 0)) { Name = "suporte_esquerdo" };
        var direito = new PartInstance<Suporte>(new Suporte(), definicao, Placement.At(100, 0, 0)) { Name = "suporte_direito" };

        var cena = Coletar(esquerdo, direito);

        cena.Components.Should().ContainSingle().Which.Name.Should().Be("Suporte");
        var pasta = cena.Nodes.Should().ContainSingle().Subject;
        pasta.Name.Should().Be("Suporte");
        pasta.Mesh.Should().BeNull();
        pasta.Children.Select(c => c.Name).Should().Equal("suporte_esquerdo", "suporte_direito");
        pasta.Children.Should().OnlyContain(c => c.ComponentId == cena.Components[0].Id && c.Mesh == null);
    }

    [Fact]
    public void MesmaDefinicaoComCoresDiferentes_DoisComponentes()
    {
        var definicao = Caixa();
        var cena = Coletar(
            new PartInstance<Suporte>(new Suporte(), definicao, Placement.Identity) { Name = "a", Color = "#FF0000" },
            new PartInstance<Suporte>(new Suporte(), definicao, Placement.Identity) { Name = "b", Color = "#00FF00" });

        cena.Components.Select(c => c.Name).Should().Equal("Suporte", "Suporte_2");
        cena.Components.Select(c => c.MaterialId).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void DuasDefinicoesDoMesmoTipo_ComponentesComNomesDistintos_NaMesmaPasta()
    {
        var cena = Coletar(
            new PartInstance<Suporte>(new Suporte(), Caixa(), Placement.Identity) { Name = "a" },
            new PartInstance<Suporte>(new Suporte(), Caixa(), Placement.Identity) { Name = "b" });

        cena.Components.Select(c => c.Name).Should().Equal("Suporte", "Suporte_2");
        cena.Nodes.Should().ContainSingle().Which.Children.Should().HaveCount(2);
    }

    [Fact]
    public void Matriz_LevaPontoLocalAoMesmoPontoQueOPlacement()
    {
        var placement = Placement.Rotation(new Vector3D(1, 1, 0), 35).Then(Placement.At(12, -7, 30));
        var cena = Coletar(new PartInstance<Suporte>(new Suporte(), Caixa(), placement) { Name = "girado" });

        var m = cena.Nodes[0].Children[0].Matrix;
        var local = new Point3D(3, 5, 7);
        var esperado = placement.Apply(local);
        (m[0] * local.X + m[1] * local.Y + m[2] * local.Z + m[3]).Should().BeApproximately(esperado.X, 1e-9);
        (m[4] * local.X + m[5] * local.Y + m[6] * local.Z + m[7]).Should().BeApproximately(esperado.Y, 1e-9);
        (m[8] * local.X + m[9] * local.Y + m[10] * local.Z + m[11]).Should().BeApproximately(esperado.Z, 1e-9);
        m[12..].Should().Equal(0, 0, 0, 1);
    }

    [Fact]
    public void InstanciaEspelhada_ViraGrupoComMalhaDoMundo_NaPastaDoTipo()
    {
        var definicao = Caixa();
        var normal = new PartInstance<Suporte>(new Suporte(), definicao, Placement.Identity) { Name = "normal" };
        var espelhada = new PartInstance<Suporte>(new Suporte(), definicao, Placement.MirrorX(atX: 50)) { Name = "espelhada" };

        var cena = Coletar(normal, espelhada);

        var filhos = cena.Nodes.Should().ContainSingle().Subject.Children;
        filhos.Single(c => c.Name == "espelhada").Should().Match<ExportNode>(n => n.Mesh != null && n.ComponentId == null);
        ExportTestMeshes.Volume(filhos.Single(c => c.Name == "espelhada").Mesh!).Should().BeApproximately(1000, 1e-3);
        cena.Components.Should().ContainSingle();
    }

    [Fact]
    public void SolidoSolto_GrupoNaRaiz_FerramentaEscondidaFicaDeFora()
    {
        var mesa = Caixa("mesa");
        var ferramenta = Caixa("furo");
        ferramenta.IsVisible = false;

        var cena = Coletar(mesa, ferramenta);

        cena.Nodes.Should().ContainSingle().Which.Should().Match<ExportNode>(n => n.Name == "mesa" && n.Mesh != null && n.Children.Count == 0);
        cena.Components.Should().BeEmpty();
    }

    [Fact]
    public void ComSelecao_SoOsSelecionados()
    {
        var a = Caixa("a");
        var b = Caixa("b");
        b.IsSelected = true;

        Coletar(a, b).Nodes.Select(n => n.Name).Should().Equal("b");
    }

    [Fact]
    public void MalhaVazia_InstanciaVaiParaSkipped_SolidoSemProblemaNaoAvisa()
    {
        var plano = Caixa("plano_de_trabalho");
        var quebrado = Caixa("quebrado");
        var instancia = new PartInstance<Suporte>(new Suporte(), Caixa(), Placement.Identity) { Name = "peca_vazia" };
        var malhas = Malhas(_ => TriangleMesh.Empty, new Dictionary<Guid, string> { [quebrado.Id] = "falhou" });

        var cena = new SceneExportCollector(malhas.Object).Collect([plano, quebrado, instancia]);

        cena.Nodes.Should().BeEmpty("a pasta do tipo sem instâncias não deve aparecer");
        cena.Skipped.Should().BeEquivalentTo("quebrado", "peca_vazia");
    }

    [Fact]
    public void NomesRepetidos_FicamUnicos_InclusiveContraAPastaDoTipo()
    {
        var cena = Coletar(
            Caixa("x"), Caixa("x"), Caixa(""),
            new PartInstance<Bloco>(new Bloco(), Caixa(), Placement.Identity) { Name = "Bloco" });

        Todos(cena.Nodes).Select(n => n.Name).Should().OnlyHaveUniqueItems()
            .And.Contain(["x", "x_2", "solido_1", "Bloco", "Bloco_2"]);
    }

    [Fact]
    public void Materiais_UmPorCor_SemCorUsaOPadraoDaViewport()
    {
        var cena = Coletar(
            new BoxFeature(Point3D.Origin, 1, 1, 1) { Name = "a", Color = "#a67b50" },
            new BoxFeature(Point3D.Origin, 1, 1, 1) { Name = "b", Color = "#A67B50" },
            new BoxFeature(Point3D.Origin, 1, 1, 1) { Name = "c" });

        cena.Materials.Select(m => m.Name).Should().Equal("#A67B50", "#EBEBF2");
        cena.Materials[0].Should().Match<ExportMaterial>(m => m.R == 0xA6 && m.G == 0x7B && m.B == 0x50);
        cena.Nodes[0].MaterialId.Should().Be(cena.Nodes[1].MaterialId);
    }

    [Fact]
    public async Task Servico_ComCenaConstruindo_NaoGrava()
    {
        var malhas = Malhas();
        malhas.SetupGet(m => m.PendingCount).Returns(3);
        var escritor = new Mock<ISceneFileWriter>();

        var resultado = await new SceneExportService(malhas.Object, escritor.Object).ExportAsync([Caixa()], "x.dae");

        resultado.Succeeded.Should().BeFalse();
        resultado.Message.Should().Contain("construindo");
        escritor.Verify(w => w.WriteAsync(It.IsAny<ExportScene>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task Servico_Grava_EResumeOQueSaiu()
    {
        var malhas = Malhas();
        var escritor = new Mock<ISceneFileWriter>();
        escritor.Setup(w => w.WriteAsync(It.IsAny<ExportScene>(), It.IsAny<string>())).Returns(Task.CompletedTask);
        var definicao = Caixa();

        var resultado = await new SceneExportService(malhas.Object, escritor.Object).ExportAsync(
        [
            new PartInstance<Suporte>(new Suporte(), definicao, Placement.Identity) { Name = "a" },
            new PartInstance<Suporte>(new Suporte(), definicao, Placement.At(5, 0, 0)) { Name = "b" },
            Caixa("mesa"),
        ], "x.dae");

        resultado.Succeeded.Should().BeTrue();
        resultado.Message.Should().Contain("2 peça(s) em 1 componente(s) e 1 grupo(s)");
        escritor.Verify(w => w.WriteAsync(It.IsAny<ExportScene>(), "x.dae"), Times.Once);
    }
}
