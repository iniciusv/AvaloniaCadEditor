// ===== Pecas/PlacaSuporteCarrinhoY.cs =====
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
/// Furos horizontais Ø3,4 para fixação periférica. A face externa (|x| = 16) tinha furo +
/// alojamento de porca apenas junto ao recorte (furo_x_5/alojamento_porca_x_5) e a meia
/// altura (furo_x_6/alojamento_porca_x_6); a extremidade oposta dessa MESMA face — a ponta
/// que não tinha esse furo (y = +19, mesma cota Y de furo_x_1 na face interna) — recebeu um
/// par equivalente: furo_x_7 com alojamento_porca_x_7, mesma face de entrada, mesma
/// profundidade (alojamento a 5 mm da face) e alinhado em X com os alojamentos vizinhos.
/// O par em Y (furo_y_2/alojamento_porca_y_2) entrou pelo topo (y = +24) e teve o
/// alojamento recuado 2 mm em relação à face mais próxima (prof_y_2 = 5.084), mantendo o
/// furo de entrada encontrando o alojamento da porca no fundo.
/// As arestas do CORPO BRUTO (envelope extrudado) recebem um chanfro (bevel) de RaioChanfro mm,
/// cortado ANTES dos furos: o chanfro genérico é um algoritmo de MALHA que faz uma booleana por
/// aresta convexa, então aplicá-lo ao sólido já furado custaria ~950 booleanas encadeadas (cada
/// boca de furo redonda vira uma aresta por segmento de tesselação) e a cena travaria. Mesma
/// ordem da <see cref="CantoneiraPlacaExtrusao2020"/>; de quebra, as bocas dos furos ficam vivas.
/// Origem local (0,0,0) no centro da placa, na meia-espessura (base em Z = -5,5).
/// Com EspelhadoNoEixoY = true, toda a geometria é refletida em relação ao eixo Y local
/// (X → −X), gerando a placa direita como imagem especular da esquerda.
/// </summary>
public sealed record PlacaSuporteCarrinhoY(bool EspelhadoNoEixoY = false, double RaioChanfro = 1.0) : Part
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
            new Point3D(2 * sx, -0.406, -5.5),
            new Point3D(2 * sx, -24, -5.5)
        });
        var corpo_placa = new ExtrudeFeature(perfil_base, 11, new Vector3D(0, 0, 11), "corpo_placa");

        // Chanfro (bevel) nas arestas do envelope, cortado ANTES dos furos — ver o <summary>:
        // depois dos furos o custo é proibitivo. As arestas côncavas (o canto interno do recorte)
        // são puladas pelo kernel.
        IFeature corpo = corpo_placa;
        if (RaioChanfro > 0.001)
        {
            var corpo_chanfrado = new FilletFeature("corpo_chanfrado", corpo_placa, RaioChanfro)
            {
                Mode = FilletMode.Bevel,
                ApplyToAllEdges = true
            };
            corpo = corpo_chanfrado;
        }

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
        // Y = passoFixacao / 2 (12,5): mesma coordenada Y do furo_fixacao_carrinho_2,
        // ficando alinhado com ele no eixo Y.
        var furo_porca_1 = new CylinderFeature(new Point3D(5.5 * sx, passoFixacao / 2, -2.5), Vector3D.UnitZ, raioFuro, 9) { Name = "furo_porca_1", IsTransientTool = true };
        // Deslocado 2 mm para frente (−Y local) em relação ao alojamento_porca_2.
        var furo_porca_2 = new CylinderFeature(new Point3D(-4.5 * sx, 10, -2.5), Vector3D.UnitZ, raioFuro, 9) { Name = "furo_porca_2", IsTransientTool = true };
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

        // Face externa direita da placa — é a face que os furos furo_x_5, furo_x_6 e
        // furo_x_7 cortam.
        const double faceExternaX = 16.0;
        // Os alojamentos alojamento_porca_x_5, alojamento_porca_x_6 e alojamento_porca_x_7
        // devem ficar a 5 mm dessa face (medido até a borda do alojamento mais próxima da
        // face). Como o furo entra 1 mm fora da face (|x| = 17), a profundidade necessária é:
        //   profundidade = 17 − (16 − 5) = 6 mm  →  borda do alojamento em |x| = 11 (5 mm da face).
        const double profAlojamentoProximoFace = 6.0;

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

        // O parafuso entra pela face externa direita (x = +16, início em 17*sx) e avança
        // para DENTRO (−sx). O alojamento da porca fica a 5 mm da face (profundidade menor),
        // permanecendo alinhado ao alojamento_porca_x_6 no eixo X.
        var entrada_x_5 = new Point3D(17 * sx, -18, zFuros);
        var direcao_x_5 = new Vector3D(-sx, 0, 0);
        const double prof_x_5 = profAlojamentoProximoFace;
        var furo_x_5 = new CylinderFeature(entrada_x_5, direcao_x_5, raioFuro, prof_x_5) { Name = "furo_x_5", IsTransientTool = true };
        var alojamento_porca_x_5 = new PrismFeature(PerfilPorcaHorizontal(), espessuraPorca, PlanoPorca(entrada_x_5, direcao_x_5, prof_x_5), "alojamento_porca_x_5") { IsTransientTool = true };

        // Alinhado ao alojamento_porca_x_5 no eixo X (mesma face de entrada e mesma
        // profundidade → mesmo |x| do alojamento, a 5 mm da face). Em y = +2 NÃO existe a
        // parede do recorte (o recorte só vai até y = -0.406), de modo que o furo entra pela
        // face externa mais próxima (x = +16) e segue para dentro (−X).
        var entrada_x_6 = new Point3D(17 * sx, 2, zFuros);
        var direcao_x_6 = new Vector3D(-sx, 0, 0);
        const double prof_x_6 = profAlojamentoProximoFace;
        var furo_x_6 = new CylinderFeature(entrada_x_6, direcao_x_6, raioFuro, prof_x_6) { Name = "furo_x_6", IsTransientTool = true };
        var alojamento_porca_x_6 = new PrismFeature(PerfilPorcaHorizontal(), espessuraPorca, PlanoPorca(entrada_x_6, direcao_x_6, prof_x_6), "alojamento_porca_x_6") { IsTransientTool = true };

        // Extremidade oposta da MESMA face externa (a ponta que não tinha esse furo):
        // em y = +19 — mesma cota Y de furo_x_1 na face interna, a 5 mm do topo da placa —
        // entra-se pela face externa (x = 17*sx) e segue para dentro (−sx), exatamente como
        // no alojamento_porca_x_6: mesma profundidade, alojamento da porca a 5 mm da face e
        // alinhado em X com alojamento_porca_x_5/alojamento_porca_x_6.
        var entrada_x_7 = new Point3D(17 * sx, 19, zFuros);
        var direcao_x_7 = new Vector3D(-sx, 0, 0);
        const double prof_x_7 = profAlojamentoProximoFace;
        var furo_x_7 = new CylinderFeature(entrada_x_7, direcao_x_7, raioFuro, prof_x_7) { Name = "furo_x_7", IsTransientTool = true };
        var alojamento_porca_x_7 = new PrismFeature(PerfilPorcaHorizontal(), espessuraPorca, PlanoPorca(entrada_x_7, direcao_x_7, prof_x_7), "alojamento_porca_x_7") { IsTransientTool = true };

        // Furo Y: entra pelo topo (y=+24) e desce; a porca fica no fundo.
        // O alojamento_porca_y_2 foi afastado 2 mm da face mais próxima (o topo y = +24):
        // antes ficava a ~2,08 mm (prof_y_2 = 3.084), agora fica a ~4,08 mm (prof_y_2 = 5.084).
        // Como furo e porca saem da mesma profundidade, o furo continua encontrando o
        // alojamento exatamente no fundo, sem deixar material entre eles.
        var entrada_y_2 = new Point3D(0.5 * sx, 25, zFuros);
        var direcao_y_2 = new Vector3D(0, -1, 0);
        const double prof_y_2 = 5.084;
        var furo_y_2 = new CylinderFeature(entrada_y_2, direcao_y_2, raioFuro, prof_y_2) { Name = "furo_y_2", IsTransientTool = true };
        var alojamento_porca_y_2 = new PrismFeature(PerfilPorcaHorizontal(), espessuraPorca, PlanoPorca(entrada_y_2, direcao_y_2, prof_y_2), "alojamento_porca_y_2") { IsTransientTool = true };

        var placa_suporte_carrinho_y = BooleanFeature.CreateDifference("placa_suporte_carrinho_y", corpo,
            furo_fixacao_carrinho_1, furo_fixacao_carrinho_2, furo_fixacao_carrinho_3,
            alojamento_porca_1, alojamento_porca_2, alojamento_porca_3,
            furo_porca_1, furo_porca_2, furo_porca_3,
            furo_x_1, furo_x_2, furo_x_5, furo_x_6, furo_x_7,
            furo_y_2,
            alojamento_porca_x_1, alojamento_porca_x_2,
            alojamento_porca_x_5, alojamento_porca_x_6, alojamento_porca_x_7,
            alojamento_porca_y_2);

        return placa_suporte_carrinho_y;
    }
}