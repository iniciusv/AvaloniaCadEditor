namespace AvaloniaCadEditor.Application.Ai;

/// <summary>
/// Fonte única das regras e dos exemplos do "CadScript" — o sub-dialeto de C# usado para descrever
/// uma cena. O mesmo texto é consumido pelos três lugares que precisam ensiná-lo:
/// <list type="bullet">
///   <item>o system prompt do chat de IA (<see cref="CadScriptProtocol.GetSystemPrompt"/>);</item>
///   <item>o <c>.cad/api.md</c> gerado dentro de cada cena;</item>
///   <item>o <c>AGENTS.md</c> gerado para IAs externas (ex.: Claude Code rodando na pasta da cena).</item>
/// </list>
/// Antes, cada um tinha a sua própria redação das mesmas regras, e elas divergiam com o tempo.
/// Cada bloco é uma lista de bullets em markdown, sem título: quem consome põe o seu próprio
/// cabeçalho. Nada aqui pode depender do formato de um consumidor específico.
/// </summary>
public static class CadScriptRules
{
    /// <summary>Regras válidas para qualquer arquivo de uma cena.</summary>
    public const string Common =
        """
        - Unidades em milímetros. O eixo Z aponta para cima.
        - NÃO use Console, System.IO, rede, threads, reflexão ou qualquer tipo de I/O.
        - Todo número decimal usa ponto ('.'), nunca vírgula.
        - Nomes de variáveis são únicos, curtos e em snake_case (ex.: box_a1, hole_b2, fillet_c3).
        """;

    /// <summary>
    /// A regra mais importante do dialeto: o app rastreia cada sólido pelo nome para ligá-lo à sua
    /// linha de código, à seleção na viewport e à árvore da cena.
    /// </summary>
    public const string TrackableSolids =
        """
        - SÓLIDOS RASTREÁVEIS POR NOME: variáveis, const, arrays, laços e funções locais podem ser usados
          para CALCULAR valores (posições, diâmetros, passos), mas cada sólido (BoxFeature, CylinderFeature,
          PrismFeature, ExtrudeFeature, FilletFeature, BooleanFeature...) deve ser criado numa declaração
          própria, com Name LITERAL igual ao nome da variável e único no arquivo:
          var furo_1 = new CylinderFeature(...) { Name = "furo_1" };
        - NUNCA crie sólidos dentro de laços, LINQ (Select(... new CylinderFeature ...)) ou listas montadas
          dinamicamente, e NUNCA use nome interpolado ou concatenado ($"furo_{i}", "furo_" + i). Para N
          furos iguais, calcule as posições em variáveis e declare furo_1 ... furo_N, um por linha.
        """;

    /// <summary>O arquivo de entrada da cena (statements top-level) — formato "script".</summary>
    public const string SceneFile =
        """
        - Contém APENAS declarações e chamadas soltas (statements top-level). NUNCA use 'using',
          'namespace', 'class', 'struct', 'interface', 'record' nem declare métodos.
        - Toda feature deve ter Name = "<mesmo nome da variável>" e ser registrada com AddFeature(variavel).
        - Ferramentas booleanas transitórias (ex.: o cilindro usado apenas para furar) devem ter
          IsTransientTool = true e também devem ser registradas com AddFeature.
        - Para usar uma peça JÁ DECLARADA em Pecas/, NÃO redeclare o tipo aqui: chame
          var p = Add(new NomeDaPeca(...), at: Placement.At(x, y, z));
        - NUNCA declare uma peça/objeto reutilizável NOVO neste arquivo. Um objeto ou sólido reutilizável
          (motor, parafuso, suporte etc.) É uma peça: ele vai num arquivo próprio em Pecas/, mesmo que
          apareça só uma vez na cena.
        """;

    /// <summary>Os arquivos de Pecas/ — formato "peça" (e os auxiliares que convivem com eles).</summary>
    public const string PartFile =
        """
        - Um arquivo de peça contém EXATAMENTE UM
          'public sealed record NomeDaPeca(<parâmetros com valor padrão>) : Part { ... }'.
        - NUNCA use statements soltos, 'using', nem chame AddFeature/Add num arquivo de peça — isso é só
          do arquivo de entrada da cena.
        - 'protected override IFeature Build()' constrói a geometria em coordenadas LOCAIS (origem da peça
          em 0,0,0) e devolve UMA ÚNICA IFeature; não chama AddFeature/Add.
        - Para peças compostas (corpo + furos + eixo etc.), monte tudo DENTRO do próprio Build() com
          BooleanFeature.CreateUnion/CreateDifference/CreateIntersection e devolva o resultado final —
          instâncias de peça (PartInstance) ainda não podem ser operandos de BooleanFeature, só as
          features criadas dentro do Build().
        - Dê a cada parâmetro um nome em português e um valor padrão sensato (para que 'new NomeDaPeca()'
          já funcione sozinho); pode usar [System.ComponentModel.DataAnnotations.Range(min, max)] (nome
          completo, sem using) — valores fora do intervalo viram erro.
        - Em Pecas/ também pode haver arquivos AUXILIARES (ex.: uma static class Medidas com constantes):
          só declarações de tipos, sem statements soltos.
        """;

