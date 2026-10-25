using AiCore.Models;
using AvaloniaCadEditor.Harness.Tools;

namespace AvaloniaCadEditor.Harness.Agents;

/// <summary>O que o Inspetor levantou: o resumo que vai ao Editor e o que custou.</summary>
public sealed record InspectorReport(string Summary, IReadOnlyList<AgentStep> Steps, int PromptTokens, int CompletionTokens, bool HitLimit);

/// <summary>
/// Sub-agente que só lê: consulta a geometria com as ferramentas de leitura e devolve um resumo
/// curto. Pensado para um modelo local (grátis, pode fazer muitas consultas) trabalhar para um
/// Editor pago, que só paga pelo resumo. Também funciona quando o Editor não tem ferramentas.
/// </summary>
public sealed class InspectorAgent
{
    public const int MaxSummaryChars = 2000;

    private readonly ToolRegistry _tools;
    private readonly AgentLoop _loop;

    public InspectorAgent(LlmOptions options, AgentLimits limits, string profileName, ToolSendDelegate? send = null, ToolRegistry? tools = null)
    {
        Options = options ?? throw new ArgumentNullException(nameof(options));
        Limits = limits ?? throw new ArgumentNullException(nameof(limits));
        ProfileName = profileName;
        _tools = (tools ?? ToolRegistry.Default()).ReadOnly();
        _loop = new AgentLoop(send);
    }

    public LlmOptions Options { get; }
    public AgentLimits Limits { get; }
    public string ProfileName { get; }

    /// <summary>As ferramentas que o Inspetor tem (só de leitura).</summary>
    public ToolRegistry Tools => _tools;

    /// <param name="request">O pedido do usuário com o contexto (código, seleção) ou a pergunta do Editor.</param>
    public async Task<InspectorReport> InspectAsync(string request, CadToolContext context, CancellationToken cancellationToken)
    {
        var history = new List<ChatMessage>
        {
            new() { Role = "system", Content = Prompt },
            new() { Role = "user", Content = request }
        };

        var run = await _loop.RunAsync(history, Options, _tools, context, Limits, cancellationToken);
        string summary = run.Final.Content.Trim();
        if (summary.Length > MaxSummaryChars) summary = summary.Substring(0, MaxSummaryChars) + " …(resumo cortado)";
        if (summary.Length == 0) summary = "Sem notas geométricas.";

        return new InspectorReport(summary, run.Steps, run.PromptTokens, run.CompletionTokens, run.HitLimit);
    }

    public const string Prompt = """
        Você é o Inspetor de geometria de uma cena CAD escrita em CadScript (C#). Você NÃO edita nada e não
        escreve código. Use as ferramentas de leitura (list_targets, get_bounding_box, get_view,
        get_dimensions, evaluate_scene) para levantar os fatos geométricos de que o Editor precisa para
        atender ao pedido: nomes dos alvos, caixas, posições de furos, faces e bordas, distâncias e
        alinhamentos.

        As vistas e cotas vêm em coordenadas da folha; o cabeçalho diz que eixo é U e V e onde fica o
        (0, 0, 0) do código. Converta para o sistema do código (código = folha − origem) e diga em que
        sistema cada número está.

        Responda com um resumo objetivo em tópicos, com até 2000 caracteres. Se o pedido não depende de
        geometria, responda apenas "Sem notas geométricas."
        """;
}
