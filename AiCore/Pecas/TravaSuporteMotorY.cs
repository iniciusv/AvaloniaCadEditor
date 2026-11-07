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
/// </summary>
public sealed record TravaSuporteMotorY(
    double AvancoSuporte = 5.35,
    double Espessura = 10,
    double Comprimento = 45,
    double PassoFurosSuporte = 15,
    double PassoFurosPedestal = 34,
    double MaterialSobCabecaSuporte = 6,
    double ProfundidadeRebaixoTopo = 3,
    double DiametroRebaixoCabeca = 6.5) : Part
{
    protected override IFeature Build()
    {
        var xFrente = -10 + AvancoSuporte;
        var largura = 10 - xFrente;
        var corpo = new BoxFeature(new Point3D(xFrente, -Comprimento / 2, 0), largura, Comprimento, Espessura) { Name = "corpo" };

        // Chanfro antes dos furos: só as 12 arestas da caixa, bocas dos furos vivas.
        var corpo_chanfrado = new FilletFeature("corpo_chanfrado", corpo, 1.0) { Mode = FilletMode.Bevel, ApplyToAllEdges = true };

        var raioM3 = 3.4 / 2;
        var raioCabeca = DiametroRebaixoCabeca / 2;

        // M3 para o suporte, em X, na altura do meio da espessura dele; rebaixo da cabeça na face +X.
        var ySuporte = PassoFurosSuporte / 2;
        var zSuporte = Espessura / 2;
        var profundidadeRebaixoSuporte = largura - MaterialSobCabecaSuporte;
        var furo_suporte_1 = new CylinderFeature(new Point3D(xFrente - 1, -ySuporte, zSuporte), Vector3D.UnitX, raioM3, largura + 2) { Name = "furo_suporte_1", IsTransientTool = true };
        var furo_suporte_2 = new CylinderFeature(new Point3D(xFrente - 1, ySuporte, zSuporte), Vector3D.UnitX, raioM3, largura + 2) { Name = "furo_suporte_2", IsTransientTool = true };
        var rebaixo_suporte_1 = new CylinderFeature(new Point3D(11, -ySuporte, zSuporte), new Vector3D(-1, 0, 0), raioCabeca, profundidadeRebaixoSuporte + 1) { Name = "rebaixo_suporte_1", IsTransientTool = true };
        var rebaixo_suporte_2 = new CylinderFeature(new Point3D(11, ySuporte, zSuporte), new Vector3D(-1, 0, 0), raioCabeca, profundidadeRebaixoSuporte + 1) { Name = "rebaixo_suporte_2", IsTransientTool = true };

        // M3 verticais para o pedestal, em X = 0: o centro do pedestal (e da extrusão); rebaixo raso da cabeça no topo.
        var xPedestal = 0.0;
        var yPedestal = PassoFurosPedestal / 2;
        var furo_pedestal_1 = new CylinderFeature(new Point3D(xPedestal, -yPedestal, -1), Vector3D.UnitZ, raioM3, Espessura + 2) { Name = "furo_pedestal_1", IsTransientTool = true };
        var furo_pedestal_2 = new CylinderFeature(new Point3D(xPedestal, yPedestal, -1), Vector3D.UnitZ, raioM3, Espessura + 2) { Name = "furo_pedestal_2", IsTransientTool = true };
        var rebaixo_pedestal_1 = new CylinderFeature(new Point3D(xPedestal, -yPedestal, Espessura - ProfundidadeRebaixoTopo), Vector3D.UnitZ, raioCabeca, ProfundidadeRebaixoTopo + 1) { Name = "rebaixo_pedestal_1", IsTransientTool = true };
        var rebaixo_pedestal_2 = new CylinderFeature(new Point3D(xPedestal, yPedestal, Espessura - ProfundidadeRebaixoTopo), Vector3D.UnitZ, raioCabeca, ProfundidadeRebaixoTopo + 1) { Name = "rebaixo_pedestal_2", IsTransientTool = true };

        return BooleanFeature.CreateDifference("trava_suporte_motor_y", corpo_chanfrado,
            furo_suporte_1, furo_suporte_2, rebaixo_suporte_1, rebaixo_suporte_2,
            furo_pedestal_1, furo_pedestal_2, rebaixo_pedestal_1, rebaixo_pedestal_2);
    }
}
