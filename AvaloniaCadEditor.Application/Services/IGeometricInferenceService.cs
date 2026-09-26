using System.Collections.Generic;
using AvaloniaCadEditor.Domain.Entities;
using AvaloniaCadEditor.Domain.Features;
using AvaloniaCadEditor.Domain.ValueObjects;

namespace AvaloniaCadEditor.Application.Services;

public interface IGeometricInferenceService
{
    /// <summary>
    /// Checks if a world coordinate point coincides with any vertex of existing features in the sketch.
    /// </summary>
    VertexReference? InferVertexReference(Sketch sketch, Point3D worldPoint, double tolerance = 1e-3);

    /// <summary>
    /// Checks if a given work plane matches a face of any existing solid feature in the sketch,
    /// returning a LinkedWorkPlane if found.
    /// </summary>
    LinkedWorkPlane? InferLinkedPlane(Sketch sketch, WorkPlane plane, double tolerance = 1e-3);

    /// <summary>
    /// Checks if a set of points are coplanar with an existing face of a feature in the sketch.
    /// </summary>
    LinkedWorkPlane? InferLinkedPlaneFromPoints(Sketch sketch, IEnumerable<Point3D> worldPoints, double tolerance = 1e-3);
}
