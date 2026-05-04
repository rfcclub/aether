using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Aether.Skills;

public class SkillRegistry 
{
    private readonly ConcurrentDictionary<string, SkillDefinition> _skills = new();
    private readonly ILogger<SkillRegistry> _logger;

    public SkillRegistry(ILogger<SkillRegistry> logger)
    {
        _logger = logger;
    }

    public void Register(SkillDefinition skill)
    {
        if (string.IsNullOrWhiteSpace(skill.Name))
            throw new ArgumentException("Skill name cannot be empty", nameof(skill));

        _skills[skill.Name] = skill;
        _logger.LogInformation("Registered skill: {SkillName}", skill.Name);
    }

    public void Unregister(string name)
    {
        if (_skills.TryRemove(name, out _))
        {
            _logger.LogInformation("Unregistered skill: {SkillName}", name);
        }
    }

    public SkillDefinition? Resolve(string name) =>
        _skills.TryGetValue(name, out var skill) ? skill : null;

    public IEnumerable<SkillDefinition> List() => _skills.Values.OrderBy(s => s.Name);

    public IEnumerable<SkillDefinition> FindMatching(string userMessage)
    {
        // Simple keyword overlap matching for auto-trigger
        // Could be enhanced with embeddings/cosine similarity
        foreach (var skill in _skills.Values)
        {
            var score = ComputeRelevance(userMessage, skill);
            if (score >= 0.3f)
            {
                yield return skill;
            }
        }
    }

    public bool HasSkill(string name) => _skills.ContainsKey(name);

    private static float ComputeRelevance(string message, SkillDefinition skill)
    {
        var messageLower = message.ToLowerInvariant();
        var descLower = skill.Description.ToLowerInvariant();
        var whenLower = skill.WhenToUse.ToLowerInvariant();

        // Extract key phrases (nouns, verbs - simple heuristic)
        var messageWords = messageLower.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var descWords = descLower.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var whenWords = whenLower.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        var matchCount = messageWords.Count(w =>
            descWords.Contains(w) || whenWords.Contains(w));

        if (messageWords.Length == 0) return 0f;
        return (float)matchCount / messageWords.Length;
    }
}