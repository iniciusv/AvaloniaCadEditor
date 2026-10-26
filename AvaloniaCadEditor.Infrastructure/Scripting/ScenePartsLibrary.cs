using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Text;
using AvaloniaCadEditor.Application.Interfaces;

namespace AvaloniaCadEditor.Infrastructure.Scripting;

/// <summary>
/// Os arquivos da cena sem top-level statements (as peças, em <c>Pecas/</c>) compilados num assembly à
/// parte e guardados entre execuções pelo conteúdo deles. Arrastar uma peça ou mudar um valor no inspector
/// reescreve só a <c>Cena.cs</c>: com isto a execução compila só ela, e o código das peças, que continua
/// carregado, não passa de novo pelo JIT. Na cena de referência (AiCore), a avaliação cai de ~470 ms para
/// ~190 ms.
/// <para>
/// A cena enxerga os tipos internos das peças por <c>InternalsVisibleTo</c>, então o assembly dela tem
/// sempre o nome <see cref="EntryAssemblyName"/>. Quando as peças não compilam sozinhas (dependem de um
/// tipo declarado na <c>Cena.cs</c>, ou têm erro), a biblioteca fica marcada como inviável para aquele
/// conteúdo e o <see cref="RoslynSceneEvaluator"/> compila a cena inteira, como antes.
/// </para>
/// <para>
/// Consequência: um campo estático de uma peça sobrevive entre execuções enquanto as peças não mudam.
/// <c>Build()</c> deve ser puro (ARCHITECTURE §5.4), então isso não deveria aparecer.
/// </para>
/// </summary>
internal sealed class ScenePartsLibrary
{
    /// <summary>Nome fixo do assembly da cena: é a ele que as peças abrem os tipos internos.</summary>
    public const string EntryAssemblyName = "CadScene_Entry";

    // Mais de um conteúdo de peças ao mesmo tempo: a cena na tela e a validação da IA (com a peça proposta).
    private const int MaxCached = 4;

    private static readonly object Gate = new();

    /// <summary>A mais recente primeiro.</summary>
    private static readonly LinkedList<ScenePartsLibrary> Cache = new();

    private readonly byte[]? _image;
    private readonly byte[]? _pdb;
    private LibraryLoadContext? _context;
    private Assembly? _assembly;
    private bool _evicted;

    private ScenePartsLibrary(string key, string assemblyName, byte[]? image, byte[]? pdb, IReadOnlyList<Diagnostic> diagnostics)
    {
        Key = key;
        AssemblyName = assemblyName;
        _image = image;
        _pdb = pdb;
        Diagnostics = diagnostics;
        Reference = image != null ? MetadataReference.CreateFromImage(image) : null;
    }

    private string Key { get; }

    public string AssemblyName { get; }

    /// <summary>Para a compilação da cena; <c>null</c> quando as peças não compilam sozinhas.</summary>
    public MetadataReference? Reference { get; }

    /// <summary>Avisos da compilação das peças (a da cena só traz os dela).</summary>
    public IReadOnlyList<Diagnostic> Diagnostics { get; }

    /// <summary>
    /// A biblioteca das peças em <paramref name="documents"/> (com as árvores já lidas, na mesma ordem):
    /// do cache, ou compilada agora.
    /// </summary>
    public static ScenePartsLibrary GetOrCompile(
        IReadOnlyList<ScriptDocument> documents,
        IReadOnlyList<SyntaxTree> trees,
        IEnumerable<SyntaxTree> sharedTrees,
        IReadOnlyList<MetadataReference> references,
        CancellationToken cancellationToken)
    {
        string key = KeyOf(documents);
        lock (Gate)
        {
            for (var node = Cache.First; node != null; node = node.Next)
            {
                if (node.Value.Key == key)
                {
                    Cache.Remove(node);
                    Cache.AddFirst(node);
                    return node.Value;
                }
            }

            // Dentro do lock: duas execuções iguais ao mesmo tempo compilam uma vez só.
            var library = Compile(key, trees, sharedTrees, references, cancellationToken);
            Cache.AddFirst(library);
            while (Cache.Count > MaxCached)
            {
                Cache.Last!.Value.Evict();
                Cache.RemoveLast();
            }
            return library;
        }
    }

    /// <summary>
    /// O assembly das peças, carregado na primeira cena que o usa. <c>null</c> se ele acabou de sair do
    /// cache: quem chamou compila a cena inteira.
    /// </summary>
    public Assembly? Load()
    {
        lock (Gate)
        {
            if (_evicted || _image == null) return null;
            if (_assembly == null)
            {
                _context = new LibraryLoadContext(AssemblyName);
                _assembly = _context.LoadFromStream(new MemoryStream(_image), new MemoryStream(_pdb!));
            }
            return _assembly;
        }
    }

    private void Evict()
    {
        _evicted = true;
        // Cooperativo: uma cena ainda rodando (ou features ainda na tela) seguram o assembly até acabarem.
        _context?.Unload();
        _context = null;
        _assembly = null;
    }

    private static ScenePartsLibrary Compile(
        string key,
        IReadOnlyList<SyntaxTree> trees,
        IEnumerable<SyntaxTree> sharedTrees,
        IReadOnlyList<MetadataReference> references,
        CancellationToken cancellationToken)
    {
        string assemblyName = "CadSceneParts_" + key[..16];
        var internalsVisibleTo = CSharpSyntaxTree.ParseText(
            SourceText.From($"[assembly: System.Runtime.CompilerServices.InternalsVisibleTo(\"{EntryAssemblyName}\")]", Encoding.UTF8),
            RoslynSceneEvaluator.ParseOptions,
            path: "PartsAssemblyInfo.g.cs",
            cancellationToken: cancellationToken);

        var compilation = CSharpCompilation.Create(
            assemblyName,
            sharedTrees.Append(internalsVisibleTo).Concat(trees),
            references,
            RoslynSceneEvaluator.CompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var peStream = new MemoryStream();
        using var pdbStream = new MemoryStream();
        EmitResult emit = compilation.Emit(
            peStream,
            pdbStream,
            options: new EmitOptions(debugInformationFormat: DebugInformationFormat.PortablePdb),
            cancellationToken: cancellationToken);

        return emit.Success
            ? new ScenePartsLibrary(key, assemblyName, peStream.ToArray(), pdbStream.ToArray(), emit.Diagnostics.ToList())
            : new ScenePartsLibrary(key, assemblyName, null, null, Array.Empty<Diagnostic>());
    }

    private static string KeyOf(IReadOnlyList<ScriptDocument> documents)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var document in documents)
        {
            hash.AppendData(Encoding.UTF8.GetBytes(document.FilePath));
            hash.AppendData(new byte[] { 0 });
            hash.AppendData(Encoding.UTF8.GetBytes(document.Text ?? string.Empty));
            hash.AppendData(new byte[] { 0 });
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    /// <summary>Colecionável e sem resolução própria, como o da cena: Domain e runtime vêm do contexto padrão.</summary>
    private sealed class LibraryLoadContext(string name) : AssemblyLoadContext(name, isCollectible: true)
    {
        protected override Assembly? Load(AssemblyName assemblyName) => null;
    }
}
