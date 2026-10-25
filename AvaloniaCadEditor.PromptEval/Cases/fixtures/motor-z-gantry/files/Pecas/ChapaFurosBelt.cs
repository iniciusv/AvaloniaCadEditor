/// <summary>
/// Chapa de cobertura/pressão assentada no topo do BlocoFixacaoBelt.
/// Corpo: 20 (X) x 10 (Y) x 3 (Z), base em Z = 0 (origem local no canto da base).
/// O chanfro (bevel) de RaioChanfro é aplicado APENAS no corpo base (a caixa), em todas as
/// suas arestas; os furos são subtraídos depois, de modo que continuam com paredes retas.
/// Contém os dois furos verticais passantes de 3,2 mm que reproduzem, na mesma posição,
/// os furos furo_vertical_3_2 e furo_vertical_3_2_espelhado do BlocoFixacaoBelt
/// (11 mm de distância entre eles, centrados na largura e no comprimento da chapa).
/// </summary>
public sealed record ChapaFurosBelt(
    double LarguraX = 20.0,
    double ComprimentoY = 10.0,
    double Espessura = 3.0,
    double DistanciaFurosX = 11.0,
    double DiametroFuro = 3.2,
    double RaioChanfro = 1.0) : Part
{
    protected override IFeature Build()
    {
        var xFuro1 = LarguraX / 2.0 - DistanciaFurosX / 2.0;
        var xFuro2 = LarguraX / 2.0 + DistanciaFurosX / 2.0;
        var yFuro = ComprimentoY / 2.0;

        // Corpo base (caixa) e, em seguida, o chanfro aplicado somente nele.
        var corpo = new BoxFeature(new Point3D(0, 0, 0), LarguraX, ComprimentoY, Espessura) { Name = "corpo" };
        var corpo_chanfrado = new FilletFeature(name: "corpo_chanfrado", baseFeature: corpo, radius: RaioChanfro) { Mode = FilletMode.Bevel, ApplyToAllEdges = true };

        // Furos verticais passantes, subtraídos após o chanfro.
        var furo_1 = new CylinderFeature(new Point3D(xFuro1, yFuro, -1.0), Vector3D.UnitZ, DiametroFuro / 2.0, Espessura + 2.0) { Name = "furo_1", IsTransientTool = true };
        var furo_2 = new CylinderFeature(new Point3D(xFuro2, yFuro, -1.0), Vector3D.UnitZ, DiametroFuro / 2.0, Espessura + 2.0) { Name = "furo_2", IsTransientTool = true };

        return BooleanFeature.CreateDifference("chapa_furos_belt", corpo_chanfrado, furo_1, furo_2);
    }
}