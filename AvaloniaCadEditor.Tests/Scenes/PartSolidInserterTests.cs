using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AvaloniaCadEditor.Application.EditContext;
using AvaloniaCadEditor.Application.Interfaces;
using AvaloniaCadEditor.Application.Services;
using AvaloniaCadEditor.Domain.Features;
using AvaloniaCadEditor.Domain.Mesh;
using AvaloniaCadEditor.Domain.Profiles;
using AvaloniaCadEditor.Domain.ValueObjects;
using AvaloniaCadEditor.Infrastructure.Scripting;
using FluentAssertions;
using Xunit;

namespace AvaloniaCadEditor.Tests.Scenes;

/// <summary>
/// Extrudar dentro de uma peça aberta (duplo clique): o sólido que o gerador faz no MUNDO, em modo
/// autônomo, vai direto para o Build() da peça em coordenadas LOCAIS, e a cena não muda. As faces são
/// montadas numa malha, como as que o clique numa peça produz (FaceProfile), para passar pelo mesmo
/// caminho do app: FaceProfile → FeatureCodeGenerator → PartSolidInserter.
/// </summary>
public class PartSolidInserterTests
{
    private const string SceneFile = ScriptDocument.DefaultSceneFile;
    private const string PartFile = "Pecas/Bloco.cs";

    /// <summary>Bloco 40 × 30 × 10 com a origem num canto e um furo passante.</summary>
    private const string BlocoPart = """
        public sealed record Bloco(double Largura = 40) : Part
        {
            protected override IFeature Build()
            {
                var corpo = new BoxFeature(Point3D.Origin, Largura, 30, 10) { Name = "corpo" };
                var furo = new CylinderFeature(new Point3D(10, 15, -1), Vector3D.UnitZ, 2, 12) { Name = "furo", IsTransientTool = true };
                return BooleanFeature.CreateDifference("bloco", corpo, furo);
            }
        }
        """;

    /// <summary>
    /// Girado 90° em Z e erguido: o ponto local (x, y, z) vai para o mundo em (100 − y, 50 + x, 20 + z).
    /// </summary>
    private const string BlocoScene = """
        var bloco = Add(new Bloco(), at: Placement.At(100, 50, 20, rotationZDegrees: 90), name: "bloco");
        """;

    private static readonly Placement BlocoPlacement = Placement.At(100, 50, 20, rotationZDegrees: 90);

    private static ScriptDocument[] Docs(string scene = BlocoScene, string part = BlocoPart) =>
        new[] { new ScriptDocument(SceneFile, scene), new ScriptDocument(PartFile, part) };

    private static async Task<PartEditContext> ContextFor(IReadOnlyList<ScriptDocument> documents, string instance = "bloco")
    {
        var result = await new RoslynSceneEvaluator().EvaluateAsync(documents);
        result.Success.Should().BeTrue(result.ErrorMessage + string.Join("\n", result.Diagnostics));
        var context = PartEditContext.For(result.Features.Single(f => f.Name == instance), result.Features);
        context.Should().NotBeNull();
        return context!;
    }

    private static Task<PartSolidInsertion> Insert(
        IReadOnlyList<ScriptDocument> documents, string code, PartEditContext context, PartMergeMode mode = PartMergeMode.UnionAfterCuts) =>
        new PartSolidInserter(new RoslynSceneEvaluator(), new RoslynPartMerger())
            .InsertAsync(documents, SceneFile, code, context, mode);

    /// <summary>Executa a cena com a mudança e devolve a definição local da instância.</summary>
    private static async Task<IFeature> DefinitionAfter(IReadOnlyList<ScriptDocument> documents, PartSolidInsertion insertion, string instance = "bloco")
    {
        var applied = documents.Select(d => insertion.Changes.FirstOrDefault(c => c.FilePath == d.FilePath) ?? d).ToList();
        var result = await new RoslynSceneEvaluator().EvaluateAsync(applied);
        result.Success.Should().BeTrue(result.ErrorMessage + string.Join("\n", result.Diagnostics));
        return result.Features.OfType<PartInstance>().Single(i => i.Name == instance).Definition;
    }

    private static IFeature? FindByName(IFeature feature, string name) => feature.Name == name ? feature : feature switch
    {
        BooleanFeature b => b.BaseFeatures.Concat(b.ToolFeatures).Select(f => FindByName(f, name)).FirstOrDefault(f => f != null),
        TransformedFeature t => FindByName(t.Source, name),
        FilletFeature f => FindByName(f.BaseFeature, name),
        _ => null
    };

