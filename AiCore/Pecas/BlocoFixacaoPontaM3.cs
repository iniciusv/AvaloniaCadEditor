/// <summary>
/// Versão do <see cref="BlocoFixacaoM4"/> para a ponta, com as mesmas medidas externas e furos passantes
/// para M3 com rebaixo de 3 mm nas duas bocas de cada furo (cabeça de um lado, porca ou cabeça do outro).
/// Os dois furos verticais (em Z, entrando pelo topo) ficam juntos num lado do comprimento, no meio da
/// largura em Y; o furo horizontal (em Y, entrando pela face Y = 0) fica no outro lado, em vez de entre
/// eles, a DistanciaFuroHorizontal (entre centros) do furo vertical mais próximo.
/// Como no SuporteRetornoCorreiaY, o chanfro é só do corpo: furos e rebaixos ficam com as arestas vivas.
/// Origem local (0, 0, 0) no canto inferior; X = comprimento, Y = largura, Z = altura.
/// </summary>
public sealed record BlocoFixacaoPontaM3(
    double Comprimento = 30,
    double Largura = 20,
    double Altura = 20,
    double DiametroFuro = 3.4,
    double DiametroRebaixo = 6.5,     // cabeça cilíndrica do M3 (5,5) com folga
    double ProfundidadeRebaixo = 3,
    double DistanciaFuroBorda = 5,
    double DistanciaFuroHorizontal = 6,
    double RaioChanfro = 1) : Part
{
    protected override IFeature Build()
    {
        var corpo = new BoxFeature(Point3D.Origin, Comprimento, Largura, Altura) { Name = "corpo" };
        // Chanfro antes dos furos: as bocas e o fundo dos rebaixos não são chanfrados.
        var corpo_chanfrado = new FilletFeature("corpo_chanfrado", corpo, RaioChanfro)
        {
            Mode = FilletMode.Bevel,
            ApplyToAllEdges = true
        };

        var xFuroHorizontal = Comprimento - DistanciaFuroBorda;
        var paraBaixo = new Vector3D(0, 0, -1);
        var paraTras = new Vector3D(0, -1, 0);
        var furo_vertical_a = new FuroRebaixado(new Point3D(DistanciaFuroBorda, Largura / 2, Altura), paraBaixo, Altura, DiametroFuro, DiametroRebaixo, ProfundidadeRebaixo).Instance("furo_vertical_a");
        var furo_vertical_b = new FuroRebaixado(new Point3D(xFuroHorizontal - DistanciaFuroHorizontal, Largura / 2, Altura), paraBaixo, Altura, DiametroFuro, DiametroRebaixo, ProfundidadeRebaixo).Instance("furo_vertical_b");
        var furo_horizontal = new FuroRebaixado(new Point3D(xFuroHorizontal, 0, Altura / 2), Vector3D.UnitY, Largura, DiametroFuro, DiametroRebaixo, ProfundidadeRebaixo).Instance("furo_horizontal");
        // O mesmo furo entrando pela face oposta: dá o rebaixo da outra boca.
        var furo_vertical_a_oposto = new FuroRebaixado(new Point3D(DistanciaFuroBorda, Largura / 2, 0), Vector3D.UnitZ, Altura, DiametroFuro, DiametroRebaixo, ProfundidadeRebaixo).Instance("furo_vertical_a_oposto");
        var furo_vertical_b_oposto = new FuroRebaixado(new Point3D(xFuroHorizontal - DistanciaFuroHorizontal, Largura / 2, 0), Vector3D.UnitZ, Altura, DiametroFuro, DiametroRebaixo, ProfundidadeRebaixo).Instance("furo_vertical_b_oposto");
        var furo_horizontal_oposto = new FuroRebaixado(new Point3D(xFuroHorizontal, Largura, Altura / 2), paraTras, Largura, DiametroFuro, DiametroRebaixo, ProfundidadeRebaixo).Instance("furo_horizontal_oposto");

        return BooleanFeature.CreateDifference("bloco_fixacao_ponta_m3", corpo_chanfrado,
            furo_vertical_a, furo_vertical_b, furo_horizontal,
            furo_vertical_a_oposto, furo_vertical_b_oposto, furo_horizontal_oposto);
    }
}
