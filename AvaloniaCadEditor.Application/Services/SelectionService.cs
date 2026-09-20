using System;
using System.Linq;
using AvaloniaCadEditor.Domain.Entities;
using AvaloniaCadEditor.Domain.Features;
using AvaloniaCadEditor.Domain.Profiles;

namespace AvaloniaCadEditor.Application.Services;

public class SelectionService : ISelectionService
{
    public SelectionType CurrentSelectionType { get; private set; } = SelectionType.None;
    public Guid? SelectedId { get; private set; }
    public object? SelectedObject { get; private set; }

    public event Action? SelectionChanged;

    public void SelectEntity(Entity? entity)
    {
        CurrentSelectionType = entity != null ? SelectionType.Entity : SelectionType.None;
        SelectedId = entity?.Id;
        SelectedObject = entity;

        SelectionChanged?.Invoke();
    }

    public void SelectProfile(IProfile? profile)
    {
        CurrentSelectionType = profile != null ? SelectionType.Profile : SelectionType.None;
        SelectedId = profile?.Id;
        SelectedObject = profile;

        SelectionChanged?.Invoke();
    }

    public void SelectFeature(IFeature? feature)
    {
        CurrentSelectionType = feature != null ? SelectionType.Feature : SelectionType.None;
        SelectedId = feature?.Id;
        SelectedObject = feature;

        SelectionChanged?.Invoke();
    }

    public void SelectById(Sketch sketch, Guid? id)
    {
        if (id == null)
        {
            ClearSelection(sketch);
            return;
        }

        // 1. Search in Features
        var feature = sketch.Features.FirstOrDefault(f => f.Id == id.Value);
        if (feature != null)
        {
            UnselectAllInSketch(sketch);
            feature.IsSelected = true;
            SelectFeature(feature);
            return;
        }

        // 2. Search in Profiles
        var profile = sketch.Profiles.FirstOrDefault(p => p.Id == id.Value);
        if (profile != null)
        {
            UnselectAllInSketch(sketch);
            profile.IsSelected = true;
            SelectProfile(profile);
            return;
        }

        // 3. Search in Entities
        var entity = sketch.Entities.FirstOrDefault(e => e.Id == id.Value);
        if (entity != null)
        {
            UnselectAllInSketch(sketch);
            entity.IsSelected = true;
            SelectEntity(entity);
            return;
        }

        ClearSelection(sketch);
    }

    public void ClearSelection(Sketch? sketch = null)
    {
        if (sketch != null)
        {
            UnselectAllInSketch(sketch);
        }

        CurrentSelectionType = SelectionType.None;
        SelectedId = null;
        SelectedObject = null;

        SelectionChanged?.Invoke();
    }

    private void UnselectAllInSketch(Sketch sketch)
    {
        foreach (var f in sketch.Features) f.IsSelected = false;
        foreach (var p in sketch.Profiles) p.IsSelected = false;
        foreach (var e in sketch.Entities) e.IsSelected = false;
    }
}
