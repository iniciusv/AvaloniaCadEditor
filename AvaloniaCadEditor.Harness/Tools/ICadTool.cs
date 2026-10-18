using System.Text.Json;
using System.Text.Json.Nodes;

namespace AvaloniaCadEditor.Harness.Tools;

/// <summary>
/// Uma ferramenta que a IA pode chamar. Ferramentas são finas: validam os argumentos, delegam a um
/// serviço (<see cref="CadToolContext"/>, <see cref="Inspection.SceneInspectionService"/>) e formatam
/// o resultado em texto. Erros voltam como texto ("Erro: ..."), para o modelo poder corrigir.
/// </summary>
public interface ICadTool
{
    /// <summary>Nome da função para o modelo (snake_case, ex.: "get_view").</summary>
    string Name { get; }

    /// <summary>O modelo escolhe a ferramenta lendo isto: diga o que faz e quando usar.</summary>
    string Description { get; }

    /// <summary>Só lê (pode ser dada ao Inspetor); false = altera a cópia de trabalho.</summary>
    bool IsReadOnly { get; }

    /// <summary>JSON Schema dos argumentos (ver <see cref="ToolSchema"/>).</summary>
    JsonObject ParametersSchema { get; }

    Task<string> ExecuteAsync(JsonElement args, CadToolContext context, CancellationToken cancellationToken);
}
