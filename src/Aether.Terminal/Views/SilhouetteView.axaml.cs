using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Markup.Xaml;

namespace Aether.Terminal.Views;

public partial class SilhouetteView : UserControl
{
    public static readonly StyledProperty<bool> IsThinkingProperty =
        AvaloniaProperty.Register<SilhouetteView, bool>(nameof(IsThinking));

    public bool IsThinking
    {
        get => GetValue(IsThinkingProperty);
        set => SetValue(IsThinkingProperty, value);
    }

    public static readonly IValueConverter HeatToOpacityConverter =
        new FuncValueConverter<double, double>(heat => Math.Clamp(heat / 300.0, 0.05, 0.4));

    public SilhouetteView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
