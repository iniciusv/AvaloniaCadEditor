/// <summary>
/// Calço impresso entre o trilho MGN9 do eixo X e o perfil 2020 que fica sob ele: chapinha de
/// 20 × 20 × 3 mm com um furo passante Ø3,4 no meio, por onde passa o parafuso M3 que prende o
/// trilho no rasgo do perfil. Quadrada, cobre a largura do perfil 2020.
/// As arestas do corpo levam um chanfro (bevel) de RaioChanfro, cortado antes do furo: a boca fica viva.
/// Origem local (0, 0, 0) no eixo do furo, na face de baixo; X = largura, Y = comprimento, Z = espessura.
/// </summary>
public sealed record ApoioTrilhoX(
    double Largura = 20,
    double Comprimento = 20,
    double Espessura = 3,
    double DiametroFuro = 3.4,
    double RaioChanfro = 1) : Part
{
    protected override IFeature Build()
    {
        var corpo = new BoxFeature(new Point3D(-Largura / 2, -Comprimento / 2, 0), Largura, Comprimento, Espessura) { Name = "corpo" };
        var corpo_chanfrado = new FilletFeature("corpo_chanfrado", corpo, RaioChanfro)
        {
            Mode = FilletMode.Bevel,
            ApplyToAllEdges = true
        };
        var furo = new CylinderFeature(Point3D.Origin, Vector3D.UnitZ, DiametroFuro / 2, Espessura)
        {
            Name = "furo",
            IsTransientTool = true
        };

        return BooleanFeature.CreateDifference("apoio_trilho_x", corpo_chanfrado, furo);
    }
}
