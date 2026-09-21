using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AvaloniaCadEditor.Domain.Entities;
using AvaloniaCadEditor.Domain.Interfaces;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using AvaloniaCadEditor.Domain.ValueObjects;

namespace AvaloniaCadEditor.Application.Services;

public class ScriptGlobals
{
    public IModelContext ctx { get; set; } = null!;
}

public class ModelCodeRunner
{
    public async Task<Sketch> ExecuteAsync(string code)
    {
        var sketch = new Sketch();
        var context = new ModelContext(sketch);

        var options = ScriptOptions.Default
            .AddReferences(typeof(IModelContext).Assembly)
            .AddReferences(typeof(Point3D).Assembly)
            .AddImports(
                "System",
                "System.Collections.Generic",
                "AvaloniaCadEditor.Domain.ValueObjects",
                "AvaloniaCadEditor.Domain.Profiles",
                "AvaloniaCadEditor.Domain.Features",
                "AvaloniaCadEditor.Domain.Interfaces");

        // The generated code defines a static class ModelDefinition with a Build method.
        // We will execute the script to declare the class, then execute a continuation to call the Build method.
        
        var state = await CSharpScript.RunAsync(code, options, globals: new ScriptGlobals { ctx = context });
        await state.ContinueWithAsync("ModelDefinition.Build(ctx);");

        return sketch;
    }
}