    /// <summary>Uma face plana (polígono convexo, em ordem anti-horária vista pela normal) como malha.</summary>
    private static TriangleMesh FaceMesh(Vector3D normal, params Point3D[] polygon)
    {
        var positions = polygon.SelectMany(p => new[] { (float)p.X, (float)p.Y, (float)p.Z }).ToArray();
        var normals = polygon.SelectMany(_ => new[] { (float)normal.X, (float)normal.Y, (float)normal.Z }).ToArray();
        var indices = new List<uint>();
        for (uint i = 1; i + 1 < polygon.Length; i++) indices.AddRange(new[] { 0u, i, i + 1 });
        return new TriangleMesh(positions, normals, indices.ToArray());
    }

    /// <summary>Um disco (leque em torno do centro) como malha: a boca de um ressalto ou de um furo.</summary>
    private static TriangleMesh DiskMesh(Point3D center, double radius, int segments = 32)
    {
        var points = new List<Point3D> { center };
        for (int i = 0; i < segments; i++)
        {
            double a = 2 * Math.PI * i / segments;
            points.Add(new Point3D(center.X + radius * Math.Cos(a), center.Y + radius * Math.Sin(a), center.Z));
        }
        var positions = points.SelectMany(p => new[] { (float)p.X, (float)p.Y, (float)p.Z }).ToArray();
        var normals = points.SelectMany(_ => new[] { 0f, 0f, 1f }).ToArray();
        var indices = new List<uint>();
        for (uint i = 1; i <= segments; i++) indices.AddRange(new[] { 0u, i, i == segments ? 1u : i + 1 });
        return new TriangleMesh(positions, normals, indices.ToArray());
    }

    /// <summary>
    /// O quadrado local x ∈ [20, 30], y ∈ [5, 15] no topo (z = 10) do bloco, como o clique o vê no mundo:
    /// x ∈ [85, 95], y ∈ [70, 80], z = 30.
    /// </summary>
    private static FaceProfile TopSquare(IFeature owner)
    {
        var mesh = FaceMesh(Vector3D.UnitZ,
            new Point3D(85, 70, 30), new Point3D(95, 70, 30), new Point3D(95, 80, 30), new Point3D(85, 80, 30));
        return new FaceProfile(owner, new WorkPlane(Vector3D.UnitZ, new Point3D(90, 75, 30)), mesh);
    }

    private static void ShouldSpanLocally(IFeature solid, (double Min, double Max) x, (double Min, double Max) y, (double Min, double Max) z)
    {
        var vertices = solid.GetVertices();
        vertices.Min(v => v.X).Should().BeApproximately(x.Min, 1e-4);
        vertices.Max(v => v.X).Should().BeApproximately(x.Max, 1e-4);
        vertices.Min(v => v.Y).Should().BeApproximately(y.Min, 1e-4);
        vertices.Max(v => v.Y).Should().BeApproximately(y.Max, 1e-4);
        vertices.Min(v => v.Z).Should().BeApproximately(z.Min, 1e-4);
        vertices.Max(v => v.Z).Should().BeApproximately(z.Max, 1e-4);
    }

    // ---------------------------------------------------------------- Unir (altura positiva)

    [Fact]
    public async Task FaceRetangular_PecaGirada_GravaACaixaNoBuildEmCoordenadasLocais()
    {
        var documents = Docs();
        var context = await ContextFor(documents);
        string code = FeatureCodeGenerator.GenerateExtrusionCode(TopSquare(new BoxFeature(Point3D.Origin, 1, 1, 1)), 5, null, ExtrusionTarget.Standalone);

        var insertion = await Insert(documents, code, context);

        insertion.Refusal.Should().BeNull();
        insertion.GeometryVerified.Should().BeTrue("caixa é comparada pelos vértices");
        insertion.Changes.Should().ContainSingle().Which.FilePath.Should().Be(PartFile, "a cena não muda");
        insertion.Changes[0].Text.Should().Contain($"var {insertion.InsertedName}").And.NotContain("Push/Pull");

        var definition = await DefinitionAfter(documents, insertion);
        ShouldSpanLocally(FindByName(definition, insertion.InsertedName!)!, (20, 30), (5, 15), (10, 15));
    }

