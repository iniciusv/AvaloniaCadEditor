using System.Collections.Generic;
using AvaloniaCadEditor.Application.Scenes;

namespace AvaloniaCadEditor.Application.Library;

/// <summary>De onde vem um item da biblioteca.</summary>
public enum PartLibrarySource
{
    /// <summary>Já está nos arquivos da cena aberta (<c>Pecas/</c>).</summary>
    Scene,

    /// <summary>Da pasta da biblioteca, fora de qualquer cena: inserir copia o arquivo para a cena.</summary>
    Library
}

/// <summary>
/// Modelo pronto de uma peça: uma propriedade estática que devolve o próprio tipo
/// (<c>public static Rolamento F623ZZ =&gt; new(3, 10, 4, 11.5, 1);</c>).
/// </summary>
/// <param name="Name">Nome da propriedade (<c>F623ZZ</c>).</param>
/// <param name="Summary">Descrição do <c>&lt;summary&gt;</c> da propriedade, ou vazia.</param>
/// <param name="Arguments">Texto de cada argumento, por nome de parâmetro. Vazio quando o modelo não é um
/// <c>new(...)</c> que dê para ler: aí ele só pode ser inserido como está.</param>
public sealed record PartLibraryPreset(string Name, string Summary, IReadOnlyDictionary<string, string> Arguments);

/// <summary>
/// Uma peça no catálogo, lida só pela sintaxe do arquivo: nada é compilado nem executado.
/// </summary>
/// <param name="Type">O record da peça. <see cref="PartTypeInfo.FilePath"/> é relativo à pasta da origem.</param>
/// <param name="Source">Cena aberta ou biblioteca.</param>
/// <param name="Category">Subpasta na biblioteca (<c>Perfis</c>, <c>Projetos/Impressora 3D</c>); vazia na raiz.</param>
/// <param name="Summary">Texto do <c>&lt;summary&gt;</c>, sem as tags, em uma linha.</param>
/// <param name="Presets">Modelos prontos declarados no record.</param>
/// <param name="IsCutter">Vazio (<c>Part.IsCutter</c>): fura a peça aberta em vez de entrar na cena.</param>
public sealed record PartLibraryItem(
    PartTypeInfo Type,
    PartLibrarySource Source,
    string Category,
    string Summary,
    IReadOnlyList<PartLibraryPreset> Presets,
    bool IsCutter = false)
{
    public string Name => Type.Name;
    public string FilePath => Type.FilePath;
    public IReadOnlyList<PartTypeParameter> Parameters => Type.Parameters;

    /// <summary>Primeira frase do <see cref="Summary"/>, para a linha da lista.</summary>
    public string ShortSummary => PartLibraryCatalog.FirstSentence(Summary);
}
