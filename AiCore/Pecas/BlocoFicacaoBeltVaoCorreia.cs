/// <summary>
/// Sólido base do <see cref="BlocoFixacaoBelt"/> reduzido à METADE ESQUERDA do vão da correia
/// (a composição completa é espelhada em X). Contém o corpo, o vão da correia, os dois furos
/// verticais (furo_vertical e furo_vertical_3_2) e o extra (prism_f62c + cylinder_821a),
/// mas SEM porca_pressao_correia, rebaixo_fixacao, furo_fixacao, rebaixo_frontal nem rebaixo_inferior.
/// A altura geral foi reduzida subindo o limite INFERIOR do corpo (BaseCorpoZ), mantendo o vão
/// da correia intacto (o vão já começa acima do novo piso).
/// O vão da correia tem 10 mm de altura (AlturaVao), para a correia de 10 mm: o teto do vão
/// coincide com o topo do corpo, então o piso do vão (BaseVaoZ = 7) desce 2 mm e a correia fica
/// apoiada no fundo do vão com o topo rente à face superior do bloco, sem alterar o envelope.
/// O comprimento do vão (ComprimentoVaoY) é derivado do RaioFuroVertical (2 × raio), não sendo
/// mais exposto como parâmetro.
/// O furo_vertical (rebaixo cilíndrico) e o boolOp_7b90 (prism_f62c + cylinder_821a) começam
/// junto com o vão da correia (Z = BaseVaoZ = 7) e sobem até o topo do corpo (Z = AlturaCorpo = 17),
/// preenchendo todo o rasgo verticalmente.
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
    double AlturaVao = 10.0,
    double BaseVaoX = -22.61533,
    double BaseVaoY = 15.75,
    double BaseVaoZ = 7.0,
    double RaioFuroVertical = 3.25,
    double FuroVerticalX = -5.61533,
    double FuroVerticalY = 18.75,
    double FuroVerticalZ = 7.0,
    double AlturaFuroVertical = 10.0,
    double RaioFuroVertical3_2 = 1.6) : Part
{
    protected override IFeature Build()
    {
        // Comprimento do vão derivado do raio do furo vertical (2 × raio).
        var comprimentoVaoY = 2.0 * RaioFuroVertical;

        var corpo = new BoxFeature(
            new Point3D(BaseCorpoX, BaseCorpoY, BaseCorpoZ),
            LarguraCorpoX,
            ComprimentoCorpoY,
            AlturaCorpo - BaseCorpoZ)
        {
            Name = "corpo"
        };

        var vao_correia = new BoxFeature(new Point3D(BaseVaoX, BaseVaoY, BaseVaoZ), LarguraVaoX, comprimentoVaoY, AlturaVao) { Name = "vao_correia", IsTransientTool = true }.Transformed(Placement.Translate(new Vector3D(0, -0.25, 0)));

        // Rebaixo CILÍNDRICO no topo (alojamento da cabeça do parafuso M3 inserido por cima).
        var furo_vertical = new CylinderFeature(new Point3D(FuroVerticalX, FuroVerticalY, FuroVerticalZ), Vector3D.UnitZ, RaioFuroVertical, AlturaFuroVertical) { Name = "furo_vertical", IsTransientTool = true };
        // Furo vertical passante de 3,2 mm, atravessando toda a peça.
        var furo_vertical_3_2 = new CylinderFeature(new Point3D(FuroVerticalX, FuroVerticalY, BaseCorpoZ - 1.0), Vector3D.UnitZ, RaioFuroVertical3_2, AlturaCorpo - BaseCorpoZ + 2.0) { Name = "furo_vertical_3_2", IsTransientTool = true };

        // Extra incorporado do BlocoFixacaoBelt: prisma com furo de 1,3 mm.
        var prism_f62c_verts = new[]
        {
            new Point3D(26, 3.0, 0),
            new Point3D(26, 6.25, 0),
            new Point3D(30.3, 6.25, 0),
            new Point3D(30.3, 7.55, 0),
            new Point3D(41, 7.55, 0),
            new Point3D(41, 3.0, 0)
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
        var boolOp_7b90 = new BooleanFeature(name: "boolOp_7b90", operation: BooleanOperationType.Union, baseFeature: prism_f62c, toolFeatures: new IFeature[] { cylinder_821a }).Transformed(Placement.Translate(new Vector3D(0, 0.5, 0)));

        // O vão é subtraído antes da união com o extra para que o extra permaneça dentro do rasgo.
        var corpo_com_vao = BooleanFeature.CreateDifference("corpo_com_vao", corpo, vao_correia);
        var corpo_com_extra = BooleanFeature.CreateUnion("corpo_com_extra", corpo_com_vao, boolOp_7b90);

        // Os furos verticais são subtraídos POR ÚLTIMO, cortando corpo e extra simultaneamente.
        var bloco_ficacao_belt_vao_correia = BooleanFeature.CreateDifference(
            "bloco_ficacao_belt_vao_correia",
            corpo_com_extra,
            furo_vertical,
            furo_vertical_3_2);

        var bloco_ficacao_belt_vao_correia_chanfrado = new FilletFeature(name: "bloco_ficacao_belt_vao_correia_chanfrado", baseFeature: bloco_ficacao_belt_vao_correia, radius: 1)
        {
            Mode = FilletMode.Bevel,
            ApplyToAllEdges = false,
            Faces = { new FaceReference(bloco_ficacao_belt_vao_correia, -1, new Vector3D(0, 0, 1), new Point3D(-0.115334, 25, 17), "CustomFace") }
        };
        return bloco_ficacao_belt_vao_correia_chanfrado;
    }
}