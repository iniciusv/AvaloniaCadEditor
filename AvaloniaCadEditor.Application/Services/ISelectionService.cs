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

    /// <summary>
    /// Alterna a marcação de uma ENTIDADE sem desmarcar o resto — o Ctrl+clique numa linha, para
    /// acumular as arestas de um caminho. Não mexe num PERFIL marcado: seção e caminho de uma
    /// varredura são escolhas independentes (ver <c>SweepCommand</c>), e um apaga o outro se este
    /// método limpar. A seleção "primária" continua sendo o perfil, se houver; senão passa a ser
    /// (ou deixa de ser) a entidade.
    /// </summary>
    void ToggleEntity(Sketch sketch, Entity entity);

    /// <summary>
    /// Como o <see cref="SelectMany(Sketch, IEnumerable{IFeature}, IEnumerable{Entity})"/>, mais um
    /// PERFIL que fica marcado junto. É o que permite "esta região E estas arestas" ao mesmo tempo:
    /// a região é a seção de uma varredura e as arestas são o caminho. Quando vem um perfil, ele é
    /// a seleção primária, porque é isso que o resto do app lê (<c>SelectedObject as IProfile</c>).
    /// </summary>
    void SelectMany(Sketch sketch, IEnumerable<IFeature> features, IEnumerable<Entity> entities, IProfile? profile);
}
