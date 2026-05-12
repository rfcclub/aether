namespace Aether.Ui.Renderers;

public class TuiUiRenderer : IUiRenderer
{
    public bool SupportsInteractivity => true;
    public UiLayout[] SupportedLayouts => new[] { UiLayout.List, UiLayout.Grid };

    public object Render(UiDocument doc)
    {
        // Stub: Terminal.Gui integration not yet implemented.
        // Returns a plain-text representation for now.
        var sb = new System.Text.StringBuilder();
        sb.AppendLine(doc.Text);
        sb.AppendLine();

        foreach (var section in doc.Sections)
        {
            sb.AppendLine($"[{section.Title}]");
            foreach (var item in section.Items)
            {
                var marker = item.Selected ? " * " : "   ";
                sb.AppendLine($"{marker}{item.Label}");
            }
        }

        return sb.ToString();
    }
}
