using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AvaloniaCadEditor.Application.Commands;
using AvaloniaCadEditor.Application.EditContext;
using AvaloniaCadEditor.Application.Interfaces;
using AvaloniaCadEditor.Application.Services;
using AvaloniaCadEditor.Domain.Features;
using AvaloniaCadEditor.Domain.Profiles;
using AvaloniaCadEditor.Domain.ValueObjects;
using AvaloniaCadEditor.Infrastructure.OpenCascade;
using AvaloniaCadEditor.UI.Controls.Rendering;
using AvaloniaCadEditor.UI.ViewModels;
using FluentAssertions;
using Moq;
using Xunit;

namespace AvaloniaCadEditor.Tests.Scenes;

/// <summary>
/// Entidade aberta com duplo clique, pelas ViewModels: o nível decide o que se seleciona (sólidos na cena,
/// faces e vazios dentro), o resto do modelo fica à vista mas esmaecido, o H isola a entidade aberta, e o
/// Extrude numa face da peça aberta grava no Build() dela.
/// </summary>
public class OpenEntityTests
{
    private sealed record PecaDeTeste : Part
    {
        protected override IFeature Build() => throw new NotSupportedException();
    }

    private static CadViewModel CreateViewModel() =>
        TestUtils.CreateCadViewModel(
            new Mock<AvaloniaCadEditor.Domain.Interfaces.IGeometryEngine>().Object,
            new Mock<IOSnapService>().Object,
            null,
            new OpenCascadeGeometryKernel());

    /// <summary>
    /// Bloco 20 × 20 × 10 com um furo passante no centro, instanciado em x = 100 ("bloco_1"), e uma tampa
    /// solta por cima dele ("tampa", z de 20 a 25).
    /// </summary>
    private static (CadViewModel Vm, PartInstance Bloco, BoxFeature Tampa) Scene()
    {
        var corpo = new BoxFeature(Point3D.Origin, 20, 20, 10) { Name = "corpo" };
        var furo = new CylinderFeature(new Point3D(10, 10, 0), Vector3D.UnitZ, 3, 10) { Name = "furo", IsTransientTool = true };
        var definicao = BooleanFeature.CreateDifference("bloco", corpo, furo);
        var bloco = new PartInstance<PecaDeTeste>(new PecaDeTeste(), definicao, Placement.At(100, 0, 0)) { Name = "bloco_1" };
        var tampa = new BoxFeature(new Point3D(95, -5, 20), 30, 30, 5) { Name = "tampa" };

        var vm = CreateViewModel();
        vm.Sketch.AddFeature(bloco);
        vm.Sketch.AddFeature(tampa);
        return (vm, bloco, tampa);
    }

    /// <summary>Raio de cima para baixo num ponto do bloco fora do furo: passa pela tampa antes.</summary>
    private static Ray RayDown(float x = 105, float y = 5) =>
        new(new System.Numerics.Vector3(x, y, 100), new System.Numerics.Vector3(0, 0, -1));

    // ---------------------------------------------------------------- Nível

    [Fact]
    public void ONivelDecideOQueSeSeleciona()
    {
        var (vm, _, _) = Scene();
        vm.CurrentSelectionMode.Should().Be(CadViewModel.TargetSelectionMode.Feature);
        vm.SelectionLevelText.Should().Be("Cena: sólidos inteiros");

        vm.ActiveBooleanGroupName = "bloco_1";
        vm.SelectionLevel.Should().Be(SelectionLevel.Entity);
        vm.CurrentSelectionMode.Should().Be(CadViewModel.TargetSelectionMode.Face);
        vm.SelectionLevelText.Should().Contain("bloco_1");

        vm.CurrentMode = EditorMode.SketchEdit;
        vm.CurrentSelectionMode.Should().Be(CadViewModel.TargetSelectionMode.Edge, "o esboço ganha da entidade aberta");
        vm.SolidSelectionMode.Should().Be(CadViewModel.TargetSelectionMode.Face,
            "por baixo das linhas, o clique nos sólidos continua pegando as faces da entidade aberta");
    }

    // ---------------------------------------------------------------- Esboço por cima do nível

