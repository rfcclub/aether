using System.Text.Json;

namespace Aether.Skills;

public enum SkillTriggerMode
{
    Explicit,   // /<skill-name>
    Auto,       // description similarity match
    Both
}

public record SkillDefinition(
    string Name,
    string Description,
    string WhenToUse,
    string[] Tools,
    bool AutoApply,
    string Body,
    SkillTriggerMode TriggerMode = SkillTriggerMode.Both
);

public record SkillContext(
    SkillDefinition Skill,
    string TriggerReason,  // "explicit", "auto: cosine similarity 0.82"
    DateTime TriggeredAt
);

public interface ISkillRegistry
{
    void Register(SkillDefinition skill);
    void Unregister(string name);
    SkillDefinition? Resolve(string name);
    IEnumerable<SkillDefinition> List();
    IEnumerable<SkillDefinition> FindMatching(string userMessage);
    bool HasSkill(string name);
}

public interface ISkillLoader
{
    Task<IReadOnlyList<SkillDefinition>> LoadFromDirectoryAsync(string path, CancellationToken ct = default);
    SkillDefinition? ParseSkillFile(string path, string content);
}

public interface ISkillTrigger
{
    SkillContext? DetectTrigger(string userMessage, IReadOnlyList<SkillDefinition> skills);
    SkillContext? DetectExplicit(string userMessage);
    SkillContext? DetectAuto(string userMessage, IReadOnlyList<SkillDefinition> skills);
}