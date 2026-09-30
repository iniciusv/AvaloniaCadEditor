using System;
using System.Collections.Generic;
using System.Linq;

namespace AvaloniaCadEditor.Domain.Features;

public class BooleanFeature : FeatureBase
{
    public BooleanOperationType Operation { get; set; }
    public List<IFeature> BaseFeatures { get; set; }
    public List<IFeature> ToolFeatures { get; set; }

    /// <summary>
    /// Atalho de leitura para a primeira base. Mantido para compatibilidade com scripts e código
    /// existentes; operações com uma base só (o caso comum) continuam funcionando sem mudanças.
    /// </summary>
    public IFeature BaseFeature => BaseFeatures[0];

    public BooleanFeature(string name, BooleanOperationType operation, IFeature baseFeature, IEnumerable<IFeature> toolFeatures)
        : this(name, operation, new[] { baseFeature }, toolFeatures)
    {
    }

    public BooleanFeature(string name, BooleanOperationType operation, IEnumerable<IFeature> baseFeatures, IEnumerable<IFeature> toolFeatures)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Operation = operation;

        var baseList = new List<IFeature>(baseFeatures ?? Array.Empty<IFeature>());
        if (baseList.Count == 0)
            throw new ArgumentException("Uma BooleanFeature precisa de ao menos uma base.", nameof(baseFeatures));
        if (baseList.Any(b => b == null))
            throw new ArgumentNullException(nameof(baseFeatures));

        BaseFeatures = baseList;
        ToolFeatures = new List<IFeature>(toolFeatures ?? Array.Empty<IFeature>());

        foreach (var b in BaseFeatures)
        {
            PartInstance.EnsureCanBeBooleanOperand(b);
        }
        foreach (var tool in ToolFeatures)
        {
            PartInstance.EnsureCanBeBooleanOperand(tool);
        }

        // Parametricity: Hide the original volumes from the 3D view so they don't overlap with the boolean result
        foreach (var b in BaseFeatures)
        {
            b.IsVisible = false;
        }
        foreach (var tool in ToolFeatures)
        {
            tool.IsVisible = false;
        }
    }

    public static BooleanFeature CreateUnion(string name, IFeature baseFeature, params IFeature[] toolFeatures)
    {
        return new BooleanFeature(name, BooleanOperationType.Union, baseFeature, toolFeatures);
    }

    public static BooleanFeature CreateUnion(string name, IEnumerable<IFeature> baseFeatures, params IFeature[] toolFeatures)
    {
        return new BooleanFeature(name, BooleanOperationType.Union, baseFeatures, toolFeatures);
    }

    public static BooleanFeature CreateDifference(string name, IFeature baseFeature, params IFeature[] toolFeatures)
    {
        return new BooleanFeature(name, BooleanOperationType.Subtract, baseFeature, toolFeatures);
    }

    public static BooleanFeature CreateDifference(string name, IEnumerable<IFeature> baseFeatures, params IFeature[] toolFeatures)
    {
        return new BooleanFeature(name, BooleanOperationType.Subtract, baseFeatures, toolFeatures);
    }

    public static BooleanFeature CreateIntersection(string name, IFeature baseFeature, params IFeature[] toolFeatures)
    {
        return new BooleanFeature(name, BooleanOperationType.Intersect, baseFeature, toolFeatures);
    }

    public static BooleanFeature CreateIntersection(string name, IEnumerable<IFeature> baseFeatures, params IFeature[] toolFeatures)
    {
        return new BooleanFeature(name, BooleanOperationType.Intersect, baseFeatures, toolFeatures);
    }

    public void AddBaseFeature(IFeature feature)
    {
        if (!BaseFeatures.Contains(feature))
        {
            PartInstance.EnsureCanBeBooleanOperand(feature);
            BaseFeatures.Add(feature);
            feature.IsVisible = false;
        }
    }

    public void RemoveBaseFeature(IFeature feature)
    {
        // Uma BooleanFeature sempre precisa de ao menos uma base.
        if (BaseFeatures.Count <= 1) return;

        if (BaseFeatures.Remove(feature))
        {
            feature.IsVisible = true;
        }
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

    /// <summary>
    /// Troca a primeira base (BaseFeatures[0]). Para booleanas com várias bases, use
    /// <see cref="AddBaseFeature"/>/<see cref="RemoveBaseFeature"/> para as demais.
    /// </summary>
    public IFeature ChangeBaseFeature(IFeature newBase)
    {
        PartInstance.EnsureCanBeBooleanOperand(newBase);
        var oldBase = BaseFeatures[0];
        if (oldBase != null)
        {
            oldBase.IsVisible = true;
        }

        BaseFeatures[0] = newBase ?? throw new ArgumentNullException(nameof(newBase));
        BaseFeatures[0].IsVisible = false;

        return oldBase;
    }

    public override IEnumerable<IFeature> Dependencies
    {
        get
        {
            foreach (var b in BaseFeatures)
            {
                yield return b;
            }
            foreach (var tool in ToolFeatures)
            {
                yield return tool;
            }
        }
    }
}
