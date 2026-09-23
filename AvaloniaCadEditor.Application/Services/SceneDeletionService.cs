using System;
using System.Linq;
using AvaloniaCadEditor.Domain.Entities;
using AvaloniaCadEditor.Domain.Features;

namespace AvaloniaCadEditor.Application.Services;

public class SceneDeletionService : ISceneDeletionService
{
    private readonly ISelectionService _selectionService;

    public SceneDeletionService(ISelectionService selectionService)
    {
        _selectionService = selectionService;
    }

    public void DeleteSelected(Sketch sketch, Guid? modelTreeSelectedId)
    {
        var selectedId = _selectionService.SelectedId ?? modelTreeSelectedId;

        if (selectedId.HasValue)
        {
            var id = selectedId.Value;

            // 1. Check in Entities
            var targetEntity = sketch.Entities.FirstOrDefault(e => e.Id == id);
            if (targetEntity != null)
            {
                sketch.RemoveEntity(targetEntity);
                _selectionService.ClearSelection(sketch);
                return;
            }

            // 2. Check in Features
            var targetFeature = sketch.Features.FirstOrDefault(f => f.Id == id);
            if (targetFeature != null)
            {
                if (targetFeature is WorkPlaneFeature wpf && wpf.IsBasePlane)
                {
                    return; // Cannot delete base plane
                }
                if (targetFeature is BooleanFeature boolF)
                {
                    boolF.BaseFeature.IsVisible = true;
                    foreach (var tool in boolF.ToolFeatures)
                        tool.IsVisible = true;
                }
                sketch.RemoveFeature(targetFeature);
                _selectionService.ClearSelection(sketch);
                return;
            }

            // 3. Check in Profiles
            var targetProfile = sketch.Profiles.FirstOrDefault(p => p.Id == id);
            if (targetProfile != null)
            {
                var entitiesToRemove = sketch.Entities.Where(e => targetProfile.SourceEntityIds.Contains(e.Id)).ToList();
                foreach (var entity in entitiesToRemove)
                {
                    sketch.RemoveEntity(entity);
                }
                sketch.RemoveProfile(targetProfile);
                _selectionService.ClearSelection(sketch);
                return;
            }
        }
        else
        {
            // Check IsSelected flags on Entities, Features, and Profiles
            var selectedEntities = sketch.Entities.Where(e => e.IsSelected).ToList();
            if (selectedEntities.Count > 0)
            {
                foreach (var entity in selectedEntities)
                {
                    sketch.RemoveEntity(entity);
                }
                _selectionService.ClearSelection(sketch);
                return;
            }

            var selectedFeatures = sketch.Features.Where(f => f.IsSelected).ToList();
            if (selectedFeatures.Count > 0)
            {
                foreach (var feature in selectedFeatures)
                {
                    if (feature is WorkPlaneFeature wpf && wpf.IsBasePlane) continue;
                    if (feature is BooleanFeature boolF)
                    {
                        boolF.BaseFeature.IsVisible = true;
                        foreach (var tool in boolF.ToolFeatures)
                            tool.IsVisible = true;
                    }
                    sketch.RemoveFeature(feature);
                }
                _selectionService.ClearSelection(sketch);
                return;
            }

            var selectedProfiles = sketch.Profiles.Where(p => p.IsSelected).ToList();
            if (selectedProfiles.Count > 0)
            {
                foreach (var profile in selectedProfiles)
                {
                    var entitiesToRemove = sketch.Entities.Where(e => profile.SourceEntityIds.Contains(e.Id)).ToList();
                    foreach (var entity in entitiesToRemove)
                    {
                        sketch.RemoveEntity(entity);
                    }
                    sketch.RemoveProfile(profile);
                }
                _selectionService.ClearSelection(sketch);
                return;
            }
        }
    }
}
