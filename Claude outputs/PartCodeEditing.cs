using System.Collections.Generic;
using System.Threading.Tasks;
using AvaloniaCadEditor.Application.Scenes;
using AvaloniaCadEditor.Domain.Features;

namespace AvaloniaCadEditor.UI.ViewModels;

/// <summary>O que o inspector precisa do editor de código para mostrar e editar uma peça.</summary>
public interface IPartCodeEditor
{
    /// <summary>Como cada parâmetro está escrito na chamada <c>Add(...)</c> da instância (null se não achar).</summary>
    IReadOnlyList<PartArgument>? ReadPartArguments(PartInstance instance);

    /// <summary>Quantas instâncias da cena atual vieram da mesma chamada (ex.: dentro de um for).</summary>
    int CountInstancesFromSameCall(PartInstance instance);

    /// <summary>Reescreve o parâmetro no código e reexecuta. Retorna a mensagem de erro, ou null.</summary>
    Task<string?> SetPartParameterAsync(PartInstance instance, string parameterName, string text);

    /// <summary>Abre no editor o arquivo e a linha onde o tipo da peça é declarado.</summary>
    bool GoToPartDefinition(string typeName);
}

/// <summary>
/// Pedido para o editor rolar até uma linha. É uma classe (não record) de propósito: dois pedidos
/// para a mesma linha precisam ser diferentes, senão o binding ignora o segundo.
/// </summary>
public sealed class EditorLineRequest
{
    public EditorLineRequest(int line) => Line = line;

    /// <summary>Linha, começando em 1.</summary>
    public int Line { get; }
}
