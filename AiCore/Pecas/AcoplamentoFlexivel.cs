/// <summary>
/// Acoplamento flexível de alumínio, simplificado: um cilindro com um furo em cada ponta, o de baixo
/// para o eixo do motor e o de cima para o fuso. A fenda helicoidal e os parafusos de aperto não são
/// modelados. O padrão (Ø20 × 25, furos 5 e 8) é o 5 × 8 mm que liga um NEMA17 a um fuso T8.
/// Origem local (0, 0, 0) no centro da face de baixo, eixo +Z; cada furo vai até o meio.
/// </summary>
public sealed record AcoplamentoFlexivel(
    double DiametroExterno = 20,
    double Comprimento = 25,
    double DiametroFuroMotor = 5,
    double DiametroFuroFuso = 8) : Part
{
    protected override IFeature Build()
    {
        var corpo = new CylinderFeature(Point3D.Origin, Vector3D.UnitZ, DiametroExterno / 2, Comprimento) { Name = "corpo" };
        // Cada furo sai 1 mm além da sua face, e o do fuso desce 0,5 mm dentro do do motor: nenhuma
        // face das ferramentas coincide com outra.
        var furo_motor = new CylinderFeature(new Point3D(0, 0, -1), Vector3D.UnitZ, DiametroFuroMotor / 2, Comprimento / 2 + 1) { Name = "furo_motor", IsTransientTool = true };
        var furo_fuso = new CylinderFeature(new Point3D(0, 0, Comprimento / 2 - 0.5), Vector3D.UnitZ, DiametroFuroFuso / 2, Comprimento / 2 + 1.5) { Name = "furo_fuso", IsTransientTool = true };
        return BooleanFeature.CreateDifference("acoplamento_flexivel", corpo, furo_motor, furo_fuso);
    }
}
