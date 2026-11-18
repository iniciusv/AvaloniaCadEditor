/// <summary>
/// Chapa impressa que pendura o gantry na porca do fuso Z. Apoia em cima do tubo travessa, presa por dois M5
/// de cabeça cilíndrica (a cabeça assenta em cima da chapa, sem rebaixo) que descem pela chapa e pelas duas
/// paredes do tubo, com a porca por baixo do tubo, e avança para trás do tubo até o eixo do fuso. O flange da PorcaFlangeadaT8 assenta em cima da chapa, com o
/// corpo para cima, preso pelos 4 furos do círculo de Ø16. Soltar os dois M5 desacopla o eixo Z.
/// <para>
/// As 4 quinas em planta são cortadas a 45° (ChanfroQuinas), e o contorno de cima e de baixo tem um chanfro
/// menor (Chanfro). As bocas dos furos ficam vivas.
/// </para>
/// Origem local (0, 0, 0) no eixo do fuso, na face de cima da chapa (onde o flange assenta). A chapa vai de
/// Z = −Espessura a 0, e a face de baixo apoia no topo do tubo. Os furos M5 ficam DistanciaFusoFurosTubo à
/// frente do eixo, e a face traseira do tubo em Y = DistanciaEixoTubo; a chapa avança AvancoSobreTubo além
/// dela, sobre o tubo. É esta peça que diz onde o eixo do fuso fica em relação ao tubo.
/// </summary>
public sealed record SuporteFusoZ : Part
{
    /// <summary>Do eixo do fuso até a linha dos dois furos M5 do tubo, em Y.</summary>
    public const double DistanciaFusoFurosTubo = 25;

    /// <summary>Do eixo do fuso até a face traseira do tubo.</summary>
    public double DistanciaEixoTubo => DistanciaFusoFurosTubo - RecuoFurosTubo;

    public double Largura => 30;
    public double Espessura => 8;

    /// <summary>Quanto a chapa vai para trás do eixo do fuso: o raio do flange da porca (11) e 3 mm de borda.</summary>
    public double RecuoPlaca => 14;

    /// <summary>Quanto a chapa avança sobre o tubo, a partir da face traseira dele.</summary>
    public double AvancoSobreTubo => 20;

    /// <summary>Chanfro a 45° das 4 arestas verticais (as quinas em planta), em cada lado da quina.</summary>
    public double ChanfroQuinas => 8;

    /// <summary>Chanfro a 45° das arestas do contorno de cima e de baixo.</summary>
    public double Chanfro => 1.5;

    /// <summary>Passagem do fuso T8 pela chapa, com folga.</summary>
    public double DiametroPassagemFuso => 11;
    public double DiametroFurosPorca => 3.4;
    public double DiametroCirculoFurosPorca => 16;

    /// <summary>Distância entre os dois furos M5 do tubo, ao longo dele (X), centrados no eixo do fuso.</summary>
    public const double DistanciaFurosTubo = 18;

    /// <summary>Distância do centro dos furos M5 à face traseira do tubo (a do lado do fuso).</summary>
    public const double RecuoFurosTubo = 10;

    /// <summary>Furo passante para M5, na chapa e no tubo.</summary>
    public const double DiametroFurosTubo = 5.5;

    protected override IFeature Build()
    {
        // Contorno em planta: o retângulo com as 4 quinas cortadas a 45° (ChanfroQuinas em cada lado), que dá
        // o chanfro maior nas arestas verticais. Extrudado na espessura, de Z = −Espessura a 0.
        var xLado = Largura / 2;
        var yTras = -RecuoPlaca;
        var yFrente = DistanciaEixoTubo + AvancoSobreTubo;
        var c = ChanfroQuinas;
        var perfil = new PolygonProfile(new List<Point3D>
        {
            new Point3D(-xLado + c, yTras, -Espessura),
            new Point3D(xLado - c, yTras, -Espessura),
            new Point3D(xLado, yTras + c, -Espessura),
            new Point3D(xLado, yFrente - c, -Espessura),
            new Point3D(xLado - c, yFrente, -Espessura),
            new Point3D(-xLado + c, yFrente, -Espessura),
            new Point3D(-xLado, yFrente - c, -Espessura),
            new Point3D(-xLado, yTras + c, -Espessura),
        });
        var chapa_contorno = new ExtrudeFeature(perfil, Espessura, new Vector3D(0, 0, Espessura), "chapa_contorno");

        // Chanfro menor em todo o contorno de cima e de baixo, antes dos furos: as bocas deles ficam vivas.
        var yMeio = (yTras + yFrente) / 2;
        var chapa = new FilletFeature("chapa", chapa_contorno, Chanfro) { Mode = FilletMode.Bevel };
        chapa.AddFace(new FaceReference(chapa_contorno, -1, Vector3D.UnitZ, new Point3D(0, yMeio, 0), "topo"));
        chapa.AddFace(new FaceReference(chapa_contorno, -1, new Vector3D(0, 0, -1), new Point3D(0, yMeio, -Espessura), "base"));

        var passagem_fuso = new CylinderFeature(new Point3D(0, 0, -Espessura - 1), Vector3D.UnitZ, DiametroPassagemFuso / 2, Espessura + 2) { Name = "passagem_fuso", IsTransientTool = true };

        // Furos do flange da porca, nos eixos X e Y do círculo (os mesmos da porca).
        var r = DiametroCirculoFurosPorca / 2;
        var furo_porca_1 = new CylinderFeature(new Point3D(r, 0, -Espessura - 1), Vector3D.UnitZ, DiametroFurosPorca / 2, Espessura + 2) { Name = "furo_porca_1", IsTransientTool = true };
        var furo_porca_2 = new CylinderFeature(new Point3D(0, r, -Espessura - 1), Vector3D.UnitZ, DiametroFurosPorca / 2, Espessura + 2) { Name = "furo_porca_2", IsTransientTool = true };
        var furo_porca_3 = new CylinderFeature(new Point3D(-r, 0, -Espessura - 1), Vector3D.UnitZ, DiametroFurosPorca / 2, Espessura + 2) { Name = "furo_porca_3", IsTransientTool = true };
        var furo_porca_4 = new CylinderFeature(new Point3D(0, -r, -Espessura - 1), Vector3D.UnitZ, DiametroFurosPorca / 2, Espessura + 2) { Name = "furo_porca_4", IsTransientTool = true };

        // Os dois M5 sobre o tubo, a RecuoFurosTubo da face traseira dele: furo reto, a cabeça assenta na face de
        // cima.
        var yFuroTubo = DistanciaEixoTubo + RecuoFurosTubo;
        var xFuroTubo = DistanciaFurosTubo / 2;
        var raioFuroTubo = DiametroFurosTubo / 2;
        var furo_tubo_a = new CylinderFeature(new Point3D(-xFuroTubo, yFuroTubo, -Espessura - 1), Vector3D.UnitZ, raioFuroTubo, Espessura + 2) { Name = "furo_tubo_a", IsTransientTool = true };
        var furo_tubo_b = new CylinderFeature(new Point3D(xFuroTubo, yFuroTubo, -Espessura - 1), Vector3D.UnitZ, raioFuroTubo, Espessura + 2) { Name = "furo_tubo_b", IsTransientTool = true };

        return BooleanFeature.CreateDifference("suporte_fuso_z", chapa,
            passagem_fuso, furo_porca_1, furo_porca_2, furo_porca_3, furo_porca_4,
            furo_tubo_a, furo_tubo_b);
    }
}
