using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AvaloniaCadEditor.Application.Commands;
using AvaloniaCadEditor.Application.Interfaces;
using AvaloniaCadEditor.Application.Scenes;
using AvaloniaCadEditor.Application.Services;
using AvaloniaCadEditor.Domain.Features;
using AvaloniaCadEditor.Domain.Mesh;
using AvaloniaCadEditor.Domain.Profiles;
using AvaloniaCadEditor.Domain.ValueObjects;
using AvaloniaCadEditor.Infrastructure.Scripting;
using FluentAssertions;
using Xunit;

namespace AvaloniaCadEditor.Tests.Scenes;

/// <summary>
/// Fillet/chanfro aplicado pela interface numa instância de peça: vai para o fim do <c>Build()</c> do
/// record (ver <see cref="PartBuildFilletEditor"/>), com a normal da face em coordenadas da peça — e
/// não para a cena, onde a instância nem é uma variável <c>IFeature</c>.
/// </summary>
public class PartFilletEditingTests
{
    private const string RetornaVariavel = """
        public sealed record Bloco() : Part
        {
            protected override IFeature Build()
            {
                var corpo = new BoxFeature(Point3D.Origin, 20, 20, 10) { Name = "corpo" };
                var furo = new CylinderFeature(new Point3D(10, 10, -1), Vector3D.UnitZ, 1.5, 12) { Name = "furo", IsTransientTool = true };
                var bloco = BooleanFeature.CreateDifference("bloco", corpo, furo);
                return bloco;
            }
        }
        """;

    /// <summary>O topo do corpo (z = 10) como plano: normal +Z e um ponto da face.</summary>
    private static readonly (Vector3D Normal, Point3D Point)[] Topo = { (new Vector3D(0, 0, 1), new Point3D(10, 10, 10)) };

    [Fact]
    public void AddFillet_RetornoPorVariavel_EnvolveNumFilletComAFaceERetornaEle()
    {
        string updated = PartBuildFilletEditor.AddFillet(RetornaVariavel, "Bloco", FilletMode.Bevel, 2, Topo);

        updated.Should().Contain("var bloco_chanfrado = new FilletFeature(name: \"bloco_chanfrado\", baseFeature: bloco, radius: 2)");
        updated.Should().Contain("Mode = FilletMode.Bevel,");
        updated.Should().Contain("ApplyToAllEdges = false,");
        updated.Should().Contain("Faces = { new FaceReference(bloco, -1, new Vector3D(0, 0, 1), new Point3D(10, 10, 10), \"CustomFace\") }");
        updated.Should().Contain("return bloco_chanfrado;");
        updated.Should().NotContain("return bloco;");
    }

    [Fact]
    public void AddFillet_RetornoPorExpressao_ExtraiParaVariavelComONomeDoSolido()
    {
        const string record = """
            public sealed record Bloco() : Part
            {
                protected override IFeature Build()
                {
                    var corpo = new BoxFeature(Point3D.Origin, 20, 20, 10) { Name = "corpo" };
                    var furo = new CylinderFeature(new Point3D(10, 10, -1), Vector3D.UnitZ, 1.5, 12) { Name = "furo", IsTransientTool = true };
                    return BooleanFeature.CreateDifference("bloco_furado", corpo,
                        furo);
                }
            }
            """;

        string updated = PartBuildFilletEditor.AddFillet(record, "Bloco", FilletMode.Round, 1.5, Topo);

        updated.Should().Contain("var bloco_furado = BooleanFeature.CreateDifference(\"bloco_furado\", corpo,");
        updated.Should().Contain("var bloco_furado_arredondado = new FilletFeature(name: \"bloco_furado_arredondado\", baseFeature: bloco_furado, radius: 1.5)");
        updated.Should().Contain("Mode = FilletMode.Round,");
        updated.Should().Contain("return bloco_furado_arredondado;");
    }

    [Fact]
    public void AddFillet_SemNormais_AplicaEmTodasAsArestas()
    {
        string updated = PartBuildFilletEditor.AddFillet(RetornaVariavel, "Bloco", FilletMode.Bevel, 1, Array.Empty<(Vector3D, Point3D)>());

        updated.Should().Contain("ApplyToAllEdges = true");
        updated.Should().NotContain("Faces =");
    }

    [Fact]
    public void AddFillet_SegundoFillet_EncadeiaSobreOPrimeiroComNomeUnico()
    {
        string once = PartBuildFilletEditor.AddFillet(RetornaVariavel, "Bloco", FilletMode.Bevel, 1, Topo);
        string twice = PartBuildFilletEditor.AddFillet(once, "Bloco", FilletMode.Bevel, 1, new[] { (new Vector3D(0, 0, -1), new Point3D(10, 10, 0)) });

        twice.Should().Contain("var bloco_chanfrado_chanfrado = new FilletFeature(name: \"bloco_chanfrado_chanfrado\", baseFeature: bloco_chanfrado,");
        twice.Should().Contain("return bloco_chanfrado_chanfrado;");
    }

    [Fact]
    public void AddFillet_NomeJaUsado_GanhaSufixo()
    {
        string record = RetornaVariavel.Replace("return bloco;", "var bloco_chanfrado = 1;\n        return bloco;");

        string updated = PartBuildFilletEditor.AddFillet(record, "Bloco", FilletMode.Bevel, 1, Topo);

        updated.Should().Contain("var bloco_chanfrado_2 = new FilletFeature(name: \"bloco_chanfrado_2\"");
    }

