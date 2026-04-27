using Aether.Skills;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aether.Tests;

public class SkillTriggerTests
{
    private readonly SkillTrigger _trigger = new(NullLogger<SkillTrigger>.Instance);

    [Fact]
    public void DetectTrigger_ExplicitSlashCommand_Prioritized()
    {
        var skills = new List<SkillDefinition>
        {
            new("pdf", "process PDF documents", "", Array.Empty<string>(), false, "")
        };

        var context = _trigger.DetectTrigger("/pdf merge these files", skills);

        Assert.NotNull(context);
        Assert.Equal("pdf", context!.Skill.Name);
        Assert.Equal("explicit", context.TriggerReason);
    }

    [Fact]
    public void DetectTrigger_AutoMatch_ReturnsSkill()
    {
        var skills = new List<SkillDefinition>
        {
            new("pdf", "process and merge PDF documents", "when working with pdf files", Array.Empty<string>(), false, "")
        };

        var context = _trigger.DetectTrigger("I need to merge these PDF documents", skills);

        Assert.NotNull(context);
        Assert.Equal("pdf", context!.Skill.Name);
        Assert.StartsWith("auto:", context.TriggerReason);
    }

    [Fact]
    public void DetectTrigger_NoMatch_ReturnsNull()
    {
        var skills = new List<SkillDefinition>
        {
            new("pdf", "handle pdf", "", Array.Empty<string>(), false, "")
        };

        var context = _trigger.DetectTrigger("hello world", skills);

        Assert.Null(context);
    }

    [Fact]
    public void DetectExplicit_SlashCommand_ExtractsName()
    {
        var context = _trigger.DetectExplicit("/my-skill do something");

        Assert.NotNull(context);
        Assert.Equal("my-skill", context!.Skill.Name);
        Assert.Equal("explicit", context.TriggerReason);
    }

    [Fact]
    public void DetectExplicit_NoSlash_ReturnsNull()
    {
        var context = _trigger.DetectExplicit("no slash here");
        Assert.Null(context);
    }

    [Fact]
    public void DetectAuto_MultipleSkills_ReturnsFirstAboveThreshold()
    {
        var skills = new List<SkillDefinition>
        {
            new("unrelated", "something else entirely", "", Array.Empty<string>(), false, ""),
            new("image", "edit and transform images", "", Array.Empty<string>(), false, "")
        };

        var context = _trigger.DetectAuto("edit and transform images", skills);

        Assert.NotNull(context);
        Assert.Equal("image", context!.Skill.Name);
    }
}
