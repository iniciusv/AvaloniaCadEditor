/// <summary>
/// Bloco de fixação impresso com três furos passantes para M4: dois verticais (em Z), a
/// DistanciaFuroBorda de cada borda em X e no meio da largura em Y, e um horizontal (em Y) no centro
/// da face de Comprimento x Altura.
/// Origem local (0, 0, 0) no canto inferior; X = comprimento, Y = largura, Z = altura.
/// </summary>
public sealed record BlocoFixacaoM4(
    double Comprimento = 30,
    double Largura = 20,
    double Altura = 20,
    double DiametroFuro = 4.5,
    double DistanciaFuroBorda = 5) : Part
{
    protected override IFeature Build()
    {
        var corpo = new BoxFeature(Point3D.Origin, Comprimento, Largura, Altura) { Name = "corpo" };
        var furo_vertical_a = new CylinderFeature(new Point3D(DistanciaFuroBorda, Largura / 2, -1), Vector3D.UnitZ, DiametroFuro / 2, Altura + 2) { Name = "furo_vertical_a", IsTransientTool = true };
        var furo_vertical_b = new CylinderFeature(new Point3D(Comprimento - DistanciaFuroBorda, Largura / 2, -1), Vector3D.UnitZ, DiametroFuro / 2, Altura + 2) { Name = "furo_vertical_b", IsTransientTool = true };
        var furo_horizontal = new CylinderFeature(new Point3D(Comprimento / 2, -1, Altura / 2), Vector3D.UnitY, DiametroFuro / 2, Largura + 2) { Name = "furo_horizontal", IsTransientTool = true };
        var bloco_fixacao_m4 = BooleanFeature.CreateDifference("bloco_fixacao_m4", corpo, furo_vertical_a, furo_vertical_b, furo_horizontal);
        var bloco_fixacao_m4_chanfrado = new FilletFeature(name: "bloco_fixacao_m4_chanfrado", baseFeature: bloco_fixacao_m4, radius: 1)
        {
            Mode = FilletMode.Bevel,
            ApplyToAllEdges = true
        };
        return bloco_fixacao_m4_chanfrado;
    }
}
