/// <summary>
/// Suporte impresso que pendura o gantry na porca do fuso Z, preso pelo topo do tubo travessa. Uma aba
/// deitada sobre o topo do tubo recebe dois parafusos M4 verticais (cabeças embutidas em rebaixo). Da
/// borda traseira da aba sobem duas bochechas, atrás da face traseira do tubo, até a placa horizontal que
/// recebe por baixo o flange da PorcaFlangeadaT8. Nervuras laterais amarram a placa às bochechas. Todo o
/// peso do gantry passa por esta peça: soltar os dois parafusos da aba desacopla o eixo Z.
/// <para>
/// A aba é baixa para o motor X passar por cima dela e do tubo. A janela entre as bochechas deixa passar
/// o flange, o corpo da porca e, no fundo do curso Z, o acoplamento do motor. O espaço atrás do tubo é
/// curto (a chapa traseira da estrutura fica 33 mm atrás dele), por isso o eixo fica perto da face do
/// tubo: o fuso passa a 2 mm das bochechas. Só os dois furos do flange no eixo X são usados; os do eixo Y
/// cairiam sobre a linha das bochechas.
/// </para>
/// Origem local (0, 0, 0) no eixo do fuso, na face de baixo da placa (onde o flange encosta). A face
/// traseira do tubo fica em Y = DistanciaEixoTubo e o topo dele em Z = −AlturaPlaca; a aba avança para
/// +Y sobre o tubo e a placa para −Y, longe dele.
/// </summary>
public sealed record SuporteFusoZ(
    double Largura = 56,
    double DistanciaEixoTubo = 11,
    double AlturaPlaca = 30,
    double ComprimentoPlaca = 25,
    double EspessuraPlaca = 8,
    double EspessuraBochecha = 5,
    double LarguraJanela = 24,
    double ProfundidadeAba = 20,
    double EspessuraAba = 6,
    double EspessuraNervura = 5,
    double DiametroPassagemFuso = 10,
    double DiametroFurosPorca = 3.4,
    double DiametroCirculoFurosPorca = 16,
    double DiametroFurosAba = 4.3,
    double DistanciaFurosAbaX = 36,
    double DiametroRebaixoAba = 8,
    double ProfundidadeRebaixoAba = 4) : Part
{
    protected override IFeature Build()
    {
        var yFundoBochecha = DistanciaEixoTubo - EspessuraBochecha;
        var yFimPlaca = DistanciaEixoTubo - ComprimentoPlaca;
        var zTopoTubo = -AlturaPlaca;
        var larguraBochecha = (Largura - LarguraJanela) / 2;
        var alturaBochecha = EspessuraPlaca + AlturaPlaca;

        var placa = new BoxFeature(new Point3D(-Largura / 2, yFimPlaca, 0), Largura, ComprimentoPlaca, EspessuraPlaca) { Name = "placa" };
        var bochecha_a = new BoxFeature(new Point3D(-Largura / 2, yFundoBochecha, zTopoTubo), larguraBochecha, EspessuraBochecha, alturaBochecha) { Name = "bochecha_a" };
        var bochecha_b = new BoxFeature(new Point3D(LarguraJanela / 2, yFundoBochecha, zTopoTubo), larguraBochecha, EspessuraBochecha, alturaBochecha) { Name = "bochecha_b" };
        // A aba começa 0,5 mm atrás da face do tubo para entrar nas bochechas sem faces só tangentes.
        var aba = new BoxFeature(new Point3D(-Largura / 2, DistanciaEixoTubo - 0.5, zTopoTubo), Largura, ProfundidadeAba + 0.5, EspessuraAba) { Name = "aba" };

        // Nervuras triangulares no plano YZ (u = Y, v = Z), extrudadas em +X a partir de cada borda lateral,
        // por baixo da placa e atrás das bochechas. Entram 0,5 mm nas duas.
        var perfilNervura = new[]
        {
            new Point3D(yFundoBochecha + 0.5, 0.5, 0),
            new Point3D(yFimPlaca, 0.5, 0),
            new Point3D(yFundoBochecha + 0.5, zTopoTubo, 0),
        };
        var nervura_a = new PrismFeature(perfilNervura, EspessuraNervura, new WorkPlane(new Point3D(-Largura / 2, 0, 0), Vector3D.UnitX, Vector3D.UnitY), "nervura_a");
        var nervura_b = new PrismFeature(perfilNervura, EspessuraNervura, new WorkPlane(new Point3D(Largura / 2 - EspessuraNervura, 0, 0), Vector3D.UnitX, Vector3D.UnitY), "nervura_b");

        var corpo_bochecha_a = BooleanFeature.CreateUnion("corpo_bochecha_a", placa, bochecha_a);
        var corpo_bochecha_b = BooleanFeature.CreateUnion("corpo_bochecha_b", corpo_bochecha_a, bochecha_b);
        var corpo_aba = BooleanFeature.CreateUnion("corpo_aba", corpo_bochecha_b, aba);
        var corpo_nervura_a = BooleanFeature.CreateUnion("corpo_nervura_a", corpo_aba, nervura_a);
        var corpo_nervuras = BooleanFeature.CreateUnion("corpo_nervuras", corpo_nervura_a, nervura_b);

        var passagem_fuso = new CylinderFeature(new Point3D(0, 0, -1), Vector3D.UnitZ, DiametroPassagemFuso / 2, EspessuraPlaca + 2) { Name = "passagem_fuso", IsTransientTool = true };

        // Furos do flange da porca nos dois pontos do círculo sobre o eixo X (os mesmos da porca).
        var r = DiametroCirculoFurosPorca / 2;
        var furo_porca_a = new CylinderFeature(new Point3D(-r, 0, -1), Vector3D.UnitZ, DiametroFurosPorca / 2, EspessuraPlaca + 2) { Name = "furo_porca_a", IsTransientTool = true };
        var furo_porca_b = new CylinderFeature(new Point3D(r, 0, -1), Vector3D.UnitZ, DiametroFurosPorca / 2, EspessuraPlaca + 2) { Name = "furo_porca_b", IsTransientTool = true };

        // Furos verticais da aba para os M4 que entram no topo do tubo, no meio da aba, com rebaixo para a
        // cabeça não ficar acima da aba (o motor X passa por cima dela).
        var yFurosAba = DistanciaEixoTubo + ProfundidadeAba / 2;
        var zTopoAba = zTopoTubo + EspessuraAba;
        var furo_aba_a = new CylinderFeature(new Point3D(-DistanciaFurosAbaX / 2, yFurosAba, zTopoTubo - 1), Vector3D.UnitZ, DiametroFurosAba / 2, EspessuraAba + 2) { Name = "furo_aba_a", IsTransientTool = true };
        var furo_aba_b = new CylinderFeature(new Point3D(DistanciaFurosAbaX / 2, yFurosAba, zTopoTubo - 1), Vector3D.UnitZ, DiametroFurosAba / 2, EspessuraAba + 2) { Name = "furo_aba_b", IsTransientTool = true };
        var rebaixo_aba_a = new CylinderFeature(new Point3D(-DistanciaFurosAbaX / 2, yFurosAba, zTopoAba - ProfundidadeRebaixoAba), Vector3D.UnitZ, DiametroRebaixoAba / 2, ProfundidadeRebaixoAba + 1) { Name = "rebaixo_aba_a", IsTransientTool = true };
        var rebaixo_aba_b = new CylinderFeature(new Point3D(DistanciaFurosAbaX / 2, yFurosAba, zTopoAba - ProfundidadeRebaixoAba), Vector3D.UnitZ, DiametroRebaixoAba / 2, ProfundidadeRebaixoAba + 1) { Name = "rebaixo_aba_b", IsTransientTool = true };

        return BooleanFeature.CreateDifference("suporte_fuso_z", corpo_nervuras,
            passagem_fuso, furo_porca_a, furo_porca_b, furo_aba_a, furo_aba_b, rebaixo_aba_a, rebaixo_aba_b);
    }
}
