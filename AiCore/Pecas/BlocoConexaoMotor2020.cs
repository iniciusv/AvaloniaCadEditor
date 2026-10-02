using System;
using System.Collections.Generic;

/// <summary>
/// Bloco impresso intermediário para conectar o SuporteMotorNema17 ao perfil de alumínio 2020.
/// Dimensões: 40 mm (Largura, X) x 20 mm (Profundidade, Y) x 20 mm (Altura, Z).
/// Furos no sentido Y:
/// 1. Dois furos passantes M3 alinhados com os furos laterais do SuporteMotorNema17 (passo 15 mm em X),
///    com rebaixo para a cabeça do parafuso na face frontal (Y = -Profundidade / 2).
/// 2. Dois furos passantes M3 para fixação ao perfil 2020 (passo 30 mm em X),
///    com rebaixo para a cabeça do parafuso no lado oposto (face traseira, Y = +Profundidade / 2).
/// Origem (0, 0, 0) no centro em X e Y, base em Z = 0.
/// </summary>
public sealed record BlocoConexaoMotor2020(
    double Largura = 40.0,
    double Profundidade = 20.0,
    double Altura = 20.0,
    double PassoFurosSuporte = 15.0,
    double PassoFurosExtrusao = 30.0,
    double DiametroFuroM3 = 3.2,
    double DiametroRebaixo = 6.0,
    double ProfundidadeRebaixo = 3.0,
    double AlturaFurosSuporteZ = 15.0,
    double AlturaFurosExtrusaoZ = 10.0,
    double RaioChanfro = 1.0) : Part
{
    protected override IFeature Build()
    {
        var baseCorner = new Point3D(-Largura / 2.0, -Profundidade / 2.0, 0);
        var corpoBase = new BoxFeature(baseCorner, Largura, Profundidade, Altura) { Name = "corpo_base" };

        var corpo = RaioChanfro > 0.001
            ? new FilletFeature("corpo_chanfrado", corpoBase, RaioChanfro)
            {
                Mode = FilletMode.Bevel,
                ApplyToAllEdges = true
            }
            : (IFeature)corpoBase;

        var cortes = new List<IFeature>();

        // 1. Furos M3 conectados ao SuporteMotorNema17 (passo 15 mm)
        // O suporte encosta na face traseira (+Y) do bloco. O rebaixo fica na face frontal (Y = -Profundidade / 2)
        // para a cabeça do parafuso M3 que entra de -Y para +Y e rosqueia na porca do suporte.
        foreach (var sinalX in new[] { -1.0, 1.0 })
        {
            var posX = sinalX * PassoFurosSuporte / 2.0;
            var nome = sinalX < 0 ? "esq" : "dir";

            var furo = new CylinderFeature(
                globalBaseCenter: new Point3D(posX, -Profundidade / 2.0 - 1.0, AlturaFurosSuporteZ),
                globalDirection: Vector3D.UnitY,
                radius: DiametroFuroM3 / 2.0,
                height: Profundidade + 2.0)
            {
                Name = $"furo_suporte_{nome}",
                IsTransientTool = true
            };
            cortes.Add(furo);

            var rebaixo = new CylinderFeature(
                globalBaseCenter: new Point3D(posX, -Profundidade / 2.0 - 1.0, AlturaFurosSuporteZ),
                globalDirection: Vector3D.UnitY,
                radius: DiametroRebaixo / 2.0,
                height: ProfundidadeRebaixo + 1.0)
            {
                Name = $"rebaixo_suporte_{nome}",
                IsTransientTool = true
            };
            cortes.Add(rebaixo);
        }

        // 2. Furos M3 conectados ao perfil 2020 (passo 30 mm)
        // O perfil encosta na face frontal (-Y) do bloco. O rebaixo fica na face traseira (Y = +Profundidade / 2)
        // para a cabeça do parafuso M3 entrar por trás (+Y para -Y) e prender a porca T no perfil.
        foreach (var sinalX in new[] { -1.0, 1.0 })
        {
            var posX = sinalX * PassoFurosExtrusao / 2.0;
            var nome = sinalX < 0 ? "esq" : "dir";

            var furo = new CylinderFeature(
                globalBaseCenter: new Point3D(posX, -Profundidade / 2.0 - 1.0, AlturaFurosExtrusaoZ),
                globalDirection: Vector3D.UnitY,
                radius: DiametroFuroM3 / 2.0,
                height: Profundidade + 2.0)
            {
                Name = $"furo_2020_{nome}",
                IsTransientTool = true
            };
            cortes.Add(furo);

            // Rebaixo na face traseira (Y = +Profundidade / 2) apontando para -Y
            var rebaixo = new CylinderFeature(
                globalBaseCenter: new Point3D(posX, Profundidade / 2.0 + 1.0, AlturaFurosExtrusaoZ),
                globalDirection: new Vector3D(0, -1, 0),
                radius: DiametroRebaixo / 2.0,
                height: ProfundidadeRebaixo + 1.0)
            {
                Name = $"rebaixo_2020_{nome}",
                IsTransientTool = true
            };
            cortes.Add(rebaixo);
        }

        return BooleanFeature.CreateDifference("bloco_conexao_motor_2020", corpo, cortes.ToArray());
    }
}
