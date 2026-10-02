public sealed record HotendV6(
    double DiametroBico = 6,
    double AlturaBico = 3.8,
    double LarguraBloco = 16,
    double ProfundidadeBloco = 20,
    double AlturaBloco = 11.7,
    double DiametroPescoco = 6,
    double AlturaPescoco = 4.8,
    double DiametroHeatsink = 22.3,
    double AlturaHeatsink = 22,
    double DiametroColar = 16,
    double AlturaColar = 10,
    double DiametroRoscaM7 = 7,
    double ProfundidadeRoscaM7 = 10) : Part
{
    protected override IFeature Build()
    {
        var zBico = 0.0;
        var zBloco = zBico + AlturaBico;
        var zPescoco = zBloco + AlturaBloco;
        var zHeatsink = zPescoco + AlturaPescoco;
        var zColar = zHeatsink + AlturaHeatsink;
        var zTopo = zColar + AlturaColar;

        var bico = new CylinderFeature(new Point3D(0, 0, zBico), Vector3D.UnitZ, DiametroBico / 2, AlturaBico) { Name = "bico" };
        var bloco = new BoxFeature(new Point3D(-LarguraBloco / 2, -ProfundidadeBloco / 2, zBloco), LarguraBloco, ProfundidadeBloco, AlturaBloco) { Name = "bloco_aquecedor" };
        var pescoco = new CylinderFeature(new Point3D(0, 0, zPescoco), Vector3D.UnitZ, DiametroPescoco / 2, AlturaPescoco) { Name = "pescoco" };
        var heatsink = new CylinderFeature(new Point3D(0, 0, zHeatsink), Vector3D.UnitZ, DiametroHeatsink / 2, AlturaHeatsink) { Name = "heatsink" };
        var colar = new CylinderFeature(new Point3D(0, 0, zColar), Vector3D.UnitZ, DiametroColar / 2, AlturaColar) { Name = "colar_fixacao" };

        var corpo = BooleanFeature.CreateUnion("corpo_hotend", bico, bloco, pescoco, heatsink, colar);

        var furoM7 = new CylinderFeature(
            new Point3D(0, 0, zTopo - ProfundidadeRoscaM7),
            Vector3D.UnitZ,
            DiametroRoscaM7 / 2,
            ProfundidadeRoscaM7)
        {
            Name = "furo_m7",
            IsTransientTool = true
        };

        return BooleanFeature.CreateDifference("hotend_v6", corpo, furoM7);
    }
}