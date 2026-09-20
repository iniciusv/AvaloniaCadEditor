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
}
