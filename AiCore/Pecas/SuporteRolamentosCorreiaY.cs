/// <summary>
/// Suporte em C dos rolamentos de retorno da correia Y (a polia larga). É o mesmo desenho do
/// <see cref="SuporteRolamentosCorreia"/> do eixo X, com as medidas FIXAS aqui: as duas pontas do Y usam
/// esta peça sem nada mudado na cena. Para mudar uma medida, mude a propriedade dela neste arquivo.
///
/// Origem local: x = 0 na face de apoio (encostada na parede do suporte_retorno_correia_y), y = 0 no
/// plano de simetria (o eixo dos rolamentos e o parafuso do rasgo ficam nele), z = 0 na face de baixo
/// do rolamento de baixo. O C abre para +X.
///
/// Corte visto de cima (X →):
///   | EspessuraFundo (9) | FolgaRolamento (16) | AlemEixo (6) |
///   face de apoio        face interna do C     eixo           ponta dos braços
///
/// - fundo: bloco maciço da face de apoio até a face interna do C, na altura toda. Leva o M3 do rasgo
///   (fixacao_parede), com a porca entrando por um canal na lateral (+Y).
/// - braco_superior e braco_inferior: os braços do C, com VaoRolamentos entre os colares.
/// - colar_inferior e colar_superior: anéis em que só o anel interno dos rolamentos encosta.
/// - fixacao_eixo: o parafuso dos rolamentos (M4) desce do topo do braço de cima e rosqueia na porca
///   alojada por baixo do braço de baixo.
/// O contorno externo leva um chanfro de RaioChanfro, cortado antes dos colares e dos furos.
/// </summary>
public sealed record SuporteRolamentosCorreiaY() : Part
{
    /// <summary>Altura livre para os rolamentos/polia, de colar a colar (Z). Entre as faces dos braços dá 2 × FolgaColar a mais.</summary>
    public double VaoRolamentos => 20.0;

    /// <summary>Do eixo dos rolamentos até a face interna do C (XY): a folga para o diâmetro da polia com a correia.</summary>
    public double FolgaRolamento => 16.0;

    /// <summary>
    /// Espessura maciça do fundo do C, da face de apoio até a face interna. Leva a porca do M3 do rasgo:
    /// 3 mm de material até o alojamento (ProfundidadePorcaParede − 1), 3 mm de alojamento e 3 mm até o vão do C.
    /// </summary>
    public double EspessuraFundo => 9.0;

    /// <summary>Quanto os braços passam do eixo, para o lado da abertura.</summary>
    public double AlemEixo => 6.0;

    /// <summary>Largura da peça (Y local).</summary>
    public double Largura => 10.0;

    public double EspessuraBraco => 5.5;

    /// <summary>Altura dos colares em que o anel interno dos rolamentos encosta.</summary>
    public double FolgaColar => 0.5;

    public double DiametroColar => 5.0;

    public double RaioChanfro => 1.0;

    /// <summary>Da face de apoio até onde começa o alojamento da porca do M3 do rasgo (mais 1 mm fora da peça).</summary>
    public double ProfundidadePorcaParede => 4.0;

    public Rosca RoscaEixo => Rosca.M4;

    /// <summary>Da face de apoio ao eixo dos rolamentos. Calculada: o eixo fica onde o fundo e a folga mandam.</summary>
    public double DistanciaEixo => EspessuraFundo + FolgaRolamento;

    /// <summary>Centro da face de baixo do rolamento de baixo (a origem dele).</summary>
    public Point3D EixoRolamentos => new Point3D(DistanciaEixo, 0, 0);

    public double ZTopo => VaoRolamentos + FolgaColar + EspessuraBraco;

    public double ZBase => -FolgaColar - EspessuraBraco;

    /// <summary>Eixo do parafuso do rasgo: o meio da altura da peça.</summary>
    public double AlturaParafuso => (ZBase + ZTopo) / 2.0;

