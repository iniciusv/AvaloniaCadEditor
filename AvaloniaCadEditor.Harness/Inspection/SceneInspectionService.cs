using System.Text;
using AvaloniaCadEditor.Application.Interfaces;
using AvaloniaCadEditor.Application.Isolation;
using AvaloniaCadEditor.Domain.Entities;
using AvaloniaCadEditor.Domain.Features;
using AvaloniaCadEditor.Domain.Mesh;
using AvaloniaCadEditor.Domain.ValueObjects;
using AvaloniaCadEditor.Drafting;
using AvaloniaCadEditor.Harness.Views;
using static AvaloniaCadEditor.Harness.Views.TextFormat;

namespace AvaloniaCadEditor.Harness.Inspection;

/// <summary>
/// Uma feature de topo pronta para ser inspecionada: a malha no mundo e no referencial dela, e
/// onde fica o (0, 0, 0) do código nesse referencial.
/// </summary>
/// <param name="FrameIsWorld">Sem referencial próprio (peça girada fora de Z, ou feature sem posicionamento): vale o do mundo.</param>
public sealed record InspectionTarget(
    IFeature Feature,
    PartFrame Frame,
    bool FrameIsWorld,
    TriangleMesh WorldMesh,
    TriangleMesh FrameMesh,
    string? PartType,
    Point3D? CodeOrigin);

/// <summary>Opções da vista em texto.</summary>
public sealed record ViewTextOptions
{
    public bool IncludeHidden { get; init; } = true;
    public int MaxChars { get; init; } = 6000;
    public HiddenLineOptions HiddenLines { get; init; } = new();
}

/// <summary>
/// O que a IA consegue "ver" de uma cena já avaliada: a vista em arame em texto, as cotas e a caixa
/// de cada feature de topo. Faz o mesmo caminho do isolamento (tecla H) e da folha de cotas da UI —
/// malha pelo kernel, referencial pelo <see cref="PartFrameResolver"/>, cotas pelo
/// <see cref="DraftingAnalyzer"/> — sem depender da tela. O único estado é o cache, por feature.
/// </summary>
public sealed class SceneInspectionService
{
    private readonly IGeometryKernel _kernel;
    private readonly Dictionary<Guid, InspectionTarget> _targets = new();
    private readonly Dictionary<(Guid, StandardView, bool, int), string> _views = new();

    /// <param name="kernel">Um kernel só das ferramentas: invalidar o cache dele não mexe no da UI.</param>
    public SceneInspectionService(IGeometryKernel kernel)
    {
        _kernel = kernel ?? throw new ArgumentNullException(nameof(kernel));
    }

    /// <summary>Esquece tudo (a cena foi reavaliada: as features são outras).</summary>
    public void Clear()
    {
        _targets.Clear();
        _views.Clear();
        _kernel.InvalidateAll();
    }

    public InspectionTarget Prepare(IFeature feature)
    {
        if (_targets.TryGetValue(feature.Id, out var cached)) return cached;

        var world = _kernel.BuildFeatureMesh(feature);
        var frame = PartFrameResolver.Resolve(feature, world.Positions);
        bool isWorld = frame == null;
        frame ??= new PartFrame(feature.Name, Placement.Identity);

        var rootToWorld = feature switch
        {
            PartInstance instance => instance.Placement,
            TransformedFeature transformed => transformed.Placement,
            _ => null
        };
        Point3D? codeOrigin = rootToWorld != null ? frame.ToFrame(rootToWorld.Apply(Point3D.Origin))
            : isWorld ? Point3D.Origin : null;

        var target = new InspectionTarget(feature, frame, isWorld, world, world.Transform(frame.WorldToFrame),
            (feature as PartInstance)?.PartTypeName, codeOrigin);
        _targets[feature.Id] = target;
        return target;
    }

    /// <summary>A vista em arame do alvo, em texto (ver <see cref="WireframeReport"/>).</summary>
    public string DescribeView(InspectionTarget target, StandardView view, ViewTextOptions? options = null)
    {
        options ??= new ViewTextOptions();
        var key = (target.Feature.Id, view, options.IncludeHidden, options.MaxChars);
        if (_views.TryGetValue(key, out var cached)) return cached;

        var basis = ProjectionBasis.For(view);
        var lines = HiddenLineProjector.Project(target.FrameMesh, basis, options.HiddenLines);
        var circles = Sheet(target, view)?.Entities.OfType<ViewCircle>().ToList() ?? new List<ViewCircle>();
        var header = new WireframeHeader(target.Feature.Name, target.PartType, target.FrameIsWorld, target.CodeOrigin);

        string text = WireframeReport.Format(lines, basis, header, circles, options.IncludeHidden, options.MaxChars);
        _views[key] = text;
        return text;
    }

