using System;
using System.Collections.Generic;
using AvaloniaCadEditor.Domain.ValueObjects;

namespace AvaloniaCadEditor.Domain.Profiles.Classifiers;

public class RectangleClassifier : IShapeClassifier
{
    private const double Tolerance = 1e-3;

    public ProfileCandidate? Classify(IReadOnlyList<Point3D> loopVertices, IReadOnlyList<Guid> sourceEntityIds, WorkPlane plane)
    {
        if (loopVertices == null || loopVertices.Count != 4)
            return null;

        var p0 = loopVertices[0];
        var p1 = loopVertices[1];
        var p2 = loopVertices[2];
        var p3 = loopVertices[3];

        var e0 = p1 - p0; // Vector p0 -> p1
        var e1 = p2 - p1; // Vector p1 -> p2
        var e2 = p3 - p2; // Vector p2 -> p3
        var e3 = p0 - p3; // Vector p3 -> p0

        double len0 = e0.Length;
        double len1 = e1.Length;
        double len2 = e2.Length;
        double len3 = e3.Length;

        if (len0 < Tolerance || len1 < Tolerance || len2 < Tolerance || len3 < Tolerance)
            return null;

        // Check opposite lengths
        if (Math.Abs(len0 - len2) > Tolerance || Math.Abs(len1 - len3) > Tolerance)
            return null;

        // Check orthogonality (dot product of normalized adjacent edges should be ~0)
        var n0 = e0.Normalize();
        var n1 = e1.Normalize();
        var n2 = e2.Normalize();
        var n3 = e3.Normalize();

        double dot01 = Math.Abs(DotProduct(n0, n1));
        double dot12 = Math.Abs(DotProduct(n1, n2));
        double dot23 = Math.Abs(DotProduct(n2, n3));
        double dot30 = Math.Abs(DotProduct(n3, n0));

        if (dot01 > Tolerance || dot12 > Tolerance || dot23 > Tolerance || dot30 > Tolerance)
            return null;

        // Valid Rectangle!
        double width = len0;
        double height = len1;
        var center = new Point3D((p0.X + p2.X) / 2.0, (p0.Y + p2.Y) / 2.0, (p0.Z + p2.Z) / 2.0);
        double rotationAngle = Math.Atan2(e0.Y, e0.X);

        return new ProfileCandidate(
            ProfileClassification.Rectangle,
            "Retângulo (Paramétrico)",
            specificityRank: 1, // Highest priority
            factory: () => new RectangleProfile(center, width, height, rotationAngle, sourceEntityIds: sourceEntityIds, plane: plane)
        );
    }

    private static double DotProduct(Vector3D a, Vector3D b)
    {
        return a.X * b.X + a.Y * b.Y + a.Z * b.Z;
    }
}
