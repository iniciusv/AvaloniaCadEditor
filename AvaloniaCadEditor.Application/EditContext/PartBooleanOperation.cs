using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using AvaloniaCadEditor.Application.Interfaces;
using AvaloniaCadEditor.Application.Scenes;
using AvaloniaCadEditor.Domain.Features;
using AvaloniaCadEditor.Domain.ValueObjects;

namespace AvaloniaCadEditor.Application.EditContext;

/// <summary>
/// Resultado de <see cref="PartBooleanOperation.ApplyAsync"/>: os arquivos alterados (o da peça e, se algum
/// operando era um sólido solto, a cena), ou o motivo da recusa. Tudo ou nada: se um operando é recusado,
/// nenhum é aplicado.
/// </summary>
/// <param name="InsertedNames">Os nomes dos operandos como ficaram no <c>Build()</c>, na ordem.</param>
/// <param name="GeometryVerified">False se algum operando não pôde ser comparado (ver <see cref="PartMergeCheck"/>).</param>
public sealed record PartBooleanResult(
    IReadOnlyList<ScriptDocument> Changes, string? Refusal, IReadOnlyList<string> Warnings,
    IReadOnlyList<string> InsertedNames, bool GeometryVerified)
{
    public static PartBooleanResult Refuse(string reason) =>
        new(Array.Empty<ScriptDocument>(), reason, Array.Empty<string>(), Array.Empty<string>(), false);

    /// <summary>True se a cena também muda (algum operando era um sólido solto, e ele sai de lá).</summary>
    public bool ChangesScene(string entryFile) => Changes.Any(c => PartSolidInserter.SamePath(c.FilePath, entryFile));
}

/// <summary>
/// Cortar ou Unir com uma peça aberta (duplo clique): a base é a peça, e cada operando entra no <c>Build()</c>
/// dela, em coordenadas locais.
/// <list type="bullet">
/// <item><b>Sólido solto:</b> é consumido, como numa booleana comum: sai da cena e entra no <c>Build()</c>
/// (<see cref="IPartMerger"/>, o mesmo do "Incorporar à peça").</item>
/// <item><b>Outra peça</b> (ex.: o motor que deixa a forma na placa): é copiada como sub-peça,
/// <c>new Tipo(...).Instance("nome", at: ...)</c>, com os parâmetros como literais. A original fica na cena, e
/// a cópia não a referencia.</item>
/// </list>
/// Cada operando é conferido pelo <see cref="PartMergeCheck"/> antes do próximo. Não grava nem aplica: quem
/// chama aplica <see cref="PartBooleanResult.Changes"/>.
/// </summary>
public sealed class PartBooleanOperation
{
    private readonly ISceneEvaluator _evaluator;
    private readonly IPartMerger _merger;

    public PartBooleanOperation(ISceneEvaluator evaluator, IPartMerger merger)
    {
        _evaluator = evaluator ?? throw new ArgumentNullException(nameof(evaluator));
        _merger = merger ?? throw new ArgumentNullException(nameof(merger));
    }

    /// <summary>Cortar vira ferramenta da diferença; Unir entra depois dos cortes, como o Extrude para fora.</summary>
    public static PartMergeMode ModeFor(BooleanOperationType operation) =>
        operation == BooleanOperationType.Subtract ? PartMergeMode.SubtractAsTool : PartMergeMode.UnionAfterCuts;

