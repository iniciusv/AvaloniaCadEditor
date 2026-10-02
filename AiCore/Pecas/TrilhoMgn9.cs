/// <summary>
/// Trilho de guia linear miniatura MGN9 (perfil 9 x 6,5 mm), com furos de fixação
/// passantes + rebaixo (parafuso M3 escareado), espaçados a cada 20 mm.
/// Medidas conforme catálogo (ex.: DOLD Mechatronik, série MGN9R): WR=9, HR=6,5,
/// furo passante Ø3,5, rebaixo Ø6 x 3,5 de profundidade, passo 20, margem mínima 7,5.
/// </summary>
public sealed record TrilhoMgn9(double Comprimento = 200, double Largura = 9, double Altura = 6.5) : Part
{
    private const double Passo = 20;
    private const double MargemMinima = 7.5;
    private const double DiametroFuroPassante = 3.5;
    private const double DiametroRebaixo = 6;
    private const double ProfundidadeRebaixo = 3.5;

    private int NumFuros => Math.Max(2, (int)Math.Floor((Comprimento - 2 * MargemMinima) / Passo) + 1);

    /// <summary>
    /// Distância entre o primeiro e o último furo de fixação — é por eles (nas duas pontas)
    /// que o trilho é parafusado diretamente nos carrinhos do trilho maior.
    /// </summary>
    public double DistanciaEntreUltimosFuros => (NumFuros - 1) * Passo;

    // Geometria em coordenadas locais: a origem da peça é (0, 0, 0).
    // X = largura do trilho, Y = comprimento, Z = altura (de baixo para cima).
    protected override IFeature Build()
    {
        var corpo = new BoxFeature(Point3D.Origin, Largura, Comprimento, Altura) { Name = "corpo" };

        var numFuros = NumFuros;
        var vaoTotal = DistanciaEntreUltimosFuros;
        var margemReal = (Comprimento - vaoTotal) / 2;

        var furos = new List<IFeature>();
        for (var i = 0; i < numFuros; i++)
        {
            var y = margemReal + i * Passo;

            var passante = new CylinderFeature(new Point3D(Largura / 2, y, 0), Vector3D.UnitZ, DiametroFuroPassante / 2, Altura)
            {
                Name = $"furo_passante_{i}",
                IsTransientTool = true
            };
            var rebaixo = new CylinderFeature(new Point3D(Largura / 2, y, Altura - ProfundidadeRebaixo), Vector3D.UnitZ, DiametroRebaixo / 2, ProfundidadeRebaixo)
            {
                Name = $"rebaixo_{i}",
                IsTransientTool = true
            };

            furos.Add(passante);
            furos.Add(rebaixo);
        }

        return BooleanFeature.CreateDifference("trilho_mgn9", corpo, furos.ToArray());
    }
}
