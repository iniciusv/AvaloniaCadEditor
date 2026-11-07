using System;
using System.Collections.Generic;
using System.Linq;
using AvaloniaCadEditor.Domain.ValueObjects;

namespace AvaloniaCadEditor.Application.EditContext;

/// <summary>
/// Em que nível o usuário está, e portanto o que um clique pode selecionar. Não há seletor
/// Sólido/Face/Aresta: o nível decide.
/// </summary>
public enum SelectionLevel
{
    /// <summary>Nível da cena: só sólidos inteiros.</summary>
    Scene,

    /// <summary>Dentro de uma entidade aberta com duplo clique: as faces dela e os vazios (ferramentas das booleanas).</summary>
    Entity,

    /// <summary>
    /// Editando um esboço: as linhas, círculos e regiões do plano de trabalho. Um clique que não pega nenhum
    /// deles cai no nível de baixo (<see cref="SelectionLevels.Beneath"/>): o esboço é uma camada por cima da
    /// cena ou da entidade aberta, e não a troca por outra coisa.
    /// </summary>
    Sketch
}

/// <summary>
/// Regras puras da seleção por nível. Seleção é o que se edita: dentro de uma entidade, só ela. Referência
/// é outra coisa (snap, plano de trabalho a partir de uma face, projeção) e continua valendo para a cena
/// toda; quem precisa de referência não passa por aqui.
/// </summary>
public static class SelectionLevels
{
    public static SelectionLevel Resolve(bool isSketchEditing, bool isEntityOpen) =>
        isSketchEditing ? SelectionLevel.Sketch
        : isEntityOpen ? SelectionLevel.Entity
        : SelectionLevel.Scene;

    /// <summary>
    /// O nível que decide o que se pega nos SÓLIDOS: o da entidade aberta ou o da cena. No esboço, é o que vale
    /// para um clique fora das linhas e regiões; fora do esboço, é o próprio nível.
    /// </summary>
    public static SelectionLevel Beneath(bool isEntityOpen) =>
        isEntityOpen ? SelectionLevel.Entity : SelectionLevel.Scene;

    /// <summary>
    /// O alvo de um clique, a partir de todos os acertos do raio (do mais perto ao mais longe).
    /// <list type="bullet">
    /// <item>Sem entidade aberta: o acerto mais perto.</item>
    /// <item>Com entidade aberta: só ela e os vazios dela contam; o resto da cena não é selecionável. Um vazio
    /// ganha do corpo mesmo atrás dele (os vazios ficam dentro do corpo, e senão nunca seriam alcançados),
    /// exceto com <paramref name="bodyOnly"/>, para quem quer a face do corpo (o Extrude).</item>
    /// </list>
    /// </summary>
    /// <param name="hits">Acertos do raio, do mais perto ao mais longe.</param>
    /// <param name="openEntityId">A entidade aberta; null no nível da cena.</param>
    /// <param name="openEntityToolIds">Os vazios da entidade aberta, como estão desenhados.</param>
    /// <param name="bodyOnly">Só o corpo da entidade (faces para extrudar), ignorando os vazios.</param>
    public static RaycastHit? Pick(IReadOnlyList<RaycastHit> hits, Guid? openEntityId,
        IReadOnlyCollection<Guid> openEntityToolIds, bool bodyOnly = false)
    {
        ArgumentNullException.ThrowIfNull(hits);
        ArgumentNullException.ThrowIfNull(openEntityToolIds);

        if (openEntityId == null) return hits.Count > 0 ? hits[0] : null;

        if (!bodyOnly)
        {
            foreach (var hit in hits)
            {
                if (hit.FeatureId is Guid id && openEntityToolIds.Contains(id)) return hit;
            }
        }

        foreach (var hit in hits)
        {
            if (hit.FeatureId == openEntityId) return hit;
        }
        return null;
    }

    /// <summary>Os acertos que podem ser selecionados no nível atual (para o ciclo do TAB).</summary>
    public static IReadOnlyList<RaycastHit> Selectable(IReadOnlyList<RaycastHit> hits, Guid? openEntityId,
        IReadOnlyCollection<Guid> openEntityToolIds)
    {
        ArgumentNullException.ThrowIfNull(hits);
        ArgumentNullException.ThrowIfNull(openEntityToolIds);

        if (openEntityId == null) return hits;
        return hits.Where(h => h.FeatureId == openEntityId || (h.FeatureId is Guid id && openEntityToolIds.Contains(id))).ToList();
    }

    /// <summary>Texto curto do nível para a barra lateral (o lugar do antigo seletor).</summary>
    public static string Describe(SelectionLevel level, string? openEntityName) => level switch
    {
        SelectionLevel.Sketch => openEntityName == null
            ? "Esboço: linhas e regiões (fora delas, sólidos)"
            : $"Esboço em {openEntityName}: linhas e regiões (fora delas, faces)",
        SelectionLevel.Entity => $"Dentro de {openEntityName}: faces e vazios",
        _ => "Cena: sólidos inteiros"
    };
}