    /// <param name="documents">Os documentos atuais da cena.</param>
    /// <param name="entryFile">O arquivo de entrada.</param>
    /// <param name="context">A peça aberta.</param>
    /// <param name="operandNames">Os operandos, pelo nome de topo na cena.</param>
    public async Task<PartBooleanResult> ApplyAsync(
        IReadOnlyList<ScriptDocument> documents, string entryFile, PartEditContext context,
        IReadOnlyList<string> operandNames, BooleanOperationType operation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(operandNames);
        if (operandNames.Count == 0) return PartBooleanResult.Refuse("Nenhum operando escolhido.");

        var mode = ModeFor(operation);
        var current = documents.ToList();
        var inserted = new List<string>();
        var warnings = new List<string>();
        bool verified = true;
        PartInstance? firstTarget = null;
        IReadOnlyList<IFeature>? firstFeatures = null;

        foreach (var operandName in operandNames.Distinct())
        {
            var state = await _evaluator.EvaluateAsync(current, cancellationToken);
            if (!state.Success) return PartBooleanResult.Refuse($"A cena não executa: {PartSolidInserter.FirstError(state)}");

            var targets = state.Features.OfType<PartInstance>().Where(i => i.Name == context.InstanceName).ToList();
            if (targets.Count != 1) return PartBooleanResult.Refuse($"A peça aberta '{context.InstanceName}' não está mais na cena.");
            var target = targets[0];
            firstTarget ??= target;
            firstFeatures ??= state.Features;

            if (operandName == target.Name) continue; // a própria peça é a base
            var operand = state.Features.FirstOrDefault(f => f.Name == operandName);
            if (operand == null) return PartBooleanResult.Refuse($"'{operandName}' não está na cena.");

            var step = operand is PartInstance part
                ? await CopyPartAsync(current, entryFile, context, target, part, state.Features, mode, cancellationToken)
                : await ConsumeSolidAsync(current, entryFile, target, operand, state.Features, mode, cancellationToken);
            if (step.Refusal != null) return PartBooleanResult.Refuse($"{operandName}: {step.Refusal}");

            current = Apply(current, step.Changes);
            inserted.Add(step.InsertedName!);
            warnings.AddRange(step.Warnings);
            verified &= step.Verified;
        }

        if (inserted.Count == 0) return PartBooleanResult.Refuse("Escolha um sólido além da própria peça.");
        warnings.AddRange(PartMergeWarnings.For(firstTarget!, firstFeatures!));

        var changes = current
            .Where(d => documents.FirstOrDefault(o => PartSolidInserter.SamePath(o.FilePath, d.FilePath)) is not { } original || original.Text != d.Text)
            .ToList();
        return new PartBooleanResult(changes, null, warnings.Distinct().ToList(), inserted, verified);
    }

    private sealed record Step(IReadOnlyList<ScriptDocument> Changes, string? Refusal, IReadOnlyList<string> Warnings, string? InsertedName, bool Verified)
    {
        public static Step Refuse(string reason) => new(Array.Empty<ScriptDocument>(), reason, Array.Empty<string>(), null, false);
    }

    // ---------------------------------------------------------------- Sólido solto: consumido

    private async Task<Step> ConsumeSolidAsync(
        IReadOnlyList<ScriptDocument> documents, string entryFile, PartInstance target, IFeature operand,
        IReadOnlyList<IFeature> features, PartMergeMode mode, CancellationToken cancellationToken)
    {
        var entry = documents.FirstOrDefault(d => PartSolidInserter.SamePath(d.FilePath, entryFile));
        if (entry == null) return Step.Refuse($"o arquivo de entrada {entryFile} não faz parte da cena.");
        int line = PartSolidInserter.RegistrationLine(entry.Text, operand.Name);
        if (line == 0) return Step.Refuse($"não achei o AddFeature({operand.Name}) em {entryFile}.");

        var merge = _merger.Merge(documents, new PartMergeRequest(
            entry.FilePath, line, target.PartTypeName, target.Placement, (operand as TransformedFeature)?.Placement, mode));
        if (merge.Refusal != null) return Step.Refuse(merge.Refusal);

        var after = await _evaluator.EvaluateAsync(merge.ApplyTo(documents), cancellationToken);
        var verdict = PartMergeCheck.Verify(operand, features, after, target.Name);
        if (verdict.Problem != null) return Step.Refuse(verdict.Problem);

        return new Step(merge.Changes, null, merge.Warnings, merge.InsertedName, verdict.GeometryVerified);
    }

    // ---------------------------------------------------------------- Outra peça: copiada como sub-peça

    private async Task<Step> CopyPartAsync(
        IReadOnlyList<ScriptDocument> documents, string entryFile, PartEditContext context, PartInstance target,
        PartInstance operand, IReadOnlyList<IFeature> features, PartMergeMode mode, CancellationToken cancellationToken)
    {
        // Uma peça dentro dela mesma (direta ou por uma sub-peça) nunca terminaria de construir.
        if (operand.PartTypeName == target.PartTypeName || Contains(operand.Definition, target.PartTypeName))
            return Step.Refuse($"{operand.PartTypeName} contém {target.PartTypeName}; não dá para usá-la dentro dela.");

        string? construction = PartConstruction(operand.Part, out string? problem);
        if (construction == null) return Step.Refuse(problem!);

        var taken = features.Select(f => f.Name).ToHashSet(StringComparer.Ordinal);
        string name = Unique($"{Identifier(operand.Name)}_copia", taken);
        string snippet =
            $"var {name} = {construction}.Instance(\"{name}\");\n" +
            $"AddFeature({name}.Transformed({PlacementSource.Code(operand.Placement)}));";

        var insertion = await new PartSolidInserter(_evaluator, _merger)
            .InsertAsync(documents, entryFile, snippet, context, mode, cancellationToken);
        if (insertion.Refusal != null) return Step.Refuse(insertion.Refusal);

        // Os avisos de instâncias e espelho são da peça alvo, e saem uma vez só no fim.
        var own = insertion.Warnings.Except(PartMergeWarnings.For(target, features)).ToList();
        return new Step(insertion.Changes, null, own, insertion.InsertedName, insertion.GeometryVerified);
    }

