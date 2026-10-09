using AvaloniaCadEditor.Application.Services;
using AvaloniaCadEditor.Domain.Entities;
using AvaloniaCadEditor.Domain.Features;
using AvaloniaCadEditor.Domain.Profiles;
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

    // --- Região e arestas marcadas ao mesmo tempo ---
    //
    // É o que a varredura por pré-seleção precisa: a região é a seção, as arestas são o caminho.
    // Antes disso toda chamada ao serviço desmarcava tudo, e uma escolha apagava a outra.

    private static (Sketch Sketch, RectangleProfile Regiao, LineEntity A, LineEntity B) CenaRegiaoMaisArestas()
    {
        var sketch = new Sketch();
        var a = new LineEntity(new Point3D(0, 0, 0), new Point3D(100, 0, 0));
        var b = new LineEntity(new Point3D(100, 0, 0), new Point3D(100, 80, 0));
        sketch.AddEntity(a);
        sketch.AddEntity(b);

        var regiao = new RectangleProfile(new Point3D(200, 200, 0), 20, 10);
        sketch.SetProfiles(new IProfile[] { regiao });

        return (sketch, regiao, a, b);
    }

    [Fact]
    public void SelectMany_ComPerfil_DeixaRegiaoEArestasMarcadas()
    {
        var (sketch, regiao, a, b) = CenaRegiaoMaisArestas();
        var service = new SelectionService();

        service.SelectMany(sketch, System.Array.Empty<IFeature>(), new Entity[] { a, b }, regiao);

        regiao.IsSelected.Should().BeTrue();
        a.IsSelected.Should().BeTrue();
        b.IsSelected.Should().BeTrue();
        service.CurrentSelectionType.Should().Be(SelectionType.Profile);
        service.SelectedObject.Should().Be(regiao, "é o perfil que o resto do app lê como SelectedObject");
    }

    [Fact]
    public void SelectMany_SemPerfil_ContinuaLimpandoARegiao()
    {
        // A sobrecarga de três argumentos não pode mudar de comportamento: é ela que os ramos de
        // volumes e faces do retângulo usam.
        var (sketch, regiao, a, _) = CenaRegiaoMaisArestas();
        var service = new SelectionService();
        service.SelectProfile(regiao);
        regiao.IsSelected = true;

        service.SelectMany(sketch, System.Array.Empty<IFeature>(), new Entity[] { a });

        regiao.IsSelected.Should().BeFalse();
        service.SelectedObject.Should().Be(a);
    }

    [Fact]
    public void ToggleEntity_AcumulaArestasSemDesmarcarARegiao()
    {
        var (sketch, regiao, a, b) = CenaRegiaoMaisArestas();
        var service = new SelectionService();
        service.SelectById(sketch, regiao.Id);

        service.ToggleEntity(sketch, a);
        service.ToggleEntity(sketch, b);

        regiao.IsSelected.Should().BeTrue("a região é a seção e não pode ser apagada ao marcar o caminho");
        a.IsSelected.Should().BeTrue();
        b.IsSelected.Should().BeTrue();
        service.SelectedObject.Should().Be(regiao, "com uma região marcada, é ela a seleção primária");
    }

    [Fact]
    public void ToggleEntity_DuasVezes_VoltaAoEstadoAnterior()
    {
        var (sketch, _, a, b) = CenaRegiaoMaisArestas();
        var service = new SelectionService();

        service.ToggleEntity(sketch, a);
        service.ToggleEntity(sketch, b);
        service.ToggleEntity(sketch, b);

        a.IsSelected.Should().BeTrue();
        b.IsSelected.Should().BeFalse();
        service.SelectedObject.Should().Be(a, "desmarcar a primária promove outra ainda marcada");
    }

    [Fact]
    public void ToggleEntity_DesmarcarAUltima_LimpaASelecao()
    {
        var (sketch, _, a, _) = CenaRegiaoMaisArestas();
        var service = new SelectionService();

        service.ToggleEntity(sketch, a);
        service.ToggleEntity(sketch, a);

        a.IsSelected.Should().BeFalse();
        service.CurrentSelectionType.Should().Be(SelectionType.None);
        service.SelectedObject.Should().BeNull();
    }

    [Fact]
    public void SelectMany_ComCompoundProfile_MarcaCadaSubPerfil()
    {
        // O ProfileFaceRenderPass desenha os sub-perfis, não o composto: sem marcá-los, a seleção
        // múltipla de regiões sairia invisível.
        var sketch = new Sketch();
        var r1 = new RectangleProfile(new Point3D(0, 0, 0), 10, 10);
        var r2 = new RectangleProfile(new Point3D(50, 0, 0), 10, 10);
        var composto = new CompoundProfile(new IProfile[] { r1, r2 });
        var service = new SelectionService();

        service.SelectMany(sketch, System.Array.Empty<IFeature>(), System.Array.Empty<Entity>(), composto);

        r1.IsSelected.Should().BeTrue();
        r2.IsSelected.Should().BeTrue();
        service.SelectedObject.Should().Be(composto);
    }
}
