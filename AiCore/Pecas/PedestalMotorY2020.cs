/// <summary>
/// Pedestal impresso (ABS) que assenta no topo da extrusão 2020 sob o trilho Y, no fim dela onde o trilho já
/// acabou, e sustenta o SuporteMotorNema17 de um motor do eixo Y de ponta-cabeça. Metade de um par: a
/// <see cref="TravaSuporteMotorY"/> prende a face da frente do suporte e é aparafusada em cima deste pedestal.
/// <para>
/// Toda a fixação na estrutura é em Z, de cima: 2 M3 passantes no canal de cima da extrusão (porca T), com a
/// cabeça num rebaixo raso no topo. O rebaixo sai do comprimento do parafuso: ComprimentoParafusoExtrusao menos
/// EngateTNut (o quanto ele passa do topo da extrusão para atravessar a porca T). Montagem: primeiro o pedestal na extrusão; depois o
/// conjunto motor + suporte + trava (aparafusados na bancada) apoia no topo e a trava é presa por 2 M3 verticais,
/// que rosqueiam em porcas alojadas aqui, inseridas pela face +X.
/// </para>
/// <para>
/// Origem local (0, 0, 0) no centro do topo da extrusão, com Y no eixo do motor; o motor fica do lado −X. O bloco
/// ocupa a largura da extrusão (X −10…10), Comprimento em Y e vai de Z 0 a AlturaApoio, onde o suporte apoia a faixa
/// que avança sobre a extrusão (X −10 a −10 + AvancoSuporte) e a trava apoia o resto.
/// </para>
/// </summary>
public sealed record PedestalMotorY2020(
    double AlturaApoio = 27,
    double AvancoSuporte = 5.35,
    double Comprimento = 45,
    double PassoFurosExtrusao = 12,
    double PassoFurosTrava = 34,
    double ComprimentoParafusoExtrusao = 25,
    double EngateTNut = 4,
    double DiametroRebaixoCabeca = 6.5,
    double ProfundidadePorcaTrava = 6) : Part
{
    /// <summary>X do eixo dos M3 da trava: o centro do pedestal (e da extrusão), alinhado aos M3 da extrusão.</summary>
    public double XFurosTrava => 0;

    protected override IFeature Build()
    {
        var corpo = new BoxFeature(new Point3D(-10, -Comprimento / 2, 0), 20, Comprimento, AlturaApoio) { Name = "corpo" };

        // Chanfro antes dos furos: só as 12 arestas da caixa, bocas dos furos vivas.
        var corpo_chanfrado = new FilletFeature("corpo_chanfrado", corpo, 1.0) { Mode = FilletMode.Bevel, ApplyToAllEdges = true };

        // M3 no canal de cima da extrusão (eixo em X = 0). A cabeça assenta onde o parafuso, passando EngateTNut do
        // topo da extrusão, cabe inteiro: o rebaixo é só o que sobra da altura do pedestal.
        var yExtrusao = PassoFurosExtrusao / 2;
        var raioM3 = 3.4 / 2;
        var raioCabecaM3 = DiametroRebaixoCabeca / 2;
        var zAssentoCabeca = ComprimentoParafusoExtrusao - EngateTNut;
        var alturaRebaixo = AlturaApoio - zAssentoCabeca + 1;
        var furo_extrusao_1 = new CylinderFeature(new Point3D(0, -yExtrusao, -1), Vector3D.UnitZ, raioM3, AlturaApoio + 2) { Name = "furo_extrusao_1", IsTransientTool = true };
        var furo_extrusao_2 = new CylinderFeature(new Point3D(0, yExtrusao, -1), Vector3D.UnitZ, raioM3, AlturaApoio + 2) { Name = "furo_extrusao_2", IsTransientTool = true };
        var rebaixo_extrusao_1 = new CylinderFeature(new Point3D(0, -yExtrusao, zAssentoCabeca), Vector3D.UnitZ, raioCabecaM3, alturaRebaixo) { Name = "rebaixo_extrusao_1", IsTransientTool = true };
        var rebaixo_extrusao_2 = new CylinderFeature(new Point3D(0, yExtrusao, zAssentoCabeca), Vector3D.UnitZ, raioCabecaM3, alturaRebaixo) { Name = "rebaixo_extrusao_2", IsTransientTool = true };

        // M3 verticais da trava: porca a ProfundidadePorcaTrava do topo, inserida por um canal aberto na face +X.
        var yTrava = PassoFurosTrava / 2;
        var canalTrava = 10 - XFurosTrava + 1;
        var fixacao_trava_1 = new FuroPorca(
            Entrada: new Point3D(XFurosTrava, -yTrava, AlturaApoio + 1),
            Direcao: new Vector3D(0, 0, -1),
            Profundidade: 1 + ProfundidadePorcaTrava,
            EixoHexagono: Vector3D.UnitX,
            ComprimentoCanal: canalTrava).Instance("fixacao_trava_1");
        var fixacao_trava_2 = new FuroPorca(
            Entrada: new Point3D(XFurosTrava, yTrava, AlturaApoio + 1),
            Direcao: new Vector3D(0, 0, -1),
            Profundidade: 1 + ProfundidadePorcaTrava,
            EixoHexagono: Vector3D.UnitX,
            ComprimentoCanal: canalTrava).Instance("fixacao_trava_2");

        return BooleanFeature.CreateDifference("pedestal_motor_y_2020", corpo_chanfrado,
            furo_extrusao_1, furo_extrusao_2, rebaixo_extrusao_1, rebaixo_extrusao_2,
            fixacao_trava_1, fixacao_trava_2);
    }
}
