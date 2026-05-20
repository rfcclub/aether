using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Aether.Terminal.Views
{
    public partial class SilhouetteView : UserControl
    {
        public static readonly StyledProperty<bool> IsThinkingProperty =
            AvaloniaProperty.Register<SilhouetteView, bool>(nameof(IsThinking));

        public bool IsThinking
        {
            get => GetValue(IsThinkingProperty);
            set => SetValue(IsThinkingProperty, value);
        }

        public SilhouetteView()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
