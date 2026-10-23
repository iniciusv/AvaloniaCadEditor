/// <summary>
/// Suporte em C (visto ao longo de Y): uma aba embaixo e outra em cima, ligadas por uma alma no lado
/// +X, com a abertura voltada para −X. Cada aba tem dois furos passantes em X, um perto de cada ponta.
/// Origem local (0, 0, 0) no canto inferior −X −Y; X = profundidade das abas, Y = comprimento,
/// Z = altura.
/// </summary>
public sealed record SuporteC(
    double Comprimento = 34,
    double Profundidade = 10,
    double Altura = 32,
    double EspessuraAba = 6,
    double EspessuraAlma = 5,
    double DiametroFuro = 4,
    double DistanciaFuroPonta = 4) : Part
{
    protected override IFeature Build()
    {
        // Abas e alma num perfil só (C no plano XZ), extrudado em Y: sem união de sólidos encostados.
        var xAlma = Profundidade - EspessuraAlma;
        var perfil = new[]
        {
            new Point3D(0, 0, 0),
            new Point3D(Profundidade, 0, 0),
            new Point3D(Profundidade, Altura, 0),
            new Point3D(0, Altura, 0),
            new Point3D(0, Altura - EspessuraAba, 0),
            new Point3D(xAlma, Altura - EspessuraAba, 0),
            new Point3D(xAlma, EspessuraAba, 0),
            new Point3D(0, EspessuraAba, 0)
        };
        // Normal −Y a partir de Y = Comprimento: o X local fica em X e o Y local em Z.
        var plano = new WorkPlane(new Point3D(0, Comprimento, 0), new Vector3D(0, -1, 0), Vector3D.UnitX);
        var corpo = new PrismFeature(perfil, Comprimento, plano, "corpo");

        var yFuroInicio = DistanciaFuroPonta;
        var yFuroFim = Comprimento - DistanciaFuroPonta;
        var zFuroBaixo = EspessuraAba / 2;
        var zFuroCima = Altura - EspessuraAba / 2;
        var comprimentoFuro = Profundidade + 2;
        var furo_baixo_1 = new CylinderFeature(new Point3D(-1, yFuroInicio, zFuroBaixo), Vector3D.UnitX, DiametroFuro / 2, comprimentoFuro) { Name = "furo_baixo_1", IsTransientTool = true };
        var furo_baixo_2 = new CylinderFeature(new Point3D(-1, yFuroFim, zFuroBaixo), Vector3D.UnitX, DiametroFuro / 2, comprimentoFuro) { Name = "furo_baixo_2", IsTransientTool = true };
        var furo_cima_1 = new CylinderFeature(new Point3D(-1, yFuroInicio, zFuroCima), Vector3D.UnitX, DiametroFuro / 2, comprimentoFuro) { Name = "furo_cima_1", IsTransientTool = true };
        var furo_cima_2 = new CylinderFeature(new Point3D(-1, yFuroFim, zFuroCima), Vector3D.UnitX, DiametroFuro / 2, comprimentoFuro) { Name = "furo_cima_2", IsTransientTool = true };
        return BooleanFeature.CreateDifference("suporte_c", corpo, furo_baixo_1, furo_baixo_2, furo_cima_1, furo_cima_2);
    }
}
