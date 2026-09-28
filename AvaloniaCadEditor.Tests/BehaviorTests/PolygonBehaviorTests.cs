using System;
using System.Linq;
using AvaloniaCadEditor.Application.Commands;
using AvaloniaCadEditor.Domain.Entities;
using AvaloniaCadEditor.Domain.ValueObjects;
using FluentAssertions;
using Xunit;

namespace AvaloniaCadEditor.Tests.BehaviorTests;

public class PolygonBehaviorTests
{
    [Fact]
    public void DesenharPoligono_DoisCliques_DeveGerarHexagonoFechadoPorPadrao()
    {
        var sketch = new Sketch();
        var command = new DrawPolygonCommand();

        command.OnMouseDown(sketch, new Point3D(0, 0));  // centro
        command.OnMouseDown(sketch, new Point3D(10, 0)); // define raio e rotação

        var lines = sketch.Entities.OfType<LineEntity>().ToList();
        lines.Should().HaveCount(6, "o padrão é um hexágono (6 lados)");

        // O primeiro vértice deve coincidir com o ponto clicado.
        lines[0].StartPoint.Should().Be(new Point3D(10, 0));

        // A cadeia deve ser fechada: cada linha começa onde a anterior termina.
        for (int i = 0; i < lines.Count; i++)
        {
            lines[i].EndPoint.Should().Be(lines[(i + 1) % lines.Count].StartPoint);
        }

        // Todos os vértices devem estar a uma distância de ~10 do centro.
        foreach (var line in lines)
        {
            line.StartPoint.DistanceTo(new Point3D(0, 0)).Should().BeApproximately(10, 1e-6);
        }
    }

    [Fact]
    public void DesenharPoligono_DigitandoNumeroDeLados_DeveMudarQuantidadeDeLinhas()
    {
        var sketch = new Sketch();
        var command = new DrawPolygonCommand();

        command.OnValueEntered(sketch, 4); // muda para quadrado antes de definir o centro

        command.OnMouseDown(sketch, new Point3D(0, 0));
        command.OnMouseDown(sketch, new Point3D(10, 0));

        var lines = sketch.Entities.OfType<LineEntity>().ToList();
        lines.Should().HaveCount(4);
    }

    [Fact]
    public void DesenharPoligono_ComRaioVCB_DeveSerExato()
    {
        var sketch = new Sketch();
        var command = new DrawPolygonCommand();

        command.OnMouseDown(sketch, new Point3D(5, 5)); // centro
        command.OnMouseMove(sketch, new Point3D(15, 5)); // define a rotação (ângulo 0)
        command.OnValueEntered(sketch, 20.0); // raio exato via VCB

        var lines = sketch.Entities.OfType<LineEntity>().ToList();
        lines.Should().HaveCount(6);
        lines[0].StartPoint.DistanceTo(new Point3D(5, 5)).Should().BeApproximately(20, 1e-6);
    }

    [Fact]
    public void DesenharPoligono_CancelamentoViaEscape_NaoDeveAdicionarNada()
    {
        var sketch = new Sketch();
        var command = new DrawPolygonCommand();

        command.OnMouseDown(sketch, new Point3D(0, 0));
        command.OnKeyDown(sketch, CadKey.Escape);
        command.OnMouseDown(sketch, new Point3D(10, 0)); // novo primeiro clique após cancelar

        sketch.Entities.Should().BeEmpty();
    }
}
