using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using AvaloniaCadEditor.Domain.Entities;

namespace AvaloniaCadEditor.Application.Services;

public class ProjectRepository
{
    private readonly ModelCodeGenerator _generator = new();
    private readonly ModelCodeRunner _runner = new();

    public void SaveToFile(Sketch sketch, string filePath)
    {
        var code = _generator.Generate(sketch);
        File.WriteAllText(filePath, code);
    }

    public async Task<Sketch> LoadFromFileAsync(string filePath)
    {
        var code = File.ReadAllText(filePath);
        return await _runner.ExecuteAsync(code);
    }
}
