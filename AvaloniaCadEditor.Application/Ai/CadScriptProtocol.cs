using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using AiCore.Abstractions;

namespace AvaloniaCadEditor.Application.Ai;

/// <summary>
/// Protocolo de IA para o "CadScript": a IA recebe o código C# vigente do arquivo ativo da cena
/// mais o pedido em linguagem natural, e deve responder com um texto curto seguido (quando a
/// geometria muda) de um único bloco de código contendo o ARQUIVO COMPLETO resultante.
/// Uma cena pode ter um único arquivo (statements soltos) ou vários arquivos — nesse caso, o
/// arquivo ativo tanto pode ser o de entrada (mesmo formato) quanto um arquivo de peça em Pecas/
/// (um record que herda de Part). O formato exigido em cada turno vem em <see cref="CadScriptContext.Kind"/>
/// e é repetido na mensagem do usuário, já que pode mudar de um turno para o outro.
/// </summary>
public class CadScriptProtocol : IAiProtocol<CadScriptContext, string>
{
    private static readonly Regex CodeBlockRegex = new(
        @"```(?:cadscript|csharp|cs)?\s*\r?\n(?<code>.*?)```",
        RegexOptions.Singleline | RegexOptions.IgnoreCase);

    /// <summary>
    /// Exemplos few-shot de SCRIPT (arquivo único ou de entrada) mostrados à IA. Cobertos por teste
    /// que garante que cada um deles realmente compila e executa no <c>RoslynSceneEvaluator</c> —
    /// ou seja, o "dialeto" ensinado à IA é sempre válido.
    /// </summary>
    public static readonly IReadOnlyList<string> Examples = new[]
    {
        // Caixa simples
        """
        var box_a1b2 = new BoxFeature(baseCorner: new Point3D(0, 0, 0), width: 50, length: 30, height: 10) { Name = "box_a1b2" };
        AddFeature(box_a1b2);
        """,

        // Furo cilíndrico numa caixa (diferença booleana)
        """
        var box_1234 = new BoxFeature(baseCorner: new Point3D(0, 0, 0), width: 50, length: 30, height: 10) { Name = "box_1234" };
        AddFeature(box_1234);
        var hole_5678 = new CylinderFeature(globalBaseCenter: new Point3D(25, 15, 0), globalDirection: new Vector3D(0, 0, 1), radius: 4, height: 10) { Name = "hole_5678", IsTransientTool = true };
        AddFeature(hole_5678);
        var boolOp_9abc = BooleanFeature.CreateDifference("boolOp_9abc", box_1234, hole_5678);
        AddFeature(boolOp_9abc);
        """,

        // Fillet (arredondamento) em todas as arestas de uma caixa
        """
        var box_edge1 = new BoxFeature(baseCorner: new Point3D(0, 0, 0), width: 40, length: 40, height: 20) { Name = "box_edge1" };
        AddFeature(box_edge1);
        var fillet_edge2 = new FilletFeature(name: "fillet_edge2", baseFeature: box_edge1, radius: 3) { Mode = FilletMode.Round, ApplyToAllEdges = true };
        AddFeature(fillet_edge2);
        """,

        // Prisma extrudado a partir de um polígono (perfil em L) no plano padrão
        """
        var prism_L_verts = new[] { new Point3D(0, 0, 0), new Point3D(20, 0, 0), new Point3D(20, 10, 0), new Point3D(10, 10, 0), new Point3D(10, 20, 0), new Point3D(0, 20, 0) };
        var prism_L = new PrismFeature(prism_L_verts, height: 15) { Name = "prism_L" };
        AddFeature(prism_L);
        """
    };

    /// <summary>
    /// Exemplo de arquivo de ENTRADA que usa uma peça já declarada em Pecas/ (não a redeclara).
    /// Ao contrário de <see cref="Examples"/>, não compila sozinho (Placa não está declarada aqui) —
    /// é mostrado ao lado do primeiro <see cref="PartExamples"/> (que declara Placa) e testado junto
    /// dele, como os dois arquivos de um projeto de verdade.
    /// </summary>
    public const string SceneUsingPartExample =
        """
        var placa_1 = Add(new Placa(100, 60));
        var placa_2 = Add(new Placa(100, 60), at: Placement.At(0, 80, 0));
        """;

