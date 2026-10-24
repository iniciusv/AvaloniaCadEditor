/// <summary>
/// Suporte em C (visto ao longo de Y): uma aba embaixo e outra em cima, ligadas por uma alma no lado
/// +X, com a abertura voltada para −X. Cada aba tem dois furos passantes em X, um perto de cada ponta.
/// Origem local (0, 0, 0) no centro da abertura do C (meio da altura, canto −X −Y): o vão interno e a
/// furação não mudam com a Altura — o que cresce são as abas, igualmente para +Z e para −Z.
/// X = profundidade das abas, Y = comprimento, Z = altura.
/// </summary>
public sealed record SuporteC(
    double Comprimento = 34,
    double Profundidade = 10,
    double Altura = 32) : Part
{
    protected override IFeature Build()
    {
        // O vão interno e o padrão de furação são fixos: a Altura só engrossa as duas abas, metade
        // para cima e metade para baixo, mantendo a simetria em Z.
        const double espessuraAlma = 5;
        const double alturaInterna = 20;
        const double diametroFuro = 4;
        const double passoFuros = 26; // distância centro a centro entre furos, em Y e em Z

        var espessuraAba = (Altura - alturaInterna) / 2;
        var zInternoBaixo = -alturaInterna / 2;
        var zInternoCima = alturaInterna / 2;
        var zBase = zInternoBaixo - espessuraAba;
        var zTopo = zInternoCima + espessuraAba;

        var xAlma = Profundidade - espessuraAlma;
        var perfil = new[]
        {
            new Point3D(0, zBase, 0),
            new Point3D(Profundidade, zBase, 0),
            new Point3D(Profundidade, zTopo, 0),
            new Point3D(0, zTopo, 0),
            new Point3D(0, zInternoCima, 0),
            new Point3D(xAlma, zInternoCima, 0),
            new Point3D(xAlma, zInternoBaixo, 0),
            new Point3D(0, zInternoBaixo, 0)
        };
        var plano = new WorkPlane(new Point3D(0, Comprimento, 0), new Vector3D(0, -1, 0), Vector3D.UnitX);
        var corpo = new PrismFeature(perfil, Comprimento, plano, "corpo");

        // Furos de fixação: o par fica a passoFuros um do outro, centrado no vão interno (em Z) e no
        // comprimento (em Y) — a posição deles não depende da Altura.
        var yFuroInicio = (Comprimento - passoFuros) / 2;
        var yFuroFim = yFuroInicio + passoFuros;
        var zFuroBaixo = -passoFuros / 2;
        var zFuroCima = passoFuros / 2;
        var comprimentoFuro = Profundidade + 2;

        var furo_baixo_1 = new CylinderFeature(new Point3D(-1, yFuroInicio, zFuroBaixo), Vector3D.UnitX, diametroFuro / 2, comprimentoFuro) { Name = "furo_baixo_1", IsTransientTool = true };
        var furo_baixo_2 = new CylinderFeature(new Point3D(-1, yFuroFim, zFuroBaixo), Vector3D.UnitX, diametroFuro / 2, comprimentoFuro) { Name = "furo_baixo_2", IsTransientTool = true };
        var furo_cima_1 = new CylinderFeature(new Point3D(-1, yFuroInicio, zFuroCima), Vector3D.UnitX, diametroFuro / 2, comprimentoFuro) { Name = "furo_cima_1", IsTransientTool = true };
        var furo_cima_2 = new CylinderFeature(new Point3D(-1, yFuroFim, zFuroCima), Vector3D.UnitX, diametroFuro / 2, comprimentoFuro) { Name = "furo_cima_2", IsTransientTool = true };
        return BooleanFeature.CreateDifference("suporte_c", corpo, furo_baixo_1, furo_baixo_2, furo_cima_1, furo_cima_2);
    }
}