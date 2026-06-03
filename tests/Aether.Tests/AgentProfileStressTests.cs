using Aether.Agent;
using Aether.Agents;
using Aether.Config;
using Aether.Providers;
using Aether.Tooling;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aether.Tests;

public class AgentProfileStressTests
{
    [Fact]
    public async Task SwitchingProfiles_ShouldNotLeakState()
    {
        // Simulate two different agent profiles
        var profile1 = CreateProfile("agent1", "You are Agent 1");
        var profile2 = CreateProfile("agent2", "You are Agent 2");

        var provider = new FakeLlmProvider("fake", "model", new LlmResponse("ok"));
        var tools = new FakeToolExecutor();

        // If AetherSoul is a Singleton (as currently in Program.cs), 
        // we might be tempted to reuse it. But AetherSoul holds state in _ctx.
        
        var soul1 = new AetherSoul(provider, tools, profile1);
        await soul1.ProcessAsync("g1", "Hello from user 1");

        // Simulate switching to agent 2
        var soul2 = new AetherSoul(provider, tools, profile2);
        await soul2.ProcessAsync("g2", "Hello from user 2");

        // Verify soul 2 doesn't have messages from user 1
        Assert.Equal(3, soul1.Messages.Count); // system + user + assistant
        Assert.Equal(3, soul2.Messages.Count); // system + user + assistant
        Assert.DoesNotContain(soul2.Messages, m => m.Content.Contains("user 1"));
    }

    private static AgentProfile CreateProfile(string name, string identity)
    {
        var dir = Path.Combine(Path.GetTempPath(), "aether_test_" + name);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "IDENTITY.md"), identity);
        return new AgentProfile(name, dir, new AgentConfig(), new AgentModelConfig());
    }
}
