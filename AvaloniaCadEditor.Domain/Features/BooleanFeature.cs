using System;
using System.Collections.Generic;

namespace AvaloniaCadEditor.Domain.Features;

public class BooleanFeature : FeatureBase
{
    public BooleanOperationType Operation { get; set; }
    public IFeature BaseFeature { get; set; }
    public List<IFeature> ToolFeatures { get; set; }

    public BooleanFeature(string name, BooleanOperationType operation, IFeature baseFeature, IEnumerable<IFeature> toolFeatures)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Operation = operation;
        BaseFeature = baseFeature ?? throw new ArgumentNullException(nameof(baseFeature));
        ToolFeatures = new List<IFeature>(toolFeatures ?? Array.Empty<IFeature>());

        // Parametricity: Hide the original volumes from the 3D view so they don't overlap with the boolean result
        BaseFeature.IsVisible = false;
        foreach (var tool in ToolFeatures)
        {
            tool.IsVisible = false;
        }
    }

    public void AddToolFeature(IFeature feature)
    {
        if (!ToolFeatures.Contains(feature))
        {
            ToolFeatures.Add(feature);
            feature.IsVisible = false;
        }
    }

    public void RemoveToolFeature(IFeature feature)
    {
        if (ToolFeatures.Remove(feature))
        {
            feature.IsVisible = true;
        }
    }

    public IFeature ChangeBaseFeature(IFeature newBase)
    {
        var oldBase = BaseFeature;
        if (oldBase != null)
        {
            oldBase.IsVisible = true;
        }
        
        BaseFeature = newBase ?? throw new ArgumentNullException(nameof(newBase));
        BaseFeature.IsVisible = false;
        
        return oldBase;
    }

    public override IEnumerable<IFeature> Dependencies
    {
        get
        {
            yield return BaseFeature;
            foreach (var tool in ToolFeatures)
            {
                yield return tool;
            }
        }
    }
}
