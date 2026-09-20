using System.Linq;
using AvaloniaCadEditor.Application.Commands;
using AvaloniaCadEditor.Domain.Entities;
using AvaloniaCadEditor.Domain.ValueObjects;
using AvaloniaCadEditor.Tests.Extensions;
using FluentAssertions;
using Xunit;

namespace AvaloniaCadEditor.Tests.BehaviorTests;

public class DrawBehaviorTests
{
    [Fact]
    public void DesenharQuadrado_DeveGerarQuatroLinhasConectadas()
    {
        // Arrange
        var sketch = new Sketch();
        var command = new DrawLineCommand();

        // Act - Simulando os cliques de um usuário para formar um quadrado 10x10
        command.OnMouseDown(sketch, new Point3D(0, 0));   // Primeiro clique: Inicia a linha
        command.OnMouseDown(sketch, new Point3D(10, 0));  // Desenha Linha 1 (base), inicia a próxima
        command.OnMouseDown(sketch, new Point3D(10, 10)); // Desenha Linha 2 (lateral direita)
        command.OnMouseDown(sketch, new Point3D(0, 10));  // Desenha Linha 3 (topo)
        command.OnMouseDown(sketch, new Point3D(0, 0));   // Desenha Linha 4 (lateral esquerda e fecha)
        command.Cancel(); // Esc ou Botão direito para soltar a ferramenta

        // Assert - O outcome (resultado final)
        var entities = sketch.Entities.OfType<LineEntity>().ToList();
        
        entities.Should().HaveCount(4, "porque o usuário desenhou 4 segmentos para formar um quadrado.");

        // Verifica estruturalmente cada linha usando a extensão Custom Assertion para tolerância
        entities[0].BeEquivalentToCadLine(new LineEntity(new Point3D(0, 0), new Point3D(10, 0)));
        entities[1].BeEquivalentToCadLine(new LineEntity(new Point3D(10, 0), new Point3D(10, 10)));
        entities[2].BeEquivalentToCadLine(new LineEntity(new Point3D(10, 10), new Point3D(0, 10)));
        entities[3].BeEquivalentToCadLine(new LineEntity(new Point3D(0, 10), new Point3D(0, 0)));
    }

    [Fact]
    public void DesenharLinha_ComRestricaoDeEixoX_DeveIgnorarOYdoMouse()
    {
        // Arrange
        var sketch = new Sketch();
        var command = new DrawLineCommand();

        // Act
        command.OnMouseDown(sketch, new Point3D(5, 5)); // Começa em 5,5
        
        // Simula pressionar a Seta Direita (Travar no Eixo X / Vermelho)
        command.OnKeyDown(sketch, CadKey.ArrowRight); 

        // Usuário move o mouse loucamente e clica em y=20 (O y deve ser ignorado)
        command.OnMouseDown(sketch, new Point3D(15, 20)); 

        // Assert
        var entities = sketch.Entities.OfType<LineEntity>().ToList();
        entities.Should().HaveCount(1);

        // O Y final da reta deve ser 5 (travado na origem inicial), e não 20.
        entities[0].BeEquivalentToCadLine(new LineEntity(new Point3D(5, 5), new Point3D(15, 5)));
    }

    [Fact]
    public void DesenharLinha_ComRestricaoDeEixoX_ETamanhoFixo_DeveSerExata()
    {
        // Arrange
        var sketch = new Sketch();
        var command = new DrawLineCommand();

        // Act
        command.OnMouseDown(sketch, new Point3D(0, 0)); 
        command.OnKeyDown(sketch, CadKey.ArrowRight); 
        
        // Mouse point to the right
        command.OnMouseMove(sketch, new Point3D(100, 100)); 
        
        // Usuário digita "30" e aperta Enter
        command.OnValueEntered(sketch, 30.0);

        // Assert
        var entities = sketch.Entities.OfType<LineEntity>().ToList();
        entities.Should().HaveCount(1);
        entities[0].BeEquivalentToCadLine(new LineEntity(new Point3D(0, 0), new Point3D(30, 0)));
    }

    [Fact]
    public void DesenharLinha_RestricaoDeEixoDupla_DeveSobrescreverAnterior()
    {
        var sketch = new Sketch();
        var command = new DrawLineCommand();
        command.OnMouseDown(sketch, new Point3D(0, 0));
        
        command.OnKeyDown(sketch, CadKey.ArrowRight); // Trava no X
        command.OnKeyDown(sketch, CadKey.ArrowLeft);  // Trava no Y (sobrescreve X)

        command.OnMouseMove(sketch, new Point3D(50, 50)); 
        command.OnValueEntered(sketch, 40.0); // Conclui linha travada no Y com tamanho 40

        var entities = sketch.Entities.OfType<LineEntity>().ToList();
        entities.Should().HaveCount(1);
        entities[0].BeEquivalentToCadLine(new LineEntity(new Point3D(0, 0), new Point3D(0, 40)));
    }

    [Fact]
    public void DesenharLinha_CancelamentoViaEscape_NaoDeveAdicionarLinha()
    {
        var sketch = new Sketch();
        var command = new DrawLineCommand();
        command.OnMouseDown(sketch, new Point3D(0, 0)); // Primeiro clique
        
        command.Cancel(); // Equivalente ao Escape no ViewModel

        command.OnMouseDown(sketch, new Point3D(10, 10)); // Como foi cancelado, esse clique é um NOVO primeiro clique

        sketch.Entities.Should().BeEmpty();
    }

    [Fact]
    public void DesenharCirculo_ComRaioVCB_DeveSerExato()
    {
        var sketch = new Sketch();
        var command = new DrawCircleCommand();
        
        command.OnMouseDown(sketch, new Point3D(5, 5)); // Ponto Central
        
        command.OnValueEntered(sketch, 12.5); // VCB com Raio exato

        var entities = sketch.Entities.OfType<CircleEntity>().ToList();
        entities.Should().HaveCount(1);
        entities[0].Center.Should().Be(new Point3D(5, 5));
        entities[0].Radius.Should().Be(12.5);
    }
}
