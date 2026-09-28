using AvaloniaCadEditor.Domain.Entities;
using AvaloniaCadEditor.Domain.ValueObjects;
using AvaloniaCadEditor.Infrastructure.Engines;
using FluentAssertions;
using Xunit;

namespace AvaloniaCadEditor.Tests.BehaviorTests;

/// <summary>
/// Testa o HitTest de MathGeometryEngine usado na seleção de entidades do esboço:
/// deve escolher a entidade mais próxima do clique, não a primeira dentro da tolerância.
/// </summary>
public class HitTestBehaviorTests
{
    private readonly MathGeometryEngine _engine = new();

    [Fact]
    public void HitTest_ComLinhasParalelasProximas_DeveSelecionarAMaisProxima()
    {
        var lower = new LineEntity(new Point3D(0, 0), new Point3D(10, 0));
        var upper = new LineEntity(new Point3D(0, 1), new Point3D(10, 1));

        // Clique bem em cima da linha de baixo, mas dentro da faixa de tolerância das duas.
        var hit = _engine.HitTest(new Entity[] { lower, upper }, new Point3D(5, 0.2), 2.0);

        hit.Should().BeSameAs(lower);
    }

    [Fact]
    public void HitTest_ForaDaTolerancia_NaoDeveSelecionarNada()
    {
        var line = new LineEntity(new Point3D(0, 0), new Point3D(10, 0));

        var hit = _engine.HitTest(new Entity[] { line }, new Point3D(5, 3), 2.0);

        hit.Should().BeNull();
    }

    [Fact]
    public void HitTest_ComLinhaECirculoProximos_DeveSelecionarOMaisProximo()
    {
        var line = new LineEntity(new Point3D(0, 0), new Point3D(10, 0));
        var circle = new CircleEntity(new Point3D(5, 3), 2.5); // passa em y = 0.5 abaixo do centro

        var nearCircle = _engine.HitTest(new Entity[] { line, circle }, new Point3D(5, 0.45), 1.0);
        var nearLine = _engine.HitTest(new Entity[] { circle, line }, new Point3D(5, 0.05), 1.0);

        nearCircle.Should().BeSameAs(circle);
        nearLine.Should().BeSameAs(line);
    }

    [Fact]
    public void HitTest_ComEntidadesSobrepostas_DevePreferirAUltimaDesenhada()
    {
        var first = new LineEntity(new Point3D(0, 0), new Point3D(10, 0));
        var last = new LineEntity(new Point3D(0, 0), new Point3D(10, 0));

        var hit = _engine.HitTest(new Entity[] { first, last }, new Point3D(5, 0.1), 1.0);

        hit.Should().BeSameAs(last);
    }
}