    /// <summary>
    /// As cotas automáticas da vista, no mesmo texto do "Copiar" da folha de cotas. Sem as cotas
    /// manuais nem as escondidas pelo usuário (são estado da tela) e sem as dicas de parâmetro.
    /// </summary>
    public string DescribeDimensions(InspectionTarget target, StandardView view)
    {
        if (view == StandardView.Isometric)
            return "A vista Isometric não tem cotas. Use Front, Back, Top, Bottom, Right ou Left.";

        var sheet = Sheet(target, view);
        if (sheet == null) return $"Não foi possível montar a folha de cotas de '{target.Feature.Name}' na vista {view} (a peça não tem malha).";

        return DraftingReport.Format(sheet, sheet.Dimensions, target.Frame.OwnerName, target.PartType, codeOrigin: target.CodeOrigin);
    }

    /// <summary>A caixa do alvo no referencial dele e no mundo.</summary>
    public string DescribeBoundingBox(InspectionTarget target)
    {
        var (fMin, fMax) = Bounds(target.FrameMesh);
        var (wMin, wMax) = Bounds(target.WorldMesh);
        var sb = new StringBuilder();
        sb.AppendLine($"# Caixa — {target.Feature.Name}{(target.PartType != null ? $" ({target.PartType})" : "")}");
        if (!target.FrameIsWorld)
            sb.AppendLine($"- referencial da peça: min {P(fMin)} max {P(fMax)} · tamanho {P(Size(fMin, fMax))}");
        sb.AppendLine($"- mundo: min {P(wMin)} max {P(wMax)} · tamanho {P(Size(wMin, wMax))}");
        if (target.CodeOrigin is { } o && !target.FrameIsWorld)
            sb.AppendLine($"- origem do código (0, 0, 0) no referencial da peça: {P(o)}");
        return sb.ToString();
    }

    /// <summary>As features de topo com tipo e caixa no mundo — o "índice" do que dá para inspecionar.</summary>
    public string ListTargets(IReadOnlyList<IFeature> features)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Features de topo — {features.Count}");
        foreach (var feature in features)
        {
            var target = Prepare(feature);
            var (min, max) = Bounds(target.WorldMesh);
            string type = target.PartType != null ? $" ({target.PartType})" : $" ({feature.GetType().Name.Replace("Feature", "")})";
            string tool = feature.IsTransientTool ? " · ferramenta (vazio)" : "";
            string frame = target.FrameIsWorld ? "" : " · tem referencial próprio";
            string box = target.WorldMesh.TriangleCount == 0 ? "sem malha" : $"mundo min {P(min)} max {P(max)}";
            sb.AppendLine($"- {feature.Name}{type}: {box}{frame}{tool}");
        }
        return sb.ToString();
    }

    private DraftingSheet? Sheet(InspectionTarget target, StandardView view)
    {
        if (view == StandardView.Isometric) return null;
        var input = DraftingInput.ForIsolated(target.Feature, target.Frame, target.WorldMesh.Positions, view);
        return input == null ? null : DraftingAnalyzer.Analyze(input);
    }

    private static (Point3D Min, Point3D Max) Bounds(TriangleMesh mesh)
    {
        var p = mesh.Positions;
        if (p.Length < 3) return (Point3D.Origin, Point3D.Origin);
        double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue, maxZ = double.MinValue;
        for (int i = 0; i + 2 < p.Length; i += 3)
        {
            minX = Math.Min(minX, p[i]); minY = Math.Min(minY, p[i + 1]); minZ = Math.Min(minZ, p[i + 2]);
            maxX = Math.Max(maxX, p[i]); maxY = Math.Max(maxY, p[i + 1]); maxZ = Math.Max(maxZ, p[i + 2]);
        }
        return (new Point3D(minX, minY, minZ), new Point3D(maxX, maxY, maxZ));
    }

    private static Point3D Size(Point3D min, Point3D max) => new(max.X - min.X, max.Y - min.Y, max.Z - min.Z);

    private static string P(Point3D p) => $"({N(p.X)}, {N(p.Y)}, {N(p.Z)})";
}
