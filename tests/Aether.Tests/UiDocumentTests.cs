using Aether.Ui;

namespace Aether.Tests;

public sealed class UiDocumentTests
{
    [Fact]
    public void UiDocument_DefaultValues()
    {
        var doc = new UiDocument();
        Assert.Equal("", doc.Text);
        Assert.Empty(doc.Sections);
        Assert.Equal(UiLayout.List, doc.Layout);
        Assert.Equal("", doc.CallbackNamespace);
    }

    [Fact]
    public void UiDocument_WithSections()
    {
        var doc = new UiDocument
        {
            Text = "Test",
            Sections = new List<UiSection>
            {
                new() { Title = "Section 1", Items = new List<UiItem> { new() { Id = "a", Label = "A" } } }
            },
            Layout = UiLayout.Paged,
            CallbackNamespace = "test"
        };

        Assert.Equal("Test", doc.Text);
        Assert.Single(doc.Sections);
        Assert.Equal("Section 1", doc.Sections[0].Title);
        Assert.Single(doc.Sections[0].Items);
        Assert.Equal("a", doc.Sections[0].Items[0].Id);
        Assert.Equal(UiLayout.Paged, doc.Layout);
        Assert.Equal("test", doc.CallbackNamespace);
    }

    [Fact]
    public void UiItem_HasExpectedProperties()
    {
        var item = new UiItem
        {
            Id = "model-1",
            Label = "Model 1",
            Emoji = "✅",
            Selected = true,
            StatusBadge = "(active)"
        };

        Assert.Equal("model-1", item.Id);
        Assert.Equal("Model 1", item.Label);
        Assert.Equal("✅", item.Emoji);
        Assert.True(item.Selected);
        Assert.Equal("(active)", item.StatusBadge);
    }

    [Fact]
    public void UiDocument_IsImmutable()
    {
        var doc1 = new UiDocument { Text = "A" };
        var doc2 = doc1 with { Text = "B" };

        Assert.Equal("A", doc1.Text);
        Assert.Equal("B", doc2.Text);
        Assert.NotSame(doc1, doc2);
    }

    [Fact]
    public void UiCallback_HasExpectedProperties()
    {
        var cb = new UiCallback
        {
            Namespace = "model",
            Action = "select",
            Data = "fireworks/kimi"
        };

        Assert.Equal("model", cb.Namespace);
        Assert.Equal("select", cb.Action);
        Assert.Equal("fireworks/kimi", cb.Data);
    }

    [Fact]
    public void UiLayout_AllValues()
    {
        Assert.Equal(0, (int)UiLayout.List);
        Assert.Equal(1, (int)UiLayout.Grid);
        Assert.Equal(2, (int)UiLayout.Paged);
    }
}
