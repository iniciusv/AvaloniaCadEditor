using System.Reflection;
using AvaloniaCadEditor.Domain.Features;
using AvaloniaCadEditor.Domain.ValueObjects;
using FluentAssertions;

namespace AvaloniaCadEditor.Tests.Features;

public class FeatureFingerprintTests
{
    private sealed record PlacaTeste(double Largura) : Part
    {
        protected override IFeature Build() => new BoxFeature(Point3D.Origin, Largura, 10, 2);
    }

    private static BoxFeature Box() => new(new Point3D(1, 2, 3), 10, 20, 5) { Name = "box" };
    private static CylinderFeature Cylinder() => new(new Point3D(5, 5, 0), new Vector3D(0, 0, 1), 2, 10);
    private static PrismFeature Prism() => new(new[] { new Point3D(0, 0, 0), new Point3D(4, 0, 0), new Point3D(0, 3, 0) }, 7);

    private static string Fp(IFeature feature) => FeatureFingerprint.Compute(feature)!;

    [Fact]
    public void FeaturesComMesmaGeometria_TemMesmaChave_MesmoComIdENomeDiferentes()
    {
        var a = Box();
        var b = Box();
        b.Name = "outro nome";
        b.IsSelected = true;
        b.IsVisible = false;

        a.Id.Should().NotBe(b.Id);
        Fp(a).Should().Be(Fp(b));
    }

    [Fact]
    public void BooleanaIgual_TemMesmaChave()
    {
        var a = BooleanFeature.CreateDifference("a", Box(), Cylinder(), Cylinder());
        var b = BooleanFeature.CreateDifference("b", Box(), Cylinder(), Cylinder());

        Fp(a).Should().Be(Fp(b));
    }

    public static IEnumerable<object[]> Mutacoes()
    {
        yield return new object[] { "Box.BaseCorner", (Func<IFeature>)(() => { var f = Box(); f.BaseCorner = new Point3D(1, 2, 3.5); return f; }) };
        yield return new object[] { "Box.Width", (Func<IFeature>)(() => { var f = Box(); f.Width = 11; return f; }) };
        yield return new object[] { "Box.Length", (Func<IFeature>)(() => { var f = Box(); f.Length = 21; return f; }) };
        yield return new object[] { "Box.Height", (Func<IFeature>)(() => { var f = Box(); f.Height = 6; return f; }) };
        yield return new object[] { "Box.DirectionX", (Func<IFeature>)(() => { var f = Box(); f.DirectionX = new Vector3D(0, 1, 0); return f; }) };
        yield return new object[] { "Box.DirectionY", (Func<IFeature>)(() => { var f = Box(); f.DirectionY = new Vector3D(1, 0, 0); return f; }) };
        yield return new object[] { "Box.DirectionZ", (Func<IFeature>)(() => { var f = Box(); f.DirectionZ = new Vector3D(0, 0, -1); return f; }) };
        yield return new object[] { "Box.IsEnabled", (Func<IFeature>)(() => { var f = Box(); f.IsEnabled = false; return f; }) };
    }

    [Theory]
    [MemberData(nameof(Mutacoes))]
    public void MudarQualquerParametroGeometrico_MudaAChave(string caso, Func<IFeature> mutada)
    {
        Fp(mutada()).Should().NotBe(Fp(Box()), caso);
    }

    [Fact]
    public void ParametrosDoCilindroEDoPrisma_MudamAChave()
    {
        var baseCyl = Fp(Cylinder());
        var c1 = Cylinder(); c1.GlobalBaseCenter = new Point3D(5, 5, 1);
        var c2 = Cylinder(); c2.GlobalDirection = new Vector3D(0, 1, 0);
        var c3 = Cylinder(); c3.Radius = 2.5;
        var c4 = Cylinder(); c4.Height = 11;
        new[] { c1, c2, c3, c4 }.Select(Fp).Should().AllSatisfy(k => k.Should().NotBe(baseCyl));

        var basePrism = Fp(Prism());
        var p1 = Prism(); p1.Height = 8;
        var p2 = Prism(); p2.Plane = new WorkPlane(new Vector3D(1, 0, 0), Point3D.Origin);
        var p3 = new PrismFeature(new[] { new Point3D(0, 0, 0), new Point3D(4, 0, 0), new Point3D(0, 3.5, 0) }, 7);
        new[] { p1, p2, p3 }.Select(Fp).Should().AllSatisfy(k => k.Should().NotBe(basePrism));
    }

