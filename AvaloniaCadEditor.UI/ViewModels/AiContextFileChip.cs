using System;
using System.Windows.Input;
using AvaloniaCadEditor.Application.Ai.Context;

namespace AvaloniaCadEditor.UI.ViewModels;

/// <summary>Um arquivo do contexto enviado à IA, mostrado como chip acima da caixa de texto do chat.</summary>
public sealed class AiContextFileChip
{
    public AiContextFileChip(AiContextFile file, bool isPinned, Action<AiContextFileChip> remove, Action<AiContextFileChip> togglePin)
    {
        FilePath = file.FilePath;
        Reason = file.Reason;
        IsPinned = isPinned;
        RemoveCommand = new RelayCommand(() => remove(this));
        TogglePinCommand = new RelayCommand(() => togglePin(this));
        ToolTip = file.Reason switch
        {
            AiContextReason.Entry => $"{FilePath} — scene entry file (always sent)",
            AiContextReason.Active => $"{FilePath} — open in the editor",
            AiContextReason.Selection => $"{FilePath} — selected: {file.Detail}",
            AiContextReason.Dependency => $"{FilePath} — declares {file.Detail}, used by another file in the context",
            _ => $"{FilePath} — added manually"
        };
    }

    public string FilePath { get; }
    public AiContextReason Reason { get; }

    /// <summary>Só o nome do arquivo (o caminho completo fica no tooltip).</summary>
    public string DisplayName => System.IO.Path.GetFileName(FilePath);

    public string ToolTip { get; }
    public bool IsPinned { get; }

    /// <summary>O arquivo de entrada não pode sair do contexto.</summary>
    public bool CanRemove => Reason != AiContextReason.Entry;

    public bool CanPin => Reason != AiContextReason.Entry;

    public ICommand RemoveCommand { get; }
    public ICommand TogglePinCommand { get; }
}
