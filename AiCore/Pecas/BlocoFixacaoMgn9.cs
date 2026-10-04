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

        var zCentro = Altura / 2.0;

        // Perfil hexagonal da porca M3 + canal de inserção vertical (+Z): função local que
        // só CALCULA os pontos (o sólido é declarado em cada porca_* abaixo).
        List<Point3D> PerfilPorca()
        {
            var s = LarguraPorca;
            var r = (s / 2.0) / (Math.Sqrt(3.0) / 2.0); // raio até os vértices
            var hTopo = Altura - zCentro + 1.0;         // extrapola o topo p/ corte limpo
            return new List<Point3D>
            {
                new Point3D(0, -r, 0),
                new Point3D(s / 2.0, -r / 2.0, 0),
                new Point3D(s / 2.0, r / 2.0, 0),
                new Point3D(s / 2.0, hTopo, 0),
                new Point3D(-s / 2.0, hTopo, 0),
                new Point3D(-s / 2.0, r / 2.0, 0),
                new Point3D(-s / 2.0, -r / 2.0, 0)
            };
        }

        // Plano do alojamento da porca (captive nut) numa posição (x, yPorca): função local
        // que só CALCULA um WorkPlane, com o canal de inserção apontando p/ +Z.
        WorkPlane PlanoPorca(double x, double yPorca)
        {
            var yInicio = yPorca - EspessuraPorca / 2.0;
            return new WorkPlane(new Point3D(x, yInicio, zCentro), Vector3D.UnitY, new Vector3D(-1, 0, 0));
        }

        // 1. Furos verticais de fixação ao carrinho linear MGN9C (malha PassoFurosX × PassoFurosY).
        var xFix = PassoFurosX / 2.0;
        var yFix = PassoFurosY / 2.0;
        var furo_fixacao_1 = new CylinderFeature(new Point3D(-xFix, -yFix, 0), Vector3D.UnitZ, DiametroFuroFixacao / 2.0, Altura) { Name = "furo_fixacao_1", IsTransientTool = true };
        var furo_fixacao_2 = new CylinderFeature(new Point3D(-xFix, yFix, 0), Vector3D.UnitZ, DiametroFuroFixacao / 2.0, Altura) { Name = "furo_fixacao_2", IsTransientTool = true };
        var furo_fixacao_3 = new CylinderFeature(new Point3D(xFix, -yFix, 0), Vector3D.UnitZ, DiametroFuroFixacao / 2.0, Altura) { Name = "furo_fixacao_3", IsTransientTool = true };
        var furo_fixacao_4 = new CylinderFeature(new Point3D(xFix, yFix, 0), Vector3D.UnitZ, DiametroFuroFixacao / 2.0, Altura) { Name = "furo_fixacao_4", IsTransientTool = true };

        // 2. Três colunas de fixação M3 (central e laterais): furo passante no eixo Y +
        //    alojamentos de porca frontal (Y-) e traseiro (Y+). Cada furo e cada porca é uma
        //    declaração própria, com posições derivadas das mesmas variáveis de coluna.
        var raioM3 = DiametroFuroM3 / 2.0;
        var yFuroInicio = -Comprimento / 2.0 - 1.0;   // começa fora da face frontal
        var comprimentoFuro = Comprimento + 2.0;      // atravessa as duas faces p/ corte limpo
        var xCentral = 0.0;
        var xEsq = -DistanciaFurosLateraisX;
        var xDir = DistanciaFurosLateraisX;

        var furo_m3_central = new CylinderFeature(new Point3D(xCentral, yFuroInicio, zCentro), Vector3D.UnitY, raioM3, comprimentoFuro) { Name = "furo_m3_central", IsTransientTool = true };
        var porca_frontal_central = new PrismFeature(PerfilPorca(), EspessuraPorca, PlanoPorca(xCentral, PosicaoPorcaY), "porca_frontal_central") { IsTransientTool = true };
        var porca_traseira_central = new PrismFeature(PerfilPorca(), EspessuraPorca, PlanoPorca(xCentral, PosicaoPorcaTraseiraY), "porca_traseira_central") { IsTransientTool = true };

        var furo_m3_lateral_esq = new CylinderFeature(new Point3D(xEsq, yFuroInicio, zCentro), Vector3D.UnitY, raioM3, comprimentoFuro) { Name = "furo_m3_lateral_esq", IsTransientTool = true };
        var porca_frontal_lateral_esq = new PrismFeature(PerfilPorca(), EspessuraPorca, PlanoPorca(xEsq, PosicaoPorcaY), "porca_frontal_lateral_esq") { IsTransientTool = true };
        var porca_traseira_lateral_esq = new PrismFeature(PerfilPorca(), EspessuraPorca, PlanoPorca(xEsq, PosicaoPorcaTraseiraY), "porca_traseira_lateral_esq") { IsTransientTool = true };

        var furo_m3_lateral_dir = new CylinderFeature(new Point3D(xDir, yFuroInicio, zCentro), Vector3D.UnitY, raioM3, comprimentoFuro) { Name = "furo_m3_lateral_dir", IsTransientTool = true };
        var porca_frontal_lateral_dir = new PrismFeature(PerfilPorca(), EspessuraPorca, PlanoPorca(xDir, PosicaoPorcaY), "porca_frontal_lateral_dir") { IsTransientTool = true };
        var porca_traseira_lateral_dir = new PrismFeature(PerfilPorca(), EspessuraPorca, PlanoPorca(xDir, PosicaoPorcaTraseiraY), "porca_traseira_lateral_dir") { IsTransientTool = true };

        return BooleanFeature.CreateDifference("bloco_fixacao_mgn9", corpo,
            furo_fixacao_1, furo_fixacao_2, furo_fixacao_3, furo_fixacao_4,
            furo_m3_central, porca_frontal_central, porca_traseira_central,
            furo_m3_lateral_esq, porca_frontal_lateral_esq, porca_traseira_lateral_esq,
            furo_m3_lateral_dir, porca_frontal_lateral_dir, porca_traseira_lateral_dir);
    }
}
