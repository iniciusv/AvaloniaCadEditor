namespace AvaloniaCadEditor.Application.Interfaces;

/// <summary>Lembra a pasta da última cena aberta, para oferecer "carregar a última" numa próxima inicialização.</summary>
public interface ILastSceneProvider
{
    string? GetLastScenePath();
    void SetLastScenePath(string folderPath);
}
