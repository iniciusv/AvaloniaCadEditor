/// <summary>
/// Suporte impresso para fixar um motor NEMA 17 (ver <see cref="MotorNema17"/>) em cima de uma
/// extrusão de alumínio 2020 (ver <see cref="Extrusao2020"/>).
/// Placa centralizada em X e Y: a face frontal do motor (lado do boss/eixo) encosta na face
/// de baixo da placa, com o eixo passando por um furo de alívio central; os 4 furos de fixação
/// do motor (padrão 31 x 31 mm) atravessam a placa para os parafusos chegarem aos furos
/// rosqueados (cegos) do motor. Por baixo, 2 furos passantes alinhados ao furo central da
/// extrusão (ao longo do seu comprimento) permitem aparafusar a placa diretamente nela.
/// FIXAÇÕES LATERAIS COM PORCA M3: um furo de cada lado (±PassoFurosLateral/2); o parafuso entra
/// pela face frontal (1 mm fora dela) e a porca fica no fundo, inserida por um canal aberto no
/// TOPO da placa. Cada fixação é UMA instância de <see cref="FuroPorca"/> (furo e alojamento
/// hex derivados das mesmas variáveis, logo sempre coaxiais), subtraída do corpo — mesmo padrão
/// das fixações laterais da <see cref="PlacaSuporteCarrinhoY"/>.
/// Todos os furos passantes começam 1 mm abaixo da base e terminam 1 mm acima do topo, para o
/// corte não ficar rente às faces.
/// A origem (0, 0, 0) local fica na face de baixo da placa (encosto na extrusão), centralizada
/// em X e Y.
/// As arestas do CORPO BRUTO (envelope) recebem um chanfro (bevel) de RaioChanfro mm, cortado
/// ANTES dos furos — ver <see cref="PlacaSuporteCarrinhoY"/>: o chanfro genérico é um algoritmo
/// de MALHA que faz uma booleana por aresta, então aplicá-lo ao sólido já furado custaria caro
/// demais; de quebra, as bocas dos furos ficam vivas.
/// </summary>
public sealed record SuporteMotorNema17(
    double Largura = 45,
    double ComprimentoFrente = 22.5,
    double ComprimentoTras = 22.5,
    double Espessura = 6,
    double DiametroFuroEixo = 28,
    double PassoFurosMotor = 31,
    double DiametroFuroMotor = 3.4,
    double PassoFurosExtrusao = 20,
    double DiametroFuroExtrusao = 5.5,
    double RaioChanfro = 2.0,
    double PassoFurosLateral = 15,
    double DiametroFuroM3Lateral = 3.2,
    double ParedeSolidaPorcaLateral = 4.0) : Part
{
    /// <summary>Comprimento total (frente + trás). A face "frente" (y negativo) é a mais próxima da extrusão.</summary>
    public double Comprimento => ComprimentoFrente + ComprimentoTras;

    protected override IFeature Build()
    {
        // Envelope (bloco) e chanfro do corpo bruto, ANTES dos furos (ver o <summary>).
        var corpo_base = new BoxFeature(new Point3D(-Largura / 2, -ComprimentoFrente, 0), Largura, Comprimento, Espessura)
        {
            Name = "corpo_base"
        };

        IFeature corpo = corpo_base;
        if (RaioChanfro > 0.001)
        {
            var corpo_chanfrado = new FilletFeature("corpo_chanfrado", corpo_base, RaioChanfro)
            {
                Mode = FilletMode.Bevel,
                ApplyToAllEdges = true
            };
            corpo = corpo_chanfrado;
        }

        // Furos passantes: começam 1 mm abaixo da base e terminam 1 mm acima do topo (corte limpo).
        const double folga_passante = 1.0;
        var z_base_furos = -folga_passante;
        var altura_furo_passante = Espessura + 2 * folga_passante;

        // Furo central de alívio: passa o boss/eixo do motor.
        var furo_alivio_eixo = new CylinderFeature(
            new Point3D(0, 0, z_base_furos), Vector3D.UnitZ, DiametroFuroEixo / 2, altura_furo_passante)
        {
            Name = "furo_alivio_eixo",
            IsTransientTool = true
        };

        // 4 furos de fixação do motor (padrão 31 × 31), um por canto.
        var x_furos_motor = PassoFurosMotor / 2;
        var raio_furo_motor = DiametroFuroMotor / 2;
        var furo_motor_1 = new CylinderFeature(new Point3D(-x_furos_motor, -x_furos_motor, z_base_furos), Vector3D.UnitZ, raio_furo_motor, altura_furo_passante) { Name = "furo_motor_1", IsTransientTool = true };
        var furo_motor_2 = new CylinderFeature(new Point3D(x_furos_motor, -x_furos_motor, z_base_furos), Vector3D.UnitZ, raio_furo_motor, altura_furo_passante) { Name = "furo_motor_2", IsTransientTool = true };
        var furo_motor_3 = new CylinderFeature(new Point3D(-x_furos_motor, x_furos_motor, z_base_furos), Vector3D.UnitZ, raio_furo_motor, altura_furo_passante) { Name = "furo_motor_3", IsTransientTool = true };
        var furo_motor_4 = new CylinderFeature(new Point3D(x_furos_motor, x_furos_motor, z_base_furos), Vector3D.UnitZ, raio_furo_motor, altura_furo_passante) { Name = "furo_motor_4", IsTransientTool = true };

        // 2 furos passantes alinhados ao furo central da extrusão 2020 (ao longo do seu comprimento).
        var y_furos_extrusao = PassoFurosExtrusao / 2;
        var raio_furo_extrusao = DiametroFuroExtrusao / 2;
        var furo_extrusao_a = new CylinderFeature(new Point3D(0, -y_furos_extrusao, z_base_furos), Vector3D.UnitZ, raio_furo_extrusao, altura_furo_passante) { Name = "furo_extrusao_a", IsTransientTool = true };
        var furo_extrusao_b = new CylinderFeature(new Point3D(0, y_furos_extrusao, z_base_furos), Vector3D.UnitZ, raio_furo_extrusao, altura_furo_passante) { Name = "furo_extrusao_b", IsTransientTool = true };

        // -------------------------------------------------------------------------------
        // Fixações laterais com porca M3 (uma de cada lado). O parafuso entra pela face frontal,
        // 1 mm fora dela (y = -ComprimentoFrente - 1); a porca fica no fundo e é inserida por um
        // canal aberto no TOPO da placa (EixoHexagono = +Z), que extrapola 1 mm o topo.
        // -------------------------------------------------------------------------------
        var z_centro_lateral = Espessura / 2.0;
        var comprimento_canal_lateral = Espessura - z_centro_lateral + 1.0;   // até 1 mm acima do topo
        var profundidade_alojamento_lateral = 1.0 + ParedeSolidaPorcaLateral;
        var x_furos_lateral = PassoFurosLateral / 2;

        var fixacao_lateral_a = new FuroPorca(
            Entrada: new Point3D(-x_furos_lateral, -ComprimentoFrente - 1.0, z_centro_lateral),
            Direcao: Vector3D.UnitY,
            Profundidade: profundidade_alojamento_lateral,
            Insercao: InsercaoPorca.Lateral,
            EixoHexagono: Vector3D.UnitZ,
            ComprimentoCanal: comprimento_canal_lateral,
            DiametroFuro: DiametroFuroM3Lateral).Instance("fixacao_lateral_a");

        var fixacao_lateral_b = new FuroPorca(
            Entrada: new Point3D(x_furos_lateral, -ComprimentoFrente - 1.0, z_centro_lateral),
            Direcao: Vector3D.UnitY,
            Profundidade: profundidade_alojamento_lateral,
            Insercao: InsercaoPorca.Lateral,
            EixoHexagono: Vector3D.UnitZ,
            ComprimentoCanal: comprimento_canal_lateral,
            DiametroFuro: DiametroFuroM3Lateral).Instance("fixacao_lateral_b");

        // -------------------------------------------------------------------------------
        // Vazio trapezoidal na borda traseira (lado oposto à extrusão), passante em toda a
        // espessura — alívio/clearance. Nos últimos 5 mm antes da face externa, as paredes
        // deixam de afunilar e ficam retas (perpendiculares a essa face).
        // -------------------------------------------------------------------------------
        const double y_interno = 1.82737;
        const double x_interno_a = 13.88023;
        const double x_interno_b = -13.88023;
        const double x_externo_a = 9.0;
        const double x_externo_b = -9.60529;
        const double comprimento_reto_na_face = 5.0;

        var y_externo = ComprimentoTras;
        var y_transicao = y_externo - comprimento_reto_na_face;
        var t = (y_transicao - y_interno) / (y_externo - y_interno);
        var x_transicao_a = x_interno_a + t * (x_externo_a - x_interno_a);
        var x_transicao_b = x_interno_b + t * (x_externo_b - x_interno_b);

        var vertices_vazio_traseiro = new[]
        {
            new Point3D(x_transicao_a, y_externo, 0),
            new Point3D(x_transicao_a, y_transicao, 0),
            new Point3D(x_interno_a, y_interno, 0),
            new Point3D(x_interno_b, y_interno, 0),
            new Point3D(x_transicao_b, y_transicao, 0),
            new Point3D(x_transicao_b, y_externo, 0)
        };
        var plano_vazio_traseiro = new WorkPlane(
            origin: new Point3D(0, 0, z_base_furos),
            normal: Vector3D.UnitZ,
            xAxis: Vector3D.UnitX);
        var vazio_traseiro = new PrismFeature(vertices_vazio_traseiro, height: altura_furo_passante, plane: plano_vazio_traseiro, name: "vazio_traseiro")
        {
            IsTransientTool = true
        };

        return BooleanFeature.CreateDifference("suporte_motor_nema17", corpo,
            furo_alivio_eixo,
            furo_motor_1, furo_motor_2, furo_motor_3, furo_motor_4,
            furo_extrusao_a, furo_extrusao_b,
            fixacao_lateral_a, fixacao_lateral_b,
            vazio_traseiro);
    }
}