    [Fact]
    public async Task PecaSoTransladada_CoordenadasSoPerdemATranslacao()
    {
        const string scene = """var bloco = Add(new Bloco(), at: Placement.At(100, 50, 20), name: "bloco");""";
        var documents = Docs(scene);
        var context = await ContextFor(documents);
        // Local x ∈ [20, 30], y ∈ [5, 15], topo z = 10 → mundo x ∈ [120, 130], y ∈ [55, 65], z = 30.
        var mesh = FaceMesh(Vector3D.UnitZ,
            new Point3D(120, 55, 30), new Point3D(130, 55, 30), new Point3D(130, 65, 30), new Point3D(120, 65, 30));
        var face = new FaceProfile(new BoxFeature(Point3D.Origin, 1, 1, 1), new WorkPlane(Vector3D.UnitZ, new Point3D(125, 60, 30)), mesh);

        var insertion = await Insert(documents, FeatureCodeGenerator.GenerateExtrusionCode(face, 5, null, ExtrusionTarget.Standalone), context);

        insertion.Refusal.Should().BeNull();
        insertion.Changes.Single().Text.Should().Contain("new Point3D(20, 5, 10)");
        var definition = await DefinitionAfter(documents, insertion);
        ShouldSpanLocally(FindByName(definition, insertion.InsertedName!)!, (20, 30), (5, 15), (10, 15));
    }

    // ---------------------------------------------------------------- Cortar (altura negativa)

    [Fact]
    public async Task AlturaNegativa_EntraComoFerramentaDaDiferenca()
    {
        var documents = Docs();
        var context = await ContextFor(documents);
        string code = FeatureCodeGenerator.GenerateExtrusionCode(TopSquare(new BoxFeature(Point3D.Origin, 1, 1, 1)), -4, null, ExtrusionTarget.Standalone);

        var insertion = await Insert(documents, code, context, PartMergeMode.SubtractAsTool);

        insertion.Refusal.Should().BeNull();
        string part = insertion.Changes.Single().Text;
        part.Should().Contain($"{insertion.InsertedName}.IsTransientTool = true;")
            .And.Contain($"BooleanFeature.CreateDifference(\"bloco\", corpo, furo, {insertion.InsertedName});");

        var definition = await DefinitionAfter(documents, insertion);
        ShouldSpanLocally(FindByName(definition, insertion.InsertedName!)!, (20, 30), (5, 15), (6, 10));
    }

    // ---------------------------------------------------------------- Face circular

    [Fact]
    public async Task FaceCircular_ViraSoUmCilindroLocal_SemLerAVariavelDaPeca()
    {
        var documents = Docs();
        var context = await ContextFor(documents);
        // Centro local (25, 10, 10) → mundo (90, 75, 30).
        var face = new FaceProfile(new BoxFeature(Point3D.Origin, 1, 1, 1),
            new WorkPlane(Vector3D.UnitZ, new Point3D(90, 75, 30)), DiskMesh(new Point3D(90, 75, 30), 3));

        string code = FeatureCodeGenerator.GenerateExtrusionCode(face, 6, null, ExtrusionTarget.Standalone);
        code.Should().Contain("new CylinderFeature(").And.NotContain("Push/Pull").And.NotContain("BooleanFeature");

        var insertion = await Insert(documents, code, context);

        insertion.Refusal.Should().BeNull();
        insertion.GeometryVerified.Should().BeTrue();
        var cylinder = FindByName(await DefinitionAfter(documents, insertion), insertion.InsertedName!).Should().BeOfType<CylinderFeature>().Subject;
        cylinder.GlobalBaseCenter.X.Should().BeApproximately(25, 1e-4);
        cylinder.GlobalBaseCenter.Y.Should().BeApproximately(10, 1e-4);
        cylinder.GlobalBaseCenter.Z.Should().BeApproximately(10, 1e-4);
        cylinder.Radius.Should().BeApproximately(3, 1e-4);
    }

    [Fact]
    public void FaceCircular_ModoCena_ContinuaComPushPull()
    {
        var owner = new BoxFeature(Point3D.Origin, 1, 1, 1) { Name = "bloco" };
        var face = new FaceProfile(owner, new WorkPlane(Vector3D.UnitZ, new Point3D(90, 75, 30)), DiskMesh(new Point3D(90, 75, 30), 3));

        FeatureCodeGenerator.GenerateExtrusionCode(face, 6, null).Should().Contain("Push/Pull", "o modo da cena não mudou");
    }

    // ---------------------------------------------------------------- Perfil desenhado num plano inclinado

