/// <summary>
/// Fuso trapezoidal (padrão T8: Ø8, passo 2, avanço 8 mm por volta), simplificado como um cilindro
/// liso do diâmetro externo: a rosca não é modelada. Origem local (0, 0, 0) no centro da ponta de
/// baixo, eixo +Z.
/// </summary>
public sealed record FusoTrapezoidal(
    double Diametro = 8,
    double Comprimento = 300) : Part
{
    protected override IFeature Build() =>
        new CylinderFeature(Point3D.Origin, Vector3D.UnitZ, Diametro / 2, Comprimento) { Name = "fuso_trapezoidal" };
}
