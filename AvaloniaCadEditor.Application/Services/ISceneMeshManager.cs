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
    RaycastHit? Raycast3D(Sketch sketch, Ray ray, Func<IFeature, bool>? includeFilter = null);

    /// <summary>
    /// Como <see cref="Raycast3D"/>, mas retorna todos os sólidos atingidos pelo raio, ordenados do mais
    /// próximo ao mais distante da câmera - usado para alternar entre sólidos sobrepostos (tecla TAB).
    /// </summary>
    IReadOnlyList<RaycastHit> RaycastAll3D(Sketch sketch, Ray ray, Func<IFeature, bool>? includeFilter = null);
    TriangleMesh BuildCompositeMesh(Sketch sketch, IFeature? previewFeature);
    void ClearCache();

    /// <summary>
    /// Prepara para uma cena recém-executada: esquece as malhas por Id, mas deixa o kernel reaproveitar
    /// geometria idêntica da execução anterior. Para descartar tudo (ex.: mudou a tesselação), use <see cref="ClearCache"/>.
    /// </summary>
    void ResetForNewScene();
}
