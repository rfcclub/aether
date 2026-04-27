using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace Aether.Skills;

public partial class SkillParser : ISkillLoader
{
    private readonly ILogger<SkillParser> _logger;

    // Frontmatter: --- ... --- followed by body
    [GeneratedRegex(@"^---\s*\n(.*?)\n---\s*\n(.*)$", RegexOptions.Singleline)]
    private static partial Regex FrontmatterRegex();

    [GeneratedRegex(@"^\s*[-*]\s*")]
    private static partial Regex ListItemRegex();

    public SkillParser(ILogger<SkillParser> logger)
    {
        _logger = logger;
    }

    public async Task<IReadOnlyList<SkillDefinition>> LoadFromDirectoryAsync(string path, CancellationToken ct = default)
    {
        var skills = new List<SkillDefinition>();

        if (!Directory.Exists(path))
        {
            _logger.LogWarning("Skill directory not found: {Path}", path);
            return skills;
        }

        var files = Directory.GetFiles(path, "*.md", SearchOption.AllDirectories);
        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var content = await File.ReadAllTextAsync(file, ct);
                var skill = ParseSkillFile(file, content);
                if (skill != null)
                {
                    skills.Add(skill);
                    _logger.LogInformation("Loaded skill: {SkillName} from {File}", skill.Name, file);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load skill from {File}", file);
            }
        }

        return skills;
    }

    public SkillDefinition? ParseSkillFile(string path, string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return null;

        var match = FrontmatterRegex().Match(content);
        if (!match.Success)
        {
            _logger.LogWarning("No frontmatter found in {Path}", path);
            return null;
        }

        var frontmatter = match.Groups[1].Value;
        var body = match.Groups[2].Value.Trim();

        var name = ExtractField(frontmatter, "name") ?? Path.GetFileNameWithoutExtension(path);
        var description = ExtractField(frontmatter, "description") ?? "";
        var whenToUse = ExtractField(frontmatter, "when_to_use") ?? "";
        var toolsStr = ExtractField(frontmatter, "tools") ?? "";
        var autoApplyStr = ExtractField(frontmatter, "auto_apply") ?? "false";

        var tools = string.IsNullOrWhiteSpace(toolsStr)
            ? Array.Empty<string>()
            : toolsStr.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var autoApply = bool.TryParse(autoApplyStr, out var val) && val;

        if (string.IsNullOrWhiteSpace(description))
        {
            _logger.LogWarning("Skill missing description in {Path}", path);
            return null;
        }

        return new SkillDefinition(
            Name: name,
            Description: description,
            WhenToUse: whenToUse,
            Tools: tools,
            AutoApply: autoApply,
            Body: body
        );
    }

    private static string? ExtractField(string frontmatter, string field)
    {
        var pattern = $@"^{field}\s*:\s*(.+)$";
        var match = Regex.Match(frontmatter, pattern, RegexOptions.Multiline | RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }
}