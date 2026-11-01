using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AvaloniaCadEditor.Application.Ai;
using AvaloniaCadEditor.Application.Interfaces;
using AvaloniaCadEditor.Domain.Features;
using AvaloniaCadEditor.Domain.Scripting;

namespace AvaloniaCadEditor.Application.Scenes;

/// <summary>Resultado de <see cref="SceneCheckService"/>.</summary>
public sealed record SceneCheckReport(
    string SceneName,
    int FileCount,
    bool Success,
    int FeatureCount,
    bool GeometryChecked,
    IReadOnlyList<ScriptDiagnostic> Diagnostics)
{
    public IEnumerable<ScriptDiagnostic> Errors => Diagnostics.Where(d => d.Severity == ScriptDiagnosticSeverity.Error);

    /// <summary>Contagens do <see cref="CadScriptEditabilityLint"/> somadas de todos os arquivos (null = não conferido).</summary>
    public EditabilityMetrics? Editability { get; init; }

    /// <summary>Avisos de estilo que existem mas não foram listados (só com <see cref="EditabilityCheckOptions.ListStyleWarnings"/>).</summary>
    public int HiddenStyleWarnings { get; init; }

    /// <summary>Texto para o terminal: um diagnóstico por linha e o veredito no final.</summary>
    public string Format()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"AvaloniaCad check: {SceneName} ({FileCount} arquivo(s))");
        foreach (var d in Diagnostics)
        {
            sb.AppendLine(d.ToString());
        }

        if (Editability is { } e)
        {
            string ratio = e.EditablePointRatio is { } r ? $"{Math.Round(r * 100):0}%" : "-";
            sb.Append($"Editabilidade: {e.TrackableSolids}/{e.Solids} sólidos rastreáveis, {ratio} das coordenadas editáveis");
            sb.AppendLine(HiddenStyleWarnings > 0
                ? $", {HiddenStyleWarnings} aviso(s) de estilo (liste com --estilo)."
                : ".");
        }

        int errors = Errors.Count();
        sb.AppendLine(Success
            ? $"OK: {FeatureCount} feature(s){(GeometryChecked ? ", geometria validada" : "")}."
            : $"FALHOU: {errors} erro(s).");
        return sb.ToString();
    }
}

/// <summary>Como o <c>check</c> trata o <see cref="CadScriptEditabilityLint"/>.</summary>
/// <param name="ListStyleWarnings">Lista cada aviso de estilo (CAD0201); sem isso, só a contagem no resumo.</param>
/// <param name="Strict">Sólido não rastreável (CAD0200) vira erro e reprova a cena, em vez de aviso.</param>
public sealed record EditabilityCheckOptions(bool ListStyleWarnings = false, bool Strict = false)
{
    public static EditabilityCheckOptions Default { get; } = new();
}

/// <summary>
/// Verificação autônoma de uma cena: compila e executa (nível 2) e, opcionalmente, constrói
/// cada feature visível no kernel geométrico (nível 3). É o mesmo avaliador usado pela UI.
/// </summary>
public sealed class SceneCheckService
{
    private readonly ISceneEvaluator _evaluator;
    private readonly IGeometryKernel? _kernel;

    public SceneCheckService(ISceneEvaluator evaluator, IGeometryKernel? kernel = null)
    {
        _evaluator = evaluator ?? throw new ArgumentNullException(nameof(evaluator));
        _kernel = kernel;
    }

    public Task<SceneCheckReport> CheckAsync(SceneProject project, bool checkGeometry, CancellationToken cancellationToken = default) =>
        CheckAsync(project, checkGeometry, EditabilityCheckOptions.Default, cancellationToken);

