using System.Reflection;
using AvaloniaCadEditor.Domain.Features;
using AvaloniaCadEditor.Domain.Profiles;
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
    private static ConeFeature Cone() => new(new Point3D(5, 5, 0), new Vector3D(0, 0, 1), 6, 3, 10);
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
    public void ParametrosDoCone_MudamAChave()
    {
        var baseCone = Fp(Cone());
        var c1 = Cone(); c1.GlobalBaseCenter = new Point3D(5, 5, 1);
        var c2 = Cone(); c2.GlobalDirection = new Vector3D(0, 1, 0);
        var c3 = Cone(); c3.BottomRadius = 6.5;
        var c4 = Cone(); c4.TopRadius = 0;      // tronco vs. cone completo: geometrias diferentes
        var c5 = Cone(); c5.Height = 11;
        new[] { c1, c2, c3, c4, c5 }.Select(Fp).Should().AllSatisfy(k => k.Should().NotBe(baseCone));
    }

    [Fact]
    public void ConeECilindroComOsMesmosNumeros_TemChavesDiferentes()
    {
        Fp(new ConeFeature(Point3D.Origin, new Vector3D(0, 0, 1), 5, 5, 10))
            .Should().NotBe(Fp(new CylinderFeature(Point3D.Origin, new Vector3D(0, 0, 1), 5, 10)));
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

    private static ImportedMeshFeature SemChave() => new(AvaloniaCadEditor.Domain.Mesh.TriangleMesh.Empty);

    [Fact]
    public void TiposNaoSuportados_NaoTemChave()
    {
        FeatureFingerprint.Compute(SemChave()).Should().BeNull();
        // Um filho sem chave contamina o pai: a booleana (ou o fillet) inteira fica fora do cache.
        FeatureFingerprint.Compute(BooleanFeature.CreateUnion("u", Box(), SemChave())).Should().BeNull();
        FeatureFingerprint.Compute(new FilletFeature("f", SemChave(), 1)).Should().BeNull();
    }

    private static FilletFeature Fillet(IFeature? baseFeature = null) =>
        new("f", baseFeature ?? Box(), 1) { Mode = FilletMode.Bevel, ApplyToAllEdges = true };

    [Fact]
    public void Fillet_IgualTemMesmaChave_EDependeDeCadaParametro()
    {
        Fp(Fillet()).Should().Be(Fp(Fillet()));

        var baseFillet = Fp(Fillet());
        var f1 = Fillet(); f1.Radius = 2;
        var f2 = Fillet(); f2.Mode = FilletMode.Round;
        var f3 = Fillet(); f3.ApplyToAllEdges = false;
        var f4 = Fillet(); f4.AddEdge(new EdgeReference(f4.BaseFeature, EdgeType.TopFront));
        var f5 = Fillet(); f5.AddFace(new FaceReference(f5.BaseFeature, (int)FaceType.Top, new Vector3D(0, 0, 1), "topo"));
        var outraBase = Box(); outraBase.Height = 6;
        var f6 = Fillet(outraBase);
        new[] { f1, f2, f3, f4, f5, f6 }.Select(Fp).Should().AllSatisfy(k => k.Should().NotBe(baseFillet));

        // A geometria do dono da aresta entra na chave.
        var a = Fillet(); a.AddEdge(new EdgeReference(Box(), EdgeType.TopFront));
        var donoMaior = Box(); donoMaior.Width = 30;
        var b = Fillet(); b.AddEdge(new EdgeReference(donoMaior, EdgeType.TopFront));
        Fp(a).Should().NotBe(Fp(b));
    }

    private static ExtrudeFeature Extrusao(IProfile? profile = null) =>
        new(profile ?? new PolygonProfile(new[] { new Point3D(0, 0, 0), new Point3D(4, 0, 0), new Point3D(0, 3, 0) }), 5);

    [Fact]
    public void Extrusao_IgualTemMesmaChave_EDependeDoPerfilEDaDirecao()
    {
        Fp(Extrusao()).Should().Be(Fp(Extrusao()));

        var baseExt = Fp(Extrusao());
        var e1 = Extrusao(); e1.Direction = new Vector3D(0, 0, 6);
        var e2 = Extrusao(new PolygonProfile(new[] { new Point3D(0, 0, 0), new Point3D(4, 0, 0), new Point3D(0, 3.5, 0) }));
        var e3 = Extrusao(new PolygonProfile(new[] { new Point3D(0, 0, 0), new Point3D(4, 0, 0), new Point3D(0, 3, 0) },
            plane: new WorkPlane(new Vector3D(1, 0, 0), Point3D.Origin)));
        new[] { e1, e2, e3 }.Select(Fp).Should().AllSatisfy(k => k.Should().NotBe(baseExt));

        var ret = Fp(Extrusao(new RectangleProfile(Point3D.Origin, 4, 3)));
        Fp(Extrusao(new RectangleProfile(Point3D.Origin, 4, 3))).Should().Be(ret);
        Fp(Extrusao(new RectangleProfile(Point3D.Origin, 4, 3, rotationAngle: 0.1))).Should().NotBe(ret);

        var circ = Fp(Extrusao(new CircleProfile(Point3D.Origin, 2)));
        Fp(Extrusao(new CircleProfile(Point3D.Origin, 2.5))).Should().NotBe(circ);
    }

    /// <summary>Perfis que o fingerprint não sabe descrever (ex.: compostos, com furos) ficam fora do cache.</summary>
    [Fact]
    public void Extrusao_DePerfilDesconhecido_NaoTemChave()
    {
        var face = new FaceProfile(Box(), WorkPlane.XY, AvaloniaCadEditor.Domain.Mesh.TriangleMesh.Empty);
        FeatureFingerprint.Compute(Extrusao(face)).Should().BeNull();
    }

    [Theory]
    [InlineData(typeof(PolygonProfile), new[] { "Vertices", "Plane" })]
    [InlineData(typeof(RectangleProfile), new[] { "Center", "Width", "Height", "RotationAngle", "Plane" })]
    [InlineData(typeof(CircleProfile), new[] { "Center", "Radius", "Plane" })]
    public void FingerprintCobreTodasAsPropriedadesDosPerfis(Type profileType, string[] cobertas)
    {
        // Derivadas (Normal, Area, Center do polígono) ou sem efeito na geometria.
        var naoGeometricas = new HashSet<string> { "Id", "Classification", "AvailableCandidates", "SourceEntityIds", "Normal", "Area", "Center", "IsSelected", "IsVisible" };

        var faltando = profileType
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .Distinct()
            .Where(n => !naoGeometricas.Contains(n) && !cobertas.Contains(n))
            .ToList();

        faltando.Should().BeEmpty($"{profileType.Name} ganhou propriedades que o FeatureFingerprint não conhece");
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
    // IsFullCone é derivada de TopRadius.
    [InlineData(typeof(ConeFeature), new[] { "GlobalBaseCenter", "GlobalDirection", "BottomRadius", "TopRadius", "Height", "IsFullCone" })]
    [InlineData(typeof(PrismFeature), new[] { "Vertices", "Height", "Plane" })]
    [InlineData(typeof(BooleanFeature), new[] { "Operation", "BaseFeatures", "ToolFeatures", "BaseFeature" })]
    [InlineData(typeof(TransformedFeature), new[] { "Source", "Placement" })]
    [InlineData(typeof(FilletFeature), new[] { "BaseFeature", "Edges", "Faces", "Radius", "Mode", "ApplyToAllEdges" })]
    // BaseArea, Volume, BaseCenter, TopCenter e Centroid são derivadas de Profile/Height/Direction.
    [InlineData(typeof(ExtrudeFeature), new[] { "Profile", "Height", "Direction", "BaseArea", "Volume", "BaseCenter", "TopCenter", "Centroid" })]
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

    /// <summary>
    /// A theory acima só checa os tipos que alguém lembrou de listar num [InlineData] — uma feature
    /// nova que ninguém liste simplesmente nunca é verificada, e o teste passa verde enquanto o
    /// fingerprint ignora o tipo. Este teste fecha esse buraco.
    /// </summary>
    [Fact]
    public void TodaFeatureConcretaDoDominio_EstaNaTheoryDeCobertura()
    {
        var listados = typeof(FeatureFingerprintTests)
            .GetMethod(nameof(FingerprintCobreTodasAsPropriedadesPublicas))!
            .GetCustomAttributes<InlineDataAttribute>()
            .Select(d => (Type)d.GetData(null!).First()[0]!)
            .Select(t => t.IsGenericType ? t.GetGenericTypeDefinition() : t)
            .ToHashSet();

        // Sem geometria paramétrica própria: não são cacheáveis por desenho (FeatureFingerprint
        // devolve null para elas, e quem chama simplesmente não usa o cache).
        var fora = new HashSet<Type> { typeof(ImportedMeshFeature), typeof(WorkPlaneFeature) };

        typeof(FeatureBase).Assembly.GetTypes()
            .Where(t => t.IsPublic && !t.IsAbstract && typeof(FeatureBase).IsAssignableFrom(t))
            .Where(t => !fora.Contains(t))
            .Select(t => t.IsGenericType ? t.GetGenericTypeDefinition() : t)
            .Should().BeSubsetOf(listados, "toda feature nova precisa entrar na theory de cobertura do fingerprint");
    }
}
