using System.Linq;
using AvaloniaCadEditor.Application.Interfaces;
using AvaloniaCadEditor.Application.Scenes;
using AvaloniaCadEditor.Infrastructure.Scripting;
using FluentAssertions;
using Xunit;

namespace AvaloniaCadEditor.Tests.Scenes;

/// <summary>
/// Clicar num sólido do Build() de uma peça rastreia a cadeia que o produz — locais, funções
/// locais e, no fim, os parâmetros do record — e diz quais entradas são exclusivas dele.
/// O record abaixo tem a mesma forma da CantoneiraPlacaExtrusao2020 real.
/// </summary>
public class PartSolidTracerTests
{
    private const string CantoneiraRecord = """
        public sealed record Cantoneira(
            double EspessuraAbaPlaca = 6,
            double EspessuraAbaExtrusao = 5,
            double LarguraAbaPlacaY = 26.5,
            double AlturaAbaExtrusao = 16,
            double PassoFurosExtrusao = 20,
            double DistanciaPrimeiroFuroX = 15,
            double AlturaFurosPlacaZ = 16.625,
            double DistanciaFuroPlaca1Y = 21.5,
            double DistanciaFuroPlaca2Y = 6.5,
            double DiametroFuroM3 = 3.4,
            double AvancoReforcoX = 20,
            bool EspelhadoNoEixoX = false) : Part
        {
            protected override IFeature Build()
            {
                var sx = EspelhadoNoEixoX ? -1.0 : 1.0;
                var raioFuro = DiametroFuroM3 / 2;
                var zBase = -AlturaAbaExtrusao / 2;

                double CantoBaseX(double extensao) => sx > 0 ? 0 : -extensao;

                var aba_placa = new BoxFeature(new Point3D(CantoBaseX(EspessuraAbaPlaca), -LarguraAbaPlacaY, zBase), EspessuraAbaPlaca, LarguraAbaPlacaY, AvancoReforcoX) { Name = "aba_placa" };
                var corpo_bruto = BooleanFeature.CreateUnion("corpo_bruto", aba_placa);

                var entradaFurosPlacaX = -1 * sx;
                var direcaoFurosPlaca = new Vector3D(sx, 0, 0);
                var furo_placa_1 = new CylinderFeature(new Point3D(entradaFurosPlacaX, -DistanciaFuroPlaca1Y, AlturaFurosPlacaZ), direcaoFurosPlaca, raioFuro, EspessuraAbaPlaca + 2) { Name = "furo_placa_1", IsTransientTool = true };
                var furo_placa_2 = new CylinderFeature(new Point3D(entradaFurosPlacaX, -DistanciaFuroPlaca2Y, AlturaFurosPlacaZ), direcaoFurosPlaca, raioFuro, EspessuraAbaPlaca + 2) { Name = "furo_placa_2", IsTransientTool = true };

                var xFuroExtrusao1 = DistanciaPrimeiroFuroX * sx;
                var sem_variavel = new CylinderFeature(new Point3D(xFuroExtrusao1, -EspessuraAbaExtrusao - 1, 0), Vector3D.UnitY, raioFuro, EspessuraAbaExtrusao + 2) { Name = "furo_extrusao_1", IsTransientTool = true };

                return BooleanFeature.CreateDifference("cantoneira", corpo_bruto, furo_placa_1, furo_placa_2, sem_variavel);
            }
        }
        """;

    private static ScriptDocument[] Documents() => new[]
    {
        new ScriptDocument(ScriptDocument.DefaultSceneFile, "var c = Add(new Cantoneira());"),
        new ScriptDocument("Pecas/Cantoneira.cs", CantoneiraRecord)
    };

    private static PartSolidInputs Trace(string solidName) =>
        new RoslynPartSolidTracer().Trace(Documents(), "Cantoneira", solidName)!;

    private static SolidInput Input(PartSolidInputs inputs, string name) =>
        inputs.Inputs.Single(i => i.Name == name);

    [Fact]
    public void Trace_AchaOsParametrosDoRecordQueAlimentamOFuro()
    {
        var traced = Trace("furo_placa_1");

        traced.Inputs.Where(i => i.Kind == SolidInputKind.PartParameter).Select(i => i.Name)
            .Should().BeEquivalentTo(
                "DistanciaFuroPlaca1Y", "AlturaFurosPlacaZ", "DiametroFuroM3",
                "EspessuraAbaPlaca", "EspelhadoNoEixoX");
    }

    [Fact]
    public void Trace_AchaOsLocaisIntermediariosDaCadeia()
    {
        var traced = Trace("furo_placa_1");

        traced.Inputs.Where(i => i.Kind == SolidInputKind.Local).Select(i => i.Name)
            .Should().BeEquivalentTo("sx", "raioFuro", "entradaFurosPlacaX", "direcaoFurosPlaca");
    }

