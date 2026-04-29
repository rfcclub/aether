using Aether.Agents;

namespace Aether.Tests;

public sealed class AgentProfileTests : IDisposable
{
    private readonly string _agentDir;

    public AgentProfileTests()
    {
        _agentDir = Path.Combine(Path.GetTempPath(), $"aether-test-agent-{Guid.NewGuid()}");
        Directory.CreateDirectory(_agentDir);
        Directory.CreateDirectory(Path.Combine(_agentDir, "memory"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_agentDir))
            Directory.Delete(_agentDir, recursive: true);
    }

    [Fact]
    public async Task LoadPersonaAsync_LoadsConfiguredFilesInOrder()
    {
        File.WriteAllText(Path.Combine(_agentDir, "SOUL.md"), "I am Maria.");
        File.WriteAllText(Path.Combine(_agentDir, "USER.md"), "User is Thoor.");

        var config = new AgentConfig { StartupFiles = new() { "SOUL.md", "USER.md" } };
        var profile = new AgentProfile("maria", _agentDir, config);

        var persona = await profile.LoadPersonaAsync();

        Assert.Contains("I am Maria.", persona);
        Assert.Contains("User is Thoor.", persona);
        var soulIndex = persona.IndexOf("I am Maria.");
        var userIndex = persona.IndexOf("User is Thoor.");
        Assert.True(soulIndex < userIndex, "SOUL.md should load before USER.md");
    }

    [Fact]
    public async Task LoadPersonaAsync_SkipsMissingOptionalFiles()
    {
        var config = new AgentConfig { StartupFiles = new() { "SOUL.md", "NONEXISTENT.md" } };
        var profile = new AgentProfile("maria", _agentDir, config);

        var persona = await profile.LoadPersonaAsync();

        Assert.NotNull(persona);
    }

    [Fact]
    public async Task LoadDailyMemoryAsync_LoadsTodayAndYesterday()
    {
        var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
        var yesterday = DateTime.UtcNow.AddDays(-1).ToString("yyyy-MM-dd");
        File.WriteAllText(Path.Combine(_agentDir, "memory", $"{today}.md"), "Today's notes.");
        File.WriteAllText(Path.Combine(_agentDir, "memory", $"{yesterday}.md"), "Yesterday's notes.");

        var profile = new AgentProfile("maria", _agentDir, new AgentConfig());

        var memory = await profile.LoadDailyMemoryAsync();

        Assert.Contains("Today's notes.", memory);
        Assert.Contains("Yesterday's notes.", memory);
    }

    [Fact]
    public async Task LoadFileAsync_ReturnsNullForMissingFile()
    {
        var profile = new AgentProfile("maria", _agentDir, new AgentConfig());

        var result = await profile.LoadFileAsync("NONEXISTENT.md");

        Assert.Null(result);
    }

    [Fact]
    public void Name_ReturnsConfiguredName()
    {
        var profile = new AgentProfile("maria", _agentDir, new AgentConfig());

        Assert.Equal("maria", profile.Name);
    }
}
