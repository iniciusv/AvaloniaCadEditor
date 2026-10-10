using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AvaloniaCadEditor.Application.Interfaces;
using AvaloniaCadEditor.Application.Scenes;
using AvaloniaCadEditor.Application.Services;
using AvaloniaCadEditor.Domain.Entities;
using AvaloniaCadEditor.Domain.Features;
using AvaloniaCadEditor.Domain.ValueObjects;
using AvaloniaCadEditor.UI.ViewModels;
using FluentAssertions;
using Xunit;

namespace AvaloniaCadEditor.Tests.BehaviorTests;

/// <summary>
/// "Incorporar na peça" no inspector: com um sólido solto e uma peça selecionados juntos (Ctrl+clique),
/// o alvo já é a peça da seleção — em qualquer ordem de clique.
/// </summary>
public class PropertyInspectorMergeTests
{
    private sealed record Caixa() : Part
    {
        protected override IFeature Build() => new BoxFeature(Point3D.Origin, 10, 10, 10) { Name = "caixa" };
    }

    /// <summary>Só o que o inspector usa para incorporar; o resto não é chamado nestes testes.</summary>
    private sealed class FakeEditor : IPartCodeEditor
    {
        private readonly Sketch _sketch;
        public FakeEditor(Sketch sketch) => _sketch = sketch;

        public (IFeature Solid, PartInstance Target, PartMergeMode Mode)? Merged { get; private set; }

        public IReadOnlyList<PartInstance> ScenePartInstances() => _sketch.Features.OfType<PartInstance>().ToList();

        public IReadOnlyList<IFeature> SelectedSceneFeatures() =>
            _sketch.Features.Where(f => f.IsSelected && f is not WorkPlaneFeature).ToList();

        public Task<string?> MergeIntoPartAsync(IFeature feature, PartInstance target, PartMergeMode mode)
        {
            Merged = (feature, target, mode);
            return Task.FromResult<string?>(null);
        }

        public IReadOnlyList<PartArgument>? ReadPartArguments(PartInstance instance) => null;
        public int CountInstancesFromSameCall(PartInstance instance) => 1;
        public Task<string?> SetPartParameterAsync(PartInstance instance, string parameterName, string text) => throw new NotSupportedException();
        public Task<string?> SetPartTypeParameterDefaultAsync(PartInstance owner, string parameterName, string text) => throw new NotSupportedException();
        public (int Overriding, int Total) CountInstancesOverriding(string partTypeName, string parameterName) => (0, 1);
        public bool GoToPartDefinition(string typeName) => false;
        public Task<string?> MovePartInstanceAsync(PartInstance instance, Vector3D offset) => throw new NotSupportedException();
        public Task<string?> SetPartInstanceColorAsync(PartInstance instance, string? color) => throw new NotSupportedException();
        public Task<string?> RenameFeatureAsync(IFeature feature, string newName) => throw new NotSupportedException();
        public Task<string?> ConvertToPartAsync(IFeature feature, string typeName) => throw new NotSupportedException();
    }

    private static (Sketch Sketch, SelectionService Selection, FakeEditor Editor, PropertyInspectorViewModel Inspector, BoxFeature Solto, PartInstance Perto, PartInstance Longe) Setup()
    {
        var sketch = new Sketch();
        var solto = new BoxFeature(new Point3D(2, 2, 10), 2, 2, 2) { Name = "dente" };
        var caixa = new Caixa();
        var longe = new PartInstance<Caixa>(caixa, new BoxFeature(Point3D.Origin, 10, 10, 10), Placement.At(500, 0, 0)) { Name = "longe" };
        var perto = new PartInstance<Caixa>(caixa, new BoxFeature(Point3D.Origin, 10, 10, 10), Placement.Identity) { Name = "perto" };
        sketch.AddFeature(solto);
        sketch.AddFeature(longe);
        sketch.AddFeature(perto);

        var selection = new SelectionService();
        var editor = new FakeEditor(sketch);
        var inspector = new PropertyInspectorViewModel(selection, () => { }, () => editor);
        return (sketch, selection, editor, inspector, solto, perto, longe);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SolidoSoltoEPecaSelecionados_OAlvoEAPecaDaSelecao(bool pecaPorUltimo)
    {
        var (sketch, selection, editor, inspector, solto, _, longe) = Setup();

        // A peça escolhida é a LONGE: no modo par vale a seleção, não a proximidade.
        if (pecaPorUltimo)
        {
            selection.ToggleFeature(sketch, solto);
            selection.ToggleFeature(sketch, longe);
        }
        else
        {
            selection.ToggleFeature(sketch, longe);
            selection.ToggleFeature(sketch, solto);
        }

        inspector.CanMergeIntoPart.Should().BeTrue();
        inspector.IsMergePair.Should().BeTrue();
        inspector.MergeCaption.Should().Be("Incorporar dente em longe (Caixa)");
        inspector.MergeTargets.Should().ContainSingle().Which.Instance.Should().BeSameAs(longe);

        inspector.SelectedMergeMode = PropertyInspectorViewModel.MergeModes.Single(m => m.Mode == PartMergeMode.SubtractAsTool);
        inspector.MergeIntoPartCommand.Execute(null);

        editor.Merged.Should().NotBeNull();
        editor.Merged!.Value.Solid.Should().BeSameAs(solto);
        editor.Merged.Value.Target.Should().BeSameAs(longe);
        editor.Merged.Value.Mode.Should().Be(PartMergeMode.SubtractAsTool);
    }

    [Fact]
    public void SoOSolidoSolto_ListaAsPecasComAMaisProximaPrimeiro()
    {
        var (_, selection, _, inspector, solto, perto, _) = Setup();

        selection.SelectFeature(solto);

        inspector.CanMergeIntoPart.Should().BeTrue();
        inspector.IsMergePair.Should().BeFalse();
        inspector.MergeTargets.Select(t => t.Instance.Name).Should().Equal("perto", "longe");
        inspector.SelectedMergeTarget!.Instance.Should().BeSameAs(perto);
    }

    [Fact]
    public void DuasPecas_NaoOfereceIncorporar()
    {
        var (sketch, selection, _, inspector, _, perto, longe) = Setup();

        selection.ToggleFeature(sketch, perto);
        selection.ToggleFeature(sketch, longe);

        inspector.CanMergeIntoPart.Should().BeFalse();
    }
}
