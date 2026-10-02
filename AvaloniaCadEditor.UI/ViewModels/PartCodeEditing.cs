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

    /// <summary>Move a instância (reescrevendo o <c>at:</c>) e reexecuta. Retorna a mensagem de erro, ou null.</summary>
    Task<string?> MovePartInstanceAsync(PartInstance instance, AvaloniaCadEditor.Domain.ValueObjects.Vector3D offset);

    /// <summary>Grava o <c>color:</c> ("#RRGGBB" ou null = padrão) e reexecuta. Retorna a mensagem de erro, ou null.</summary>
    Task<string?> SetPartInstanceColorAsync(PartInstance instance, string? color);
}

/// <summary>Comandos de navegação disparados pelo teclado no editor (F12, Shift+F12, F2).</summary>
public interface ICodeEditorCommands
{
    /// <summary>F12: vai para a declaração do símbolo sob o cursor.</summary>
    bool GoToDefinitionAt(int offset);

    /// <summary>Shift+F12: lista as ocorrências do símbolo sob o cursor. Devolve quantas achou.</summary>
    int FindReferencesAt(int offset);

    /// <summary>F2: abre a caixa de renomear para o símbolo sob o cursor.</summary>
    bool BeginRenameAt(int offset);
}

/// <summary>
/// Pedido para o editor rolar até uma linha (e, opcionalmente, selecionar um trecho dela). É uma
/// classe (não record) de propósito: dois pedidos iguais precisam ser diferentes, senão o binding
/// ignora o segundo.
/// </summary>
public sealed class EditorLineRequest
{
    public EditorLineRequest(int line, int column = 1, int selectionLength = 0)
    {
        Line = line;
        Column = column;
        SelectionLength = selectionLength;
    }

    /// <summary>Linha, começando em 1.</summary>
    public int Line { get; }

    /// <summary>Coluna, começando em 1.</summary>
    public int Column { get; }

    /// <summary>Quantos caracteres selecionar a partir da coluna (0 = só posiciona o cursor).</summary>
    public int SelectionLength { get; }
}
