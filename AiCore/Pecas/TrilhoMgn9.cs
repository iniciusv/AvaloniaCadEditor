/// <summary>
/// Trilho de guia linear miniatura MGN9 (perfil 9 x 6,5 mm), com furos de fixação
/// passantes + rebaixo (parafuso M3 escareado), espaçados a cada 20 mm.
/// Medidas conforme catálogo (ex.: DOLD Mechatronik, série MGN9R): WR=9, HR=6,5,
/// furo passante Ø3,5, rebaixo Ø6 x 3,5 de profundidade, passo 20, margem mínima 7,5.
/// </summary>
public sealed record TrilhoMgn9(double Comprimento = 200, double Largura = 9, double Altura = 6.5) : Part
{
    // Geometria em coordenadas locais: a origem da peça é (0, 0, 0).
    // X = largura do trilho, Y = comprimento, Z = altura (de baixo para cima).
    protected override IFeature Build()
    {
        var corpo = new BoxFeature(Point3D.Origin, Largura, Comprimento, Altura) { Name = "corpo" };

        const double passo = 20;
        const double margemMinima = 7.5;
        const double diametroFuroPassante = 3.5;
        const double diametroRebaixo = 6;
        const double profundidadeRebaixo = 3.5;

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

        return BooleanFeature.CreateDifference("trilho_mgn9", corpo, furos.ToArray());
    }
}
