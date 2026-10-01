/// <summary>
/// Trilho de guia linear miniatura MGN15 (perfil 15 x 10 mm), com furos de fixação
/// passantes + rebaixo (parafuso M3 escareado), espaçados a cada 40 mm.
/// Medidas conforme catálogo (ex.: DOLD Mechatronik, série MGN15R): WR=15, HR=10,
/// furo passante Ø3,5, rebaixo Ø6 x 4,5 de profundidade, passo 40, margem mínima 15.
/// </summary>
public sealed record TrilhoMgn15(double Comprimento = 200, double Largura = 15, double Altura = 10) : Part
{
    // Geometria em coordenadas locais: a origem da peça é (0, 0, 0).
    // X = largura do trilho, Y = comprimento, Z = altura (de baixo para cima).
    protected override IFeature Build()
    {
        var corpo = new BoxFeature(Point3D.Origin, Largura, Comprimento, Altura) { Name = "corpo" };

        const double passo = 40;
        const double margemMinima = 15;
        const double diametroFuroPassante = 3.5;
        const double diametroRebaixo = 6;
        const double profundidadeRebaixo = 4.5;

        var numFuros = Math.Max(2, (int)Math.Floor((Comprimento - 2 * margemMinima) / passo) + 1);
        var vaoTotal = (numFuros - 1) * passo;
        var margemReal = (Comprimento - vaoTotal) / 2;

        var furos = new List<IFeature>();
        for (var i = 0; i < numFuros; i++)
        {
            var y = margemReal + i * passo;

            var passante = new CylinderFeature(new Point3D(Largura / 2, y, 0), Vector3D.UnitZ, diametroFuroPassante / 2, Altura)
            {
                Name = $"furo_passante_{i}",
                IsTransientTool = true
            };
            var rebaixo = new CylinderFeature(new Point3D(Largura / 2, y, Altura - profundidadeRebaixo), Vector3D.UnitZ, diametroRebaixo / 2, profundidadeRebaixo)
            {
                Name = $"rebaixo_{i}",
                IsTransientTool = true
            };

            furos.Add(passante);
            furos.Add(rebaixo);
        }

        return BooleanFeature.CreateDifference("trilho_mgn15", corpo, furos.ToArray());
    }
}