    public async Task<SceneCheckReport> CheckAsync(SceneProject project, bool checkGeometry, EditabilityCheckOptions editability, CancellationToken cancellationToken = default)
    {
        var documents = project.ReadDocuments();
        var result = await _evaluator.EvaluateAsync(documents, cancellationToken).ConfigureAwait(false);

        var diagnostics = result.Diagnostics.ToList();
        var (lint, metrics, hiddenStyle) = CheckEditability(documents, editability);
        diagnostics.AddRange(lint);
        foreach (string reference in project.GetProjectReferences())
        {
            diagnostics.Add(new ScriptDiagnostic(
                project.ToRelativePath(project.ProjectFile), 0, 0, ScriptDiagnosticSeverity.Warning, "CAD0001",
                $"Referência '{reference}' ignorada: o app ainda não carrega pacotes nem outros projetos."));
        }

        if (!result.Success)
        {
            if (!diagnostics.Any(d => d.Severity == ScriptDiagnosticSeverity.Error))
            {
                diagnostics.Add(new ScriptDiagnostic(string.Empty, 0, 0, ScriptDiagnosticSeverity.Error, "CAD0000",
                    result.ErrorMessage ?? "Falha desconhecida ao avaliar a cena."));
            }
            return new SceneCheckReport(project.Name, documents.Count, false, 0, false, diagnostics)
            {
                Editability = metrics,
                HiddenStyleWarnings = hiddenStyle
            };
        }

        bool geometryChecked = checkGeometry && _kernel != null;
        if (geometryChecked)
        {
            diagnostics.AddRange(CheckGeometry(result.Features));
        }

        bool success = !diagnostics.Any(d => d.Severity == ScriptDiagnosticSeverity.Error);
        return new SceneCheckReport(project.Name, documents.Count, success, result.Features.Count, geometryChecked, diagnostics)
        {
            Editability = metrics,
            HiddenStyleWarnings = hiddenStyle
        };
    }

    /// <summary>
    /// O <see cref="CadScriptEditabilityLint"/> em cada arquivo: sólido não rastreável sai como CAD0200 (aviso, ou
    /// erro com <see cref="EditabilityCheckOptions.Strict"/>) e cada aviso de estilo como CAD0201, só quando pedido —
    /// uma cena antiga tem centenas, e a saída do <c>check</c> é lida por IAs externas a cada rodada.
    /// </summary>
    private static (List<ScriptDiagnostic> Diagnostics, EditabilityMetrics Metrics, int HiddenStyle) CheckEditability(
        IReadOnlyList<ScriptDocument> documents, EditabilityCheckOptions options)
    {
        var diagnostics = new List<ScriptDiagnostic>();
        var metrics = EditabilityMetrics.Empty;
        int hidden = 0;

        foreach (var document in documents)
        {
            var report = CadScriptEditabilityLint.Analyze(document.Text);
            metrics = metrics.Add(report.Metrics);

            foreach (var error in report.Errors)
            {
                diagnostics.Add(new ScriptDiagnostic(document.FilePath, error.Line, 1,
                    options.Strict ? ScriptDiagnosticSeverity.Error : ScriptDiagnosticSeverity.Warning,
                    "CAD0200", $"{error.Message} [{error.RuleId}]"));
            }

            foreach (var warning in report.Warnings)
            {
                if (!options.ListStyleWarnings)
                {
                    hidden++;
                    continue;
                }
                diagnostics.Add(new ScriptDiagnostic(document.FilePath, warning.Line, 1, ScriptDiagnosticSeverity.Warning,
                    "CAD0201", $"{warning.Message} [{warning.RuleId}]"));
            }
        }

        return (diagnostics, metrics, hidden);
    }

    /// <summary>Constrói cada feature final (visível e não-ferramenta) e exige uma malha não vazia.</summary>
    private IEnumerable<ScriptDiagnostic> CheckGeometry(IReadOnlyList<IFeature> features)
    {
        foreach (var feature in features.Where(f => f.IsVisible && !f.IsTransientTool))
        {
            string? problem;
            try
            {
                var mesh = _kernel!.BuildFeatureMesh(feature);
                problem = mesh == null || mesh.TriangleCount == 0 ? "o kernel gerou uma geometria vazia" : null;
            }
            catch (Exception ex)
            {
                problem = $"o kernel falhou ao construir a geometria ({ex.GetType().Name}: {ex.Message})";
            }

            if (problem != null)
            {
                var origin = FeatureOrigin.Of(feature);
                yield return new ScriptDiagnostic(
                    origin?.FilePath ?? string.Empty,
                    origin?.Line ?? 0,
                    1,
                    ScriptDiagnosticSeverity.Error,
                    "CAD0100",
                    $"Feature '{feature.Name}': {problem}.");
            }
        }
    }
}
