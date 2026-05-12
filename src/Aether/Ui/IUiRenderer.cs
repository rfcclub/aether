namespace Aether.Ui;

public interface IUiRenderer
{
    object Render(UiDocument doc);
    bool SupportsInteractivity { get; }
    UiLayout[] SupportedLayouts { get; }
}
