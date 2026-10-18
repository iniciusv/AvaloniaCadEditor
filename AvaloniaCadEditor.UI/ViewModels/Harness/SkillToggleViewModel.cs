using System.ComponentModel;
using System.Runtime.CompilerServices;
using AvaloniaCadEditor.Harness.Skills;

namespace AvaloniaCadEditor.UI.ViewModels.Harness;

/// <summary>Uma skill do harness no painel de configurações: o checkbox liga para toda a conversa.</summary>
public sealed class SkillToggleViewModel : INotifyPropertyChanged
{
    public SkillToggleViewModel(HarnessSkill skill)
    {
        Id = skill.Id;
        Title = skill.Title;
        ToolTip = $"{skill.Description}\nDesligada, dá para usar num pedido só: comece a mensagem com /{skill.Id}.";
    }

    public string Id { get; }
    public string Title { get; }
    public string ToolTip { get; }

    private bool _isEnabled;
    public bool IsEnabled
    {
        get => _isEnabled;
        set { _isEnabled = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
