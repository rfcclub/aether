using Microsoft.Extensions.Logging;

namespace Aether.Skills;

public class SkillTrigger 
{
    private readonly ILogger<SkillTrigger> _logger;
    private const float AutoThreshold = 0.35f;

    public SkillTrigger(ILogger<SkillTrigger> logger)
    {
        _logger = logger;
    }

    public SkillContext? DetectTrigger(string userMessage, IReadOnlyList<SkillDefinition> skills)
    {
        // Explicit takes priority
        var explicitTrigger = DetectExplicit(userMessage);
        if (explicitTrigger != null)
        {
            _logger.LogDebug("Explicit skill trigger: {SkillName}", explicitTrigger.Skill.Name);
            return explicitTrigger;
        }

        // Fall back to auto detection
        return DetectAuto(userMessage, skills);
    }

    public SkillContext? DetectExplicit(string userMessage)
    {
        // Match /<skill-name> pattern
        if (!userMessage.StartsWith('/'))
        {
            return null;
        }

        var trimmed = userMessage[1..];
        var spaceIdx = trimmed.IndexOf(' ');
        var skillName = spaceIdx > 0 ? trimmed[..spaceIdx] : trimmed;

        if (!string.IsNullOrWhiteSpace(skillName))
        {
            return new SkillContext(
                Skill: new SkillDefinition(skillName, "", "", [], false, ""),
                TriggerReason: "explicit",
                TriggeredAt: DateTime.UtcNow
            );
        }

        return null;
    }

    public SkillContext? DetectAuto(string userMessage, IReadOnlyList<SkillDefinition> skills)
    {
        foreach (var skill in skills)
        {
            var score = ComputeRelevance(userMessage, skill);
            if (score >= AutoThreshold)
            {
                _logger.LogDebug("Auto skill trigger: {SkillName} (score={Score:F2})", skill.Name, score);
                return new SkillContext(
                    Skill: skill,
                    TriggerReason: $"auto: score {score:F2}",
                    TriggeredAt: DateTime.UtcNow
                );
            }
        }

        return null;
    }

    private static float ComputeRelevance(string message, SkillDefinition skill)
    {
        var messageLower = message.ToLowerInvariant();
        var descLower = skill.Description.ToLowerInvariant();
        var whenLower = skill.WhenToUse.ToLowerInvariant();

        var messageWords = messageLower.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var descWords = descLower.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var whenWords = whenLower.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        var matchCount = messageWords.Count(w =>
            descWords.Contains(w) || whenWords.Contains(w));

        if (messageWords.Length == 0) return 0f;
        return (float)matchCount / messageWords.Length;
    }
}