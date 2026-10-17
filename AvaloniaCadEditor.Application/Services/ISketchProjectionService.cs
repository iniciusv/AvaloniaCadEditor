using System.Collections.Generic;
using AvaloniaCadEditor.Domain.Entities;
using AvaloniaCadEditor.Domain.ValueObjects;

namespace AvaloniaCadEditor.Application.Services;

/// <summary>
/// Faces e sólidos que o usuário projetou como referência em cada plano de trabalho do esboço. Vale só pela
/// sessão (não vai para o CadScript): sai e volta com o esboço e acompanha o sólido quando ele muda.
/// </summary>
public interface ISketchProjectionService
{
    /// <summary>Como os sólidos projetados (<see cref="ProjectionTarget.Body"/>) viram referência.</summary>
    VolumeProjectionMode VolumeMode { get; set; }

    /// <summary>Adiciona, se aquela face ou sólido ainda não estiver projetado no plano.</summary>
    void Add(Sketch sketch, WorkPlane plane, ProjectionSource source);

    /// <summary>Tira a face ou o sólido que <paramref name="source"/> aponta (ainda que clicado em outro ponto).</summary>
    void Remove(Sketch sketch, WorkPlane plane, ProjectionSource source);

    void Clear(WorkPlane plane);

    /// <summary>A face (a mesma região, não o mesmo ponto) ou o sólido já está projetado no plano.</summary>
    bool Contains(Sketch sketch, WorkPlane plane, ProjectionSource source);

    bool HasAny(WorkPlane plane);

    /// <summary>
    /// As referências projetadas no plano, calculadas na malha atual de cada feature. Fontes cuja feature
    /// sumiu da cena saem; as de uma feature recriada (cena rodada de novo) são religadas pelo nome.
    /// </summary>
    IReadOnlyList<PlaneReference> ReferencesFor(Sketch sketch, WorkPlane plane);
}
