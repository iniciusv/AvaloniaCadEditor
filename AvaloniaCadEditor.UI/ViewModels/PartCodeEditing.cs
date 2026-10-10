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

    /// <summary>
    /// Troca o valor PADRÃO do parâmetro no record da peça — vale para todas as instâncias, não só
    /// a selecionada. Retorna a mensagem de erro, ou null.
    /// </summary>
    Task<string?> SetPartTypeParameterDefaultAsync(PartInstance owner, string parameterName, string text);

    /// <summary>
    /// Quantas instâncias da peça passam esse parâmetro na própria chamada <c>Add(...)</c> (e
    /// portanto não sentem uma mudança no padrão do record), e quantas existem no total.
    /// </summary>
    (int Overriding, int Total) CountInstancesOverriding(string partTypeName, string parameterName);

    /// <summary>Abre no editor o arquivo e a linha onde o tipo da peça é declarado.</summary>
    bool GoToPartDefinition(string typeName);

    /// <summary>Move a instância (reescrevendo o <c>at:</c>) e reexecuta. Retorna a mensagem de erro, ou null.</summary>
    Task<string?> MovePartInstanceAsync(PartInstance instance, AvaloniaCadEditor.Domain.ValueObjects.Vector3D offset);

    /// <summary>Grava o <c>color:</c> ("#RRGGBB" ou null = padrão) e reexecuta. Retorna a mensagem de erro, ou null.</summary>
    Task<string?> SetPartInstanceColorAsync(PartInstance instance, string? color);

    /// <summary>Renomeia a feature no código (<c>name:</c> ou o literal do nome) e reexecuta. Retorna a mensagem de erro, ou null.</summary>
    Task<string?> RenameFeatureAsync(IFeature feature, string newName);

    /// <summary>Converte um sólido solto em peça (record em Pecas/) e reexecuta. Retorna a mensagem de erro, ou null.</summary>
    Task<string?> ConvertToPartAsync(IFeature feature, string typeName);

    /// <summary>As instâncias de peça da última execução da cena (alvos possíveis de uma incorporação).</summary>
    IReadOnlyList<PartInstance> ScenePartInstances();

    /// <summary>As features de topo marcadas na cena (Ctrl+clique acumula várias), sem planos de trabalho.</summary>
    IReadOnlyList<IFeature> SelectedSceneFeatures();

    /// <summary>
    /// Incorpora um sólido solto ao Build() da peça de <paramref name="target"/> (coordenadas convertidas
    /// para o referencial local dela) e reexecuta. Retorna a mensagem de erro, ou null.
    /// </summary>
    Task<string?> MergeIntoPartAsync(IFeature feature, PartInstance target, AvaloniaCadEditor.Application.Interfaces.PartMergeMode mode);
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
    public EditorLineRequest(int line, int column = 1, int selectionLength = 0, bool takeFocus = false)
    {
        Line = line;
        Column = column;
        SelectionLength = selectionLength;
        TakeFocus = takeFocus;
    }

    /// <summary>Linha, começando em 1.</summary>
    public int Line { get; }

    /// <summary>Coluna, começando em 1.</summary>
    public int Column { get; }

    /// <summary>Quantos caracteres selecionar a partir da coluna (0 = só posiciona o cursor).</summary>
    public int SelectionLength { get; }

    /// <summary>
    /// Se o editor deve ficar com o foco do teclado. Só para navegação explícita (F12, lista de
    /// referências): quem navegou a partir da cena continua na cena, senão os atalhos de ferramenta
    /// (M, R, ...) viram digitação no código.
    /// </summary>
    public bool TakeFocus { get; }
}
