/// <summary>
/// Trilho de guia linear HGR15 (perfil 15 x 15 mm), com 5 furos de fixação passantes + rebaixo
/// (parafuso M4 x 16 de cabeça cilíndrica), passo 60 mm, centrados no comprimento.
/// Medidas conforme catálogo HIWIN (série HG, tamanho 15): WR=15, HR=15, furo passante Ø4,5,
/// rebaixo Ø7,5 x 5,3 de profundidade, passo P=60. Os 5 furos fixam o intervalo de comprimento:
/// abaixo de 260 a margem das pontas fica menor que 10 mm; acima de 360 já caberia um 6º furo.
/// Geometria simplificada: seção retangular, sem os canais das pistas de esferas.
/// </summary>
public sealed record TrilhoHgr15(
    [System.ComponentModel.DataAnnotations.Range(260, 360)] double Comprimento = 300,
    double Largura = 15,
    double Altura = 15) : Part
{
    public double Passo => 60;

    // Geometria em coordenadas locais: a origem da peça é (0, 0, 0).
    // X = largura do trilho, Y = comprimento, Z = altura (de baixo para cima).
    protected override IFeature Build()
    {
        var xFuros = Largura / 2;
        var yCentro = Comprimento / 2;

        var corpo = new BoxFeature(Point3D.Origin, Largura, Comprimento, Altura) { Name = "corpo" };

        var furo_1 = new FuroRebaixado(new Point3D(xFuros, yCentro - 2 * Passo, Altura), new Vector3D(0, 0, -1), Altura).Instance("furo_1");
        var furo_2 = new FuroRebaixado(new Point3D(xFuros, yCentro - Passo, Altura), new Vector3D(0, 0, -1), Altura).Instance("furo_2");
        var furo_3 = new FuroRebaixado(new Point3D(xFuros, yCentro, Altura), new Vector3D(0, 0, -1), Altura).Instance("furo_3");
        var furo_4 = new FuroRebaixado(new Point3D(xFuros, yCentro + Passo, Altura), new Vector3D(0, 0, -1), Altura).Instance("furo_4");
        var furo_5 = new FuroRebaixado(new Point3D(xFuros, yCentro + 2 * Passo, Altura), new Vector3D(0, 0, -1), Altura).Instance("furo_5");

        return BooleanFeature.CreateDifference("trilho_hgr15", corpo, furo_1, furo_2, furo_3, furo_4, furo_5);
    }
}
