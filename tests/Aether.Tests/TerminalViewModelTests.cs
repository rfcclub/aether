using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Aether.Agent;
using Aether.Agents;
using Aether.Config;
using Aether.Data;
using Aether.Providers;
using Aether.Terminal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Aether.Tests;

public class TerminalViewModelTests
{
    private readonly AetherSoul _soul;
    private readonly GoalStore _goalStore;
    private readonly AgentProfile _profile;
    private readonly ILogger<TerminalViewModel> _logger = NullLogger<TerminalViewModel>.Instance;

    public TerminalViewModelTests()
    {
        var mockLlm = new Mock<ILLMProvider>();
        var options = new SandboxOptions("none", 30000, 512, false, Array.Empty<string>());
        var tools = new Aether.Agent.ToolExecutor(options);
        var agentCfg = new AgentConfig();
        _profile = new AgentProfile("Maria", ".", agentCfg, new AgentModelConfig());
        
        _soul = new AetherSoul(mockLlm.Object, tools, _profile);
        
        var schemaPath = Path.GetFullPath("../../../src/Aether/Data/Schema.sql");
        var db = new AetherDb(":memory:", schemaPath);
        _goalStore = new GoalStore(db);
    }

    [Fact]
    public void Constructor_InitializesCorrectly()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        
        var schemaJson = "{\"agentName\": \"Maria\", \"substrateType\": \"2B\", \"assemblySequence\": [{\"file\": \"test.md\", \"label\": \"Test\", \"panel\": \"Context\"}]}";
        File.WriteAllText(Path.Combine(tempDir, "SUBSTRATE_SCHEMA.json"), schemaJson);
        
        var agentCfg = new AgentConfig();
        var profile = new AgentProfile("Maria", tempDir, agentCfg, new AgentModelConfig());

        // Act
        var vm = new TerminalViewModel(_soul, _goalStore, profile, tempDir, "Maria", "test-model", _logger);

        // Assert
        Assert.Equal("Maria", vm.AgentName);
        Assert.Equal("test-model", vm.ModelName);
        Assert.False(vm.IsThinking);
        Assert.NotEmpty(vm.SubstrateSequence); 
        Directory.Delete(tempDir, true);
    }

    [Fact]
    public void LoadSubstrateSchema_LoadsFromAgentDirectoryIfPresent()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        var agentDir = Path.Combine(tempDir, "agents", "maria");
        Directory.CreateDirectory(agentDir);
        
        var schemaJson = "{\"agentName\": \"Test\", \"substrateType\": \"2B\", \"assemblySequence\": [{\"file\": \"test.md\", \"label\": \"Test\", \"panel\": \"Context\"}]}";
        File.WriteAllText(Path.Combine(agentDir, "SUBSTRATE_SCHEMA.json"), schemaJson);

        var agentCfg = new AgentConfig();
        var profile = new AgentProfile("Maria", tempDir, agentCfg, new AgentModelConfig());

        // Act
        var vm = new TerminalViewModel(_soul, _goalStore, profile, tempDir, "Maria", "model", _logger);

        // Assert
        Assert.NotNull(vm.SubstrateSequence);
        Directory.Delete(tempDir, true);
    }
}
