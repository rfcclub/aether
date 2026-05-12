using System.Text.Json;

namespace Aether.Ui.Renderers;

public class WebSocketUiRenderer : IUiRenderer
{
    public bool SupportsInteractivity => true;
    public UiLayout[] SupportedLayouts => new[] { UiLayout.List, UiLayout.Paged, UiLayout.Grid };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public object Render(UiDocument doc)
    {
        var sections = doc.Sections.Select(s => new
        {
            title = s.Title,
            subtitle = s.Subtitle,
            items = s.Items.Select(i => new
            {
                id = i.Id,
                label = i.Label,
                emoji = i.Emoji,
                selected = i.Selected,
                statusBadge = i.StatusBadge
            })
        });

        var payload = new
        {
            type = "interactive",
            text = doc.Text,
            sections = sections,
            layout = doc.Layout.ToString().ToLowerInvariant(),
            callbackNamespace = doc.CallbackNamespace
        };

        return JsonSerializer.Serialize(payload, JsonOptions);
    }
}
