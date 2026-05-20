using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Metadata;
using Aether.Terminal.Models;

namespace Aether.Terminal.Views;

public class MessageTemplateSelector : IDataTemplate
{
    [Content]
    public Dictionary<ChatRole, IDataTemplate> Templates { get; } = new();

    public Control? Build(object? param)
    {
        if (param is ChatMessage message && Templates.TryGetValue(message.Role, out var template))
        {
            return template.Build(param);
        }
        return null;
    }

    public bool Match(object? data)
    {
        return data is ChatMessage;
    }
}
