/// <summary>
/// Garfo impresso (ABS) que prende uma ponta do tubo travessa num carrinho HGH15 do eixo Z. A placa é
/// parafusada no topo do carrinho pelos 4 furos M4 dele (M4 × 10, cabeça em rebaixo do lado do tubo) e
/// tem uma parede na frente da ponta do tubo. Duas bochechas abraçam as faces ±Y do tubo e um fundo passa
/// por baixo dele, fechando o U. O tubo é preso por 2 M5 passantes em Y (bochecha, tubo, bochecha, com
/// porca); os furos do tubo são feitos na montagem, usando as bochechas como gabarito.
/// <para>
/// O carrinho fica atrás (−Y) e abaixo do tubo: a carga vai em diagonal da placa até as bochechas. O furo
/// M4 de cima e de trás cai na linha da bochecha de trás, então ela tem um canal de acesso em X na face
/// externa, por onde passam o parafuso e a chave. Os M5 ficam acima desse canal e fora da faixa em X das
/// cabeças dos M5 do espaçador do tubo (17,6 mm da ponta, por dentro do tubo).
/// </para>
/// Sem chanfros: o lado B da cena é esta peça espelhada, e um fillet não pode ser espelhado.
/// Origem local (0, 0, 0) no canto do carrinho: face de fixação (topo do carrinho), ponta −Y e ponta de
/// baixo. X sai do carrinho para o tubo, Y e Z são os do mundo no lado A. A ponta do tubo fica em
/// X = VaoTubo, a face −Y dele em Y = DeslocamentoTuboY e a face de baixo em Z = AlturaBaseTubo.
/// </summary>
public sealed record FixacaoCarrinhoZTubo(
    double VaoTubo = 12,
    double DeslocamentoTuboY = 12,
    double AlturaBaseTubo = 31.4,
    double LarguraTubo = 50,
    double AlturaTubo = 30,
    double LarguraCarrinho = 34,
    double ComprimentoCarrinho = 61.4,
    double PassoFurosCarrinho = 26,
    double FolgaPontaTubo = 0.5,
    double FolgaTubo = 0.3,
    double EspessuraBochecha = 8,
    double EspessuraFundo = 6,
    double AvancoBochecha = 36,
    double DistanciaParafusoTubo1 = 8,
    double DistanciaParafusoTubo2 = 28,
    double AlturaParafusosTubo = 22.5,
    double MaterialSobCabecaM4 = 5.5,
    double DiametroFuroM5 = 5.5) : Part
{
    /// <summary>Espessura da placa no carrinho: o vão até a ponta do tubo menos a folga.</summary>
    public double EspessuraPlaca => VaoTubo - FolgaPontaTubo;

    protected override IFeature Build()
    {
        const double diametroRebaixoM4 = 7.5;

        // Faixa do tubo com folga, e o envelope do U em volta dela.
        var yInterno0 = DeslocamentoTuboY - FolgaTubo;
        var yInterno1 = DeslocamentoTuboY + LarguraTubo + FolgaTubo;
        var y0 = yInterno0 - EspessuraBochecha;
        var y1 = yInterno1 + EspessuraBochecha;
        var zFundoInterno = AlturaBaseTubo - FolgaTubo;
        var zBase = zFundoInterno - EspessuraFundo;
        var zTopo = AlturaBaseTubo + AlturaTubo;
        var xFim = VaoTubo + AvancoBochecha;

        var placa_carrinho = new BoxFeature(Point3D.Origin, EspessuraPlaca, LarguraCarrinho, ComprimentoCarrinho) { Name = "placa_carrinho" };
        var parede_ponta = new BoxFeature(new Point3D(0, y0, zBase), EspessuraPlaca, y1 - y0, zTopo - zBase) { Name = "parede_ponta" };
        var fundo = new BoxFeature(new Point3D(0, y0, zBase), xFim, y1 - y0, EspessuraFundo) { Name = "fundo" };
        var bochecha_tras = new BoxFeature(new Point3D(0, y0, zBase), xFim, EspessuraBochecha, zTopo - zBase) { Name = "bochecha_tras" };
        var bochecha_frente = new BoxFeature(new Point3D(0, yInterno1, zBase), xFim, EspessuraBochecha, zTopo - zBase) { Name = "bochecha_frente" };

        // Uma ferramenta por booleana, sempre sobre um sólido que ela já invade.
        var corpo_placa = BooleanFeature.CreateUnion("corpo_placa", placa_carrinho, parede_ponta);
        var corpo_fundo = BooleanFeature.CreateUnion("corpo_fundo", corpo_placa, fundo);
        var corpo_tras = BooleanFeature.CreateUnion("corpo_tras", corpo_fundo, bochecha_tras);
        var corpo = BooleanFeature.CreateUnion("corpo", corpo_tras, bochecha_frente);

        // Furos M4 do carrinho (malha PassoFurosCarrinho), entrando pela face do lado do tubo.
        var yFuro1 = LarguraCarrinho / 2 - PassoFurosCarrinho / 2;
        var yFuro2 = LarguraCarrinho / 2 + PassoFurosCarrinho / 2;
        var zFuro1 = ComprimentoCarrinho / 2 - PassoFurosCarrinho / 2;
        var zFuro2 = ComprimentoCarrinho / 2 + PassoFurosCarrinho / 2;
        var direcaoM4 = new Vector3D(-1, 0, 0);
        var rebaixoM4 = EspessuraPlaca - MaterialSobCabecaM4;
        var furo_m4_1 = new FuroRebaixado(new Point3D(EspessuraPlaca, yFuro1, zFuro1), direcaoM4, EspessuraPlaca, DiametroRebaixo: diametroRebaixoM4, ProfundidadeRebaixo: rebaixoM4).Instance("furo_m4_1");
        var furo_m4_2 = new FuroRebaixado(new Point3D(EspessuraPlaca, yFuro2, zFuro1), direcaoM4, EspessuraPlaca, DiametroRebaixo: diametroRebaixoM4, ProfundidadeRebaixo: rebaixoM4).Instance("furo_m4_2");
        var furo_m4_3 = new FuroRebaixado(new Point3D(EspessuraPlaca, yFuro1, zFuro2), direcaoM4, EspessuraPlaca, DiametroRebaixo: diametroRebaixoM4, ProfundidadeRebaixo: rebaixoM4).Instance("furo_m4_3");
        var furo_m4_4 = new FuroRebaixado(new Point3D(EspessuraPlaca, yFuro2, zFuro2), direcaoM4, EspessuraPlaca, DiametroRebaixo: diametroRebaixoM4, ProfundidadeRebaixo: rebaixoM4).Instance("furo_m4_4");

        // Canal de acesso ao furo_m4_3 (cima, trás), pela bochecha de trás até a ponta dela.
        var raioAcesso = diametroRebaixoM4 / 2 + 0.25;
        var acesso_m4 = new CylinderFeature(new Point3D(EspessuraPlaca - 1, yFuro1, zFuro2), Vector3D.UnitX, raioAcesso, xFim - EspessuraPlaca + 2) { Name = "acesso_m4", IsTransientTool = true };

        // M5 passantes em Y pelas duas bochechas (o tubo é furado na montagem por eles).
        var zM5 = AlturaBaseTubo + AlturaParafusosTubo;
        var furo_m5_1 = new CylinderFeature(new Point3D(VaoTubo + DistanciaParafusoTubo1, y0 - 1, zM5), Vector3D.UnitY, DiametroFuroM5 / 2, y1 - y0 + 2) { Name = "furo_m5_1", IsTransientTool = true };
        var furo_m5_2 = new CylinderFeature(new Point3D(VaoTubo + DistanciaParafusoTubo2, y0 - 1, zM5), Vector3D.UnitY, DiametroFuroM5 / 2, y1 - y0 + 2) { Name = "furo_m5_2", IsTransientTool = true };

        return BooleanFeature.CreateDifference("fixacao_carrinho_z_tubo", corpo,
            furo_m4_1, furo_m4_2, furo_m4_3, furo_m4_4, acesso_m4, furo_m5_1, furo_m5_2);
    }
}
