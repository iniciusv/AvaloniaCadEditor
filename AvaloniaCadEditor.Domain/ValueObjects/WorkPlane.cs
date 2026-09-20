using System.Numerics;

namespace AvaloniaCadEditor.Domain.ValueObjects;

public readonly struct WorkPlane
{
    public Vector3 Normal { get; }
    public Vector3 Origin { get; }

    public WorkPlane(Vector3 normal, Vector3 origin)
    {
        Normal = Vector3.Normalize(normal);
        Origin = origin;
    }

    /// <summary>
    /// Default XY Plane (Z=0) used as the floor.
    /// </summary>
    public static WorkPlane XY => new WorkPlane(Vector3.UnitZ, Vector3.Zero);
}
