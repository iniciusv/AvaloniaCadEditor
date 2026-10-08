using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using AvaloniaCadEditor.UI.ViewModels;

namespace AvaloniaCadEditor.UI.Reports;

/// <summary>
/// Copia um relatório (guias, cotas) para a área de transferência e avisa na barra de status. Os
/// relatórios em si são texto puro gerado fora da UI (<c>ReferenceGuideReport</c>, <c>DraftingReport</c>);
/// aqui só fica o acesso ao clipboard, que precisa de um <see cref="Visual"/> para achar o TopLevel.
/// </summary>
public static class ReportClipboard
{
    public static async Task CopyAsync(Visual owner, string? text, CadViewModel? vm, string what)
    {
        if (string.IsNullOrEmpty(text))
        {
            if (vm != null) vm.StatusText = $"✗ Copiar {what}: nada para copiar.";
            return;
        }

        var clipboard = TopLevel.GetTopLevel(owner)?.Clipboard;
        if (clipboard == null)
        {
            if (vm != null) vm.StatusText = $"✗ Copiar {what}: área de transferência indisponível.";
            return;
        }

        await clipboard.SetTextAsync(text);
        if (vm != null)
        {
            int lines = text.Split('\n').Length;
            vm.StatusText = $"✓ Copiado: {what} ({lines} linhas).";
        }
    }
}
