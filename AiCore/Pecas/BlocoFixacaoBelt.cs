/// <summary>
/// Bloco de pressão da correia (belt), fixado na lateral do bloco_fixacao_mgn9.
/// Corpo: 20,5 (X) x 16,5 (Y) x 17 (Z) por metade; modelada como meia-peça e espelhada em X no Build().
/// Furo de pressão da correia usa o mesmo padrão de <see cref="FuroPorca"/> da <see cref="PlacaSuporteCarrinhoY"/>:
/// furo de passagem e alojamento hexagonal da porca M3 saem das mesmas variáveis (sempre coaxiais).
/// Origem local = base do bloco_fixacao_mgn9 (local = mundo - (0, 0, 26)).
/// </summary>
public sealed record BlocoFixacaoBelt(
    double LarguraCorpoX = 20.5,
    double ComprimentoCorpoY = 14.5,
    double AlturaCorpo = 17.0,
    double AlturaRebaixoInferior = 3.0,
    double ComprimentoRebaixoInferior = 10.0,
    double MargemRebaixoInferior = 2.0,
    double ProfundidadeRebaixoFrontal = 4.5,
    double AlturaRebaixoFrontal = 5.0,
    double LarguraRebaixoFrontal = 6.0,
    double BaseCorpoX = -20.615334,
    double BaseCorpoY = 10.5,
    double LarguraVaoX = 17.0,
    double ComprimentoVaoY = 6.0,
    double AlturaVao = 8.0,
    double BaseVaoX = -22.61533,
    double BaseVaoY = 15.75,
    double BaseVaoZ = 9.0,
    double RaioFuroVertical = 3.0,
    double FuroVerticalX = -5.61533,
    double FuroVerticalY = 18.75,
    double FuroVerticalZ = 9.0,
    double AlturaFuroVertical = 8.0,
    double RaioFuroVertical3_2 = 1.6,
    double RaioEscareadoFuroVertical = 3.0,
    double FuroLateralX = -10.0,
    double FuroLateralZ = 5.0,
    double RaioFuroRebaixo = 3.6,
    double ComprimentoFuroRebaixo = 12.0,
    double RaioFuroPassante = 1.7,
    double ComprimentoFuroPassante = 20.0,
    double FuroPressaoX = -16.11533,
    double FuroPressaoZ = 13.0,
    double PosicaoPorcaTraseiraY = 15.375,
    double EspessuraPorca = 3.0) : Part
{
    protected override IFeature Build()
    {
        var yFundo = BaseCorpoY + ComprimentoCorpoY;

        // Plano de espelhamento: face interna/direita do corpo (local X = BaseCorpoX + LarguraCorpoX).
        // A reflexão de qualquer ponto é X' = 2 * xm - X.
        var xm = BaseCorpoX + LarguraCorpoX;

        // Escareado de 90° (o raio decresce 1 mm por mm de altura) na PORÇÃO INFERIOR do furo,
        // com o vértice do cone apontando para cima: a base do cone fica na face inferior do corpo
        // (lado largo) e estreita subindo até o diâmetro do furo passante. Estende-se um pouco
        // abaixo da face para garantir um corte limpo.
        var profundidadeEscareado = RaioEscareadoFuroVertical - RaioFuroVertical3_2;
        const double margemEscareadoAbaixo = 1.0;
        var zBaseEscareado = 5;
        var alturaEscareado = profundidadeEscareado + margemEscareadoAbaixo;

        // ---- Furo de pressão da correia: furo + porca numa ÚNICA <FuroPorca> (padrão da
        //      PlacaSuporteCarrinhoY). O furo de passagem e o alojamento hexagonal saem das MESMAS
        //      variáveis, então ficam sempre coaxiais. O canal de inserção da porca abre no topo
        //      (EixoHexagono = +Z, que é o default do FuroPorca para furos horizontais);
        //      ComprimentoCanal = alturaTopoPorca extrapola 1 mm o topo do corpo, p/ corte limpo.
        var alturaTopoPorca = AlturaCorpo - FuroPressaoZ + 1.0;              // 5 mm
        var entradaPorcaY = yFundo + 1.0;                                   // 1 mm fora da face traseira
        var fundoPorcaY = PosicaoPorcaTraseiraY + EspessuraPorca / 2.0;     // face de entrada do alojamento
        var profundidadePorca = entradaPorcaY - fundoPorcaY;
        const double folgaAntesPorca = 10.0;                                // furo p/ fora da face traseira
        const double folgaDepoisPorca = 9.0;                                // furo p/ fora da face frontal

        // ==================== METADE ORIGINAL (como modelada) ====================

        var corpo = new BoxFeature(
            new Point3D(BaseCorpoX, BaseCorpoY, 0),
            LarguraCorpoX,
            ComprimentoCorpoY,
            AlturaCorpo)
        {
            Name = "corpo"
        };

        // Rebaixo na parte inferior traseira para evitar conflito com o bloco_conexao_motor_2020,
        // restrito à região de sobreposição (com margem de segurança).
        var rebaixo_inferior = new BoxFeature(new Point3D(BaseCorpoX, yFundo - ComprimentoRebaixoInferior - MargemRebaixoInferior, 0.0), LarguraCorpoX, ComprimentoRebaixoInferior + MargemRebaixoInferior, AlturaRebaixoInferior + MargemRebaixoInferior) { Name = "rebaixo_inferior", IsTransientTool = true };

        // Vazio da base frontal, restrito à PONTA: nasce na face de extremidade X = BaseCorpoX e
        // avança LarguraRebaixoFrontal para dentro do corpo, aberto na face Y = BaseCorpoY.
        var rebaixo_frontal = new BoxFeature(new Point3D(BaseCorpoX, BaseCorpoY, 0.0), LarguraRebaixoFrontal, ProfundidadeRebaixoFrontal, AlturaRebaixoFrontal) { Name = "rebaixo_frontal", IsTransientTool = true };

        var vao_correia = new BoxFeature(new Point3D(BaseVaoX, BaseVaoY, BaseVaoZ), LarguraVaoX, ComprimentoVaoY, AlturaVao) { Name = "vao_correia", IsTransientTool = true };
        // Rebaixo CILÍNDRICO no topo (alojamento da cabeça do parafuso M3 inserido por cima).
        var furo_vertical = new CylinderFeature(new Point3D(FuroVerticalX, FuroVerticalY, FuroVerticalZ), Vector3D.UnitZ, RaioFuroVertical, AlturaFuroVertical) { Name = "furo_vertical", IsTransientTool = true };
        // Furo vertical passante de 3,2 mm, atravessando toda a peça.
        var furo_vertical_3_2 = new CylinderFeature(new Point3D(FuroVerticalX, FuroVerticalY, -1.0), Vector3D.UnitZ, RaioFuroVertical3_2, AlturaCorpo + 2.0) { Name = "furo_vertical_3_2", IsTransientTool = true };
        // Rebaixo cônico (escareado) na PORÇÃO INFERIOR do furo, com o vértice para cima, para
        // alojar a cabeça do parafuso M3 inserido por baixo.
        var rebaixo_conico = new ConeFeature(
            globalBaseCenter: new Point3D(FuroVerticalX, FuroVerticalY, zBaseEscareado),
            globalDirection: Vector3D.UnitZ,
            bottomRadius: RaioEscareadoFuroVertical + margemEscareadoAbaixo,
            topRadius: RaioFuroVertical3_2,
            height: alturaEscareado) { Name = "rebaixo_conico", IsTransientTool = true };
        var rebaixo_fixacao = new CylinderFeature(new Point3D(FuroLateralX, yFundo, FuroLateralZ), new Vector3D(0, -1, 0), RaioFuroRebaixo, ComprimentoFuroRebaixo) { Name = "rebaixo_fixacao", IsTransientTool = true };
        var furo_fixacao = new CylinderFeature(new Point3D(FuroLateralX, yFundo, FuroLateralZ), new Vector3D(0, -1, 0), RaioFuroPassante, ComprimentoFuroPassante) { Name = "furo_fixacao", IsTransientTool = true };

        // Furo de pressão da correia + alojamento hexagonal da porca M3, construído por uma única
        // FuroPorca (subtrai furo e alojamento juntos). O furo entra pela face traseira (Y = yFundo)
        // e a porca é inserida pelo canal aberto no topo.
        var porca_pressao_correia = new FuroPorca(
            new Point3D(FuroPressaoX, entradaPorcaY, FuroPressaoZ),
            new Vector3D(0, -1, 0),
            profundidadePorca,
            ComprimentoCanal: alturaTopoPorca,
            FolgaAntes: folgaAntesPorca,
            FolgaDepois: folgaDepoisPorca).Instance("porca_pressao_correia");

        // ==================== METADE ESPELHADA EM X ====================
        // Cada sólido acima é refletido pelo plano X = xm (X' = 2*xm - X).

        var corpo_espelhado = new BoxFeature(
            new Point3D(xm, BaseCorpoY, 0),
            LarguraCorpoX,
            ComprimentoCorpoY,
            AlturaCorpo)
        {
            Name = "corpo_espelhado"
        };

        var rebaixo_inferior_espelhado = new BoxFeature(new Point3D(xm, yFundo - ComprimentoRebaixoInferior - MargemRebaixoInferior, 0.0), LarguraCorpoX, ComprimentoRebaixoInferior + MargemRebaixoInferior, AlturaRebaixoInferior + MargemRebaixoInferior) { Name = "rebaixo_inferior_espelhado", IsTransientTool = true };
        var rebaixo_frontal_espelhado = new BoxFeature(new Point3D(2.0 * xm - BaseCorpoX - LarguraRebaixoFrontal, BaseCorpoY, 0.0), LarguraRebaixoFrontal, ProfundidadeRebaixoFrontal, AlturaRebaixoFrontal) { Name = "rebaixo_frontal_espelhado", IsTransientTool = true };
        var vao_correia_espelhado = new BoxFeature(new Point3D(2.0 * xm - BaseVaoX - LarguraVaoX, BaseVaoY, BaseVaoZ), LarguraVaoX, ComprimentoVaoY, AlturaVao) { Name = "vao_correia_espelhado", IsTransientTool = true };
        var furo_vertical_espelhado = new CylinderFeature(new Point3D(2.0 * xm - FuroVerticalX, FuroVerticalY, FuroVerticalZ), Vector3D.UnitZ, RaioFuroVertical, AlturaFuroVertical) { Name = "furo_vertical_espelhado", IsTransientTool = true };
        var furo_vertical_3_2_espelhado = new CylinderFeature(new Point3D(2.0 * xm - FuroVerticalX, FuroVerticalY, -1.0), Vector3D.UnitZ, RaioFuroVertical3_2, AlturaCorpo + 2.0) { Name = "furo_vertical_3_2_espelhado", IsTransientTool = true };
        var rebaixo_conico_espelhado = new ConeFeature(
            globalBaseCenter: new Point3D(2.0 * xm - FuroVerticalX, FuroVerticalY, zBaseEscareado),
            globalDirection: Vector3D.UnitZ,
            bottomRadius: RaioEscareadoFuroVertical + margemEscareadoAbaixo,
            topRadius: RaioFuroVertical3_2,
            height: alturaEscareado) { Name = "rebaixo_conico_espelhado", IsTransientTool = true };
        var rebaixo_fixacao_espelhado = new CylinderFeature(new Point3D(2.0 * xm - FuroLateralX, yFundo, FuroLateralZ), new Vector3D(0, -1, 0), RaioFuroRebaixo, ComprimentoFuroRebaixo) { Name = "rebaixo_fixacao_espelhado", IsTransientTool = true };
        var furo_fixacao_espelhado = new CylinderFeature(new Point3D(2.0 * xm - FuroLateralX, yFundo, FuroLateralZ), new Vector3D(0, -1, 0), RaioFuroPassante, ComprimentoFuroPassante) { Name = "furo_fixacao_espelhado", IsTransientTool = true };

        // Mesmo conjunto furo + porca, reconstruído na posição espelhada em X.
        var porca_pressao_correia_espelhado = new FuroPorca(
            new Point3D(2.0 * xm - FuroPressaoX, entradaPorcaY, FuroPressaoZ),
            new Vector3D(0, -1, 0),
            profundidadePorca,
            ComprimentoCanal: alturaTopoPorca,
            FolgaAntes: folgaAntesPorca,
            FolgaDepois: folgaDepoisPorca).Instance("porca_pressao_correia_espelhado");

        // Une as duas metades e subtrai todos os vazios (originais + espelhados).
        var baseCorpos = new IFeature[] { corpo, corpo_espelhado };
        // Incorporado da cena (Cena.cs): coordenadas convertidas para o referencial local da peça.
        var prism_f62c_verts = new[]
        {
            new Point3D(26, 3.25, 0),
            new Point3D(26, 6.25, 0),
            new Point3D(30.3, 6.25, 0),
            new Point3D(30.3, 7.55, 0),
            new Point3D(41, 7.55, 0),
            new Point3D(41, 3.25, 0)
        };
        var prism_f62c_plane = new WorkPlane(new Point3D(20.38467, 25, 9), new Vector3D(0, 0, 1), new Vector3D(-1, 0, 0));
        var prism_f62c = new PrismFeature(prism_f62c_verts, height: 8, plane: prism_f62c_plane)
        {
            Name = "prism_f62c"
        };
        var cylinder_821a = new CylinderFeature(globalBaseCenter: new Point3D(-9.91533, 18.75, 9), globalDirection: new Vector3D(0, 0, 1), radius: 1.3, height: 8)
        {
            Name = "cylinder_821a"
        };
        var cylinder_54d2 = new CylinderFeature(globalBaseCenter: new Point3D(-5.61533, 18.75, 9), globalDirection: new Vector3D(0, 0, 1), radius: 3, height: 8)
        {
            Name = "cylinder_54d2"
        };
        var boolOp_7b90 = new BooleanFeature(name: "boolOp_7b90", operation: BooleanOperationType.Union, baseFeature: prism_f62c, toolFeatures: new IFeature[] { cylinder_821a });
        var boolOp_16db = new BooleanFeature(name: "boolOp_16db", operation: BooleanOperationType.Subtract, baseFeature: boolOp_7b90, toolFeatures: new IFeature[] { cylinder_54d2 });

        var bloco_fixacao_belt_base = BooleanFeature.CreateDifference(
            "bloco_fixacao_belt_base",
            baseCorpos,
            rebaixo_inferior, rebaixo_inferior_espelhado,
            rebaixo_frontal, rebaixo_frontal_espelhado,
            vao_correia, vao_correia_espelhado,
            furo_vertical, furo_vertical_espelhado,
            furo_vertical_3_2, furo_vertical_3_2_espelhado,
            rebaixo_conico, rebaixo_conico_espelhado,
            rebaixo_fixacao, rebaixo_fixacao_espelhado,
            furo_fixacao, furo_fixacao_espelhado,
            porca_pressao_correia, porca_pressao_correia_espelhado);
        return BooleanFeature.CreateUnion("bloco_fixacao_belt", bloco_fixacao_belt_base, boolOp_16db);
    }
}