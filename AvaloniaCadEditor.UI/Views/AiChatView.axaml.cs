using System.Collections.Specialized;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Threading;
using AvaloniaCadEditor.UI.ViewModels;

namespace AvaloniaCadEditor.UI.Views;

public partial class AiChatView : UserControl
{
    public AiChatView()
    {
        InitializeComponent();

        DataContextChanged += (s, e) =>
        {
            if (DataContext is AiChatViewModel vm)
            {
                vm.Messages.CollectionChanged += OnMessagesChanged;
            }
        };
    }

    private void OnMessagesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        Dispatcher.UIThread.Post(() => MessagesScrollViewer.ScrollToEnd());
    }

    private void OnInputKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            if (DataContext is AiChatViewModel vm && vm.SendCommand.CanExecute(null))
            {
                vm.SendCommand.Execute(null);
            }
            e.Handled = true;
        }
    }

    private async void OnCopyCodeClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control control && control.DataContext is AiChatMessageItem item && item.Code != null)
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel?.Clipboard != null)
            {
                await topLevel.Clipboard.SetTextAsync(item.Code);
            }
        }
    }
}
