using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
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

    /// <summary>Texto para o terminal: um diagnóstico por linha e o veredito no final.</summary>
    public string Format()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"AvaloniaCad check: {SceneName} ({FileCount} arquivo(s))");
        foreach (var d in Diagnostics)
        {
            sb.AppendLine(d.ToString());
        }

        int errors = Errors.Count();
        sb.AppendLine(Success
            ? $"OK: {FeatureCount} feature(s){(GeometryChecked ? ", geometria validada" : "")}."
            : $"FALHOU: {errors} erro(s).");
        return sb.ToString();
    }
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

    public async Task<SceneCheckReport> CheckAsync(SceneProject project, bool checkGeometry, CancellationToken cancellationToken = default)
    {
        var documents = project.ReadDocuments();
        var result = await _evaluator.EvaluateAsync(documents, cancellationToken).ConfigureAwait(false);

        var diagnostics = result.Diagnostics.ToList();
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
            return new SceneCheckReport(project.Name, documents.Count, false, 0, false, diagnostics);
        }

        bool geometryChecked = checkGeometry && _kernel != null;
        if (geometryChecked)
        {
            diagnostics.AddRange(CheckGeometry(result.Features));
        }

        bool success = !diagnostics.Any(d => d.Severity == ScriptDiagnosticSeverity.Error);
        return new SceneCheckReport(project.Name, documents.Count, success, result.Features.Count, geometryChecked, diagnostics);
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
