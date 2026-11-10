using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using AvaloniaCadEditor.Application.Interfaces;
using AvaloniaCadEditor.Application.Scenes;
using AvaloniaCadEditor.Domain.Features;
using AvaloniaCadEditor.Infrastructure.OpenCascade;
using AvaloniaCadEditor.Infrastructure.Scripting;
using FluentAssertions;
using Xunit;

namespace AvaloniaCadEditor.Tests.Scenes;

/// <summary>
/// A ConexaoYAdaptador5mm é a única peça do repo que funde tubos VARRIDOS tangentes, e o modo como
/// ela falha é silencioso: o OCCT não resolve a fusão de sólidos tangentes, o FuseBalanced descarta
/// o operando perdedor sem reportar nada, e o resultado é uma peça com UM ramal só — que passa no
/// `check --geometry` porque a malha não fica vazia. Só a geometria denuncia, então é ela que o
/// teste olha: a peça é simétrica em X por construção, e um ramal faltando quebra essa simetria.
/// </summary>
public class ConexaoYAdaptador5mmTests
{
    /// <summary>
    /// Ancora na localização DESTE arquivo, não no bin: o bin dos testes pode estar fora do repo
    /// (um OutDir próprio, por exemplo), e aí subir a pasta a partir dele nunca acha o AiCore.
    /// </summary>
    private static string AiCoreFolder([CallerFilePath] string? thisFile = null)
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(thisFile!)!);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "AiCore")))
        {
            dir = dir.Parent;
        }

        dir.Should().NotBeNull("este teste lê a cena AiCore do repo, então precisa achá-la");
        return Path.Combine(dir!.FullName, "AiCore");
    }

    /// <summary>
    /// A cena deste teste. Não é a <c>Cena.cs</c> do AiCore: ela é a montagem em que se está trabalhando (hoje,
    /// a impressora) e a conexão pode não estar nela, o que deixava o teste sem peça para conferir. A peça,
    /// essa sim, vem do disco como está (<c>Pecas/ConexaoYAdaptador5mm.cs</c>).
    /// </summary>
    private const string Cena = "Add(new ConexaoYAdaptador5mm(), at: Placement.At(120, 120, 20), name: \"conexao_y_5mm\");";

    private static async Task<IFeature> ConstruirPeca()
    {
        var documents = SceneProject.Load(AiCoreFolder()).ReadDocuments();
        string entry = SceneProject.FindEntryFile(documents);
        documents.Should().Contain(d => d.FilePath == entry, "a pasta AiCore precisa de um arquivo de cena para ser trocado pela cena do teste");

        var result = await new RoslynSceneEvaluator().EvaluateAsync(
            documents.Select(d => d.FilePath == entry ? d with { Text = Cena } : d).ToList());

        result.Success.Should().BeTrue(result.ErrorMessage);

        var peca = result.Features.FirstOrDefault(f => f.Name == "conexao_y_5mm");
        peca.Should().NotBeNull("a cena do teste cria a peça com este nome");
        return peca!;
    }

    [Fact]
    public async Task OsDoisRamaisSobrevivemAFusao_EAPecaFicaSimetricaEmX()
    {
        var peca = await ConstruirPeca();
        var malha = new OpenCascadeGeometryKernel().BuildFeatureMesh(peca);

        malha.TriangleCount.Should().BeGreaterThan(0);

        var xs = Enumerable.Range(0, malha.VertexCount).Select(i => (double)malha.Positions[i * 3]).ToList();
        var zs = Enumerable.Range(0, malha.VertexCount).Select(i => (double)malha.Positions[i * 3 + 2]).ToList();

        // A peça está em Placement.At(120, 120, 20) e é simétrica em torno do próprio eixo.
        const double xEixo = 120.0;
        var folga = 0.2;

        // Envelope em X: ±(Deslocamento + RaioExterno) = ±9,75 em torno do eixo, dos dois lados.
        (xs.Min() - xEixo).Should().BeApproximately(-9.75, folga);
        (xs.Max() - xEixo).Should().BeApproximately(+9.75, folga);

        // Altura total 45 mm: boca inferior em z = 0 (apoiada no plano) e bocas superiores em z = 45.
        zs.Min().Should().BeApproximately(0.0, folga);
        zs.Max().Should().BeApproximately(45.0, folga);

        // O teste de fato: no topo existem DUAS bocas, uma de cada lado do eixo. Com um ramal
        // descartado, todos os vértices do topo cairiam de um lado só.
        var topo = Enumerable.Range(0, malha.VertexCount)
            .Where(i => malha.Positions[i * 3 + 2] > 44.9)
            .Select(i => (double)malha.Positions[i * 3] - xEixo)
            .ToList();

        topo.Should().Contain(x => x < -1.0, "a boca superior esquerda tem que existir");
        topo.Should().Contain(x => x > +1.0, "a boca superior direita tem que existir");

        // E são simétricas: o ramal esquerdo chega tão longe quanto o direito.
        (topo.Min() + topo.Max()).Should().BeApproximately(0.0, folga);
    }

    /// <summary>
    /// Os três canais comunicam, então a peça é um tubo: oca por dentro. Se os furos varridos
    /// tivessem sido descartados (a mesma armadilha de tangência, do lado das ferramentas de corte),
    /// a peça sairia maciça — e nada mais na cena acusaria isso.
    /// </summary>
    [Fact]
    public async Task OsCanaisInternosForamCortados_EntaoAPecaEOca()
    {
        var peca = await ConstruirPeca();
        var malha = new OpenCascadeGeometryKernel().BuildFeatureMesh(peca);

        // A boca inferior é um anel, não um disco: na base existe vértice a ~3,25 do eixo
        // (RaioFuroPorta), que só a parede interna do furo produz. O raio externo NESSA face é
        // 4,25 e não RaioExterno 4,75, porque o chanfro comeu o canto externo — ou seja, a mesma
        // medida confere o furo e o chanfro de uma vez.
        var raiosNaBase = Enumerable.Range(0, malha.VertexCount)
            .Where(i => malha.Positions[i * 3 + 2] < 0.1)
            .Select(i => Math.Sqrt(
                Math.Pow(malha.Positions[i * 3] - 120.0, 2) +
                Math.Pow(malha.Positions[i * 3 + 1] - 120.0, 2)))
            .ToList();

        raiosNaBase.Should().NotBeEmpty();
        raiosNaBase.Min().Should().BeApproximately(3.25, 0.2, "a boca inferior tem que estar furada");
        raiosNaBase.Max().Should().BeApproximately(4.25, 0.2, "o chanfro recua a borda externa em Chanfro");
    }
}
