/// <summary>
/// Suporte em "-----C" dos dois rolamentos da ponta do eixo X: uma haste que encosta na parede do
/// <see cref="BlocoFicacaoBeltVaoCorreia"/> e é presa por um parafuso que passa pelo rasgo dela, e um
/// C na outra ponta que abraça os dois F623ZZ costas com costas num M3 vertical.
/// O C abre para longe da haste (+X): a correia dá a volta no rolamento pelo lado das costas do C e
/// os dois ramos saem pela abertura.
/// Origem local: x = 0 na face de apoio (encostada na parede do bloco), y = 0 no plano de simetria
/// (o eixo dos rolamentos e o parafuso do rasgo ficam nele), z = 0 na face de baixo do rolamento de
/// baixo. Os rolamentos ocupam z = [0, AlturaRolamentos] com o eixo em <see cref="EixoRolamentos"/>.
/// - haste: de x = 0 até as costas, de AlturaHaste para cima. Embaixo dela fica a fixação superior do
///   bloco com a cabeça do parafuso furo_fixacao_1, por isso ela começa acima dessa cabeça.
/// - fixacao_parede: M3 horizontal que vem de fora da parede do bloco, atravessa o rasgo e rosqueia
///   numa porca inserida pelo canal aberto no topo da haste. O rasgo é vertical: a altura do parafuso
///   pode variar no curso dele, e o suporte é posto na altura da correia.
/// - costas, braco_superior e braco_inferior: o C. As costas ficam a FolgaRolamento do eixo, longe da
///   flange e da correia enrolada no rolamento.
/// - colar_inferior e colar_superior: anéis estreitos em que só o anel interno dos rolamentos encosta
///   (o anel externo gira com a correia), como o colar da <see cref="ArruelaEspacadora"/>.
/// - fixacao_eixo: o M3 dos rolamentos desce do topo do braço de cima e rosqueia na porca alojada por
///   baixo do braço de baixo.
/// O contorno externo leva um chanfro de RaioChanfro, cortado antes dos colares e dos furos.
/// </summary>
public sealed record SuporteRolamentosCorreia(
    double DistanciaEixo = 20.5,      // da face de apoio ao eixo dos rolamentos
    double AlturaRolamentos = 8.0,    // dois F623ZZ de 4 mm
    double FolgaRolamento = 7.5,      // do eixo à face interna das costas (flange r 5,75, correia r 6,2)
    double AlemEixo = 6.0,            // dos braços além do eixo
    double Largura = 12.0,            // Y
    double EspessuraBraco = 5.0,
    double EspessuraCostas = 4.0,
    double AlturaHaste = 2.0,         // face de baixo da haste (acima da cabeça do parafuso do bloco)
    double AlturaParafuso = 7.5,      // eixo do parafuso do rasgo
    double ProfundidadePorcaParede = 4.0,
    double FolgaColar = 0.5,          // altura dos colares
    double DiametroColar = 5.0,
    double RaioChanfro = 1.0) : Part
{
    /// <summary>Centro da face de baixo do rolamento de baixo (a origem dele).</summary>
    public Point3D EixoRolamentos => new Point3D(DistanciaEixo, 0, 0);

    public double ZTopo => AlturaRolamentos + FolgaColar + EspessuraBraco;

    public double ZBase => -FolgaColar - EspessuraBraco;

    protected override IFeature Build()
    {
        var metadeY = Largura / 2.0;
        var xCostas = DistanciaEixo - FolgaRolamento - EspessuraCostas;
        var xFaceInternaCostas = DistanciaEixo - FolgaRolamento;
        var xPontaBracos = DistanciaEixo + AlemEixo;
        var zTopo = ZTopo;
        var zBase = ZBase;
        var zBracoSuperior = AlturaRolamentos + FolgaColar;

        if (xCostas <= 0.001)
        {
            throw new System.ArgumentOutOfRangeException(nameof(DistanciaEixo), DistanciaEixo,
                $"SuporteRolamentosCorreia: o eixo precisa ficar a mais de {FolgaRolamento + EspessuraCostas:0.###} mm da face de apoio.");
        }

        // Haste ("-----"): da face de apoio até a face interna das costas, só acima de AlturaHaste.
        var haste = new BoxFeature(new Point3D(0, -metadeY, AlturaHaste), xFaceInternaCostas, Largura, zTopo - AlturaHaste) { Name = "haste" };
        // C: costas na altura toda e os dois braços até além do eixo. Os braços entram 1 mm nas costas.
        var costas = new BoxFeature(new Point3D(xCostas, -metadeY, zBase), EspessuraCostas, Largura, zTopo - zBase) { Name = "costas" };
        var braco_superior = new BoxFeature(new Point3D(xFaceInternaCostas - 1.0, -metadeY, zBracoSuperior), xPontaBracos - xFaceInternaCostas + 1.0, Largura, EspessuraBraco) { Name = "braco_superior" };
        var braco_inferior = new BoxFeature(new Point3D(xFaceInternaCostas - 1.0, -metadeY, zBase), xPontaBracos - xFaceInternaCostas + 1.0, Largura, EspessuraBraco) { Name = "braco_inferior" };

        var c_rolamentos = BooleanFeature.CreateUnion("c_rolamentos", costas, braco_superior, braco_inferior);
        var envelope = BooleanFeature.CreateUnion("envelope", haste, c_rolamentos);

        // Chanfro só no envelope de caixas: os colares e as bocas dos furos ficam vivos.
        IFeature corpo_externo = envelope;
        if (RaioChanfro > 0.001)
        {
            var envelope_chanfrado = new FilletFeature("envelope_chanfrado", envelope, RaioChanfro)
            {
                Mode = FilletMode.Bevel,
                ApplyToAllEdges = true
            };
            corpo_externo = envelope_chanfrado;
        }

        // Colares nas faces internas dos braços; entram 0,5 mm no braço para a união não ter tampas coplanares.
        var colar_inferior = new CylinderFeature(new Point3D(DistanciaEixo, 0, -FolgaColar - 0.5), Vector3D.UnitZ, DiametroColar / 2.0, FolgaColar + 0.5) { Name = "colar_inferior" };
        var colar_superior = new CylinderFeature(new Point3D(DistanciaEixo, 0, AlturaRolamentos), Vector3D.UnitZ, DiametroColar / 2.0, FolgaColar + 0.5) { Name = "colar_superior" };
        var corpo = BooleanFeature.CreateUnion("corpo", corpo_externo, colar_inferior, colar_superior);

        // M3 dos rolamentos: a porca entra pela face de baixo (alojamento de 4 mm a partir de 1 mm fora dela)
        // e o furo sai 1 mm acima do topo do braço de cima.
        const double espessuraAlojamentoEixo = 4.0;
        var zEntradaEixo = zBase - 1.0;
        var fixacao_eixo = new FuroPorca(new Point3D(DistanciaEixo, 0, zEntradaEixo), Vector3D.UnitZ, 0,
            Insercao: InsercaoPorca.Axial, EspessuraAlojamento: espessuraAlojamentoEixo,
            FolgaDepois: zTopo + 1.0 - (zEntradaEixo + espessuraAlojamentoEixo)).Instance("fixacao_eixo");

        // M3 do rasgo: entra pela face de apoio, porca no canal aberto no topo da haste. O furo termina
        // dentro das costas (FolgaDepois), sem chegar ao vão da correia.
        const double espessuraAlojamentoParede = 3.0;
        var xFimFuroParede = xFaceInternaCostas - 2.0;
        var fixacao_parede = new FuroPorca(new Point3D(-1.0, 0, AlturaParafuso), Vector3D.UnitX, ProfundidadePorcaParede,
            Insercao: InsercaoPorca.Lateral, EixoHexagono: Vector3D.UnitZ, ComprimentoCanal: zTopo - AlturaParafuso + 1.0,
            EspessuraAlojamento: espessuraAlojamentoParede,
            FolgaDepois: xFimFuroParede - (-1.0 + ProfundidadePorcaParede + espessuraAlojamentoParede)).Instance("fixacao_parede");

        return BooleanFeature.CreateDifference("suporte_rolamentos_correia", corpo, fixacao_eixo, fixacao_parede);
    }
}
