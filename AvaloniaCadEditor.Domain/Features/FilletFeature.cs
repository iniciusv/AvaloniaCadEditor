using System;
using System.Collections.Generic;
using AvaloniaCadEditor.Domain.ValueObjects;

namespace AvaloniaCadEditor.Domain.Features;

public class FilletFeature : FeatureBase
{
    public IFeature BaseFeature { get; set; }
    public List<EdgeReference> Edges { get; set; } = new();
    public List<FaceReference> Faces { get; set; } = new();
    public double Radius { get; set; }
    public FilletMode Mode { get; set; } = FilletMode.Round;
    public bool ApplyToAllEdges { get; set; } = false;

    /// <summary>
    /// Deixa sem chanfro/arredondamento as arestas em arco com o material do lado de fora do círculo: a boca
    /// de um furo (de parafuso, por exemplo) e o canto arredondado côncavo de um rebaixo. As arestas externas
    /// continuam. Vale para arcos reconhecidos na malha; um furo que não sai num plano perpendicular a ele
    /// (inclinado, numa face curva) não é reconhecido como arco e continua sendo chanfrado.
    /// </summary>
    public bool SkipHoleEdges { get; set; } = false;

    public FilletFeature(string name, IFeature baseFeature, double radius)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        BaseFeature = baseFeature ?? throw new ArgumentNullException(nameof(baseFeature));
        Radius = radius;

        // Hide base feature from 3D view so it is replaced by this fillet result
        BaseFeature.IsVisible = false;
    }

    public void AddEdge(EdgeReference edge)
    {
        if (!Edges.Contains(edge))
            Edges.Add(edge);
    }

    public void AddFace(FaceReference face)
    {
        if (!Faces.Contains(face))
            Faces.Add(face);
    }

    public override IEnumerable<IFeature> Dependencies
    {
        get
        {
            yield return BaseFeature;
        }
    }
}
