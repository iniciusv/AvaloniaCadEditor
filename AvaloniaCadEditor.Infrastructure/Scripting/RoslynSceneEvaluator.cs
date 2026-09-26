using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using AvaloniaCadEditor.Application.Interfaces;
using AvaloniaCadEditor.Domain.Features;
using AvaloniaCadEditor.Domain.ValueObjects;
using AvaloniaCadEditor.Domain.Profiles;

namespace AvaloniaCadEditor.Infrastructure.Scripting;

public class ScriptGlobals
{
    private readonly List<IFeature> _features = new();
    public IReadOnlyList<IFeature> Features => _features;

    public void AddFeature(IFeature feature)
    {
        if (feature != null)
        {
            _features.Add(feature);
        }
    }
}

public class RoslynSceneEvaluator : ISceneEvaluator
{
    public async Task<SceneEvaluationResult> EvaluateAsync(string code)
    {
        var globals = new ScriptGlobals();
        
        var options = ScriptOptions.Default
            .WithReferences(
                typeof(IFeature).Assembly, // Domain assembly
                typeof(System.Math).Assembly // System.Runtime
            )
            .WithImports(
                "System",
                "System.Collections.Generic",
                "AvaloniaCadEditor.Domain.Features",
                "AvaloniaCadEditor.Domain.ValueObjects",
                "AvaloniaCadEditor.Domain.Profiles"
            );

        try
        {
            await CSharpScript.EvaluateAsync(code, options, globals);
            return new SceneEvaluationResult(globals.Features, true);
        }
        catch (CompilationErrorException ex)
        {
            var errors = string.Join(Environment.NewLine, ex.Diagnostics);
            System.IO.File.WriteAllText("script_compile_error.log", "=== CODE ===\r\n" + code + "\r\n\r\n=== ERRORS ===\r\n" + errors);
            return new SceneEvaluationResult(
                Array.Empty<IFeature>(), 
                false,
                errors);
        }
        catch (Exception ex)
        {
            System.IO.File.WriteAllText("script_runtime_error.log", "=== CODE ===\r\n" + code + "\r\n\r\n=== ERROR ===\r\n" + ex.ToString());
            return new SceneEvaluationResult(
                Array.Empty<IFeature>(), 
                false,
                ex.Message);
        }
    }
}
