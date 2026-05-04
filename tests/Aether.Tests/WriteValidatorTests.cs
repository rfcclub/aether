using Aether.Agents;

namespace Aether.Tests;

public sealed class WriteValidatorTests
{
    [Fact]
    public void Validate_AllowsEpisodicLogWrites()
    {
        var validator = new WriteValidator(new BootConfig());

        var result = validator.ValidateWrite("INTROSPECTION.md", BootLayer.Learning);

        Assert.True(result.Allowed);
        Assert.False(result.RequiresApproval);
    }

    [Fact]
    public void Validate_RequiresCreatorApprovalForConstitution()
    {
        var validator = new WriteValidator(new BootConfig());

        var result = validator.ValidateWrite("AGENTS_GUARD.md", BootLayer.Constitution);

        Assert.False(result.Allowed);
        Assert.True(result.RequiresApproval);
    }

    [Fact]
    public void Validate_AllowsConstitutionRead_Always()
    {
        var validator = new WriteValidator(new BootConfig());

        var result = validator.ValidateRead("AGENTS_GUARD.md", BootLayer.Constitution);

        Assert.True(result.Allowed);
    }

    [Fact]
    public void Validate_LayerFromPath_IdentifiesConstitution()
    {
        var config = new BootConfig
        {
            ConstitutionFiles = new() { "AGENTS_GUARD.md", "AGENTS.md" }
        };

        var layer = WriteValidator.ClassifyPath("AGENTS_GUARD.md", config);

        Assert.Equal(BootLayer.Constitution, layer);
    }

    [Fact]
    public void Validate_LayerFromPath_IdentifiesIdentity()
    {
        var config = new BootConfig
        {
            IdentityFiles = new() { "SOUL.md", "USER.md" }
        };

        var layer = WriteValidator.ClassifyPath("SOUL.md", config);

        Assert.Equal(BootLayer.Identity, layer);
    }
}
