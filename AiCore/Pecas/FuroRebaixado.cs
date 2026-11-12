/// <summary>
/// Vazio de fixação para parafuso de cabeça cilíndrica: furo passante + rebaixo coaxial para a
/// cabeça, para ser SUBTRAÍDO de outra peça (no Build dela:
/// <c>var furo_1 = new FuroRebaixado(...).Instance("furo_1");</c> e a instância entra no CreateDifference).
/// Coordenadas: as da peça hospedeira — construída já no lugar e instanciada sem Placement.
///  - Face: centro do furo SOBRE a face por onde o parafuso entra (a peça avança 1 mm para fora
///    dela sozinha, para o corte não ficar rente à face).
///  - Direcao: para dentro do material.
///  - Profundidade: espessura atravessada pelo furo, a partir da Face (ele sai 1 mm além).
///  - ProfundidadeRebaixo: quanto a cabeça do parafuso afunda, a partir da Face.
/// </summary>
public sealed record FuroRebaixado(
    Point3D Face = default,
    Vector3D? Direcao = null,
    double Profundidade = 10,
    double DiametroFuro = 4.5,
    double DiametroRebaixo = 7.5,
    double ProfundidadeRebaixo = 5.3) : Part
{
    /// <summary>Parafuso M3 de cabeça cilíndrica (cabeça Ø5,5 × 3), com folga de impressão.</summary>
    public static FuroRebaixado M3 => new(DiametroFuro: 3.4, DiametroRebaixo: 6.5, ProfundidadeRebaixo: 3.3);

    /// <summary>Parafuso M4 de cabeça cilíndrica (cabeça Ø7 × 4), com folga de impressão.</summary>
    public static FuroRebaixado M4 => new(DiametroFuro: 4.5, DiametroRebaixo: 7.5, ProfundidadeRebaixo: 4.3);

    /// <summary>Parafuso M5 de cabeça cilíndrica (cabeça Ø8,5 × 5), com folga de impressão.</summary>
    public static FuroRebaixado M5 => new(DiametroFuro: 5.5, DiametroRebaixo: 9.5, ProfundidadeRebaixo: 5.3);

    /// <summary>Vazio: a biblioteca oferece "Furar a peça aberta" (Face e Direcao vêm do clique).</summary>
    public override bool IsCutter => true;

    protected override IFeature Build()
    {
        const double sobra = 1;
        var direcao = (Direcao ?? new Vector3D(0, 0, -1)).Normalize();

        // O furo começa na própria Face, já dentro do rebaixo, para as duas tampas não ficarem coplanares.
        var rebaixo = new CylinderFeature(Face - direcao * sobra, direcao, DiametroRebaixo / 2, ProfundidadeRebaixo + sobra) { Name = "rebaixo" };
        var furo = new CylinderFeature(Face, direcao, DiametroFuro / 2, Profundidade + sobra) { Name = "furo" };
        var furo_rebaixado = BooleanFeature.CreateUnion("furo_rebaixado", rebaixo, furo);
        return furo_rebaixado;
    }
}
