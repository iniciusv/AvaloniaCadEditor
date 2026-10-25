/// <summary>
/// Bloco de fixação da correia (belt) com o rebaixo cilíndrico do topo restaurado.
/// Reaproveita a peça BlocoFixacaoBelt (corpo com espelhamento em X, vão da correia, alojamentos
/// de porca e os dois furos verticais passantes de 3,2 mm) e subtrai, sobre cada furo vertical,
/// um alojamento CILÍNDRICO (Ø6 x 8 mm, de Z = AlturaCorpo - AlturaFuroVertical até o topo)
/// aberto na face superior, para alojar a cabeça do parafuso M3.
/// A segunda posição é o espelho em X do furo, pelo mesmo plano de espelhamento usado pela peça
/// base: X' = 2 * (BaseCorpoX + LarguraCorpoX) - FuroVerticalX.
/// As propriedades expostas (BaseCorpoX, LarguraCorpoX, AlturaCorpo, FuroVerticalX, FuroVerticalY)
/// repetem os valores da peça base, para que a cena possa posicionar a chapa de furos do belt.
/// </summary>
public sealed record BlocoFixacaoBeltComRebaixoSuperior(
    double RaioFuroVertical = 3.0,
    double AlturaFuroVertical = 8.0) : Part
{
    private static readonly BlocoFixacaoBelt Base = new BlocoFixacaoBelt();

    public double BaseCorpoX => Base.BaseCorpoX;
    public double LarguraCorpoX => Base.LarguraCorpoX;
    public double AlturaCorpo => Base.AlturaCorpo;
    public double FuroVerticalX => Base.FuroVerticalX;
    public double FuroVerticalY => Base.FuroVerticalY;

    protected override IFeature Build()
    {
        var bloco = Base.Instance("bloco_base");

        // Mesmo plano de espelhamento em X da peça base (X' = 2 * xm - X).
        var xm = Base.BaseCorpoX + Base.LarguraCorpoX;

        // Rebaixo cilíndrico do topo: termina na face superior do corpo.
        var zBase = Base.AlturaCorpo - AlturaFuroVertical;

        var furo_vertical = new CylinderFeature(
            globalBaseCenter: new Point3D(Base.FuroVerticalX, Base.FuroVerticalY, zBase),
            globalDirection: Vector3D.UnitZ,
            radius: RaioFuroVertical,
            height: AlturaFuroVertical) { Name = "furo_vertical", IsTransientTool = true };

        var furo_vertical_espelhado = new CylinderFeature(
            globalBaseCenter: new Point3D(2.0 * xm - Base.FuroVerticalX, Base.FuroVerticalY, zBase),
            globalDirection: Vector3D.UnitZ,
            radius: RaioFuroVertical,
            height: AlturaFuroVertical) { Name = "furo_vertical_espelhado", IsTransientTool = true };

        return BooleanFeature.CreateDifference(
            "bloco_fixacao_belt_rebaixado",
            bloco,
            furo_vertical,
            furo_vertical_espelhado);
    }
}