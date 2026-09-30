using System;
using System.Linq;
using AvaloniaCadEditor.Application.Commands;
using AvaloniaCadEditor.Application.Services;
using AvaloniaCadEditor.Domain.Entities;
using AvaloniaCadEditor.Domain.Features;
using AvaloniaCadEditor.Domain.Interfaces;
using AvaloniaCadEditor.Domain.Profiles;
using AvaloniaCadEditor.Domain.Scripting;
using AvaloniaCadEditor.Domain.ValueObjects;
using AvaloniaCadEditor.Infrastructure.OpenCascade;
using AvaloniaCadEditor.Tests.Extensions;
using AvaloniaCadEditor.Tests.Scenes;
using AvaloniaCadEditor.UI.ViewModels;
using FluentAssertions;
using Moq;
using Xunit;

namespace AvaloniaCadEditor.Tests.BehaviorTests;

public class BooleanBehaviorTests
{
    /// <summary>Peça de teste cuja definição usa fillet — não pode virar operando de booleana girada.</summary>
    private sealed record PlacaComFillet(double Largura) : Part
    {
        protected override IFeature Build()
        {
            var box = new BoxFeature(Point3D.Origin, Largura, Largura, 10);
            return new FilletFeature("filete", box, 2) { ApplyToAllEdges = true };
        }
    }

    [Fact]
    public void BooleanFeature_AceitaInstanciaDePecaGirada()
    {
        // Regressão: instâncias giradas eram sempre recusadas como operando de booleana, mesmo
        // quando a definição é uma extrusão simples que o kernel sabe reconstruir já girada.
        using var _ = SceneHost.Begin(new SceneBuilder());
        var girada = Scene.Add(new Placa(20), at: Placement.RotationAbout(Point3D.Origin, Vector3D.UnitZ, 45));
        var ferramenta = new BoxFeature(new Point3D(-100, -100, -100), 5, 5, 5);

        Action act = () => BooleanFeature.CreateDifference("corte", girada, ferramenta);

        act.Should().NotThrow();
    }

    [Fact]
    public void BooleanFeature_RecusaInstanciaGiradaComFillet()
    {
        // O kernel reconstrói extrusões/caixas/cilindros girados, mas não um fillet (depende das
        // arestas do sólido base não-girado) — esse caso continua bloqueado, com mensagem clara.
        using var _ = SceneHost.Begin(new SceneBuilder());
        var girada = Scene.Add(new PlacaComFillet(20), at: Placement.RotationAbout(Point3D.Origin, Vector3D.UnitZ, 45));
        var ferramenta = new BoxFeature(new Point3D(-100, -100, -100), 5, 5, 5);

        Action act = () => BooleanFeature.CreateDifference("corte", girada, ferramenta);

        act.Should().Throw<NotSupportedException>().WithMessage("*girada*fillet*");
    }

    [Fact]
    public void BooleanCut_ComOperandoGirado_KernelReconstroiOSolidoJaGirado()
    {
        using var _ = SceneHost.Begin(new SceneBuilder());
        // Placa(20): BoxFeature local Width=20 (X), Length=40 (Y), Height=5 (Z), canto em (0,0,0).
        var placa = Scene.Add(new Placa(20), at: Placement.RotationAbout(Point3D.Origin, Vector3D.UnitZ, 90));
        var ferramentaLonge = new BoxFeature(new Point3D(1000, 1000, 1000), 1, 1, 1);

        var corte = BooleanFeature.CreateDifference("corte", placa, ferramentaLonge);
        var kernel = new OpenCascadeGeometryKernel();

        var mesh = kernel.BuildFeatureMesh(corte);

        mesh.VertexCount.Should().BeGreaterThan(0);
        double volume = MeshVolumeCalculator.ComputeVolume(mesh);
        volume.Should().BeApproximately(20 * 40 * 5, 20 * 40 * 5 * 0.02);

        double minX = double.MaxValue, maxX = double.MinValue, minY = double.MaxValue, maxY = double.MinValue;
        for (int i = 0; i < mesh.VertexCount; i++)
        {
            minX = Math.Min(minX, mesh.Positions[i * 3]);
            maxX = Math.Max(maxX, mesh.Positions[i * 3]);
            minY = Math.Min(minY, mesh.Positions[i * 3 + 1]);
            maxY = Math.Max(maxY, mesh.Positions[i * 3 + 1]);
        }

        // A peça local ocupa X:[0,20] Y:[0,40]; girada 90° em Z (mão direita) em torno da origem,
        // passa a ocupar X:[-40,0] Y:[0,20] - confirma que o sólido saiu do kernel já girado, e não
        // só a caixa envolvente movida/intacta.
        minX.Should().BeApproximately(-40, 0.5);
        maxX.Should().BeApproximately(0, 0.5);
        minY.Should().BeApproximately(0, 0.5);
        maxY.Should().BeApproximately(20, 0.5);
    }

