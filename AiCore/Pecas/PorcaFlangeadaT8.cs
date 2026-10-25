/// <summary>
/// Porca flangeada de latão para fuso T8, nas medidas de catálogo: flange Ø22 × 3,5 com 4 furos Ø3,5
/// num círculo de Ø16, corpo Ø10,2 e 15 mm de comprimento total. A rosca é o furo liso do diâmetro do
/// fuso. Origem local (0, 0, 0) no centro da face do flange que encosta na peça; o flange ocupa
/// Z = [0, EspessuraFlange] e o corpo segue até Z = Comprimento.
/// </summary>
public sealed record PorcaFlangeadaT8(
    double DiametroFlange = 22,
    double EspessuraFlange = 3.5,
    double DiametroCorpo = 10.2,
    double Comprimento = 15,
    double DiametroFuso = 8,
    double DiametroFurosFixacao = 3.5,
    double DiametroCirculoFuros = 16) : Part
{
    protected override IFeature Build()
    {
        var flange = new CylinderFeature(Point3D.Origin, Vector3D.UnitZ, DiametroFlange / 2, EspessuraFlange) { Name = "flange" };
        // O corpo começa 0,5 mm dentro do flange, para a união não ter faces só tangentes.
        var corpo = new CylinderFeature(new Point3D(0, 0, EspessuraFlange - 0.5), Vector3D.UnitZ, DiametroCorpo / 2, Comprimento - EspessuraFlange + 0.5) { Name = "corpo" };
        var porca = BooleanFeature.CreateUnion("porca", flange, corpo);

        // Furos de fixação nos eixos X e Y do círculo de furos.
        var r = DiametroCirculoFuros / 2;
        var furo_fixacao_1 = new CylinderFeature(new Point3D(r, 0, -1), Vector3D.UnitZ, DiametroFurosFixacao / 2, EspessuraFlange + 2) { Name = "furo_fixacao_1", IsTransientTool = true };
        var furo_fixacao_2 = new CylinderFeature(new Point3D(0, r, -1), Vector3D.UnitZ, DiametroFurosFixacao / 2, EspessuraFlange + 2) { Name = "furo_fixacao_2", IsTransientTool = true };
        var furo_fixacao_3 = new CylinderFeature(new Point3D(-r, 0, -1), Vector3D.UnitZ, DiametroFurosFixacao / 2, EspessuraFlange + 2) { Name = "furo_fixacao_3", IsTransientTool = true };
        var furo_fixacao_4 = new CylinderFeature(new Point3D(0, -r, -1), Vector3D.UnitZ, DiametroFurosFixacao / 2, EspessuraFlange + 2) { Name = "furo_fixacao_4", IsTransientTool = true };
        var furo_fuso = new CylinderFeature(new Point3D(0, 0, -1), Vector3D.UnitZ, DiametroFuso / 2, Comprimento + 2) { Name = "furo_fuso", IsTransientTool = true };

        return BooleanFeature.CreateDifference("porca_flangeada_t8", porca, furo_fuso, furo_fixacao_1, furo_fixacao_2, furo_fixacao_3, furo_fixacao_4);
    }
}
