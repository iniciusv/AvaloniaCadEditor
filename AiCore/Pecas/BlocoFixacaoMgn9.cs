using System;
using System.Collections.Generic;

/// <summary>
/// Bloco impresso para fixação ao carrinho MGN9C.
/// Envelope de referência: 29,9 mm (X, direção do trilho) x 21,0 mm (Y, perpendicular ao trilho) x Altura (Z).
/// Possui:
/// - Chanfro (Bevel) em todas as arestas externas do bloco original.
/// - 4 furos verticais passantes de diâmetro 3,2 mm (fixação M3 ao carrinho MGN9C), espaçados 10 mm (X) x 15 mm (Y).
/// - 3 furos horizontais passantes M3 (X = 0 e X = ±DistanciaFurosLateraisX, Z = Altura / 2).
/// - 2 conjuntos de alojamentos hexagonais para porcas M3 com saída vertical para cima (+Z):
///   - Frontal (Y = -3,0 mm): 6 mm de parede sólida até a face frontal (Y = -10,5 mm).
///   - Traseiro (Y = +3,0 mm): 6 mm de parede sólida até a face traseira (Y = +10,5 mm).
///   - Entre os alojamentos frontal e traseiro resta uma divisória central sólida de 3 mm de espessura.
/// A origem (0, 0, 0) local fica na base inferior, centralizada em X e Y.
/// </summary>
public sealed record BlocoFixacaoMgn9(
    double Altura = 10,
    double Largura = 29.9,
    double Comprimento = 21.0,
    double DiametroFuroFixacao = 3.2,
    double PassoFurosX = 10,
    double PassoFurosY = 15,
    double DiametroFuroM3 = 3.2,
    double LarguraPorca = 5.83,
    double EspessuraPorca = 3.0,
    double PosicaoPorcaY = -3.0,
    double PosicaoPorcaTraseiraY = 3.0,
    double DistanciaFurosLateraisX = 10.0,
    double RaioChanfro = 1.0) : Part
{
    protected override IFeature Build()
    {
        var baseCorner = new Point3D(-Largura / 2, -Comprimento / 2, 0);
        var corpoBase = new BoxFeature(baseCorner, Largura, Comprimento, Altura) { Name = "corpo_base" };

        var corpo = RaioChanfro > 0.001
            ? new FilletFeature("corpo_chanfrado", corpoBase, RaioChanfro)
            {
                Mode = FilletMode.Bevel,
                ApplyToAllEdges = true
            }
            : (IFeature)corpoBase;

        var cortes = new List<IFeature>();

        // 1. Furos verticais de fixação ao carrinho linear MGN9C
        var i = 0;
        foreach (var sinalX in new[] { -1.0, 1.0 })
        {
            foreach (var sinalY in new[] { -1.0, 1.0 })
            {
                var x = sinalX * PassoFurosX / 2;
                var y = sinalY * PassoFurosY / 2;
                var furo = new CylinderFeature(
                    new Point3D(x, y, 0),
                    Vector3D.UnitZ,
                    DiametroFuroFixacao / 2,
                    Altura)
                {
                    Name = $"furo_fixacao_{i}",
                    IsTransientTool = true
                };
                cortes.Add(furo);
                i++;
            }
        }

        // 2. Três furos M3 passantes e alojamentos duplos para porcas (frontal e traseiro)
        var zCentro = Altura / 2.0;
        var posicoesX = new[]
        {
            ("central", 0.0),
            ("lateral_esq", -DistanciaFurosLateraisX),
            ("lateral_dir", DistanciaFurosLateraisX)
        };

        foreach (var (nome, posX) in posicoesX)
        {
            // Furo horizontal passante para parafuso M3
            cortes.Add(CriarFuroPassante(
                nome: $"furo_m3_{nome}",
                x: posX,
                zCentro: zCentro,
                comprimentoBloco: Comprimento,
                diametroFuro: DiametroFuroM3));

            // Alojamento de porca frontal (fixação frontal / suporte hotend)
            cortes.Add(CriarAlojamentoPorca(
                nome: $"porca_frontal_{nome}",
                x: posX,
                yPorca: PosicaoPorcaY,
                zCentro: zCentro,
                alturaBloco: Altura,
                larguraPorca: LarguraPorca,
                espessuraPorca: EspessuraPorca));

            // Alojamento de porca traseiro (fixação traseira de outros componentes)
            cortes.Add(CriarAlojamentoPorca(
                nome: $"porca_traseira_{nome}",
                x: posX,
                yPorca: PosicaoPorcaTraseiraY,
                zCentro: zCentro,
                alturaBloco: Altura,
                larguraPorca: LarguraPorca,
                espessuraPorca: EspessuraPorca));
        }

        return BooleanFeature.CreateDifference("bloco_fixacao_mgn9", corpo, cortes.ToArray());
    }

    /// <summary>
    /// Cria o furo cilíndrico passante no eixo Y para o parafuso M3.
    /// </summary>
    private static IFeature CriarFuroPassante(
        string nome,
        double x,
        double zCentro,
        double comprimentoBloco,
        double diametroFuro)
    {
        return new CylinderFeature(
            globalBaseCenter: new Point3D(x, -comprimentoBloco / 2.0 - 1.0, zCentro),
            globalDirection: Vector3D.UnitY,
            radius: diametroFuro / 2.0,
            height: comprimentoBloco + 2.0)
        {
            Name = nome,
            IsTransientTool = true
        };
    }

    /// <summary>
    /// Gera o alojamento hexagonal para porca M3 com saída vertical para cima (+Z) (captive nut slot).
    /// </summary>
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
}
