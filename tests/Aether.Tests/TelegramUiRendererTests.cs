using Aether.Ui;
using Aether.Ui.Renderers;
using Telegram.Bot.Types.ReplyMarkups;

namespace Aether.Tests;

public sealed class TelegramUiRendererTests
{
    private readonly TelegramUiRenderer _renderer = new();

    [Fact]
    public void Render_ConvertsSectionsToButtonRows()
    {
        var doc = new UiDocument
        {
            Text = "Hello",
            Sections = new List<UiSection>
            {
                new()
                {
                    Title = "Section A",
                    Items = new List<UiItem>
                    {
                        new() { Id = "a1", Label = "Item 1" },
                        new() { Id = "a2", Label = "Item 2" }
                    }
                }
            },
            CallbackNamespace = "test"
        };

        var (text, markup) = ((string, InlineKeyboardMarkup))_renderer.Render(doc);

        Assert.Equal("Hello", text);
        Assert.NotNull(markup);
        // Header row + 2 item rows = 3 rows
        Assert.Equal(3, markup.InlineKeyboard.Count());
        // Header is non-clickable (callback = "noop")
        var headerButton = markup.InlineKeyboard.First().First();
        Assert.Contains("Section A", headerButton.Text);
        Assert.Equal("noop", headerButton.CallbackData);
    }

    [Fact]
    public void Render_SelectedItemGetsCheckmark()
    {
        var doc = new UiDocument
        {
            Text = "Select",
            Sections = new List<UiSection>
            {
                new()
                {
                    Title = "Models",
                    Items = new List<UiItem>
                    {
                        new() { Id = "m1", Label = "Model 1", Selected = true },
                        new() { Id = "m2", Label = "Model 2" }
                    }
                }
            },
            CallbackNamespace = "model"
        };

        var (_, markup) = ((string, InlineKeyboardMarkup))_renderer.Render(doc);

        var itemButton = markup.InlineKeyboard.ElementAt(1).First();
        Assert.Contains("✅", itemButton.Text);
        Assert.Contains("Model 1", itemButton.Text);
    }

    [Fact]
    public void Render_ProviderWithSelectedItemGetsIndicator()
    {
        var doc = new UiDocument
        {
            Text = "Providers",
            Sections = new List<UiSection>
            {
                new()
                {
                    Title = "Fireworks",
                    Items = new List<UiItem>
                    {
                        new() { Id = "m1", Label = "Kimi", Selected = true }
                    }
                }
            },
            CallbackNamespace = "model"
        };

        var (_, markup) = ((string, InlineKeyboardMarkup))_renderer.Render(doc);

        var headerButton = markup.InlineKeyboard.First().First();
        Assert.StartsWith("●", headerButton.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_PagedLayout_AddsPaginationRow()
    {
        var doc = new UiDocument
        {
            Text = "Paged",
            Sections = new List<UiSection>
            {
                new() { Title = "Page 1", Items = new List<UiItem> { new() { Id = "a", Label = "A" } } },
                new() { Title = "Page 2", Items = new List<UiItem> { new() { Id = "b", Label = "B" } } },
                new() { Title = "Page 3", Items = new List<UiItem> { new() { Id = "c", Label = "C" } } }
            },
            Layout = UiLayout.Paged,
            CallbackNamespace = "model"
        };

        var (_, markup) = ((string, InlineKeyboardMarkup))_renderer.Render(doc);

        // Header + 1 item + pagination row = 3 rows
        Assert.Equal(3, markup.InlineKeyboard.Count());
        var lastRow = markup.InlineKeyboard.Last();
        Assert.Equal(3, lastRow.Count()); // prev, info, next
        Assert.Equal("·", lastRow.ElementAt(0).Text);           // first page: prev disabled
        Assert.Contains("Page 1/3", lastRow.ElementAt(1).Text); // info
        Assert.Equal("▶️", lastRow.ElementAt(2).Text);           // next enabled
    }

    [Fact]
    public void Render_EmptySection_NoHeaderRow()
    {
        var doc = new UiDocument
        {
            Text = "Empty",
            Sections = new List<UiSection>
            {
                new() { Title = "", Items = new List<UiItem>() }
            },
            CallbackNamespace = "test"
        };

        var (_, markup) = ((string, InlineKeyboardMarkup))_renderer.Render(doc);
        Assert.Empty(markup.InlineKeyboard);
    }

    [Fact]
    public void Render_CallbackDataIncludesNamespace()
    {
        var doc = new UiDocument
        {
            Text = "Test",
            Sections = new List<UiSection>
            {
                new()
                {
                    Title = "Actions",
                    Items = new List<UiItem>
                    {
                        new() { Id = "reset", Label = "Reset" }
                    }
                }
            },
            CallbackNamespace = "model"
        };

        var (_, markup) = ((string, InlineKeyboardMarkup))_renderer.Render(doc);

        var button = markup.InlineKeyboard.ElementAt(1).First();
        Assert.Equal("model:reset", button.CallbackData);
    }

    [Fact]
    public void Render_ItemWithEmoji()
    {
        var doc = new UiDocument
        {
            Text = "Test",
            Sections = new List<UiSection>
            {
                new()
                {
                    Title = "Providers",
                    Items = new List<UiItem>
                    {
                        new() { Id = "p1", Label = "Fireworks", Emoji = "🔥" }
                    }
                }
            },
            CallbackNamespace = "test"
        };

        var (_, markup) = ((string, InlineKeyboardMarkup))_renderer.Render(doc);

        var button = markup.InlineKeyboard.ElementAt(1).First();
        Assert.Contains("🔥", button.Text);
        Assert.Contains("Fireworks", button.Text);
    }

    [Fact]
    public void Render_ItemWithStatusBadge()
    {
        var doc = new UiDocument
        {
            Text = "Test",
            Sections = new List<UiSection>
            {
                new()
                {
                    Title = "Providers",
                    Items = new List<UiItem>
                    {
                        new() { Id = "p1", Label = "Fireworks", StatusBadge = "(3)" }
                    }
                }
            },
            CallbackNamespace = "test"
        };

        var (_, markup) = ((string, InlineKeyboardMarkup))_renderer.Render(doc);

        var button = markup.InlineKeyboard.ElementAt(1).First();
        Assert.Contains("(3)", button.Text);
    }

    [Fact]
    public void Render_SupportsInteractivity()
    {
        Assert.True(_renderer.SupportsInteractivity);
    }

    [Fact]
    public void Render_SupportedLayouts()
    {
        Assert.Contains(UiLayout.List, _renderer.SupportedLayouts);
        Assert.Contains(UiLayout.Paged, _renderer.SupportedLayouts);
    }
}
