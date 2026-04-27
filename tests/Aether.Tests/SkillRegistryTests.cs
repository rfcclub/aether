using Aether.Skills;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aether.Tests;

public class SkillRegistryTests
{
    private readonly SkillRegistry _registry = new(NullLogger<SkillRegistry>.Instance);

    [Fact]
    public void Register_AddsSkill()
    {
        var skill = new SkillDefinition("test", "desc", "", Array.Empty<string>(), false, "");
        _registry.Register(skill);

        Assert.True(_registry.HasSkill("test"));
        Assert.Equal(skill, _registry.Resolve("test"));
    }

    [Fact]
    public void Register_DuplicateName_Overwrites()
    {
        var first = new SkillDefinition("test", "first", "", Array.Empty<string>(), false, "");
        var second = new SkillDefinition("test", "second", "", Array.Empty<string>(), false, "");
        _registry.Register(first);
        _registry.Register(second);

        var resolved = _registry.Resolve("test");
        Assert.Equal("second", resolved!.Description);
    }

    [Fact]
    public void Register_EmptyName_Throws()
    {
        var skill = new SkillDefinition("", "desc", "", Array.Empty<string>(), false, "");
        Assert.Throws<ArgumentException>(() => _registry.Register(skill));
    }

    [Fact]
    public void Unregister_RemovesSkill()
    {
        var skill = new SkillDefinition("test", "desc", "", Array.Empty<string>(), false, "");
        _registry.Register(skill);
        _registry.Unregister("test");

        Assert.False(_registry.HasSkill("test"));
        Assert.Null(_registry.Resolve("test"));
    }

    [Fact]
    public void Resolve_MissingSkill_ReturnsNull()
    {
        Assert.Null(_registry.Resolve("nonexistent"));
    }

    [Fact]
    public void List_ReturnsOrderedSkills()
    {
        _registry.Register(new SkillDefinition("b", "desc", "", Array.Empty<string>(), false, ""));
        _registry.Register(new SkillDefinition("a", "desc", "", Array.Empty<string>(), false, ""));
        _registry.Register(new SkillDefinition("c", "desc", "", Array.Empty<string>(), false, ""));

        var list = _registry.List().ToList();
        Assert.Equal(3, list.Count);
        Assert.Equal("a", list[0].Name);
        Assert.Equal("b", list[1].Name);
        Assert.Equal("c", list[2].Name);
    }

    [Fact]
    public void FindMatching_KeywordMatch_ReturnsSkill()
    {
        _registry.Register(new SkillDefinition("pdf", "process PDF documents", "", Array.Empty<string>(), false, ""));
        _registry.Register(new SkillDefinition("image", "edit images", "", Array.Empty<string>(), false, ""));

        var matches = _registry.FindMatching("process pdf documents").ToList();

        Assert.Contains(matches, s => s.Name == "pdf");
    }

    [Fact]
    public void FindMatching_NoMatch_ReturnsEmpty()
    {
        _registry.Register(new SkillDefinition("pdf", "process pdf", "", Array.Empty<string>(), false, ""));

        var matches = _registry.FindMatching("hello world");

        Assert.Empty(matches);
    }
}