    /// <summary>A face de cima do bloco aberto, como o clique a selecionaria.</summary>
    private static IProfile SelectTopFace(CadViewModel vm)
    {
        var hit = vm.Raycast3D(RayDown());
        hit.Should().NotBeNull();
        var owner = vm.Sketch.Features.Single(f => f.Id == hit!.Value.FeatureId);
        var face = new FaceProfile(owner, new WorkPlane(Vector3D.UnitZ, new Point3D(100, 0, 10)), vm.GetFeatureMesh(owner.Id)!);
        vm.SelectionService.SelectProfile(face);
        return face;
    }

    [Fact]
    public void FaceSelecionada_L_Esc_Esc_VoltaParaAEntidadePegandoFaces()
    {
        var (vm, _, _) = Scene();
        vm.ActiveBooleanGroupName = "bloco_1";
        var face = SelectTopFace(vm);

        vm.OnKeyDown("L");
        vm.IsSketchEditing.Should().BeTrue();
        vm.ActiveWorkPlane.Should().Be(face.Plane, "o esboço é no plano da face");

        vm.OnKeyDown("Escape"); // larga a linha, fica no esboço
        vm.IsSketchEditing.Should().BeTrue();
        vm.CurrentTool.Should().BeOfType<SelectTool>();

        vm.OnKeyDown("Escape"); // sai do esboço (a face serviu para o plano e já não está marcada)

        vm.IsSketchEditing.Should().BeFalse("o Esc sai um nível, do esboço para a entidade aberta");
        vm.IsEditingBooleanGroup.Should().BeTrue("a entidade continua aberta: sair dela é o próximo Esc");
        vm.SelectionLevel.Should().Be(SelectionLevel.Entity);
        vm.CurrentSelectionMode.Should().Be(CadViewModel.TargetSelectionMode.Face);

        vm.OnKeyDown("Escape");
        vm.IsEditingBooleanGroup.Should().BeFalse("o Esc seguinte sai da entidade");
    }

    [Fact]
    public void NoEsboco_AFaceDaEntidadeAbertaContinuaAlcancavel()
    {
        var (vm, bloco, _) = Scene();
        vm.ActiveBooleanGroupName = "bloco_1";
        SelectTopFace(vm);
        vm.OnKeyDown("L");
        vm.OnKeyDown("Escape");

        vm.IsSketchEditing.Should().BeTrue();
        vm.Raycast3D(RayDown())!.Value.FeatureId.Should().Be(bloco.Id, "o raio de seleção não muda no esboço");
        vm.SolidSelectionMode.Should().Be(CadViewModel.TargetSelectionMode.Face);
    }

    [Fact]
    public void EntrarEmOutraEntidade_SaiDoEsboco()
    {
        var (vm, _, _) = Scene();
        vm.ActiveBooleanGroupName = "bloco_1";
        SelectTopFace(vm);
        vm.OnKeyDown("L");
        vm.IsSketchEditing.Should().BeTrue();

        vm.ActiveBooleanGroupName = "tampa";

        vm.IsSketchEditing.Should().BeFalse("trocar de entidade é trocar de nível");
        vm.SelectionLevel.Should().Be(SelectionLevel.Entity);
        vm.CurrentSelectionMode.Should().Be(CadViewModel.TargetSelectionMode.Face);
        vm.CurrentTool.Should().BeOfType<SelectTool>();
    }

    [Fact]
    public void NaCena_L_Esc_Esc_VoltaASelecionarSolidos()
    {
        var (vm, _, _) = Scene();

        vm.OnKeyDown("L");
        vm.OnKeyDown("Escape");
        vm.OnKeyDown("Escape");

        vm.IsSketchEditing.Should().BeFalse();
        vm.CurrentSelectionMode.Should().Be(CadViewModel.TargetSelectionMode.Feature);
    }

    [Fact]
    public void NaCena_OCliquePegaOSolidoDaFrente()
    {
        var (vm, _, tampa) = Scene();

        vm.Raycast3D(RayDown())!.Value.FeatureId.Should().Be(tampa.Id);
    }

    [Fact]
    public void DentroDaPeca_OResto_NaoESelecionavel_MasContinuaComoReferencia()
    {
        var (vm, bloco, tampa) = Scene();
        vm.ActiveBooleanGroupName = "bloco_1";

        vm.Raycast3D(RayDown())!.Value.FeatureId.Should().Be(bloco.Id, "a tampa na frente é de fora da entidade aberta");
        vm.RaycastReference(RayDown())!.Value.FeatureId.Should().Be(tampa.Id, "referência (plano de trabalho, duplo clique) vê a cena toda");
    }

