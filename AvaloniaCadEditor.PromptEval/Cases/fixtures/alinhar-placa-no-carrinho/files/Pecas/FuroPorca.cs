/// <summary>Rosca do parafuso: define o furo de passagem e a porca da <see cref="FuroPorca"/>.</summary>
public enum Rosca { M3, M4, M5 }

/// <summary>Por onde a porca entra no alojamento da <see cref="FuroPorca"/>.</summary>
public enum InsercaoPorca
{
    /// <summary>Perpendicular ao furo, por um canal aberto no sentido de EixoHexagono.</summary>
    Lateral,
    /// <summary>Pelo próprio eixo do furo: o alojamento abre na face de entrada.</summary>
    Axial
}

/// <summary>
/// Vazio de fixação: furo de passagem + alojamento hexagonal de porca, para ser SUBTRAÍDO de outra
/// peça (no Build dela: <c>var fixacao_1 = new FuroPorca(...).Instance("fixacao_1");</c> e a instância
/// entra no CreateDifference). Furo e alojamento saem das mesmas variáveis, então ficam sempre coaxiais.
/// Coordenadas: as da peça hospedeira — a FuroPorca é construída já no lugar e instanciada sem Placement.
///  - Entrada: centro do furo onde o parafuso entra (por convenção, 1 mm FORA da face alvo, para o
///    corte não ficar rente à face).
///  - Direcao: para dentro do material.
///  - Profundidade: da Entrada até a face de entrada do alojamento (Axial: 0 = alojamento aberto na face).
///  - O furo começa FolgaAntes antes do alojamento e termina FolgaDepois depois dele, para o parafuso
///    atravessar a porca com espaço livre para a ponta.
///  - EixoHexagono: direção de um vértice do hexágono; na inserção Lateral é também o lado do canal,
///    que vai do eixo do furo até ComprimentoCanal (passe da face por onde a porca entra).
///    Padrão: +Z, ou +Y quando o furo é vertical.
///  - FolgaAlojamento: folga extra POR LADO do hexágono (ex.: 0,2 na primeira camada da impressão).
///  - Medidas por Rosca (porca com folga de impressão de ~0,33 entre faces e ~0,6 na espessura);
///    DiametroFuro, LarguraPorca e EspessuraAlojamento, quando informados, valem no lugar da tabela.
/// </summary>
public sealed record FuroPorca(
    Point3D Entrada = default,
    Vector3D? Direcao = null,
    double Profundidade = 4,
    Rosca Rosca = Rosca.M3,
    InsercaoPorca Insercao = InsercaoPorca.Lateral,
    Vector3D? EixoHexagono = null,
    double ComprimentoCanal = 5,
    double FolgaAntes = 8,
    double FolgaDepois = 8,
    double FolgaAlojamento = 0,
    double? EspessuraAlojamento = null,
    double? DiametroFuro = null,
    double? LarguraPorca = null) : Part
{
    protected override IFeature Build()
    {
        var direcao = (Direcao ?? Vector3D.UnitZ).Normalize();
        var eixoHexagono = EixoHexagono ?? (System.Math.Abs(direcao.Z) > 0.999 ? Vector3D.UnitY : Vector3D.UnitZ);

        // Furo de passagem, largura da porca entre faces e espessura, já com a folga de impressão.
        var (diametroTabela, larguraTabela, espessuraTabela) = Rosca switch
        {
            Rosca.M4 => (4.5, 7.33, 3.8),
            Rosca.M5 => (5.5, 8.33, 4.6),
            _ => (3.4, 5.83, 3.0)
        };
        var raioFuro = (DiametroFuro ?? diametroTabela) / 2;
        var apotema = (LarguraPorca ?? larguraTabela) / 2 + FolgaAlojamento;   // centro → face
        var raioVertice = apotema / (System.Math.Sqrt(3.0) / 2.0);              // centro → vértice
        var espessura = EspessuraAlojamento ?? espessuraTabela;

        if (Insercao == InsercaoPorca.Lateral && ComprimentoCanal <= raioVertice / 2)
        {
            throw new System.ArgumentOutOfRangeException(nameof(ComprimentoCanal), ComprimentoCanal,
                $"FuroPorca: o canal de inserção precisa passar de {raioVertice / 2:0.###} mm (meio lado do hexágono).");
        }

        // Plano do alojamento no fundo (Entrada + Direcao * Profundidade): normal = Direcao e eixo V
        // local = EixoHexagono, de modo que o vértice e o canal apontam para EixoHexagono.
        var fundo = Entrada + direcao * Profundidade;
        var plano = new WorkPlane(fundo, direcao, eixoHexagono.CrossProduct(direcao));

        var perfil = Insercao == InsercaoPorca.Lateral
            ? new System.Collections.Generic.List<Point3D>
            {
                new Point3D(0, -raioVertice, 0),
                new Point3D(apotema, -raioVertice / 2, 0),
                new Point3D(apotema, raioVertice / 2, 0),
                new Point3D(apotema, ComprimentoCanal, 0),
                new Point3D(-apotema, ComprimentoCanal, 0),
                new Point3D(-apotema, raioVertice / 2, 0),
                new Point3D(-apotema, -raioVertice / 2, 0)
            }
            : new System.Collections.Generic.List<Point3D>
            {
                new Point3D(0, -raioVertice, 0),
                new Point3D(apotema, -raioVertice / 2, 0),
                new Point3D(apotema, raioVertice / 2, 0),
                new Point3D(0, raioVertice, 0),
                new Point3D(-apotema, raioVertice / 2, 0),
                new Point3D(-apotema, -raioVertice / 2, 0)
            };

        var furo = new CylinderFeature(Entrada + direcao * (Profundidade - FolgaAntes), direcao, raioFuro, FolgaAntes + espessura + FolgaDepois) { Name = "furo" };
        var alojamento = new PrismFeature(perfil, espessura, plano, "alojamento");
        var furo_porca = BooleanFeature.CreateUnion("furo_porca", furo, alojamento);
        return furo_porca;
    }
}
