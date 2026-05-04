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

