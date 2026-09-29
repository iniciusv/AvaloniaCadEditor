using System;
using System.Collections.Generic;
using System.Linq;
using AvaloniaCadEditor.Application.Interfaces;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace AvaloniaCadEditor.Infrastructure.Scripting;

/// <summary>
/// Ir para a definição, achar referências e renomear usando o modelo semântico do Roslyn sobre a
/// mesma compilação que executa a cena. Não usa o pacote Workspaces: percorre os identificadores
/// com o nome procurado e compara os símbolos, o que basta para cenas do tamanho de um projeto CAD.
/// </summary>
public sealed class RoslynCodeNavigator : ISceneCodeNavigator
{
    public SymbolReferences? FindReferences(IReadOnlyList<ScriptDocument> documents, string filePath, int position)
    {
        var context = Analyze(documents, filePath, position);
        if (context == null) return null;
        var (compilation, trees, symbol) = context.Value;

        var group = RelatedSymbols(symbol);
        var occurrences = FindOccurrences(compilation, trees, symbol.Name, group);
        var definitions = group
            .SelectMany(s => s.Locations)
            .Where(l => l.IsInSource && trees.Contains(l.SourceTree))
            .Select(l => ToLocation(l.SourceTree!, l.SourceSpan))
            .Distinct()
            .OrderBy(l => l.FilePath, StringComparer.OrdinalIgnoreCase).ThenBy(l => l.Start)
            .ToList();

        return new SymbolReferences(symbol.Name, KindName(symbol), definitions.Count > 0, definitions, occurrences);
    }

    public RenameResult Rename(IReadOnlyList<ScriptDocument> documents, string filePath, int position, string newName)
    {
        newName = (newName ?? string.Empty).Trim();
        if (!SyntaxFacts.IsValidIdentifier(newName) || SyntaxFacts.GetKeywordKind(newName) != SyntaxKind.None)
        {
            throw new InvalidOperationException($"\"{newName}\" não é um nome válido em C#.");
        }

        var context = Analyze(documents, filePath, position)
            ?? throw new InvalidOperationException("Posicione o cursor sobre um nome (variável, peça, parâmetro...) para renomear.");
        // O "?? throw" já desembrulha o Nullable: aqui context é a tupla, sem .Value.
        var (compilation, trees, symbol) = context;

        var group = RelatedSymbols(symbol);
        if (!group.Any(s => s.Locations.Any(l => l.IsInSource && trees.Contains(l.SourceTree))))
        {
            throw new InvalidOperationException($"{symbol.Name} faz parte da API do AvaloniaCad e não pode ser renomeado aqui.");
        }
        if (newName == symbol.Name)
        {
            return new RenameResult(symbol.Name, newName, new Dictionary<string, string>(), 0);
        }

        var occurrences = FindOccurrences(compilation, trees, symbol.Name, group);
        var changed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in occurrences.GroupBy(o => o.FilePath, StringComparer.OrdinalIgnoreCase))
        {
            var doc = documents.First(d => string.Equals(d.FilePath, file.Key, StringComparison.OrdinalIgnoreCase));
            var edits = file.Select(o => new TextChange(new TextSpan(o.Start, o.Length), newName)).OrderBy(e => e.Span.Start);
            changed[doc.FilePath] = SourceText.From(doc.Text ?? string.Empty).WithChanges(edits).ToString();
        }

        // Conflitos (o novo nome já existe no mesmo escopo, esconde outro símbolo...) aparecem como
        // erros novos de compilação: nesse caso nada é alterado.
        var renamed = documents
            .Select(d => changed.TryGetValue(d.FilePath, out var text) ? new ScriptDocument(d.FilePath, text) : d)
            .ToList();
        int errorsBefore = CountErrors(compilation);
        var after = RoslynSceneEvaluator.CreateCompilation(renamed, out _);
        var newErrors = after.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        if (newErrors.Count > errorsBefore)
        {
            var first = newErrors.First();
            var where = first.Location.GetLineSpan();
            throw new InvalidOperationException(
                $"Renomear para \"{newName}\" quebraria a cena: {where.Path}({where.StartLinePosition.Line + 1}): {first.GetMessage()}");
        }

