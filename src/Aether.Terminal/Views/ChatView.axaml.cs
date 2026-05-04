using System.Collections;
using Avalonia.Controls;
using Avalonia.Threading;

namespace Aether.Terminal.Views;

public partial class ChatView : UserControl
{
    public ChatView()
    {
        InitializeComponent();
    }

    public IEnumerable? ItemsSource
    {
        get => MessagesList.ItemsSource;
        set => MessagesList.ItemsSource = value;
    }

    public void ScrollToEnd()
    {
        Dispatcher.UIThread.Post(() =>
        {
            ChatScroller.ScrollToEnd();
        });
    }
}
