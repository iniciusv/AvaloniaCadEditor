/// <summary>
/// Espaçador impresso de face 20 x 20 com duas abas na face de trás, uma em cada borda em Y, que
/// formam um U com o bloco. Um furo passante para M3 no centro da face de 20 x 20 atravessa o bloco
/// em X, passando entre as abas. Uma aba lateral de EspessuraAbaLateral sai da face +Y por toda a
/// altura, de x = 0 até ComprimentoAbaLateral, passando da face +X do bloco.
/// Origem local (0, 0, 0) no canto inferior das abas; X = espessura (abas de 0 a EspessuraAba, bloco
/// de EspessuraAba a EspessuraAba + Espessura), Y = largura, Z = altura.
/// </summary>
public sealed record Espacador(
    double Largura = 20,
    double Altura = 20,
    double Espessura = 6,
    double EspessuraAba = 3,
    double LarguraAba = 2.2,
    double DiametroFuro = 3.4,
    double ComprimentoAbaLateral = 15,
    double EspessuraAbaLateral = 2) : Part
{
    protected override IFeature Build()
    {
        // Bloco, abas e aba lateral num perfil só (U na planta), extrudado em Z: sem união de sólidos
        // encostados.
        var espessuraTotal = EspessuraAba + Espessura;
        var perfil = new[]
        {
            new Point3D(0, 0, 0),
            new Point3D(espessuraTotal, 0, 0),
            new Point3D(espessuraTotal, Largura, 0),
            new Point3D(ComprimentoAbaLateral, Largura, 0),
            new Point3D(ComprimentoAbaLateral, Largura + EspessuraAbaLateral, 0),
            new Point3D(0, Largura + EspessuraAbaLateral, 0),
            new Point3D(0, Largura - LarguraAba, 0),
            new Point3D(EspessuraAba, Largura - LarguraAba, 0),
            new Point3D(EspessuraAba, LarguraAba, 0),
            new Point3D(0, LarguraAba, 0)
        };
        var plano = new WorkPlane(Point3D.Origin, Vector3D.UnitZ, Vector3D.UnitX);

        var corpo = new PrismFeature(perfil, Altura, plano, "corpo");
        var furo_central = new CylinderFeature(new Point3D(-1, Largura / 2, Altura / 2), Vector3D.UnitX, DiametroFuro / 2, espessuraTotal + 2) { Name = "furo_central", IsTransientTool = true };
        return BooleanFeature.CreateDifference("espacador", corpo, furo_central);
    }
}
