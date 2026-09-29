using System;
using System.IO;
using AvaloniaCadEditor.Application.Scenes;

namespace AvaloniaCadEditor.Tests.Scenes;

/// <summary>Pasta temporária para uma cena de teste; apagada ao descartar.</summary>
public sealed class SceneTestFolder : IDisposable
{
    public SceneTestFolder(string name = "CenaTeste")
    {
        Parent = Path.Combine(Path.GetTempPath(), "avaloniacad-tests", Guid.NewGuid().ToString("N"));
        Root = Path.Combine(Parent, name);
    }

    public string Parent { get; }

    /// <summary>Pasta da cena (ainda não existe até <see cref="CreateScene"/>).</summary>
    public string Root { get; }

    /// <summary>Ferramentas apontando para a pasta de saída dos testes, que contém o Domain.dll.</summary>
    public static SceneToolPaths Tools => new(AppContext.BaseDirectory, "AvaloniaCadEditor.UI.exe");

    public SceneProject CreateScene() => SceneProjectTemplate.Create(Root, Tools);

    public string FullPath(string relativePath) =>
        Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar));

    public void Write(string relativePath, string content)
    {
        string full = FullPath(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Parent)) Directory.Delete(Parent, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Arquivos presos (ex.: watcher ainda fechando) não podem falhar o teste.
        }
    }
}
