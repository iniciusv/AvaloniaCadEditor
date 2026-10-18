using System.Collections.Generic;

/// <summary>
/// Cantoneira impressa que amarra a <see cref="PlacaSuporteCarrinhoY"/> (topo do carrinho
/// MGN15H) ao perfil de alumínio 2020 transversal (<see cref="Extrusao2020"/>, instância
/// "extrusao_2020_sob_trilho_x" na cena). Formato em L com dois reforços de canto horizontais e
/// chanfro (Bevel) configurável em todas as arestas externas, como no
/// <see cref="BlocoConexaoMotor2020"/>.
///
/// Fixação:
/// 1. Aba contra a face interna da placa (plano local X = 0): 2 furos passantes Ø3,4 no eixo X;
///    o parafuso M3 entra por esta peça e rosqueia nas porcas M3 cativas da placa
///    (<c>furo_x_1</c>/<c>alojamento_porca_x_1</c> e <c>furo_x_2</c>/<c>alojamento_porca_x_2</c>
///    de <see cref="PlacaSuporteCarrinhoY"/>). As porcas ficam de 3 a 6 mm dentro da placa, logo
///    com <c>EspessuraAbaPlaca</c> = 6 o parafuso é um M3x12.
/// 2. Aba contra a face frontal livre do perfil 2020 (plano local Y = 0): 2 furos passantes Ø3,4
///    no eixo Y, parafusados pela frente (de -Y para +Y), centralizados na altura do perfil
///    (Z = 0 local) — é a altura do rasgo T, onde entra a porca em T.
///
/// Origem local (0, 0, 0) na interseção dos três planos de referência da montagem: face da placa
/// (X = 0, material em X >= 0), face frontal do perfil (Y = 0, material em Y &lt;= 0) e meia-altura
/// do perfil (Z = 0, eixo dos furos do perfil). Os eixos locais são paralelos aos da cena — a peça
/// é posicionada apenas por translação.
///
/// <c>AlturaFurosPlacaZ</c>, <c>DistanciaFuroPlaca1Y</c> e <c>DistanciaFuroPlaca2Y</c> são
/// derivados dos furos <c>furo_x_1</c>/<c>furo_x_2</c> de <see cref="PlacaSuporteCarrinhoY"/>
/// (Z = 22,625 e Y = -31,5 / -16,5 na cena, menos a origem desta peça em Z = 6 e Y = -10);
/// se aqueles furos mudarem, estes parâmetros acompanham.
///
/// Nenhum furo desta peça tem alojamento de porca: todos são passantes simples, as porcas ficam
/// na placa (porca M3 cativa) e no rasgo do perfil (porca em T).
///
/// Com <c>EspelhadoNoEixoX = true</c>, toda a geometria é refletida em relação ao plano X = 0,
/// gerando a cantoneira do lado B (que apoia a <c>placa_suporte_carrinho_y_b</c>): o material
/// passa a crescer em -X a partir da face da placa. Y e Z não mudam — os furos com porca ficam
/// no mesmo Y nas duas placas e a aba continua na face -Y do mesmo perfil 2020.
/// </summary>
public sealed record CantoneiraPlacaExtrusao2020(
    double EspessuraAbaPlaca = 6,
    double EspessuraAbaExtrusao = 5,
    double LarguraAbaPlacaY = 26.5,
    double RecuoFundoAbaPlacaY = 2,
    double AlturaAbaPlaca = 29,
    double AlturaAbaExtrusao = 16,
    double PassoFurosExtrusao = 20,
    double DistanciaPrimeiroFuroX = 15,
    double MargemFinalAbaExtrusaoX = 5,
    double AlturaFurosPlacaZ = 16.625,
    double DistanciaFuroPlaca1Y = 21.5,
    double DistanciaFuroPlaca2Y = 6.5,
    double DiametroFuroM3 = 3.4,
    double EspessuraReforco = 3,
    double AvancoReforcoX = 20,
    double AvancoReforcoY = 20,
    double RaioChanfro = 1.0,
    bool EspelhadoNoEixoX = false) : Part
{
    protected override IFeature Build()
    {
        // Fator de espelhamento em X: +1 (lado A, material em X >= 0) ou -1 (lado B, imagem
        // especular em relação ao plano X = 0, material em X <= 0). Y e Z não mudam: os furos da
        // placa continuam no mesmo Y nos dois lados e o perfil 2020 é o mesmo.
        var sx = EspelhadoNoEixoX ? -1.0 : 1.0;

        var raioFuro = DiametroFuroM3 / 2;
        var zBase = -AlturaAbaExtrusao / 2;   // base comum das duas abas e do reforço inferior
        var zTopoAbaExtrusao = AlturaAbaExtrusao / 2;

        // Função local: só CALCULA o canto em X de uma caixa que vai de X = 0 até X = sx * extensao
        // (com sx = -1 o canto base passa a ser o lado negativo).
        double CantoBaseX(double extensao) => sx > 0 ? 0 : -extensao;

        // Aba contra a placa: parede em X, comprida em Y (acompanha a face interna da placa) e alta
        // em Z (sobe do perfil até o topo da placa, onde ficam os furos com porca).
        // A ponta funda (lado do furo_placa_2) recua RecuoFundoAbaPlacaY: o carrinho MGN9C que corre
        // no trilho_mgn9_x tem 20 mm de largura e varre Y de -10 a +10 na cena (Z de 16 a 26) ao
        // longo de todo o curso em X — a aba pararia rente nele, então recua para abrir folga.
        var comprimentoAbaPlacaY = LarguraAbaPlacaY - RecuoFundoAbaPlacaY;
        var aba_placa = new BoxFeature(new Point3D(CantoBaseX(EspessuraAbaPlaca), -LarguraAbaPlacaY, zBase), EspessuraAbaPlaca, comprimentoAbaPlacaY, AlturaAbaPlaca) { Name = "aba_placa" };

        // Aba contra o perfil 2020: parede fina em Y, comprida em X (o suficiente para os 2 furos
        // mais as margens). O começo da aba fica em voo: a face da placa é a origem (X = 0) e a
        // ponta do perfil está 1,5 mm adiante no lado A (X = -105 contra -106,5 na cena) e 3,5 mm
        // no lado B (X = 103 contra 106,5), porque o perfil não é centrado entre as duas placas.
        // A aba começa sempre na face da placa (X = 0) e termina MargemFinalAbaExtrusaoX depois do
        // último furo: assim, deslocar DistanciaPrimeiroFuroX arrasta os dois furos e o fim da aba
        // juntos, sem engordar a margem do lado da placa.
        var comprimentoAbaExtrusaoX = DistanciaPrimeiroFuroX + PassoFurosExtrusao + MargemFinalAbaExtrusaoX;
        var aba_extrusao = new BoxFeature(new Point3D(CantoBaseX(comprimentoAbaExtrusaoX), -EspessuraAbaExtrusao, zBase), comprimentoAbaExtrusaoX, EspessuraAbaExtrusao, AlturaAbaExtrusao) { Name = "aba_extrusao" };

        // Reforços de canto: a aresta do L é vertical, então o gusset tem de ficar em plano
        // horizontal. São dois (um no topo e um na base da aba do perfil) em vez de um só no meio
        // para deixar livre a faixa central em Z — é por ela que a chave alcança, pela frente, os
        // parafusos que prendem a peça ao perfil.
        // Função local: só CALCULA os pontos do triângulo (o sólido é declarado abaixo).
        List<Point3D> PerfilReforco(double z) => new List<Point3D>
        {
            new Point3D(EspessuraAbaPlaca * sx, -EspessuraAbaExtrusao, z),
            new Point3D(EspessuraAbaPlaca * sx, -EspessuraAbaExtrusao - AvancoReforcoY, z),
            new Point3D((EspessuraAbaPlaca + AvancoReforcoX) * sx, -EspessuraAbaExtrusao, z)
        };

        var perfil_reforco_inferior = new PolygonProfile(PerfilReforco(zBase));
        var reforco_canto_inferior = new ExtrudeFeature(perfil_reforco_inferior, EspessuraReforco, new Vector3D(0, 0, EspessuraReforco), "reforco_canto_inferior");

        var perfil_reforco_superior = new PolygonProfile(PerfilReforco(zTopoAbaExtrusao - EspessuraReforco));
        var reforco_canto_superior = new ExtrudeFeature(perfil_reforco_superior, EspessuraReforco, new Vector3D(0, 0, EspessuraReforco), "reforco_canto_superior");

        var corpo_bruto = BooleanFeature.CreateUnion("corpo_bruto", aba_placa, aba_extrusao, reforco_canto_inferior, reforco_canto_superior);

        // Chanfro em todas as arestas externas, cortado ANTES dos furos para não chanfrar a boca
        // deles. As arestas côncavas (encontro das abas com os reforços) são puladas pelo kernel.
        IFeature corpo = corpo_bruto;
        if (RaioChanfro > 0.001)
        {
            var corpo_chanfrado = new FilletFeature("corpo_chanfrado", corpo_bruto, RaioChanfro)
            {
                Mode = FilletMode.Bevel,
                ApplyToAllEdges = true
            };
            corpo = corpo_chanfrado;
        }

        // Furos para as porcas M3 cativas da placa: entram 1 mm fora da face (X = -sx) e atravessam
        // a aba no sentido da placa, continuando para dentro dela.
        var entradaFurosPlacaX = -1 * sx;
        var direcaoFurosPlaca = new Vector3D(sx, 0, 0);
        var furo_placa_1 = new CylinderFeature(new Point3D(entradaFurosPlacaX, -DistanciaFuroPlaca1Y, AlturaFurosPlacaZ), direcaoFurosPlaca, raioFuro, EspessuraAbaPlaca + 2) { Name = "furo_placa_1", IsTransientTool = true };
        var furo_placa_2 = new CylinderFeature(new Point3D(entradaFurosPlacaX, -DistanciaFuroPlaca2Y, AlturaFurosPlacaZ), direcaoFurosPlaca, raioFuro, EspessuraAbaPlaca + 2) { Name = "furo_placa_2", IsTransientTool = true };

        // Furos para o perfil 2020: entram pela frente (Y negativo) e atravessam a aba no sentido
        // +Y, na meia-altura do perfil (Z = 0), onde corre o rasgo T.
        var xFuroExtrusao1 = DistanciaPrimeiroFuroX * sx;
        var xFuroExtrusao2 = (DistanciaPrimeiroFuroX + PassoFurosExtrusao) * sx;
        var furo_extrusao_1 = new CylinderFeature(new Point3D(xFuroExtrusao1, -EspessuraAbaExtrusao - 1, 0), Vector3D.UnitY, raioFuro, EspessuraAbaExtrusao + 2) { Name = "furo_extrusao_1", IsTransientTool = true };
        var furo_extrusao_2 = new CylinderFeature(new Point3D(xFuroExtrusao2, -EspessuraAbaExtrusao - 1, 0), Vector3D.UnitY, raioFuro, EspessuraAbaExtrusao + 2) { Name = "furo_extrusao_2", IsTransientTool = true };

        return BooleanFeature.CreateDifference("cantoneira_placa_extrusao_2020", corpo,
            furo_placa_1, furo_placa_2,
            furo_extrusao_1, furo_extrusao_2);
    }
}
