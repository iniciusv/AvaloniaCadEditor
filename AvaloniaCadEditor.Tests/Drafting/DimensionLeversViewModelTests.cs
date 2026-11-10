using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AvaloniaCadEditor.Analysis;
using AvaloniaCadEditor.Application.Interfaces;
using AvaloniaCadEditor.Application.Levers;
using AvaloniaCadEditor.Application.Services;
using AvaloniaCadEditor.Domain.Entities;
using AvaloniaCadEditor.Domain.Features;
using AvaloniaCadEditor.Domain.ValueObjects;
using AvaloniaCadEditor.Drafting;
using AvaloniaCadEditor.Infrastructure.OpenCascade;
using AvaloniaCadEditor.Infrastructure.Scripting;
using AvaloniaCadEditor.UI.Drafting;
using AvaloniaCadEditor.UI.ViewModels;
using FluentAssertions;
using Moq;
using Xunit;

namespace AvaloniaCadEditor.Tests.Drafting;

/// <summary>
/// O painel da cota selecionada, pelo ViewModel: a cena é executada de verdade, a peça é isolada com H e a
/// cota é clicada, como no app. A sonda é a de produção, menos nos testes de cancelamento, que precisam de
/// uma que fique parada.
/// </summary>
public class DimensionLeversViewModelTests
{
    // Duas instâncias da mesma peça; a cota entre os furos mede 20 (de x = -10 a x = 10).
    private const string Cena = """
        Add(new Bloco(), at: Placement.At(100, 50, 26), name: "peca");
        Add(new Bloco(), at: Placement.At(200, 50, 26), name: "outra");

        public sealed record Bloco(double Largura = 40) : Part
        {
            protected override IFeature Build()
            {
                const double raioFuro = 1.7;
                var corpo = new BoxFeature(new Point3D(-20, 0, 0), Largura, 16, 17) { Name = "corpo" };
                var furo_a = new CylinderFeature(new Point3D(-10, 16, 5), new Vector3D(0, -1, 0), raioFuro, 20) { Name = "furo_a", IsTransientTool = true };
                var furo_b = new CylinderFeature(new Point3D(20.0 / 2, 16, 5), new Vector3D(0, -1, 0), raioFuro, 20) { Name = "furo_b", IsTransientTool = true };
                return BooleanFeature.CreateDifference("bloco", corpo, furo_a, furo_b);
            }
        }
        """;

    private static async Task<(CadViewModel Cad, DraftingViewModel Drafting)> Isolado(ILeverProbeFactory factory, string cena = Cena)
    {
        var cad = TestUtils.CreateCadViewModel(
            new Mock<AvaloniaCadEditor.Domain.Interfaces.IGeometryEngine>().Object,
            new Mock<IOSnapService>().Object,
            null,
            new OpenCascadeGeometryKernel());
        var drafting = new DraftingViewModel(cad, factory);
        cad.Drafting = drafting;

        cad.ScriptEditor.GeneratedCSharpCode = cena;
        await cad.ScriptEditor.RunCodeAsync();
        cad.ScriptEditor.HasErrors.Should().BeFalse(cad.ScriptEditor.ErrorSummary);

        cad.Sketch.Features.Single(f => f.Name == "peca").IsSelected = true;
        cad.OnKeyDown("H", false);
        drafting.IsVisible = true;
        drafting.SetView(StandardView.Front);
        return (cad, drafting);
    }

    private static DraftDimension EntreFuros(DraftingViewModel drafting) =>
        drafting.Dimensions.Single(d => d.Kind == DimensionKind.Horizontal && Math.Abs(d.Value - 20) < 1e-6);

    /// <summary>Espera o trabalho em segundo plano acabar, inclusive o que ele mesmo dispara (a prévia depois das opções).</summary>
    private static async Task Settle(DimensionLeversViewModel levers)
    {
        Task pending;
        do
        {
            pending = levers.Pending;
            await pending.WaitAsync(TimeSpan.FromSeconds(60));
        }
        while (!ReferenceEquals(pending, levers.Pending));
    }

    /// <summary>A opção de um número só. Os furos são simétricos em X, então cada um aparece também num grupo
    /// com o outro, que mantém a simetria.</summary>
    private static LeverOptionItem Sozinho(DimensionLeversViewModel levers, string comeco) =>
        levers.Options.Single(o => !o.Option.IsGroup && o.Title.StartsWith(comeco, StringComparison.Ordinal));

    // ------------------------------------------------------------------ opções

