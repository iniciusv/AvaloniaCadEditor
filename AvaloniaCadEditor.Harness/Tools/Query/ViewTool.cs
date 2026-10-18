using System.Text.Json;
using System.Text.Json.Nodes;
using AvaloniaCadEditor.Domain.Entities;
using AvaloniaCadEditor.Harness.Inspection;

namespace AvaloniaCadEditor.Harness.Tools.Query;

/// <summary>"O que está na tela": a vista ortográfica em arame do alvo, como texto 2D.</summary>
public sealed class ViewTool : ICadTool
{
    /// <summary>Teto do orçamento pedido pelo modelo (o padrão vem do perfil, via <see cref="DefaultMaxChars"/>).</summary>
    private const int MaxCharsLimit = 40_000;

    public ViewTool(int defaultMaxChars = 6000)
    {
        DefaultMaxChars = defaultMaxChars;
    }

    public int DefaultMaxChars { get; }

    public string Name => "get_view";

    public string Description =>
        "Vista ortográfica em arame do alvo, isolado, em texto: as linhas visíveis e ocultas (tracejadas) com " +
        "coordenadas 2D da folha (U para a direita, V para cima, em mm) e os círculos com Ø, centro e o nome do furo. " +
        "Cada furo traz o eixo também em coordenadas do MUNDO e do CÓDIGO da peça, de 'entrada' (face voltada para " +
        "quem olha) a 'fundo' — use essas, sem converter da folha. Use para achar furos e conferir o resultado.";

    public bool IsReadOnly => true;

    public JsonObject ParametersSchema => ToolSchema.Object(
        ToolSchema.String("target", TargetLookup.TargetDescription, required: false),
        ToolSchema.String("view", "Vista padrão (padrão Front).", required: false, values: ToolArgs.ViewNames),
        ToolSchema.Boolean("include_hidden", "Incluir as linhas ocultas (padrão true).", required: false),
        ToolSchema.Integer("max_chars", "Tamanho máximo do texto; as linhas menores são omitidas primeiro.", required: false));

    public async Task<string> ExecuteAsync(JsonElement args, CadToolContext context, CancellationToken cancellationToken)
    {
        var a = new ToolArgs(args);
        var view = a.View("view", StandardView.Front);
        var options = new ViewTextOptions
        {
            IncludeHidden = a.Bool("include_hidden", true),
            MaxChars = Math.Clamp(a.Int("max_chars", DefaultMaxChars), 500, MaxCharsLimit)
        };

        var (target, error) = await TargetLookup.PrepareAsync(context, a.OptionalString("target"), cancellationToken);
        return target == null ? error! : context.Inspection.DescribeView(target, view, options);
    }
}