    /// <summary>
    /// Exemplos few-shot de ARQUIVO DE PEÇA (Pecas/*.cs): um único record que herda de Part.
    /// Cobertos por teste que compõe cada exemplo com um arquivo de entrada sintético
    /// (<c>Add(new Peca(...))</c>) e confirma que compila e executa no <c>RoslynSceneEvaluator</c>.
    /// </summary>
    public static readonly IReadOnlyList<string> PartExamples = new[]
    {
        // Peça simples: uma chapa retangular
        """
        public sealed record Placa(double Largura = 100, double Comprimento = 60, double Espessura = 5) : Part
        {
            protected override IFeature Build() =>
                new BoxFeature(Point3D.Origin, Largura, Comprimento, Espessura) { Name = "placa" };
        }
        """,

        // Peça composta: corpo com furos de fixação (diferença booleana dentro do Build())
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
        """,

        // Medidas calculadas com const/variáveis, mas cada sólido declarado um a um com nome literal
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
        """
    };

    public string GetSystemPrompt()
    {
        var sb = new StringBuilder();

        sb.AppendLine("Você é um assistente especializado em um editor CAD paramétrico 3D construído em C#.");
        sb.AppendLine("Sua tarefa é gerar/editar o código C# de UM ARQUIVO por vez da cena 3D. Uma cena pode ser:");
        sb.AppendLine("- um SCRIPT ÚNICO: statements top-level que descrevem toda a geometria (formato \"script\");");
        sb.AppendLine("- um PROJETO com vários arquivos: um arquivo de entrada (ex.: Cena.cs, também formato \"script\") e,");
        sb.AppendLine("  em Pecas/, arquivos de PEÇA que declaram tipos reutilizáveis (formato \"peça\").");
        sb.AppendLine("Cada pedido traz uma seção \"### ARQUIVO ATIVO\" dizendo o nome do arquivo em edição e qual desses");
        sb.AppendLine("dois formatos ele segue AGORA. Leia essa seção a cada turno — o formato pode mudar de um turno para");
        sb.AppendLine("o outro se o usuário trocar de arquivo — e responda EXATAMENTE nesse formato.");
        sb.AppendLine();
        sb.AppendLine("FORMATO \"script\" (arquivo único, ou o arquivo de entrada de um projeto):");
        sb.AppendLine("- Contém APENAS declarações e chamadas soltas (statements top-level).");
        sb.AppendLine("- NUNCA use 'using', 'namespace', 'class', 'struct', 'interface', 'record' ou declare métodos.");
        sb.AppendLine("- Toda feature deve ter Name = \"<mesmo nome da variável>\" e ser registrada com AddFeature(variavel).");
        sb.AppendLine("- Ferramentas booleanas transitórias (ex.: o cilindro usado apenas para furar) devem ter IsTransientTool = true e também devem ser registradas com AddFeature.");
        sb.AppendLine("- Para usar uma peça JÁ DECLARADA em Pecas/ (ver \"PEÇAS DISPONÍVEIS\" abaixo, se houver), NÃO redeclare o");
        sb.AppendLine("  tipo aqui: chame var p = Add(new NomeDaPeca(...), at: Placement.At(x, y, z));");
        sb.AppendLine("- Se o pedido pedir uma peça/objeto reutilizável NOVO (ex.: \"crie um motor X\") enquanto este arquivo de");
        sb.AppendLine("  entrada está ativo, NÃO declare o tipo aqui (é proibido); responda só em texto pedindo para abrir ou");
        sb.AppendLine("  criar o arquivo da peça em Pecas/ primeiro, sem bloco de código.");
        sb.AppendLine();
        sb.AppendLine("FORMATO \"peça\" (arquivo em Pecas/):");
        sb.AppendLine("- Contém EXATAMENTE UM 'public sealed record NomeDaPeca(<parâmetros com valor padrão>) : Part { ... }'.");
        sb.AppendLine("- NUNCA use statements soltos, 'using', nem chame AddFeature ou Add — isso é do arquivo de entrada da cena.");
        sb.AppendLine("- 'protected override IFeature Build()' constrói a geometria em coordenadas LOCAIS (origem em 0,0,0) e");
        sb.AppendLine("  devolve UMA ÚNICA IFeature. Para combinar partes (ex.: corpo + furos + eixo), monte tudo DENTRO do");
        sb.AppendLine("  próprio Build() com BooleanFeature.CreateUnion/CreateDifference/CreateIntersection e devolva o resultado");
        sb.AppendLine("  final — instâncias de peça (PartInstance) ainda não podem ser operandos de BooleanFeature.");
        sb.AppendLine("- Dê a cada parâmetro um nome em português e um valor padrão sensato (para que 'new NomeDaPeca()' já");
        sb.AppendLine("  funcione sozinho); pode usar [System.ComponentModel.DataAnnotations.Range(min, max)] (nome completo, sem using).");
        sb.AppendLine();
        sb.AppendLine("REGRAS COMUNS AOS DOIS FORMATOS:");
        sb.AppendLine("- Unidades em milímetros. O eixo Z aponta para cima.");
        sb.AppendLine("- NÃO use Console, System.IO, threads, reflexão ou qualquer tipo de I/O.");
        sb.AppendLine("- Todo número decimal usa ponto ('.'), nunca vírgula.");
        sb.AppendLine("- Nomes de variáveis são únicos, curtos e em snake_case (ex.: box_a1, hole_b2, fillet_c3).");
        sb.AppendLine("- SÓLIDOS RASTREÁVEIS POR NOME: variáveis, const, arrays, laços e funções locais podem ser usados para");
        sb.AppendLine("  CALCULAR valores (posições, diâmetros, passos), mas cada sólido (BoxFeature, CylinderFeature, PrismFeature,");
        sb.AppendLine("  ExtrudeFeature, FilletFeature, BooleanFeature...) deve ser criado numa declaração própria, com Name LITERAL");
        sb.AppendLine("  igual ao nome da variável e único no arquivo/peça: var furo_1 = new CylinderFeature(...) { Name = \"furo_1\" };");
        sb.AppendLine("  NUNCA crie sólidos dentro de laços, LINQ (Select(... new CylinderFeature ...)) ou listas montadas");
        sb.AppendLine("  dinamicamente, e NUNCA use nome interpolado ou concatenado ($\"furo_{i}\"). Para N furos iguais, calcule as");
        sb.AppendLine("  posições em variáveis e declare furo_1 ... furo_N, um por linha.");
        sb.AppendLine("- Prefira reaproveitar/editar o ARQUIVO ATUAL em vez de recomeçar do zero, a menos que o pedido peça algo totalmente novo.");
        sb.AppendLine();
        sb.AppendLine("API DISPONÍVEL (extraída automaticamente do código-fonte atual do projeto — sempre confie nela, não invente membros):");
        sb.AppendLine(CadScriptApiReference.Build());
        sb.AppendLine("EXEMPLOS VÁLIDOS — formato \"script\":");
        foreach (var example in Examples)
        {
            sb.AppendLine("```cadscript");
            sb.AppendLine(example.Trim());
            sb.AppendLine("```");
        }
        sb.AppendLine();
        sb.AppendLine("EXEMPLOS VÁLIDOS — formato \"peça\":");
        foreach (var example in PartExamples)
        {
            sb.AppendLine("```cadscript");
            sb.AppendLine(example.Trim());
            sb.AppendLine("```");
        }
        sb.AppendLine();
        sb.AppendLine("EXEMPLO — arquivo de entrada usando a peça Placa acima (não redeclare o tipo):");
        sb.AppendLine("```cadscript");
        sb.AppendLine(SceneUsingPartExample.Trim());
        sb.AppendLine("```");
        sb.AppendLine();
        sb.AppendLine("FORMATO OBRIGATÓRIO DA RESPOSTA:");
        sb.AppendLine("1. Um parágrafo curto (1 a 3 frases), em português, explicando o que foi feito ou respondendo à pergunta.");
        sb.AppendLine("2. Se — e somente se — o pedido exigir alterar o arquivo ativo, inclua logo em seguida UM ÚNICO bloco de");
        sb.AppendLine("   código delimitado por ```cadscript e ``` contendo o ARQUIVO COMPLETO resultante (nunca um trecho, nunca");
        sb.AppendLine("   um diff: sempre o arquivo inteiro, pronto para substituir o arquivo ativo e ser executado), no formato");
        sb.AppendLine("   indicado em \"### ARQUIVO ATIVO\".");
        sb.AppendLine("3. Se o pedido for apenas uma pergunta, ou pedir algo que não cabe no formato do arquivo ativo (ver acima),");
        sb.AppendLine("   responda somente com o texto, sem bloco de código.");

        return sb.ToString();
    }

