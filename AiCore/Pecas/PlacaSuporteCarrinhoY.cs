/// <summary>
/// Placa/suporte impresso que se parafusa sobre um carrinho linear MGN15H (ver
/// <see cref="CarrinhoMgn15H"/>). Envelope 32 × 48 mm em planta, 11 mm de espessura, com
/// recorte em um dos cantos. Fixação ao carrinho por 3 furos passantes Ø3,4 na
/// malha 25 × 25 mm dos furos M3 do topo do carrinho (o 4º canto coincide com o recorte,
/// por isso usa 3 parafusos). Inclui alojamentos hexagonais para porcas M3, com canal de
/// inserção aberto na face superior, nos 3 furos verticais e apenas nos furos horizontais
/// (X e Y) cujo furo vaza para uma face externa da placa — por onde o parafuso entra de
/// fora e rosqueia na porca. Furos internos que não saem do sólido não recebem porca.
/// Mesmo estilo dos alojamentos de porca do <see cref="BlocoFixacaoMgn9"/>.
/// Furos horizontais Ø3,4 para fixação periférica.
/// Origem local (0,0,0) no centro da placa, na meia-espessura (base em Z = -5,5).
/// Com EspelhadoNoEixoY = true, toda a geometria é refletida em relação ao eixo Y local
/// (X → −X), gerando a placa direita como imagem especular da esquerda.
/// </summary>
public sealed record PlacaSuporteCarrinhoY(bool EspelhadoNoEixoY = false) : Part
{
    protected override IFeature Build()
    {
        // Fator de espelhamento em X: +1 (normal) ou −1 (imagem especular em relação ao eixo Y).
        var sx = EspelhadoNoEixoY ? -1.0 : 1.0;

        var perfil_base = new PolygonProfile(new System.Collections.Generic.List<Point3D>
        {
            new Point3D(16 * sx, -24, -5.5),
            new Point3D(16 * sx, 24, -5.5),
            new Point3D(-16 * sx, 24, -5.5),
            new Point3D(-16 * sx, -0.406, -5.5),
            new Point3D(-1 * sx, -0.406, -5.5),
            new Point3D(-1 * sx, -24, -5.5)
        });
        var corpo_placa = new ExtrudeFeature(perfil_base, 11, new Vector3D(0, 0, 11), "corpo_placa");

        const double passoFixacao = 25;   // malha 25 × 25 dos furos M3 do carrinho
        const double raioFuro = 1.7;      // Ø3,4 (folga p/ M3)

        // Fixação ao carrinho MGN15H: 3 furos passantes (o 4º canto é o recorte).
        var furo_fixacao_carrinho_1 = new CylinderFeature(new Point3D(passoFixacao / 2 * sx, -passoFixacao / 2, -6.5), Vector3D.UnitZ, raioFuro, 13) { Name = "furo_fixacao_carrinho_1", IsTransientTool = true };
        var furo_fixacao_carrinho_2 = new CylinderFeature(new Point3D(passoFixacao / 2 * sx, passoFixacao / 2, -6.5), Vector3D.UnitZ, raioFuro, 13) { Name = "furo_fixacao_carrinho_2", IsTransientTool = true };
        var furo_fixacao_carrinho_3 = new CylinderFeature(new Point3D(-passoFixacao / 2 * sx, passoFixacao / 2, -6.5), Vector3D.UnitZ, raioFuro, 13) { Name = "furo_fixacao_carrinho_3", IsTransientTool = true };

        // 3 alojamentos hexagonais p/ porca M3 (furos verticais).
        var perfil_porca_1 = new PolygonProfile(new System.Collections.Generic.List<Point3D>
        {
            new Point3D(5.5 * sx, -11.37, -6.5), new Point3D(2.581 * sx, -9.685, -6.5), new Point3D(2.581 * sx, -6.315, -6.5),
            new Point3D(5.5 * sx, -4.63, -6.5), new Point3D(8.419 * sx, -6.315, -6.5), new Point3D(8.419 * sx, -9.685, -6.5)
        });
        var alojamento_porca_1 = new ExtrudeFeature(perfil_porca_1, 4, new Vector3D(0, 0, 4), "alojamento_porca_1") { IsTransientTool = true };

        var perfil_porca_2 = new PolygonProfile(new System.Collections.Generic.List<Point3D>
        {
            new Point3D(-1.581 * sx, 10.315, -6.5), new Point3D(-4.5 * sx, 8.63, -6.5), new Point3D(-7.419 * sx, 10.315, -6.5),
            new Point3D(-7.419 * sx, 13.685, -6.5), new Point3D(-4.5 * sx, 15.37, -6.5), new Point3D(-1.581 * sx, 13.685, -6.5)
        });
        var alojamento_porca_2 = new ExtrudeFeature(perfil_porca_2, 4, new Vector3D(0, 0, 4), "alojamento_porca_2") { IsTransientTool = true };

        var perfil_porca_3 = new PolygonProfile(new System.Collections.Generic.List<Point3D>
        {
            new Point3D(8.419 * sx, 13.685, -6.5), new Point3D(8.419 * sx, 10.315, -6.5), new Point3D(5.5 * sx, 8.63, -6.5),
            new Point3D(2.581 * sx, 10.315, -6.5), new Point3D(2.581 * sx, 13.685, -6.5), new Point3D(5.5 * sx, 15.37, -6.5)
        });
        var alojamento_porca_3 = new ExtrudeFeature(perfil_porca_3, 4, new Vector3D(0, 0, 4), "alojamento_porca_3") { IsTransientTool = true };

        // Furos verticais parciais Ø3,4 (entrada dos parafusos das porcas M3).
        var furo_porca_1 = new CylinderFeature(new Point3D(5.5 * sx, 12, -2.5), Vector3D.UnitZ, raioFuro, 9) { Name = "furo_porca_1", IsTransientTool = true };
        var furo_porca_2 = new CylinderFeature(new Point3D(-4.5 * sx, 12, -2.5), Vector3D.UnitZ, raioFuro, 9) { Name = "furo_porca_2", IsTransientTool = true };
        var furo_porca_3 = new CylinderFeature(new Point3D(5.5 * sx, -8, -2.5), Vector3D.UnitZ, raioFuro, 9) { Name = "furo_porca_3", IsTransientTool = true };

        // -------------------------------------------------------------------------------
        // Fixações horizontais periféricas: cada ponto é um par coerente furo + porca.
        // O parafuso ENTRA por uma face externa da placa (entrada_*), segue para DENTRO
        // (direcao_*) por uma profundidade (prof_*), e a porca M3 fica alojada no fundo do
        // furo. A posição/orientação da porca é DERIVADA das mesmas variáveis do furo
        // (PlanoPorca), então furo e porca permanecem sempre coerentes. Mesmo conceito do
        // alojamento hexagonal com canal de inserção para +Z do BlocoFixacaoMgn9.
        // -------------------------------------------------------------------------------
        const double larguraPorca = 5.83;
        const double espessuraPorca = 3.0;
        const double zFuros = 1.125;        // Z (local) do eixo dos furos horizontais
        const double zTopoPlaca = 5.5;
        var hTopoPorca = zTopoPlaca - zFuros + 1.0; // canal de inserção extrapola o topo p/ corte limpo

        // Perfil hexagonal da porca + canal de inserção: função local que só CALCULA pontos
        // (o sólido em si é declarado em cada alojamento_porca_* abaixo).
        System.Collections.Generic.List<Point3D> PerfilPorcaHorizontal()
        {
            var r = (larguraPorca / 2.0) / (System.Math.Sqrt(3.0) / 2.0); // raio até os vértices
            return new System.Collections.Generic.List<Point3D>
            {
                new Point3D(0, -r, 0),
                new Point3D(larguraPorca / 2.0, -r / 2.0, 0),
                new Point3D(larguraPorca / 2.0, r / 2.0, 0),
                new Point3D(larguraPorca / 2.0, hTopoPorca, 0),
                new Point3D(-larguraPorca / 2.0, hTopoPorca, 0),
                new Point3D(-larguraPorca / 2.0, r / 2.0, 0),
                new Point3D(-larguraPorca / 2.0, -r / 2.0, 0)
            };
        }

        // Plano da porca no fundo do furo, DERIVADO da entrada/direção/profundidade do furo:
        // função local que só CALCULA um WorkPlane. Normal = direção do parafuso; eixo U
        // perpendicular no plano XY mantém o canal de inserção apontando p/ +Z (como no
        // BlocoFixacaoMgn9).
        WorkPlane PlanoPorca(Point3D entrada, Vector3D direcao, double profundidade)
        {
            var fundo = entrada + direcao * profundidade;
            var eixoU = new Vector3D(-direcao.Y, direcao.X, 0);
            return new WorkPlane(fundo, direcao, eixoU);
        }

        // Furos X: entram por uma face externa (esquerda x=-16, parede do recorte x=-1 ou
        // direita x=+16) e seguem para dentro; a porca fica no fundo.
        var entrada_x_1 = new Point3D(-17 * sx, 19, zFuros);
        var direcao_x_1 = new Vector3D(sx, 0, 0);
        const double prof_x_1 = 4;
        var furo_x_1 = new CylinderFeature(entrada_x_1, direcao_x_1, raioFuro, prof_x_1) { Name = "furo_x_1", IsTransientTool = true };
        var alojamento_porca_x_1 = new PrismFeature(PerfilPorcaHorizontal(), espessuraPorca, PlanoPorca(entrada_x_1, direcao_x_1, prof_x_1), "alojamento_porca_x_1") { IsTransientTool = true };

        var entrada_x_2 = new Point3D(-17 * sx, 4, zFuros);
        var direcao_x_2 = new Vector3D(sx, 0, 0);
        const double prof_x_2 = 4;
        var furo_x_2 = new CylinderFeature(entrada_x_2, direcao_x_2, raioFuro, prof_x_2) { Name = "furo_x_2", IsTransientTool = true };
        var alojamento_porca_x_2 = new PrismFeature(PerfilPorcaHorizontal(), espessuraPorca, PlanoPorca(entrada_x_2, direcao_x_2, prof_x_2), "alojamento_porca_x_2") { IsTransientTool = true };

        var entrada_x_5 = new Point3D(-1 * sx, -16, zFuros);
        var direcao_x_5 = new Vector3D(sx, 0, 0);
        const double prof_x_5 = 11;
        var furo_x_5 = new CylinderFeature(entrada_x_5, direcao_x_5, raioFuro, prof_x_5) { Name = "furo_x_5", IsTransientTool = true };
        var alojamento_porca_x_5 = new PrismFeature(PerfilPorcaHorizontal(), espessuraPorca, PlanoPorca(entrada_x_5, direcao_x_5, prof_x_5), "alojamento_porca_x_5") { IsTransientTool = true };

        var entrada_x_6 = new Point3D(-1 * sx, -1, zFuros);
        var direcao_x_6 = new Vector3D(sx, 0, 0);
        const double prof_x_6 = 11;
        var furo_x_6 = new CylinderFeature(entrada_x_6, direcao_x_6, raioFuro, prof_x_6) { Name = "furo_x_6", IsTransientTool = true };
        var alojamento_porca_x_6 = new PrismFeature(PerfilPorcaHorizontal(), espessuraPorca, PlanoPorca(entrada_x_6, direcao_x_6, prof_x_6), "alojamento_porca_x_6") { IsTransientTool = true };

        var entrada_x_8 = new Point3D(17 * sx, -16, zFuros);
        var direcao_x_8 = new Vector3D(-sx, 0, 0);
        const double prof_x_8 = 4;
        var furo_x_8 = new CylinderFeature(entrada_x_8, direcao_x_8, raioFuro, prof_x_8) { Name = "furo_x_8", IsTransientTool = true };
        var alojamento_porca_x_8 = new PrismFeature(PerfilPorcaHorizontal(), espessuraPorca, PlanoPorca(entrada_x_8, direcao_x_8, prof_x_8), "alojamento_porca_x_8") { IsTransientTool = true };

        // Furo Y: entra pelo topo (y=+24) e desce; a porca fica no fundo.
        var entrada_y_2 = new Point3D(0.5 * sx, 25, zFuros);
        var direcao_y_2 = new Vector3D(0, -1, 0);
        const double prof_y_2 = 3.084;
        var furo_y_2 = new CylinderFeature(entrada_y_2, direcao_y_2, raioFuro, prof_y_2) { Name = "furo_y_2", IsTransientTool = true };
        var alojamento_porca_y_2 = new PrismFeature(PerfilPorcaHorizontal(), espessuraPorca, PlanoPorca(entrada_y_2, direcao_y_2, prof_y_2), "alojamento_porca_y_2") { IsTransientTool = true };

        return BooleanFeature.CreateDifference("placa_suporte_carrinho_y", corpo_placa,
            furo_fixacao_carrinho_1, furo_fixacao_carrinho_2, furo_fixacao_carrinho_3,
            alojamento_porca_1, alojamento_porca_2, alojamento_porca_3,
            furo_porca_1, furo_porca_2, furo_porca_3,
            furo_x_1, furo_x_2, furo_x_5, furo_x_6, furo_x_8,
            furo_y_2,
            alojamento_porca_x_1, alojamento_porca_x_2,
            alojamento_porca_x_5, alojamento_porca_x_6, alojamento_porca_x_8,
            alojamento_porca_y_2);
    }
}