/// <summary>
/// Sólido base do <see cref="BlocoFixacaoBelt"/> reduzido à METADE ESQUERDA do vão da correia
/// (a composição completa é espelhada em X), SEM porca_pressao_correia, rebaixo_fixacao,
/// furo_fixacao, rebaixo_frontal nem rebaixo_inferior.
/// A correia entra pelo canal, dá a volta no parafuso (furo_parafuso) dentro do alojamento e volta
/// pelo mesmo canal, contornando o nariz: as duas camadas ficam espremidas em LarguraCanal.
/// O vazio é montado no espaço das ferramentas: lingueta (material acima do canal) e nariz (raio de
/// dobra do fio de retorno) são TIRADOS do vao_correia antes de ele cortar o corpo — nenhum sólido
/// é unido dentro do vazio. Todas as posições derivam do parafuso:
/// - a parede de entrada (y = ParafusoY − RaioAlojamento) é tangente ao alojamento, para o fio de
///   entrada ir reto até enrolar no parafuso;
/// - o nariz é tangente ao teto do canal e fica a FolgaNariz da superfície do parafuso. FolgaNariz
///   nunca pode ser igual a RaioAlojamento − RaioParafuso (nariz tangente ao alojamento); abaixo
///   disso o alojamento corta o nariz e a folga real ali passa a ser a do alojamento.
/// As ferramentas passam 1 mm do topo (z = AlturaCorpo) e a lingueta/nariz sobram 1 mm para todo
/// lado que não é face de trabalho, para não haver faces coplanares nas booleanas.
/// O vão da correia vai de BaseVaoZ = 7 ao topo (10 mm, para a correia de 10 mm).
/// Origem local = base do bloco_fixacao_mgn9 (local = mundo - (0, 0, 26)), igual ao BlocoFixacaoBelt.
/// </summary>
public sealed record BlocoFicacaoBeltVaoCorreia(
    double LarguraCorpoX = 20.5,
    double ComprimentoCorpoY = 14.5,
    double AlturaCorpo = 17.0,
    double BaseCorpoX = -20.615334,
    double BaseCorpoY = 10.5,
    double BaseCorpoZ = 4.0,
    double BaseVaoX = -22.61533,
    double BaseVaoZ = 7.0,
    double ParafusoX = -5.61533,
    double ParafusoY = 18.75,
    double RaioParafuso = 1.6,
    double RaioAlojamento = 3.25,  // parafuso + uma volta de correia + folga
    double LarguraCanal = 2.45,    // duas camadas de correia, apertadas
    double RaioNariz = 1.3,        // raio de dobra do fio de retorno
    double FolgaNariz = 1.43) : Part // nunca igual a RaioAlojamento − RaioParafuso (tangência)
{
    protected override IFeature Build()
    {
        // As ferramentas passam 1 mm acima do topo: nada termina coplanar com z = AlturaCorpo.
        var topoFerramenta = AlturaCorpo + 1.0;
        var alturaFerramenta = topoFerramenta - BaseVaoZ;

        // Parede de entrada tangente ao alojamento: o fio de entrada vai reto até enrolar no parafuso.
        var yParede = ParafusoY - RaioAlojamento;
        var yTetoCanal = yParede + LarguraCanal;
        // Nariz tangente ao teto do canal, a FolgaNariz da superfície do parafuso.
        var yNariz = yTetoCanal + RaioNariz;
        var dyNariz = yNariz - ParafusoY;
        var xNariz = ParafusoX - Math.Sqrt(Math.Pow(RaioParafuso + FolgaNariz + RaioNariz, 2) - dyNariz * dyNariz);

        var corpo = new BoxFeature(new Point3D(BaseCorpoX, BaseCorpoY, BaseCorpoZ), LarguraCorpoX, ComprimentoCorpoY, AlturaCorpo - BaseCorpoZ) { Name = "corpo" };

        var vao_correia = new BoxFeature(new Point3D(BaseVaoX, yParede, BaseVaoZ), ParafusoX - BaseVaoX, yNariz - yParede, alturaFerramenta) { Name = "vao_correia", IsTransientTool = true };
        // Material acima do canal estreito; sobra 1 mm para todo lado que não é face de trabalho.
        var lingueta = new BoxFeature(new Point3D(BaseVaoX - 1.0, yTetoCanal, BaseVaoZ - 1.0), xNariz - BaseVaoX + 1.0, yNariz - yTetoCanal + 1.0, alturaFerramenta + 2.0) { Name = "lingueta", IsTransientTool = true };
        var nariz = new CylinderFeature(new Point3D(xNariz, yNariz, BaseVaoZ - 1.0), Vector3D.UnitZ, RaioNariz, alturaFerramenta + 2.0) { Name = "nariz", IsTransientTool = true };
        // Espaço da volta da correia em torno do parafuso.
        var alojamento = new CylinderFeature(new Point3D(ParafusoX, ParafusoY, BaseVaoZ), Vector3D.UnitZ, RaioAlojamento, alturaFerramenta) { Name = "alojamento", IsTransientTool = true };
        // Furo passante do parafuso em que a correia dá a volta.
        var furo_parafuso = new CylinderFeature(new Point3D(ParafusoX, ParafusoY, BaseCorpoZ - 1.0), Vector3D.UnitZ, RaioParafuso, AlturaCorpo - BaseCorpoZ + 2.0) { Name = "furo_parafuso", IsTransientTool = true };

        // O vazio é montado no espaço das ferramentas: lingueta e nariz saem do vão, uma por booleana.
        var vao_sem_lingueta = BooleanFeature.CreateDifference("vao_sem_lingueta", vao_correia, lingueta);
        var canal_correia = BooleanFeature.CreateDifference("canal_correia", vao_sem_lingueta, nariz);

        var corpo_com_canal = BooleanFeature.CreateDifference("corpo_com_canal", corpo, canal_correia);
        var bloco_ficacao_belt_vao_correia = BooleanFeature.CreateDifference("bloco_ficacao_belt_vao_correia", corpo_com_canal, alojamento, furo_parafuso);

        var bloco_ficacao_belt_vao_correia_chanfrado = new FilletFeature(name: "bloco_ficacao_belt_vao_correia_chanfrado", baseFeature: bloco_ficacao_belt_vao_correia, radius: 1)
        {
            Mode = FilletMode.Bevel,
            ApplyToAllEdges = false,
            Faces = { new FaceReference(bloco_ficacao_belt_vao_correia, -1, new Vector3D(0, 0, 1), new Point3D(BaseCorpoX + LarguraCorpoX, BaseCorpoY + ComprimentoCorpoY, AlturaCorpo), "CustomFace") }
        };
        return bloco_ficacao_belt_vao_correia_chanfrado;
    }
}
