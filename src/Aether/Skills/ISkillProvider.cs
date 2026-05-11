namespace Aether.Skills;

public interface ISkillProvider
{
    IReadOnlyList<SkillDefinition> GetSkills();
    bool ValidateSkill(SkillDefinition skill, out string? error);
}
