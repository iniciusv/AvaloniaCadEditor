using System.Text.Json;
using System.Text.Json.Nodes;

namespace AvaloniaCadEditor.Harness.Tools.Edit;

/// <summary>Troca um trecho exato de um arquivo — uma edição pequena sem reescrever o arquivo inteiro.</summary>
public sealed class ReplaceInFileTool : ICadTool
{
    public string Name => "replace_in_file";

    public string Description =>
        "Substitui um trecho de um arquivo da cena por outro. old_text precisa aparecer exatamente uma vez no arquivo " +
        "(copie com espaços e quebras de linha). Para criar um arquivo novo em Pecas/, use old_text vazio. " +
        "Prefira isto a reescrever o arquivo inteiro.";

    public bool IsReadOnly => false;

    public JsonObject ParametersSchema => ToolSchema.Object(
        ToolSchema.String("path", "Caminho do arquivo relativo à cena (ex.: Cena.cs, Pecas/Placa.cs). Vazio = o arquivo ativo.", required: false),
        ToolSchema.String("old_text", "Trecho exato a substituir (vazio = criar arquivo novo)."),
        ToolSchema.String("new_text", "Texto que entra no lugar."));

    public Task<string> ExecuteAsync(JsonElement args, CadToolContext context, CancellationToken cancellationToken)
    {
        var a = new ToolArgs(args);
        string path = a.OptionalString("path") is { Length: > 0 } p ? p.Replace('\\', '/') : context.ActiveFile;
        string oldText = a.OptionalString("old_text") ?? string.Empty;
        string newText = a.OptionalString("new_text") ?? throw new ToolArgumentException("falta o argumento 'new_text'.");

        if (context.EditError(path) is { } error) return Task.FromResult("Erro: " + error);

        string? current = context.ReadFile(path);
        if (current == null)
        {
            if (oldText.Length > 0) return Task.FromResult($"Erro: o arquivo {path} não existe; para criá-lo, use old_text vazio.");
            context.WriteFile(path, newText);
            return Task.FromResult($"ok — {path} criado ({newText.Split('\n').Length} linhas). Rode evaluate_scene para conferir.");
        }

        if (oldText.Length == 0) return Task.FromResult($"Erro: {path} já existe; informe old_text.");

        // O modelo escreve \n; o arquivo pode estar em \r\n.
        if (current.Contains("\r\n") && !oldText.Contains('\r'))
        {
            oldText = oldText.Replace("\n", "\r\n");
            newText = newText.Replace("\r\n", "\n").Replace("\n", "\r\n");
        }

        int first = current.IndexOf(oldText, StringComparison.Ordinal);
        if (first < 0) return Task.FromResult($"Erro: old_text não aparece em {path}. Copie o trecho exatamente como está no arquivo.");
        if (current.IndexOf(oldText, first + 1, StringComparison.Ordinal) >= 0)
            return Task.FromResult($"Erro: old_text aparece mais de uma vez em {path}; inclua mais contexto para ficar único.");

        context.WriteFile(path, current.Substring(0, first) + newText + current.Substring(first + oldText.Length));
        int line = current.Take(first).Count(c => c == '\n') + 1;
        return Task.FromResult($"ok — {path} alterado na linha {line}. Rode evaluate_scene para conferir.");
    }
}