    [Fact]
    public async Task CotaClicada_ListaSoAsMedidas_EAsIndiretasFicamAUmClique()
    {
        var (_, drafting) = await Isolado(new RoslynLeverProbeFactory());
        var levers = drafting.Levers;

        drafting.OnLabelClicked(EntreFuros(drafting));
        await Settle(levers);

        levers.IsBusy.Should().BeFalse();
        levers.Options.Should().NotBeEmpty().And.OnlyContain(o => o.Option.IsDirect);
        levers.Options.Select(o => o.Title).Should().Contain("furo_a · globalBaseCenter.x = -10")
            .And.Contain("furo_b · globalBaseCenter.x = 20.0", "20.0 é a medida de 20.0 / 2");
        levers.SelectedOption!.Option.IsDirect.Should().BeTrue("só uma medida vem escolhida");

        levers.IndirectCount.Should().BeGreaterThan(0, "o divisor de 20.0 / 2 também muda a cota");
        levers.CanToggleIndirect.Should().BeTrue();

        var escolhida = levers.SelectedOption;
        levers.ToggleIndirectCommand.Execute(null);

        levers.Options.Where(o => o.Option.Caveat == LeverCaveat.Factor).Should().NotBeEmpty()
            .And.OnlyContain(o => o.Caveat.Contains("divisor") && o.Title.EndsWith("= 2"));
        levers.SelectedOption!.Key.Should().Be(escolhida!.Key, "mostrar as indiretas não troca a escolha");
    }

    [Fact]
    public async Task NumeroDaPeca_AvisaQueMudaAsOutrasInstancias()
    {
        var (_, drafting) = await Isolado(new RoslynLeverProbeFactory());
        drafting.OnLabelClicked(EntreFuros(drafting));
        await Settle(drafting.Levers);

        var furo = Sozinho(drafting.Levers, "furo_a");

        furo.WideScope.Should().Contain("2 instâncias de Bloco");
        furo.NarrowScope.Should().BeEmpty();
    }

    [Fact]
    public async Task ArgumentoNaCena_SoMudaEstaInstancia_EOPadraoMudaAsQueNaoPassamOParametro()
    {
        string cena = Cena.Replace("Add(new Bloco(), at: Placement.At(100, 50, 26), name: \"peca\");",
            "Add(new Bloco(Largura: 40), at: Placement.At(100, 50, 26), name: \"peca\");");
        var (_, drafting) = await Isolado(new RoslynLeverProbeFactory(), cena);
        var total = drafting.Dimensions.Single(d => d.Kind == DimensionKind.Horizontal && Math.Abs(d.Value - 40) < 1e-6);

        drafting.OnLabelClicked(total);
        await Settle(drafting.Levers);

        var argumento = drafting.Levers.Options.Single(o => o.Option.Primary.Kind == CodeLeverKind.Argument);
        argumento.NarrowScope.Should().Contain("só esta instância");
        argumento.WideScope.Should().BeEmpty();
        drafting.Levers.Options.Should().NotContain(o => o.Option.Primary.Kind == CodeLeverKind.Default,
            "esta instância passa a Largura: o padrão não a muda");
    }

    // ------------------------------------------------------------------ prévia

    [Fact]
    public async Task ValorDigitado_MostraAntesDeGravar_OsNumerosAsOutrasCotasEOFantasma()
    {
        var (cad, drafting) = await Isolado(new RoslynLeverProbeFactory());
        var levers = drafting.Levers;

        // A altura dos furos (5, da base ao centro). Acima dela a folha tem a cota (12), que fecha os 17.
        drafting.OnLabelClicked(drafting.Dimensions.Single(d => d.Kind == DimensionKind.Vertical && Math.Abs(d.Value - 5) < 1e-6));
        await Settle(levers);
        levers.Preview.Should().BeNull("o valor ainda é o que a cota mede");

        // Os dois furos juntos: são simétricos, e subir um só quebraria a simetria.
        levers.SelectedOption = levers.Options.Single(o => o.Option.IsGroup && o.Title.StartsWith("furo_a", StringComparison.Ordinal));
        levers.TargetText = "7";
        await Settle(levers);

        var preview = levers.Preview;
        preview.Should().NotBeNull();
        preview!.Edits.Split('\n').Should().BeEquivalentTo(
            "furo_a · globalBaseCenter.z: 5 → 7", "furo_b · globalBaseCenter.z: 5 → 7");
        preview.Others.Should().Be("também muda: 12 → 10", "o que os furos sobem sai da cota de cima");
        preview.Warning.Should().BeEmpty();

        levers.GhostEntities.Select(e => e.SourceName).Should().BeEquivalentTo("furo_a", "furo_b");
        var antes = drafting.Sheet!.Entities.OfType<ViewCircle>().Single(c => c.SourceName == "furo_a");
        var depois = levers.GhostEntities.OfType<ViewCircle>().Single(c => c.SourceName == "furo_a");
        depois.Center.V.Should().BeApproximately(antes.Center.V + 2, 1e-3, "o fantasma é o furo já 2 mm acima");
        depois.Center.U.Should().BeApproximately(antes.Center.U, 1e-6);
        cad.ScriptEditor.GeneratedCSharpCode.Should().Be(Cena, "a prévia não grava nada");

        levers.TargetText = "abc";
        await Settle(levers);
        levers.Preview.Should().BeNull("sem número não há o que prever");
    }

