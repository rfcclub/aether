using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aether.Scheduling;

public static class CronFrontmatterParser
{
    private static readonly Regex FrontmatterRegex = new(
        @"^---\s*\n(.*?)\n---\s*\n?(.*)",
        RegexOptions.Singleline | RegexOptions.Compiled);

    public static async Task<CronTaskDefinition?> ParseAsync(string filePath, ILogger? logger = null)
    {
        logger ??= NullLogger.Instance;

        if (!File.Exists(filePath))
            return null;

        var content = await File.ReadAllTextAsync(filePath);
        var match = FrontmatterRegex.Match(content);

        string schedule = "0 * * * *"; // default: hourly
        string agent = "default";
        string channel = "telegram";
        bool enabled = true;
        string body;

        if (match.Success)
        {
            var frontmatter = match.Groups[1].Value;
            body = match.Groups[2].Value.Trim();

            foreach (var line in frontmatter.Split('\n'))
            {
                var colonIdx = line.IndexOf(':');
                if (colonIdx <= 0) continue;

                var key = line[..colonIdx].Trim().ToLowerInvariant();
                var value = line[(colonIdx + 1)..].Trim().Trim('"');

                switch (key)
                {
                    case "schedule": schedule = value; break;
                    case "agent": agent = value; break;
                    case "channel": channel = value; break;
                    case "enabled":
                        enabled = !string.Equals(value, "false", StringComparison.OrdinalIgnoreCase);
                        break;
                }
            }
        }
        else
        {
            body = content.Trim();
            logger.LogWarning("Cron task '{Path}' has no YAML frontmatter, using defaults", Path.GetFileName(filePath));
        }

        if (string.IsNullOrWhiteSpace(body))
        {
            logger.LogWarning("Cron task '{Path}' has empty body, skipping", Path.GetFileName(filePath));
            return null;
        }

        return new CronTaskDefinition(schedule, agent, channel, enabled, body, filePath);
    }
}
