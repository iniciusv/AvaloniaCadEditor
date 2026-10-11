public sealed record Placa(double Largura = 100, double Comprimento = 60, double Espessura = 5) : Part
{
    protected override IFeature Build() =>
        new BoxFeature(Point3D.Origin, Largura, Comprimento, Espessura) { Name = "placa" };
}