    // ------------------------------------------------------------------ escolha lembrada e aplicar

    [Fact]
    public async Task OpcaoEscolhida_SobreviveAReexecucao_EAplicarGravaPorEla()
    {
        var (cad, drafting) = await Isolado(new RoslynLeverProbeFactory());
        var levers = drafting.Levers;
        drafting.OnLabelClicked(EntreFuros(drafting));
        await Settle(levers);
        levers.SelectedOption!.Option.IsGroup.Should().BeTrue("a primeira da lista move os dois furos e mantém a simetria");

        levers.SelectedOption = Sozinho(levers, "furo_b");
        levers.Update(drafting.Sheet, drafting.Dimensions); // a folha foi refeita (outra execução, uma cota escondida)
        await Settle(levers);
        levers.SelectedOption!.Should().Match<LeverOptionItem>(o => !o.Option.IsGroup && o.Title.StartsWith("furo_b"),
            "a lista foi refeita, a escolha do usuário não");

        levers.TargetText = "24";
        levers.ApplyCommand.Execute(null);
        await Settle(levers);

        cad.ScriptEditor.GeneratedCSharpCode.Should().Contain("new Point3D(28.0 / 2, 16, 5)", "20.0 / 2 → 28.0 / 2 leva o furo_b de 10 para 14")
            .And.Contain("new Point3D(-10, 16, 5)", "o furo_a não foi a opção escolhida");
        // Com só um furo movido o par deixa de ser simétrico e o desenho dá outra chave à cota. Ela é
        // reencontrada pelo que mede (os dois furos) e pelo valor que acabou de receber.
        levers.Selected.Should().NotBeNull("aplicar não fecha o painel");
        levers.Selected!.Value.Should().BeApproximately(24, 1e-6);
        levers.MeasuredSources.Should().BeEquivalentTo("furo_a", "furo_b");
        levers.SelectedOption!.Title.Should().Be("furo_b · globalBaseCenter.x = 28.0", "e a opção escolhida continua a mesma");
    }

    // ------------------------------------------------------------------ cancelamento

    /// <summary>Uma sonda que não fica pronta: a criação espera até ser solta ou cancelada.</summary>
    private sealed class StuckFactory : ILeverProbeFactory
    {
        private readonly object _gate = new();
        private readonly List<CancellationToken> _tokens = new();

        public SemaphoreSlim Entered { get; } = new(0);

        public IReadOnlyList<CancellationToken> Tokens
        {
            get { lock (_gate) return _tokens.ToList(); }
        }

        public ILeverProbe? Create(IReadOnlyList<ScriptDocument> documents, string partTypeName, CancellationToken cancellationToken = default)
        {
            lock (_gate) _tokens.Add(cancellationToken);
            Entered.Release();
            cancellationToken.WaitHandle.WaitOne(TimeSpan.FromSeconds(30));
            cancellationToken.ThrowIfCancellationRequested();
            return null;
        }
    }

    [Fact]
    public async Task SairDoIsolamento_CancelaOEstudoQueAindaRodava()
    {
        var factory = new StuckFactory();
        var (cad, drafting) = await Isolado(factory);
        drafting.OnLabelClicked(EntreFuros(drafting));
        (await factory.Entered.WaitAsync(TimeSpan.FromSeconds(30))).Should().BeTrue("o estudo começou");
        drafting.Levers.IsBusy.Should().BeTrue();

        cad.OnKeyDown("H", false); // sai do isolamento: a folha some

        factory.Tokens.Should().ContainSingle().Which.IsCancellationRequested.Should().BeTrue();
        await Settle(drafting.Levers);
        drafting.Levers.HasSelection.Should().BeFalse();
        drafting.Levers.IsBusy.Should().BeFalse();
        drafting.Levers.Status.Should().BeEmpty("cancelar não é erro: nada de \"A task was canceled\"");
    }

