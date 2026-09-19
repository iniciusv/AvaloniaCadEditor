using AvaloniaCadEditor.Domain.Entities;
using AvaloniaCadEditor.Domain.ValueObjects;

namespace AvaloniaCadEditor.Domain.Interfaces;

public interface IGeometryEngine
{
    /// <summary>
    /// Calcula a interseção geométrica mais próxima de duas entidades, se houver.
    /// Útil para o comando Trim/Fillet.
    /// </summary>
    IEnumerable<Point3D> GetIntersections(Entity a, Entity b);

    /// <summary>
    /// Encontra pontos de snap em uma entidade (ex: Endpoints, Midpoint, Center).
    /// </summary>
    IEnumerable<Point3D> GetSnapPoints(Entity entity);

    /// <summary>
    /// Encontra a entidade que foi clicada (dentro de uma tolerância).
    /// </summary>
    Entity? HitTest(IEnumerable<Entity> entities, Point3D point, double tolerance);

    /// <summary>
    /// Realiza a operação matemática de aparar (trim) uma entidade.
    /// Remove o segmento da entidade que contém o 'clickPoint', limitado pelas 'intersections'.
    /// Retorna as partes restantes da entidade.
    /// </summary>
    IEnumerable<Entity> TrimEntity(Entity entity, IEnumerable<Point3D> intersections, Point3D clickPoint);

    /// <summary>
    /// Aplica um fillet entre duas entidades dado um raio. 
    /// Retorna as entidades modificadas (as originais encurtadas) e a nova entidade de Arco.
    /// </summary>
    IEnumerable<Entity> FilletEntities(Entity e1, Point3D click1, Entity e2, Point3D click2, double radius);

    /// <summary>
    /// Recebe as entidades 2D do Sketch e gera a representação de um sólido 3D (extrusão).
    /// </summary>
    object Extrude(IEnumerable<Entity> entities, double thickness);
}
