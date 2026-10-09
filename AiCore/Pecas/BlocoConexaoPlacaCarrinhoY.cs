// ===== Pecas/BlocoConexaoPlacaCarrinhoY.cs =====
/// <summary>
/// Bloco 15 (X) × 48 (Y) × 25 (Z) mm que se parafusa SOBRE a
/// <see cref="PlacaSuporteCarrinhoY"/>, aproveitando os dois furos com que a placa é fixada ao
/// carrinho MGN15H (furo_fixacao_carrinho_1 e furo_fixacao_carrinho_2, afastados 25 mm em Y):
/// o MESMO parafuso atravessa o bloco, a placa e vai à porca do carrinho.
/// Origem local (0,0,0) no CENTRO DA FACE INFERIOR, no eixo dos DOIS furos verticais (que ficam
/// em y = ±PassoFuros/2, x = 0); o bloco cresce em +Z.
/// A FACE DIREITA (+X) fica a DistanciaFuroFaceDireita do eixo dos furos, de modo que ela caia
/// exatamente sobre a face mais à direita da placa (16 − 12,5 = 3,5 mm do eixo dos furos da
/// placa). A maior parte da largura cresce para −X, então o bloco fica deslocado para dentro.
/// Cada furo vertical leva um REBAIXO cilíndrico no topo (Ø6,5 × 10 mm) para a cabeça do M3.
/// RASGO LATERAL (stadium): atravessa o bloco no sentido X, na mesma coordenada Y de
/// porca_inferior_2 (10 mm do centro do bloco, do lado −Y). É um RASGO vertical, não um furo
/// redondo: a caixa rasgo_lateral_1 dá o trecho reto e dois cilindros — cilindro_rasgo_superior_1
/// e cilindro_rasgo_inferior_1, afastados ±RasgoMeiaAltura do eixo — arredondam as duas pontas.
/// O bloco é SIMÉTRICO em relação ao plano médio Y = 0, então existe a contraparte espelhada
/// do rasgo no lado +Y (rasgo_lateral_2, cilindro_rasgo_superior_2 e cilindro_rasgo_inferior_2),
/// na cota −YFuroLateral.
/// Como aquela porca fica abaixo da base do bloco, a cota Z do eixo é livre (ZFuroLateral).
/// VAZIO DA FACE DIREITA: caixa que ocupa toda a face direita (o comprimento Y inteiro) a partir
/// de AlturaInicioVazioFaceDireita e avança ProfundidadeVazioFaceDireita para dentro (−X). Como
/// vai além do topo, forma um degrau no canto superior direito do bloco.
/// Como na placa, o chanfro das arestas do CORPO BRUTO é cortado ANTES dos vazios: o chanfro
/// genérico é um algoritmo de MALHA que faz uma booleana por aresta convexa e, aplicado ao
/// sólido já furado, custaria centenas de booleanas encadeadas.
/// </summary>
public sealed record BlocoConexaoPlacaCarrinhoY(
    double Largura = 15,
    double Comprimento = 48,
    double Altura = 25,
    double PassoFuros = 25,
    double DiametroFuro = 3.4,
    double DistanciaFuroFaceDireita = 3.5,
    double DiametroRebaixo = 6.5,
    double ProfundidadeRebaixo = 20,
    double DiametroFuroLateral = 3.4,
    double YFuroLateral = -10,
    double ZFuroLateral = 12.5,
    double RasgoMeiaAltura = 6.0,
    double AlturaInicioVazioFaceDireita = 4.0,
    double ProfundidadeVazioFaceDireita = 8.0,
    double RaioChanfro = 1.0) : Part
{
    protected override IFeature Build()
    {
        // A face direita (+X) fica a DistanciaFuroFaceDireita do eixo dos furos (para cair sobre a
        // face mais à direita da placa); o resto da largura cresce para −X.
        var xMin = DistanciaFuroFaceDireita - Largura;
        var xMax = DistanciaFuroFaceDireita;

        var corpo_bloco = new BoxFeature(new Point3D(xMin, -Comprimento / 2, 0), Largura, Comprimento, Altura) { Name = "corpo_bloco" };

        // Chanfro (bevel) nas arestas do envelope, cortado antes dos vazios (ver o <summary>).
        IFeature corpo = corpo_bloco;
        if (RaioChanfro > 0.001)
        {
            var corpo_chanfrado = new FilletFeature("corpo_chanfrado", corpo_bloco, RaioChanfro)
            {
                Mode = FilletMode.Bevel,
                ApplyToAllEdges = true
            };
            corpo = corpo_chanfrado;
        }

        // Furos passantes verticais, alinhados aos da placa (mesmo passo em Y e mesmo Ø).
        var furo_fixacao_1 = new CylinderFeature(new Point3D(0, -PassoFuros / 2, -1), Vector3D.UnitZ, DiametroFuro / 2, Altura + 2) { Name = "furo_fixacao_1", IsTransientTool = true };
        var furo_fixacao_2 = new CylinderFeature(new Point3D(0, PassoFuros / 2, -1), Vector3D.UnitZ, DiametroFuro / 2, Altura + 2) { Name = "furo_fixacao_2", IsTransientTool = true };

        // Rebaixos cilíndricos no topo de cada furo (Ø6,5 × 10 mm) para a cabeça do parafuso.
        var rebaixo_1 = new CylinderFeature(new Point3D(0, -PassoFuros / 2, Altura - ProfundidadeRebaixo), Vector3D.UnitZ, DiametroRebaixo / 2, ProfundidadeRebaixo + 1) { Name = "rebaixo_1", IsTransientTool = true };
        var rebaixo_2 = new CylinderFeature(new Point3D(0, PassoFuros / 2, Altura - ProfundidadeRebaixo), Vector3D.UnitZ, DiametroRebaixo / 2, ProfundidadeRebaixo + 1) { Name = "rebaixo_2", IsTransientTool = true };

        // --- Rasgo lateral (stadium) no lado −Y, na cota Y de porca_inferior_2 ---
        // Trecho reto: caixa que atravessa o bloco no sentido X e cobre as duas pontas (±RasgoMeiaAltura).
        var rasgo_lateral_1 = new BoxFeature(new Point3D(xMin - 1, YFuroLateral - DiametroFuroLateral / 2, ZFuroLateral - RasgoMeiaAltura), Largura + 2, DiametroFuroLateral, 2.0 * RasgoMeiaAltura) { Name = "rasgo_lateral_1", IsTransientTool = true };
        // Ponta superior: cilindro que arredonda a extremidade de cima do rasgo.
        var cilindro_rasgo_superior_1 = new CylinderFeature(new Point3D(xMax + 1, YFuroLateral, ZFuroLateral + RasgoMeiaAltura), new Vector3D(-1, 0, 0), DiametroFuroLateral / 2, Largura + 2) { Name = "cilindro_rasgo_superior_1", IsTransientTool = true };
        // Ponta inferior: cilindro que arredonda a extremidade de baixo do rasgo.
        var cilindro_rasgo_inferior_1 = new CylinderFeature(new Point3D(xMax + 1, YFuroLateral, ZFuroLateral - RasgoMeiaAltura), new Vector3D(-1, 0, 0), DiametroFuroLateral / 2, Largura + 2) { Name = "cilindro_rasgo_inferior_1", IsTransientTool = true };

        // --- Contraparte espelhada do rasgo, no lado +Y (simetria em relação ao plano Y = 0) ---
        var rasgo_lateral_2 = new BoxFeature(new Point3D(xMin - 1, -YFuroLateral - DiametroFuroLateral / 2, ZFuroLateral - RasgoMeiaAltura), Largura + 2, DiametroFuroLateral, 2.0 * RasgoMeiaAltura) { Name = "rasgo_lateral_2", IsTransientTool = true };
        var cilindro_rasgo_superior_2 = new CylinderFeature(new Point3D(xMax + 1, -YFuroLateral, ZFuroLateral + RasgoMeiaAltura), new Vector3D(-1, 0, 0), DiametroFuroLateral / 2, Largura + 2) { Name = "cilindro_rasgo_superior_2", IsTransientTool = true };
        var cilindro_rasgo_inferior_2 = new CylinderFeature(new Point3D(xMax + 1, -YFuroLateral, ZFuroLateral - RasgoMeiaAltura), new Vector3D(-1, 0, 0), DiametroFuroLateral / 2, Largura + 2) { Name = "cilindro_rasgo_inferior_2", IsTransientTool = true };

        // --- Vazio que ocupa a face direita, a partir de 10 mm de altura ---
        var vazio_face_direita_1 = new BoxFeature(new Point3D(xMax - ProfundidadeVazioFaceDireita, -Comprimento / 2, AlturaInicioVazioFaceDireita), ProfundidadeVazioFaceDireita + 1, Comprimento, Altura - AlturaInicioVazioFaceDireita + 1) { Name = "vazio_face_direita_1", IsTransientTool = true };

        return BooleanFeature.CreateDifference("bloco_conexao_placa_carrinho_y", corpo,
            furo_fixacao_1, furo_fixacao_2, rebaixo_1, rebaixo_2,
            rasgo_lateral_1, cilindro_rasgo_superior_1, cilindro_rasgo_inferior_1,
            rasgo_lateral_2, cilindro_rasgo_superior_2, cilindro_rasgo_inferior_2,
            vazio_face_direita_1);
    }
}