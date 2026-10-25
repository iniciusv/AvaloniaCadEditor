/// <summary>
/// Placa 40 × 30 × 5 mm com dois furos passantes Ø3,4. Origem local no centro da placa em planta,
/// com a base em Z = 0. Com Espelhada = true, a geometria é refletida em relação ao eixo Y local
/// (X → −X), gerando a placa direita como imagem especular da esquerda.
/// </summary>
public sealed record PlacaFuros(bool Espelhada = false) : Part
{
    protected override IFeature Build()
    {
        // Fator de espelhamento em X: +1 (normal) ou −1 (imagem especular).
        var sx = Espelhada ? -1.0 : 1.0;

        var corpo = new BoxFeature(new Point3D(-20, -15, 0), 40, 30, 5) { Name = "corpo" };
        var furo_1 = new CylinderFeature(new Point3D(-12.5 * sx, -7.5, 0), Vector3D.UnitZ, 1.7, 5) { Name = "furo_1", IsTransientTool = true };
        var furo_2 = new CylinderFeature(new Point3D(12.5 * sx, 9, 0), Vector3D.UnitZ, 1.7, 5) { Name = "furo_2", IsTransientTool = true };
        return BooleanFeature.CreateDifference("placa_furos", corpo, furo_1, furo_2);
    }
}
