using Avalonia.Controls;
using Avalonia.Input;

namespace Aether.Terminal;

public partial class MainWindow : Window
{
    private readonly TerminalViewModel _viewModel;

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = null!; // designer-only, never used at runtime
    }

    public MainWindow(TerminalViewModel viewModel) : this()
    {
        _viewModel = viewModel;
        DataContext = viewModel;

        ChatArea.ItemsSource = viewModel.Messages;
        viewModel.Messages.CollectionChanged += (_, _) => ChatArea.ScrollToEnd();
    }

    private void OnInputKeyDown(object? sender, KeyEventArgs e)
    {
        if (_viewModel is null) return;
        if (e.Key == Key.Up)
        {
            _viewModel.NavigateHistory(up: true);
            e.Handled = true;
        }
        else if (e.Key == Key.Down)
        {
            _viewModel.NavigateHistory(up: false);
            e.Handled = true;
        }
    }
}
