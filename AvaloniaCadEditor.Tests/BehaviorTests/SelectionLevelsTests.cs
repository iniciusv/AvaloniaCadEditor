using System;
using AvaloniaCadEditor.Application.EditContext;
using AvaloniaCadEditor.Domain.ValueObjects;
using FluentAssertions;
using Xunit;

namespace AvaloniaCadEditor.Tests.BehaviorTests;

/// <summary>
/// Seleção por nível: na cena, sólidos inteiros; dentro de uma entidade aberta (duplo clique), só ela e os
/// vazios dela; no esboço, as linhas. Não há mais seletor Sólido/Face/Aresta.
/// </summary>
public class SelectionLevelsTests
{
    private static readonly Guid Body = Guid.NewGuid();
    private static readonly Guid Tool = Guid.NewGuid();
    private static readonly Guid Other = Guid.NewGuid();

    private static RaycastHit Hit(float distance, Guid id) => new(distance, Point3D.Origin, Vector3D.UnitZ, id);

    [Theory]
    [InlineData(false, false, SelectionLevel.Scene)]
    [InlineData(false, true, SelectionLevel.Entity)]
    [InlineData(true, false, SelectionLevel.Sketch)]
    [InlineData(true, true, SelectionLevel.Sketch)]
    public void Resolve_OEsbocoGanhaDaEntidadeAberta(bool sketch, bool entityOpen, SelectionLevel expected) =>
        SelectionLevels.Resolve(sketch, entityOpen).Should().Be(expected);

    [Fact]
    public void NaCena_OAcertoMaisPerto()
    {
        var hits = new[] { Hit(1, Other), Hit(2, Body) };

        SelectionLevels.Pick(hits, openEntityId: null, Array.Empty<Guid>())!.Value.FeatureId.Should().Be(Other);
    }

    [Fact]
    public void DentroDaEntidade_OQueEstaNaFrenteEDeFora_NaoESelecionavel()
    {
        var hits = new[] { Hit(1, Other), Hit(2, Body) };

        SelectionLevels.Pick(hits, Body, Array.Empty<Guid>())!.Value.FeatureId.Should().Be(Body);
    }

    [Fact]
    public void DentroDaEntidade_OVazioGanhaDoCorpoMesmoAtrasDele()
    {
        var hits = new[] { Hit(1, Body), Hit(2, Tool) };

        SelectionLevels.Pick(hits, Body, new[] { Tool })!.Value.FeatureId.Should().Be(Tool);
    }

    [Fact]
    public void DentroDaEntidade_SoOCorpo_IgnoraOsVazios()
    {
        var hits = new[] { Hit(1, Tool), Hit(2, Body) };

        SelectionLevels.Pick(hits, Body, new[] { Tool }, bodyOnly: true)!.Value.FeatureId.Should().Be(Body);
    }

    [Fact]
    public void DentroDaEntidade_SemAcertoNela_Nada()
    {
        var hits = new[] { Hit(1, Other) };

        SelectionLevels.Pick(hits, Body, new[] { Tool }).Should().BeNull();
    }

    [Fact]
    public void Selecionaveis_DentroDaEntidade_SoElaEOsVazios()
    {
        var hits = new[] { Hit(1, Other), Hit(2, Body), Hit(3, Tool) };

        SelectionLevels.Selectable(hits, Body, new[] { Tool })
            .Should().HaveCount(2).And.OnlyContain(h => h.FeatureId == Body || h.FeatureId == Tool);
        SelectionLevels.Selectable(hits, null, new[] { Tool }).Should().HaveCount(3);
    }
}
