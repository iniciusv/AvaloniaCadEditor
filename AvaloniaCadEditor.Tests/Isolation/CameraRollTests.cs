using System.Numerics;
using AvaloniaCadEditor.Domain.Entities;
using AvaloniaCadEditor.Domain.ValueObjects;
using FluentAssertions;
using Xunit;

namespace AvaloniaCadEditor.Tests.Isolation;

/// <summary>
/// <see cref="CameraState.Roll"/>: a rotação da tela em torno da direção de visão, além do azimute e
/// elevação orbitais — como <see cref="StandardViewTests"/> usa para vistas de um referencial girado
/// fora de Z.
/// </summary>
public class CameraRollTests
{
    [Fact]
    public void Up_ComRolagem_GiraEmTornoDaDirecaoDeVisao()
    {
        var camera = new CameraState { Target = Vector3.Zero, Distance = 10, Azimuth = 0, Elevation = 0 };
        var dir = Vector3.Normalize(camera.Position - camera.Target);
        var upSemRolagem = camera.Up;

        camera.Roll = MathF.PI / 2f;
        var upComRolagem = camera.Up;

        // Continua perpendicular à direção de visão e com o mesmo tamanho; só girou em torno dela.
        Vector3.Dot(upComRolagem, dir).Should().BeApproximately(0, 1e-5f);
        upComRolagem.Length().Should().BeApproximately(upSemRolagem.Length(), 1e-5f);
        upComRolagem.Should().NotBe(upSemRolagem);
        // Meia volta (duas rolagens de 90°) devolve o oposto.
        camera.Roll = MathF.PI;
        var upMeiaVolta = camera.Up;
        upMeiaVolta.X.Should().BeApproximately(-upSemRolagem.X, 1e-5f);
        upMeiaVolta.Y.Should().BeApproximately(-upSemRolagem.Y, 1e-5f);
        upMeiaVolta.Z.Should().BeApproximately(-upSemRolagem.Z, 1e-5f);
    }

    [Fact]
    public void Orbit_ZeraARolagemEContinuaOrbitandoNormalmente()
    {
        var camera = new CameraState { Roll = MathF.PI / 3f };

        camera.Orbit(0.2f, -0.1f);

        camera.Roll.Should().Be(0f);
    }

    [Fact]
    public void Pan_ComRolagem_UsaOCimaRoladoParaOsEixosDaTela()
    {
        var camera = new CameraState { Target = Vector3.Zero, Distance = 10, Azimuth = MathF.PI, Elevation = 0, Roll = MathF.PI / 4f };
        var forward = Vector3.Normalize(camera.Target - camera.Position);
        var rightEsperado = Vector3.Normalize(Vector3.Cross(forward, camera.Up));
        var targetAntes = camera.Target;

        camera.Pan(1f, 0f);

        var deslocamento = Vector3.Normalize(camera.Target - targetAntes);
        // Pan(deltaX>0) move o Target para a esquerda da tela (oposto de 'right').
        Vector3.Dot(deslocamento, -rightEsperado).Should().BeApproximately(1f, 1e-4f);
    }

    [Fact]
    public void ScreenPointToRay_NoCentro_ApontaParaOAlvoMesmoComRolagem()
    {
        var camera = new CameraState { Target = new Vector3(5, 0, 0), Distance = 20, Azimuth = 0.7f, Elevation = 0.3f, Roll = MathF.PI / 5f };
        var viewport = new Vector2(800, 600);

        var ray = camera.ScreenPointToRay(viewport / 2f, viewport);

        var expected = Vector3.Normalize(camera.Target - camera.Position);
        Vector3.Dot(Vector3.Normalize(ray.Direction), expected).Should().BeApproximately(1f, 1e-3f);
    }
}
