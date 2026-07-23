using System.CommandLine;
using System.CommandLine.IO;
using System.Text.Json;
using Aether.Cli;
using Aether.Config;
using Aether.Workspace;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aether.Tests;

/// <summary>
/// Tests for `aether provider add` and `aether provider list` CLI commands.
/// Uses --providers-dir + --anima-env options to keep tests hermetic.
/// </summary>
public sealed class ProviderCliTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _aetherDir;
    private readonly string _providersDir;
    private readonly string _animaEnvPath;
    private readonly AgentAuthProfiles _authProfiles;
    private readonly AgentWorkspaceScaffolder _scaffolder;

    public ProviderCliTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"aether_provcli_{Guid.NewGuid():N}");
        _aetherDir = Path.Combine(_tempDir, ".aether");
        Directory.CreateDirectory(_aetherDir);
        _providersDir = Path.Combine(_tempDir, "providers.d");
        Directory.CreateDirectory(_providersDir);
        _animaEnvPath = Path.Combine(_tempDir, "anima.env");

        _authProfiles = new AgentAuthProfiles(_aetherDir, NullLogger<AgentAuthProfiles>.Instance);
        _scaffolder = new AgentWorkspaceScaffolder(NullLogger<AgentWorkspaceScaffolder>.Instance);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private (AetherCli cli, TestConsole console) NewCli()
    {
        var cli = new AetherCli(_aetherDir, _scaffolder, _authProfiles, NullLogger<AetherCli>.Instance);
        return (cli, new TestConsole());
    }

    // --- provider list ---

    [Fact]
    public async Task Provider_list_shows_templates_with_status()
    {
        WriteTemplate("testprov.yaml", """
            id: testprov
            label: "Test Provider"
            api: openai-completions
            baseUrl: https://testprov.example/v1
            apiKey: ${TESTPROV_UNIQ_KEY_8a3f}
            models:
              - id: glm-5.2
            """);
        File.WriteAllText(_animaEnvPath, "TESTPROV_UNIQ_KEY_8a3f=testprov_real\n");

        var (cli, console) = NewCli();
        var cmd = cli.BuildRootCommand();
        var result = await cmd.InvokeAsync(
            $"provider list --providers-dir {_providersDir} --anima-env {_animaEnvPath}", console);

        Assert.Equal(0, result);
        var output = console.Out.ToString() ?? "";
        Assert.Contains("Test Provider", output);
        Assert.Contains("testprov", output);
    }

    [Fact]
    public async Task Provider_list_json_outputs_array()
    {
        WriteTemplate("nahcrof.yaml", """
            id: nahcrof
            label: "Nahcrof AI"
            api: openai-completions
            baseUrl: https://crof.ai/v1
            apiKey: ${NAHCROF_API_KEY}
            models:
              - id: glm-5.2
            """);

        var (cli, console) = NewCli();
        var cmd = cli.BuildRootCommand();
        var result = await cmd.InvokeAsync(
            $"provider list --json --providers-dir {_providersDir}", console);

        Assert.Equal(0, result);
        var output = console.Out.ToString() ?? "";
        using var doc = JsonDocument.Parse(output);
        Assert.Equal(1, doc.RootElement.GetArrayLength());
        Assert.Equal("nahcrof", doc.RootElement[0].GetProperty("id").GetString());
    }

    // --- provider add --mode import ---

    [Fact]
    public async Task Provider_add_import_writes_spec_provider_entry_with_resolved_key()
    {
        // Use a uniquely-named env var unlikely to be in the real shell env, so the test
        // is hermetic and resolves from the fake anima.env rather than process.env.
        WriteTemplate("testprov.yaml", """
            id: testprov
            label: "Test Provider"
            api: openai-completions
            baseUrl: https://testprov.example/v1
            apiKey: ${TESTPROV_UNIQ_KEY_8a3f}
            models:
              - id: glm-4.7-flash
              - id: glm-5.2
            """);
        File.WriteAllText(_animaEnvPath, "TESTPROV_UNIQ_KEY_8a3f=testprov_secret\n");

        var (cli, console) = NewCli();
        var cmd = cli.BuildRootCommand();
        var result = await cmd.InvokeAsync(
            $"provider add --mode import --template testprov --providers-dir {_providersDir} --anima-env {_animaEnvPath}",
            console);

        Assert.Equal(0, result);
        var configPath = Path.Combine(_aetherDir, "config.json");
        Assert.True(File.Exists(configPath));

        var json = await File.ReadAllTextAsync(configPath);
        using var doc = JsonDocument.Parse(json);
        var entry = doc.RootElement.GetProperty("providers").GetProperty("testprov");
        Assert.Equal("openai", entry.GetProperty("type").GetString());
        Assert.Equal("https://testprov.example/v1", entry.GetProperty("base_url").GetString());
        Assert.Equal("testprov_secret", entry.GetProperty("api_key").GetString());
        Assert.Equal("glm-4.7-flash", entry.GetProperty("model").GetString());
    }

    [Fact]
    public async Task Provider_add_import_missing_key_non_interactive_errors_with_hint()
    {
        // Use a uniquely-named env var that is NOT in the real shell env, so the key
        // is genuinely missing and the non-interactive path errors out.
        WriteTemplate("missingprov.yaml", """
            id: missingprov
            label: "Missing Provider"
            api: openai-completions
            baseUrl: https://missingprov.example/v1
            apiKey: ${MISSINGPROV_UNIQ_KEY_2d7c}
            models:
              - id: kimi-k2.6
            """);
        File.WriteAllText(_animaEnvPath, "");

        var (cli, console) = NewCli();
        var cmd = cli.BuildRootCommand();
        var result = await cmd.InvokeAsync(
            $"provider add --mode import --template missingprov --providers-dir {_providersDir} --anima-env {_animaEnvPath} --non-interactive",
            console);

        // non-interactive + missing key → non-zero exit, no config written
        Assert.NotEqual(0, result);
        Assert.False(File.Exists(Path.Combine(_aetherDir, "config.json")));
        var output = console.Out.ToString() ?? "";
        Assert.Contains("MISSINGPROV_UNIQ_KEY_2d7c", output);
    }

    [Fact]
    public async Task Provider_add_import_with_explicit_key_flag_overrides_resolution()
    {
        WriteTemplate("nahcrof.yaml", """
            id: nahcrof
            label: "Nahcrof AI"
            api: openai-completions
            baseUrl: https://crof.ai/v1
            apiKey: ${NAHCROF_API_KEY}
            models:
              - id: glm-5.2
            """);
        File.WriteAllText(_animaEnvPath, "NAHCROF_API_KEY=from-env\n");

        var (cli, console) = NewCli();
        var cmd = cli.BuildRootCommand();
        var result = await cmd.InvokeAsync(
            $"provider add --mode import --template nahcrof --api-key explicit-key --providers-dir {_providersDir} --anima-env {_animaEnvPath}",
            console);

        Assert.Equal(0, result);
        var json = await File.ReadAllTextAsync(Path.Combine(_aetherDir, "config.json"));
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("explicit-key",
            doc.RootElement.GetProperty("providers").GetProperty("nahcrof").GetProperty("api_key").GetString());
    }

    [Fact]
    public async Task Provider_add_import_unsupported_adapter_errors_without_writing()
    {
        WriteTemplate("gemini.yaml", """
            id: gemini
            label: Gemini
            api: generic
            baseUrl: https://generativelanguage.googleapis.com
            apiKey: lit-key
            models:
              - id: gemini-3.1-flash
            """);

        var (cli, console) = NewCli();
        var cmd = cli.BuildRootCommand();
        var result = await cmd.InvokeAsync(
            $"provider add --mode import --template gemini --providers-dir {_providersDir} --anima-env {_animaEnvPath}",
            console);

        Assert.NotEqual(0, result);
        Assert.False(File.Exists(Path.Combine(_aetherDir, "config.json")));
        var output = console.Out.ToString() ?? "";
        Assert.Contains("unsupported", output.ToLower());
    }

    [Fact]
    public async Task Provider_add_import_unknown_template_errors()
    {
        WriteTemplate("nahcrof.yaml", """
            id: nahcrof
            label: "Nahcrof AI"
            api: openai-completions
            baseUrl: https://crof.ai/v1
            apiKey: ${NAHCROF_API_KEY}
            models:
              - id: glm-5.2
            """);

        var (cli, console) = NewCli();
        var cmd = cli.BuildRootCommand();
        var result = await cmd.InvokeAsync(
            $"provider add --mode import --template does-not-exist --providers-dir {_providersDir}",
            console);

        Assert.NotEqual(0, result);
        Assert.False(File.Exists(Path.Combine(_aetherDir, "config.json")));
    }

    // --- provider add --mode raw ---

    [Fact]
    public async Task Provider_add_raw_writes_manual_entry()
    {
        var (cli, console) = NewCli();
        var cmd = cli.BuildRootCommand();
        var result = await cmd.InvokeAsync(
            "provider add --mode raw --name custom-prov " +
            "--url https://custom.example/v1 --type openai " +
            "--api-key sk-raw --models gpt-4o,gpt-4o-mini", console);

        Assert.Equal(0, result);
        var json = await File.ReadAllTextAsync(Path.Combine(_aetherDir, "config.json"));
        using var doc = JsonDocument.Parse(json);
        var entry = doc.RootElement.GetProperty("providers").GetProperty("custom-prov");
        Assert.Equal("openai", entry.GetProperty("type").GetString());
        Assert.Equal("https://custom.example/v1", entry.GetProperty("base_url").GetString());
        Assert.Equal("sk-raw", entry.GetProperty("api_key").GetString());
        Assert.Equal("gpt-4o", entry.GetProperty("model").GetString());
        Assert.Equal(2, entry.GetProperty("models").GetArrayLength());
    }

    [Fact]
    public async Task Provider_add_raw_anthropic_type()
    {
        var (cli, console) = NewCli();
        var cmd = cli.BuildRootCommand();
        var result = await cmd.InvokeAsync(
            "provider add --mode raw --name mm --url https://api.minimax.io/anthropic " +
            "--type anthropic --api-key mm-key --models MiniMax-M2.7", console);

        Assert.Equal(0, result);
        var json = await File.ReadAllTextAsync(Path.Combine(_aetherDir, "config.json"));
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("anthropic",
            doc.RootElement.GetProperty("providers").GetProperty("mm").GetProperty("type").GetString());
    }

    [Fact]
    public async Task Provider_add_raw_requires_name_and_url()
    {
        var (cli, console) = NewCli();
        var cmd = cli.BuildRootCommand();
        var result = await cmd.InvokeAsync("provider add --mode raw --api-key k", console);
        Assert.NotEqual(0, result);
    }

    private void WriteTemplate(string name, string content)
        => File.WriteAllText(Path.Combine(_providersDir, name), content);
}
