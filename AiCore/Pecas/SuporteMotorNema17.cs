/// <summary>
/// Suporte impresso para fixar um motor NEMA 17 (ver <see cref="MotorNema17"/>) em cima de uma
/// extrusão de alumínio 2020 (ver <see cref="Extrusao2020"/>).
/// Placa centralizada em X e Y: a face frontal do motor (lado do boss/eixo) encosta na face
/// de baixo da placa, com o eixo passando por um furo de alívio central; os 4 furos de fixação
/// do motor (padrão 31 x 31 mm) atravessam a placa para os parafusos chegarem aos furos
/// rosqueados (cegos) do motor. Por baixo, 2 furos passantes alinhados ao furo central da
/// extrusão (ao longo do seu comprimento) permitem aparafusar a placa diretamente nela.
/// A origem (0, 0, 0) local fica na face de baixo da placa (encosto na extrusão), centralizada
/// em X e Y.
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
    double LarguraPorcaLateral = 5.83,
    double EspessuraPorcaLateral = 3.0,
    double ParedeSolidaPorcaLateral = 4.0) : Part
{
    /// <summary>Comprimento total (frente + trás). A face "frente" (y negativo) é a mais próxima da extrusão.</summary>
    public double Comprimento => ComprimentoFrente + ComprimentoTras;

    protected override IFeature Build()
    {
        var baseCorner = new Point3D(-Largura / 2, -ComprimentoFrente, 0);
        var corpoBase = new BoxFeature(baseCorner, Largura, Comprimento, Espessura) { Name = "corpo_base" };

        IFeature corpo = corpoBase;
        if (RaioChanfro > 0.001)
        {
            var chanfro = new FilletFeature("corpo_chanfrado", corpoBase, RaioChanfro)
            {
                Mode = FilletMode.Bevel,
                ApplyToAllEdges = true
            };
            corpo = chanfro;
        }

        var cortes = new List<IFeature>
        {
            new CylinderFeature(new Point3D(0, 0, 0), Vector3D.UnitZ, DiametroFuroEixo / 2, Espessura)
            {
                Name = "furo_alivio_eixo",
                IsTransientTool = true
            }
        };

        var i = 0;
        foreach (var sinalX in new[] { -1.0, 1.0 })
        {
            foreach (var sinalY in new[] { -1.0, 1.0 })
            {
                var furo = new CylinderFeature(
                    new Point3D(sinalX * PassoFurosMotor / 2, sinalY * PassoFurosMotor / 2, 0),
                    Vector3D.UnitZ,
                    DiametroFuroMotor / 2,
                    Espessura)
                {
                    Name = $"furo_motor_{i}",
                    IsTransientTool = true
                };
                cortes.Add(furo);
                i++;
            }
        }

        foreach (var sinalY in new[] { -1.0, 1.0 })
        {
            var furo = new CylinderFeature(
                new Point3D(0, sinalY * PassoFurosExtrusao / 2, 0),
                Vector3D.UnitZ,
                DiametroFuroExtrusao / 2,
                Espessura)
            {
                Name = $"furo_extrusao_{(sinalY < 0 ? "a" : "b")}",
                IsTransientTool = true
            };
            cortes.Add(furo);
        }

        // 2 furos horizontais M3 (passantes, eixo Y) com alojamento de porca (saída vertical para
        // cima), recuados para dentro do sólido (não mais na parte estreita/estendida da placa) —
        // mesmo padrão do bloco_fixacao_mgn9.
        var zCentroLateral = Espessura / 2.0;
        foreach (var sinalX in new[] { -1.0, 1.0 })
        {
            var x = sinalX * PassoFurosLateral / 2;
            var nome = sinalX < 0 ? "lateral_a" : "lateral_b";

            cortes.Add(CriarFuroPassanteY(
                nome: $"furo_lateral_{nome}",
                x: x,
                zCentro: zCentroLateral,
                yInicio: -ComprimentoFrente - 1.0,
                comprimentoBloco: Comprimento,
                diametroFuro: DiametroFuroM3Lateral));

            cortes.Add(CriarAlojamentoPorcaY(
                nome: $"porca_lateral_{nome}",
                x: x,
                yPorca: -ComprimentoFrente + ParedeSolidaPorcaLateral + EspessuraPorcaLateral / 2,
                zCentro: zCentroLateral,
                alturaBloco: Espessura,
                larguraPorca: LarguraPorcaLateral,
                espessuraPorca: EspessuraPorcaLateral));
        }

        // Vazio trapezoidal na borda traseira (lado oposto à extrusão), passante em toda a
        // espessura — alívio/clearance desenhado manualmente na cena (prism_290a) e incorporado
        // aqui como corte paramétrico do suporte. Nos últimos 5 mm antes da face externa, as
        // paredes deixam de afunilar e ficam retas (perpendiculares a essa face).
        const double yInterno = 1.82737;
        const double xInternoA = 13.88023;
        const double xInternoB = -13.88023;
        const double xExternoA = 9.0;
        const double xExternoB = -9.60529;
        const double comprimentoRetoNaFace = 5.0;

        var yExterno = ComprimentoTras;
        var yTransicao = yExterno - comprimentoRetoNaFace;
        var t = (yTransicao - yInterno) / (yExterno - yInterno);
        var xTransicaoA = xInternoA + t * (xExternoA - xInternoA);
        var xTransicaoB = xInternoB + t * (xExternoB - xInternoB);

        var verticesVazioTraseiro = new[]
        {
            new Point3D(xTransicaoA, yExterno, 0),
            new Point3D(xTransicaoA, yTransicao, 0),
            new Point3D(xInternoA, yInterno, 0),
            new Point3D(xInternoB, yInterno, 0),
            new Point3D(xTransicaoB, yTransicao, 0),
            new Point3D(xTransicaoB, yExterno, 0)
        };
        var planoVazioTraseiro = new WorkPlane(
            origin: new Point3D(0, 0, 0),
            normal: Vector3D.UnitZ,
            xAxis: Vector3D.UnitX);
        cortes.Add(new PrismFeature(verticesVazioTraseiro, height: Espessura, plane: planoVazioTraseiro, name: "vazio_traseiro")
        {
            IsTransientTool = true
        });

        return BooleanFeature.CreateDifference("suporte_motor_nema17", corpo, cortes.ToArray());
    }

    /// <summary>
    /// Furo horizontal passante no eixo Y (mesmo padrão do BlocoFixacaoMgn9).
    /// </summary>
    private static IFeature CriarFuroPassanteY(
        string nome,
        double x,
        double zCentro,
        double yInicio,
        double comprimentoBloco,
        double diametroFuro)
    {
        return new CylinderFeature(
            globalBaseCenter: new Point3D(x, yInicio, zCentro),
            globalDirection: Vector3D.UnitY,
            radius: diametroFuro / 2.0,
            height: comprimentoBloco + 2.0)
        {
            Name = nome,
            IsTransientTool = true
        };
    }

    /// <summary>
    /// Alojamento hexagonal para porca M3 com saída vertical para cima (+Z), mesmo padrão do
    /// BlocoFixacaoMgn9.
    /// </summary>
    private static IFeature CriarAlojamentoPorcaY(
        string nome,
        double x,
        double yPorca,
        double zCentro,
        double alturaBloco,
        double larguraPorca,
        double espessuraPorca)
    {
        var s = larguraPorca;
        var r = (s / 2.0) / (Math.Sqrt(3.0) / 2.0);
        var hTopo = alturaBloco - zCentro + 1.0;

        var verticesAlojamento = new[]
        {
            new Point3D(0, -r, 0),
            new Point3D(s / 2.0, -r / 2.0, 0),
            new Point3D(s / 2.0, r / 2.0, 0),
            new Point3D(s / 2.0, hTopo, 0),
            new Point3D(-s / 2.0, hTopo, 0),
            new Point3D(-s / 2.0, r / 2.0, 0),
            new Point3D(-s / 2.0, -r / 2.0, 0)
        };

        var yInicio = yPorca - espessuraPorca / 2.0;
        var planoAlojamento = new WorkPlane(
            origin: new Point3D(x, yInicio, zCentro),
            normal: Vector3D.UnitY,
            xAxis: new Vector3D(-1, 0, 0));

        return new PrismFeature(
            verticesAlojamento,
            height: espessuraPorca,
            plane: planoAlojamento,
            name: nome)
        {
            IsTransientTool = true
        };
    }
}
