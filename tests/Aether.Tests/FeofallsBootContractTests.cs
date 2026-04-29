using Aether.Agents;

namespace Aether.Tests;

public sealed class FeofallsBootContractTests : IDisposable
{
    private readonly string _agentDir;

    public FeofallsBootContractTests()
    {
        _agentDir = Path.Combine(Path.GetTempPath(), $"aether-boot-{Guid.NewGuid()}");
        Directory.CreateDirectory(_agentDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_agentDir))
            Directory.Delete(_agentDir, recursive: true);
    }

    [Fact]
    public async Task LoadConstitutionAsync_LoadsGuardAndAgentsFiles()
    {
        File.WriteAllText(Path.Combine(_agentDir, "AGENTS_GUARD.md"), "Red lines: no exfil.");
        File.WriteAllText(Path.Combine(_agentDir, "AGENTS.md"), "Execute, don't perform.");
        var config = new FeofallsConfig();
        var contract = new FeofallsBootContract(_agentDir, config);

        var constitution = await contract.LoadConstitutionAsync();

        Assert.Contains("no exfil", constitution);
        Assert.Contains("Execute, don't perform", constitution);
    }

    [Fact]
    public async Task LoadConstitutionAsync_SkipsMissingFiles()
    {
        var config = new FeofallsConfig { ConstitutionFiles = new() { "NONEXISTENT.md" } };
        var contract = new FeofallsBootContract(_agentDir, config);

        var constitution = await contract.LoadConstitutionAsync();

        Assert.Equal(string.Empty, constitution);
    }

    [Fact]
    public async Task LoadIdentityAsync_LoadsSoulUserIdentity()
    {
        File.WriteAllText(Path.Combine(_agentDir, "SOUL.md"), "You're Maria.");
        File.WriteAllText(Path.Combine(_agentDir, "USER.md"), "User is Thoor.");
        File.WriteAllText(Path.Combine(_agentDir, "IDENTITY.md"), "Name: Maria");
        var config = new FeofallsConfig();
        var contract = new FeofallsBootContract(_agentDir, config);

        var identity = await contract.LoadIdentityAsync();

        Assert.Contains("You're Maria", identity);
        Assert.Contains("User is Thoor", identity);
        Assert.Contains("Name: Maria", identity);
    }

    [Fact]
    public async Task LoadCognitiveAsync_LoadsMemoryFile()
    {
        File.WriteAllText(Path.Combine(_agentDir, "MEMORY.md"), "Trusted heuristic: always check inbox.");
        var config = new FeofallsConfig();
        var contract = new FeofallsBootContract(_agentDir, config);

        var cognitive = await contract.LoadCognitiveAsync();

        Assert.Contains("Trusted heuristic", cognitive);
    }

    [Fact]
    public async Task LoadWorkingStateAsync_LoadsInboxAndHeartbeat()
    {
        File.WriteAllText(Path.Combine(_agentDir, "TASK_INBOX.md"), "- [ ] Review PR");
        File.WriteAllText(Path.Combine(_agentDir, "HEARTBEAT.md"), "Check inbox");
        var config = new FeofallsConfig();
        var contract = new FeofallsBootContract(_agentDir, config);

        var state = await contract.LoadWorkingStateAsync();

        Assert.Contains("Review PR", state);
        Assert.Contains("Check inbox", state);
    }
}
