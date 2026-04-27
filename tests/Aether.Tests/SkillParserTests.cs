using Aether.Skills;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aether.Tests;

public class SkillParserTests
{
    private readonly SkillParser _parser = new(NullLogger<SkillParser>.Instance);

    [Fact]
    public void ParseSkillFile_WithValidFrontmatter_ReturnsSkill()
    {
        var content = """
            ---
            name: test-skill
            description: A test skill for unit testing
            when_to_use: When user asks for testing
            tools: read,write
            auto_apply: true
            ---
            This is the skill body.
            """;

        var skill = _parser.ParseSkillFile("test.md", content);

        Assert.NotNull(skill);
        Assert.Equal("test-skill", skill!.Name);
        Assert.Equal("A test skill for unit testing", skill.Description);
        Assert.Equal("When user asks for testing", skill.WhenToUse);
        Assert.Equal(new[] { "read", "write" }, skill.Tools);
        Assert.True(skill.AutoApply);
        Assert.Equal("This is the skill body.", skill.Body);
    }

    [Fact]
    public void ParseSkillFile_WithoutName_UsesFileName()
    {
        var content = """
            ---
            description: Some description
            ---
            Body here.
            """;

        var skill = _parser.ParseSkillFile("my-skill.md", content);

        Assert.NotNull(skill);
        Assert.Equal("my-skill", skill!.Name);
    }

    [Fact]
    public void ParseSkillFile_WithoutDescription_ReturnsNull()
    {
        var content = """
            ---
            name: no-desc
            ---
            Body.
            """;

        var skill = _parser.ParseSkillFile("test.md", content);

        Assert.Null(skill);
    }

    [Fact]
    public void ParseSkillFile_WithoutFrontmatter_ReturnsNull()
    {
        var skill = _parser.ParseSkillFile("test.md", "Just some content, no frontmatter.");
        Assert.Null(skill);
    }

    [Fact]
    public void ParseSkillFile_EmptyContent_ReturnsNull()
    {
        var skill = _parser.ParseSkillFile("test.md", "");
        Assert.Null(skill);
    }

    [Fact]
    public void ParseSkillFile_AutoApplyFalse_Default()
    {
        var content = """
            ---
            name: manual-skill
            description: A manual skill
            ---
            Body.
            """;

        var skill = _parser.ParseSkillFile("test.md", content);

        Assert.NotNull(skill);
        Assert.False(skill!.AutoApply);
    }
}
