using System;

namespace AvaloniaCadEditor.Domain.Features;

public interface IFeature
{
    Guid Id { get; }
    string Name { get; set; }
    bool IsEnabled { get; set; }
}
