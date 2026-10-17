/// <summary>
/// Arruela impressa que levanta um <see cref="Rolamento"/> até a faixa da correia. Disco de base
/// (DiametroExterno) com um colar estreito no topo (DiametroColar): o colar encosta só no anel
/// interno do rolamento, que fica parado no eixo, e deixa o anel externo — o que gira com a correia —
/// sem atrito com a arruela.
/// Origem local (0, 0, 0) no centro da face de baixo, eixo +Z; Altura é o total, colar incluído.
/// </summary>
public sealed record ArruelaEspacadora(
    double Altura = 4,
    double DiametroFuro = 3.4,
    double DiametroExterno = 8,
    double DiametroColar = 5,
    double AlturaColar = 0.5) : Part
{
    protected override IFeature Build()
    {
        if (AlturaColar >= Altura)
        {
            throw new System.ArgumentOutOfRangeException(nameof(AlturaColar), AlturaColar,
                $"ArruelaEspacadora: o colar precisa ser mais baixo que a arruela ({Altura:0.###} mm).");
        }

        // O colar entra 0,5 mm na base, para a união não ter tampas coplanares.
        const double sobreposicao = 0.5;
        var alturaBase = Altura - AlturaColar;

        var base_arruela = new CylinderFeature(Point3D.Origin, Vector3D.UnitZ, DiametroExterno / 2, alturaBase) { Name = "base_arruela" };
        var colar = new CylinderFeature(new Point3D(0, 0, alturaBase - sobreposicao), Vector3D.UnitZ, DiametroColar / 2, AlturaColar + sobreposicao) { Name = "colar" };
        var corpo = BooleanFeature.CreateUnion("corpo", base_arruela, colar);

        var furo = new CylinderFeature(new Point3D(0, 0, -1), Vector3D.UnitZ, DiametroFuro / 2, Altura + 2) { Name = "furo", IsTransientTool = true };

        return BooleanFeature.CreateDifference("arruela_espacadora", corpo, furo);
    }
}
