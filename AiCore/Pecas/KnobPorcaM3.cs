/// <summary>
/// Knob (manípulo de aperto à mão) sextavado, 18 mm entre faces, com porca M3 cativa: o parafuso entra por baixo,
/// atravessa o pescoço e rosqueia na porca encaixada no alojamento sextavado do topo. Gira com a mão ou com chave 18.
/// A origem (0, 0, 0) é o centro da face de baixo do pescoço (Ø8 x 3), a que encosta na peça apertada; o eixo é +Z
/// e o topo fica em Z = Altura + 3. O alojamento tem 5,8 mm entre faces e 2,5 mm de fundo (porca M3 de 5,5 x 2,4
/// com folga de impressão): a porca entra pelo topo, sob pressão ou com uma gota de cola.
/// </summary>
public sealed record KnobPorcaM3(double Altura = 8) : Part
{
    protected override IFeature Build()
    {
        // Corpo sextavado de 18 mm entre faces, com as faces paralelas às do alojamento (parede uniforme).
        var plano_corpo = new WorkPlane(origin: new Point3D(0, 0, 3), normal: Vector3D.UnitZ, xAxis: Vector3D.UnitX);
        var corpo = new PrismFeature(new[]
        {
            new Point3D(10.392, 0, 0),
            new Point3D(5.196, 9, 0),
            new Point3D(-5.196, 9, 0),
            new Point3D(-10.392, 0, 0),
            new Point3D(-5.196, -9, 0),
            new Point3D(5.196, -9, 0)
        }, height: Altura, plane: plano_corpo, name: "corpo");
        var corpo_chanfrado = new FilletFeature(name: "corpo_chanfrado", baseFeature: corpo, radius: 0.8)
        {
            Mode = FilletMode.Bevel,
            ApplyToAllEdges = false
        };
        corpo_chanfrado.AddFace(new FaceReference(corpo, -1, Vector3D.UnitZ, new Point3D(0, 0, Altura + 3), "topo"));

        // O pescoço entra 0,5 mm no corpo: faces só encostadas não se fundem com segurança.
        var pescoco = new CylinderFeature(new Point3D(0, 0, 0), Vector3D.UnitZ, 4, 3.5) { Name = "pescoco" };
        var knob_bruto = BooleanFeature.CreateUnion("knob_bruto", corpo_chanfrado, pescoco);

        var furo = new CylinderFeature(new Point3D(0, 0, -1), Vector3D.UnitZ, 1.6, Altura + 5) { Name = "furo", IsTransientTool = true };

        // Sextavado de 5,8 mm entre faces, do fundo do alojamento até 1 mm acima do topo.
        var plano_alojamento = new WorkPlane(origin: new Point3D(0, 0, Altura + 0.5), normal: Vector3D.UnitZ, xAxis: Vector3D.UnitX);
        var alojamento_porca = new PrismFeature(new[]
        {
            new Point3D(3.349, 0, 0),
            new Point3D(1.674, 2.9, 0),
            new Point3D(-1.674, 2.9, 0),
            new Point3D(-3.349, 0, 0),
            new Point3D(-1.674, -2.9, 0),
            new Point3D(1.674, -2.9, 0)
        }, height: 3.5, plane: plano_alojamento, name: "alojamento_porca") { IsTransientTool = true };

        return BooleanFeature.CreateDifference("knob_porca_m3", knob_bruto, furo, alojamento_porca);
    }
}