    [Fact]
    public void DentroDaPeca_OVazioESelecionavel_MasOExtrudeSoPegaOCorpo()
    {
        var (vm, _, _) = Scene();
        vm.ActiveBooleanGroupName = "bloco_1";
        var furo = vm.ActiveToolFeatures.Should().ContainSingle().Subject;

        // Pelo centro do furo: o vazio é atingido; o corpo não (o furo é passante).
        vm.Raycast3D(RayDown(110, 10))!.Value.FeatureId.Should().Be(furo.Id);
        vm.IsOpenEntityToolHit(vm.Raycast3D(RayDown(110, 10))).Should().BeTrue();

        vm.CurrentTool = new ExtrudeCommand();
        vm.Raycast3D(RayDown(110, 10)).Should().BeNull("o Extrude quer uma face do corpo, não o vazio");
    }

    [Fact]
    public void EntrarNumaEntidade_LimpaASelecaoDoNivelAnterior()
    {
        var (vm, _, tampa) = Scene();
        vm.SelectionService.SelectById(vm.Sketch, tampa.Id);
        tampa.IsSelected.Should().BeTrue();

        vm.ActiveBooleanGroupName = "bloco_1";

        tampa.IsSelected.Should().BeFalse();
    }

    [Fact]
    public void ComAEntidadeAberta_ORestoFicaEsmaecido()
    {
        var (vm, bloco, tampa) = Scene();
        var tampaItem = new SolidRenderable(tampa, false, false);
        var blocoItem = new SolidRenderable(bloco, false, false);
        var antes = SolidRenderables.ResolveColors(vm, tampaItem).Face;

        vm.ActiveBooleanGroupName = "bloco_1";

        SolidRenderables.IsOutsideOpenEntity(vm, tampaItem).Should().BeTrue();
        SolidRenderables.IsOutsideOpenEntity(vm, blocoItem).Should().BeFalse();
        SolidRenderables.ResolveColors(vm, tampaItem).Face.Should().NotBe(antes);
    }

    [Fact]
    public void H_DentroDaEntidade_IsolaAEntidadeAberta()
    {
        var (vm, bloco, tampa) = Scene();
        vm.ActiveBooleanGroupName = "bloco_1";

        vm.OnKeyDown("H");

        vm.IsIsolating.Should().BeTrue();
        vm.IsolatedFeature.Should().BeSameAs(bloco);
        tampa.IsVisible.Should().BeFalse();
    }

    [Fact]
    public void EntidadeQueSumiuDoCodigo_VoltaAoNivelDaCena()
    {
        var (vm, _, tampa) = Scene();
        vm.ActiveBooleanGroupName = "bloco_1";

        vm.Sketch.ClearFeatures();
        vm.Sketch.AddFeature(tampa);
        vm.ReapplyOpenEntity();

        vm.IsEditingBooleanGroup.Should().BeFalse();
        vm.SelectionLevel.Should().Be(SelectionLevel.Scene);
    }

    [Fact]
    public void SolidoSolto_AbreSemContextoDePeca()
    {
        var (vm, _, _) = Scene();

        vm.ActiveBooleanGroupName = "tampa";

        vm.SelectionLevel.Should().Be(SelectionLevel.Entity);
        vm.EditContext.Should().BeNull("o código de um sólido solto está na cena, em coordenadas do mundo");
        vm.ActiveGroupTitle.Should().Be("Editando sólido:");
    }

    // ---------------------------------------------------------------- Entrega 1, ponta a ponta

    private const string BlocoRecord = """
        public sealed record Bloco(double Largura = 40) : Part
        {
            protected override IFeature Build()
            {
                var corpo = new BoxFeature(Point3D.Origin, Largura, 30, 10) { Name = "corpo" };
                return corpo;
            }
        }
        """;

    /// <summary>Girado 90° em Z e erguido: o topo local (z = 10) fica em z = 30 no mundo.</summary>
    private const string BlocoScene = """
        var bloco = Add(new Bloco(), at: Placement.At(100, 50, 20, rotationZDegrees: 90), name: "bloco");
        """;

