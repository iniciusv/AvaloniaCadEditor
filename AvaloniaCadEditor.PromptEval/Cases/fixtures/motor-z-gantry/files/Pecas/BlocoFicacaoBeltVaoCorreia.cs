/// <summary>
/// Conexão da correia à <see cref="PlacaSuporteCarrinhoY"/>: uma <see cref="GarraCorreia"/> encostada
/// na face externa da placa (x = 16 local da placa), com o topo rente ao topo da placa, mais o
/// suporte que a prende na placa.
/// Mesmo referencial da garra: x = 0 no plano de simetria, y = 0 na face de apoio (encostada na
/// placa; a garra cresce em +Y, para fora, e a fixação superior avança em −Y, sobre a placa), z = 0
/// na base da garra.
/// - furo_fixacao_x_6: furo horizontal no plano de simetria, coaxial à fixacao_x_6 da placa — o
///   parafuso entra pela face de fora da garra e rosqueia na porca da placa. Passa pela nervura
///   entre os dois alojamentos (paredes de ~0,55 mm de cada lado).
/// - parede: continua para cima a parede maciça da garra (de y = 0 até a parede do canal), sem
///   cobrir o canal; leva dois rasgos verticais (stadium) que a atravessam em Y, a DistanciaRasgos
///   para cada lado do meio dos furos de fixação e AlturaRasgo acima do topo da placa.
/// - fixacao_superior: chapa horizontal assentada no topo da placa, com os furos coincidentes com
///   furo_fixacao_carrinho_1 e furo_fixacao_carrinho_2 (o mesmo parafuso vai à porca do carrinho).
/// Todo o contorno externo leva um chanfro (bevel) de RaioChanfro mm; as bocas do canal, dos furos
/// e dos rasgos ficam vivas (ver o comentário do envelope_chanfrado).
/// </summary>
public sealed record BlocoFicacaoBeltVaoCorreia(
    double AlturaParede = 20.0,
    double EspessuraFixacaoSuperior = 5.0,
    double PassoFurosFixacao = 25.0,
    double DeslocamentoFurosFixacao = 2.0,   // em X, do plano de simetria ao meio dos dois furos
    double DistanciaFurosFixacaoFace = 3.5,  // dos furos à face de apoio (= dos furos da placa à face dela)
    double MargemFurosFixacao = 4.0,         // da chapa além dos furos
    double DiametroFuro = 3.4,
    double DistanciaFuroLateralTopo = 4.375, // eixo da fixacao_x_6 abaixo do topo da placa
    double DistanciaRasgos = 10.0,
    double AlturaRasgo = 12.5,
    double RasgoMeiaAltura = 4.0,
    double RaioChanfro = 1.0) : Part
{
    private static readonly GarraCorreia Garra = new GarraCorreia();

    public double Altura => Garra.Altura;

    protected override IFeature Build()
    {
        var metadeX = Garra.Comprimento / 2.0;
        var raioFuro = DiametroFuro / 2.0;
        var topoGarra = Garra.Altura;
        var yParede = Garra.YParede;

        var garra_correia = Garra.Instance("garra_correia");

        // Parede maciça da garra continuada para cima; apoiada no topo dela (faces coincidentes).
        var parede = new BoxFeature(new Point3D(-metadeX, 0, topoGarra), Garra.Comprimento, yParede, AlturaParede) { Name = "parede" };
        // Chapa horizontal sobre a placa, da parede até MargemFurosFixacao além dos furos.
        var yFurosFixacao = -DistanciaFurosFixacaoFace;
        var yInicioFixacao = yFurosFixacao - MargemFurosFixacao;
        var fixacao_superior = new BoxFeature(new Point3D(-metadeX, yInicioFixacao, topoGarra), Garra.Comprimento, yParede - yInicioFixacao, EspessuraFixacaoSuperior) { Name = "fixacao_superior" };

        var suporte = BooleanFeature.CreateUnion("suporte", parede, fixacao_superior);
        var corpo = BooleanFeature.CreateUnion("corpo", garra_correia, suporte);

        // Furos verticais coincidentes com furo_fixacao_carrinho_1 e furo_fixacao_carrinho_2 da placa.
        var furo_fixacao_1 = new CylinderFeature(new Point3D(DeslocamentoFurosFixacao - PassoFurosFixacao / 2.0, yFurosFixacao, topoGarra - 1.0), Vector3D.UnitZ, raioFuro, EspessuraFixacaoSuperior + 2.0) { Name = "furo_fixacao_1", IsTransientTool = true };
        var furo_fixacao_2 = new CylinderFeature(new Point3D(DeslocamentoFurosFixacao + PassoFurosFixacao / 2.0, yFurosFixacao, topoGarra - 1.0), Vector3D.UnitZ, raioFuro, EspessuraFixacaoSuperior + 2.0) { Name = "furo_fixacao_2", IsTransientTool = true };

        // Furo horizontal coaxial à fixacao_x_6 da placa, atravessando a garra no plano de simetria.
        var furo_fixacao_x_6 = new CylinderFeature(new Point3D(0, -1.0, topoGarra - DistanciaFuroLateralTopo), Vector3D.UnitY, raioFuro, Garra.Largura + 2.0) { Name = "furo_fixacao_x_6", IsTransientTool = true };

        // Rasgos verticais (stadium) na parede, atravessando-a em Y: a caixa dá o trecho reto e os
        // dois cilindros arredondam as pontas.
        var xRasgo1 = DeslocamentoFurosFixacao - DistanciaRasgos;
        var xRasgo2 = DeslocamentoFurosFixacao + DistanciaRasgos;
        var zRasgo = topoGarra + AlturaRasgo;
        var rasgo_1 = new BoxFeature(new Point3D(xRasgo1 - raioFuro, -1.0, zRasgo - RasgoMeiaAltura), DiametroFuro, yParede + 2.0, 2.0 * RasgoMeiaAltura) { Name = "rasgo_1", IsTransientTool = true };
        var cilindro_rasgo_superior_1 = new CylinderFeature(new Point3D(xRasgo1, -1.0, zRasgo + RasgoMeiaAltura), Vector3D.UnitY, raioFuro, yParede + 2.0) { Name = "cilindro_rasgo_superior_1", IsTransientTool = true };
        var cilindro_rasgo_inferior_1 = new CylinderFeature(new Point3D(xRasgo1, -1.0, zRasgo - RasgoMeiaAltura), Vector3D.UnitY, raioFuro, yParede + 2.0) { Name = "cilindro_rasgo_inferior_1", IsTransientTool = true };
        var rasgo_2 = new BoxFeature(new Point3D(xRasgo2 - raioFuro, -1.0, zRasgo - RasgoMeiaAltura), DiametroFuro, yParede + 2.0, 2.0 * RasgoMeiaAltura) { Name = "rasgo_2", IsTransientTool = true };
        var cilindro_rasgo_superior_2 = new CylinderFeature(new Point3D(xRasgo2, -1.0, zRasgo + RasgoMeiaAltura), Vector3D.UnitY, raioFuro, yParede + 2.0) { Name = "cilindro_rasgo_superior_2", IsTransientTool = true };
        var cilindro_rasgo_inferior_2 = new CylinderFeature(new Point3D(xRasgo2, -1.0, zRasgo - RasgoMeiaAltura), Vector3D.UnitY, raioFuro, yParede + 2.0) { Name = "cilindro_rasgo_inferior_2", IsTransientTool = true };

        var bloco_furado = BooleanFeature.CreateDifference("bloco_furado", corpo,
            furo_fixacao_1, furo_fixacao_2, furo_fixacao_x_6,
            rasgo_1, cilindro_rasgo_superior_1, cilindro_rasgo_inferior_1,
            rasgo_2, cilindro_rasgo_superior_2, cilindro_rasgo_inferior_2);

        if (RaioChanfro <= 0.001)
        {
            return bloco_furado;
        }

        // Chanfro no ENVELOPE (garra bruta + suporte, sem vazio nenhum), intersectado com a peça furada:
        // o chanfro por malha faz uma booleana por aresta convexa, e no sólido já furado cada boca de
        // furo/alojamento viraria dezenas de arestas. Assim só o contorno externo é chanfrado e as bocas
        // do canal, dos furos e dos rasgos ficam vivas.
        var envelope_garra = new BoxFeature(new Point3D(-metadeX, 0, 0), Garra.Comprimento, Garra.Largura, Garra.Altura) { Name = "envelope_garra" };
        var envelope = BooleanFeature.CreateUnion("envelope", envelope_garra, suporte);
        var envelope_chanfrado = new FilletFeature("envelope_chanfrado", envelope, RaioChanfro)
        {
            Mode = FilletMode.Bevel,
            ApplyToAllEdges = true
        };

        return BooleanFeature.CreateIntersection("bloco_ficacao_belt_vao_correia", bloco_furado, envelope_chanfrado);
    }
}
