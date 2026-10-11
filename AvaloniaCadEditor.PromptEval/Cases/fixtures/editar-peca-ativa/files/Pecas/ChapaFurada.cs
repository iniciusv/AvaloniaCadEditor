public sealed record ChapaFurada(double Largura = 50, double Comprimento = 30, double Espessura = 5, double DiametroFuro = 5) : Part
{
    protected override IFeature Build()
    {
        var corpo = new BoxFeature(Point3D.Origin, Largura, Comprimento, Espessura) { Name = "corpo" };
        var furo_1 = new CylinderFeature(new Point3D(DiametroFuro, DiametroFuro, 0), Vector3D.UnitZ, DiametroFuro / 2, Espessura) { Name = "furo_1", IsTransientTool = true };
        var furo_2 = new CylinderFeature(new Point3D(Largura - DiametroFuro, Comprimento - DiametroFuro, 0), Vector3D.UnitZ, DiametroFuro / 2, Espessura) { Name = "furo_2", IsTransientTool = true };
        return BooleanFeature.CreateDifference("chapa_furada", corpo, furo_1, furo_2);
    }
}
