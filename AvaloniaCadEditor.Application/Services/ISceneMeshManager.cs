using System;
using System.Collections.Generic;
using AvaloniaCadEditor.Application.Interfaces;
using AvaloniaCadEditor.Domain.Entities;
using AvaloniaCadEditor.Domain.Features;
using AvaloniaCadEditor.Domain.Mesh;
using AvaloniaCadEditor.Domain.ValueObjects;

namespace AvaloniaCadEditor.Application.Services;

public interface ISceneMeshManager
{
    TriangleMesh GetFeatureMesh(Sketch sketch, Guid featureId, IFeature? previewFeature, bool forceRebuild = false);
    void InvalidateFeatureAndDependents(Sketch sketch, Guid featureId);
    /// <summary>
    /// Malha de uma feature que pode não estar no sketch (ex.: ferramenta de uma peça envolvida num
    /// <see cref="TransformedFeature"/> para aparecer no lugar da instância). Cacheada pelo Id.
    /// </summary>
    TriangleMesh GetMeshFor(IFeature feature);

    /// <param name="extraFeatures">Sólidos fora do sketch que também podem ser atingidos (sempre testados).</param>
    RaycastHit? Raycast3D(Sketch sketch, Ray ray, Func<IFeature, bool>? includeFilter = null, IEnumerable<IFeature>? extraFeatures = null);

    /// <summary>
    /// Como <see cref="Raycast3D"/>, mas retorna todos os sólidos atingidos pelo raio, ordenados do mais
    /// próximo ao mais distante da câmera - usado para alternar entre sólidos sobrepostos (tecla TAB).
    /// </summary>
    IReadOnlyList<RaycastHit> RaycastAll3D(Sketch sketch, Ray ray, Func<IFeature, bool>? includeFilter = null, IEnumerable<IFeature>? extraFeatures = null);
    TriangleMesh BuildCompositeMesh(Sketch sketch, IFeature? previewFeature);
    void ClearCache();

    /// <summary>
    /// Repassa <see cref="Interfaces.IGeometryKernel.BuildProblems"/>: por que a geometria de uma
    /// feature não saiu, por Id. Vazio quando está tudo bem.
    /// </summary>
    IReadOnlyDictionary<Guid, string> BuildProblems { get; }

    /// <summary>
    /// Prepara para uma cena recém-executada: esquece as malhas por Id, mas deixa o kernel reaproveitar
    /// geometria idêntica da execução anterior. Para descartar tudo (ex.: mudou a tesselação), use <see cref="ClearCache"/>.
    /// </summary>
    void ResetForNewScene();

    /// <summary>
    /// Como <see cref="ResetForNewScene"/>, mas constrói as malhas de <paramref name="features"/> fora da
    /// thread da UI e sem esquecer as da cena que ainda está na tela (saem no próximo
    /// <see cref="BuildCompositeMesh"/>, quando o sketch já tem as features novas).
    /// <para>
    /// A cena nova pode ir para a tela antes de a tarefa terminar: enquanto uma feature da lista não fica
    /// pronta, a malha composta a deixa de fora e <see cref="GetFeatureMesh"/>/<see cref="GetMeshFor"/>
    /// devolvem malha vazia sem guardar, em vez de esperar pelo kernel. Cancelado, lança
    /// <see cref="OperationCanceledException"/> e não guarda nada pela metade.
    /// </para>
    /// </summary>
    /// <param name="hidden">O resto da cena nova, que não vai para a tela agora: não é construído, mas a
    /// geometria que já tem no kernel continua em cache para quando voltar (<see cref="PrepareFeaturesAsync"/>).</param>
    System.Threading.Tasks.Task PrepareSceneAsync(IReadOnlyList<IFeature> features, System.Threading.CancellationToken cancellation,
        IReadOnlyList<IFeature>? hidden = null);

    /// <summary>
    /// Features da cena que já está na tela e que voltam a aparecer sem nova execução (ex.: sair do
    /// isolamento do H): as que não têm malha são construídas fora da thread da UI e ficam pendentes até lá,
    /// como em <see cref="PrepareSceneAsync"/>, mas sem recomeçar a cena no kernel.
    /// </summary>
    System.Threading.Tasks.Task PrepareFeaturesAsync(IReadOnlyList<IFeature> features, System.Threading.CancellationToken cancellation);

    /// <summary>
    /// Quantas features do último <see cref="PrepareSceneAsync"/> (e dos <see cref="PrepareFeaturesAsync"/>
    /// depois dele) ainda não têm malha. Uma construção cancelada deixa as dela aqui até a próxima cena:
    /// construí-las na UI travaria a janela.
    /// </summary>
    int PendingCount { get; }

    /// <summary>
    /// Supressão temporária de chanfros e fillets (<see cref="IGeometryKernel.SuppressFillets"/>). As malhas
    /// das duas versões ficam em cache separado: alternar não refaz nada que já foi construído.
    /// </summary>
    bool SuppressFillets { get; set; }
}
