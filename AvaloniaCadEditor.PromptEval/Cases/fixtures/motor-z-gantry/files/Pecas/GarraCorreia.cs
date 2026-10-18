/// <summary>
/// Garra de correia reutilizável: bloco em que a correia (10 mm) entra por um canal, dá a volta num
/// parafuso vertical dentro do alojamento e volta pelo mesmo canal, contornando o nariz — as duas
/// camadas ficam espremidas em LarguraCanal. Uma garra em cada ponta: a metade do lado −X é
/// modelada e espelhada pelo plano x = 0, então a correia entra pelas duas pontas.
/// Origem local: x = 0 no plano de simetria, y = 0 na face de apoio (a garra cresce em +Y, para
/// fora), z = 0 na base; o vão abre na face de cima (z = Altura).
/// O vazio é montado no espaço das ferramentas: lingueta (material acima do canal) e nariz (raio de
/// dobra do fio de retorno) são TIRADOS do vao_correia antes de ele cortar o corpo — nenhum sólido
/// é unido dentro do vazio. Todas as posições derivam do parafuso:
/// - a parede de entrada (y = ParafusoY − RaioAlojamento) é tangente ao alojamento, para o fio de
///   entrada ir reto até enrolar no parafuso;
/// - o nariz é tangente ao teto do canal e fica a FolgaNariz da superfície do parafuso. FolgaNariz
///   nunca pode ser igual a RaioAlojamento − RaioParafuso (nariz tangente ao alojamento); abaixo
///   disso o alojamento corta o nariz e a folga real ali passa a ser a do alojamento.
/// As ferramentas passam 1 mm do topo e a lingueta/nariz sobram 1 mm para todo lado que não é face
/// de trabalho, para não haver faces coplanares nas booleanas.
/// A boca do canal, nas pontas, leva um chanfro de ChanfroEntrada (funil) nas duas paredes e no
/// fundo, para guiar a correia na entrada; a boca de cima do alojamento leva um cone de
/// ChanfroAlojamento, que chanfra também a parede do canal junto dele.
/// </summary>
public sealed record GarraCorreia(
    double Comprimento = 41.0,       // X, as duas garras
    double Largura = 14.5,           // Y, da face de apoio para fora
    double Altura = 13.0,
    double AlturaCorreia = 10.0,     // profundidade do vão a partir do topo
    double DistanciaParafuso = 5.5,  // de cada parafuso ao plano de simetria
    double ParafusoY = 8.25,
    double RaioParafuso = 1.6,
    double RaioAlojamento = 3.25,    // parafuso + uma volta de correia + folga
    double LarguraCanal = 2.45,      // duas camadas de correia, apertadas
    double RaioNariz = 1.3,          // raio de dobra do fio de retorno
    double FolgaNariz = 1.43,        // nunca igual a RaioAlojamento − RaioParafuso (tangência)
    double ChanfroEntrada = 1.0,     // funil na boca do canal, por onde a correia entra
    double ChanfroAlojamento = 1.0) : Part // cone na boca de cima do alojamento e da parede do canal
{
    /// <summary>Face do canal do lado da face de apoio: entre y = 0 e ela a garra é parede maciça.</summary>
    public double YParede => ParafusoY - RaioAlojamento;

    /// <summary>Face do canal do lado do nariz: a outra camada da correia encosta nela.</summary>
    public double YTetoCanal => YParede + LarguraCanal;

    /// <summary>Centro do nariz da garra do lado −X: tangente ao teto do canal, a FolgaNariz do parafuso.</summary>
    public double NarizY => YTetoCanal + RaioNariz;
    public double NarizX => -DistanciaParafuso - Math.Sqrt(Math.Pow(RaioParafuso + FolgaNariz + RaioNariz, 2) - Math.Pow(NarizY - ParafusoY, 2));

    protected override IFeature Build()
    {
        var metadeX = Comprimento / 2.0;
        var xParafuso = -DistanciaParafuso;
        // O vão começa 2 mm fora da ponta e as ferramentas passam 1 mm do topo.
        var xEntrada = -metadeX - 2.0;
        var zVao = Altura - AlturaCorreia;
        var alturaFerramenta = Altura + 1.0 - zVao;

        // Parede de entrada tangente ao alojamento: o fio de entrada vai reto até enrolar no parafuso.
        var yParede = YParede;
        var yTetoCanal = YTetoCanal;
        // Nariz tangente ao teto do canal, a FolgaNariz da superfície do parafuso.
        var yNariz = NarizY;
        var xNariz = NarizX;

        var corpo = new BoxFeature(new Point3D(-metadeX, 0, 0), metadeX, Largura, Altura) { Name = "corpo" };

        var vao_correia = new BoxFeature(new Point3D(xEntrada, yParede, zVao), xParafuso - xEntrada, yNariz - yParede, alturaFerramenta) { Name = "vao_correia", IsTransientTool = true };
        // Material acima do canal estreito; sobra 1 mm para todo lado que não é face de trabalho.
        var lingueta = new BoxFeature(new Point3D(xEntrada - 1.0, yTetoCanal, zVao - 1.0), xNariz - xEntrada + 1.0, yNariz - yTetoCanal + 1.0, alturaFerramenta + 2.0) { Name = "lingueta", IsTransientTool = true };
        var nariz = new CylinderFeature(new Point3D(xNariz, yNariz, zVao - 1.0), Vector3D.UnitZ, RaioNariz, alturaFerramenta + 2.0) { Name = "nariz", IsTransientTool = true };
        // Espaço da volta da correia em torno do parafuso.
        var alojamento = new CylinderFeature(new Point3D(xParafuso, ParafusoY, zVao), Vector3D.UnitZ, RaioAlojamento, alturaFerramenta) { Name = "alojamento", IsTransientTool = true };
        // Furo passante do parafuso em que a correia dá a volta.
        var furo_parafuso = new CylinderFeature(new Point3D(xParafuso, ParafusoY, -1.0), Vector3D.UnitZ, RaioParafuso, Altura + 2.0) { Name = "furo_parafuso", IsTransientTool = true };

        // O vazio é montado no espaço das ferramentas: lingueta e nariz saem do vão, uma por booleana.
        var vao_sem_lingueta = BooleanFeature.CreateDifference("vao_sem_lingueta", vao_correia, lingueta);
        var canal_correia = BooleanFeature.CreateDifference("canal_correia", vao_sem_lingueta, nariz);

        var corpo_com_canal = BooleanFeature.CreateDifference("corpo_com_canal", corpo, canal_correia);
        var metade_furada = BooleanFeature.CreateDifference("metade_furada", corpo_com_canal, alojamento, furo_parafuso);

        // Entrada da correia: chanfro de ChanfroEntrada a 45° nas três arestas da boca do canal (as duas
        // paredes e o fundo), um funil que é a interseção de um prisma na planta (abre em Y) com um no
        // perfil (desce em Z). Para dentro, o funil continua afunilando 0,5 mm além do chanfro: ali ele
        // já é mais estreito que o canal, então as faces inclinadas CRUZAM as paredes do canal em vez de
        // coincidir com elas. Para fora, sobra além da ponta; os dois prismas terminam em pontos
        // diferentes para não terem faces coplanares entre si.
        IFeature metade = metade_furada;

        // Bevel da boca do alojamento: cone a 45° que abre ChanfroAlojamento na face de cima. Como a parede
        // do canal é tangente ao alojamento, o mesmo cone chanfra a parede do canal junto dele (e a ponta
        // do nariz). Passa 1 mm acima do topo e, para baixo, continua afunilando 0,5 mm para dentro do
        // alojamento, para CRUZAR a parede do furo em vez de encostar nela.
        if (ChanfroAlojamento > 0.001)
        {
            var bevel_alojamento = new ConeFeature(new Point3D(xParafuso, ParafusoY, Altura - ChanfroAlojamento - 0.5), Vector3D.UnitZ,
                RaioAlojamento - 0.5, RaioAlojamento + ChanfroAlojamento + 1.0, ChanfroAlojamento + 1.5) { Name = "bevel_alojamento", IsTransientTool = true };
            var metade_com_bevel = BooleanFeature.CreateDifference("metade_com_bevel", metade, bevel_alojamento);
            metade = metade_com_bevel;
        }

        if (ChanfroEntrada > 0.001)
        {
            var xBoca = -metadeX;
            var xFimFunil = xBoca + ChanfroEntrada + 0.5;
            var zBaseFunil = zVao - ChanfroEntrada - 2.0;

            var funil_planta = new PrismFeature(new[]
            {
                new Point3D(xBoca - 1.0, yParede - ChanfroEntrada - 1.0, 0),
                new Point3D(xFimFunil, yParede + 0.5, 0),
                new Point3D(xFimFunil, yTetoCanal - 0.5, 0),
                new Point3D(xBoca - 1.0, yTetoCanal + ChanfroEntrada + 1.0, 0)
            }, Altura + 2.0 - zBaseFunil, new WorkPlane(new Point3D(0, 0, zBaseFunil), Vector3D.UnitZ, Vector3D.UnitX), "funil_planta");
            // Plano XZ com normal −Y: u = X e v = Z; atravessa a garra inteira em Y.
            var funil_perfil = new PrismFeature(new[]
            {
                new Point3D(xBoca - 2.0, zVao - ChanfroEntrada - 2.0, 0),
                new Point3D(xFimFunil + 0.3, zVao + 0.8, 0),
                new Point3D(xFimFunil + 0.3, Altura + 1.0, 0),
                new Point3D(xBoca - 2.0, Altura + 1.0, 0)
            }, Largura + 2.0, new WorkPlane(new Point3D(0, Largura + 1.0, 0), new Vector3D(0, -1, 0), Vector3D.UnitX), "funil_perfil");
            var funil_entrada = BooleanFeature.CreateIntersection("funil_entrada", funil_planta, funil_perfil);

            var metade_com_entrada = BooleanFeature.CreateDifference("metade_com_entrada", metade, funil_entrada);
            metade = metade_com_entrada;
        }

        // Garra do lado +X: a mesma metade espelhada pelo plano de simetria.
        var metade_espelhada = metade.Transformed(Placement.MirrorX(atX: 0), name: "metade_espelhada");

        return BooleanFeature.CreateUnion("garra_correia", metade, metade_espelhada);
    }
}