    [Fact]
    public void BooleanCut_Dominio_DeveGerarMalhaValida()
    {
        // Arrange
        var circle1 = new CircleProfile(new Point3D(0, 0, 0), 10.0);
        var cylinder1 = new ExtrudeFeature(circle1, 30.0);

        var circle2 = new CircleProfile(new Point3D(15, 0, 0), 10.0);
        var cylinder2 = new ExtrudeFeature(circle2, 30.0);

        var booleanCut = new BooleanFeature("Cut Test", BooleanOperationType.Subtract, cylinder1, new[] { cylinder2 });

        var kernel = new OpenCascadeGeometryKernel();

        // Act
        var mesh = kernel.BuildFeatureMesh(booleanCut);

        // Assert
        mesh.Should().NotBeNull();
        mesh.VertexCount.Should().BeGreaterThan(0);
        mesh.TriangleCount.Should().BeGreaterThan(0);

        var baseMesh = kernel.BuildFeatureMesh(cylinder1);
        mesh.TriangleCount.Should().NotBe(baseMesh.TriangleCount);
    }

    [Fact]
    public void FluxoE2E_CriarDoisCirculos_Extrudar_BooleanCut_ViaAtalhosDeTeclado()
    {
        // Arrange
        var kernel = new OpenCascadeGeometryKernel();
        var mockEngine = new Mock<IGeometryEngine>();
        var mockSnap = new Mock<IOSnapService>();
        mockSnap.Setup(s => s.GetSnappedPoint(It.IsAny<Sketch>(), It.IsAny<Point3D>(), It.IsAny<Point3D?>(), It.IsAny<AvaloniaCadEditor.Domain.Mesh.TriangleMesh>()))
                .Returns<Sketch, Point3D, Point3D?, AvaloniaCadEditor.Domain.Mesh.TriangleMesh>((s, p, t, m) => p);
        var profileService = new ProfileRecognitionService();
        var selectionService = new SelectionService();
        var vm = TestUtils.CreateCadViewModel(mockEngine.Object, mockSnap.Object, profileService, kernel, selectionService);

        // Act - Passo 1: Desenhar Círculo 1 (r=10, origem)
        vm.OnKeyDown("C");
        vm.OnPointerPressed(new Point3D(0, 0, 0));
        vm.OnKeyDown("D1"); vm.OnKeyDown("D0"); vm.OnKeyDown("Return");

        // Act - Passo 2: Extrudar Círculo 1 (h=30)
        vm.OnKeyDown("E");
        vm.OnKeyDown("D3"); vm.OnKeyDown("D0"); vm.OnKeyDown("Return");

        // Act - Passo 3: Desenhar Círculo 2 (r=10, offset x=15)
        vm.OnKeyDown("C");
        vm.OnPointerPressed(new Point3D(15, 0, 0));
        vm.OnKeyDown("D1"); vm.OnKeyDown("D0"); vm.OnKeyDown("Return");

        // Act - Passo 4: Extrudar Círculo 2 (h=30)
        vm.OnKeyDown("E");
        vm.OnKeyDown("D3"); vm.OnKeyDown("D0"); vm.OnKeyDown("Return");

        // Assert: Setup is correct
        var cylinders = vm.Sketch.Features.OfType<CylinderFeature>().ToList();
        cylinders.Should().HaveCount(2);

        // Act - Passo 5: Boolean Cut
        vm.SelectBooleanCutCommand.Execute(null);
        var booleanTool = (BooleanTool)vm.CurrentTool!;

        // Precisa passar o sketch para a ferramenta
        booleanTool.OnMouseDown(vm.Sketch, Point3D.Origin);

        // Selecionar Base (Cilindro 1) via SelectionService (Option B)
        selectionService.SelectFeature(cylinders[0]);

        // Clicar no Cilindro 2 para usá-lo como ferramenta (via SelectionService como a UI faria)
        selectionService.SelectFeature(cylinders[1]);

        // Assert: Boolean Feature created
        var booleanFeatures = vm.Sketch.Features.OfType<BooleanFeature>().ToList();
        booleanFeatures.Should().ContainSingle();
        var cutFeature = booleanFeatures.Single();
        var newCylinders = vm.Sketch.Features.OfType<CylinderFeature>().ToList();
        cutFeature.Operation.Should().Be(BooleanOperationType.Subtract);
        cutFeature.BaseFeature.Should().Be(newCylinders[0]);
        cutFeature.ToolFeatures.Should().ContainSingle().Which.Should().Be(newCylinders[1]);
    }

