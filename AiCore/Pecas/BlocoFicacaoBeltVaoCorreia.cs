/// <summary>
/// Sólido base do <see cref="BlocoFixacaoBelt"/> reduzido à METADE ESQUERDA do vão da correia
/// (a composição completa é espelhada em X). Contém o corpo, o vão da correia, os dois furos
/// verticais (furo_vertical e furo_vertical_3_2) e o extra (prism_f62c + cylinder_821a −
/// cylinder_54d2, agrupados em boolOp_16db), mas SEM porca_pressao_correia, rebaixo_fixacao,
/// furo_fixacao, rebaixo_frontal nem rebaixo_inferior.
/// A altura geral foi reduzida subindo o limite INFERIOR do corpo (BaseCorpoZ), mantendo o vão
/// da correia intacto (o vão já começa acima do novo piso).
/// O vão da correia tem 10 mm de altura (AlturaVao), para a correia de 10 mm: o teto do vão
/// coincide com o topo do corpo, então o piso do vão (BaseVaoZ = 7) desce 2 mm e a correia fica
/// apoiada no fundo do vão com o topo rente à face superior do bloco, sem alterar o envelope.
/// O furo_vertical (rebaixo cilíndrico) e o boolOp_7b90 (prism_f62c + cylinder_821a) começam
/// junto com o vão da correia (Z = BaseVaoZ = 7) e sobem até o topo do corpo (Z = AlturaCorpo = 17),
/// preenchendo todo o rasgo verticalmente. O cylinder_54d2 (rebaixo de 3 mm) e o boolOp_16db
/// acompanham a mesma faixa (Z = 7 a 17).
/// O furo_vertical_3_2 (furo passante) atravessa toda a peça (de Z = 3 a Z = 18).
/// Origem local = base do bloco_fixacao_mgn9 (local = mundo - (0, 0, 26)), igual ao BlocoFixacaoBelt.
/// </summary>
public sealed record BlocoFicacaoBeltVaoCorreia(
    double LarguraCorpoX = 20.5,
    double ComprimentoCorpoY = 14.5,
    double AlturaCorpo = 17.0,
    double BaseCorpoX = -20.615334,
    double BaseCorpoY = 10.5,
    double BaseCorpoZ = 4.0,
    double LarguraVaoX = 17.0,
    double ComprimentoVaoY = 6.0,
    double AlturaVao = 10.0,
    double BaseVaoX = -22.61533,
    double BaseVaoY = 15.75,
    double BaseVaoZ = 7.0,
    double RaioFuroVertical = 3.0,
    double FuroVerticalX = -5.61533,
    double FuroVerticalY = 18.75,
    double FuroVerticalZ = 7.0,
    double AlturaFuroVertical = 10.0,
    double RaioFuroVertical3_2 = 1.6,
    double RaioChanfro = 1.0) : Part
{
    protected override IFeature Build()
    {
        var corpo = new BoxFeature(
            new Point3D(BaseCorpoX, BaseCorpoY, BaseCorpoZ),
            LarguraCorpoX,
            ComprimentoCorpoY,
            AlturaCorpo - BaseCorpoZ)
        {
            Name = "corpo"
        };

        var vao_correia = new BoxFeature(new Point3D(BaseVaoX, BaseVaoY, BaseVaoZ), LarguraVaoX, ComprimentoVaoY, AlturaVao) { Name = "vao_correia", IsTransientTool = true };

        // Rebaixo CILÍNDRICO no topo (alojamento da cabeça do parafuso M3 inserido por cima).
        // Começa junto com o vão da correia (Z = BaseVaoZ) e sobe até o topo do corpo (Z = AlturaCorpo).
        var furo_vertical = new CylinderFeature(new Point3D(FuroVerticalX, FuroVerticalY, FuroVerticalZ), Vector3D.UnitZ, RaioFuroVertical, AlturaFuroVertical) { Name = "furo_vertical", IsTransientTool = true };
        // Furo vertical passante de 3,2 mm, atravessando toda a peça (de BaseCorpoZ - 1 até AlturaCorpo + 1).
        var furo_vertical_3_2 = new CylinderFeature(new Point3D(FuroVerticalX, FuroVerticalY, BaseCorpoZ - 1.0), Vector3D.UnitZ, RaioFuroVertical3_2, AlturaCorpo - BaseCorpoZ + 2.0) { Name = "furo_vertical_3_2", IsTransientTool = true };

        // Extra incorporado do BlocoFixacaoBelt: prisma com furo de 1,3 mm e rebaixo de 3 mm.
        // Começa junto com o vão da correia (Z = BaseVaoZ = 7) e sobe até o topo do corpo (Z = 17).
        var prism_f62c_verts = new[]
        {
            new Point3D(26, 3.25, 0),
            new Point3D(26, 6.25, 0),
            new Point3D(30.3, 6.25, 0),
            new Point3D(30.3, 7.55, 0),
            new Point3D(41, 7.55, 0),
            new Point3D(41, 3.25, 0)
        };
        var prism_f62c_plane = new WorkPlane(new Point3D(20.38467, 25, 7), new Vector3D(0, 0, 1), new Vector3D(-1, 0, 0));
        var prism_f62c = new PrismFeature(prism_f62c_verts, height: 10, plane: prism_f62c_plane)
        {
            Name = "prism_f62c"
        };
        var cylinder_821a = new CylinderFeature(globalBaseCenter: new Point3D(-9.91533, 18.75, 7), globalDirection: new Vector3D(0, 0, 1), radius: 1.3, height: 10)
        {
            Name = "cylinder_821a"
        };
        var cylinder_54d2 = new CylinderFeature(globalBaseCenter: new Point3D(-5.61533, 18.75, 7), globalDirection: new Vector3D(0, 0, 1), radius: 3, height: 10)
        {
            Name = "cylinder_54d2"
        };
        var boolOp_7b90 = new BooleanFeature(name: "boolOp_7b90", operation: BooleanOperationType.Union, baseFeature: prism_f62c, toolFeatures: new IFeature[] { cylinder_821a });
        var boolOp_16db = new BooleanFeature(name: "boolOp_16db", operation: BooleanOperationType.Subtract, baseFeature: boolOp_7b90, toolFeatures: new IFeature[] { cylinder_54d2 });

        var bloco_ficacao_belt_vao_correia_metade = BooleanFeature.CreateDifference(
            "bloco_ficacao_belt_vao_correia_metade",
            corpo,
            vao_correia,
            furo_vertical,
            furo_vertical_3_2);

        var bloco_ficacao_belt_vao_correia = BooleanFeature.CreateUnion("bloco_ficacao_belt_vao_correia", bloco_ficacao_belt_vao_correia_metade, boolOp_16db);

        var bloco_ficacao_belt_vao_correia_chanfrado = new FilletFeature(name: "bloco_ficacao_belt_vao_correia_chanfrado", baseFeature: bloco_ficacao_belt_vao_correia, radius: 1)
        {
            Mode = FilletMode.Bevel,
            ApplyToAllEdges = false,
            Faces = { new FaceReference(bloco_ficacao_belt_vao_correia, -1, new Vector3D(0, 0, 1), new Point3D(-10.365334, 17.75, 17), "CustomFace") }
        };
        return bloco_ficacao_belt_vao_correia_chanfrado;
    }
}