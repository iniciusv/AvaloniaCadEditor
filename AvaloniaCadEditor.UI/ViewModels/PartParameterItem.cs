using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;

namespace AvaloniaCadEditor.UI.ViewModels;

/// <summary>Uma linha editável de parâmetro de peça no inspector (Enter aplica no código).</summary>
public sealed class PartParameterItem : INotifyPropertyChanged
{
    private readonly Func<string, string, Task<string?>> _apply;
    private string _text;
    private string? _error;

    public PartParameterItem(string name, string typeName, string text, bool isEditable, string? hint,
        Func<string, string, Task<string?>> apply)
    {
        Name = name;
        TypeName = typeName;
        _text = text;
        IsEditable = isEditable;
        Hint = hint;
        _apply = apply;
        ApplyCommand = new RelayCommand(async () => await ApplyAsync());
    }

    public string Name { get; }
    public string TypeName { get; }
    public bool IsEditable { get; }

    /// <summary>Intervalo permitido ou, se não editável, a expressão que calcula o valor.</summary>
    public string? Hint { get; }

    public string Text
    {
        get => _text;
        set { _text = value ?? string.Empty; OnPropertyChanged(); }
    }

    public string? Error
    {
        get => _error;
        private set { _error = value; OnPropertyChanged(); }
    }

    public ICommand ApplyCommand { get; }

    public async Task ApplyAsync()
    {
        if (!IsEditable) return;
        Error = await _apply(Name, Text);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