    [Fact]
    public void BooleanCut_BoundingBoxDoResultado_DeveSerReduzida()
    {
        // Arrange
        var circle1 = new CircleProfile(new Point3D(0, 0, 0), 10.0);
        var cylinder1 = new ExtrudeFeature(circle1, 30.0);

        var circle2 = new CircleProfile(new Point3D(15, 0, 0), 10.0);
        var cylinder2 = new ExtrudeFeature(circle2, 30.0);

        var booleanCut = new BooleanFeature("Cut Test", BooleanOperationType.Subtract, cylinder1, new[] { cylinder2 });
        var kernel = new OpenCascadeGeometryKernel();

        // Act
        var resultMesh = kernel.BuildFeatureMesh(booleanCut);
        var baseMesh = kernel.BuildFeatureMesh(cylinder1);

        // A Bounding Box em X do cilindro base era [-10, 10] (tamanho 20)
        // O cilindro 2 tem centro em x=15 e raio=10
        // A interseção de dois círculos de raio 10 separados por 15:
        // x^2 + y^2 = 100
        // (x-15)^2 + y^2 = 100  =>  x = 7.5
        // Portanto, a figura resultante (Cut) não passará de X = 7.5 nos "chifres" da meia-lua
        
        double minX = double.MaxValue;
        double maxX = double.MinValue;
        for (int i = 0; i < resultMesh.Positions.Length; i += 3)
        {
            if (resultMesh.Positions[i] < minX) minX = resultMesh.Positions[i];
            if (resultMesh.Positions[i] > maxX) maxX = resultMesh.Positions[i];
        }

        // Assert
        resultMesh.Positions.Length.Should().BeGreaterThan(0);
        
        minX.Should().BeApproximately(-10.0, 0.5);
        maxX.Should().BeApproximately(7.5, 0.5, "os vértices extremos do corte nos chifres ficam em X = 7.5");
    }

    [Fact]
    public void BooleanTool_ComGeometriasInstanciadasDoCodigo_DeveSelecionarCorretamente()
    {
        // 1. Arrange - Instanciar geometria diretamente como feito pelo Avaliador C#
        var cylinder1 = CylinderFeature.FromWorkPlane(WorkPlane.XY, new Point3D(0, 0, 0), 10, 30);
        var cylinder2 = CylinderFeature.FromWorkPlane(WorkPlane.XY, new Point3D(15, 0, 0), 10, 30);
        
        var sketch = new Sketch();
        sketch.AddFeature(cylinder1);
        sketch.AddFeature(cylinder2);
        
        var selectionService = new SelectionService();
        var booleanTool = new BooleanTool(BooleanOperationType.Subtract, selectionService);

        booleanTool.CodeGenerated += (s, code) => 
        {
            var op = new BooleanFeature("test_cut", BooleanOperationType.Subtract, cylinder1, new[] { cylinder2 });
            sketch.AddFeature(op);
        };

        // 2. Act - Simular seleção do Cyl 1 e depois Cyl 2 via UI/SelectionService
        selectionService.SelectFeature(cylinder1);
        booleanTool.OnMouseDown(sketch, new Point3D(2, 0, 0)); // Atribui sketch interno e possivelmente executa se pronto
        
        // Assert parcial - Cyl 1 deve ser o base
        selectionService.SelectedObject.Should().Be(cylinder1);
        
        selectionService.SelectFeature(cylinder2);
        booleanTool.OnMouseDown(sketch, new Point3D(13, 0, 0));
        
        // Assert - A operação deve ter sido consumada e inserida no Sketch
        sketch.Features.Should().Contain(f => f is BooleanFeature);
        var booleanOp = sketch.Features.OfType<BooleanFeature>().First();
        booleanOp.BaseFeature.Should().Be(cylinder1);
        booleanOp.ToolFeatures.Should().Contain(cylinder2);
    }

