/// <summary>
/// Espaçador impresso entre a face de baixo de uma extrusão 2020 e o topo do tubo travessa. Abaixa o tubo
/// para o motor X (e a aba do suporte do fuso Z) passarem por cima dele. Os dois furos de M5 que atravessam
/// a parede de cima do tubo e o espaçador até a porca T no rasgo da extrusão são feitos à mão, sem
/// precisão: por isso são RASGOS abertos para as bordas em ±Y, com a ponta arredondada na posição do furo
/// original — dá para o parafuso entrar deslizando e sobra folga para acertar a montagem em Y.
/// Origem local (0, 0, 0) no canto inferior; X = largura (a do perfil), Y = profundidade (a do tubo),
/// Z = altura.
/// </summary>
public sealed record EspacadorTuboTravessa(
    double Largura = 20,
    double Profundidade = 50,
    double Altura = 10,
    double DiametroFuro = 5.3,
    double DistanciaFurosY = 25) : Part
{
    protected override IFeature Build()
    {
        const double raioChanfro = 1.0;
        const double folgaFerramenta = 1.0;

        var raioRasgo = DiametroFuro / 2;
        var yRasgoA = (Profundidade - DistanciaFurosY) / 2;
        var yRasgoB = (Profundidade + DistanciaFurosY) / 2;

        var corpo = new BoxFeature(Point3D.Origin, Largura, Profundidade, Altura) { Name = "corpo" };
        var corpo_chanfrado = new FilletFeature("corpo_chanfrado", corpo, raioChanfro)
        {
            Mode = FilletMode.Bevel,
            ApplyToAllEdges = true,
        };
        // Rasgo A: da ponta arredondada em yRasgoA até sair pela borda −Y.
        var rasgo_a = new BoxFeature(new Point3D(Largura / 2 - raioRasgo, -folgaFerramenta, -folgaFerramenta), DiametroFuro, yRasgoA + folgaFerramenta, Altura + 2 * folgaFerramenta) { Name = "rasgo_a", IsTransientTool = true };
        var ponta_rasgo_a = new CylinderFeature(new Point3D(Largura / 2, yRasgoA, -folgaFerramenta), Vector3D.UnitZ, raioRasgo, Altura + 2 * folgaFerramenta) { Name = "ponta_rasgo_a", IsTransientTool = true };
        // Rasgo B: da ponta arredondada em yRasgoB até sair pela borda +Y.
        var rasgo_b = new BoxFeature(new Point3D(Largura / 2 - raioRasgo, yRasgoB, -folgaFerramenta), DiametroFuro, Profundidade - yRasgoB + folgaFerramenta, Altura + 2 * folgaFerramenta) { Name = "rasgo_b", IsTransientTool = true };
        var ponta_rasgo_b = new CylinderFeature(new Point3D(Largura / 2, yRasgoB, -folgaFerramenta), Vector3D.UnitZ, raioRasgo, Altura + 2 * folgaFerramenta) { Name = "ponta_rasgo_b", IsTransientTool = true };
        return BooleanFeature.CreateDifference("espacador_tubo_travessa", corpo_chanfrado, rasgo_a, ponta_rasgo_a, rasgo_b, ponta_rasgo_b);
    }
}