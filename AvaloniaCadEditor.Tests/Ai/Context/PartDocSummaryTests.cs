using AvaloniaCadEditor.Application.Ai.Context;
using FluentAssertions;
using Xunit;

namespace AvaloniaCadEditor.Tests.Ai.Context;

/// <summary>
/// O resumo que vai ao lado da assinatura de uma peça fora do contexto. Os casos de montagem do
/// PromptEval falhavam porque a IA via só a assinatura e não sabia onde ficava a origem da peça.
/// </summary>
public class PartDocSummaryTests
{
    private const string Carrinho = """
        /// <summary>
        /// Carrinho (bloco) longo MGN15H do trilho linear MGN15. Envelope de referência: 32 x 58,8 mm
        /// em planta, 16 mm de altura. Geometria simplificada (ver <see cref="TrilhoMgn15"/>).
        /// </summary>
        public sealed record CarrinhoMgn15H(double Largura = 32) : Part
        {
            // Geometria em coordenadas locais: a origem da peça é (0, 0, 0), coincidindo com a
            // base do trilho (o carrinho "abraça" o trilho por baixo até a altura dele).
            protected override IFeature Build()
            {
                // passo dos furos
                const double passo = 25;
                return new BoxFeature(Point3D.Origin, Largura, 58.8, 16) { Name = "corpo" };
            }
        }
        """;

    [Fact]
    public void PegaAPrimeiraFraseEOComentarioDoCorpoSobreAOrigem()
    {
        string summary = PartDocSummary.Extract(Carrinho, "CarrinhoMgn15H")!;

        summary.Should().StartWith("Carrinho (bloco) longo MGN15H do trilho linear MGN15.");
        summary.Should().Contain("a origem da peça é (0, 0, 0), coincidindo com a base do trilho (o carrinho \"abraça\" o trilho por baixo até a altura dele).");
        summary.Should().NotContain("passo dos furos", "comentários do corpo só entram quando falam da origem");
        summary.Should().NotContain("///").And.NotContain("<see");
    }

    [Fact]
    public void SummaryLongo_PegaAFraseDaOrigemMesmoNoFimECabeNoLimite()
    {
        string filler = string.Concat(Enumerable.Range(1, 30).Select(i => $"/// Detalhe de fabricação número {i} com bastante texto.\n"));
        string source = "/// <summary>\n/// Placa impressa que se parafusa sobre o carrinho.\n" + filler +
                        "/// Origem local (0,0,0) no centro da placa, na meia-espessura (base em Z = -5,5).\n/// </summary>\n" +
                        "public sealed record Placa(bool Espelhada = false) : Part { protected override IFeature Build() => null!; }";

        string summary = PartDocSummary.Extract(source, "Placa")!;

        summary.Should().StartWith("Placa impressa que se parafusa sobre o carrinho.");
        summary.Should().Contain("Origem local (0,0,0) no centro da placa, na meia-espessura");
        summary.Should().NotContain("Detalhe de fabricação");
        summary.Length.Should().BeLessThanOrEqualTo(PartDocSummary.MaxLength + 2);
    }

    [Fact]
    public void DecimalComVirgula_NaoQuebraAFrase()
    {
        string source = "/// <summary>Arruela de 0,5 mm. Origem no centro.</summary>\npublic sealed record Arruela : Part { protected override IFeature Build() => null!; }";

        PartDocSummary.Extract(source, "Arruela").Should().Be("Arruela de 0,5 mm. Origem no centro.");
    }

    [Fact]
    public void SemComentario_DevolveNull()
    {
        PartDocSummary.Extract("public sealed record Nada : Part { protected override IFeature Build() => null!; }", "Nada")
            .Should().BeNull();
    }

    [Fact]
    public void PecaQueNaoExiste_DevolveNull()
    {
        PartDocSummary.Extract(Carrinho, "Outra").Should().BeNull();
    }
}
