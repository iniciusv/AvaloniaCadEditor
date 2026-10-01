/// <summary>
/// Carrinho (bloco) curto MGN9C do trilho linear MGN9. Envelope de referência: 20 x 28,9 mm
/// em planta, 10 mm de altura total medida da base do trilho até o topo do carrinho.
/// 4 furos de fixação M3 (rosqueados, profundidade 3 mm) no topo, espaçados 15 mm
/// (transversal) x 10 mm (longitudinal). Medidas conforme catálogo (ex.: DOLD Mechatronik,
/// série MGN9C). Geometria simplificada (sem o rasgo em V que abraça o trilho) — serve
/// como referência de envelope e de posição dos furos para projetar peças que se encaixam nele.
/// </summary>
public sealed record CarrinhoMgn9C(double Largura = 20, double Comprimento = 28.9, double Altura = 10) : Part
{
    // Geometria em coordenadas locais: a origem da peça é (0, 0, 0), coincidindo com a
    // base do trilho (o carrinho "abraça" o trilho por baixo até a altura dele).
    protected override IFeature Build()
    {
        var corpo = new BoxFeature(Point3D.Origin, Largura, Comprimento, Altura) { Name = "corpo" };

        const double passoTransversal = 15; // entre furos, no sentido da largura
        const double passoLongitudinal = 10; // entre furos, no sentido do comprimento
        const double diametroFuro = 3; // M3
        const double profundidadeFuro = 3;

        var xCentro = Largura / 2;
        var yCentro = Comprimento / 2;

        var furos = new List<IFeature>();
        var i = 0;
        foreach (var sinalX in new[] { -1.0, 1.0 })
        {
            foreach (var sinalY in new[] { -1.0, 1.0 })
            {
                var x = xCentro + sinalX * passoTransversal / 2;
                var y = yCentro + sinalY * passoLongitudinal / 2;
                var furo = new CylinderFeature(new Point3D(x, y, Altura - profundidadeFuro), Vector3D.UnitZ, diametroFuro / 2, profundidadeFuro)
                {
                    Name = $"furo_fixacao_{i}",
                    IsTransientTool = true
                };
                furos.Add(furo);
                i++;
            }
        }

        return BooleanFeature.CreateDifference("carrinho_mgn9c", corpo, furos.ToArray());
    }
}
