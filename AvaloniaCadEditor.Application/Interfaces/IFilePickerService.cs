using System.Threading.Tasks;

namespace AvaloniaCadEditor.Application.Interfaces;

public interface IFilePickerService
{
    Task<string?> OpenFileAsync(string title, string[] extensions);
    Task<string?> SaveFileAsync(string title, string defaultExtension, string defaultFileName);
}