    /// <summary>Como o arquivo de entrada instancia e posiciona uma peça.</summary>
    public const string PartUsage =
        """
        - Instanciar na cena: var p1 = Add(new Placa(100, 60), at: Placement.At(0, 80, 0, rotationZDegrees: 90));
        - Variantes de uma peça: expressão 'with' do C# sobre p1.Part —
          var p2 = Add(p1.Part with { Espessura = 8 }, at: Placement.At(150, 0, 0));
        - Ponto local da peça convertido para o mundo: p2.World(p => new Point3D(p.Largura, 0, 0)).
        - color ("#RRGGBB") é opcional e pinta o sólido inteiro; só use se o pedido envolver cores.
        - Peças com os mesmos parâmetros compartilham a geometria (construída uma vez); cada instância só
          acrescenta a sua posição.
        """;

    /// <summary>Peça simples: uma chapa retangular.</summary>
    public const string PartExamplePlaca =
        """
        public sealed record Placa(double Largura = 100, double Comprimento = 60, double Espessura = 5) : Part
        {
            protected override IFeature Build() =>
                new BoxFeature(Point3D.Origin, Largura, Comprimento, Espessura) { Name = "placa" };
        }
        """;

    /// <summary>Peça composta: corpo com furos de fixação (diferença booleana dentro do Build()).</summary>
    public const string PartExampleChapaFurada =
        """
        public sealed record ChapaFurada(double Largura = 50, double Comprimento = 30, double Espessura = 5, double DiametroFuro = 5) : Part
        {
            protected override IFeature Build()
            {
                var corpo = new BoxFeature(Point3D.Origin, Largura, Comprimento, Espessura) { Name = "corpo" };
                var furo_1 = new CylinderFeature(new Point3D(DiametroFuro, DiametroFuro, 0), Vector3D.UnitZ, DiametroFuro / 2, Espessura) { Name = "furo_1", IsTransientTool = true };
                var furo_2 = new CylinderFeature(new Point3D(Largura - DiametroFuro, Comprimento - DiametroFuro, 0), Vector3D.UnitZ, DiametroFuro / 2, Espessura) { Name = "furo_2", IsTransientTool = true };
                return BooleanFeature.CreateDifference("chapa_furada", corpo, furo_1, furo_2);
            }
        }
        """;

    /// <summary>
    /// Medidas calculadas com const/variáveis, mas cada sólido declarado um a um com nome literal:
    /// o exemplo canônico de <see cref="TrackableSolids"/>.
    /// </summary>
    public const string PartExampleBlocoQuatroFuros =
        """
        public sealed record BlocoQuatroFuros(double Largura = 20, double Comprimento = 30, double Altura = 10) : Part
        {
            protected override IFeature Build()
            {
                const double passoX = 15;
                const double passoY = 10;
                const double diametroFuro = 3;
                const double profundidadeFuro = 3;

                var x1 = Largura / 2 - passoX / 2;
                var x2 = Largura / 2 + passoX / 2;
                var y1 = Comprimento / 2 - passoY / 2;
                var y2 = Comprimento / 2 + passoY / 2;
                var zFuro = Altura - profundidadeFuro;

                var corpo = new BoxFeature(Point3D.Origin, Largura, Comprimento, Altura) { Name = "corpo" };
                var furo_1 = new CylinderFeature(new Point3D(x1, y1, zFuro), Vector3D.UnitZ, diametroFuro / 2, profundidadeFuro) { Name = "furo_1", IsTransientTool = true };
                var furo_2 = new CylinderFeature(new Point3D(x2, y1, zFuro), Vector3D.UnitZ, diametroFuro / 2, profundidadeFuro) { Name = "furo_2", IsTransientTool = true };
                var furo_3 = new CylinderFeature(new Point3D(x1, y2, zFuro), Vector3D.UnitZ, diametroFuro / 2, profundidadeFuro) { Name = "furo_3", IsTransientTool = true };
                var furo_4 = new CylinderFeature(new Point3D(x2, y2, zFuro), Vector3D.UnitZ, diametroFuro / 2, profundidadeFuro) { Name = "furo_4", IsTransientTool = true };
                return BooleanFeature.CreateDifference("bloco_quatro_furos", corpo, furo_1, furo_2, furo_3, furo_4);
            }
        }
        """;

    /// <summary>
    /// Arquivo de ENTRADA que usa a peça <c>Placa</c> de <see cref="PartExamplePlaca"/> (não a redeclara).
    /// Ao contrário dos exemplos de peça, não compila sozinho — é mostrado/testado ao lado dela.
    /// </summary>
    public const string SceneUsingPartExample =
        """
        var placa_1 = Add(new Placa(100, 60));
        var placa_2 = Add(new Placa(100, 60), at: Placement.At(0, 80, 0));
        """;
}
