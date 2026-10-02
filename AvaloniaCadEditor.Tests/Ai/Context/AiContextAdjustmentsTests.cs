using AvaloniaCadEditor.Application.Ai.Context;
using FluentAssertions;
using Xunit;

namespace AvaloniaCadEditor.Tests.Ai.Context;

public class AiContextAdjustmentsTests
{
    [Fact]
    public void FixarEExcluir_SaoMutuamenteExclusivos()
    {
        var adjustments = new AiContextAdjustments();

        adjustments.Exclude("Pecas/A.cs");
        adjustments.Pin("Pecas/A.cs");
        adjustments.Excluded.Should().BeEmpty();
        adjustments.IsPinned("pecas/a.cs").Should().BeTrue("caminhos não diferenciam maiúsculas");

        adjustments.Exclude("Pecas/A.cs");
        adjustments.Pinned.Should().BeEmpty();
        adjustments.Excluded.Should().Contain("Pecas/A.cs");
    }

    [Fact]
    public void TogglePin_AlternaEReset_Limpa()
    {
        var adjustments = new AiContextAdjustments();

        adjustments.TogglePin("Pecas/A.cs");
        adjustments.IsPinned("Pecas/A.cs").Should().BeTrue();
        adjustments.TogglePin("Pecas/A.cs");
        adjustments.IsPinned("Pecas/A.cs").Should().BeFalse();

        adjustments.Pin("Pecas/B.cs");
        adjustments.Exclude("Pecas/C.cs");
        adjustments.Reset();
        adjustments.Pinned.Should().BeEmpty();
        adjustments.Excluded.Should().BeEmpty();
    }
}
