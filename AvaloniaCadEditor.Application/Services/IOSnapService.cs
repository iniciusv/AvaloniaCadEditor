using AvaloniaCadEditor.Domain.Entities;
using AvaloniaCadEditor.Domain.ValueObjects;

namespace AvaloniaCadEditor.Application.Services;

public interface IOSnapService
{
    bool IsEnabled { get; set; }
    OsnapSettings Settings { get; }
    
    /// <summary>
    /// Calcula a melhor coordenada de snap dado o cursor atual e as entidades presentes no sketch.
    /// Se nenhum snap for encontrado dentro da tolerância, retorna o próprio cursor.
    /// </summary>
    /// <param name="solidSnapOnPlaneOnly">
    /// Desenhando sobre uma face (modo de esboço): do sólido, só valem as referências do plano de trabalho
    /// (<c>sketch.Plane</c>) — os contornos das faces nele e o corte dos sólidos que o atravessam. Sem isso, vale
    /// qualquer vértice da malha em 3D, o que Mover/Girar usam para levar um sólido até outro.
    /// </param>
    /// <param name="planeReferences">
    /// Referências já calculadas do plano (contornos coplanares e cortes dos sólidos visíveis, ver
    /// <c>PlaneSection</c>). Nulo: calcula a partir de <paramref name="solidMesh"/>.
    /// </param>
    Point3D GetSnappedPoint(Sketch sketch, Point3D cursor, Point3D? referencePoint, AvaloniaCadEditor.Domain.Mesh.TriangleMesh? solidMesh = null, bool solidSnapOnPlaneOnly = false, IReadOnlyList<PlaneReference>? planeReferences = null);
}
