using System;

/// <summary>
/// Bloco de pressão da correia (belt), fixado na lateral do bloco_fixacao_mgn9.
/// Corpo: 20,5 (X) x 16,5 (Y) x 10 (Z); base alinhada à base do bloco_fixacao_mgn9.
/// - Vão superior por onde a correia passa (altura 30 a 38 no mundo).
/// - 2 furos coaxiais em Y (X = -10, Z = 5) alinhados à fixação lateral esquerda do bloco.
/// - Furo de pressão da correia (X = -18,11533, Z = 8) + alojamento hexagonal M3 na traseira
///   com saída vertical (+Z), igual aos alojamentos laterais do bloco_fixacao_mgn9.
/// Origem local = base do bloco_fixacao_mgn9 (local = mundo - (0, 0, 26)).
/// </summary>
public sealed record BlocoFixacaoBelt(
    double LarguraCorpoX = 20.5,
    double ComprimentoCorpoY = 16.5,
    double AlturaCorpo = 10.0,
    double BaseCorpoX = -20.615334,
    double BaseCorpoY = 10.5,
    double LarguraVaoX = 17.0,
    double ComprimentoVaoY = 6.0,
    double AlturaVao = 8.0,
    double BaseVaoX = -22.61533,
    double BaseVaoY = 15.75,
    double BaseVaoZ = 4.0,
    double RaioFuroVertical = 3.0,
    double FuroVerticalX = -5.61533,
    double FuroVerticalY = 18.75,
    double FuroVerticalZ = 4.0,
    double AlturaFuroVertical = 8.0,
    double FuroLateralX = -10.0,
    double FuroLateralZ = 5.0,
    double RaioFuroRebaixo = 2.3,
    double ComprimentoFuroRebaixo = 6.0,
    double RaioFuroPassante = 2.1,
    double ComprimentoFuroPassante = 20.0,
    double FuroPressaoX = -18.11533,
    double FuroPressaoZ = 8.0,
    double RaioFuroPressao = 1.7,
    double PosicaoPorcaTraseiraY = 24.375,
    double LarguraPorca = 5.83,
    double EspessuraPorca = 3.0) : Part
{
    protected override IFeature Build()
    {
        var yFundo = BaseCorpoY + ComprimentoCorpoY;

        var corpo = new BoxFeature(
            new Point3D(BaseCorpoX, BaseCorpoY, 0),
            LarguraCorpoX,
            ComprimentoCorpoY,
            AlturaCorpo)
        {
            Name = "corpo"
        };

        var vao_correia = new BoxFeature(new Point3D(BaseVaoX, BaseVaoY, BaseVaoZ), LarguraVaoX, ComprimentoVaoY, AlturaVao) { Name = "vao_correia", IsTransientTool = true };
        var furo_vertical = new CylinderFeature(new Point3D(FuroVerticalX, FuroVerticalY, FuroVerticalZ), Vector3D.UnitZ, RaioFuroVertical, AlturaFuroVertical) { Name = "furo_vertical", IsTransientTool = true };
        var rebaixo_fixacao = new CylinderFeature(new Point3D(FuroLateralX, yFundo, FuroLateralZ), new Vector3D(0, -1, 0), RaioFuroRebaixo, ComprimentoFuroRebaixo) { Name = "rebaixo_fixacao", IsTransientTool = true };
        var furo_fixacao = new CylinderFeature(new Point3D(FuroLateralX, yFundo, FuroLateralZ), new Vector3D(0, -1, 0), RaioFuroPassante, ComprimentoFuroPassante) { Name = "furo_fixacao", IsTransientTool = true };
        var furo_pressao_correia = new CylinderFeature(new Point3D(FuroPressaoX, yFundo, FuroPressaoZ), new Vector3D(0, -1, 0), RaioFuroPressao, ComprimentoFuroPassante) { Name = "furo_pressao_correia", IsTransientTool = true };

        // Alojamento hexagonal da porca M3 com saída vertical (+Z), desenhado num plano normal a Y.
        var raioVerticePorca = (LarguraPorca / 2.0) / (Math.Sqrt(3.0) / 2.0); // raio até os vértices
        var alturaTopoPorca = AlturaCorpo - FuroPressaoZ + 1.0;              // extrapola o topo para corte limpo
        var verticesPorca = new[]
        {
            new Point3D(0, -raioVerticePorca, 0),
            new Point3D(LarguraPorca / 2.0, -raioVerticePorca / 2.0, 0),
            new Point3D(LarguraPorca / 2.0, raioVerticePorca / 2.0, 0),
            new Point3D(LarguraPorca / 2.0, alturaTopoPorca, 0),
            new Point3D(-LarguraPorca / 2.0, alturaTopoPorca, 0),
            new Point3D(-LarguraPorca / 2.0, raioVerticePorca / 2.0, 0),
            new Point3D(-LarguraPorca / 2.0, -raioVerticePorca / 2.0, 0)
        };
        var planoPorca = new WorkPlane(
            origin: new Point3D(FuroPressaoX, PosicaoPorcaTraseiraY - EspessuraPorca / 2.0, FuroPressaoZ),
            normal: Vector3D.UnitY,
            xAxis: new Vector3D(-1, 0, 0));
        var porca_pressao_correia = new PrismFeature(verticesPorca, height: EspessuraPorca, plane: planoPorca) { Name = "porca_pressao_correia", IsTransientTool = true };

        return BooleanFeature.CreateDifference("bloco_fixacao_belt", corpo, vao_correia, furo_vertical, rebaixo_fixacao, furo_fixacao, furo_pressao_correia, porca_pressao_correia);
    }
}
