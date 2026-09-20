using System.Linq;
using AvaloniaCadEditor.Application.Services;
using AvaloniaCadEditor.Domain.Entities;
using AvaloniaCadEditor.Domain.Profiles;
using AvaloniaCadEditor.Domain.ValueObjects;
using FluentAssertions;
using Xunit;

namespace AvaloniaCadEditor.Tests.BehaviorTests;

public class ProfileRecognitionTests
{
    [Fact]
    public void ReconhecerQuadrado_DeveCriarRectangleProfileComoPadrao_EOferecerPolygonComoOpcao()
    {
        // Arrange: 4 linhas formando um retângulo 20x10
        var sketch = new Sketch();
        sketch.AddEntity(new LineEntity(new Point3D(0, 0), new Point3D(20, 0)));
        sketch.AddEntity(new LineEntity(new Point3D(20, 0), new Point3D(20, 10)));
        sketch.AddEntity(new LineEntity(new Point3D(20, 10), new Point3D(0, 10)));
        sketch.AddEntity(new LineEntity(new Point3D(0, 10), new Point3D(0, 0)));

        var service = new ProfileRecognitionService();

        // Act
        var profiles = service.RecognizeProfiles(sketch);

        // Assert
        profiles.Should().HaveCount(1);
        var profile = profiles[0];

        // 1. Padrão sugerido mais específico
        profile.Classification.Should().Be(ProfileClassification.Rectangle);
        profile.Should().BeOfType<RectangleProfile>();

        var rect = (RectangleProfile)profile;
        rect.Width.Should().BeApproximately(20.0, 1e-4);
        rect.Height.Should().BeApproximately(10.0, 1e-4);
        rect.Center.Should().Be(new Point3D(10, 5, 0));
        rect.Area.Should().BeApproximately(200.0, 1e-4);

        // 2. Opções disponíveis para escolha / override do usuário
        rect.AvailableCandidates.Should().HaveCount(2);
        rect.AvailableCandidates[0].Classification.Should().Be(ProfileClassification.Rectangle);
        rect.AvailableCandidates[1].Classification.Should().Be(ProfileClassification.Polygon);

        // 3. Teste de override: Usuário troca para Polígono Geral
        var switchedProfile = rect.SwitchClassification(ProfileClassification.Polygon);
        switchedProfile.Classification.Should().Be(ProfileClassification.Polygon);
        switchedProfile.Should().BeOfType<PolygonProfile>();
        switchedProfile.GetVertices().Should().HaveCount(4);
    }

    [Fact]
    public void ReconhecerTriangulo_NaoDeveSerRetangulo_DeveSerApenasPolygon()
    {
        // Arrange: Triângulo (não pode ser classificado como retângulo)
        var sketch = new Sketch();
        sketch.AddEntity(new LineEntity(new Point3D(0, 0), new Point3D(10, 0)));
        sketch.AddEntity(new LineEntity(new Point3D(10, 0), new Point3D(5, 8)));
        sketch.AddEntity(new LineEntity(new Point3D(5, 8), new Point3D(0, 0)));

        var service = new ProfileRecognitionService();

        // Act
        var profiles = service.RecognizeProfiles(sketch);

        // Assert
        profiles.Should().HaveCount(1);
        var profile = profiles[0];
        profile.Classification.Should().Be(ProfileClassification.Polygon);
        profile.AvailableCandidates.Should().HaveCount(1);
        profile.AvailableCandidates[0].Classification.Should().Be(ProfileClassification.Polygon);
    }

    [Fact]
    public void ReconhecerCirculo_DeveGerarCircleProfileParametrico()
    {
        // Arrange
        var sketch = new Sketch();
        sketch.AddEntity(new CircleEntity(new Point3D(15, 25, 0), 7.5));

        var service = new ProfileRecognitionService();

        // Act
        var profiles = service.RecognizeProfiles(sketch);

        // Assert
        profiles.Should().HaveCount(1);
        var profile = profiles[0];
        profile.Classification.Should().Be(ProfileClassification.Circle);

        var circle = (CircleProfile)profile;
        circle.Center.Should().Be(new Point3D(15, 25, 0));
        circle.Radius.Should().Be(7.5);
        circle.Area.Should().BeApproximately(System.Math.PI * 7.5 * 7.5, 1e-4);
    }
}
