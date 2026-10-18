using AiCore.Abstractions;
using AiCore.Models;
using AvaloniaCadEditor.Application.Ai;
using AvaloniaCadEditor.Application.Interfaces;
using AvaloniaCadEditor.Harness.Agents;
using AvaloniaCadEditor.Harness.Roles;

namespace AvaloniaCadEditor.Harness;

/// <summary>
/// O ponto de ligação do harness com o app: o composition root (UI, PromptEval) pede uma sessão de
/// chat e recebe a de sempre (<see cref="CadScriptChatSession"/>) ou a com ferramentas
/// (<see cref="HarnessChatSession"/>, com Inspetor se houver), conforme o <c>harness_settings.json</c>.
/// </summary>
public sealed class HarnessSessionFactory
{
    private readonly IAiSettingsProvider _appSettings;
    private readonly IHarnessSettingsStore _harnessSettings;
    private readonly ISceneEvaluator _evaluator;
    private readonly Func<IGeometryKernel> _kernelFactory;
    private readonly ToolSendDelegate? _send;

    /// <param name="kernelFactory">Um kernel novo por sessão: o cache das ferramentas não mexe no da UI.</param>
    public HarnessSessionFactory(IAiSettingsProvider appSettings, IHarnessSettingsStore harnessSettings, ISceneEvaluator evaluator,
        Func<IGeometryKernel> kernelFactory, ToolSendDelegate? send = null)
    {
        _appSettings = appSettings ?? throw new ArgumentNullException(nameof(appSettings));
        _harnessSettings = harnessSettings ?? throw new ArgumentNullException(nameof(harnessSettings));
        _evaluator = evaluator ?? throw new ArgumentNullException(nameof(evaluator));
        _kernelFactory = kernelFactory ?? throw new ArgumentNullException(nameof(kernelFactory));
        _send = send;
    }

    /// <summary>Como a última sessão criada ficou (para o rótulo do chat), inclusive avisos.</summary>
    public string Description { get; private set; } = string.Empty;

    /// <param name="editorOptions">As opções do perfil ativo do chat (o Editor, se o harness não escolher outro).</param>
    public ICadScriptChatSession Create(LlmOptions editorOptions, IAiProtocol<CadScriptContext, CadScriptChanges> protocol)
    {
        var harness = _harnessSettings.Load();
        if (!harness.Enabled)
        {
            Description = string.Empty;
            return new CadScriptChatSession(editorOptions, protocol, _evaluator);
        }

        var app = _appSettings.GetSettings();
        var warnings = new List<string>();

        var (editor, editorError) = RoleOptionsResolver.Resolve(app, harness, AgentRole.Editor);
        if (editorError != null) warnings.Add(editorError);
        var options = editor?.Options ?? editorOptions;
        var limits = editor?.Limits ?? AgentLimits.Remote;

        InspectorAgent? inspector = null;
        var (inspectorRole, inspectorError) = RoleOptionsResolver.Resolve(app, harness, AgentRole.Inspector);
        if (inspectorError != null) warnings.Add(inspectorError);
        if (inspectorRole != null)
            inspector = new InspectorAgent(inspectorRole.Options, inspectorRole.Limits, inspectorRole.Profile.ToString(), _send);

        Description = "ferramentas" + (inspector != null ? $" · Inspetor: {inspector.ProfileName}" : "") +
            (warnings.Count > 0 ? " · ⚠ " + string.Join(" ", warnings) : "");

        return new HarnessChatSession(options, protocol, _evaluator, _kernelFactory(), limits, send: _send,
            inspector: inspector, preInspection: harness.PreInspection);
    }
}
