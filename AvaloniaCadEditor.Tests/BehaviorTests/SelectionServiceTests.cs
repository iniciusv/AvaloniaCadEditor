using AvaloniaCadEditor.Application.Services;
using AvaloniaCadEditor.Domain.Entities;
using AvaloniaCadEditor.Domain.Features;
using AvaloniaCadEditor.Domain.ValueObjects;
using FluentAssertions;
using Xunit;

namespace AvaloniaCadEditor.Tests.BehaviorTests;

public class SelectionServiceTests
{
    [Fact]
    public void ToggleFeature_MarcaVariasFeaturesSemDesmarcarAsOutras()
    {
        var a = new BoxFeature(Point3D.Origin, 10, 10, 10, "a");
        var b = new BoxFeature(new Point3D(20, 0, 0), 10, 10, 10, "b");
        var sketch = new Sketch();
        sketch.AddFeature(a);
        sketch.AddFeature(b);

        var service = new SelectionService();

        service.ToggleFeature(sketch, a);
        service.ToggleFeature(sketch, b);

        a.IsSelected.Should().BeTrue();
        b.IsSelected.Should().BeTrue("Ctrl+clique acumula, não substitui a seleção anterior");
        service.SelectedObject.Should().Be(b, "a última marcada é a seleção primária");
    }

    [Fact]
    public void ToggleFeature_MarcarDeNovoDesmarca_EASelecaoPrimariaPassaParaOutraAindaMarcada()
    {
        var a = new BoxFeature(Point3D.Origin, 10, 10, 10, "a");
        var b = new BoxFeature(new Point3D(20, 0, 0), 10, 10, 10, "b");
        var sketch = new Sketch();
        sketch.AddFeature(a);
        sketch.AddFeature(b);

        var service = new SelectionService();
        service.ToggleFeature(sketch, a);
        service.ToggleFeature(sketch, b);

        service.ToggleFeature(sketch, b); // desmarca b, que era a seleção primária

        b.IsSelected.Should().BeFalse();
        a.IsSelected.Should().BeTrue();
        service.SelectedObject.Should().Be(a);
    }

    [Fact]
    public void ToggleFeature_DesmarcarATodas_LimpaASelecao()
    {
        var a = new BoxFeature(Point3D.Origin, 10, 10, 10, "a");
        var sketch = new Sketch();
        sketch.AddFeature(a);

        var service = new SelectionService();
        service.ToggleFeature(sketch, a);
        service.ToggleFeature(sketch, a);

        a.IsSelected.Should().BeFalse();
        service.SelectedObject.Should().BeNull();
        service.CurrentSelectionType.Should().Be(SelectionType.None);
    }

    [Fact]
    public void SelectMany_SubstituiASelecao_EMarcaTodasAsFeaturesEEntidades()
    {
        var a = new BoxFeature(Point3D.Origin, 10, 10, 10, "a");
        var b = new BoxFeature(new Point3D(20, 0, 0), 10, 10, 10, "b");
        var c = new BoxFeature(new Point3D(40, 0, 0), 10, 10, 10, "c");
        var line = new LineEntity(Point3D.Origin, new Point3D(5, 0, 0));
        var sketch = new Sketch();
        sketch.AddFeature(a);
        sketch.AddFeature(b);
        sketch.AddFeature(c);
        sketch.AddEntity(line);

        var service = new SelectionService();
        service.SelectById(sketch, c.Id);

        service.SelectMany(sketch, new IFeature[] { a, b }, new Entity[] { line });

        a.IsSelected.Should().BeTrue();
        b.IsSelected.Should().BeTrue();
        line.IsSelected.Should().BeTrue();
        c.IsSelected.Should().BeFalse("o retângulo substitui a seleção anterior");
        service.SelectedObject.Should().Be(a, "a primeira feature vira a seleção primária");
    }

    [Fact]
    public void SelectMany_Vazio_LimpaASelecao()
    {
        var a = new BoxFeature(Point3D.Origin, 10, 10, 10, "a");
        var sketch = new Sketch();
        sketch.AddFeature(a);
        var service = new SelectionService();
        service.SelectById(sketch, a.Id);

        service.SelectMany(sketch, System.Array.Empty<IFeature>(), System.Array.Empty<Entity>());

        a.IsSelected.Should().BeFalse();
        service.CurrentSelectionType.Should().Be(SelectionType.None);
        service.SelectedObject.Should().BeNull();
    }
}
