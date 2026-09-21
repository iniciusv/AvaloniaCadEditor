using System;

namespace AvaloniaCadEditor.Domain.Features;

public interface IFeature
{
    Guid Id { get; }
    string Name { get; set; }
    bool IsEnabled { get; set; }
    bool IsVisible { get; set; }
    bool IsSelected { get; set; }
    bool HasError { get; }
    string? ErrorMessage { get; }
}
