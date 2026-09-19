using CommunityToolkit.Mvvm.ComponentModel;

namespace AvaloniaCadEditor.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    [ObservableProperty]
    public partial string Greeting { get; set; } = "Welcome to Avalonia!";
}