    protected override IFeature Build()
    {
        var metadeY = Largura / 2.0;
        var xFaceInterna = EspessuraFundo;
        var xPontaBracos = DistanciaEixo + AlemEixo;
        var zTopo = ZTopo;
        var zBase = ZBase;
        var zBracoSuperior = VaoRolamentos + FolgaColar;

        // Fundo do C, na altura toda. Os braços entram 1 mm nele para a união não ter faces coplanares.
        var fundo = new BoxFeature(new Point3D(0, -metadeY, zBase), xFaceInterna, Largura, zTopo - zBase) { Name = "fundo" };
        var braco_superior = new BoxFeature(new Point3D(xFaceInterna - 1.0, -metadeY, zBracoSuperior), xPontaBracos - xFaceInterna + 1.0, Largura, EspessuraBraco) { Name = "braco_superior" };
        var braco_inferior = new BoxFeature(new Point3D(xFaceInterna - 1.0, -metadeY, zBase), xPontaBracos - xFaceInterna + 1.0, Largura, EspessuraBraco) { Name = "braco_inferior" };

        var envelope = BooleanFeature.CreateUnion("envelope", fundo, braco_superior, braco_inferior);

        // Chanfro só no envelope de caixas: os colares e as bocas dos furos ficam vivos.
        var envelope_chanfrado = new FilletFeature("envelope_chanfrado", envelope, RaioChanfro)
        {
            Mode = FilletMode.Bevel,
            ApplyToAllEdges = true
        };

        // Colares nas faces internas dos braços; entram 0,5 mm no braço para a união não ter tampas coplanares.
        var colar_inferior = new CylinderFeature(new Point3D(DistanciaEixo, 0, -FolgaColar - 0.5), Vector3D.UnitZ, DiametroColar / 2.0, FolgaColar + 0.5) { Name = "colar_inferior" };
        var colar_superior = new CylinderFeature(new Point3D(DistanciaEixo, 0, VaoRolamentos), Vector3D.UnitZ, DiametroColar / 2.0, FolgaColar + 0.5) { Name = "colar_superior" };
        var corpo = BooleanFeature.CreateUnion("corpo", envelope_chanfrado, colar_inferior, colar_superior);

        // Parafuso dos rolamentos: a porca entra pela face de baixo (alojamento de 4 mm a partir de 1 mm fora dela)
        // e o furo sai 1 mm acima do topo do braço de cima.
        const double espessuraAlojamentoEixo = 4.0;
        var zEntradaEixo = zBase - 1.0;
        var fixacao_eixo = new FuroPorca(new Point3D(DistanciaEixo, 0, zEntradaEixo), Vector3D.UnitZ, 0,
            Rosca: RoscaEixo, Insercao: InsercaoPorca.Axial, EspessuraAlojamento: espessuraAlojamentoEixo,
            FolgaDepois: zTopo + 1.0 - (zEntradaEixo + espessuraAlojamentoEixo)).Instance("fixacao_eixo");

        // M3 do rasgo: entra pela face de apoio e a porca entra por um canal na lateral do fundo (+Y), que
        // atravessa a peça de fora a fora, no plano do furo. O furo sai 1 mm além da face interna, no vão do C.
        const double espessuraAlojamentoParede = 3.0;
        var xFimFuroParede = xFaceInterna + 1.0;
        var fixacao_parede = new FuroPorca(new Point3D(-1.0, 0, AlturaParafuso), Vector3D.UnitX, ProfundidadePorcaParede,
            Insercao: InsercaoPorca.Lateral, EixoHexagono: Vector3D.UnitY, ComprimentoCanal: metadeY + 1.0,
            EspessuraAlojamento: espessuraAlojamentoParede,
            FolgaDepois: xFimFuroParede - (-1.0 + ProfundidadePorcaParede + espessuraAlojamentoParede)).Instance("fixacao_parede");

        return BooleanFeature.CreateDifference("suporte_rolamentos_correia_y", corpo, fixacao_eixo, fixacao_parede);
    }
}