        return new RenameResult(symbol.Name, newName, changed, occurrences.Count);
    }

    // ---------------------------------------------------------------- Internos

    private static (CSharpCompilation Compilation, IReadOnlyList<SyntaxTree> Trees, ISymbol Symbol)? Analyze(
        IReadOnlyList<ScriptDocument> documents, string filePath, int position)
    {
        var compilation = RoslynSceneEvaluator.CreateCompilation(documents, out var trees);
        var tree = trees.FirstOrDefault(t => string.Equals(t.FilePath, filePath, StringComparison.OrdinalIgnoreCase));
        if (tree == null) return null;

        var root = tree.GetRoot();
        position = Math.Clamp(position, 0, Math.Max(0, root.FullSpan.End - 1));
        var token = root.FindToken(position);
        // Cursor logo depois do nome (o caso mais comum ao clicar no fim da palavra).
        if (!token.IsKind(SyntaxKind.IdentifierToken) && position > 0)
        {
            token = root.FindToken(position - 1);
        }
        if (!token.IsKind(SyntaxKind.IdentifierToken)) return null;

        var symbol = SymbolOf(compilation.GetSemanticModel(tree), token);
        return symbol == null ? null : (compilation, trees, Normalize(symbol));
    }

    private static ISymbol? SymbolOf(SemanticModel model, SyntaxToken token)
    {
        var node = token.Parent;
        if (node == null) return null;

        // Declarações: o identificador é o próprio nome declarado.
        var declared = model.GetDeclaredSymbol(node);
        if (declared != null && declared.Name == token.ValueText) return declared;

        var info = model.GetSymbolInfo(node);
        return info.Symbol ?? info.CandidateSymbols.FirstOrDefault();
    }

    /// <summary>Construtor vira o tipo (clicar em "new Flange" é pedir pela Flange).</summary>
    private static ISymbol Normalize(ISymbol symbol) => symbol switch
    {
        IMethodSymbol { MethodKind: MethodKind.Constructor } ctor => ctor.ContainingType.OriginalDefinition,
        IMethodSymbol { ReducedFrom: not null } reduced => reduced.ReducedFrom!.OriginalDefinition,
        _ => symbol.OriginalDefinition
    };

    /// <summary>
    /// O símbolo e os que o C# gera junto com ele: num record posicional, o parâmetro do construtor
    /// primário e a propriedade de mesmo nome são a mesma coisa para quem escreve a cena
    /// (<c>new Flange(Espessura: 4)</c>, <c>f.Espessura</c> e <c>with { Espessura = 4 }</c>).
    /// </summary>
    private static HashSet<ISymbol> RelatedSymbols(ISymbol symbol)
    {
        var group = new HashSet<ISymbol>(SymbolEqualityComparer.Default) { symbol };

        // Nomes distintos em cada ramo: variáveis de padrão no if valem também no else.
        if (symbol is IParameterSymbol { ContainingSymbol: IMethodSymbol { MethodKind: MethodKind.Constructor } parameterOwner } &&
            IsPrimaryConstructor(parameterOwner))
        {
            foreach (var generated in parameterOwner.ContainingType.GetMembers(symbol.Name).OfType<IPropertySymbol>())
                group.Add(generated.OriginalDefinition);
        }
        else if (symbol is IPropertySymbol recordProperty && recordProperty.ContainingType.IsRecord)
        {
            foreach (var primary in recordProperty.ContainingType.InstanceConstructors.Where(IsPrimaryConstructor))
                foreach (var parameter in primary.Parameters.Where(p => p.Name == symbol.Name))
                    group.Add(parameter.OriginalDefinition);
        }

        return group;
    }

    private static bool IsPrimaryConstructor(IMethodSymbol ctor) =>
        ctor.DeclaringSyntaxReferences.Any(r => r.GetSyntax() is TypeDeclarationSyntax);

    private static List<CodeLocation> FindOccurrences(
        CSharpCompilation compilation, IReadOnlyList<SyntaxTree> trees, string name, HashSet<ISymbol> group)
    {
        var result = new List<CodeLocation>();
        foreach (var tree in trees)
        {
            SemanticModel? model = null;
            foreach (var token in tree.GetRoot().DescendantTokens())
            {
                if (!token.IsKind(SyntaxKind.IdentifierToken) || token.ValueText != name) continue;
                model ??= compilation.GetSemanticModel(tree);

                var found = SymbolOf(model, token);
                if (found != null && group.Contains(Normalize(found)))
                {
                    result.Add(ToLocation(tree, token.Span));
                }
            }
        }
        return result;
    }

    private static CodeLocation ToLocation(SyntaxTree tree, TextSpan span)
    {
        var text = tree.GetText();
        var position = text.Lines.GetLinePosition(span.Start);
        string lineText = text.Lines[position.Line].ToString().Trim();
        return new CodeLocation(tree.FilePath, span.Start, span.Length, position.Line + 1, position.Character + 1, lineText);
    }

    private static int CountErrors(CSharpCompilation compilation) =>
        compilation.GetDiagnostics().Count(d => d.Severity == DiagnosticSeverity.Error);

    private static string KindName(ISymbol symbol) => symbol switch
    {
        INamedTypeSymbol { IsRecord: true } => "record",
        INamedTypeSymbol => "tipo",
        IMethodSymbol => "método",
        IPropertySymbol => "propriedade",
        IFieldSymbol { IsConst: true } => "constante",
        IFieldSymbol => "campo",
        IParameterSymbol => "parâmetro",
        ILocalSymbol => "variável",
        IRangeVariableSymbol => "variável",
        _ => symbol.Kind.ToString().ToLowerInvariant()
    };
}