    [Fact]
    public async Task PrismaNumPlanoInclinado_EntraComOPlanoReescrito()
    {
        var documents = Docs();
        var context = await ContextFor(documents);
        var plane = new WorkPlane(new Point3D(90, 75, 30), new Vector3D(0, -1, 1).Normalize(), Vector3D.UnitX);
        var triangle = new PolygonProfile(new[] { new Point3D(0, 0, 0), new Point3D(6, 0, 0), new Point3D(0, 4, 0) }, plane: plane);

        string code = FeatureCodeGenerator.GenerateExtrusionCode(triangle, 3, null, ExtrusionTarget.Standalone);
        var insertion = await Insert(documents, code, context);

        insertion.Refusal.Should().BeNull();
        insertion.GeometryVerified.Should().BeTrue("prisma é comparado pelos vértices");
        insertion.Changes.Single().Text.Should().Contain("new WorkPlane(");
    }

    // ---------------------------------------------------------------- Recusas e avisos

    [Fact]
    public async Task PecaDeclaradaNaCena_RecusaSemMudarNada()
    {
        var documents = new[] { new ScriptDocument(SceneFile, BlocoScene + "\n" + BlocoPart) };
        var context = await ContextFor(documents);
        string code = FeatureCodeGenerator.GenerateExtrusionCode(TopSquare(new BoxFeature(Point3D.Origin, 1, 1, 1)), 5, null, ExtrusionTarget.Standalone);

        var insertion = await Insert(documents, code, context);

        insertion.Refusal.Should().Contain("arquivo próprio");
        insertion.Changes.Should().BeEmpty();
    }

    [Fact]
    public async Task DuasInstancias_AvisaQueAsDuasMudam()
    {
        const string scene = BlocoScene + "\n" + """var bloco_2 = Add(new Bloco(), at: Placement.At(-100, 0, 0), name: "bloco_2");""";
        var documents = Docs(scene);
        var context = await ContextFor(documents);
        context.InstanceCount.Should().Be(2);
        string code = FeatureCodeGenerator.GenerateExtrusionCode(TopSquare(new BoxFeature(Point3D.Origin, 1, 1, 1)), 5, null, ExtrusionTarget.Standalone);

        var insertion = await Insert(documents, code, context);

        insertion.Refusal.Should().BeNull();
        insertion.Warnings.Should().Contain(w => w.Contains("bloco_2"));
        insertion.Warnings.Should().NotContain(w => w.Contains("diferente"), "as duas têm as mesmas medidas");
    }

    [Fact]
    public async Task DuasInstanciasComMedidasDiferentes_AvisaQueOSolidoNaoAcompanha()
    {
        const string scene = BlocoScene + "\n" + """var bloco_2 = Add(new Bloco(Largura: 60), at: Placement.At(-100, 0, 0), name: "bloco_2");""";
        var documents = Docs(scene);
        var context = await ContextFor(documents);
        context.DifferingParameters.Should().Equal(new[] { "Largura" }, "o letreiro avisa enquanto a peça está aberta");
        string code = FeatureCodeGenerator.GenerateExtrusionCode(TopSquare(new BoxFeature(Point3D.Origin, 1, 1, 1)), 5, null, ExtrusionTarget.Standalone);

        var insertion = await Insert(documents, code, context);

        insertion.Refusal.Should().BeNull();
        insertion.Warnings.Should().Contain(w => w.Contains("Largura diferente") && w.Contains("(bloco)"));
    }

    [Fact]
    public async Task InstanciaQueSumiu_Recusa()
    {
        var documents = Docs();
        var context = await ContextFor(documents) with { InstanceName = "outra" };
        string code = FeatureCodeGenerator.GenerateExtrusionCode(TopSquare(new BoxFeature(Point3D.Origin, 1, 1, 1)), 5, null, ExtrusionTarget.Standalone);

        (await Insert(documents, code, context)).Refusal.Should().Contain("não está mais na cena");
    }

    [Fact]
    public void Contexto_LevaOMundoParaOLocalDaPeca()
    {
        var context = new PartEditContext("bloco", "Bloco", BlocoPlacement, 1);

        var local = context.ToLocal(new Point3D(95, 70, 30));

        local.X.Should().BeApproximately(20, 1e-9);
        local.Y.Should().BeApproximately(5, 1e-9);
        local.Z.Should().BeApproximately(10, 1e-9);
    }
}