    [Fact]
    public async Task ExtrudarAFaceDeCimaDaPecaAberta_GravaNoBuildEmCoordenadasLocais_EACenaNaoMuda()
    {
        using var folder = new SceneTestFolder();
        folder.CreateScene();
        folder.Write("Pecas/Bloco.cs", BlocoRecord);
        folder.Write("Cena.cs", BlocoScene);

        var vm = CreateViewModel();
        await vm.ScriptEditor.OpenProjectAsync(folder.Root);
        vm.ScriptEditor.HasErrors.Should().BeFalse(vm.ScriptEditor.ErrorSummary);
        string sceneBefore = File.ReadAllText(folder.FullPath("Cena.cs"));

        // Duplo clique na peça.
        vm.ActiveBooleanGroupName = "bloco";
        vm.EditContext.Should().NotBeNull();
        vm.ActiveGroupTitle.Should().Be("Editando peça:");

        // Clique na face de cima (z = 30 no mundo), como o ViewportInputHandler faz.
        var instance = vm.Sketch.Features.OfType<PartInstance>().Single(i => i.Name == "bloco");
        var mesh = vm.GetFeatureMesh(instance.Id)!;
        var face = new FaceProfile(instance, new WorkPlane(Vector3D.UnitZ, new Point3D(85, 70, 30)), mesh);

        var extrude = new ExtrudeCommand();
        vm.CurrentTool = extrude;
        extrude.Target.Should().Be(ExtrusionTarget.Standalone, "com uma peça aberta o Extrude gera o sólido autônomo");
        extrude.StartDirectExtrusion(vm.Sketch, face, new Point3D(85, 70, 30));
        extrude.OnValueEntered(vm.Sketch, 5);

        // O evento é async void (como o resto das ferramentas): espera o arquivo da peça mudar.
        string part = File.ReadAllText(folder.FullPath("Pecas/Bloco.cs"));
        for (int i = 0; i < 200 && !part.Contains("box_", StringComparison.Ordinal); i++)
        {
            await Task.Delay(50);
            part = File.ReadAllText(folder.FullPath("Pecas/Bloco.cs"));
        }

        part.Should().Contain("box_", "a extrusão foi gravada no Build() da peça");
        part.Should().NotContain("Push/Pull");
        File.ReadAllText(folder.FullPath("Cena.cs")).Should().Be(sceneBefore, "a cena não muda");
        vm.ScriptEditor.ActiveFile.Should().Be("Pecas/Bloco.cs", "o arquivo da peça fica ativo, para o Ctrl+Z valer");
        vm.StatusText.Should().StartWith("✓");

        // Na peça, a caixa nova fica sobre o topo local (z de 10 a 15), dentro dos 40 × 30 do bloco.
        var updated = vm.Sketch.Features.OfType<PartInstance>().Single(i => i.Name == "bloco");
        var box = Leaves(updated.Definition).OfType<BoxFeature>().Single(b => b.Name.StartsWith("box_", StringComparison.Ordinal));
        box.GetVertices().Min(v => v.Z).Should().BeApproximately(10, 1e-4);
        box.GetVertices().Max(v => v.Z).Should().BeApproximately(15, 1e-4);
        box.GetVertices().Min(v => v.X).Should().BeApproximately(0, 1e-4);
        box.GetVertices().Max(v => v.X).Should().BeApproximately(40, 1e-4);
        vm.IsEditingBooleanGroup.Should().BeTrue("a peça continua aberta depois de gravar");
    }

    // ---------------------------------------------------------------- Booleana dentro da entidade aberta

    private static async Task<string> WaitFor(SceneTestFolder folder, string file, Func<string, bool> condition)
    {
        string text = File.ReadAllText(folder.FullPath(file));
        for (int i = 0; i < 200 && !condition(text); i++)
        {
            await Task.Delay(50);
            text = File.ReadAllText(folder.FullPath(file));
        }
        return text;
    }

