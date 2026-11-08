/// <summary>
/// Trava impressa (ABS) que prende a face da frente do SuporteMotorNema17 de um motor do eixo Y de ponta-cabeça
/// ao <see cref="PedestalMotorY2020"/>. É uma barra com a espessura e o comprimento (em Y) do suporte, encostada na
/// face da frente dele e apoiada no topo do pedestal:
/// <para>
/// - 2 M3 em X (passo PassoFurosSuporte, no meio da espessura) atravessam a trava e rosqueiam nas porcas das
///   fixações laterais do suporte; cabeça num rebaixo na face +X. São apertados na bancada, antes de o conjunto ir
///   para a estrutura;
/// - 2 M3 verticais (passo PassoFurosPedestal) atravessam a trava de cima para baixo e rosqueiam nas porcas do
///   pedestal; a cabeça fica num rebaixo raso no topo, acessível de cima.
/// </para>
/// <para>
/// Mesma origem em X e Y do pedestal (centro da extrusão, Y no eixo do motor), com Z 0 na face de baixo da trava
/// (o topo do pedestal). Ocupa X de −10 + AvancoSuporte (a face da frente do suporte) a 10 (a face +X da extrusão),
/// Comprimento em Y e Espessura em Z.
/// </para>
/// <para>
/// O corpo é UM sólido só, de perfil em L extrudado no comprimento (Y): o braço baixo (X de −10 a −10 + AvancoSuporte,
/// altura Elevacao) ocupa a faixa em que o suporte encosta no pedestal, e é na face de cima desse braço que o suporte
/// apoia — é a trava que levanta o conjunto do motor em Elevacao, sem mexer no suporte nem no pedestal. O braço alto
/// (X de −10 + AvancoSuporte a 10, altura Espessura + Elevacao) encosta na face da frente do suporte, com o topo rente
/// ao dele, e leva os 2 M3 em X na meia-espessura do suporte (Elevacao + Espessura/2). Não há peça de apoio separada.
/// </para>
/// </summary>
public sealed record TravaSuporteMotorY(
    double AvancoSuporte = 5.35,
    double Espessura = 10,
    double Elevacao = 5,
    double Comprimento = 45,
    double PassoFurosSuporte = 15,
    double PassoFurosPedestal = 34,
    double MaterialSobCabecaSuporte = 6,
    double ProfundidadeRebaixoTopo = 3,
    double DiametroRebaixoCabeca = 6.5) : Part
{
    /// <summary>Altura total da trava: o braço alto, Espessura acima do braço baixo que levanta o suporte (Elevacao).</summary>
    public double Altura => Espessura + Elevacao;

    protected override IFeature Build()
    {
        var xFrente = -10 + AvancoSuporte;
        var largura = 10 - xFrente;

        // Corpo: UM sólido só, de perfil em L extrudado no comprimento (Y). Braço baixo de X −10 a xFrente (altura
        // Elevacao), onde o suporte apoia; braço alto de xFrente a 10 (altura Altura), que encosta na face da frente
        // do suporte com o topo rente ao dele.
        var perfil = new[]
        {
            new Point3D(-10, 0, 0),
            new Point3D(10, 0, 0),
            new Point3D(10, Altura, 0),
            new Point3D(xFrente, Altura, 0),
            new Point3D(xFrente, Elevacao, 0),
            new Point3D(-10, Elevacao, 0)
        };
        // Plano do perfil: eixo U = X e eixo V = +Z (normal −Y, X local +X); a extrusão de Comprimento vai para −Y.
        var plano_perfil = new WorkPlane(origin: new Point3D(0, Comprimento / 2, 0), normal: new Vector3D(0, -1, 0), xAxis: Vector3D.UnitX);
        var corpo = new PrismFeature(perfil, Comprimento, plano_perfil) { Name = "corpo" };

        // Chanfro antes dos furos: 1 mm nas arestas do corpo, bocas dos furos vivas.
        var corpo_chanfrado = new FilletFeature("corpo_chanfrado", corpo, 1.0) { Mode = FilletMode.Bevel, ApplyToAllEdges = true };

        var raioM3 = 3.4 / 2;
        var raioCabeca = DiametroRebaixoCabeca / 2;

        // M3 para o suporte, em X, na altura do meio da espessura dele (que está Elevacao acima da base); rebaixo da
        // cabeça na face +X.
        var ySuporte = PassoFurosSuporte / 2;
        var zSuporte = Elevacao + Espessura / 2;
        var profundidadeRebaixoSuporte = largura - MaterialSobCabecaSuporte;
        var furo_suporte_1 = new CylinderFeature(new Point3D(xFrente - 1, -ySuporte, zSuporte), Vector3D.UnitX, raioM3, largura + 2) { Name = "furo_suporte_1", IsTransientTool = true };
        var furo_suporte_2 = new CylinderFeature(new Point3D(xFrente - 1, ySuporte, zSuporte), Vector3D.UnitX, raioM3, largura + 2) { Name = "furo_suporte_2", IsTransientTool = true };
        var rebaixo_suporte_1 = new CylinderFeature(new Point3D(11, -ySuporte, zSuporte), new Vector3D(-1, 0, 0), raioCabeca, profundidadeRebaixoSuporte + 1) { Name = "rebaixo_suporte_1", IsTransientTool = true };
        var rebaixo_suporte_2 = new CylinderFeature(new Point3D(11, ySuporte, zSuporte), new Vector3D(-1, 0, 0), raioCabeca, profundidadeRebaixoSuporte + 1) { Name = "rebaixo_suporte_2", IsTransientTool = true };

        // M3 verticais para o pedestal, em X = 0: o centro do pedestal (e da extrusão), atravessando o braço alto de
        // cima abaixo; rebaixo raso da cabeça no topo (Altura).
        var xPedestal = 0.0;
        var yPedestal = PassoFurosPedestal / 2;
        var furo_pedestal_1 = new CylinderFeature(new Point3D(xPedestal, -yPedestal, -1), Vector3D.UnitZ, raioM3, Altura + 2) { Name = "furo_pedestal_1", IsTransientTool = true };
        var furo_pedestal_2 = new CylinderFeature(new Point3D(xPedestal, yPedestal, -1), Vector3D.UnitZ, raioM3, Altura + 2) { Name = "furo_pedestal_2", IsTransientTool = true };
        var rebaixo_pedestal_1 = new CylinderFeature(new Point3D(xPedestal, -yPedestal, Altura - ProfundidadeRebaixoTopo), Vector3D.UnitZ, raioCabeca, ProfundidadeRebaixoTopo + 1) { Name = "rebaixo_pedestal_1", IsTransientTool = true };
        var rebaixo_pedestal_2 = new CylinderFeature(new Point3D(xPedestal, yPedestal, Altura - ProfundidadeRebaixoTopo), Vector3D.UnitZ, raioCabeca, ProfundidadeRebaixoTopo + 1) { Name = "rebaixo_pedestal_2", IsTransientTool = true };

        return BooleanFeature.CreateDifference("trava_suporte_motor_y", corpo_chanfrado,
            furo_suporte_1, furo_suporte_2, rebaixo_suporte_1, rebaixo_suporte_2,
            furo_pedestal_1, furo_pedestal_2, rebaixo_pedestal_1, rebaixo_pedestal_2);
    }
}