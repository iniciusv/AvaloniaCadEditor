/// <summary>Descreva a peça Nema17.</summary>
public sealed record Nema17(double Largura = 50, double Comprimento = 50, double Espessura = 10) : Part
{
    // Geometria em coordenadas locais: a origem da peça é (0, 0, 0).
    protected override IFeature Build() =>
        new BoxFeature(Point3D.Origin, Largura, Comprimento, Espessura) { Name = "Nema17" };
}