    // --- BooleanTool: acrescentar a uma booleana existente em vez de encadear (ver OperandAppendRequested) ---

    [Fact]
    public void BooleanTool_BaseJaESendoUmaBooleanaDoMesmoTipo_PedeAcrescimoEmVezDeGerarFeatureNova()
    {
        var box = new BoxFeature(new Point3D(0, 0, 0), 20, 20, 20, "box_1");
        var toolA = new BoxFeature(new Point3D(2, 2, -1), 4, 4, 22, "a");
        var existing = new BooleanFeature("boolOp_1", BooleanOperationType.Subtract, box, new IFeature[] { toolA });
        var toolB = new BoxFeature(new Point3D(8, 8, -1), 4, 4, 22, "b");

        var sketch = new Sketch();
        sketch.AddFeature(box);
        sketch.AddFeature(toolA);
        sketch.AddFeature(existing);
        sketch.AddFeature(toolB);

        var selectionService = new SelectionService();
        var booleanTool = new BooleanTool(BooleanOperationType.Subtract, selectionService);

        string? codeGenerated = null;
        (string BooleanVar, string ToolVar, string FallbackCode)? appendRequest = null;
        booleanTool.CodeGenerated += (s, code) => codeGenerated = code;
        booleanTool.OperandAppendRequested += (s, args) => appendRequest = args;

        selectionService.SelectFeature(existing);
        booleanTool.OnMouseDown(sketch, Point3D.Origin);
        selectionService.SelectFeature(toolB);
        booleanTool.OnMouseDown(sketch, Point3D.Origin);

        codeGenerated.Should().BeNull("a base já é uma BooleanFeature do mesmo tipo: deve acrescentar, não encadear");
        appendRequest.Should().NotBeNull();
        appendRequest!.Value.BooleanVar.Should().Be("boolOp_1");
        appendRequest.Value.ToolVar.Should().Be("b");
        appendRequest.Value.FallbackCode.Should().Contain("BooleanFeature");
    }

    [Fact]
    public void BooleanTool_BaseSendoBooleanaDeOutraOperacao_GeraFeatureNovaEncadeada()
    {
        var box = new BoxFeature(new Point3D(0, 0, 0), 20, 20, 20, "box_1");
        var toolA = new BoxFeature(new Point3D(2, 2, -1), 4, 4, 22, "a");
        // A base já existente é uma UNIÃO; a ferramenta atual é de Subtract - operações diferentes.
        var existingUnion = new BooleanFeature("boolOp_1", BooleanOperationType.Union, box, new IFeature[] { toolA });
        var toolB = new BoxFeature(new Point3D(8, 8, -1), 4, 4, 22, "b");

        var sketch = new Sketch();
        sketch.AddFeature(box);
        sketch.AddFeature(toolA);
        sketch.AddFeature(existingUnion);
        sketch.AddFeature(toolB);

        var selectionService = new SelectionService();
        var booleanTool = new BooleanTool(BooleanOperationType.Subtract, selectionService);

        string? codeGenerated = null;
        (string BooleanVar, string ToolVar, string FallbackCode)? appendRequest = null;
        booleanTool.CodeGenerated += (s, code) => codeGenerated = code;
        booleanTool.OperandAppendRequested += (s, args) => appendRequest = args;

        selectionService.SelectFeature(existingUnion);
        booleanTool.OnMouseDown(sketch, Point3D.Origin);
        selectionService.SelectFeature(toolB);
        booleanTool.OnMouseDown(sketch, Point3D.Origin);

        appendRequest.Should().BeNull();
        codeGenerated.Should().NotBeNull();
        codeGenerated.Should().Contain("BooleanOperationType.Subtract");
    }
}
