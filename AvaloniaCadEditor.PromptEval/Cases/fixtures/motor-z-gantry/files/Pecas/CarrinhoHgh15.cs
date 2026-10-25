/// <summary>
/// Carrinho (bloco) quadrado do trilho linear HGR15. Padrão: HGH15CA — 34 x 61,4 mm em planta,
/// 28 mm de altura total medida da base do trilho até o topo do carrinho, com a base do carrinho
/// 4,3 mm acima da base do trilho. 4 furos de fixação M4 (rosqueados, profundidade 5) no topo,
/// em malha 26 x 26 mm. Medidas conforme catálogo HIWIN (série HG: H=28, H1=4,3, N=9,5, W=34,
/// B=26, C=26, L=61,4, M4x5). Geometria simplificada: bloco único com o canal retangular que
/// abraça o trilho — serve de envelope e de posição dos furos para projetar as peças de fixação.
/// </summary>
public sealed record CarrinhoHgh15(
    double Largura = 34,
    double Comprimento = 61.4,
    double Altura = 28,
    double AlturaBase = 4.3,
    double LarguraTrilho = 15,
    double AlturaTrilho = 15) : Part
{
    /// <summary>Distância da lateral do carrinho à lateral do trilho (N do catálogo).</summary>
    public double RecuoTrilho => (Largura - LarguraTrilho) / 2;

    // Geometria em coordenadas locais: a origem da peça é (0, 0, 0), coincidindo com a base do
    // trilho no canto do carrinho. X = largura, Y = comprimento (sentido do trilho), Z = altura.
    // O trilho ocupa x de RecuoTrilho a RecuoTrilho + LarguraTrilho.
    protected override IFeature Build()
    {
        const double passoTransversal = 26; // B, entre furos no sentido da largura
        const double passoLongitudinal = 26; // C, entre furos no sentido do comprimento
        const double diametroFuro = 4; // M4
        const double profundidadeFuro = 5;
        const double folgaCanal = 0.5; // por lado e acima do trilho, para as faces não coincidirem

        var x1 = Largura / 2 - passoTransversal / 2;
        var x2 = Largura / 2 + passoTransversal / 2;
        var y1 = Comprimento / 2 - passoLongitudinal / 2;
        var y2 = Comprimento / 2 + passoLongitudinal / 2;
        var zFuro = Altura - profundidadeFuro;
        var alturaFuro = profundidadeFuro + 1;

        var corpo = new BoxFeature(new Point3D(0, 0, AlturaBase), Largura, Comprimento, Altura - AlturaBase) { Name = "corpo" };
        var canal_trilho = new BoxFeature(new Point3D(RecuoTrilho - folgaCanal, -1, AlturaBase - 1), LarguraTrilho + 2 * folgaCanal, Comprimento + 2, AlturaTrilho + folgaCanal - AlturaBase + 1) { Name = "canal_trilho", IsTransientTool = true };

        var furo_1 = new CylinderFeature(new Point3D(x1, y1, zFuro), Vector3D.UnitZ, diametroFuro / 2, alturaFuro) { Name = "furo_1", IsTransientTool = true };
        var furo_2 = new CylinderFeature(new Point3D(x2, y1, zFuro), Vector3D.UnitZ, diametroFuro / 2, alturaFuro) { Name = "furo_2", IsTransientTool = true };
        var furo_3 = new CylinderFeature(new Point3D(x1, y2, zFuro), Vector3D.UnitZ, diametroFuro / 2, alturaFuro) { Name = "furo_3", IsTransientTool = true };
        var furo_4 = new CylinderFeature(new Point3D(x2, y2, zFuro), Vector3D.UnitZ, diametroFuro / 2, alturaFuro) { Name = "furo_4", IsTransientTool = true };

        return BooleanFeature.CreateDifference("carrinho_hgh15", corpo, canal_trilho, furo_1, furo_2, furo_3, furo_4);
    }
}
