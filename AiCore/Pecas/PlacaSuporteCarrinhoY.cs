// ===== Pecas/PlacaSuporteCarrinhoY.cs =====
/// <summary>
/// Placa/suporte impresso que se parafusa sobre um carrinho linear MGN15H (ver
/// <see cref="CarrinhoMgn15H"/>). Envelope 32 × 48 mm em planta, 11 mm de espessura, com
/// recorte em um dos cantos. Fixação ao carrinho por 3 furos passantes Ø3,4 na
/// malha 25 × 25 mm dos furos M3 do topo do carrinho (o 4º canto coincide com o recorte,
/// por isso usa 3 parafusos).
/// FIXAÇÕES COM PORCA M3: cada ponto é UMA instância de <see cref="FuroPorca"/> (furo + alojamento
/// hexagonal derivados das mesmas variáveis, então sempre coaxiais), subtraída do corpo:
///  - porca_inferior_1/2/3: inserção AXIAL pela face inferior (alojamento de 4 mm entre
///    z = -6,5 e -2,5, aberto para baixo); o furo sobe até 1 mm acima da face superior;
///  - fixacao_x_1, fixacao_x_2, fixacao_x_5, fixacao_x_6, fixacao_x_7 e fixacao_y_2: furos
///    horizontais que entram por uma face externa; a porca fica no fundo, inserida por um canal
///    aberto na face superior (mesmo conceito do <see cref="BlocoFixacaoMgn9"/>).
/// Em todas, o furo passa folgaFuroAlemPorca (8 mm) além de CADA lado do alojamento, para o
/// parafuso atravessar a porca com espaço livre para a ponta.
/// ALOJAMENTOS DA FACE INFERIOR: ficam abertos para baixo, na PRIMEIRA camada da impressão — onde o
/// bico costuma espalhar/empastar mais material — e recebem folgaAlojamentoVertical (0,2 mm) POR
/// LADO, ~0,4 mm a mais entre faces, para a porca entrar sem forçar. Os horizontais não.
/// porca_inferior_3 fica em y = 12,5, alinhada com furo_fixacao_carrinho_2.
/// As arestas do CORPO BRUTO (envelope extrudado) recebem um chanfro (bevel) de RaioChanfro mm,
/// cortado ANTES dos furos: o chanfro genérico é um algoritmo de MALHA que faz uma booleana por
/// aresta convexa, então aplicá-lo ao sólido já furado custaria ~950 booleanas encadeadas (cada
/// boca de furo redonda vira uma aresta por segmento de tesselagem) e a cena travaria. Mesma
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

        // Folga do furo de cada porca para os DOIS lados do alojamento (ver o <summary>).
        const double folgaFuroAlemPorca = 8.0;

        // Fixação ao carrinho MGN15H: 3 furos passantes (o 4º canto é o recorte).
        var furo_fixacao_carrinho_1 = new CylinderFeature(new Point3D(passoFixacao / 2 * sx, -passoFixacao / 2, -6.5), Vector3D.UnitZ, raioFuro, 13) { Name = "furo_fixacao_carrinho_1", IsTransientTool = true };
        var furo_fixacao_carrinho_2 = new CylinderFeature(new Point3D(passoFixacao / 2 * sx, passoFixacao / 2, -6.5), Vector3D.UnitZ, raioFuro, 13) { Name = "furo_fixacao_carrinho_2", IsTransientTool = true };
        var furo_fixacao_carrinho_3 = new CylinderFeature(new Point3D(-passoFixacao / 2 * sx, passoFixacao / 2, -6.5), Vector3D.UnitZ, raioFuro, 13) { Name = "furo_fixacao_carrinho_3", IsTransientTool = true };

        // -------------------------------------------------------------------------------
        // Porcas da face inferior (inserção axial). O alojamento abre 1 mm abaixo da base
        // (z = -6,5) e tem 4 mm; o furo começa folgaFuroAlemPorca abaixo dele (z = -14,5) e
        // sobe até 1 mm acima da face superior (z = 6,5), por isso FolgaDepois = 9.
        // EixoHexagono = +Y: vértices em ±Y, faces planas nas laterais (±X).
        // -------------------------------------------------------------------------------
        const double zBaseAlojamentoVertical = -6.5;
        const double alturaAlojamentoVertical = 4.0;
        const double folgaAlojamentoVertical = 0.2;     // folga extra por lado (1ª camada)
        var folgaAcimaPorcaVertical = 5.5 + 1.0 - (zBaseAlojamentoVertical + alturaAlojamentoVertical);   // 9

        var porca_inferior_1 = new FuroPorca(new Point3D(5.5 * sx, -8, zBaseAlojamentoVertical), Vector3D.UnitZ, 0,
            Insercao: InsercaoPorca.Axial, EixoHexagono: Vector3D.UnitY, FolgaAntes: folgaFuroAlemPorca, FolgaDepois: folgaAcimaPorcaVertical,
            FolgaAlojamento: folgaAlojamentoVertical, EspessuraAlojamento: alturaAlojamentoVertical).Instance("porca_inferior_1");
        var porca_inferior_2 = new FuroPorca(new Point3D(-4.5 * sx, 10, zBaseAlojamentoVertical), Vector3D.UnitZ, 0,
            Insercao: InsercaoPorca.Axial, EixoHexagono: Vector3D.UnitY, FolgaAntes: folgaFuroAlemPorca, FolgaDepois: folgaAcimaPorcaVertical,
            FolgaAlojamento: folgaAlojamentoVertical, EspessuraAlojamento: alturaAlojamentoVertical).Instance("porca_inferior_2");
        // Y = passoFixacao / 2 (12,5): mesma coordenada Y do furo_fixacao_carrinho_2.
        var porca_inferior_3 = new FuroPorca(new Point3D(5.5 * sx, passoFixacao / 2, zBaseAlojamentoVertical), Vector3D.UnitZ, 0,
            Insercao: InsercaoPorca.Axial, EixoHexagono: Vector3D.UnitY, FolgaAntes: folgaFuroAlemPorca, FolgaDepois: folgaAcimaPorcaVertical,
            FolgaAlojamento: folgaAlojamentoVertical, EspessuraAlojamento: alturaAlojamentoVertical).Instance("porca_inferior_3");

        // -------------------------------------------------------------------------------
        // Fixações horizontais periféricas (inserção lateral). O parafuso ENTRA por uma face
        // externa 1 mm fora dela (|x| = 17 ou y = 25), segue para DENTRO e a porca fica no fundo,
        // inserida por um canal que extrapola 1 mm o topo da placa (corte limpo).
        // -------------------------------------------------------------------------------
        const double zFuros = 1.125;        // Z (local) do eixo dos furos horizontais
        const double zTopoPlaca = 5.5;
        var hTopoPorca = zTopoPlaca - zFuros + 1.0;

        // Face externa direita (x = 16): os alojamentos de fixacao_x_5/6/7 ficam a 5 mm dela
        // (borda mais próxima). Entrando 1 mm fora (|x| = 17): profundidade = 17 − (16 − 5) = 6 mm.
        const double profAlojamentoProximoFace = 6.0;

        // Entram pela face esquerda (x = -16) e seguem para +X.
        var fixacao_x_1 = new FuroPorca(new Point3D(-17 * sx, 19, zFuros), new Vector3D(sx, 0, 0), 4,
            ComprimentoCanal: hTopoPorca, FolgaAntes: folgaFuroAlemPorca, FolgaDepois: folgaFuroAlemPorca).Instance("fixacao_x_1");
        var fixacao_x_2 = new FuroPorca(new Point3D(-17 * sx, 4, zFuros), new Vector3D(sx, 0, 0), 4,
            ComprimentoCanal: hTopoPorca, FolgaAntes: folgaFuroAlemPorca, FolgaDepois: folgaFuroAlemPorca).Instance("fixacao_x_2");

        // Entram pela face direita (x = +16) e seguem para −X; alojamentos alinhados em X, a 5 mm da face.
        // Em y = +2 NÃO existe a parede do recorte (ele só vai até y = -0.406), por isso fixacao_x_6
        // também entra pela face direita. fixacao_x_7 fica na mesma cota Y de fixacao_x_1.
        var fixacao_x_5 = new FuroPorca(new Point3D(17 * sx, -18, zFuros), new Vector3D(-sx, 0, 0), profAlojamentoProximoFace,
            ComprimentoCanal: hTopoPorca, FolgaAntes: folgaFuroAlemPorca, FolgaDepois: folgaFuroAlemPorca).Instance("fixacao_x_5");
        var fixacao_x_6 = new FuroPorca(new Point3D(17 * sx, 2, zFuros), new Vector3D(-sx, 0, 0), profAlojamentoProximoFace,
            ComprimentoCanal: hTopoPorca, FolgaAntes: folgaFuroAlemPorca, FolgaDepois: folgaFuroAlemPorca).Instance("fixacao_x_6");
        var fixacao_x_7 = new FuroPorca(new Point3D(17 * sx, 19, zFuros), new Vector3D(-sx, 0, 0), profAlojamentoProximoFace,
            ComprimentoCanal: hTopoPorca, FolgaAntes: folgaFuroAlemPorca, FolgaDepois: folgaFuroAlemPorca).Instance("fixacao_x_7");

        // Entra pelo topo (y = +24) e desce; o alojamento fica ~4,08 mm da face.
        var fixacao_y_2 = new FuroPorca(new Point3D(0.5 * sx, 25, zFuros), new Vector3D(0, -1, 0), 5.084,
            ComprimentoCanal: hTopoPorca, FolgaAntes: folgaFuroAlemPorca, FolgaDepois: folgaFuroAlemPorca).Instance("fixacao_y_2");

        var placa_suporte_carrinho_y = BooleanFeature.CreateDifference("placa_suporte_carrinho_y", corpo,
            furo_fixacao_carrinho_1, furo_fixacao_carrinho_2, furo_fixacao_carrinho_3,
            porca_inferior_1, porca_inferior_2, porca_inferior_3,
            fixacao_x_1, fixacao_x_2, fixacao_x_5, fixacao_x_6, fixacao_x_7,
            fixacao_y_2);

        return placa_suporte_carrinho_y;
    }
}
