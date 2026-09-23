using AvaloniaCadEditor.Application.Commands;
using AvaloniaCadEditor.Application.Services;
using AvaloniaCadEditor.Domain.Entities;
using AvaloniaCadEditor.Domain.Interfaces;
using AvaloniaCadEditor.Domain.ValueObjects;
using AvaloniaCadEditor.UI.ViewModels;
using FluentAssertions;
using Moq;
using Xunit;

namespace AvaloniaCadEditor.Tests.BehaviorTests;

public class IntegrationBehaviorTests
{
    [Fact]
    public void ViewModel_TrocaDeFerramenta_ParaSelectAoApertarEspaco()
    {
        var mockEngine = new Mock<IGeometryEngine>();
        var mockSnap = new Mock<IOSnapService>();
        var vm = TestUtils.CreateCadViewModel(mockEngine.Object, mockSnap.Object);

        // Inicializa com Line
        vm.SelectLineToolCommand.Execute(null);
        vm.CurrentTool.Should().BeOfType<DrawLineCommand>();

        // Aperta espaço
        vm.OnKeyDown("Space");

        // Deve mudar pra SelectTool
        vm.CurrentTool.Should().BeOfType<SelectTool>();
    }

    [Fact]
    public void OSnap_IntegracaoComMoveTool_MagnetismoVetor()
    {
        // Cenário 5.2 do Plano de Testes
        var sketch = new Sketch();
        var quadrado = new LineEntity(new Point3D(0, 0), new Point3D(10, 0)) { IsSelected = true };
        var outraLinha = new LineEntity(new Point3D(50, 50), new Point3D(60, 50));
        sketch.AddEntity(quadrado);
        sketch.AddEntity(outraLinha);

        var mockEngine = new Mock<IGeometryEngine>();
        // Mocking snap points para a outraLinha (endpoints)
        mockEngine.Setup(e => e.GetSnapPoints(outraLinha, Moq.It.IsAny<Point3D?>())).Returns(new System.Collections.Generic.List<(Point3D, SnapPointType)> { (outraLinha.StartPoint, SnapPointType.Endpoint), (outraLinha.EndPoint, SnapPointType.Endpoint) });

        var osnapService = new OSnapService(mockEngine.Object);
        osnapService.IsEnabled = true;

        var moveTool = new MoveTool();

        // Usuário pega o canto do quadrado
        var pontoBase = new Point3D(0, 0); 
        moveTool.OnMouseDown(sketch, pontoBase);

        // Usuário move o mouse para perto do início da outra linha (50,50)
        var cursorMouse = new Point3D(49.8, 50.1); 
        
        // O OSnap entra em ação
        var snappedPoint = osnapService.GetSnappedPoint(sketch, cursorMouse, pontoBase);
        snappedPoint.Should().Be(new Point3D(50, 50)); // Magnetismo funcionou

        // Finaliza o Move clicando no ponto com snap
        moveTool.OnMouseDown(sketch, snappedPoint);

        // Verifica se o quadrado inteiro transladou colando o vértice no outro objeto perfeitamente
        quadrado.StartPoint.Should().Be(new Point3D(50, 50));
        quadrado.EndPoint.Should().Be(new Point3D(60, 50));
    }

    [Fact]
    public void ViewModel_AoTrocarParaFerramentaDeDesenho_DeveLimparSelecao()
    {
        var mockEngine = new Mock<IGeometryEngine>();
        var mockSnap = new Mock<IOSnapService>();
        var vm = TestUtils.CreateCadViewModel(mockEngine.Object, mockSnap.Object);

        // Seleciona uma linha e adiciona ao Sketch
        var line = new LineEntity(new Point3D(0, 0), new Point3D(10, 0)) { IsSelected = true };
        vm.Sketch.AddEntity(line);

        // Ao ativar a ferramenta de desenhar linha
        vm.SelectLineToolCommand.Execute(null);

        // A seleção anterior deve ter sido limpa
        line.IsSelected.Should().BeFalse();
    }
}
