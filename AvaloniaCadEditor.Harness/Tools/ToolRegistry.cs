using AiCore.Models;
using AvaloniaCadEditor.Harness.Tools.Edit;
using AvaloniaCadEditor.Harness.Tools.Query;
using AvaloniaCadEditor.Harness.Tools.Verify;

namespace AvaloniaCadEditor.Harness.Tools;

/// <summary>O conjunto de ferramentas de um agente, por nome.</summary>
public sealed class ToolRegistry
{
    private readonly Dictionary<string, ICadTool> _tools;

    public ToolRegistry(IEnumerable<ICadTool> tools)
    {
        _tools = tools.ToDictionary(t => t.Name, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>As ferramentas da primeira versão: consulta, verificação e edição determinística.</summary>
    public static ToolRegistry Default() => new(new ICadTool[]
    {
        new ListTargetsTool(),
        new BoundingBoxTool(),
        new ViewTool(),
        new DimensionsTool(),
        new EvaluateSceneTool(),
        new TranslateInstanceTool(),
        new RotateInstanceTool(),
        new ReplaceInFileTool()
    });

    public IReadOnlyCollection<ICadTool> All => _tools.Values;

    /// <summary>Só as que não alteram nada (para o Inspetor).</summary>
    public ToolRegistry ReadOnly() => new(_tools.Values.Where(t => t.IsReadOnly));

    public ICadTool? Find(string name) => _tools.TryGetValue(name, out var tool) ? tool : null;

    public IReadOnlyList<ToolDefinition> Definitions() =>
        _tools.Values.OrderBy(t => t.Name, StringComparer.Ordinal)
            .Select(t => new ToolDefinition(t.Name, t.Description, t.ParametersSchema))
            .ToList();
}
