using System.Collections.Generic;

namespace AvaloniaCadEditor.Domain.ValueObjects;

/// <summary>
/// De onde veio uma referência do plano de trabalho: o contorno de uma face que está no plano, ou o
/// corte de um sólido que atravessa o plano.
/// </summary>
public enum PlaneReferenceKind
{
    Coplanar,
    Section
}

/// <summary>
/// Geometria de referência transitória de um plano de trabalho: uma polilinha em coordenadas locais do
/// plano (Z = 0), tirada da malha de um sólido visível. Não é entidade do esboço — não é salva nem
/// selecionável; só é desenhada e serve de snap enquanto se desenha no plano.
/// </summary>
/// <param name="Center">Preenchido quando o laço é circular (boca de furo, corte de cilindro).</param>
public sealed record PlaneReference(
    PlaneReferenceKind Kind,
    IReadOnlyList<Point3D> Points,
    bool Closed,
    Point3D? Center = null,
    double? Radius = null)
{
    public bool IsCircle => Center.HasValue && Radius.HasValue;
}
