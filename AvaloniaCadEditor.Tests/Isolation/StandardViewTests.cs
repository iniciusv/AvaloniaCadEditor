using System.Numerics;
using AvaloniaCadEditor.Domain.Entities;
using AvaloniaCadEditor.Domain.ValueObjects;
using FluentAssertions;
using Xunit;

namespace AvaloniaCadEditor.Tests.Isolation;

public class StandardViewTests
{
    /// <summary>Direção de onde a câmera olha (do alvo para o olho), normalizada.</summary>
    private static Vector3 EyeDirection(CameraState camera) => Vector3.Normalize(camera.Position - camera.Target);

    private static void ShouldBe(Vector3 actual, float x, float y, float z)
    {
        actual.X.Should().BeApproximately(x, 1e-4f);
        actual.Y.Should().BeApproximately(y, 1e-4f);
        actual.Z.Should().BeApproximately(z, 1e-4f);
    }

    [Theory]
    [InlineData(StandardView.Front, 0, -1, 0)]
    [InlineData(StandardView.Back, 0, 1, 0)]
    [InlineData(StandardView.Right, 1, 0, 0)]
    [InlineData(StandardView.Left, -1, 0, 0)]
    [InlineData(StandardView.Top, 0, 0, 1)]
    [InlineData(StandardView.Bottom, 0, 0, -1)]
    public void VistaPadrao_SemOffset_OlhaDoLadoCerto(StandardView view, float x, float y, float z)
    {
        var camera = new CameraState();

        camera.SetStandardView(view);

        ShouldBe(EyeDirection(camera), x, y, z);
    }

    [Fact]
    public void Front_ComReferencialGirado180EmZ_OlhaDaFrenteDaPeca()
    {
        // Peça girada 180° em Z: a frente local (-Y local) aponta para +Y do mundo.
        var frame = new PartFrame("peca", Placement.At(0, 0, 0, rotationZDegrees: 180));
        var camera = new CameraState();

        camera.SetStandardView(StandardView.Front, (float)frame.AzimuthOffset);

        ShouldBe(EyeDirection(camera), 0, 1, 0);
        // X da peça para a direita da tela (que no mundo é -X).
        var right = Vector3.Normalize(Vector3.Cross(camera.Target - camera.Position, camera.Up));
        ShouldBe(right, -1, 0, 0);
    }

    [Fact]
    public void Right_ComReferencialGirado90EmZ_OlhaDoXLocal()
    {
        var frame = new PartFrame("peca", Placement.At(0, 0, 0, rotationZDegrees: 90));
        var camera = new CameraState();

        camera.SetStandardView(StandardView.Right, (float)frame.AzimuthOffset);

        // X local girado 90° vira +Y do mundo.
        ShouldBe(EyeDirection(camera), 0, 1, 0);
    }

    [Fact]
    public void Bottom_XParaADireita()
    {
        var camera = new CameraState();

        camera.SetStandardView(StandardView.Bottom);

        var right = Vector3.Normalize(Vector3.Cross(camera.Target - camera.Position, camera.Up));
        ShouldBe(right, 1, 0, 0);
    }
}
