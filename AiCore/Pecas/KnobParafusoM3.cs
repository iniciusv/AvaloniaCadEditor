/// <summary>
/// Knob (manípulo de aperto à mão) sextavado, 12 mm entre faces, com cabeça de parafuso M3 cativa: o parafuso
/// Allen (cabeça cilíndrica Ø5,5 x 3) entra pelo topo, a cabeça fica no alojamento e a rosca sai por baixo.
/// Gira com a mão ou com chave 12. A origem (0, 0, 0) é o centro da face de baixo, a que encosta na peça
/// apertada; o eixo é +Z e o topo fica em Z = Altura. O alojamento é sextavado com 5,6 mm entre faces e 3,1 mm
/// de fundo: as seis faces prendem a cabeça redonda por interferência, e é isso que transmite o torque. Se a
/// impressão sair folgada, uma gota de cola resolve.
/// </summary>
public sealed record KnobParafusoM3(double Altura = 8) : Part
{
    protected override IFeature Build()
    {
        // Corpo sextavado de 12 mm entre faces, com as faces paralelas às do alojamento (parede uniforme).
        var plano_corpo = new WorkPlane(origin: new Point3D(0, 0, 0), normal: Vector3D.UnitZ, xAxis: Vector3D.UnitX);
        var corpo = new PrismFeature(new[]
        {
            new Point3D(6.928, 0, 0),
            new Point3D(3.464, 6, 0),
            new Point3D(-3.464, 6, 0),
            new Point3D(-6.928, 0, 0),
            new Point3D(-3.464, -6, 0),
            new Point3D(3.464, -6, 0)
        }, height: Altura, plane: plano_corpo, name: "corpo");
        var corpo_chanfrado = new FilletFeature(name: "corpo_chanfrado", baseFeature: corpo, radius: 0.8)
        {
            Mode = FilletMode.Bevel,
            ApplyToAllEdges = false
        };
        corpo_chanfrado.AddFace(new FaceReference(corpo, -1, Vector3D.UnitZ, new Point3D(0, 0, Altura), "topo"));

        var furo = new CylinderFeature(new Point3D(0, 0, -1), Vector3D.UnitZ, 1.6, Altura + 2) { Name = "furo", IsTransientTool = true };

        // Sextavado de 5,6 mm entre faces, do fundo do alojamento até 1 mm acima do topo.
        var plano_alojamento = new WorkPlane(origin: new Point3D(0, 0, Altura - 3.1), normal: Vector3D.UnitZ, xAxis: Vector3D.UnitX);
        var alojamento_cabeca = new PrismFeature(new[]
        {
            new Point3D(3.233, 0, 0),
            new Point3D(1.617, 2.8, 0),
            new Point3D(-1.617, 2.8, 0),
            new Point3D(-3.233, 0, 0),
            new Point3D(-1.617, -2.8, 0),
            new Point3D(1.617, -2.8, 0)
        }, height: 4.1, plane: plano_alojamento, name: "alojamento_cabeca") { IsTransientTool = true };

        return BooleanFeature.CreateDifference("knob_parafuso_m3", corpo_chanfrado, furo, alojamento_cabeca);
    }
}
