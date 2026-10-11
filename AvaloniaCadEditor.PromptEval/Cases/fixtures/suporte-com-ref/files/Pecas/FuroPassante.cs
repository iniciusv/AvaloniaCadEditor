public sealed record FuroPassante(double Diametro = 6, double Profundidade = 5) : Part
{
    protected override IFeature Build() =>
        new CylinderFeature(Point3D.Origin, Vector3D.UnitZ, Diametro / 2, Profundidade) { Name = "furo" };
}