    private static bool Contains(IFeature feature, string partTypeName) => feature switch
    {
        PartInstance instance => instance.PartTypeName == partTypeName || Contains(instance.Definition, partTypeName),
        BooleanFeature boolean => boolean.BaseFeatures.Concat(boolean.ToolFeatures).Any(f => Contains(f, partTypeName)),
        TransformedFeature transformed => Contains(transformed.Source, partTypeName),
        FilletFeature fillet => Contains(fillet.BaseFeature, partTypeName),
        _ => false
    };

    /// <summary>
    /// <c>new Tipo(Param: valor, ...)</c> com os valores desta peça como literais; só os que diferem do padrão
    /// do construtor. Null (com o motivo) se algum parâmetro não for um valor simples.
    /// </summary>
    internal static string? PartConstruction(Part part, out string? problem)
    {
        problem = null;
        var type = part.GetType();
        var parameters = part.GetParameters();
        var constructor = type.GetConstructors()
            .FirstOrDefault(c => c.GetParameters().Select(p => p.Name).SequenceEqual(parameters.Select(p => p.Name)));

        var arguments = new List<string>();
        for (int i = 0; i < parameters.Count; i++)
        {
            var parameter = parameters[i];
            var info = constructor?.GetParameters()[i];
            if (info is { HasDefaultValue: true } && Equals(Normalize(info.DefaultValue, parameter.Type), parameter.Value)) continue;

            string? literal = Literal(parameter.Value, parameter.Type);
            if (literal == null)
            {
                problem = $"o parâmetro {parameter.Name} de {type.Name} não é um valor simples (número, texto, bool, enum, ponto), e a cópia não saberia escrevê-lo.";
                return null;
            }
            arguments.Add($"{parameter.Name}: {literal}");
        }
        return $"new {type.Name}({string.Join(", ", arguments)})";
    }

    private static object? Normalize(object? value, Type type)
    {
        if (value == null || type.IsInstanceOfType(value)) return value;
        try
        {
            var target = Nullable.GetUnderlyingType(type) ?? type;
            return target.IsEnum ? Enum.ToObject(target, value) : Convert.ChangeType(value, target, CultureInfo.InvariantCulture);
        }
        catch (Exception ex) when (ex is InvalidCastException or FormatException or OverflowException or ArgumentException)
        {
            return value;
        }
    }

    private static string? Literal(object? value, Type type)
    {
        if (value == null) return "null";
        switch (value)
        {
            case double d: return PlacementSource.Number(d);
            case float f: return PlacementSource.Number(f) + "f";
            case decimal m: return m.ToString(CultureInfo.InvariantCulture) + "m";
            case int or long or short or byte: return Convert.ToString(value, CultureInfo.InvariantCulture);
            case bool b: return b ? "true" : "false";
            case string s: return Microsoft.CodeAnalysis.CSharp.SymbolDisplay.FormatLiteral(s, quote: true);
            case Point3D p: return PlacementSource.Point(p);
            case Vector3D v: return PlacementSource.Vector(v);
            case Enum e when Enum.IsDefined(e.GetType(), e): return $"{e.GetType().Name}.{e}";
            default: return null;
        }
    }

    private static string Identifier(string name)
    {
        var chars = name.Select(c => char.IsLetterOrDigit(c) || c == '_' ? c : '_').ToArray();
        string id = new(chars);
        return id.Length == 0 || char.IsDigit(id[0]) ? "_" + id : id;
    }

    private static string Unique(string name, HashSet<string> taken)
    {
        if (!taken.Contains(name)) return name;
        for (int i = 2; ; i++)
        {
            string candidate = $"{name}_{i}";
            if (!taken.Contains(candidate)) return candidate;
        }
    }

    private static List<ScriptDocument> Apply(IReadOnlyList<ScriptDocument> documents, IReadOnlyList<ScriptDocument> changes)
    {
        var result = documents.Select(d => changes.FirstOrDefault(c => PartSolidInserter.SamePath(c.FilePath, d.FilePath)) ?? d).ToList();
        result.AddRange(changes.Where(c => !documents.Any(d => PartSolidInserter.SamePath(d.FilePath, c.FilePath))));
        return result;
    }
}