    [Fact]
    public void AddFillet_BuildComCorpoDeExpressao_ViraBloco()
    {
        const string record = """
            public sealed record Placa() : Part
            {
                protected override IFeature Build() =>
                    new BoxFeature(Point3D.Origin, 10, 10, 2) { Name = "placa" };
            }
            """;

        string updated = PartBuildFilletEditor.AddFillet(record, "Placa", FilletMode.Bevel, 0.5, Topo);

        updated.Should().NotContain("=>");
        updated.Should().Contain("var placa = new BoxFeature(Point3D.Origin, 10, 10, 2) { Name = \"placa\" };");
        updated.Should().Contain("return placa_chanfrado;");
    }

    [Fact]
    public void AddFillet_RecordInexistente_Recusa()
    {
        var act = () => PartBuildFilletEditor.AddFillet(RetornaVariavel, "NaoExiste", FilletMode.Bevel, 1, Topo);

        act.Should().Throw<InvalidOperationException>().WithMessage("*NaoExiste*");
    }

    [Fact]
    public async Task AddFillet_CenaExecutada_DefinicaoViraFilletComANormalLocal()
    {
        var documents = new[]
        {
            new ScriptDocument(ScriptDocument.DefaultSceneFile, """var bloco = Add(new Bloco(), at: Placement.MirrorZ(), name: "bloco");"""),
            new ScriptDocument("Pecas/Bloco.cs", PartBuildFilletEditor.AddFillet(RetornaVariavel, "Bloco", FilletMode.Bevel, 2, Topo))
        };

        var result = await new RoslynSceneEvaluator().EvaluateAsync(documents);

        result.Success.Should().BeTrue(result.ErrorMessage);
        var instance = result.Features.OfType<PartInstance>().Single();
        var fillet = instance.Definition.Should().BeOfType<FilletFeature>().Subject;
        fillet.Name.Should().Be("bloco_chanfrado");
        fillet.Mode.Should().Be(FilletMode.Bevel);
        fillet.BaseFeature.Should().BeOfType<BooleanFeature>().Which.Name.Should().Be("bloco");
        var face = fillet.Faces.Should().ContainSingle().Subject;
        face.NormalHint.Should().Be(new Vector3D(0, 0, 1));
        face.PointOnFace.Should().Be(new Point3D(10, 10, 10));
        face.GetWorkPlane().Contains(new Point3D(0, 0, 10)).Should().BeTrue("o plano da face é o topo, z = 10");
        face.GetWorkPlane().Contains(new Point3D(0, 0, 0)).Should().BeFalse("a base, paralela, não é a face");
    }

    [Fact]
    public void FilletTool_FaceDeInstancia_PedeFilletNaPecaENaoGeraCodigoDeCena()
    {
        var instance = new PartInstance<Caixa>(new Caixa(), new BoxFeature(Point3D.Origin, 1, 1, 1), Placement.MirrorZ()) { Name = "caixa" };
        var face = new FaceProfile(instance, new WorkPlane(new Vector3D(0, 0, -1), new Point3D(0.5, 0.5, -1)), TriangleMesh.Empty);
        var selection = new SelectionService();
        selection.SelectProfile(face);
        var tool = new FilletTool(selection, FilletMode.Bevel);

        string? code = null;
        (PartInstance Owner, IReadOnlyList<(Vector3D Normal, Point3D Point)> WorldFaces, FilletMode Mode, double Radius)? request = null;
        tool.CodeGenerated += (_, c) => code = c;
        tool.PartFilletRequested += (_, r) => request = r;

        tool.Execute(new AvaloniaCadEditor.Domain.Entities.Sketch());

        code.Should().BeNull("o fillet de uma peça não vai para a cena");
        request.Should().NotBeNull();
        request!.Value.Owner.Should().BeSameAs(instance);
        request.Value.Mode.Should().Be(FilletMode.Bevel);
        var worldFace = request.Value.WorldFaces.Should().ContainSingle().Subject;
        worldFace.Normal.Should().Be(new Vector3D(0, 0, -1));
        worldFace.Point.Should().Be(new Point3D(0.5, 0.5, -1), "o ponto da face (origem do plano realçado) vai junto");
    }

    [Fact]
    public void ToPartFrame_InstanciaEspelhadaEmZ_FaceDeBaixoNoMundoViraOTopoDaPeca()
    {
        var local = PartBuildFilletEditor.ToPartFrame(Placement.MirrorZ(),
            new[] { (new Vector3D(0, 0, -1), new Point3D(3, 4, -17)) });

        local.Should().ContainSingle().Which.Should().Be((new Vector3D(0, 0, 1), new Point3D(3, 4, 17)));
    }

    [Fact]
    public void FilletTool_FaceDeFeatureSolta_GeraFaceReferenceComOPontoDaFace()
    {
        var bloco = BooleanFeature.CreateUnion("bloco", new BoxFeature(Point3D.Origin, 2, 2, 2), new BoxFeature(new Point3D(2, 0, 0), 2, 2, 2));
        var face = new FaceProfile(bloco, new WorkPlane(new Vector3D(0, 0, 1), new Point3D(1.25, 1, 2)), TriangleMesh.Empty);
        var selection = new SelectionService();
        selection.SelectProfile(face);
        var tool = new FilletTool(selection, FilletMode.Bevel);

        string? code = null;
        tool.CodeGenerated += (_, c) => code = c;
        tool.Execute(new AvaloniaCadEditor.Domain.Entities.Sketch());

        code.Should().Contain("new FaceReference(bloco, -1, new Vector3D(0, 0, 1), new Point3D(1.25, 1, 2), \"CustomFace\")");
    }

    private sealed record Caixa() : Part
    {
        protected override IFeature Build() => new BoxFeature(Point3D.Origin, 1, 1, 1) { Name = "caixa" };
    }
}
