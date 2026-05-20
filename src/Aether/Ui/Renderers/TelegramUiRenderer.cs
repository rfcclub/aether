using Telegram.Bot.Types.ReplyMarkups;

namespace Aether.Ui.Renderers;

public class TelegramUiRenderer : IUiRenderer
{
    public bool SupportsInteractivity => true;
    public UiLayout[] SupportedLayouts => new[] { UiLayout.List, UiLayout.Paged };

    private const int MaxButtonsPerRow = 1;

    public object Render(UiDocument doc)
    {
        var rows = new List<List<InlineKeyboardButton>>();
        var ns = doc.CallbackNamespace;

        if (doc.Layout == UiLayout.Paged && doc.Sections.Count > 0)
        {
            // Paged: render the current section and add a compact pagination row.
            RenderSection(doc.Sections[0], ns, rows);

            var totalPages = Math.Max(doc.TotalPages, doc.Sections.Count);
            var currentPage = Math.Clamp(doc.PageIndex, 0, totalPages - 1);
            RenderPaginationRow(currentPage, totalPages, ns, doc.PageContext, rows);
        }
        else
        {
            foreach (var section in doc.Sections)
            {
                RenderSection(section, ns, rows);
            }
        }

        var markup = new InlineKeyboardMarkup(rows.Select(r => r.ToArray()));
        return (doc.Text, markup);
    }

    private static void RenderSection(UiSection section, string ns, List<List<InlineKeyboardButton>> rows)
    {
        if (section.Items.Count == 0 && string.IsNullOrWhiteSpace(section.Title))
            return;

        // Section header as a non-clickable button
        var hasSelected = section.Items.Any(i => i.Selected);
        var headerLabel = hasSelected ? $"● {section.Title}" : section.Title;
        if (!string.IsNullOrWhiteSpace(section.Subtitle))
            headerLabel += $" — {section.Subtitle}";
        rows.Add(new List<InlineKeyboardButton>
        {
            InlineKeyboardButton.WithCallbackData(headerLabel, "noop")
        });

        // Item buttons
        foreach (var item in section.Items)
        {
            var label = FormatItemLabel(item);
            var callbackData = string.IsNullOrEmpty(ns)
                ? item.Id
                : $"{ns}:{item.Id}";

            // Clamp callback data to 64 bytes (Telegram limit)
            if (callbackData.Length > 64)
                callbackData = callbackData[..64];

            rows.Add(new List<InlineKeyboardButton>
            {
                InlineKeyboardButton.WithCallbackData(label, callbackData)
            });
        }
    }

    private static string FormatItemLabel(UiItem item)
    {
        var label = item.Label;
        if (item.Selected)
            label = $"✅ {label}";
        else if (!string.IsNullOrWhiteSpace(item.Emoji))
            label = $"{item.Emoji} {label}";

        if (!string.IsNullOrWhiteSpace(item.StatusBadge))
            label = $"{label} {item.StatusBadge}";

        return label;
    }

    private static void RenderPaginationRow(int currentPage, int totalPages, string ns,
        string pageContext, List<List<InlineKeyboardButton>> rows)
    {
        // Page callback data: page:{context}:{pageNum}
        var pagePrefix = string.IsNullOrEmpty(pageContext) ? "page" : $"page:{pageContext}";

        var prevButton = currentPage > 0
            ? InlineKeyboardButton.WithCallbackData("◀️", $"{ns}:{pagePrefix}:{currentPage - 1}")
            : InlineKeyboardButton.WithCallbackData("·", "noop");

        var infoButton = InlineKeyboardButton.WithCallbackData(
            $"Page {currentPage + 1}/{totalPages}", "noop");

        var nextButton = currentPage < totalPages - 1
            ? InlineKeyboardButton.WithCallbackData("▶️", $"{ns}:{pagePrefix}:{currentPage + 1}")
            : InlineKeyboardButton.WithCallbackData("·", "noop");

        rows.Add(new List<InlineKeyboardButton> { prevButton, infoButton, nextButton });
    }
}
