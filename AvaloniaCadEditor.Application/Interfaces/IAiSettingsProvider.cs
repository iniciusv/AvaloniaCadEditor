using AvaloniaCadEditor.Domain.Settings;

namespace AvaloniaCadEditor.Application.Interfaces;

public interface IAiSettingsProvider
{
    AiSettings GetSettings();
    void SaveSettings(AiSettings settings);
}
