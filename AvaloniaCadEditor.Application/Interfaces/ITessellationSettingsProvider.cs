using AvaloniaCadEditor.Domain.Settings;

namespace AvaloniaCadEditor.Application.Interfaces;

public interface ITessellationSettingsProvider
{
    TessellationSettings GetSettings();
    void SaveSettings(TessellationSettings settings);
}
