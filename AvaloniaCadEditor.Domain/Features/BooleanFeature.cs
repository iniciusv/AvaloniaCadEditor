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

        PartInstance.EnsureCanBeBooleanOperand(BaseFeature);
        foreach (var tool in ToolFeatures)
        {
            PartInstance.EnsureCanBeBooleanOperand(tool);
        }

        // Parametricity: Hide the original volumes from the 3D view so they don't overlap with the boolean result
        BaseFeature.IsVisible = false;
        foreach (var tool in ToolFeatures)
        {
            tool.IsVisible = false;
        }
    }
    
    public static BooleanFeature CreateUnion(string name, IFeature baseFeature, params IFeature[] toolFeatures)
    {
        return new BooleanFeature(name, BooleanOperationType.Union, baseFeature, toolFeatures);
    }
    
    public static BooleanFeature CreateDifference(string name, IFeature baseFeature, params IFeature[] toolFeatures)
    {
        return new BooleanFeature(name, BooleanOperationType.Subtract, baseFeature, toolFeatures);
    }
    
    public static BooleanFeature CreateIntersection(string name, IFeature baseFeature, params IFeature[] toolFeatures)
    {
        return new BooleanFeature(name, BooleanOperationType.Intersect, baseFeature, toolFeatures);
    }

    public void AddToolFeature(IFeature feature)
    {
        if (!ToolFeatures.Contains(feature))
        {
            PartInstance.EnsureCanBeBooleanOperand(feature);
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
        PartInstance.EnsureCanBeBooleanOperand(newBase);
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
