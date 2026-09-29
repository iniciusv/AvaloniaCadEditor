using Avalonia.Controls;
using Avalonia.Platform.Storage;
using AvaloniaCadEditor.Application.Interfaces;
using System.Linq;
using System.Threading.Tasks;

namespace AvaloniaCadEditor.UI.Services;

public class AvaloniaFilePickerService : IFilePickerService
{
    private readonly Window _window;

    public AvaloniaFilePickerService(Window window)
    {
        _window = window;
    }

    public async Task<string?> OpenFileAsync(string title, string[] extensions)
    {
        var options = new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false
        };

        var result = await _window.StorageProvider.OpenFilePickerAsync(options);
        return result?.FirstOrDefault()?.TryGetLocalPath();
    }

    public async Task<string?> SaveFileAsync(string title, string defaultExtension, string defaultFileName)
    {
        var options = new FilePickerSaveOptions
        {
            Title = title,
            DefaultExtension = defaultExtension,
            SuggestedFileName = defaultFileName
        };

        var result = await _window.StorageProvider.SaveFilePickerAsync(options);
        return result?.TryGetLocalPath();
    }

    public async Task<string?> OpenFolderAsync(string title)
    {
        var options = new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false
        };

        var result = await _window.StorageProvider.OpenFolderPickerAsync(options);
        return result?.FirstOrDefault()?.TryGetLocalPath();
    }
}
