/// <summary>
/// Rolamento de esferas simplificado: um anel cheio entre DiametroInterno e DiametroExterno, com
/// flange opcional numa das pontas (rolamentos "F", que guiam a correia de lado). Não modela pistas,
/// esferas nem blindagem — é o volume que a correia e a montagem enxergam.
/// Os modelos usados na máquina saem de <see cref="F623ZZ"/> e <see cref="F686ZZ"/>; outro modelo é
/// só passar as medidas do catálogo.
/// Origem local (0, 0, 0) no centro da face de baixo, eixo +Z. Sem FlangeNoTopo, a flange ocupa
/// Z = [0, EspessuraFlange]; com ela, Z = [Largura − EspessuraFlange, Largura]. Dois F623ZZ costas com
/// costas (flanges para fora) são um normal embaixo e um com FlangeNoTopo em cima.
/// </summary>
public sealed record Rolamento(
    double DiametroInterno = 3,
    double DiametroExterno = 10,
    double Largura = 4,
    double DiametroFlange = 0,
    double EspessuraFlange = 0,
    bool FlangeNoTopo = false) : Part
{
    /// <summary>F623ZZ: 3 × 10 × 4, flange Ø11,5 × 1 — vai no parafuso M3.</summary>
    public static Rolamento F623ZZ => new(3, 10, 4, 11.5, 1);

    /// <summary>F686ZZ: 6 × 13 × 5, flange Ø15 × 1,1 — vai sobre o espaçador Ø5,9.</summary>
    public static Rolamento F686ZZ => new(6, 13, 5, 15, 1.1);

    public bool TemFlange => DiametroFlange > DiametroExterno && EspessuraFlange > 0.001;

    protected override IFeature Build()
    {
        if (DiametroInterno >= DiametroExterno)
        {
            throw new System.ArgumentOutOfRangeException(nameof(DiametroInterno), DiametroInterno,
                $"Rolamento: o furo precisa ser menor que o diâmetro externo ({DiametroExterno:0.###} mm).");
        }

        var furo = new CylinderFeature(new Point3D(0, 0, -1), Vector3D.UnitZ, DiametroInterno / 2, Largura + 2) { Name = "furo", IsTransientTool = true };

        if (!TemFlange)
        {
            var anel = new CylinderFeature(Point3D.Origin, Vector3D.UnitZ, DiametroExterno / 2, Largura) { Name = "anel" };
            return BooleanFeature.CreateDifference("rolamento", anel, furo);
        }

        // O anel entra até a meia-espessura da flange, para a união não ter tampas coplanares.
        var zFlange = FlangeNoTopo ? Largura - EspessuraFlange : 0;
        var zAnel = FlangeNoTopo ? 0 : EspessuraFlange / 2;
        var alturaAnel = Largura - EspessuraFlange / 2;

        var anel_com_flange = new CylinderFeature(new Point3D(0, 0, zAnel), Vector3D.UnitZ, DiametroExterno / 2, alturaAnel) { Name = "anel_com_flange" };
        var flange = new CylinderFeature(new Point3D(0, 0, zFlange), Vector3D.UnitZ, DiametroFlange / 2, EspessuraFlange) { Name = "flange" };
        var corpo = BooleanFeature.CreateUnion("corpo", anel_com_flange, flange);

        return BooleanFeature.CreateDifference("rolamento", corpo, furo);
    }
}
