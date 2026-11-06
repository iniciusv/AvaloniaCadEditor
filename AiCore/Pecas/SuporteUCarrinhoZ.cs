/// <summary>
/// Peça impressa (ABS) que prende um carrinho HGH15 do eixo Z no tubo travessa. Em planta é um U: uma
/// parede de fundo com dois braços que deixam entre si um vão aberto para +X. Quatro furos passantes M5
/// (Ø5,8), paralelos a Z, ficam um em cada canto: dois atravessam a parede de fundo e dois atravessam os
/// braços, todos na linha do meio da espessura dos braços.
/// <para>
/// A face de trás da parede de fundo (X = −EspessuraFundo) é parafusada no topo do carrinho HGH15 por 4 M4
/// passantes em X, na malha PassoFurosM4 × PassoFurosM4 do carrinho: centrados em Y e com a primeira linha
/// AlturaPrimeiraLinhaM4 acima da base da peça. As cabeças ficam dentro do vão do U.
/// </para>
/// <para>
/// Quatro furos passantes paralelos a Y (Ø DiametroFuroY) atravessam os dois braços de uma vez: um na linha
/// onde os braços começam (X = 0) e uma coluna de três, PassoFurosY entre si, 4 mm antes da ponta dos
/// braços. Na face da parede de fundo voltada para o vão corre um rasgo trapezoidal ao longo de Y, só entre
/// os braços, e a quina de baixo da ponta dos braços tem um chanfro de TamanhoChanfro a 45°.
/// </para>
/// <para>
/// Origem local (0, 0, 0) no centro da peça em Y e em Z, na linha onde os braços começam (X = 0). A
/// parede de fundo vai de X = −EspessuraFundo a 0, os braços de 0 a ComprimentoBraco. Em Y a peça é
/// simétrica (±Largura / 2, vão de ±VaoBracos / 2) e em Z vai de −Altura / 2 a +Altura / 2.
/// </para>
/// </summary>
public sealed record SuporteUCarrinhoZ(
    double Largura = 63,
    double VaoBracos = 39,
    double EspessuraFundo = 20,
    double ComprimentoBraco = 20,
    double Altura = 39.4,
    double DistanciaFurosX = 20,
    double DiametroFuro = 5.8,
    double PassoFurosM4 = 26,
    double AlturaPrimeiraLinhaM4 = 6.7,
    double DiametroFuroM4 = 4.6,
    double DiametroFuroY = 4,
    double PassoFurosY = 12,
    double TamanhoChanfro = 5) : Part
{
    /// <summary>Espessura de cada braço (em Y).</summary>
    public double EspessuraBraco => (Largura - VaoBracos) / 2;

    protected override IFeature Build()
    {
        var yExterno = Largura / 2;
        var yInterno = VaoBracos / 2;
        var zBase = -Altura / 2;

        var perfil = new PolygonProfile(new List<Point3D>
        {
            new Point3D(0, -yInterno, zBase),
            new Point3D(ComprimentoBraco, -yInterno, zBase),
            new Point3D(ComprimentoBraco, -yExterno, zBase),
            new Point3D(-EspessuraFundo, -yExterno, zBase),
            new Point3D(-EspessuraFundo, yExterno, zBase),
            new Point3D(ComprimentoBraco, yExterno, zBase),
            new Point3D(ComprimentoBraco, yInterno, zBase),
            new Point3D(0, yInterno, zBase),
        });
        var corpo = new ExtrudeFeature(perfil, Altura, new Vector3D(0, 0, Altura), "corpo");

        // Chanfro a 45° na quina de baixo da ponta dos braços: um prisma triangular ao longo de Y inteiro,
        // 1 mm para fora da peça em todos os lados (no vão não há material nessa faixa de X).
        var xChanfro = ComprimentoBraco - TamanhoChanfro;
        var perfilChanfro = new PolygonProfile(new List<Point3D>
        {
            new Point3D(xChanfro - 1, -yExterno - 1, zBase - 1),
            new Point3D(ComprimentoBraco + 1, -yExterno - 1, zBase - 1),
            new Point3D(ComprimentoBraco + 1, -yExterno - 1, zBase + TamanhoChanfro + 1),
        });
        var chanfro_bracos = new ExtrudeFeature(perfilChanfro, Largura + 2, new Vector3D(0, Largura + 2, 0)) { Name = "chanfro_bracos", IsTransientTool = true };

        // Rasgo trapezoidal na face X = 0 da parede de fundo, só entre os braços: boca de Z −6,7 a 6,7 e
        // fundo 3 mm para dentro, de Z −3,7 a 2,7. As paredes seguem a mesma inclinação 1 mm para fora da face.
        var profundidadeRasgo = 3.0;
        var zBocaRasgoBaixo = -6.7;
        var zBocaRasgoCima = 6.7;
        var zFundoRasgoBaixo = -3.7;
        var zFundoRasgoCima = 2.7;
        var perfilRasgo = new PolygonProfile(new List<Point3D>
        {
            new Point3D(1, -yInterno, zBocaRasgoBaixo - (zFundoRasgoBaixo - zBocaRasgoBaixo) / profundidadeRasgo),
            new Point3D(-profundidadeRasgo, -yInterno, zFundoRasgoBaixo),
            new Point3D(-profundidadeRasgo, -yInterno, zFundoRasgoCima),
            new Point3D(1, -yInterno, zBocaRasgoCima + (zBocaRasgoCima - zFundoRasgoCima) / profundidadeRasgo),
        });
        var rasgo_fundo = new ExtrudeFeature(perfilRasgo, VaoBracos, new Vector3D(0, VaoBracos, 0)) { Name = "rasgo_fundo", IsTransientTool = true };

        // Furos no meio da espessura dos braços (em Y), a ±DistanciaFurosX / 2 da linha dos braços (em X).
        var yFuro = yInterno + EspessuraBraco / 2;
        var xFuroBraco = DistanciaFurosX / 2;
        var xFuroFundo = -DistanciaFurosX / 2;
        var raioFuro = DiametroFuro / 2;
        var zFuro = zBase - 1;
        var alturaFuro = Altura + 2;
        var furo_braco_1 = new CylinderFeature(new Point3D(xFuroBraco, -yFuro, zFuro), Vector3D.UnitZ, raioFuro, alturaFuro) { Name = "furo_braco_1", IsTransientTool = true };
        var furo_fundo_1 = new CylinderFeature(new Point3D(xFuroFundo, -yFuro, zFuro), Vector3D.UnitZ, raioFuro, alturaFuro) { Name = "furo_fundo_1", IsTransientTool = true };
        var furo_fundo_2 = new CylinderFeature(new Point3D(xFuroFundo, yFuro, zFuro), Vector3D.UnitZ, raioFuro, alturaFuro) { Name = "furo_fundo_2", IsTransientTool = true };
        var furo_braco_2 = new CylinderFeature(new Point3D(xFuroBraco, yFuro, zFuro), Vector3D.UnitZ, raioFuro, alturaFuro) { Name = "furo_braco_2", IsTransientTool = true };

        // M4 do carrinho: atravessam a parede de fundo em X, da face do carrinho até o vão do U.
        var yM4 = PassoFurosM4 / 2;
        var zM4Baixo = zBase + AlturaPrimeiraLinhaM4;
        var zM4Cima = zM4Baixo + PassoFurosM4;
        var xM4 = -EspessuraFundo - 1;
        var comprimentoM4 = EspessuraFundo + 2;
        var raioM4 = DiametroFuroM4 / 2;
        var furo_m4_1 = new CylinderFeature(new Point3D(xM4, -yM4, zM4Baixo), Vector3D.UnitX, raioM4, comprimentoM4) { Name = "furo_m4_1", IsTransientTool = true };
        var furo_m4_2 = new CylinderFeature(new Point3D(xM4, yM4, zM4Baixo), Vector3D.UnitX, raioM4, comprimentoM4) { Name = "furo_m4_2", IsTransientTool = true };
        var furo_m4_3 = new CylinderFeature(new Point3D(xM4, -yM4, zM4Cima), Vector3D.UnitX, raioM4, comprimentoM4) { Name = "furo_m4_3", IsTransientTool = true };
        var furo_m4_4 = new CylinderFeature(new Point3D(xM4, yM4, zM4Cima), Vector3D.UnitX, raioM4, comprimentoM4) { Name = "furo_m4_4", IsTransientTool = true };

        // Furos em Y, passantes pelos dois braços (no vão, o da linha X = 0 corre dentro do rasgo). Todos na
        // altura Z = −0,5 do meio do rasgo; os da ponta dos braços em coluna, PassoFurosY acima e abaixo.
        var zFurosYMeio = -0.5;
        var xFurosYPonta = ComprimentoBraco - 4;
        var yFurosY = -yExterno - 1;
        var comprimentoFurosY = Largura + 2;
        var raioFuroY = DiametroFuroY / 2;
        var furo_y_fundo = new CylinderFeature(new Point3D(0, yFurosY, zFurosYMeio), Vector3D.UnitY, raioFuroY, comprimentoFurosY) { Name = "furo_y_fundo", IsTransientTool = true };
        var furo_y_ponta_baixo = new CylinderFeature(new Point3D(xFurosYPonta, yFurosY, zFurosYMeio - PassoFurosY), Vector3D.UnitY, raioFuroY, comprimentoFurosY) { Name = "furo_y_ponta_baixo", IsTransientTool = true };
        var furo_y_ponta_meio = new CylinderFeature(new Point3D(xFurosYPonta, yFurosY, zFurosYMeio), Vector3D.UnitY, raioFuroY, comprimentoFurosY) { Name = "furo_y_ponta_meio", IsTransientTool = true };
        var furo_y_ponta_cima = new CylinderFeature(new Point3D(xFurosYPonta, yFurosY, zFurosYMeio + PassoFurosY), Vector3D.UnitY, raioFuroY, comprimentoFurosY) { Name = "furo_y_ponta_cima", IsTransientTool = true };

        return BooleanFeature.CreateDifference("suporte_u_carrinho_z", corpo, chanfro_bracos, rasgo_fundo,
            furo_braco_1, furo_fundo_1, furo_fundo_2, furo_braco_2,
            furo_m4_1, furo_m4_2, furo_m4_3, furo_m4_4,
            furo_y_fundo, furo_y_ponta_baixo, furo_y_ponta_meio, furo_y_ponta_cima);
    }
}
