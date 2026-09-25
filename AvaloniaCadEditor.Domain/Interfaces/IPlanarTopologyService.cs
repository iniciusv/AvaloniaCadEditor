using System.Collections.Generic;
using AvaloniaCadEditor.Domain.Entities;

namespace AvaloniaCadEditor.Domain.Interfaces;

public interface IPlanarTopologyService
{
    /// <summary>
    /// Computes the planar arrangement of a set of geometric entities.
    /// It finds all intersections, splits the entities at these intersections,
    /// and removes duplicate collinear overlapping segments, returning a clean graph-ready set of entities.
    /// </summary>
    IEnumerable<Entity> ComputePlanarArrangement(IEnumerable<Entity> entities);
}