    [Fact]
    public async Task CortarNaPecaAberta_ConsomeOSolidoSolto_ECtrlZDesfazOsDoisArquivos()
    {
        using var folder = new SceneTestFolder();
        folder.CreateScene();
        folder.Write("Pecas/Bloco.cs", BlocoRecord);
        folder.Write("Cena.cs", BlocoScene + "\n" + """
            var rasgo = new BoxFeature(new Point3D(85, 60, 25), 10, 5, 10) { Name = "rasgo" };
            AddFeature(rasgo);
            """);

        var vm = CreateViewModel();
        await vm.ScriptEditor.OpenProjectAsync(folder.Root);
        vm.ScriptEditor.HasErrors.Should().BeFalse(vm.ScriptEditor.ErrorSummary);
        string sceneBefore = File.ReadAllText(folder.FullPath("Cena.cs"));
        string partBefore = File.ReadAllText(folder.FullPath("Pecas/Bloco.cs"));

        vm.ActiveBooleanGroupName = "bloco";
        vm.SelectBooleanCutCommand.Execute(null);
        var tool = vm.CurrentTool.Should().BeOfType<BooleanTool>().Subject;
        tool.IntoPart.Should().BeTrue();
        tool.GetStatusText().Should().Contain("em bloco", "a base é a peça aberta, sem escolher");

        tool.OnMouseDown(vm.Sketch, Point3D.Origin);
        tool.PickFeature(vm.Sketch.Features.Single(f => f.Name == "rasgo"), additive: false);

        string part = await WaitFor(folder, "Pecas/Bloco.cs", t => t.Contains("rasgo"));
        part.Should().Contain("rasgo.IsTransientTool = true;");
        File.ReadAllText(folder.FullPath("Cena.cs")).Should().NotContain("rasgo", "o sólido solto foi consumido");
        vm.ScriptEditor.ActiveFile.Should().Be("Pecas/Bloco.cs");
        vm.StatusText.Should().StartWith("✓");
        vm.IsEditingBooleanGroup.Should().BeTrue();

        vm.OnKeyDown("Z", isCtrlPressed: true);

        (await WaitFor(folder, "Cena.cs", t => t == sceneBefore)).Should().Be(sceneBefore, "o Ctrl+Z devolve o sólido à cena");
        File.ReadAllText(folder.FullPath("Pecas/Bloco.cs")).Should().Be(partBefore, "e tira da peça, no mesmo passo");
    }

    [Fact]
    public async Task CortarNumSolidoSoltoAberto_OResultadoFicaAberto()
    {
        using var folder = new SceneTestFolder();
        folder.CreateScene();
        folder.Write("Cena.cs", """
            var tampa = new BoxFeature(new Point3D(0, 0, 0), 30, 30, 5) { Name = "tampa" };
            AddFeature(tampa);
            var furo = new CylinderFeature(new Point3D(15, 15, -1), Vector3D.UnitZ, 3, 7) { Name = "furo" };
            AddFeature(furo);
            """);

        var vm = CreateViewModel();
        await vm.ScriptEditor.OpenProjectAsync(folder.Root);
        vm.ScriptEditor.HasErrors.Should().BeFalse(vm.ScriptEditor.ErrorSummary);

        vm.ActiveBooleanGroupName = "tampa";
        vm.SelectBooleanCutCommand.Execute(null);
        var tool = (BooleanTool)vm.CurrentTool!;
        tool.IntoPart.Should().BeFalse("um sólido solto não é peça: a booleana é a de sempre, na cena");
        tool.OnMouseDown(vm.Sketch, Point3D.Origin);
        tool.PickFeature(vm.Sketch.Features.Single(f => f.Name == "furo"), additive: false);

        await WaitFor(folder, "Cena.cs", t => t.Contains("BooleanFeature"));
        for (int i = 0; i < 100 && vm.ActiveBooleanGroupName == "tampa"; i++) await Task.Delay(50);

        vm.ActiveBooleanGroup.Should().NotBeNull("a entidade aberta passa a ser o resultado da booleana");
        vm.ActiveBooleanGroup!.BaseFeatures.Should().ContainSingle(f => f.Name == "tampa");
    }

    private static System.Collections.Generic.IEnumerable<IFeature> Leaves(IFeature feature) => feature switch
    {
        BooleanFeature b => b.BaseFeatures.Concat(b.ToolFeatures).SelectMany(Leaves),
        TransformedFeature t => Leaves(t.Source),
        _ => new[] { feature }
    };
}
