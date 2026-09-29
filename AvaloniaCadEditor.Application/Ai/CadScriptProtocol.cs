using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using AiCore.Abstractions;

namespace AvaloniaCadEditor.Application.Ai;

/// <summary>
/// Protocolo de IA para o "CadScript": a IA recebe o script C# vigente da cena mais o
/// pedido em linguagem natural, e deve responder com um texto curto seguido (quando a
/// geometria muda) de um único bloco de código contendo o SCRIPT COMPLETO resultante.
/// </summary>
public class CadScriptProtocol : IAiProtocol<CadScriptContext, string>
{
    private static readonly Regex CodeBlockRegex = new(
        @"```(?:cadscript|csharp|cs)?\s*\r?\n(?<code>.*?)```",
        RegexOptions.Singleline | RegexOptions.IgnoreCase);

    /// <summary>
    /// Exemplos few-shot mostrados à IA. Cobertos por teste que garante que cada um deles
    /// realmente compila e executa no <c>RoslynSceneEvaluator</c> — ou seja, o "dialeto"
    /// ensinado à IA é sempre válido.
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

    public string GetSystemPrompt()
    {
        var sb = new StringBuilder();

        sb.AppendLine("Você é um assistente especializado em um editor CAD paramétrico 3D construído em C#.");
        sb.AppendLine("Sua tarefa é gerar/editar um SCRIPT C# que descreve a cena inteira através de 'features' paramétricas.");
        sb.AppendLine("Esse script é compilado como um programa C# com top-level statements (Roslyn): existe um método global");
        sb.AppendLine("já disponível, AddFeature(IFeature), usado para registrar cada feature final na cena.");
        sb.AppendLine();
        sb.AppendLine("REGRAS OBRIGATÓRIAS DO SCRIPT:");
        sb.AppendLine("- O script contém APENAS declarações e chamadas soltas (statements top-level).");
        sb.AppendLine("- NUNCA use 'using', 'namespace', 'class', 'struct', 'interface', 'record' ou declare métodos.");
        sb.AppendLine("- NÃO use Console, System.IO, threads, reflexão ou qualquer tipo de I/O.");
        sb.AppendLine("- Unidades em milímetros. O eixo Z aponta para cima.");
        sb.AppendLine("- Todo número decimal usa ponto ('.'), nunca vírgula.");
        sb.AppendLine("- Toda feature deve ter Name = \"<mesmo nome da variável>\" e ser registrada com AddFeature(variavel).");
        sb.AppendLine("- Ferramentas booleanas transitórias (ex.: o cilindro usado apenas para furar) devem ter IsTransientTool = true e também devem ser registradas com AddFeature.");
        sb.AppendLine("- Nomes de variáveis são únicos, curtos e em snake_case (ex.: box_a1, hole_b2, fillet_c3).");
        sb.AppendLine("- Prefira reaproveitar/editar o SCRIPT ATUAL em vez de recomeçar do zero, a menos que o pedido peça algo totalmente novo.");
        sb.AppendLine();
        sb.AppendLine("API DISPONÍVEL (extraída automaticamente do código-fonte atual do projeto — sempre confie nela, não invente membros):");
        sb.AppendLine(CadScriptApiReference.Build());
        sb.AppendLine("EXEMPLOS VÁLIDOS:");
        foreach (var example in Examples)
        {
            sb.AppendLine("```cadscript");
            sb.AppendLine(example.Trim());
            sb.AppendLine("```");
        }
        sb.AppendLine();
        sb.AppendLine("FORMATO OBRIGATÓRIO DA RESPOSTA:");
        sb.AppendLine("1. Um parágrafo curto (1 a 3 frases), em português, explicando o que foi feito ou respondendo à pergunta.");
        sb.AppendLine("2. Se — e somente se — o pedido exigir alterar a geometria da cena, inclua logo em seguida UM ÚNICO bloco de");
        sb.AppendLine("   código delimitado por ```cadscript e ``` contendo o SCRIPT COMPLETO resultante da cena inteira (nunca um");
        sb.AppendLine("   trecho, nunca um diff: sempre o script inteiro, pronto para substituir o script atual e ser executado.)");
        sb.AppendLine("3. Se o pedido for apenas uma pergunta, sem mudança de geometria, responda somente com o texto, sem bloco de código.");

        return sb.ToString();
    }

    public string BuildUserMessage(CadScriptContext currentContext, string userInstruction)
    {
        var sb = new StringBuilder();

        sb.AppendLine("### PEDIDO");
        sb.AppendLine(userInstruction);
        sb.AppendLine();
        sb.AppendLine("### SCRIPT ATUAL");
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
            sb.AppendLine("### ERRO DO SCRIPT ANTERIOR");
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
