/// <summary>
/// Suporte impresso que pendura o gantry na porca do fuso Z, preso na face traseira do tubo travessa por
/// dois braços laterais. A placa horizontal recebe por baixo o flange da PorcaFlangeadaT8 (os 4 furos do
/// círculo de Ø16); os braços descem dos lados da placa até cobrir a face traseira do tubo, onde cada um
/// recebe um parafuso M4 em Y (cabeça em rebaixo pelo lado de trás). Todo o peso do gantry passa por esta
/// peça: soltar os dois parafusos desacopla o eixo Z.
/// <para>
/// Os braços ficam fora da faixa em X do motor X, que passa entre eles por cima do tubo e só para na
/// porca. A placa não avança além do flange, para não parar o motor X antes da porca.
/// </para>
/// Origem local (0, 0, 0) no eixo do fuso, na face de baixo da placa (onde o flange encosta). A face
/// traseira do tubo fica em Y = DistanciaEixoTubo e o topo dele em Z = −AlturaPlaca; os braços avançam
/// para +Y até o tubo.
/// </summary>
public sealed record SuporteFusoZ(
    double Largura = 70,
    double LarguraBraco = 12,
    double DistanciaEixoTubo = 23,
    double AvancoPlaca = 11,
    double RecuoPlaca = 14,
    double EspessuraPlaca = 8,
    double AlturaPlaca = 30,
    double AlturaTubo = 30,
    double DiametroPassagemFuso = 10,
    double DiametroFurosPorca = 3.4,
    double DiametroCirculoFurosPorca = 16,
    double DiametroFurosBraco = 4.3,
    double DiametroRebaixoBraco = 8,
    double ApoioParafusoBraco = 8) : Part
{
    protected override IFeature Build()
    {
        var zBaseTubo = -AlturaPlaca - AlturaTubo;
        var alturaBraco = EspessuraPlaca - zBaseTubo;
        var profundidadeBraco = DistanciaEixoTubo + RecuoPlaca;

        var placa = new BoxFeature(new Point3D(-Largura / 2, -RecuoPlaca, 0), Largura, RecuoPlaca + AvancoPlaca, EspessuraPlaca) { Name = "placa" };
        var braco_a = new BoxFeature(new Point3D(-Largura / 2, -RecuoPlaca, zBaseTubo), LarguraBraco, profundidadeBraco, alturaBraco) { Name = "braco_a" };
        var braco_b = new BoxFeature(new Point3D(Largura / 2 - LarguraBraco, -RecuoPlaca, zBaseTubo), LarguraBraco, profundidadeBraco, alturaBraco) { Name = "braco_b" };

        var corpo_braco_a = BooleanFeature.CreateUnion("corpo_braco_a", placa, braco_a);
        var corpo = BooleanFeature.CreateUnion("corpo", corpo_braco_a, braco_b);

        var passagem_fuso = new CylinderFeature(new Point3D(0, 0, -1), Vector3D.UnitZ, DiametroPassagemFuso / 2, EspessuraPlaca + 2) { Name = "passagem_fuso", IsTransientTool = true };

        // Furos do flange da porca, nos eixos X e Y do círculo (os mesmos da porca).
        var r = DiametroCirculoFurosPorca / 2;
        var furo_porca_1 = new CylinderFeature(new Point3D(r, 0, -1), Vector3D.UnitZ, DiametroFurosPorca / 2, EspessuraPlaca + 2) { Name = "furo_porca_1", IsTransientTool = true };
        var furo_porca_2 = new CylinderFeature(new Point3D(0, r, -1), Vector3D.UnitZ, DiametroFurosPorca / 2, EspessuraPlaca + 2) { Name = "furo_porca_2", IsTransientTool = true };
        var furo_porca_3 = new CylinderFeature(new Point3D(-r, 0, -1), Vector3D.UnitZ, DiametroFurosPorca / 2, EspessuraPlaca + 2) { Name = "furo_porca_3", IsTransientTool = true };
        var furo_porca_4 = new CylinderFeature(new Point3D(0, -r, -1), Vector3D.UnitZ, DiametroFurosPorca / 2, EspessuraPlaca + 2) { Name = "furo_porca_4", IsTransientTool = true };

        // Um M4 por braço, em Y, na meia altura do tubo e no meio do braço; o rebaixo vem pelo lado de trás e
        // deixa ApoioParafusoBraco de material contra o tubo.
        var xFuroBraco = Largura / 2 - LarguraBraco / 2;
        var zFuroBraco = zBaseTubo + AlturaTubo / 2;
        var furo_braco_a = new CylinderFeature(new Point3D(-xFuroBraco, -RecuoPlaca - 1, zFuroBraco), Vector3D.UnitY, DiametroFurosBraco / 2, profundidadeBraco + 2) { Name = "furo_braco_a", IsTransientTool = true };
        var furo_braco_b = new CylinderFeature(new Point3D(xFuroBraco, -RecuoPlaca - 1, zFuroBraco), Vector3D.UnitY, DiametroFurosBraco / 2, profundidadeBraco + 2) { Name = "furo_braco_b", IsTransientTool = true };
        var rebaixo_braco_a = new CylinderFeature(new Point3D(-xFuroBraco, -RecuoPlaca - 1, zFuroBraco), Vector3D.UnitY, DiametroRebaixoBraco / 2, profundidadeBraco - ApoioParafusoBraco + 1) { Name = "rebaixo_braco_a", IsTransientTool = true };
        var rebaixo_braco_b = new CylinderFeature(new Point3D(xFuroBraco, -RecuoPlaca - 1, zFuroBraco), Vector3D.UnitY, DiametroRebaixoBraco / 2, profundidadeBraco - ApoioParafusoBraco + 1) { Name = "rebaixo_braco_b", IsTransientTool = true };

        return BooleanFeature.CreateDifference("suporte_fuso_z", corpo,
            passagem_fuso, furo_porca_1, furo_porca_2, furo_porca_3, furo_porca_4,
            furo_braco_a, furo_braco_b, rebaixo_braco_a, rebaixo_braco_b);
    }
}
