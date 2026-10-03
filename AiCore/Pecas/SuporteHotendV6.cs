using System;
using System.Collections.Generic;

/// <summary>
/// Suporte frontal impresso para fixação do Hotend V6 ao BlocoFixacaoMgn9 do carrinho MGN9C.
/// Encaixa na face frontal do bloco de fixação (Y = 0 na coordenada local) e se conecta a ele
/// através dos mesmos 3 furos M3 (espaçamento X = 0 e X = ±DistanciaFurosLateraisX).
/// Na parte frontal, abraça/aloja o colar cilíndrico superior de fixação do Hotend V6 (Ø16 mm x 10 mm altura)
/// localizado a uma distância configurável (padrão 15 mm à frente da face de contato, alinhado com o bico em Y = -25 na cena).
/// Possui chanfro (Bevel) configurável em todas as arestas do sólido original.
/// </summary>
public sealed record SuporteHotendV6(
    double Altura = 10,
    double Largura = 50,
    double DistanciaYHotend = 13.0,
    double DiametroColar = 16.2,
    double EspessuraParedeFrontal = 7.0,
    double DiametroFuroM3 = 3.2,
    double DistanciaFurosLateraisX = 10.0,
    double DistanciaFurosMotorX = 43.0,
    double DiametroFuroMotor = 3.2,
    double DistanciaFuroFrontalX = 16.0,
    double DiametroRebaixo = 6.5,
    double ProfundidadeRebaixo = 10.0,
    double RaioChanfro = 1.0,
    double EspessuraRasgo = 1.0,
    double ProfundidadeRasgoX = 2.0,
    double LarguraPorca = 5.83,
    double EspessuraPorca = 3.0,
    double AfastamentoPorcaFrontal = 3.0) : Part
{
    protected override IFeature Build()
    {
        var comprimentoTotal = DistanciaYHotend + DiametroColar / 2.0 + EspessuraParedeFrontal;
        var baseCorner = new Point3D(-Largura / 2.0, -comprimentoTotal, 0);
        var corpoBase = new BoxFeature(baseCorner, Largura, comprimentoTotal, Altura) { Name = "corpo_base" };

        var corpo = RaioChanfro > 0.001
            ? new FilletFeature("corpo_chanfrado", corpoBase, RaioChanfro)
            {
                Mode = FilletMode.Bevel,
                ApplyToAllEdges = true
            }
            : (IFeature)corpoBase;

        var cortes = new List<IFeature>();

        // 1. Alojamento cilíndrico para o colar de fixação do Hotend V6 (Ø16 mm)
        var furoColar = new CylinderFeature(
            globalBaseCenter: new Point3D(0, -DistanciaYHotend, -1),
            globalDirection: Vector3D.UnitZ,
            radius: DiametroColar / 2.0,
            height: Altura + 2)
        {
            Name = "alojamento_colar_hotend",
            IsTransientTool = true
        };
        cortes.Add(furoColar);

        // 1b. Dois furos verticais (eixo Z) para prender o motor, na mesma linha X da extrusora,
        //     com o furo da extrusora no centro (X = 0) e 43 mm entre eles (X = ±DistanciaFurosMotorX / 2).
        if (DistanciaFurosMotorX > 0.001 && DiametroFuroMotor > 0.001)
        {
            foreach (var (nome, posX) in new[]
            {
                ("esq", -DistanciaFurosMotorX / 2.0),
                ("dir", DistanciaFurosMotorX / 2.0)
            })
            {
                var furoMotor = new CylinderFeature(
                    globalBaseCenter: new Point3D(posX, -DistanciaYHotend, -1),
                    globalDirection: Vector3D.UnitZ,
                    radius: DiametroFuroMotor / 2.0,
                    height: Altura + 2)
                {
                    Name = $"furo_motor_{nome}",
                    IsTransientTool = true
                };
                cortes.Add(furoMotor);

                // Alojamento hexagonal para porca M3 na base inferior (Z = 0) do furo, aberto para baixo.
                if (LarguraPorca > 0.001 && EspessuraPorca > 0.001)
                {
                    cortes.Add(CriarAlojamentoPorcaInferior(
                        nome: $"porca_motor_{nome}",
                        x: posX,
                        y: -DistanciaYHotend,
                        larguraPorca: LarguraPorca,
                        espessuraPorca: EspessuraPorca));
                }
            }
        }

        // 2. Três furos M3 alinhados com o BlocoFixacaoMgn9 (sentido Y, Z = Altura / 2)
        var zCentro = Altura / 2.0;
        var posicoesX = new[]
        {
            ("central", 0.0, false),
            ("lateral_esq", -DistanciaFurosLateraisX, true),
            ("lateral_dir", DistanciaFurosLateraisX, true)
        };

        foreach (var (nome, posX, comRebaixo) in posicoesX)
        {
            // Furo passante M3
            var furoM3 = new CylinderFeature(
                globalBaseCenter: new Point3D(posX, -comprimentoTotal - 1, zCentro),
                globalDirection: Vector3D.UnitY,
                radius: DiametroFuroM3 / 2.0,
                height: comprimentoTotal + 2)
            {
                Name = $"furo_m3_{nome}",
                IsTransientTool = true
            };
            cortes.Add(furoM3);

            // Rebaixo frontal para embutir a cabeça do parafuso M3 (apenas nos furos laterais acessíveis)
            if (comRebaixo)
            {
                var rebaixo = new CylinderFeature(
                    globalBaseCenter: new Point3D(posX, -comprimentoTotal - 1, zCentro),
                    globalDirection: Vector3D.UnitY,
                    radius: DiametroRebaixo / 2.0,
                    height: ProfundidadeRebaixo + 1)
                {
                    Name = $"rebaixo_m3_{nome}",
                    IsTransientTool = true
                };
                cortes.Add(rebaixo);
            }
        }

        // 2b. Furo frontal horizontal (eixo Y) na parte da frente, à direita do furo M3 lateral direito.
        if (DistanciaFuroFrontalX > 0.001)
        {
            var furoFrontal = new CylinderFeature(
                globalBaseCenter: new Point3D(DistanciaFuroFrontalX, -comprimentoTotal - 1, zCentro),
                globalDirection: Vector3D.UnitY,
                radius: DiametroFuroM3 / 2.0,
                height: comprimentoTotal + 2)
            {
                Name = "furo_frontal_dir",
                IsTransientTool = true
            };
            cortes.Add(furoFrontal);
        }

        // 3. Rasgo lateral vertical (eixo Z) que vai da face externa direita (+X) até o vazio do hotend
        if (EspessuraRasgo > 0.001)
        {
            // Rasgo partindo do raio do colar (X = DiametroColar / 2) até ultrapassar a lateral externa direita (X = Largura / 2)
            var xMinRasgo = DiametroColar / 2.0 - 0.5 -3; // penetra ligeiramente no vazio para garantir união limpa
            var xMaxRasgo = Largura / 2.0 + 1.0 +3;
            var yMinRasgo = (-DistanciaYHotend - EspessuraRasgo / 2.0)+5;
            var zMinRasgo = -1.0;

            var rasgoLateral = new BoxFeature(
                baseCorner: new Point3D(xMinRasgo, yMinRasgo, zMinRasgo),
                width: xMaxRasgo - xMinRasgo,
                length: EspessuraRasgo,
                height: Altura + 2.0)
            {
                Name = "rasgo_lateral",
                IsTransientTool = true
            };
            cortes.Add(rasgoLateral);
        }

        // 4. Alojamento para porca M3 no furo central (X = 0), na parede frontal afastado 3 mm dela, com saída vertical (+Z)
        if (LarguraPorca > 0.001 && EspessuraPorca > 0.001)
        {
            var yPorca = -comprimentoTotal + AfastamentoPorcaFrontal + EspessuraPorca / 2.0;
            cortes.Add(CriarAlojamentoPorca(
                nome: "alojamento_porca_central",
                x: 0.0,
                yPorca: yPorca,
                zCentro: zCentro,
                alturaBloco: Altura,
                larguraPorca: LarguraPorca,
                espessuraPorca: EspessuraPorca));
        }

        return BooleanFeature.CreateDifference("suporte_hotend_v6", corpo, cortes.ToArray());
    }

    private static IFeature CriarAlojamentoPorca(
        string nome,
        double x,
        double yPorca,
        double zCentro,
        double alturaBloco,
        double larguraPorca,
        double espessuraPorca)
    {
        var s = larguraPorca;
        var r = (s / 2.0) / (Math.Sqrt(3.0) / 2.0); // raio até os vértices
        var hTopo = alturaBloco - zCentro + 1.0;     // extrapola o topo para corte limpo

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

    /// <summary>
    /// Alojamento hexagonal para porca M3 na base inferior (Z = 0) de um furo vertical (eixo Z),
    /// aberto para baixo e centrado no eixo do furo.
    /// </summary>
    private static IFeature CriarAlojamentoPorcaInferior(
        string nome,
        double x,
        double y,
        double larguraPorca,
        double espessuraPorca)
    {
        var s = larguraPorca;
        var r = (s / 2.0) / (Math.Sqrt(3.0) / 2.0); // raio até os vértices

        var verticesAlojamento = new[]
        {
            new Point3D(0, -r, 0),
            new Point3D(s / 2.0, -r / 2.0, 0),
            new Point3D(s / 2.0, r / 2.0, 0),
            new Point3D(0, r, 0),
            new Point3D(-s / 2.0, r / 2.0, 0),
            new Point3D(-s / 2.0, -r / 2.0, 0)
        };

        var planoAlojamento = new WorkPlane(
            origin: new Point3D(x, y, -1),
            normal: Vector3D.UnitZ,
            xAxis: Vector3D.UnitX);

        return new PrismFeature(
            verticesAlojamento,
            height: espessuraPorca + 1,
            plane: planoAlojamento,
            name: nome)
        {
            IsTransientTool = true
        };
    }
}