    [Fact]
    public void Booleana_DependeDaOperacaoEDosFilhos()
    {
        var corte = Fp(BooleanFeature.CreateDifference("x", Box(), Cylinder()));

        Fp(BooleanFeature.CreateUnion("x", Box(), Cylinder())).Should().NotBe(corte);
        var outroFilho = Cylinder(); outroFilho.Radius = 3;
        Fp(BooleanFeature.CreateDifference("x", Box(), outroFilho)).Should().NotBe(corte);
        Fp(BooleanFeature.CreateDifference("x", Box(), Cylinder(), Cylinder())).Should().NotBe(corte);

        // Filho alterado no lugar depois de criada a booleana: a chave acompanha (não é memorizada).
        var cyl = Cylinder();
        var booleana = BooleanFeature.CreateDifference("x", Box(), cyl);
        var antes = Fp(booleana);
        cyl.Height = 99;
        Fp(booleana).Should().NotBe(antes);
    }

    [Fact]
    public void InstanciaETransformada_DependemDoPlacement()
    {
        var part = new PlacaTeste(10);
        var definicao = new BoxFeature(Point3D.Origin, 10, 10, 2);
        var a = new PartInstance<PlacaTeste>(part, definicao, Placement.At(1, 2, 3));
        var b = new PartInstance<PlacaTeste>(part, definicao, Placement.At(1, 2, 3));
        var movida = new PartInstance<PlacaTeste>(part, definicao, Placement.At(1, 2, 4));
        var girada = new PartInstance<PlacaTeste>(part, definicao, Placement.At(1, 2, 3, rotationZDegrees: 90));

        Fp(a).Should().Be(Fp(b));
        Fp(movida).Should().NotBe(Fp(a));
        Fp(girada).Should().NotBe(Fp(a));

        Fp(Box().Transformed(Placement.Translate(new Vector3D(1, 0, 0))))
            .Should().NotBe(Fp(Box().Transformed(Placement.Translate(new Vector3D(2, 0, 0)))));
    }

    [Fact]
    public void TiposNaoSuportados_NaoTemChave()
    {
        FeatureFingerprint.Compute(new FilletFeature("f", Box(), 1)).Should().BeNull();
        FeatureFingerprint.Compute(new ImportedMeshFeature(AvaloniaCadEditor.Domain.Mesh.TriangleMesh.Empty)).Should().BeNull();
        // Um filho sem chave contamina o pai: a booleana inteira fica fora do cache.
        FeatureFingerprint.Compute(BooleanFeature.CreateUnion("u", Box(), new FilletFeature("f", Cylinder(), 1))).Should().BeNull();
    }

    /// <summary>
    /// Guarda contra o pior bug possível do cache: alguém acrescenta uma propriedade geométrica a uma
    /// feature e o fingerprint não a inclui — a malha antiga seria reaproveitada sem erro nenhum.
    /// Se este teste falhar, inclua a propriedade em <see cref="FeatureFingerprint"/> (e aqui), ou
    /// liste-a como não-geométrica.
    /// </summary>
    [Theory]
    [InlineData(typeof(BoxFeature), new[] { "BaseCorner", "Width", "Length", "Height", "DirectionX", "DirectionY", "DirectionZ" })]
    [InlineData(typeof(CylinderFeature), new[] { "GlobalBaseCenter", "GlobalDirection", "Radius", "Height" })]
    [InlineData(typeof(PrismFeature), new[] { "Vertices", "Height", "Plane" })]
    [InlineData(typeof(BooleanFeature), new[] { "Operation", "BaseFeatures", "ToolFeatures", "BaseFeature" })]
    [InlineData(typeof(TransformedFeature), new[] { "Source", "Placement" })]
    [InlineData(typeof(PartInstance<PlacaTeste>), new[] { "Definition", "Placement", "Part", "PartTypeName" })]
    public void FingerprintCobreTodasAsPropriedadesPublicas(Type featureType, string[] cobertas)
    {
        var naoGeometricas = new HashSet<string> { "Id", "Name", "IsEnabled", "IsVisible", "IsSelected", "IsTransientTool", "Color", "Dependencies" };

        var faltando = featureType
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .Distinct()
            .Where(n => !naoGeometricas.Contains(n) && !cobertas.Contains(n))
            .ToList();

        faltando.Should().BeEmpty($"{featureType.Name} ganhou propriedades que o FeatureFingerprint não conhece");
    }
}