    [Fact]
    public void Trace_NaoIncluiParametrosQueNaoAlcancamOSolido()
    {
        var names = Trace("furo_placa_1").Inputs.Select(i => i.Name).ToList();

        // O que separa um rastreio de verdade de "todos os parâmetros da peça": estes alimentam
        // outras partes do Build(), e não o furo.
        names.Should().NotContain("LarguraAbaPlacaY");
        names.Should().NotContain("AlturaAbaExtrusao");
        names.Should().NotContain("AvancoReforcoX");
        names.Should().NotContain("PassoFurosExtrusao");
        names.Should().NotContain("DistanciaFuroPlaca2Y");
    }

    [Fact]
    public void Trace_SeparaOQueEExclusivoDoSolidoDoQueECompartilhado()
    {
        var traced = Trace("furo_placa_1");

        // Mexer aqui move só este furo.
        Input(traced, "DistanciaFuroPlaca1Y").IsExclusive.Should().BeTrue();
        Input(traced, "DistanciaFuroPlaca1Y").SharedWith.Should().BeEmpty();

        // Mexer aqui move os dois furos da placa.
        Input(traced, "AlturaFurosPlacaZ").SharedWith.Should().BeEquivalentTo("furo_placa_2");

        // O diâmetro vale para os três furos; a espessura da aba também é a placa.
        Input(traced, "DiametroFuroM3").SharedWith.Should().BeEquivalentTo("furo_placa_2", "sem_variavel");
        Input(traced, "EspessuraAbaPlaca").SharedWith.Should().Contain("aba_placa");
    }

    [Fact]
    public void Trace_ExclusivasVemPrimeiro_EParametrosAntesDosLocais()
    {
        var traced = Trace("furo_placa_1");

        traced.Inputs[0].Name.Should().Be("DistanciaFuroPlaca1Y");
        traced.Inputs[0].Kind.Should().Be(SolidInputKind.PartParameter);
    }

    [Fact]
    public void Trace_SegueFuncaoLocalAteOParametroQueElaUsa()
    {
        var traced = Trace("aba_placa");

        Input(traced, "CantoBaseX").Kind.Should().Be(SolidInputKind.LocalFunction);
        // CantoBaseX só usa sx, que vem de EspelhadoNoEixoX: sem atravessar a função, a trilha
        // pararia nela.
        traced.Inputs.Select(i => i.Name).Should().Contain("sx").And.Contain("EspelhadoNoEixoX");
    }

    [Fact]
    public void Trace_SolidoNomeadoSoPorLiteral_CaiNoFallbackEAindaRastreia()
    {
        // "furo_extrusao_1" é o Name, mas a variável se chama sem_variavel.
        var traced = new RoslynPartSolidTracer().Trace(Documents(), "Cantoneira", "furo_extrusao_1")!;

        traced.Should().NotBeNull();
        traced.Inputs.Select(i => i.Name).Should()
            .Contain("DistanciaPrimeiroFuroX").And.Contain("EspessuraAbaExtrusao").And.Contain("xFuroExtrusao1");
    }

    [Fact]
    public void Trace_DaADeclaracaoDoSolidoEDeCadaEntrada()
    {
        var traced = Trace("furo_placa_1");

        traced.Declaration.FilePath.Should().Be("Pecas/Cantoneira.cs");
        traced.Declaration.LineText.Should().StartWith("var furo_placa_1 = new CylinderFeature");

        // O local é destacado como instrução inteira (é o que explica a cadeia)...
        var raio = Input(traced, "raioFuro");
        raio.ValueCode.Should().Be("DiametroFuroM3 / 2");
        CantoneiraRecord.Substring(raio.Spans[0].Start, raio.Spans[0].Length)
            .Should().Be("var raioFuro = DiametroFuroM3 / 2;");

        // ...e o parâmetro do record, no próprio parâmetro da lista do topo, com o padrão atual.
        var distancia = Input(traced, "DistanciaFuroPlaca1Y");
        distancia.ValueCode.Should().Be("21.5");
        CantoneiraRecord.Substring(distancia.Spans[0].Start, distancia.Spans[0].Length)
            .Should().Be("double DistanciaFuroPlaca1Y = 21.5");
    }

    [Fact]
    public void Trace_PecaOuSolidoInexistente_DevolveNull()
    {
        var tracer = new RoslynPartSolidTracer();
        tracer.Trace(Documents(), "Cantoneira", "nao_existe").Should().BeNull();
        tracer.Trace(Documents(), "OutraPeca", "furo_placa_1").Should().BeNull();
    }

