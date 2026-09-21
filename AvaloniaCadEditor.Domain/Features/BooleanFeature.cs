using System;
using System.Collections.Generic;

namespace AvaloniaCadEditor.Domain.Features;

public class BooleanFeature : IFeature
{
    public Guid Id { get; } = Guid.NewGuid();
    public string Name { get; set; }
    public bool IsEnabled { get; set; } = true;
    public bool IsSelected { get; set; } = false;

    public BooleanOperationType Operation { get; set; }
    public IFeature BaseFeature { get; set; }
    public List<IFeature> ToolFeatures { get; set; }

    public BooleanFeature(string name, BooleanOperationType operation, IFeature baseFeature, IEnumerable<IFeature> toolFeatures)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Operation = operation;
        BaseFeature = baseFeature ?? throw new ArgumentNullException(nameof(baseFeature));
        ToolFeatures = new List<IFeature>(toolFeatures ?? Array.Empty<IFeature>());
    }
}
