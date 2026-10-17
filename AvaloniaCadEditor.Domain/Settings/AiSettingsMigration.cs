namespace AvaloniaCadEditor.Domain.Settings;

/// <summary>
/// Leva qualquer <see cref="AiSettings"/> lida do disco para o esquema atual. Idempotente: pode
/// ser aplicada mais de uma vez (quem lê e quem consome as settings aplicam).
/// </summary>
public static class AiSettingsMigration
{
    public static AiSettings Upgrade(AiSettings settings)
    {
        settings.Profiles ??= new List<AiEndpointProfile>();

        if (settings.SchemaVersion < AiSettings.CurrentSchemaVersion)
        {
            // Esquema 0: um único endpoint (DeepSeek) em campos soltos.
            if (settings.Profiles.All(p => p.Id != AiEndpointProfile.DeepSeekId))
            {
                var deepSeek = AiEndpointProfile.DeepSeek();
                deepSeek.ApiKey = settings.ApiKey;
                deepSeek.BaseUrl = settings.BaseUrl;
                deepSeek.Model = settings.Model;
                settings.Profiles.Insert(0, deepSeek);
            }

            if (string.IsNullOrEmpty(settings.ActiveProfileId))
                settings.ActiveProfileId = AiEndpointProfile.DeepSeekId;

            settings.ApiKey = string.Empty;
            settings.BaseUrl = string.Empty;
            settings.Model = string.Empty;
            settings.SchemaVersion = AiSettings.CurrentSchemaVersion;
        }

        if (settings.Profiles.Count == 0)
            settings.Profiles.Add(AiEndpointProfile.DeepSeek());

        if (settings.Profiles.All(p => p.Id != settings.ActiveProfileId))
            settings.ActiveProfileId = settings.Profiles[0].Id;

        return settings;
    }
}
