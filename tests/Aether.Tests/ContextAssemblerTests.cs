using Aether.Agent;

namespace Aether.Tests;

public class ContextAssemblerTests
{
    private static string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "aether-ctx-test-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void AssembleIdentityContext_LoadsIdentityAndAgents()
    {
        var dir = CreateTempDir();
        File.WriteAllText(Path.Combine(dir, "IDENTITY.md"), "identity content");
        File.WriteAllText(Path.Combine(dir, "AGENTS.md"), "agents content");

        var assembler = new ContextAssembler();
        var result = assembler.AssembleIdentityContext(dir);

        Assert.Contains("identity content", result);
        Assert.Contains("agents content", result);
    }

    [Fact]
    public void AssembleIdentityContext_LoadsAllBootstrapFiles()
    {
        var dir = CreateTempDir();
        File.WriteAllText(Path.Combine(dir, "IDENTITY.md"), "identity-xyz");
        File.WriteAllText(Path.Combine(dir, "AGENTS.md"), "agents-xyz");
        File.WriteAllText(Path.Combine(dir, "SOUL.md"), "soul-xyz");
        File.WriteAllText(Path.Combine(dir, "USER.md"), "user-xyz");
        File.WriteAllText(Path.Combine(dir, "MEMORY.md"), "memory-xyz");

        var assembler = new ContextAssembler();
        var result = assembler.AssembleIdentityContext(dir);

        Assert.Contains("identity-xyz", result);
        Assert.Contains("agents-xyz", result);
        Assert.Contains("soul-xyz", result);
        Assert.Contains("user-xyz", result);
        Assert.Contains("memory-xyz", result);
    }

    [Fact]
    public void AssembleIdentityContext_AgentsBeforeIdentity()
    {
        var dir = CreateTempDir();
        File.WriteAllText(Path.Combine(dir, "AGENTS.md"), "agents-content");
        File.WriteAllText(Path.Combine(dir, "IDENTITY.md"), "identity-content");

        var assembler = new ContextAssembler();
        var result = assembler.AssembleIdentityContext(dir);

        var identityIdx = result.IndexOf("### IDENTITY.md", StringComparison.Ordinal);
        var agentsIdx = result.IndexOf("### AGENTS.md", StringComparison.Ordinal);
        Assert.True(agentsIdx < identityIdx, "AGENTS.md header must come before IDENTITY.md header (priority 10 < 30)");
    }

    [Fact]
    public void AssembleIdentityContext_SkipsMissingFiles()
    {
        var dir = CreateTempDir();
        File.WriteAllText(Path.Combine(dir, "IDENTITY.md"), "identity-def");
        // No AGENTS.md

        var assembler = new ContextAssembler();
        var result = assembler.AssembleIdentityContext(dir);

        Assert.Contains("identity-def", result);
        Assert.DoesNotContain("### AGENTS.md", result);
    }

    // Dynamic context tests — these don't depend on identity file discovery

    [Fact]
    public void AssembleDynamicContext_IncludesWorkingState()
    {
        var dir = CreateTempDir();
        var assembler = new ContextAssembler();
        var result = assembler.AssembleDynamicContext(
            workingState: "in-progress task");

        Assert.Contains("## Working State", result);
        Assert.Contains("in-progress task", result);
    }

    [Fact]
    public void AssembleDynamicContext_IncludesRecentMemory()
    {
        var dir = CreateTempDir();
        var assembler = new ContextAssembler();
        var result = assembler.AssembleDynamicContext(
            recentMemory: "today's log");

        Assert.Contains("## Recent Memory", result);
        Assert.Contains("today's log", result);
    }

    [Fact]
    public void AssembleDynamicContext_IncludesGroupContext()
    {
        var dir = CreateTempDir();
        var assembler = new ContextAssembler();
        var result = assembler.AssembleDynamicContext(
            groupContext: "shared context");

        Assert.Contains("## Group Context", result);
        Assert.Contains("shared context", result);
    }

    [Fact]
    public void AssembleDynamicContext_RespectsTokenBudget()
    {
        var dir = CreateTempDir();
        var assembler = new ContextAssembler(dynamicTokenBudget: 10);
        var longText = new string('x', 500);
        var result = assembler.AssembleDynamicContext(
            groupContext: longText);

        Assert.True(result.Length <= 100, $"Expected truncated, got {result.Length} chars");
        Assert.Contains("[Content truncated", result);
    }

    [Fact]
    public void AssembleDynamicContext_EmptyWhenNoInputs()
    {
        var dir = CreateTempDir();
        var assembler = new ContextAssembler();
        var result = assembler.AssembleDynamicContext();

        Assert.True(string.IsNullOrWhiteSpace(result) || result.Length < 10);
    }
}
