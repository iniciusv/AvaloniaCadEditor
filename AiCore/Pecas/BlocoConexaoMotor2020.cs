using System;
using System.Collections.Generic;

/// <summary>
/// Bloco impresso intermediÃ¡rio para conectar o SuporteMotorNema17 ao perfil de alumÃ­nio 2020.
/// DimensÃµes: 40 mm (Largura, X) x 20 mm (Profundidade, Y) x 28 mm (Altura, Z).
/// Furos no sentido Y:
/// 1. Dois furos passantes M3 alinhados com os furos laterais do SuporteMotorNema17 (passo 15 mm em X),
///    com rebaixo para a cabeÃ§a do parafuso na face frontal (Y = -Profundidade / 2).
/// 2. Dois furos passantes M3 para fixaÃ§Ã£o ao perfil 2020 (passo 30 mm em X),
///    com rebaixo para a cabeÃ§a do parafuso no lado oposto (face traseira, Y = +Profundidade / 2).
/// 3. Rebaixo (vazio) na face superior, cobrindo metade da Ã¡rea do retÃ¢ngulo (toda a Largura,
///    metade da Profundidade), 10 mm de profundidade, do lado mais prÃ³ximo do carrinho MGN9C na montagem.
///    Esse rebaixo Ã© cortado ANTES do chanfro de quinas (bevel), para que a aresta externa que ele
///    cria na face de cima tambÃ©m saia chanfrada.
/// Origem (0, 0, 0) no centro em X e Y, base em Z = 0.
/// </summary>
public sealed record BlocoConexaoMotor2020(
    double Largura = 40.0,
    double Profundidade = 20.0,
    double Altura = 28.0,
    double PassoFurosSuporte = 15.0,
    double PassoFurosExtrusao = 20.0,
    double DiametroFuroM3 = 3.2,
    double DiametroRebaixo = 6.0,
    double ProfundidadeRebaixo = 3.0,
    double AlturaFurosSuporteZ = 23.0,
    double AlturaFurosExtrusaoZ = 10.0,
    double RaioChanfro = 1.0,
    double PassoFurosVerticaisZ = 30.0,
    double DiametroFuroVertical = 3.2,
    double FolgaLateralFuroVertical = 1.0,
    double DistanciaFurosVerticaisDaFaceTraseiraY = 5.0,
    double DistanciaFurosVerticaisDaFaceFrontalY = 5.0,
    double ProfundidadeRebaixoSuperior = 10.0,
    bool RebaixoSuperiorNoLadoNegativoY = true) : Part
{
    protected override IFeature Build()
    {
        var baseCorner = new Point3D(-Largura / 2.0, -Profundidade / 2.0, 0);
        var corpoBase = new BoxFeature(baseCorner, Largura, Profundidade, Altura) { Name = "corpo_base" };

        // O rebaixo superior (seÃ§Ã£o 4) Ã© cortado ANTES do chanfro, nÃ£o depois: assim a aresta
        // externa que ele cria na face de cima tambÃ©m Ã© chanfrada junto com o resto do corpo. A
        // aresta interna do degrau (cÃ´ncava) nÃ£o Ã© afetada pelo chanfro de qualquer forma â€” o
        // kernel pula arestas cÃ´ncavas no fillet genÃ©rico (ver OpenCascadeGeometryKernel.BuildFillet).
        IFeature corpoPreChanfro = corpoBase;
        if (ProfundidadeRebaixoSuperior > 0.001)
        {
            var profundidadeRebaixoArea = Profundidade / 2.0;
            var yInicioRebaixo = RebaixoSuperiorNoLadoNegativoY
                ? -Profundidade / 2.0
                : 0.0;

            var rebaixoSuperior = new BoxFeature(
                baseCorner: new Point3D(-Largura / 2.0, yInicioRebaixo, Altura - ProfundidadeRebaixoSuperior),
                width: Largura,
                length: profundidadeRebaixoArea,
                height: ProfundidadeRebaixoSuperior + 1.0)
            {
                Name = "rebaixo_superior",
                IsTransientTool = true
            };

            corpoPreChanfro = BooleanFeature.CreateDifference("corpo_com_rebaixo", corpoBase, rebaixoSuperior);
        }

        var corpo = RaioChanfro > 0.001
            ? new FilletFeature("corpo_chanfrado", corpoPreChanfro, RaioChanfro)
            {
                Mode = FilletMode.Bevel,
                ApplyToAllEdges = true
            }
            : corpoPreChanfro;

        var cortes = new List<IFeature>();

        // 1. Furos M3 conectados ao SuporteMotorNema17 (passo 15 mm)
        // O suporte encosta na face traseira (+Y) do bloco. O rebaixo fica na face frontal (Y = -Profundidade / 2)
        // para a cabeÃ§a do parafuso M3 que entra de -Y para +Y e rosqueia na porca do suporte.
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
        // para a cabeÃ§a do parafuso M3 entrar por trÃ¡s (+Y para -Y) e prender a porca T no perfil.
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

        // 3. Furos verticais no eixo Z passantes com folga lateral (rasgo oblongo em X para 30 mm a 31 mm)
        if (PassoFurosVerticaisZ > 0.001 && DiametroFuroVertical > 0.001)
        {
            var r = DiametroFuroVertical / 2.0;

            // Dois pares de furos em Y:
            // - Traseiro: prÃ³ximo da face ligada ao suporte (+Y)
            // - Frontal: prÃ³ximo da face oposta, ligada ao perfil 2020 (-Y)
            var posicoesY = new[]
            {
                ("tras", Profundidade / 2.0 - DistanciaFurosVerticaisDaFaceTraseiraY),
                ("frente", -Profundidade / 2.0 + DistanciaFurosVerticaisDaFaceFrontalY)
            };

            foreach (var (ladoY, yFuroVertical) in posicoesY)
            {
                foreach (var sinalX in new[] { -1.0, 1.0 })
                {
                    var nome = $"{ladoY}_{(sinalX < 0 ? "esq" : "dir")}";
                    // Centro nominal do furo em 30 mm
                    var x1 = sinalX * (PassoFurosVerticaisZ / 2.0);
                    // Centro expandido para acomodar 31 mm (para fora)
                    var x2 = sinalX * (PassoFurosVerticaisZ / 2.0 + FolgaLateralFuroVertical);

                    var xMin = Math.Min(x1, x2);
                    var xMax = Math.Max(x1, x2);

                    // Cilindro na posiÃ§Ã£o de 30 mm
                    cortes.Add(new CylinderFeature(
                        globalBaseCenter: new Point3D(x1, yFuroVertical, -1.0),
                        globalDirection: Vector3D.UnitZ,
                        radius: r,
                        height: Altura + 2.0)
                    {
                        Name = $"furo_vertical_z_{nome}_1",
                        IsTransientTool = true
                    });

                    if (FolgaLateralFuroVertical > 0.001)
                    {
                        // Cilindro na posiÃ§Ã£o expandida (31 mm)
                        cortes.Add(new CylinderFeature(
                            globalBaseCenter: new Point3D(x2, yFuroVertical, -1.0),
                            globalDirection: Vector3D.UnitZ,
                            radius: r,
                            height: Altura + 2.0)
                        {
                            Name = $"furo_vertical_z_{nome}_2",
                            IsTransientTool = true
                        });

                        // Caixa de uniÃ£o entre os dois cilindros formando o furo oblongo contÃ­nuo
                        cortes.Add(new BoxFeature(
                            baseCorner: new Point3D(xMin, yFuroVertical - r, -1.0),
                            width: xMax - xMin,
                            length: DiametroFuroVertical,
                            height: Altura + 2.0)
                        {
                            Name = $"furo_vertical_z_{nome}_rasgo",
                            IsTransientTool = true
                        });
                    }
                }
            }
        }

        // 4. Rebaixo (vazio) na face superior, ocupando metade da Ã¡rea do retÃ¢ngulo (Largura inteira x metade
        // da Profundidade), do lado mais prÃ³ximo do carrinho MGN9C na montagem (lado -Y, face frontal).
        // JÃ¡ foi cortado acima, antes do chanfro â€” ver "corpoPreChanfro".

        return BooleanFeature.CreateDifference("bloco_conexao_motor_2020", corpo, cortes.ToArray());
    }
}
