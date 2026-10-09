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

    /// <summary>
    /// O perfil marcado, quando há um. Existe à parte de <see cref="SelectedObject"/> porque um
    /// <c>CompoundProfile</c> não está em <c>Sketch.Profiles</c> e não dá para reencontrar por
    /// busca — e é ele que o <see cref="ToggleEntity"/> precisa preservar ao marcar arestas.
    /// </summary>
    private IProfile? _profile;

    public void SelectEntity(Entity? entity)
    {
        _profile = null;
        CurrentSelectionType = entity != null ? SelectionType.Entity : SelectionType.None;
        SelectedId = entity?.Id;
        SelectedObject = entity;

        SelectionChanged?.Invoke();
    }

    public void SelectProfile(IProfile? profile)
    {
        _profile = profile;
        CurrentSelectionType = profile != null ? SelectionType.Profile : SelectionType.None;
        SelectedId = profile?.Id;
        SelectedObject = profile;

        SelectionChanged?.Invoke();
    }

    public void SelectFeature(IFeature? feature)
    {
        _profile = null;
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

        // 0. Search in WorkPlanes
        var wp = sketch.WorkPlanes.FirstOrDefault(w => w.Id == id.Value);
        if (wp != null)
        {
            UnselectAllInSketch(sketch);
            wp.IsSelected = true;
            SelectFeature(wp);
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

    public void ToggleFeature(Sketch sketch, IFeature feature)
    {
        if (feature == null) return;

        if (feature.IsSelected)
        {
            feature.IsSelected = false;

            // A seleção "primária" passa para outra feature ainda marcada, ou fica vazia.
            var stillSelected = sketch.Features.FirstOrDefault(f => f.IsSelected);
            if (stillSelected != null)
            {
                CurrentSelectionType = SelectionType.Feature;
                SelectedId = stillSelected.Id;
                SelectedObject = stillSelected;
            }
            else
            {
                CurrentSelectionType = SelectionType.None;
                SelectedId = null;
                SelectedObject = null;
            }
        }
        else
        {
            feature.IsSelected = true;
            CurrentSelectionType = SelectionType.Feature;
            SelectedId = feature.Id;
            SelectedObject = feature;
        }

        SelectionChanged?.Invoke();
    }

    /// <summary>
    /// Alterna a marcação de uma entidade preservando TUDO o mais, inclusive um perfil marcado.
    /// É o Ctrl+clique numa linha: acumular arestas sem perder a região já escolhida.
    /// </summary>
    public void ToggleEntity(Sketch sketch, Entity entity)
    {
        if (entity == null) return;

        entity.IsSelected = !entity.IsSelected;

        if (_profile != null)
        {
            // A região continua mandando: ela é a seção, as arestas são só o caminho.
            CurrentSelectionType = SelectionType.Profile;
            SelectedId = _profile.Id;
            SelectedObject = _profile;
        }
        else if (entity.IsSelected)
        {
            CurrentSelectionType = SelectionType.Entity;
            SelectedId = entity.Id;
            SelectedObject = entity;
        }
        else
        {
            // Desmarcou a primária: outra entidade ainda marcada assume, senão fica vazio.
            var stillSelected = sketch.Entities.FirstOrDefault(e => e.IsSelected);
            CurrentSelectionType = stillSelected != null ? SelectionType.Entity : SelectionType.None;
            SelectedId = stillSelected?.Id;
            SelectedObject = stillSelected;
        }

        SelectionChanged?.Invoke();
    }

    public void SelectMany(Sketch sketch, IEnumerable<IFeature> features, IEnumerable<Entity> entities) =>
        SelectMany(sketch, features, entities, profile: null);

    public void SelectMany(Sketch sketch, IEnumerable<IFeature> features, IEnumerable<Entity> entities, IProfile? profile)
    {
        UnselectAllInSketch(sketch);

        var featureList = features.ToList();
        var entityList = entities.ToList();
        foreach (var f in featureList) f.IsSelected = true;
        foreach (var e in entityList) e.IsSelected = true;

        _profile = profile;
        if (profile != null)
        {
            profile.IsSelected = true;
            // Um composto não está em Sketch.Profiles: marcar os sub-perfis é o que faz o
            // ProfileFaceRenderPass desenhá-los.
            if (profile is CompoundProfile compound)
                foreach (var sub in compound.SubProfiles) sub.IsSelected = true;

            CurrentSelectionType = SelectionType.Profile;
            SelectedId = profile.Id;
            SelectedObject = profile;
            SelectionChanged?.Invoke();
            return;
        }

        if (featureList.Count > 0)
        {
            CurrentSelectionType = SelectionType.Feature;
            SelectedId = featureList[0].Id;
            SelectedObject = featureList[0];
        }
        else if (entityList.Count > 0)
        {
            CurrentSelectionType = SelectionType.Entity;
            SelectedId = entityList[0].Id;
            SelectedObject = entityList[0];
        }
        else
        {
            CurrentSelectionType = SelectionType.None;
            SelectedId = null;
            SelectedObject = null;
        }

        SelectionChanged?.Invoke();
    }

    public void ClearSelection(Sketch? sketch = null)
    {
        if (sketch != null)
        {
            UnselectAllInSketch(sketch);
        }

        _profile = null;
        CurrentSelectionType = SelectionType.None;
        SelectedId = null;
        SelectedObject = null;

        SelectionChanged?.Invoke();
    }

    private void UnselectAllInSketch(Sketch sketch)
    {
        foreach (var w in sketch.WorkPlanes) w.IsSelected = false;
        foreach (var f in sketch.Features) f.IsSelected = false;
        foreach (var p in sketch.Profiles) p.IsSelected = false;
        foreach (var e in sketch.Entities) e.IsSelected = false;
    }
}
