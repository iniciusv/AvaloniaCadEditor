using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using AvaloniaCadEditor.Application.Interfaces;

namespace AvaloniaCadEditor.Infrastructure.Library;

/// <summary>
/// A biblioteca de peças em disco. O caminho da pasta fica em <c>library_settings.json</c>, no
/// <c>%APPDATA%/AvaloniaCadEditor</c>; sem configuração, é <c>Documentos/AvaloniaCad/Biblioteca</c>. A pasta
/// não é criada só por ler: o app não grava nada nela além do que o usuário manda (o cache de miniaturas
/// fica em outro lugar).
/// </summary>
public sealed class DiskPartLibraryStore : IPartLibraryStore
{
    private static readonly HashSet<string> ExcludedFolders =
        new(StringComparer.OrdinalIgnoreCase) { "bin", "obj", ".git", ".vs", ".cad" };

    private readonly string? _settingsFile;

    /// <summary>Lê e grava o caminho nas configurações do usuário.</summary>
    public DiskPartLibraryStore()
    {
        string appFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AvaloniaCadEditor");
        _settingsFile = Path.Combine(appFolder, "library_settings.json");
        RootPath = ReadSettings() ?? DefaultRootPath;
    }

    /// <summary>Pasta fixa, sem configurações (testes).</summary>
    public DiskPartLibraryStore(string rootPath)
    {
        RootPath = Path.GetFullPath(rootPath);
    }

    public static string DefaultRootPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "AvaloniaCad", "Biblioteca");

    public string RootPath { get; private set; }

    public IReadOnlyList<ScriptDocument> ReadDocuments()
    {
        if (!Directory.Exists(RootPath)) return Array.Empty<ScriptDocument>();

        var documents = new List<ScriptDocument>();
        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(RootPath, "*.cs", SearchOption.AllDirectories).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.WriteLine($"[Biblioteca] Não consegui listar {RootPath}: {ex.Message}");
            return Array.Empty<ScriptDocument>();
        }

        foreach (var file in files)
        {
            string relative = Path.GetRelativePath(RootPath, file).Replace(Path.DirectorySeparatorChar, '/');
            if (relative.Split('/').SkipLast(1).Any(ExcludedFolders.Contains)) continue;
            try
            {
                documents.Add(new ScriptDocument(relative, File.ReadAllText(file)));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Console.WriteLine($"[Biblioteca] Não consegui ler {relative}: {ex.Message}");
            }
        }
        return documents.OrderBy(d => d.FilePath, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public void SetRootPath(string folder)
    {
        RootPath = Path.GetFullPath(folder);
        if (_settingsFile == null) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_settingsFile)!);
            File.WriteAllText(_settingsFile, JsonSerializer.Serialize(new LibrarySettings { FolderPath = RootPath },
                new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.WriteLine($"[Biblioteca] Falha ao salvar a pasta da biblioteca: {ex.Message}");
        }
    }

    private string? ReadSettings()
    {
        if (_settingsFile == null || !File.Exists(_settingsFile)) return null;
        try
        {
            var path = JsonSerializer.Deserialize<LibrarySettings>(File.ReadAllText(_settingsFile))?.FolderPath;
            return string.IsNullOrWhiteSpace(path) ? null : path;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            Console.WriteLine($"[Biblioteca] Falha ao ler library_settings.json: {ex.Message}");
            return null;
        }
    }

    private sealed class LibrarySettings
    {
        public string? FolderPath { get; set; }
    }
}

/// <summary>
/// Miniaturas em <c>%LOCALAPPDATA%/AvaloniaCadEditor/miniaturas</c>, um PNG por chave. Fica fora da pasta da
/// biblioteca para não sujar o git dela.
/// </summary>
public sealed class DiskThumbnailCache : IThumbnailCache
{
    private readonly string _folder;

    public DiskThumbnailCache(string? folder = null)
    {
        _folder = folder ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AvaloniaCadEditor", "miniaturas");
    }

    public byte[]? Get(string key)
    {
        string path = PathOf(key);
        try
        {
            return File.Exists(path) ? File.ReadAllBytes(path) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public void Put(string key, byte[] png)
    {
        try
        {
            Directory.CreateDirectory(_folder);
            File.WriteAllBytes(PathOf(key), png);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.WriteLine($"[Biblioteca] Falha ao gravar miniatura: {ex.Message}");
        }
    }

    private string PathOf(string key) => Path.Combine(_folder, key + ".png");
}
