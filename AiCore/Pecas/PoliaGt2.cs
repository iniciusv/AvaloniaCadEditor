/// <summary>
/// Polia dentada GT2 (passo 2 mm) de eixo de motor, simplificada: cubo opcional, flange, faixa
/// dentada e flange, empilhados em Z, com furo central para o eixo (sem os furos dos parafusos
/// de trava).
/// Origem local (0, 0, 0) no centro da face de baixo (a do cubo; sem cubo, a da flange inferior);
/// o eixo da polia é +Z. Em Z: cubo [0, AlturaCubo], flange inferior, dentes (LarguraDentes) e
/// flange superior até Altura.
/// Diâmetro primitivo = Dentes × 2 / π (24 dentes → 15,28 mm); o externo dos dentes fica 0,508 mm
/// menor (a linha primitiva da correia GT2 fica 0,254 mm fora do dente). Os vales são trapézios de
/// 0,75 mm de profundidade, e o contorno inteiro é UM prisma, calculado ponto a ponto.
/// </summary>
public sealed record PoliaGt2(
    [System.ComponentModel.DataAnnotations.Range(10, 80)] int Dentes = 24,
    double Altura = 10,
    double AlturaCubo = 0,
    double EspessuraFlange = 1,
    double DiametroFlange = 18.5,
    double DiametroCubo = 16,
    double DiametroFuro = 5) : Part
{
    public double DiametroPrimitivo => Dentes * 2.0 / System.Math.PI;
    public double DiametroExterno => DiametroPrimitivo - 0.508;
    public double LarguraDentes => Altura - AlturaCubo - 2 * EspessuraFlange;

    protected override IFeature Build()
    {
        if (LarguraDentes <= 0)
        {
            throw new System.ArgumentOutOfRangeException(nameof(Altura), Altura,
                $"PoliaGt2: Altura precisa passar de AlturaCubo + 2 × EspessuraFlange ({AlturaCubo + 2 * EspessuraFlange:0.###} mm).");
        }

        // Cada trecho entra 0,5 mm no vizinho mais largo, para a união não ter faces coplanares.
        const double sobreposicao = 0.5;
        const double profundidadeVale = 0.75;
        const double larguraBocaVale = 1.2;
        const double larguraFundoVale = 0.6;

        var raioExterno = DiametroExterno / 2;
        var raioFundo = raioExterno - profundidadeVale;
        var passoAngular = 2 * System.Math.PI / Dentes;
        var meiaBoca = larguraBocaVale / 2 / raioExterno;
        var meioFundo = larguraFundoVale / 2 / raioFundo;
        Point3D Polar(double raio, double angulo) => new Point3D(raio * System.Math.Cos(angulo), raio * System.Math.Sin(angulo), 0);

        var perfilDentes = new System.Collections.Generic.List<Point3D>();
        for (var i = 0; i < Dentes; i++)
        {
            var angulo = i * passoAngular;
            perfilDentes.Add(Polar(raioExterno, angulo - meiaBoca));
            perfilDentes.Add(Polar(raioFundo, angulo - meioFundo));
            perfilDentes.Add(Polar(raioFundo, angulo + meioFundo));
            perfilDentes.Add(Polar(raioExterno, angulo + meiaBoca));
        }

        var zDentes = AlturaCubo + EspessuraFlange;
        var planoDentes = new WorkPlane(new Point3D(0, 0, zDentes - sobreposicao), Vector3D.UnitZ, Vector3D.UnitX);

        var dentes = new PrismFeature(perfilDentes, LarguraDentes + 2 * sobreposicao, planoDentes, "dentes");
        var flange_inferior = new CylinderFeature(new Point3D(0, 0, AlturaCubo), Vector3D.UnitZ, DiametroFlange / 2, EspessuraFlange) { Name = "flange_inferior" };
        var flange_superior = new CylinderFeature(new Point3D(0, 0, Altura - EspessuraFlange), Vector3D.UnitZ, DiametroFlange / 2, EspessuraFlange) { Name = "flange_superior" };
        var polia_sem_cubo = BooleanFeature.CreateUnion("polia_sem_cubo", dentes, flange_inferior, flange_superior);

        IFeature corpo = polia_sem_cubo;
        if (AlturaCubo > 0.001)
        {
            var cubo = new CylinderFeature(Point3D.Origin, Vector3D.UnitZ, DiametroCubo / 2, AlturaCubo + sobreposicao) { Name = "cubo" };
            var polia_com_cubo = BooleanFeature.CreateUnion("polia_com_cubo", polia_sem_cubo, cubo);
            corpo = polia_com_cubo;
        }

        var furo = new CylinderFeature(new Point3D(0, 0, -1), Vector3D.UnitZ, DiametroFuro / 2, Altura + 2) { Name = "furo", IsTransientTool = true };

        return BooleanFeature.CreateDifference("polia_gt2", corpo, furo);
    }
}
