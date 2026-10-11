using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using AvaloniaCadEditor.Application.Interfaces;
using AvaloniaCadEditor.Domain.ValueObjects;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.Text;

namespace AvaloniaCadEditor.Infrastructure.Scripting;

/// <summary>
/// Incorpora um sólido solto da cena ao <c>Build()</c> de uma peça existente (ver <see cref="IPartMerger"/>).
/// <para>
/// O recorte é o mesmo do <see cref="RoslynPartConverter"/> (<see cref="SceneSlice"/>). A diferença está
/// nas coordenadas: o sólido foi desenhado no referencial do MUNDO e o <c>Build()</c> é LOCAL, então cada
/// <c>Point3D</c>/<c>Vector3D</c> do recorte é reescrito pela transformação mundo→local, conforme o papel
/// que ele tem no construtor em que é usado (ponto, direção, ou vértice no plano do prisma — que não muda,
/// porque o prisma gira junto com o seu <c>WorkPlane</c>). Quando algum valor não é literal ou tem um papel
/// desconhecido, nada é reescrito: o código fica como estava e a raiz ganha um <c>.Transformed(...)</c>.
/// </para>
/// </summary>
public sealed class RoslynPartMerger : IPartMerger
{
    private const double Tolerance = 1e-9;

    public PartMerge Merge(IReadOnlyList<ScriptDocument> documents, PartMergeRequest request)
    {
        var compilation = RoslynSceneEvaluator.CreateCompilation(documents, out var trees);
        if (compilation.GetDiagnostics().FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error) is { } error)
            return PartMerge.Refuse($"Corrija os erros da cena antes de incorporar ({error.GetMessage(CultureInfo.CurrentCulture)}).");

        var tree = trees.FirstOrDefault(t => RoslynPartConverter.SamePath(t.FilePath, request.FilePath));
        if (tree == null) return PartMerge.Refuse($"O arquivo {request.FilePath} não faz parte da cena.");

        var scene = new SceneStatements(tree, compilation);
        if (scene.Statements.Count == 0)
            return PartMerge.Refuse("Só dá para incorporar sólidos registrados no arquivo de entrada da cena.");

        var registration = scene.Registrations.FirstOrDefault(r => scene.Covers(r.Statement, request.Line));
        if (registration == null)
            return PartMerge.Refuse("Este sólido não foi registrado com AddFeature(variavel) — não há uma variável para incorporar.");
        if (scene.Registrations.Count(r => SymbolEqualityComparer.Default.Equals(r.Local, registration.Local)) > 1)
            return PartMerge.Refuse($"'{registration.Local.Name}' é registrado mais de uma vez com AddFeature.");
        if (!scene.Declarations.ContainsKey(registration.Local))
            return PartMerge.Refuse($"'{registration.Local.Name}' precisa ser declarado sozinho no topo da cena (var {registration.Local.Name} = ...;).");

        var slice = SceneSlice.Compute(scene, registration);
        if (slice.Refusal != null) return PartMerge.Refuse(slice.Refusal);
        if (slice.Inputs.Count > 0)
            return PartMerge.Refuse(
                $"O sólido lê variáveis da cena ({string.Join(", ", slice.Inputs.Select(i => i.Name))}); " +
                "só dá para incorporar sólidos escritos com valores literais.");

        var partTree = FindPartTree(trees, request.PartTypeName, out string? partRefusal);
        if (partRefusal != null) return PartMerge.Refuse(partRefusal);
        if (partTree == tree)
            return PartMerge.Refuse($"{request.PartTypeName} está declarada no arquivo da cena; mova-a para um arquivo próprio em Pecas/ antes de incorporar.");

        // Mundo → local. O deslocamento do registro (AddFeature(x.Transformed(P))) some com o AddFeature,
        // então entra na conta; um .Transformed na própria declaração fica no código e não entra.
        if (registration.Placement != null && request.RegisteredMove == null)
            return PartMerge.Refuse($"'{registration.Local.Name}' é registrado deslocado, mas o deslocamento avaliado não foi informado.");
        var registered = registration.Placement != null ? request.RegisteredMove! : Placement.Identity;
        var worldToLocal = registered.Then(request.TargetPlacement.Inverse());

        var warnings = new List<string>();
        var coordinates = CoordinateRewriter.Rewrite(scene, slice, registration.Local, worldToLocal);
        if (coordinates.Refusal != null) return PartMerge.Refuse(coordinates.Refusal);
        if (coordinates.FallbackReason != null)
            warnings.Add($"As coordenadas não foram reescritas ({coordinates.FallbackReason}); o sólido entrou como estava, " +
                         "envolvido num .Transformed(...) que o leva para o referencial da peça.");

        string partSource = partTree!.GetText().ToString();
        string newLine = partSource.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var insertion = BuildInsertion(scene, slice, coordinates.Changes, request.FilePath);

        var inserted = BuildEditor.Insert(partSource, request.PartTypeName, insertion, registration.Local.Name, request.Mode, newLine);
        if (inserted.Refusal != null) return PartMerge.Refuse(inserted.Refusal);

