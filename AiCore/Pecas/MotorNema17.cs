/// <summary>
/// Motor passo a passo NEMA 17, simplificado: corpo prismático quadrado (lado 42,3 mm,
/// padrão NEMA 17), saliência circular de centragem (boss) na face frontal, eixo cilíndrico
/// saliente e 4 furos de fixação M3 (padrão 31 x 31 mm), rosqueados (cegos) a partir da face
/// frontal.
/// A origem (0, 0, 0) local fica na base traseira do corpo, centralizada em X e Y;
/// o eixo (Z) cresce no sentido da face frontal (onde está o eixo saliente).
/// </summary>
public sealed record MotorNema17(
    double Comprimento = 40,
    double Lado = 42.3,
    double DiametroEixo = 5,
    double ComprimentoEixo = 24,
    double DiametroBoss = 22,
    double AlturaBoss = 2,
    double PassoFuros = 31,
    double DiametroFuroFixacao = 3.2,
    double ProfundidadeFuroFixacao = 6,
    double RaioChanfroLateral = 3.0) : Part
{
    protected override IFeature Build()
    {
        var baseCorner = new Point3D(-Lado / 2, -Lado / 2, 0);
        var corpoBase = new BoxFeature(baseCorner, Lado, Lado, Comprimento) { Name = "corpo_base" };

        IFeature corpo = corpoBase;
        if (RaioChanfroLateral > 0.001)
        {
            var chanfro = new FilletFeature("corpo_chanfrado", corpoBase, RaioChanfroLateral) { Mode = FilletMode.Bevel };
            foreach (var edge in new[] { EdgeType.FrontLeft, EdgeType.FrontRight, EdgeType.BackLeft, EdgeType.BackRight })
            {
                chanfro.AddEdge(new EdgeReference(corpoBase, edge));
            }
            corpo = chanfro;
        }

        var boss = new CylinderFeature(new Point3D(0, 0, Comprimento), Vector3D.UnitZ, DiametroBoss / 2, AlturaBoss)
        {
            Name = "boss_centragem"
        };

        var eixo = new CylinderFeature(new Point3D(0, 0, Comprimento + AlturaBoss), Vector3D.UnitZ, DiametroEixo / 2, ComprimentoEixo)
        {
            Name = "eixo"
        };

        var corpoComEixo = BooleanFeature.CreateUnion("corpo_com_eixo", corpo, boss, eixo);

        var furos = new List<IFeature>();
        var i = 0;
        foreach (var sinalX in new[] { -1.0, 1.0 })
        {
            foreach (var sinalY in new[] { -1.0, 1.0 })
            {
                var x = sinalX * PassoFuros / 2;
                var y = sinalY * PassoFuros / 2;

                var furo = new CylinderFeature(
                    new Point3D(x, y, Comprimento - ProfundidadeFuroFixacao),
                    Vector3D.UnitZ,
                    DiametroFuroFixacao / 2,
                    ProfundidadeFuroFixacao)
                {
                    Name = $"furo_fixacao_{i}",
                    IsTransientTool = true
                };
                furos.Add(furo);
                i++;
            }
        }

        return BooleanFeature.CreateDifference("motor_nema17", corpoComEixo, furos.ToArray());
    }
}
