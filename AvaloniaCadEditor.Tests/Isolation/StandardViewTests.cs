using System.Numerics;
using AvaloniaCadEditor.Domain.Entities;
using AvaloniaCadEditor.Domain.ValueObjects;
using AvaloniaCadEditor.Drafting;
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

    private static void ShouldBe(Vector3 actual, Vector3D expected)
    {
        actual.X.Should().BeApproximately((float)expected.X, 1e-4f);
        actual.Y.Should().BeApproximately((float)expected.Y, 1e-4f);
        actual.Z.Should().BeApproximately((float)expected.Z, 1e-4f);
    }

    [Theory]
    [InlineData(StandardView.Front, 0, -1, 0)]
    [InlineData(StandardView.Back, 0, 1, 0)]
    [InlineData(StandardView.Right, 1, 0, 0)]
    [InlineData(StandardView.Left, -1, 0, 0)]
    [InlineData(StandardView.Top, 0, 0, 1)]
    [InlineData(StandardView.Bottom, 0, 0, -1)]
    public void VistaPadrao_SemReferencial_OlhaDoLadoCerto(StandardView view, float x, float y, float z)
    {
        var camera = new CameraState();

        camera.SetStandardView(view);

        ShouldBe(EyeDirection(camera), x, y, z);
        camera.Roll.Should().Be(0f);
    }

    [Fact]
    public void Front_ComReferencialGirado180EmZ_OlhaDaFrenteDaPecaSemRolagem()
    {
        // Peça girada 180° em Z: a frente local (-Y local) aponta para +Y do mundo.
        var frame = new PartFrame("peca", Placement.At(0, 0, 0, rotationZDegrees: 180));
        var camera = new CameraState();

        camera.SetStandardView(StandardView.Front, frame);

        ShouldBe(EyeDirection(camera), 0, 1, 0);
        // X da peça para a direita da tela (que no mundo é -X).
        var right = Vector3.Normalize(Vector3.Cross(camera.Target - camera.Position, camera.Up));
        ShouldBe(right, -1, 0, 0);
        camera.Roll.Should().BeApproximately(0f, 1e-4f, "giro só em Z: as vistas continuam de pé sem rolagem");
    }

    [Fact]
    public void Right_ComReferencialGirado90EmZ_OlhaDoXLocalSemRolagem()
    {
        var frame = new PartFrame("peca", Placement.At(0, 0, 0, rotationZDegrees: 90));
        var camera = new CameraState();

        camera.SetStandardView(StandardView.Right, frame);

        // X local girado 90° vira +Y do mundo.
        ShouldBe(EyeDirection(camera), 0, 1, 0);
        camera.Roll.Should().BeApproximately(0f, 1e-4f);
    }

    [Fact]
    public void Bottom_XParaADireita()
    {
        var camera = new CameraState();

        camera.SetStandardView(StandardView.Bottom);

        var right = Vector3.Normalize(Vector3.Cross(camera.Target - camera.Position, camera.Up));
        ShouldBe(right, 1, 0, 0);
    }

    /// <summary>
    /// Referencial girado fora de Z, como o do carrinho_hgh15_z_b (deitado em X, depois giro de −90°
    /// em Z): para cada vista padrão, olho e "cima" da câmera devem bater com os mesmos vetores do
    /// <see cref="ViewBasis"/> (o gabarito que o desenho usa), levados ao mundo pelo referencial.
    /// </summary>
    [Theory]
    [InlineData(StandardView.Front)]
    [InlineData(StandardView.Back)]
    [InlineData(StandardView.Right)]
    [InlineData(StandardView.Left)]
    [InlineData(StandardView.Top)]
    [InlineData(StandardView.Bottom)]
    public void VistaPadrao_ComReferencialForaDeZ_BateComOViewBasis(StandardView view)
    {
        var frameToWorld = Placement.Rotation(Vector3D.UnitX, 90).Then(Placement.RotationZ(-90));
        var frame = new PartFrame("carrinho_hgh15_z_b", frameToWorld);
        var basis = ViewBasis.For(view)!;
        var camera = new CameraState();

        camera.SetStandardView(view, frame);

        ShouldBe(EyeDirection(camera), frame.FrameToWorld.ApplyVector(basis.W));
        ShouldBe(camera.Up, frame.FrameToWorld.ApplyVector(basis.V));
    }

    [Fact]
    public void Orbit_ZeraARolagem()
    {
        var frameToWorld = Placement.Rotation(Vector3D.UnitX, 90).Then(Placement.RotationZ(-90));
        var frame = new PartFrame("carrinho_hgh15_z_b", frameToWorld);
        var camera = new CameraState();
        camera.SetStandardView(StandardView.Right, frame);
        camera.Roll.Should().NotBe(0f, "a vista Direita desse referencial exige rolagem");

        camera.Orbit(0.1f, 0.1f);

        camera.Roll.Should().Be(0f, "orbitar volta ao modelo orbital Z-up, sem rolagem");
    }
}
