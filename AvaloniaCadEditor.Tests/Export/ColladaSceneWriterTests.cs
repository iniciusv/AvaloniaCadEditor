using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq;
using AvaloniaCadEditor.Application.Export;
using AvaloniaCadEditor.Domain.Mesh;
using AvaloniaCadEditor.Infrastructure.Export.Collada;
using FluentAssertions;
using Xunit;

namespace AvaloniaCadEditor.Tests.Export;

public class ColladaSceneWriterTests : IDisposable
{
    private static readonly XNamespace Ns = "http://www.collada.org/2005/11/COLLADASchema";
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"export-{Guid.NewGuid():N}.dae");

    public void Dispose()
    {
        if (File.Exists(_path)) File.Delete(_path);
    }

    private static readonly ExportMaterial Cinza = new("material_0", "#EBEBF2", 0xEB, 0xEB, 0xF2);

    /// <summary>Uma peça com duas instâncias (uma deslocada) dentro da pasta do tipo, e um sólido solto.</summary>
    private static ExportScene Cena(string solto = "mesa")
    {
        var deslocada = ExportNode.Identity;
        deslocada[3] = 100.5;
        var componente = new ExportComponent("componente_0", "SuporteC", ExportTestMeshes.Cube(), Cinza.Id);
        return new ExportScene(
            [
                ExportNode.Folder("SuporteC",
                [
                    ExportNode.Instance("suporte_c_esquerdo", ExportNode.Identity, componente.Id),
                    ExportNode.Instance("suporte_c_direito", deslocada, componente.Id),
                ]),
                ExportNode.Group(solto, ExportTestMeshes.Cube(20), Cinza.Id),
            ],
            [componente], [Cinza], [], FilletsSuppressed: false);
    }

    private async Task<XDocument> Gravar(ExportScene scene)
    {
        await new ColladaSceneWriter().WriteAsync(scene, _path);
        return XDocument.Load(_path);
    }

    [Fact]
    public async Task Asset_MilimetroEZParaCima()
    {
        var doc = await Gravar(Cena());

        doc.Root!.Name.Should().Be(Ns + "COLLADA");
        doc.Root.Attribute("version")!.Value.Should().Be("1.4.1");
        var asset = doc.Root.Element(Ns + "asset")!;
        asset.Element(Ns + "unit")!.Attribute("meter")!.Value.Should().Be("0.001");
        asset.Element(Ns + "up_axis")!.Value.Should().Be("Z_UP");
    }

    [Fact]
    public async Task Componente_EscritoUmaVez_InstanciasReferenciamComMatriz()
    {
        var doc = await Gravar(Cena());

        var componentes = doc.Descendants(Ns + "library_nodes").Elements(Ns + "node").ToList();
        componentes.Should().ContainSingle().Which.Attribute("name")!.Value.Should().Be("SuporteC");
        doc.Descendants(Ns + "geometry").Should().HaveCount(2, "uma do componente e uma do grupo solto");

        var cena = doc.Descendants(Ns + "visual_scene").Single();
        var pasta = cena.Elements(Ns + "node").First();
        pasta.Attribute("name")!.Value.Should().Be("SuporteC");
        var instancias = pasta.Elements(Ns + "node").ToList();
        instancias.Should().HaveCount(2);
        instancias.Should().OnlyContain(i => i.Element(Ns + "instance_node")!.Attribute("url")!.Value == "#" + componentes[0].Attribute("id")!.Value);
        instancias[1].Element(Ns + "matrix")!.Value.Should().Be("1 0 0 100.5 0 1 0 0 0 0 1 0 0 0 0 1");

        // Ordem do esquema dentro do nó: a matriz antes das referências.
        instancias[1].Elements().First().Name.Should().Be(Ns + "matrix");
    }

    [Fact]
    public async Task IdsValidosEUnicos_NomeOriginalPreservado()
    {
        var doc = await Gravar(Cena(solto: "chapa_mdf_mesa ção/1"));

        var ids = doc.Descendants().Attributes("id").Select(a => a.Value).ToList();
        ids.Should().OnlyHaveUniqueItems();
        ids.Should().OnlyContain(id => Regex.IsMatch(id, "^[A-Za-z][A-Za-z0-9_-]*$"));
        doc.Descendants(Ns + "node").Attributes("name").Select(a => a.Value).Should().Contain("chapa_mdf_mesa ção/1");

        // Toda referência "#id" aponta para um id que existe.
        doc.Descendants().Attributes().Where(a => a.Name.LocalName is "url" or "source" or "target")
            .Select(a => a.Value.TrimStart('#')).Should().OnlyContain(r => ids.Contains(r));
    }

    [Fact]
    public async Task Numeros_ComPonto_MesmoComCulturaPtBr()
    {
        var anterior = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("pt-BR");
        try
        {
            var scene = Cena();
            var meio = new TriangleMesh([0.5f, 0, 0, 1.25f, 0, 0, 0, 2.75f, 0], new float[9], [0, 1, 2]);
            scene = scene with { Nodes = [.. scene.Nodes, ExportNode.Group("triangulo", meio, Cinza.Id)] };

            var doc = await Gravar(scene);

            doc.Descendants(Ns + "float_array").Last().Value.Should().Be("0.5 0 0 1.25 0 0 0 2.75 0");
            doc.Descendants(Ns + "color").Single().Value.Should().Be("0.9216 0.9216 0.949 1");
            doc.Descendants(Ns + "matrix").Select(m => m.Value).Should().NotContain(v => v.Contains(','));
        }
        finally
        {
            CultureInfo.CurrentCulture = anterior;
        }
    }

    [Fact]
    public async Task Solda_CuboDe24VerticesVira8_VolumeIgual()
    {
        var doc = await Gravar(Cena());

        var geometria = doc.Descendants(Ns + "geometry").First();
        var posicoes = geometria.Descendants(Ns + "float_array").Single().Value
            .Split(' ').Select(v => float.Parse(v, CultureInfo.InvariantCulture)).ToList();
        var indices = geometria.Descendants(Ns + "p").Single().Value.Split(' ').Select(int.Parse).ToList();

        posicoes.Should().HaveCount(8 * 3);
        indices.Should().HaveCount(12 * 3);
        geometria.Descendants(Ns + "triangles").Single().Attribute("count")!.Value.Should().Be("12");
        ExportTestMeshes.Volume(posicoes, indices).Should().BeApproximately(ExportTestMeshes.Volume(ExportTestMeshes.Cube()), 1e-3);
    }

    [Fact]
    public async Task TrianguloDegenerado_DescartadoNaSolda()
    {
        // Os vértices 0 e 1 estão no mesmo ponto: o triângulo não tem área.
        var degenerado = new TriangleMesh([0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 1, 0, 0, 0, 1, 0], new float[18], [0, 1, 2, 3, 4, 5]);
        var scene = new ExportScene([ExportNode.Group("g", degenerado, Cinza.Id)], [], [Cinza], [], false);

        var doc = await Gravar(scene);

        doc.Descendants(Ns + "triangles").Single().Attribute("count")!.Value.Should().Be("1");
    }
}
