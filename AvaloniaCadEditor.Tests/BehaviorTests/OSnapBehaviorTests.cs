using AvaloniaCadEditor.Application.Services;
using AvaloniaCadEditor.Domain.Entities;
using AvaloniaCadEditor.Domain.Interfaces;
using AvaloniaCadEditor.Domain.ValueObjects;
using FluentAssertions;
using Moq;

namespace AvaloniaCadEditor.Tests.BehaviorTests;

public class OSnapBehaviorTests
{
    [Fact]
    public void OSnap_DevePuxarOCursorParaOEndpoint_QuandoEstiverNaToleranciaDefinida()
    {
        // Arrange
        var sketch = new Sketch();
        var line = new LineEntity(new Point3D(10, 10), new Point3D(20, 20));
        sketch.AddEntity(line);

        var mockEngine = new Mock<IGeometryEngine>();
        // Mock engine to say the snap points of the line are its endpoints
        mockEngine.Setup(e => e.GetSnapPoints(line, Moq.It.IsAny<Point3D?>()))
                  .Returns(new List<(Point3D, SnapPointType)> { (line.StartPoint, SnapPointType.Endpoint), (line.EndPoint, SnapPointType.Endpoint) });

        var osnapService = new OSnapService(mockEngine.Object);

        var cursorPertoDoInicio = new Point3D(9.5, 10.2); // Bem perto de 10,10
        var cursorLonge = new Point3D(50, 50);

        // Act
        var snappedPointPerto = osnapService.GetSnappedPoint(sketch, cursorPertoDoInicio, null);
        var snappedPointLonge = osnapService.GetSnappedPoint(sketch, cursorLonge, null);

        // Assert
        snappedPointPerto.Should().Be(new Point3D(10, 10)); // O cursor sofreu atração ("snap")
        snappedPointLonge.Should().Be(cursorLonge); // Não sofreu atração porque está longe
    }

    /// <summary>
    /// Sólido com a face de cima no plano de trabalho (XY, z = 0) — um quadrado 10×10 — e uma aresta 1 mm
    /// abaixo dele, logo sob o canto. Desenhando sobre a face, o canto da face tem de ganhar do vértice de
    /// baixo, mesmo estando mais longe do cursor em 3D.
    /// </summary>
    private static AvaloniaCadEditor.Domain.Mesh.TriangleMesh SolidoComFaceNoPlanoEArestaAbaixo()
    {
        var positions = new float[]
        {
            0, 0, 0,   10, 0, 0,   10, 10, 0,   0, 10, 0,   // 0-3: face de cima (no plano)
            1, 1, -1,  3, 1, -1                              // 4-5: aresta 1 mm abaixo
        };
        var featureEdges = new uint[] { 0, 1, 1, 2, 2, 3, 3, 0, 4, 5 };
        return new AvaloniaCadEditor.Domain.Mesh.TriangleMesh(positions, new float[positions.Length], new uint[] { 0, 1, 2, 0, 2, 3 }, featureEdges);
    }

    [Fact]
    public void OSnap_DesenhandoSobreAFace_SoPegaPontosDoContornoNoPlano()
    {
        var sketch = new Sketch(); // plano XY, z = 0
        var osnap = new OSnapService(new Mock<IGeometryEngine>().Object);
        var mesh = SolidoComFaceNoPlanoEArestaAbaixo();

        var noCanto = osnap.GetSnappedPoint(sketch, new Point3D(1, 1, 0), null, mesh, solidSnapOnPlaneOnly: true);
        var noMeio = osnap.GetSnappedPoint(sketch, new Point3D(5, 0.6, 0), null, mesh, solidSnapOnPlaneOnly: true);

        noCanto.Should().Be(new Point3D(0, 0, 0), "o vértice (1, 1, -1) está mais perto, mas fora do plano da face");
        noMeio.Should().Be(new Point3D(5, 0, 0), "o ponto médio de uma aresta do contorno também é ponto de snap");
    }

    [Fact]
    public void OSnap_ForaDoEsboco_MantemOSnap3DEmQualquerVertice()
    {
        var sketch = new Sketch();
        var osnap = new OSnapService(new Mock<IGeometryEngine>().Object);

        var snapped = osnap.GetSnappedPoint(sketch, new Point3D(1, 1, 0), null, SolidoComFaceNoPlanoEArestaAbaixo());

        snapped.Should().Be(new Point3D(1, 1, -1), "Mover/Girar levam sólidos até vértices de qualquer altura");
    }
}