    [Fact]
    public async Task CodigoMudou_CancelaOEstudoDaVersaoAntiga_EComecaOutro()
    {
        var factory = new StuckFactory();
        var (cad, drafting) = await Isolado(factory);
        drafting.OnLabelClicked(EntreFuros(drafting));
        (await factory.Entered.WaitAsync(TimeSpan.FromSeconds(30))).Should().BeTrue();

        cad.ScriptEditor.GeneratedCSharpCode = Cena + "\n// editado";
        drafting.Levers.Update(drafting.Sheet, drafting.Dimensions);
        (await factory.Entered.WaitAsync(TimeSpan.FromSeconds(30))).Should().BeTrue("o código novo pede um estudo novo");

        var tokens = factory.Tokens;
        tokens.Should().HaveCount(2);
        tokens[0].IsCancellationRequested.Should().BeTrue("o estudo do código antigo não serve mais");
        tokens[1].IsCancellationRequested.Should().BeFalse();

        drafting.Levers.Clear();
        cad.OnKeyDown("H", false);
        factory.Tokens[1].IsCancellationRequested.Should().BeTrue();
    }
}

/// <summary>Os textos do painel, sem ViewModel: o que cada opção diz e o que a prévia mostra.</summary>
public class LeverOptionItemTests
{
    private static CodeLever Lever(int id, string owner, CodeLeverKind kind = CodeLeverKind.Literal) =>
        new(id, kind, new CodeLocation("Pecas/P.cs", id, 2, 1, 1, ""), "10", 10, owner, "x");

    private static LeverOption Option(IReadOnlyList<string> moved, params CodeLever[] levers) =>
        new(levers.Select(l => new LeverShare(l, 1)).ToList(), 1,
            moved.Select(n => new FactChange(n, FactChangeKind.Moved, new Vector3D(1, 0, 0), new Vector3D(0, 0, 0))).ToList(),
            Array.Empty<LeverRelation>(), Array.Empty<LeverRelation>(), 0);

    [Fact]
    public void MuitosElementos_SaemResumidos()
    {
        var nomes = Enumerable.Range(1, 22).Select(i => $"furo_{i}").ToList();

        new LeverOptionItem(Option(nomes, Lever(0, "sx"))).Effect.Should().Be("move furo_1, furo_2 e mais 20");
        new LeverOptionItem(Option(nomes.Take(3).ToList(), Lever(0, "a"))).Effect.Should().Be("move furo_1, furo_2, furo_3");
    }

    [Fact]
    public void Alcance_SoApareceQuandoAPecaTemOutrasInstancias()
    {
        var literal = Option(["furo"], Lever(0, "furo"));
        var argumento = Option(["corpo"], Lever(1, "peca", CodeLeverKind.Argument));
        var padrao = Option(["corpo"], Lever(2, "Largura", CodeLeverKind.Default));
        var tres = new LeverScope("Bloco", 3);

        new LeverOptionItem(literal).HasWideScope.Should().BeFalse("uma instância só: não há o que avisar");
        new LeverOptionItem(literal, tres).WideScope.Should().Contain("muda as 3 instâncias de Bloco");
        new LeverOptionItem(argumento, tres).NarrowScope.Should().Contain("só esta instância");
        new LeverOptionItem(argumento, tres).HasWideScope.Should().BeFalse();
        new LeverOptionItem(padrao, tres).WideScope.Should().Contain("que não passa Largura");
    }

    [Fact]
    public void Chave_NaoDependeDoIdDaSonda()
    {
        var a = new LeverOptionItem(Option(["furo"], Lever(3, "furo")));
        var b = new LeverOptionItem(Option(["furo"], Lever(41, "furo")));
        var outro = new LeverOptionItem(Option(["furo"], Lever(3, "porca")));

        a.Key.Should().Be(b.Key, "outra sonda numera os mesmos números de outro jeito");
        a.Key.Should().NotBe(outro.Key);
    }

    [Fact]
    public void Previa_OpcaoQueNaoChegaAoValor_DizIssoESemFantasma()
    {
        var basis = ViewBasis.For(StandardView.Front);

        var preview = LeverPreviewItem.Build(Option(["furo"], Lever(0, "furo")), solution: null, target: 99,
            Array.Empty<PrimitiveFact>(), basis, Array.Empty<(DraftDimension, DimensionGauge)>());

        preview.Solution.Should().BeNull();
        preview.Warning.Should().Contain("não chega a 99");
        preview.Ghost.Should().BeEmpty();
        preview.HasEdits.Should().BeFalse();
    }
}
