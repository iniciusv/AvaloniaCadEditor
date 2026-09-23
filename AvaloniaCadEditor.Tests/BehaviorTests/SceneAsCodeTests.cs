using System;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;
using AvaloniaCadEditor.Domain.Entities;
using AvaloniaCadEditor.Domain.Features;
using AvaloniaCadEditor.Domain.Profiles;
using AvaloniaCadEditor.Domain.ValueObjects;
using AvaloniaCadEditor.Application.Services;
using AvaloniaCadEditor.Infrastructure.Scripting;
using AvaloniaCadEditor.Infrastructure.OpenCascade;
using AvaloniaCadEditor.Tests.Extensions;

namespace AvaloniaCadEditor.Tests.BehaviorTests;

public class SceneAsCodeTests
{
    [Fact]
    public void CylinderFeature_DeveGerarMalhaNoKernel()
    {
        var cyl = new CylinderFeature(Point3D.Origin, new Vector3D(0, 0, 1), 10, 30);
        var kernel = new OpenCascadeGeometryKernel();
        var mesh = kernel.BuildFeatureMesh(cyl);
        mesh.TriangleCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public void BoxFeature_DeveGerarMalhaNoKernel()
    {
        var box = new BoxFeature(Point3D.Origin, 10, 20, 30);
        var kernel = new OpenCascadeGeometryKernel();
        var mesh = kernel.BuildFeatureMesh(box);
        mesh.TriangleCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task RoundTrip_CodigoGerado_DeveReconstruirModeloEquivalente()
    {
        // 1. Cria modelo via domínio
        var circle = new CircleProfile(new Point3D(0, 0, 0), 10.0);
        var extrude = new ExtrudeFeature(circle, 30.0);
        var sketch = new Sketch();
        sketch.AddFeature(extrude);

        // 2. Gera código C#
        var generator = new CsCodeGenerator();
        var code = generator.GenerateCode(sketch);

        // 3. Avalia o código
        var evaluator = new RoslynSceneEvaluator();
        var result = await evaluator.EvaluateAsync(code);

        // 4. Assert: equivalência geométrica
        System.Console.WriteLine($"Erro do Evaluator: {result.ErrorMessage}");
        result.Success.Should().BeTrue($"O evaluator deveria ter compilado o código gerado. Erro: {result.ErrorMessage}");
        result.Features.Should().HaveCount(2); // WorkPlane (XY base) and the Cylinder
        result.Features[1].Should().BeOfType<CylinderFeature>();

        var kernel = new OpenCascadeGeometryKernel();
        var originalMesh = kernel.BuildFeatureMesh(extrude);
        var rebuiltMesh = kernel.BuildFeatureMesh(result.Features[1]);

        var origBounds = MeshVolumeEstimator.GetBoundsX(originalMesh);
        var newBounds = MeshVolumeEstimator.GetBoundsX(rebuiltMesh);
        
        origBounds.Min.Should().BeApproximately(newBounds.Min, 0.5f);
        origBounds.Max.Should().BeApproximately(newBounds.Max, 0.5f);
    }

    [Fact]
    public async Task Evaluator_CodigoInvalido_DeveRetornarErroSemExcecao()
    {
        var evaluator = new RoslynSceneEvaluator();
        var result = await evaluator.EvaluateAsync("isso não é código C# válido !!!");
        
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task RoundTrip_CenarioComplexoComBoolean_DeveReconstruirModeloEquivalente()
    {
        // 1. Cria modelo complexo via domínio (dois cilindros e um boolean cut)
        var circle1 = new CircleProfile(new Point3D(0, 0, 0), 10.0);
        var cylinder1 = new ExtrudeFeature(circle1, 30.0);

        var circle2 = new CircleProfile(new Point3D(15, 0, 0), 10.0);
        var cylinder2 = new ExtrudeFeature(circle2, 30.0);

        var booleanCut = new BooleanFeature("Cut Test", BooleanOperationType.Subtract, cylinder1, new[] { cylinder2 });

        var sketch = new Sketch();
        sketch.AddFeature(cylinder1);
        sketch.AddFeature(cylinder2);
        sketch.AddFeature(booleanCut);

        // 2. Gera código C# do cenário completo
        var generator = new CsCodeGenerator();
        var code = generator.GenerateCode(sketch);

        // 3. Avalia o código C#
        var evaluator = new RoslynSceneEvaluator();
        var result = await evaluator.EvaluateAsync(code);

        // 4. Assert
        System.Console.WriteLine($"Erro do Evaluator: {result.ErrorMessage}");
        result.Success.Should().BeTrue($"O evaluator deveria ter compilado o código gerado. Erro: {result.ErrorMessage}");
        
        // Espera-se os seguintes recursos injetados no Sketch:
        // - WorkPlane (XY base criado pelo gerador/sketch padrão)
        // - cylinder_1 (CylinderFeature correspondente ao ExtrudeFeature 1)
        // - cylinder_2 (CylinderFeature correspondente ao ExtrudeFeature 2)
        // - boolOp_1 (BooleanFeature)
        result.Features.Should().HaveCount(4);
        
        var generatedBooleanOp = result.Features[3] as BooleanFeature;
        generatedBooleanOp.Should().NotBeNull();
        generatedBooleanOp!.Operation.Should().Be(BooleanOperationType.Subtract);
        generatedBooleanOp.BaseFeature.Should().BeOfType<CylinderFeature>();

        // Reconstrução geométrica para validar as Bounding Boxes (equivalência do shape matemático C# com C++)
        var kernel = new OpenCascadeGeometryKernel();
        var originalMesh = kernel.BuildFeatureMesh(booleanCut);
        var rebuiltMesh = kernel.BuildFeatureMesh(generatedBooleanOp);

        var origBounds = MeshVolumeEstimator.GetBoundsX(originalMesh);
        var newBounds = MeshVolumeEstimator.GetBoundsX(rebuiltMesh);
        
        origBounds.Min.Should().BeApproximately(newBounds.Min, 0.5f);
        origBounds.Max.Should().BeApproximately(newBounds.Max, 0.5f);
    }

    [Fact]
    public async Task RoundTrip_ParametricBox_Extrusion_DeveGerarBoxProcedural()
    {
        // 1. Cria a caixa original
        var baseBox = new BoxFeature(Point3D.Origin, 30, 30, 30);
        
        // 2. Simula o usuário selecionando a face do topo e extrudando
        var paramFace = new ParametricBoxFace(baseBox, FaceType.Top);
        var extrude = new ExtrudeFeature(paramFace, 15.0);
        
        // 3. Simula o Push/Pull (Boolean Union)
        var booleanUnion = new BooleanFeature("Push/Pull", BooleanOperationType.Union, baseBox, new[] { extrude });

        var sketch = new Sketch();
        sketch.AddFeature(baseBox);
        sketch.AddFeature(extrude);
        sketch.AddFeature(booleanUnion);

        // 4. Gera o código C#
        var generator = new CsCodeGenerator();
        var code = generator.GenerateCode(sketch);

        System.Console.WriteLine("Código Gerado:\n" + code);

        // 5. Verifica se o código usa lógica procedural (referencia a FaceType.Top e dimensões)
        code.Should().Contain("GetFaceCenter(FaceType.Top)");
        code.Should().Contain("GetFaceWidth(FaceType.Top)");
        code.Should().Contain("GetFaceLength(FaceType.Top)");

        // 6. Avalia o código C#
        var evaluator = new RoslynSceneEvaluator();
        var result = await evaluator.EvaluateAsync(code);

        result.Success.Should().BeTrue($"O evaluator deveria ter compilado o código gerado. Erro: {result.ErrorMessage}");
        
        // O Sketch gerado deve ter o WorkPlane, BaseBox, TopBox e o BooleanUnion.
        result.Features.Should().HaveCount(4);
        var generatedUnion = result.Features[3] as BooleanFeature;
        generatedUnion.Should().NotBeNull();
        
        var topBox = generatedUnion!.ToolFeatures[0] as BoxFeature;
        topBox.Should().NotBeNull();
        
        // A nova caixa no topo deve ter centro Z deslocado (base 30 de altura, topo em 30. Centro Z da base era 0, Z do topo é 30. + altura 15 = centro da nova caixa na verdade o GetFaceCenter do Top da caixa original base center 0 com altura 30 é z = 30)
        // Wait, if base center is 0, height is 30. GetFaceCenter(Top) returns Z=30.
        // A nova caixa vai ter GlobalCenter no topo da velha (Z=30). E ela própria é extrudada a partir dali. 
        topBox!.GlobalCenter.Z.Should().Be(30.0);
        topBox.Width.Should().Be(30.0);
        topBox.Length.Should().Be(30.0);
        topBox.Height.Should().Be(15.0);
    }

    [Fact]
    public async Task RoundTrip_ParametricCylinder_Extrusion_DeveGerarCylinderProcedural()
    {
        // 1. Cria o cilindro original
        var baseCylinder = new CylinderFeature(Point3D.Origin, new Vector3D(0, 0, 1), 20, 50);
        
        // 2. Simula o usuário selecionando a face do topo e extrudando
        var paramFace = new ParametricCylinderFace(baseCylinder, FaceType.Top);
        var extrude = new ExtrudeFeature(paramFace, 25.0);
        
        // 3. Simula o Push/Pull (Boolean Union)
        var booleanUnion = new BooleanFeature("Push/Pull", BooleanOperationType.Union, baseCylinder, new[] { extrude });

        var sketch = new Sketch();
        sketch.AddFeature(baseCylinder);
        sketch.AddFeature(extrude);
        sketch.AddFeature(booleanUnion);

        // 4. Gera o código C#
        var generator = new CsCodeGenerator();
        var code = generator.GenerateCode(sketch);

        // 5. Verifica se o código usa lógica procedural
        code.Should().Contain("GetFaceCenter(FaceType.Top)");
        code.Should().Contain("GetFaceNormal(FaceType.Top)");
        code.Should().Contain("radius:");

        // 6. Avalia o código C#
        var evaluator = new RoslynSceneEvaluator();
        var result = await evaluator.EvaluateAsync(code);

        result.Success.Should().BeTrue($"O evaluator deveria ter compilado o código gerado. Erro: {result.ErrorMessage}");
        
        result.Features.Should().HaveCount(4);
        var generatedUnion = result.Features[3] as BooleanFeature;
        generatedUnion.Should().NotBeNull();
        
        var topCyl = generatedUnion!.ToolFeatures[0] as CylinderFeature;
        topCyl.Should().NotBeNull();
        
        // A nova caixa no topo deve ter centro Z deslocado para 50
        topCyl!.GlobalBaseCenter.Z.Should().Be(50.0);
        topCyl.Radius.Should().Be(20.0);
        topCyl.Height.Should().Be(25.0);
    }
}