        string sceneText = RemoveFromScene(scene, slice, registration);
        return new PartMerge(new[]
        {
            new ScriptDocument(tree.FilePath, sceneText),
            new ScriptDocument(partTree.FilePath, inserted.Source!)
        }, null, warnings, registration.Local.Name);
    }

    // ---------------------------------------------------------------- Onde está a peça

    private static SyntaxTree? FindPartTree(IReadOnlyList<SyntaxTree> trees, string typeName, out string? refusal)
    {
        var found = trees
            .SelectMany(t => t.GetRoot().DescendantNodes().OfType<RecordDeclarationSyntax>().Select(r => (Tree: t, Record: r)))
            .Where(x => x.Record.Identifier.ValueText == typeName && IsPart(x.Record))
            .ToList();
        refusal = found.Count switch
        {
            0 => $"Não achei a declaração de {typeName} nos arquivos da cena.",
            > 1 => $"{typeName} está declarada mais de uma vez.",
            _ => null
        };
        return found.Count == 1 ? found[0].Tree : null;
    }

    private static bool IsPart(RecordDeclarationSyntax record) =>
        record.BaseList?.Types.Any(t => t.Type.ToString() is "Part" || t.Type.ToString().EndsWith(".Part", StringComparison.Ordinal)) == true;

    // ---------------------------------------------------------------- O texto que vai para o Build()

    /// <summary>As instruções do recorte, na ordem do arquivo, com as coordenadas já reescritas.</summary>
    private sealed record Insertion(IReadOnlyList<string> Lines, IReadOnlyCollection<string> DeclaredNames, IReadOnlyCollection<string> Literals);

    private static Insertion BuildInsertion(SceneStatements scene, SceneSlice slice, IReadOnlyList<TextChange> changes, string sceneFile)
    {
        var text = new StringBuilder();
        text.Append("// Incorporado da cena (").Append(sceneFile).Append("): coordenadas convertidas para o referencial local da peça.\n");
        bool first = true;
        foreach (var statement in slice.Statements)
        {
            var (keep, move) = RoslynPartConverter.SplitLeadingTrivia(statement);
            if (!first && keep.Length > 0) text.Append('\n');
            first = false;

            string trailing = statement.GetTrailingTrivia().ToFullString().TrimEnd('\r', '\n');
            text.Append(RoslynPartConverter.Dedent(move + WithChanges(statement, changes) + trailing)).Append('\n');
        }

        var declared = slice.Statements
            .SelectMany(s => s.DescendantNodesAndSelf())
            .Select(n => n switch
            {
                VariableDeclaratorSyntax v => v.Identifier.ValueText,
                LocalFunctionStatementSyntax f => f.Identifier.ValueText,
                SingleVariableDesignationSyntax d => d.Identifier.ValueText,
                _ => null
            })
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);

        var literals = slice.Statements
            .SelectMany(s => s.DescendantTokens())
            .Where(t => t.IsKind(SyntaxKind.StringLiteralToken))
            .Select(t => t.ValueText)
            .ToHashSet(StringComparer.Ordinal);

        return new Insertion(RoslynPartConverter.Lines(text.ToString()).ToList(), declared, literals);
    }

    /// <summary>O texto da instrução (sem a trivia de cima) com as trocas que caem dentro dela.</summary>
    private static string WithChanges(StatementSyntax statement, IReadOnlyList<TextChange> changes)
    {
        int offset = statement.Span.Start;
        var inside = changes
            .Where(c => c.Span.Start >= statement.Span.Start && c.Span.End <= statement.Span.End)
            .Select(c => new TextChange(new TextSpan(c.Span.Start - offset, c.Span.Length), c.NewText!))
            .OrderBy(c => c.Span.Start);
        return SourceText.From(statement.ToString()).WithChanges(inside).ToString();
    }

    // ---------------------------------------------------------------- A cena

    private static string RemoveFromScene(SceneStatements scene, SceneSlice slice, SceneRegistration root)
    {
        var changes = new List<TextChange>();
        foreach (var statement in slice.Removed.Append(root.Statement).Distinct())
        {
            // Como no RoslynPartConverter: linhas em branco soltas acima saem junto; o título de uma
            // seção (separado por linha em branco) fica.
            var (keep, _) = RoslynPartConverter.SplitLeadingTrivia(statement);
            int start = string.IsNullOrWhiteSpace(keep) ? statement.FullSpan.Start : statement.FullSpan.Start + keep.Length;
            changes.Add(new TextChange(TextSpan.FromBounds(start, statement.FullSpan.End), string.Empty));
        }
        return scene.Tree.GetText().WithChanges(changes.OrderBy(c => c.Span.Start)).ToString();
    }

    // ---------------------------------------------------------------- Formatação

    /// <summary>Número sem ruído de ponto flutuante (como o PartSourceEditor: 6 casas, sem "-0").</summary>
    internal static string Number(double value)
    {
        double rounded = Math.Round(value, 6);
        if (rounded == 0) rounded = 0;
        return rounded.ToString("0.######", CultureInfo.InvariantCulture);
    }

    internal static string PointCode(Point3D p) => $"new Point3D({Number(p.X)}, {Number(p.Y)}, {Number(p.Z)})";

    internal static string VectorCode(Vector3D v) => $"new Vector3D({Number(v.X)}, {Number(v.Y)}, {Number(v.Z)})";

    /// <summary>
    /// O placement como código, quando ele é uma rotação só em Z (o caso de <c>Placement.At</c>),
    /// opcionalmente precedida de um espelho; senão null.
    /// </summary>
    internal static string? PlacementCode(Placement placement)
    {
        if (placement.IsMirrored)
        {
            // MirrorX é a própria inversa: placement = MirrorX ∘ (MirrorX ∘ placement), e o segundo
            // fator já não espelha. MirrorX preserva Z, então ele é só-Z quando o placement era.
            string? rest = PlacementCode(Placement.MirrorX().Then(placement));
            return rest == null ? null : $"Placement.MirrorX().Then({rest})";
        }

        var t = placement.Translation;
        if (!placement.HasRotation) return $"Placement.Translate({VectorCode(t)})";
        if (!placement.IsRotationAboutZOnly) return null;
        double degrees = placement.RotationZRadians * 180.0 / Math.PI;
        if (degrees <= -180 + 1e-9) degrees += 360; // (-180, 180]: o inverso de 180° sai como -180°
        return $"Placement.At({Number(t.X)}, {Number(t.Y)}, {Number(t.Z)}, rotationZDegrees: {Number(degrees)})";
    }

    internal static bool Same(Point3D a, Point3D b) =>
        Math.Abs(a.X - b.X) < Tolerance && Math.Abs(a.Y - b.Y) < Tolerance && Math.Abs(a.Z - b.Z) < Tolerance;

    internal static bool Same(Vector3D a, Vector3D b) =>
        Math.Abs(a.X - b.X) < Tolerance && Math.Abs(a.Y - b.Y) < Tolerance && Math.Abs(a.Z - b.Z) < Tolerance;

    /// <summary><c>BlocoFixacaoBelt</c> vira <c>bloco_fixacao_belt</c>.</summary>
    internal static string SnakeCase(string name) =>
        Regex.Replace(name, "(?<=[a-z0-9])([A-Z])", "_$1").ToLowerInvariant();

    // ================================================================ Coordenadas

    /// <summary>
    /// Reescreve, por papel semântico, cada <c>Point3D</c>/<c>Vector3D</c> do recorte para o referencial
    /// da peça. Tudo ou nada: se um valor não puder ser reescrito com segurança, nenhum é, e a raiz é
    /// envolvida num <c>.Transformed(...)</c>.
    /// </summary>
    private sealed class CoordinateRewriter
    {
        private enum Role { Point, Direction, PlaneLocal, Unknown }

        public sealed record Result(IReadOnlyList<TextChange> Changes, string? FallbackReason, string? Refusal);

        private readonly SceneStatements _scene;
        private readonly SceneSlice _slice;
        private readonly Placement _transform;
        private readonly List<TextChange> _changes = new();

        /// <summary>Trechos já trocados por inteiro (ex.: um WorkPlane de 2 argumentos): o que está dentro não é revisitado.</summary>
        private readonly List<TextSpan> _replaced = new();

        private CoordinateRewriter(SceneStatements scene, SceneSlice slice, Placement transform)
        {
            _scene = scene;
            _slice = slice;
            _transform = transform;
        }

        public static Result Rewrite(SceneStatements scene, SceneSlice slice, ILocalSymbol root, Placement transform)
        {
            if (transform.IsIdentity) return new Result(Array.Empty<TextChange>(), null, null);

            var rewriter = new CoordinateRewriter(scene, slice, transform);
            string? reason = rewriter.TryRewrite();
            if (reason == null) return new Result(rewriter._changes, null, null);

            // Fallback: o código fica como foi desenhado e a raiz ganha o .Transformed(...).
            string? placement = PlacementCode(transform);
            if (placement == null)
                return new Result(Array.Empty<TextChange>(), null,
                    $"Não dá para incorporar: {reason}, e a rotação da peça não é só em torno de Z.");

            var declaration = (LocalDeclarationStatementSyntax)scene.Declarations[root];
            var value = declaration.Declaration.Variables[0].Initializer!.Value;
            bool bare = value is ObjectCreationExpressionSyntax or ImplicitObjectCreationExpressionSyntax or
                InvocationExpressionSyntax or IdentifierNameSyntax or MemberAccessExpressionSyntax or ParenthesizedExpressionSyntax;
            var changes = new List<TextChange>();
            if (!bare) changes.Add(new TextChange(new TextSpan(value.SpanStart, 0), "("));
            changes.Add(new TextChange(new TextSpan(value.Span.End, 0), (bare ? string.Empty : ")") + $".Transformed({placement})"));
            return new Result(changes, reason, null);
        }

        /// <summary>Faz as trocas; devolve por que não foi possível (e então nada vale), ou null.</summary>
        private string? TryRewrite()
        {
            // Espelho: os WorkPlane reescritos (normal + eixo X) recalculam o eixo Y pela mão direita e
            // desfariam a reflexão das coordenadas no plano. Mais seguro deixar o código como está.
            if (_transform.IsMirrored) return "o sólido ou a peça está espelhado";

            var nodes = _slice.Statements.SelectMany(s => s.DescendantNodes()).ToList();

            // Toda feature do recorte precisa ser de um tipo cujas coordenadas sabemos reescrever.
            foreach (var creation in nodes.OfType<BaseObjectCreationExpressionSyntax>())
            {
                var type = TypeOf(creation);
                if (!IsFeature(type)) continue;
                if (type!.Name is not ("BoxFeature" or "CylinderFeature" or "ConeFeature" or "PrismFeature" or "BooleanFeature"))
                    return $"'{type.Name}' não tem as coordenadas reescritas automaticamente";
                if (type.Name == "PrismFeature" && !HasExplicitArgument(creation, "plane"))
                    return "um PrismFeature não informa o plane (o plano padrão não acompanharia a rotação)";
            }

            // Chamadas que produzem sólidos já posicionados (Transformed, Instance...) não são reescritas.
            foreach (var call in nodes.OfType<InvocationExpressionSyntax>())
            {
                if (_scene.Model.GetSymbolInfo(call).Symbol is not IMethodSymbol method) continue;
                if (method.ContainingType?.Name == "BooleanFeature" && method.Name.StartsWith("Create", StringComparison.Ordinal)) continue;
                if (IsFeature(method.ReturnType) || method.Name is "Transformed" or "Instance")
                    return $"'{method.Name}(...)' gera um sólido já posicionado";
            }

            // WorkPlane(normal, origin): com rotação, o eixo X automático sairia errado — vira o construtor de 3 argumentos.
            foreach (var creation in nodes.OfType<BaseObjectCreationExpressionSyntax>().Where(c => TypeOf(c)?.Name == "WorkPlane"))
            {
                if (_scene.Model.GetOperation(creation) is not IObjectCreationOperation op || op.Constructor == null) return "um WorkPlane não pôde ser analisado";
                if (op.Constructor.Parameters.Length != 2 || !_transform.HasRotation) continue;

                if (!TryArgumentVector(op, "normal", out var normal) || !TryArgumentPoint(op, "origin", out var origin))
                    return "um WorkPlane tem valores que não são literais";
                var plane = new WorkPlane(normal, origin);
                Replace(creation.Span,
                    $"new WorkPlane({PointCode(_transform.Apply(plane.Origin))}, {VectorCode(_transform.ApplyVector(plane.Normal))}, {VectorCode(_transform.ApplyVector(plane.XAxis))})");
            }

            // Caixas giradas: as direções não estão nos argumentos, vão no inicializador.
            if (_transform.HasRotation)
            {
                foreach (var creation in nodes.OfType<BaseObjectCreationExpressionSyntax>().Where(c => TypeOf(c)?.Name == "BoxFeature"))
                {
                    string? reason = RotateBoxDirections(creation);
                    if (reason != null) return reason;
                }
            }

            // Cada Point3D/Vector3D, pelo papel que tem.
            foreach (var expression in nodes.OfType<ExpressionSyntax>())
            {
                if (IsInsideReplaced(expression.Span)) continue;
                if (_scene.Model.GetSymbolInfo(expression).Symbol is ITypeSymbol) continue; // o nome do tipo em new Point3D(...), Point3D.Origin
                if (expression.Parent is MemberAccessExpressionSyntax access && access.Name == expression) continue; // o "UnitZ" de Vector3D.UnitZ: visitado como um todo
                var type = _scene.Model.GetTypeInfo(expression).Type;
                if (type?.Name is not ("Point3D" or "Vector3D" or "WorkPlane") || !IsDomainType(type)) continue;

                switch (expression)
                {
                    case BaseObjectCreationExpressionSyntax creation when type.Name == "WorkPlane":
                        continue; // os argumentos dele são visitados como Point3D/Vector3D
                    case IdentifierNameSyntax identifier when IsSliceLocal(identifier):
                        continue; // o inicializador da variável é que é reescrito
                    case BaseObjectCreationExpressionSyntax or MemberAccessExpressionSyntax when type.Name != "WorkPlane":
                        string? reason = RewriteValue(expression, type.Name == "Point3D");
                        if (reason != null) return reason;
                        continue;
                    default:
                        return $"'{expression}' calcula uma coordenada que não dá para converter literalmente";
                }
            }
            return null;
        }

        private string? RewriteValue(ExpressionSyntax expression, bool isPoint)
        {
            var role = RoleOf(expression);
            switch (role)
            {
                case Role.PlaneLocal:
                    return null;
                case Role.Unknown:
                    return $"'{expression}' não é usado num lugar cujo referencial o app conheça";
            }

            if (!TryConstant(expression, out var x, out var y, out var z))
                return $"'{expression}' não é literal";

            if (role == Role.Point)
            {
                var original = new Point3D(x, y, z);
                var local = _transform.Apply(original);
                if (!Same(original, local)) Replace(expression.Span, PointCode(local));
            }
            else
            {
                var original = new Vector3D(x, y, z);
                var local = _transform.ApplyVector(original);
                if (!Same(original, local)) Replace(expression.Span, VectorCode(local));
            }
            return null;
        }

        /// <summary>
        /// O papel de um Point3D/Vector3D: o parâmetro do construtor que o recebe, ou — para os vértices de
        /// um prisma guardados num array — o parâmetro que recebe o array em todos os usos.
        /// </summary>
        private Role RoleOf(ExpressionSyntax expression)
        {
            SyntaxNode node = expression;
            while (node.Parent is ParenthesizedExpressionSyntax) node = node.Parent;

            switch (node.Parent)
            {
                case ArgumentSyntax argument:
                    return RoleOf(argument);
                case AssignmentExpressionSyntax { Parent: InitializerExpressionSyntax { Parent: BaseObjectCreationExpressionSyntax owner } } assignment
                    when assignment.Right == node && TypeOf(owner)?.Name == "BoxFeature" &&
                         assignment.Left is IdentifierNameSyntax { Identifier.ValueText: "DirectionX" or "DirectionY" or "DirectionZ" }:
                    return Role.Direction;
                case InitializerExpressionSyntax initializer when initializer.IsKind(SyntaxKind.ArrayInitializerExpression) ||
                                                                 initializer.IsKind(SyntaxKind.CollectionInitializerExpression):
                    return ContainerRole(initializer.Parent);
                case ExpressionElementSyntax { Parent: CollectionExpressionSyntax collection }:
                    return ContainerRole(collection);
                default:
                    return Role.Unknown;
            }
        }

        private Role RoleOf(ArgumentSyntax argument)
        {
            if (_scene.Model.GetOperation(argument) is not IArgumentOperation { Parameter: { } parameter }) return Role.Unknown;
            if (parameter.ContainingSymbol is not IMethodSymbol { MethodKind: MethodKind.Constructor } constructor) return Role.Unknown;
            if (!IsDomainType(constructor.ContainingType)) return Role.Unknown;

            return (constructor.ContainingType.Name, parameter.Name) switch
            {
                ("CylinderFeature" or "ConeFeature", "globalBaseCenter") => Role.Point,
                ("CylinderFeature" or "ConeFeature", "globalDirection") => Role.Direction,
                ("WorkPlane", "origin") => Role.Point,
                ("WorkPlane", "normal" or "xAxis") => Role.Direction,
                ("BoxFeature", "baseCorner") => Role.Point,
                ("PrismFeature", "vertices") => Role.PlaneLocal,
                _ => Role.Unknown
            };
        }

        /// <summary>Um array de pontos: vale o papel de quem o recebe (direto, ou pela variável em que foi guardado).</summary>
        private Role ContainerRole(SyntaxNode? container)
        {
            if (container == null) return Role.Unknown;
            switch (container.Parent)
            {
                case ArgumentSyntax argument:
                    return RoleOf(argument) == Role.PlaneLocal ? Role.PlaneLocal : Role.Unknown;
                case EqualsValueClauseSyntax { Parent: VariableDeclaratorSyntax declarator }
                    when _scene.Model.GetDeclaredSymbol(declarator) is ILocalSymbol local:
                    var uses = _slice.Statements
                        .SelectMany(s => s.DescendantNodes().OfType<IdentifierNameSyntax>())
                        .Where(id => SymbolEqualityComparer.Default.Equals(_scene.Model.GetSymbolInfo(id).Symbol, local))
                        .ToList();
                    return uses.All(id => id.Parent is ArgumentSyntax a && RoleOf(a) == Role.PlaneLocal) ? Role.PlaneLocal : Role.Unknown;
                default:
                    return Role.Unknown;
            }
        }

        /// <summary>Acrescenta (ou deixa para a reescrita por papel) as direções de uma caixa girada.</summary>
        private string? RotateBoxDirections(BaseObjectCreationExpressionSyntax creation)
        {
            var assigned = creation.Initializer?.Expressions
                .OfType<AssignmentExpressionSyntax>()
                .Select(a => (a.Left as IdentifierNameSyntax)?.Identifier.ValueText)
                .OfType<string>()
                .ToHashSet() ?? new HashSet<string>();

            var defaults = new (string Name, Vector3D Value)[]
            {
                ("DirectionX", new Vector3D(1, 0, 0)), ("DirectionY", new Vector3D(0, 1, 0)), ("DirectionZ", new Vector3D(0, 0, 1))
            };
            var missing = defaults
                .Where(d => !assigned.Contains(d.Name))
                .Select(d => $"{d.Name} = {VectorCode(_transform.ApplyVector(d.Value))}")
                .ToList();
            if (missing.Count == 0) return null; // as atribuições existentes são reescritas como direções

            if (creation.Initializer == null)
            {
                _changes.Add(new TextChange(new TextSpan(creation.Span.End, 0), " { " + string.Join(", ", missing) + " }"));
            }
            else
            {
                var expressions = creation.Initializer.Expressions;
                string joined = string.Join(", ", missing);
                if (expressions.Count == 0)
                    _changes.Add(new TextChange(new TextSpan(creation.Initializer.OpenBraceToken.Span.End, 0), " " + joined + " "));
                else if (expressions.SeparatorCount == expressions.Count) // vírgula sobrando no fim
                    _changes.Add(new TextChange(new TextSpan(expressions.GetSeparator(expressions.SeparatorCount - 1).Span.End, 0), " " + joined));
                else
                    _changes.Add(new TextChange(new TextSpan(expressions.Last().Span.End, 0), ", " + joined));
            }
            return null;
        }

        private bool TryConstant(ExpressionSyntax expression, out double x, out double y, out double z)
        {
            x = y = z = 0;
            if (expression is MemberAccessExpressionSyntax member)
            {
                // Só as constantes estáticas (Point3D.Origin, Vector3D.UnitZ), não plano.Origin.
                if (_scene.Model.GetSymbolInfo(member.Expression).Symbol is not ITypeSymbol) return false;
                switch (member.Name.Identifier.ValueText)
                {
                    case "Origin": return true;
                    case "UnitX": x = 1; return true;
                    case "UnitY": y = 1; return true;
                    case "UnitZ": z = 1; return true;
                    default: return false;
                }
            }

            if (_scene.Model.GetOperation(expression) is not IObjectCreationOperation op) return false;
            if (op.Arguments.Length == 0) return true;
            if (op.Arguments.Length != 3) return false;

            var values = new double[3];
            foreach (var argument in op.Arguments)
            {
                if (argument.Parameter == null || !TryDouble(argument.Value, out double value)) return false;
                values[argument.Parameter.Ordinal] = value;
            }
            (x, y, z) = (values[0], values[1], values[2]);
            return true;
        }

        private bool TryArgumentPoint(IObjectCreationOperation op, string parameter, out Point3D point)
        {
            point = default;
            var argument = op.Arguments.FirstOrDefault(a => a.Parameter?.Name == parameter);
            if (argument?.Value.Syntax is not ExpressionSyntax syntax || !TryConstant(Unwrap(syntax), out var x, out var y, out var z)) return false;
            point = new Point3D(x, y, z);
            return true;
        }

        private bool TryArgumentVector(IObjectCreationOperation op, string parameter, out Vector3D vector)
        {
            vector = default;
            var argument = op.Arguments.FirstOrDefault(a => a.Parameter?.Name == parameter);
            if (argument?.Value.Syntax is not ExpressionSyntax syntax || !TryConstant(Unwrap(syntax), out var x, out var y, out var z)) return false;
            vector = new Vector3D(x, y, z);
            return true;
        }

        private static ExpressionSyntax Unwrap(ExpressionSyntax syntax) =>
            syntax is ParenthesizedExpressionSyntax p ? Unwrap(p.Expression) : syntax;

        private static bool TryDouble(IOperation value, out double result)
        {
            result = 0;
            if (!value.ConstantValue.HasValue || value.ConstantValue.Value == null) return false;
            try
            {
                result = Convert.ToDouble(value.ConstantValue.Value, CultureInfo.InvariantCulture);
                return true;
            }
            catch (Exception ex) when (ex is InvalidCastException or FormatException)
            {
                return false;
            }
        }

        private bool HasExplicitArgument(BaseObjectCreationExpressionSyntax creation, string parameter) =>
            _scene.Model.GetOperation(creation) is IObjectCreationOperation op &&
            op.Arguments.Any(a => a.Parameter?.Name == parameter && a.ArgumentKind == ArgumentKind.Explicit);

        private bool IsSliceLocal(IdentifierNameSyntax identifier) =>
            _scene.Model.GetSymbolInfo(identifier).Symbol is ILocalSymbol local &&
            _scene.Declarations.TryGetValue(local, out var declaration) && _slice.Statements.Contains(declaration);

        private ITypeSymbol? TypeOf(ExpressionSyntax expression) => _scene.Model.GetTypeInfo(expression).Type;

        private bool IsFeature(ITypeSymbol? type) => _scene.IsFeature(type);

        private static bool IsDomainType(ITypeSymbol type) =>
            type.ContainingNamespace?.ToDisplayString().StartsWith("AvaloniaCadEditor.Domain", StringComparison.Ordinal) == true;

        private void Replace(TextSpan span, string text)
        {
            _changes.Add(new TextChange(span, text));
            _replaced.Add(span);
        }

        private bool IsInsideReplaced(TextSpan span) => _replaced.Any(r => r.Contains(span));
    }

    // ================================================================ O Build() da peça

    private static class BuildEditor
    {
        public sealed record Result(string? Source, string? Refusal);

        public static Result Insert(string source, string typeName, Insertion insertion, string root, PartMergeMode mode, string newLine)
        {
            source = ToBlockBody(source, typeName, newLine);
            var tree = CSharpSyntaxTree.ParseText(source, RoslynSceneEvaluator.ParseOptions);
            var record = tree.GetRoot().DescendantNodes().OfType<RecordDeclarationSyntax>().First(r => r.Identifier.ValueText == typeName);
            var build = record.Members.OfType<MethodDeclarationSyntax>().FirstOrDefault(m => m.Identifier.ValueText == "Build" && m.ParameterList.Parameters.Count == 0);
            if (build?.Body == null) return new Result(null, $"{typeName} não tem um Build() com corpo.");

            var body = build.Body;
            var returns = body.DescendantNodes(n => n is not (LocalFunctionStatementSyntax or AnonymousFunctionExpressionSyntax))
                .OfType<ReturnStatementSyntax>().ToList();
            if (returns.Count != 1 || body.Statements.LastOrDefault() != returns[0] || returns[0].Expression == null)
                return new Result(null, $"O Build() de {typeName} precisa terminar num único 'return ...;' para receber o sólido.");
            var ret = returns[0];
            var oldExpr = ret.Expression!;

            // Nomes: variáveis e Names do sólido não podem colidir com os do Build().
            var buildNames = body.DescendantNodes()
                .Select(n => n switch
                {
                    VariableDeclaratorSyntax v => v.Identifier.ValueText,
                    LocalFunctionStatementSyntax f => f.Identifier.ValueText,
                    SingleVariableDesignationSyntax d => d.Identifier.ValueText,
                    ForEachStatementSyntax e => e.Identifier.ValueText,
                    _ => null
                })
                .OfType<string>()
                .ToHashSet(StringComparer.Ordinal);
            var buildLiterals = body.DescendantTokens().Where(t => t.IsKind(SyntaxKind.StringLiteralToken)).Select(t => t.ValueText).ToHashSet(StringComparer.Ordinal);
            var clashes = insertion.DeclaredNames.Where(buildNames.Contains)
                .Concat(insertion.Literals.Where(buildLiterals.Contains))
                .Distinct().OrderBy(n => n, StringComparer.Ordinal).ToList();
            if (clashes.Count > 0)
                return new Result(null, $"{typeName} já usa {string.Join(", ", clashes.Select(c => $"'{c}'"))}; renomeie o sólido antes de incorporar.");

            var taken = new HashSet<string>(buildNames.Concat(buildLiterals).Concat(insertion.DeclaredNames).Concat(insertion.Literals), StringComparer.Ordinal);
            string indent = IndentOf(source, ret);
            string snake = SnakeCase(typeName);

            var changes = new List<TextChange>();
            StatementSyntax insertBefore = ret;
            var extra = new List<string>();
            string? newReturn = null;

            bool isCreate = TryCreateCall(oldExpr, out string? method, out var args);
            switch (mode)
            {
                case PartMergeMode.UnionBeforeCuts:
                {
                    if (!isCreate || method != "CreateDifference" || args.Count < 2 || args.Any(a => a.NameColon != null))
                        return new Result(null, $"\"Unir antes dos cortes\" exige que o Build() de {typeName} termine em 'return BooleanFeature.CreateDifference(nome, bases, ferramentas...)'.");

                    var bases = args[1].Expression;
                    if (AppendToCollection(bases, root, changes))
                    {
                        // a coleção está no próprio return
                    }
                    else if (bases is IdentifierNameSyntax baseName && FindLocalCollection(body, baseName.Identifier.ValueText) is { } local)
                    {
                        AppendToCollection(local.Collection, root, changes);
                        insertBefore = local.Statement; // o sólido precisa existir antes da coleção que o lista
                    }
                    else
                    {
                        changes.Add(new TextChange(bases.Span, $"new IFeature[] {{ {bases}, {root} }}"));
                    }
                    break;
                }
                case PartMergeMode.SubtractAsTool when isCreate && method == "CreateDifference":
                    extra.Add($"{root}.IsTransientTool = true;");
                    changes.Add(new TextChange(new TextSpan(((InvocationExpressionSyntax)oldExpr).ArgumentList.CloseParenToken.SpanStart, 0), $", {root}"));
                    break;
                default:
                {
                    string combine = mode == PartMergeMode.SubtractAsTool ? "CreateDifference" : "CreateUnion";
                    if (mode == PartMergeMode.SubtractAsTool) extra.Add($"{root}.IsTransientTool = true;");

                    if (isCreate && args.Count > 0 && args[0].Expression is LiteralExpressionSyntax literal && literal.IsKind(SyntaxKind.StringLiteralExpression)
                        && SyntaxFacts.IsValidIdentifier(literal.Token.ValueText + "_base"))
                    {
                        // O resultado antigo vira <nome>_base e o novo return fica com o nome antigo.
                        string name = literal.Token.ValueText;
                        string baseVar = Unique(name + "_base", taken);
                        string renamed = WithReplacement(oldExpr, literal.Span, $"\"{baseVar}\"");
                        newReturn = $"var {baseVar} = {renamed};{newLine}{indent}return BooleanFeature.{combine}(\"{name}\", {baseVar}, {root});";
                    }
                    else if (oldExpr is IdentifierNameSyntax identifier)
                    {
                        newReturn = $"return BooleanFeature.{combine}(\"{Unique(snake, taken)}\", {identifier}, {root});";
                    }
                    else
                    {
                        string baseVar = Unique(snake + "_base", taken);
                        newReturn = $"var {baseVar} = {oldExpr};{newLine}{indent}return BooleanFeature.{combine}(\"{Unique(snake, taken)}\", {baseVar}, {root});";
                    }
                    break;
                }
            }

            if (newReturn != null) changes.Add(new TextChange(ret.Span, newReturn));

            var block = new StringBuilder();
            foreach (var line in insertion.Lines.Concat(extra))
                block.Append(line.Length == 0 ? string.Empty : indent + line).Append(newLine);
            block.Append(newLine);
            changes.Add(new TextChange(new TextSpan(insertBefore.FullSpan.Start, 0), block.ToString()));

            return new Result(SourceText.From(source).WithChanges(changes.OrderBy(c => c.Span.Start)).ToString(), null);
        }

        /// <summary><c>Build() =&gt; expr;</c> vira <c>Build() { return expr; }</c>, para poder receber instruções.</summary>
        private static string ToBlockBody(string source, string typeName, string newLine)
        {
            var tree = CSharpSyntaxTree.ParseText(source, RoslynSceneEvaluator.ParseOptions);
            var record = tree.GetRoot().DescendantNodes().OfType<RecordDeclarationSyntax>().First(r => r.Identifier.ValueText == typeName);
            var build = record.Members.OfType<MethodDeclarationSyntax>().FirstOrDefault(m => m.Identifier.ValueText == "Build" && m.ParameterList.Parameters.Count == 0);
            if (build?.ExpressionBody == null) return source;

            string methodIndent = IndentOf(source, build);
            string inner = methodIndent + "    ";
            string expression = build.ExpressionBody.Expression.ToString();
            var span = TextSpan.FromBounds(build.ParameterList.Span.End, build.SemicolonToken.Span.End);
            string block = $"{newLine}{methodIndent}{{{newLine}{inner}return {expression};{newLine}{methodIndent}}}";
            return SourceText.From(source).WithChanges(new TextChange(span, block)).ToString();
        }

        private static bool TryCreateCall(ExpressionSyntax expression, out string? method, out SeparatedSyntaxList<ArgumentSyntax> args)
        {
            method = null;
            args = default;
            if (expression is not InvocationExpressionSyntax
                {
                    Expression: MemberAccessExpressionSyntax { Expression: IdentifierNameSyntax { Identifier.ValueText: "BooleanFeature" }, Name: var name },
                    ArgumentList: var argumentList
                } || name.Identifier.ValueText is not ("CreateUnion" or "CreateDifference" or "CreateIntersection"))
                return false;
            method = name.Identifier.ValueText;
            args = argumentList.Arguments;
            return true;
        }

        /// <summary>Acrescenta o sólido a um <c>new IFeature[] { ... }</c> / <c>new[] { ... }</c> / <c>[ ... ]</c>.</summary>
        private static bool AppendToCollection(ExpressionSyntax collection, string root, List<TextChange> changes)
        {
            InitializerExpressionSyntax? initializer = collection switch
            {
                ArrayCreationExpressionSyntax a => a.Initializer,
                ImplicitArrayCreationExpressionSyntax i => i.Initializer,
                _ => null
            };
            if (initializer != null)
            {
                string prefix = initializer.Expressions.Count > 0 ? ", " : " ";
                int at = initializer.Expressions.Count > 0 ? initializer.Expressions.Last().Span.End : initializer.OpenBraceToken.Span.End;
                changes.Add(new TextChange(new TextSpan(at, 0), prefix + root));
                return true;
            }
            if (collection is CollectionExpressionSyntax list)
            {
                int at = list.Elements.Count > 0 ? list.Elements.Last().Span.End : list.OpenBracketToken.Span.End;
                changes.Add(new TextChange(new TextSpan(at, 0), (list.Elements.Count > 0 ? ", " : string.Empty) + root));
                return true;
            }
            return false;
        }

        private static (StatementSyntax Statement, ExpressionSyntax Collection)? FindLocalCollection(BlockSyntax body, string name)
        {
            foreach (var statement in body.Statements.OfType<LocalDeclarationStatementSyntax>())
            {
                var declarator = statement.Declaration.Variables.FirstOrDefault(v => v.Identifier.ValueText == name);
                if (declarator?.Initializer?.Value is ArrayCreationExpressionSyntax or ImplicitArrayCreationExpressionSyntax or CollectionExpressionSyntax)
                    return (statement, declarator.Initializer.Value);
            }
            return null;
        }

        private static string WithReplacement(SyntaxNode node, TextSpan span, string text) =>
            SourceText.From(node.ToString())
                .WithChanges(new TextChange(new TextSpan(span.Start - node.SpanStart, span.Length), text))
                .ToString();

        private static string IndentOf(string source, SyntaxNode node)
        {
            var text = SourceText.From(source);
            var line = text.Lines.GetLineFromPosition(node.SpanStart);
            return new string(text.ToString(line.Span).TakeWhile(c => c is ' ' or '\t').ToArray());
        }

        private static string Unique(string name, HashSet<string> taken)
        {
            string candidate = name;
            for (int i = 2; taken.Contains(candidate); i++) candidate = name + "_" + i;
            taken.Add(candidate);
            return candidate;
        }
    }
}
