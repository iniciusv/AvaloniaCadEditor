using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace AvaloniaCadEditor.UI.ViewModels;

/// <summary>
/// Um balão do chat de IA: uma mensagem do usuário, ou uma resposta da IA
/// (com um bloco de CadScript opcional, seu estado de validação, e um comando para aplicá-lo).
/// </summary>
public class AiChatMessageItem : INotifyPropertyChanged
{
    public bool IsUser { get; }
    public string Text { get; }
    public string? Code { get; }
    public string? Reasoning { get; }

    public bool HasCode => !string.IsNullOrWhiteSpace(Code);
    public bool HasReasoning => !string.IsNullOrWhiteSpace(Reasoning);

    private bool? _isValid;

    /// <summary>null = sem código a validar (mensagem do usuário, ou resposta somente texto).</summary>
    public bool? IsValid
    {
        get => _isValid;
        set
        {
            _isValid = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsValidCode));
        }
    }

    /// <summary>Versão não-anulável de <see cref="IsValid"/>, conveniente para bindings de UI (IsEnabled, Classes).</summary>
    public bool IsValidCode => _isValid == true;

    private string? _validationText;
    public string? ValidationText
    {
        get => _validationText;
        set { _validationText = value; OnPropertyChanged(); }
    }

    private bool _isApplied;
    public bool IsApplied
    {
        get => _isApplied;
        set { _isApplied = value; OnPropertyChanged(); }
    }

    public ICommand? ApplyCommand { get; set; }

    public AiChatMessageItem(bool isUser, string text, string? code = null, string? reasoning = null)
    {
        IsUser = isUser;
        Text = text;
        Code = code;
        Reasoning = reasoning;
    }

    /// <summary>Texto completo do balão (mensagem + raciocínio + código) para copiar/colar, útil em debug.</summary>
    public string ToClipboardText(bool includeRole = false)
    {
        var sb = new System.Text.StringBuilder();
        if (includeRole) sb.AppendLine(IsUser ? "### User" : "### Assistant");
        sb.AppendLine(Text);
        if (HasReasoning)
        {
            sb.AppendLine().AppendLine("[Reasoning]").AppendLine(Reasoning);
        }
        if (HasCode)
        {
            sb.AppendLine().AppendLine("```csharp").AppendLine(Code).AppendLine("```");
        }
        return sb.ToString().TrimEnd();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
