using System;

namespace AvaloniaCadEditor.Domain.ValueObjects;

/// <summary>
/// Referencial de leitura de uma peça isolada: orientado pelos eixos da peça (a rotação do
/// <see cref="Placement"/> da instância desfeita) e com origem no canto mínimo da caixa envolvente,
/// de modo que todo o volume fique no octante positivo. É só visual: nada que escreve código usa
/// este referencial — o código continua em coordenadas do mundo.
/// </summary>
public sealed class PartFrame
{
    public PartFrame(string ownerName, Placement frameToWorld)
    {
        OwnerName = ownerName ?? string.Empty;
        FrameToWorld = frameToWorld ?? throw new ArgumentNullException(nameof(frameToWorld));
        WorldToFrame = frameToWorld.Inverse();
    }

    /// <summary>Nome da feature de topo que define o referencial (ex.: "bloco_fixacao_belt").</summary>
    public string OwnerName { get; }

    /// <summary>Leva coordenadas do referencial para o mundo.</summary>
    public Placement FrameToWorld { get; }

    /// <summary>Leva coordenadas do mundo para o referencial.</summary>
    public Placement WorldToFrame { get; }

    public Point3D Origin => FrameToWorld.Apply(Point3D.Origin);
    public Vector3D XAxis => FrameToWorld.ApplyVector(Vector3D.UnitX);
    public Vector3D YAxis => FrameToWorld.ApplyVector(Vector3D.UnitY);
    public Vector3D ZAxis => FrameToWorld.ApplyVector(Vector3D.UnitZ);

    /// <summary>Plano XY do referencial, em coordenadas do mundo (onde a grade é desenhada).</summary>
    public WorkPlane XY => new WorkPlane(Origin, ZAxis, XAxis);

    /// <summary>Rotação do referencial em torno de Z (radianos) — usada para orientar as vistas padrão.</summary>
    public double AzimuthOffset => FrameToWorld.RotationZRadians;

    public Point3D ToWorld(Point3D framePoint) => FrameToWorld.Apply(framePoint);

    public Point3D ToFrame(Point3D worldPoint) => WorldToFrame.Apply(worldPoint);
}
