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
    RaycastHit? Raycast3D(Sketch sketch, Ray ray);
    TriangleMesh BuildCompositeMesh(Sketch sketch, IFeature? previewFeature);
    void ClearCache();
}
