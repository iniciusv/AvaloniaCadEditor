using System;
using AvaloniaCadEditor.Domain.Entities;
using AvaloniaCadEditor.Domain.Features;
using AvaloniaCadEditor.Domain.Profiles;

namespace AvaloniaCadEditor.Application.Services;

public enum SelectionType
{
    None,
    Entity,
    Profile,
    Feature
}

public interface ISelectionService
{
    SelectionType CurrentSelectionType { get; }
    Guid? SelectedId { get; }
    object? SelectedObject { get; }

    event Action? SelectionChanged;

    void SelectEntity(Entity? entity);
    void SelectProfile(IProfile? profile);
    void SelectFeature(IFeature? feature);
    void SelectById(Sketch sketch, Guid? id);
    void ClearSelection(Sketch? sketch = null);

    /// <summary>
    /// Alterna a marcação (IsSelected) de uma feature sem desmarcar as demais - usado pelo
    /// Ctrl+clique para selecionar vários sólidos de uma vez (ex.: para uma booleana com várias
    /// bases/ferramentas). Se a feature já estava marcada, desmarca; a seleção "primária"
    /// (SelectedObject/SelectedId) passa para outra feature ainda marcada, ou fica vazia.
    /// </summary>
    void ToggleFeature(Sketch sketch, IFeature feature);

    /// <summary>
    /// Substitui a seleção pelas features e entidades dadas (todas ficam com IsSelected) - usado
    /// pela seleção por retângulo. A seleção "primária" é a primeira feature, senão a primeira
    /// entidade, senão fica vazia. Dispara SelectionChanged uma única vez.
    /// </summary>
    void SelectMany(Sketch sketch, IEnumerable<IFeature> features, IEnumerable<Entity> entities);
}
