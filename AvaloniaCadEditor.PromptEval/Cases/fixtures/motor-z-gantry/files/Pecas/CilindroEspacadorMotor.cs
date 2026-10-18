/// <summary>
/// Espaçador cilíndrico que se apoia no topo do <see cref="SuporteMotorNema17"/>, com um furo
/// passante de MESMO diâmetro dos furos de fixação do motor, alinhado a um deles (o furo_motor_1
/// do suporte). O furo é DESLOCADO em relação ao eixo do corpo (furo fora de centro): fica no
/// eixo do parafuso, enquanto o material cresce para o lado oposto.
/// No TOPO há um sólido SEXTAVADO (como uma cabeça de porca), CONCÊNTRICO com o cilindro (mesmo
/// eixo Z, em x = 0), que serve de pegada para girar a peça inteira (com chave ou manualmente).
/// O furo vertical atravessa o cilindro E o sextavado.
/// Diâmetro externo padrão: 6 mm. Como o furo de Ø3,4 deixa pouca parede, o deslocamento precisa
/// ser pequeno: o limite é (DiametroExterno - DiametroFuro) / 2 = 1,3 mm (parede zero); o padrão
/// de 0,8 mm ainda deixa ~0,5 mm de parede no lado mais fino.
/// A origem (0, 0, 0) local fica no centro da face de BAIXO do cilindro (face de encosto na
/// placa); o furo é deslocado DeslocamentoFuro no sentido do eixo X local. O sextavado começa em
/// Z = Altura e o furo passa do fundo do cilindro até o topo do sextavado.
/// </summary>
public sealed record CilindroEspacadorMotor(
    double Altura = 15,
    double DiametroExterno = 6,
    double DiametroFuro = 3.4,
    double DeslocamentoFuro = 0.8,
    double FolgaFuro = 1.0,
    double LadoSextavado = 10.0,
    double AlturaSextavado = 5.0) : Part
{
    protected override IFeature Build()
    {
        // Cilindro base, apoiado na placa do suporte.
        var corpo = new CylinderFeature(new Point3D(0, 0, 0), Vector3D.UnitZ, DiametroExterno / 2, Altura)
        {
            Name = "corpo"
        };

        // Sólido sextavado no topo (pegada para girar), CONCÊNTRICO com o cilindro (mesmo eixo).
        // LadoSextavado é a medida ENTRE FACES (apotema = centro → face).
        var apotema_sextavado = LadoSextavado / 2;
        var raio_vertice_sextavado = apotema_sextavado / (System.Math.Sqrt(3.0) / 2.0);
        var plano_sextavado = new WorkPlane(
            origin: new Point3D(0, 0, Altura),
            normal: Vector3D.UnitZ,
            xAxis: Vector3D.UnitX);
        var vertices_sextavado = new[]
        {
            new Point3D(raio_vertice_sextavado, 0, 0),
            new Point3D(raio_vertice_sextavado / 2, apotema_sextavado, 0),
            new Point3D(-raio_vertice_sextavado / 2, apotema_sextavado, 0),
            new Point3D(-raio_vertice_sextavado, 0, 0),
            new Point3D(-raio_vertice_sextavado / 2, -apotema_sextavado, 0),
            new Point3D(raio_vertice_sextavado / 2, -apotema_sextavado, 0)
        };
        var sextavado = new PrismFeature(
            vertices_sextavado,
            height: AlturaSextavado,
            plane: plano_sextavado,
            name: "sextavado");

        // Furo passante pelo cilindro E pelo sextavado (1 mm de folga em cada ponta).
        var altura_total = Altura + AlturaSextavado;
        var furo = new CylinderFeature(
            new Point3D(DeslocamentoFuro, 0, -FolgaFuro),
            Vector3D.UnitZ,
            DiametroFuro / 2,
            altura_total + 2 * FolgaFuro)
        {
            Name = "furo",
            IsTransientTool = true
        };

        return BooleanFeature.CreateDifference("cilindro_espacador_motor",
            new IFeature[] { corpo, sextavado },
            furo);
    }
}