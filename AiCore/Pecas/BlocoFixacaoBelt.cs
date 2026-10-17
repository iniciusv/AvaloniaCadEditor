/// <summary>
/// Bloco de fixação da correia (belt) do eixo X, preso na lateral do bloco_fixacao_mgn9.
/// A parte de cima é uma <see cref="GarraCorreia"/>: a correia entra pelas duas pontas, dá a volta
/// no parafuso vertical de cada lado e volta pelo mesmo canal estreito, espremida em duas camadas
/// — por isso não há mais parafuso de pressão. A garra ocupa o mesmo envelope do bloco (41 x 14,5 em
/// planta), então a peça não cresce em Y, para não encostar na correia que passa pelos rolamentos
/// dos espaçadores do motor.
/// Por baixo da garra fica só a faixa frontal (encostada no bloco_fixacao_mgn9): atrás dela o
/// rebaixo inferior deixa o bloco_conexao_motor_2020 passar, e nas pontas o rebaixo frontal.
/// Origem local = base do bloco_fixacao_mgn9; o plano de simetria é x = BaseCorpoX + LarguraCorpoX.
/// </summary>
public sealed record BlocoFixacaoBelt(
    double LarguraCorpoX = 20.5,
    double ComprimentoCorpoY = 14.5,
    double AlturaCorpo = 17.0,
    double AlturaRebaixoInferior = 3.0,
    double ComprimentoRebaixoInferior = 10.0,
    double MargemRebaixoInferior = 2.0,
    double ProfundidadeRebaixoFrontal = 4.5,
    double LarguraRebaixoFrontal = 6.0,
    double BaseCorpoX = -20.615334,
    double BaseCorpoY = 10.5,
    double AlturaCorreia = 8.0,
    double RaioFuroVertical3_2 = 1.6,
    double RaioEscareadoFuroVertical = 3.0,
    double FuroLateralX = -10.0,
    double RaioFuroRebaixo = 3.6,
    double ComprimentoFuroRebaixo = 12.0,
    double RaioFuroPassante = 1.7,
    double ComprimentoFuroPassante = 20.0) : Part
{
    /// <summary>Base da garra: o topo do rebaixo inferior, que é também a altura dos furos laterais.</summary>
    public double ZBaseGarra => AlturaRebaixoInferior + MargemRebaixoInferior;

    /// <summary>Garra do topo, com o mesmo envelope do bloco e o parafuso de M3 dos furos verticais.</summary>
    public GarraCorreia Garra => new GarraCorreia(
        Comprimento: 2 * LarguraCorpoX,
        Largura: ComprimentoCorpoY,
        Altura: AlturaCorpo - ZBaseGarra,
        AlturaCorreia: AlturaCorreia,
        RaioParafuso: RaioFuroVertical3_2);

    /// <summary>Eixo do parafuso da garra do lado −X (o do lado +X é o espelho dele).</summary>
    public double FuroVerticalX => BaseCorpoX + LarguraCorpoX - Garra.DistanciaParafuso;
    public double FuroVerticalY => BaseCorpoY + Garra.ParafusoY;

    protected override IFeature Build()
    {
        var yFundo = BaseCorpoY + ComprimentoCorpoY;
        var xm = BaseCorpoX + LarguraCorpoX;
        var zGarra = ZBaseGarra;

        var garra_correia = Garra.Instance("garra_correia", at: Placement.At(xm, BaseCorpoY, zGarra));

        // Faixa frontal sob a garra (até onde começa o rebaixo inferior), sem as pontas do rebaixo
        // frontal: um bloco só, simétrico em torno de xm e já recortado, sem booleana. Sobe 1 mm
        // para dentro da base maciça da garra (o canal só começa em y = YParede), para a união ter
        // volume em comum em vez de só encostar.
        var yFimFaixa = yFundo - ComprimentoRebaixoInferior - MargemRebaixoInferior;
        var xInicioFaixa = BaseCorpoX + LarguraRebaixoFrontal;
        var faixa_frontal = new BoxFeature(new Point3D(xInicioFaixa, BaseCorpoY, 0), 2 * (xm - xInicioFaixa), yFimFaixa - BaseCorpoY, zGarra + 1.0) { Name = "faixa_frontal" };
        var corpo = BooleanFeature.CreateUnion("corpo", garra_correia, faixa_frontal);

        // Escareado de 90° (o raio cai 1 mm por mm de altura) na boca de baixo do furo do parafuso da
        // garra, para a cabeça do M3 que entra por baixo. Começa 1 mm abaixo da base da garra, já no
        // vão do rebaixo inferior.
        const double margemEscareadoAbaixo = 1.0;
        var raioEscareadoNaBase = RaioEscareadoFuroVertical + margemEscareadoAbaixo;
        var rebaixo_conico = new ConeFeature(
            globalBaseCenter: new Point3D(FuroVerticalX, FuroVerticalY, zGarra - margemEscareadoAbaixo),
            globalDirection: Vector3D.UnitZ,
            bottomRadius: raioEscareadoNaBase + margemEscareadoAbaixo,
            topRadius: RaioFuroVertical3_2,
            height: raioEscareadoNaBase + margemEscareadoAbaixo - RaioFuroVertical3_2) { Name = "rebaixo_conico", IsTransientTool = true };
        var rebaixo_conico_espelhado = rebaixo_conico.Transformed(Placement.MirrorX(atX: xm), name: "rebaixo_conico_espelhado");

        // Parafusos de fixação no bloco_fixacao_mgn9: entram pela face de trás (1 mm fora dela) no
        // sentido −Y, com rebaixo para a cabeça, na altura da base da garra.
        var rebaixo_fixacao = new CylinderFeature(new Point3D(FuroLateralX, yFundo + 1.0, zGarra), new Vector3D(0, -1, 0), RaioFuroRebaixo, ComprimentoFuroRebaixo + 1.0) { Name = "rebaixo_fixacao", IsTransientTool = true };
        var furo_fixacao = new CylinderFeature(new Point3D(FuroLateralX, yFundo + 1.0, zGarra), new Vector3D(0, -1, 0), RaioFuroPassante, ComprimentoFuroPassante + 1.0) { Name = "furo_fixacao", IsTransientTool = true };
        var rebaixo_fixacao_espelhado = rebaixo_fixacao.Transformed(Placement.MirrorX(atX: xm), name: "rebaixo_fixacao_espelhado");
        var furo_fixacao_espelhado = furo_fixacao.Transformed(Placement.MirrorX(atX: xm), name: "furo_fixacao_espelhado");

        return BooleanFeature.CreateDifference("bloco_fixacao_belt", corpo,
            rebaixo_conico, rebaixo_conico_espelhado,
            rebaixo_fixacao, rebaixo_fixacao_espelhado,
            furo_fixacao, furo_fixacao_espelhado);
    }
}
