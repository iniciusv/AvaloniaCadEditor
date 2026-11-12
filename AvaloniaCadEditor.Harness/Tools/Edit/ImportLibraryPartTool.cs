using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AvaloniaCadEditor.Application.Library;
using AvaloniaCadEditor.Harness.Tools.Query;

namespace AvaloniaCadEditor.Harness.Tools.Edit;

/// <summary>
/// Copia uma peça da biblioteca do usuário (e as peças de que ela depende) para <c>Pecas/</c> na cópia de trabalho,
/// com o mesmo plano do botão Inserir da aba (<see cref="PartLibraryImporter"/>): tipo que a cena já tem fica como
/// está, e um destino ocupado recusa. Devolve o código da peça, para a IA saber a origem e os parâmetros.
/// A biblioteca nunca é alterada.
/// </summary>
public sealed class ImportLibraryPartTool : ICadTool
{
    public string Name => "import_library_part";

    public string Description =>
        "Copia uma peça da biblioteca do usuário para Pecas/ (com as peças de que ela depende) e devolve o código dela. " +
        "Depois use-a como qualquer peça da cena: Add(new Tipo(...), at: ...) em Cena.cs, ou new Tipo(...).Instance(\"nome\") " +
        "dentro do Build() de outra. Não reescreva o arquivo importado. Ache o nome com search_library.";

    public bool IsReadOnly => false;

    public JsonObject ParametersSchema => ToolSchema.Object(
        ToolSchema.String("name", "Nome do tipo da peça (ex.: TrilhoMgn9), como search_library mostra."));

    public Task<string> ExecuteAsync(JsonElement args, CadToolContext context, CancellationToken cancellationToken)
    {
        string name = new ToolArgs(args).String("name").Trim();
        var library = context.Library;
        if (library.Count == 0) return Task.FromResult("Erro: a biblioteca de peças está vazia ou não foi configurada.");
        if (context.Context.Scope == null)
            return Task.FromResult("Erro: a cena não é uma pasta (projeto); peças da biblioteca só são copiadas para Pecas/ de um projeto.");

        var item = PartLibraryCatalog.Read(library, PartLibrarySource.Library).FirstOrDefault(i => i.Name == name);
        if (item == null) return Task.FromResult($"Erro: a biblioteca não tem a peça {name}. Use search_library para achar o nome.");

        var plan = PartLibraryImporter.Plan(library, context.SceneDocuments(), name);
        if (plan.Refusal != null) return Task.FromResult("Erro: " + plan.Refusal);

        foreach (var file in plan.NewFiles)
        {
            if (context.EditError(file.FilePath) is { } error) return Task.FromResult("Erro: " + error);
        }
        foreach (var file in plan.NewFiles) context.WriteFile(file.FilePath, file.Text);

        var text = new StringBuilder();
        text.AppendLine(plan.NewFiles.Count == 0
            ? $"ok — {name} já está na cena; nada foi copiado."
            : $"ok — copiado da biblioteca: {string.Join(", ", plan.NewFiles.Select(f => f.FilePath))}.");
        foreach (var warning in plan.Warnings) text.Append("Aviso: ").AppendLine(warning);
        text.Append("Assinatura: ").AppendLine(SearchLibraryTool.Signature(item));
        if (item.Presets.Count > 0)
            text.Append("Modelos prontos: ").AppendLine(string.Join(", ", item.Presets.Select(p => $"{item.Name}.{p.Name}")));
        if (item.IsCutter)
            text.AppendLine("É um vazio: use-o dentro do Build() da peça que vai receber o furo, como ferramenta de CreateDifference.");

        var main = plan.NewFiles.FirstOrDefault(f => PartDependencyIndex.Of(new[] { f }).TypesIn(f.FilePath).Contains(name));
        string? code = main?.Text ?? context.SceneDocuments()
            .FirstOrDefault(d => PartDependencyIndex.Of(new[] { d }).TypesIn(d.FilePath).Contains(name))?.Text;
        if (code != null)
        {
            text.AppendLine();
            text.AppendLine("```csharp");
            text.AppendLine(code.Replace("\r\n", "\n").TrimEnd());
            text.Append("```");
        }
        return Task.FromResult(text.ToString().TrimEnd());
    }
}
