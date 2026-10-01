/// <summary>
/// Hotend estilo E3D V6 / J-head (all-metal), geometria simplificada em pilha de cilindros +
/// bloco aquecedor, para uso como referência de envelope ao projetar a peça impressa que o
/// prende ao carrinho. Medidas oficiais E3D v6: comprimento total (topo do heatsink até a
/// ponta do bico) 62,3 mm = 46,8 mm "coldside" (heatsink + pescoço/heatbreak) + 15,5 mm
/// "hotside" (bloco aquecedor + bico); diâmetro do heatsink 22,3 mm. A divisão interna do
/// coldside (heatsink x pescoço) e o tamanho do bloco (16 x 20 x 11,7 mm) são valores típicos
/// de mercado, não exatos de datasheet. Sem rosca/aletas/fan shroud — só o envelope.
/// Eixo do hotend = eixo Z local; origem na ponta do bico (0, 0, 0), peça construída para cima.
/// </summary>
public sealed record HotendV6(
    double DiametroBico = 6,
    double AlturaBico = 3.8,
    double LarguraBloco = 16,
    double ProfundidadeBloco = 20,
    double AlturaBloco = 11.7,
    double DiametroPescoco = 6,
    double AlturaPescoco = 24.8,
    double DiametroHeatsink = 22.3,
    double AlturaHeatsink = 22) : Part
{
    protected override IFeature Build()
    {
        var zBico = 0.0;
        var zBloco = zBico + AlturaBico;
        var zPescoco = zBloco + AlturaBloco;
        var zHeatsink = zPescoco + AlturaPescoco;

        var bico = new CylinderFeature(new Point3D(0, 0, zBico), Vector3D.UnitZ, DiametroBico / 2, AlturaBico) { Name = "bico" };
        var bloco = new BoxFeature(new Point3D(-LarguraBloco / 2, -ProfundidadeBloco / 2, zBloco), LarguraBloco, ProfundidadeBloco, AlturaBloco) { Name = "bloco_aquecedor" };
        var pescoco = new CylinderFeature(new Point3D(0, 0, zPescoco), Vector3D.UnitZ, DiametroPescoco / 2, AlturaPescoco) { Name = "pescoco" };
        var heatsink = new CylinderFeature(new Point3D(0, 0, zHeatsink), Vector3D.UnitZ, DiametroHeatsink / 2, AlturaHeatsink) { Name = "heatsink" };

        return BooleanFeature.CreateUnion("hotend_v6", bico, bloco, pescoco, heatsink);
    }
}
