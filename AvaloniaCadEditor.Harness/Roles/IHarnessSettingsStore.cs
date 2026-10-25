namespace AvaloniaCadEditor.Harness.Roles;

public interface IHarnessSettingsStore
{
    HarnessSettings Load();
    void Save(HarnessSettings settings);
}