    public string BuildUserMessage(CadScriptContext currentContext, string userInstruction)
    {
        var sb = new StringBuilder();

        sb.AppendLine("### PEDIDO");
        sb.AppendLine(userInstruction);
        sb.AppendLine();
        sb.AppendLine("### ARQUIVO ATIVO");
        string fileName = string.IsNullOrWhiteSpace(currentContext.FileName) ? "Cena.cs" : currentContext.FileName;
        string kindLabel = currentContext.Kind switch
        {
            CadFileKind.PartFile => "peça (record que herda de Part; sem statements soltos, sem AddFeature/Add)",
            CadFileKind.SceneEntry => "script — arquivo de entrada da cena (statements soltos + AddFeature/Add)",
            _ => "script (statements soltos + AddFeature)"
        };
        sb.AppendLine($"{fileName} — formato: {kindLabel}");

        if (currentContext.PartSummaries is { Count: > 0 })
        {
            sb.AppendLine();
            sb.AppendLine("### PEÇAS JÁ DECLARADAS NA CENA (reaproveite com Add(...); não redeclare)");
            foreach (var summary in currentContext.PartSummaries)
            {
                sb.AppendLine("  " + summary);
            }
        }

        sb.AppendLine();
        sb.AppendLine("### CÓDIGO ATUAL DESTE ARQUIVO");
        sb.AppendLine("```cadscript");
        sb.AppendLine(string.IsNullOrWhiteSpace(currentContext.CurrentCode) ? "// (vazio)" : currentContext.CurrentCode.TrimEnd());
        sb.AppendLine("```");

        if (!string.IsNullOrWhiteSpace(currentContext.SelectedFeatureName))
        {
            sb.AppendLine();
            sb.AppendLine("### FEATURE SELECIONADA");
            sb.AppendLine(currentContext.SelectedFeatureName);
        }

        if (!string.IsNullOrWhiteSpace(currentContext.ErrorFeedback))
        {
            sb.AppendLine();
            sb.AppendLine("### ERRO DO ARQUIVO ANTERIOR");
            sb.AppendLine(currentContext.ErrorFeedback);
        }

        return sb.ToString();
    }

    public ProtocolParseResult<string> ParseResponse(string rawAiResponse)
    {
        if (string.IsNullOrWhiteSpace(rawAiResponse))
        {
            return new ProtocolParseResult<string> { ChatMessage = string.Empty, Payload = null };
        }

        var matches = CodeBlockRegex.Matches(rawAiResponse);
        if (matches.Count == 0)
        {
            return new ProtocolParseResult<string> { ChatMessage = rawAiResponse.Trim(), Payload = null };
        }

        var lastMatch = matches[matches.Count - 1];
        string code = lastMatch.Groups["code"].Value.Trim();

        string chatText = rawAiResponse.Remove(lastMatch.Index, lastMatch.Length);
        chatText = CodeBlockRegex.Replace(chatText, string.Empty).Trim();

        return new ProtocolParseResult<string>
        {
            ChatMessage = string.IsNullOrWhiteSpace(chatText) ? "Código gerado." : chatText,
            Payload = string.IsNullOrWhiteSpace(code) ? null : code
        };
    }
}
