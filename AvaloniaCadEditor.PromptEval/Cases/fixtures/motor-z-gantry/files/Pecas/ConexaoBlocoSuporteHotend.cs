/// <summary>
/// Placa vertical que liga o <see cref="BlocoFixacaoMgn9"/> (em cima) ao <see cref="SuporteHotendV6"/>
/// (embaixo, Desnivel mm abaixo do bloco). Encosta com a face y = 0 na face traseira do bloco e com a
/// face y = −Espessura na face de contato do suporte.
/// Mesmo referencial do suporte: x = 0 no plano de simetria, y = 0 na face do bloco (a peça cresce
/// em −Y, para o lado do hotend), z = 0 na base, rente à base do suporte.
/// - furo_bloco_central/esq/dir: os mesmos 3 furos M3 do bloco (x = 0 e ±DistanciaFurosLateraisX, na
///   meia-altura dele), passantes em Y. O parafuso entra pela face do lado do suporte e rosqueia nas
///   porcas traseiras do bloco.
/// - fixacao_suporte_central/esq/dir: os 3 furos M3 do suporte (meia-altura dele), cada um uma
///   <see cref="FuroPorca"/> de inserção AXIAL: a porca entra por um bolso hexagonal aberto na face do
///   bloco (y = 0, livre nessa altura) e o parafuso vem do suporte, puxando-a contra o fundo do bolso.
///   A peça é impressa deitada, com essa face maior no plano da mesa, então o bolso não tem ponte.
/// O CORPO BRUTO recebe o chanfro (bevel) de RaioChanfro mm antes dos furos, como na
/// <see cref="ChapaFurosBelt"/>: as bocas dos furos ficam vivas.
/// </summary>
public sealed record ConexaoBlocoSuporteHotend(
    double Largura = 29.9,
    double Espessura = 8.0,
    double Desnivel = 20.0,             // da base do bloco à base do suporte
    double AlturaBloco = 10.0,
    double AlturaSuporte = 10.0,
    double DistanciaFurosLateraisX = 10.0,
    double DiametroFuroM3 = 3.2,
    double ProfundidadeBolsoPorca = 3.0,  // da face do bloco ao fundo do alojamento da porca
    double RaioChanfro = 1.0) : Part
{
    public double Altura => Desnivel + AlturaBloco;

    protected override IFeature Build()
    {
        var corpo = new BoxFeature(new Point3D(-Largura / 2.0, -Espessura, 0), Largura, Espessura, Altura) { Name = "corpo" };
        IFeature corpoFinal = corpo;
        if (RaioChanfro > 0.001)
        {
            var corpo_chanfrado = new FilletFeature(name: "corpo_chanfrado", baseFeature: corpo, radius: RaioChanfro) { Mode = FilletMode.Bevel, ApplyToAllEdges = true };
            corpoFinal = corpo_chanfrado;
        }

        var raioM3 = DiametroFuroM3 / 2.0;
        var yEntrada = -Espessura - 1.0;    // 1 mm fora da face do suporte, para o corte não ficar rente
        var xEsq = -DistanciaFurosLateraisX;
        var xDir = DistanciaFurosLateraisX;

        // Furos do bloco: passantes, na meia-altura do bloco.
        var zFurosBloco = Desnivel + AlturaBloco / 2.0;
        var furo_bloco_central = new CylinderFeature(new Point3D(0, yEntrada, zFurosBloco), Vector3D.UnitY, raioM3, Espessura + 2.0) { Name = "furo_bloco_central", IsTransientTool = true };
        var furo_bloco_esq = new CylinderFeature(new Point3D(xEsq, yEntrada, zFurosBloco), Vector3D.UnitY, raioM3, Espessura + 2.0) { Name = "furo_bloco_esq", IsTransientTool = true };
        var furo_bloco_dir = new CylinderFeature(new Point3D(xDir, yEntrada, zFurosBloco), Vector3D.UnitY, raioM3, Espessura + 2.0) { Name = "furo_bloco_dir", IsTransientTool = true };

        // Furos do suporte com porca M3 de inserção axial pela face do bloco (y = 0), que nessa altura
        // fica livre: a peça é impressa deitada, então o alojamento é um bolso aberto na face maior,
        // sem canal nem ponte. Como em porca_inferior_* da PlacaSuporteCarrinhoY, a Entrada fica 1 mm
        // fora da face com Profundidade 0, por isso o alojamento tem 1 mm a mais que o bolso. As folgas
        // de 8 mm da FuroPorca fazem o furo vazar a face do suporte.
        var zFurosSuporte = AlturaSuporte / 2.0;
        var yEntradaPorca = 1.0;
        var espessuraAlojamentoPorca = ProfundidadeBolsoPorca + 1.0;
        var fixacao_suporte_central = new FuroPorca(new Point3D(0, yEntradaPorca, zFurosSuporte), new Vector3D(0, -1, 0), 0,
            Insercao: InsercaoPorca.Axial, EspessuraAlojamento: espessuraAlojamentoPorca, DiametroFuro: DiametroFuroM3).Instance("fixacao_suporte_central");
        var fixacao_suporte_esq = new FuroPorca(new Point3D(xEsq, yEntradaPorca, zFurosSuporte), new Vector3D(0, -1, 0), 0,
            Insercao: InsercaoPorca.Axial, EspessuraAlojamento: espessuraAlojamentoPorca, DiametroFuro: DiametroFuroM3).Instance("fixacao_suporte_esq");
        var fixacao_suporte_dir = new FuroPorca(new Point3D(xDir, yEntradaPorca, zFurosSuporte), new Vector3D(0, -1, 0), 0,
            Insercao: InsercaoPorca.Axial, EspessuraAlojamento: espessuraAlojamentoPorca, DiametroFuro: DiametroFuroM3).Instance("fixacao_suporte_dir");

        var conexao_bloco_suporte_hotend = BooleanFeature.CreateDifference("conexao_bloco_suporte_hotend", corpoFinal,
            furo_bloco_central, furo_bloco_esq, furo_bloco_dir,
            fixacao_suporte_central, fixacao_suporte_esq, fixacao_suporte_dir);

        return conexao_bloco_suporte_hotend;
    }
}
