using System.Collections.Generic;
using AvaloniaCadEditor.Domain.ValueObjects;

namespace AvaloniaCadEditor.Application.Commands;

/// <summary>Um segmento de linha guia (ex.: o transferidor do RotateTool), em coordenadas do mundo.</summary>
public sealed record GuideSegment(Point3D From, Point3D To, float R, float G, float B);

/// <summary>
/// Uma ferramenta que desenha linhas guia auxiliares além do preview de entidades normal
/// (<see cref="ICommandTool.GetPreviewEntities"/>) — ex.: o transferidor do RotateTool.
/// </summary>
public interface IGuideProvider
{
    IEnumerable<GuideSegment> GetGuideSegments();
}