    // ---------------------------------------------------------------- Escopo por record

    private const string OutraPecaRecord = """
        public sealed record Bloco(double DiametroFuroM3 = 8) : Part
        {
            protected override IFeature Build()
            {
                var raioFuro = DiametroFuroM3 / 2;
                var furo_placa_1 = new CylinderFeature(new Point3D(0, 0, 0), Vector3D.UnitZ, raioFuro, 10) { Name = "furo_placa_1" };
                return BooleanFeature.CreateUnion("bloco", furo_placa_1);
            }
        }
        """;

    /// <summary>
    /// Dois records com uma variável de mesmo nome (<c>raioFuro</c>) e um sólido de mesmo nome não
    /// se misturam — o caso que o RoslynDependencyAnalyzer, que indexa variáveis por nome no
    /// arquivo todo, não sabe tratar.
    /// </summary>
    [Fact]
    public void Trace_DoisRecordsComNomesIguais_NaoVazamEntreSi()
    {
        var documents = new[]
        {
            new ScriptDocument(ScriptDocument.DefaultSceneFile, "var c = Add(new Cantoneira());"),
            new ScriptDocument("Pecas/Cantoneira.cs", CantoneiraRecord),
            new ScriptDocument("Pecas/Bloco.cs", OutraPecaRecord)
        };
        var tracer = new RoslynPartSolidTracer();

        var naCantoneira = tracer.Trace(documents, "Cantoneira", "furo_placa_1")!;
        var noBloco = tracer.Trace(documents, "Bloco", "furo_placa_1")!;

        naCantoneira.Declaration.FilePath.Should().Be("Pecas/Cantoneira.cs");
        noBloco.Declaration.FilePath.Should().Be("Pecas/Bloco.cs");

        // O mesmo nome de parâmetro, em peças diferentes, tem padrão diferente.
        naCantoneira.Inputs.Single(i => i.Name == "DiametroFuroM3").ValueCode.Should().Be("3.4");
        noBloco.Inputs.Single(i => i.Name == "DiametroFuroM3").ValueCode.Should().Be("8");

        // E o Bloco não enxerga nada da Cantoneira.
        noBloco.Inputs.Select(i => i.Name).Should().NotContain("sx").And.NotContain("DistanciaFuroPlaca1Y");
    }

    // ---------------------------------------------------------------- Gravar o padrão do record

    [Fact]
    public void SetPartTypeParameterDefault_TrocaOPadraoNoRecord()
    {
        string updated = PartSourceEditor.SetPartTypeParameterDefault(
            CantoneiraRecord, "Cantoneira", "DistanciaFuroPlaca1Y", "19.5");

        updated.Should().Contain("double DistanciaFuroPlaca1Y = 19.5");
        updated.Should().NotContain("double DistanciaFuroPlaca1Y = 21.5");
        // Os vizinhos ficam intactos.
        updated.Should().Contain("double DistanciaFuroPlaca2Y = 6.5");
    }

    [Fact]
    public void SetPartTypeParameterDefault_ParametroSemPadrao_Acrescenta()
    {
        const string source = """
            public sealed record Chapa(double Espessura) : Part
            {
                protected override IFeature Build() => new BoxFeature(Point3D.Origin, 10, 10, Espessura) { Name = "chapa" };
            }
            """;

        string updated = PartSourceEditor.SetPartTypeParameterDefault(source, "Chapa", "Espessura", "4");

        updated.Should().Contain("record Chapa(double Espessura = 4)");
    }

    [Fact]
    public void SetPartTypeParameterDefault_PadraoCalculado_Recusa()
    {
        const string source = """
            public sealed record Chapa(double Espessura = Medidas.Base * 2) : Part
            {
                protected override IFeature Build() => new BoxFeature(Point3D.Origin, 10, 10, Espessura) { Name = "chapa" };
            }
            """;

        var act = () => PartSourceEditor.SetPartTypeParameterDefault(source, "Chapa", "Espessura", "4");

        act.Should().Throw<System.InvalidOperationException>().WithMessage("*calculado no código*");
    }

    [Fact]
    public void SetPartTypeParameterDefault_PecaOuParametroInexistente_Recusa()
    {
        var semPeca = () => PartSourceEditor.SetPartTypeParameterDefault(CantoneiraRecord, "OutraPeca", "X", "1");
        semPeca.Should().Throw<System.InvalidOperationException>().WithMessage("*OutraPeca*");

        var semParametro = () => PartSourceEditor.SetPartTypeParameterDefault(CantoneiraRecord, "Cantoneira", "NaoExiste", "1");
        semParametro.Should().Throw<System.InvalidOperationException>().WithMessage("*NaoExiste*");
    }
}
