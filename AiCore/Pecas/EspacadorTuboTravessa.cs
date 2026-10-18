/// <summary>
/// Espaçador impresso entre a face de baixo de uma extrusão 2020 e o topo do tubo travessa. Abaixa o tubo
/// para o motor X (e a aba do suporte do fuso Z) passarem por cima dele. Dois furos verticais para os M5
/// que atravessam a parede de cima do tubo e o espaçador até a porca T no rasgo da extrusão.
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
        var corpo = new BoxFeature(Point3D.Origin, Largura, Profundidade, Altura) { Name = "corpo" };
        var furo_a = new CylinderFeature(new Point3D(Largura / 2, (Profundidade - DistanciaFurosY) / 2, -1), Vector3D.UnitZ, DiametroFuro / 2, Altura + 2) { Name = "furo_a", IsTransientTool = true };
        var furo_b = new CylinderFeature(new Point3D(Largura / 2, (Profundidade + DistanciaFurosY) / 2, -1), Vector3D.UnitZ, DiametroFuro / 2, Altura + 2) { Name = "furo_b", IsTransientTool = true };
        return BooleanFeature.CreateDifference("espacador_tubo_travessa", corpo, furo_a, furo_b);
    }
}
