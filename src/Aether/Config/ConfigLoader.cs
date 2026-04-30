using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Aether.Config;

public sealed class ConfigLoader
{
    private readonly IConfiguration _configuration;
    private readonly string _aetherDir;
    private readonly ILogger<ConfigLoader> _logger;
    private readonly AgentAuthProfiles? _authProfiles;

    private AetherAppConfig? _cachedConfig;
    private Dictionary<string, AgentEntryConfig>? _cachedAgents;

    public ConfigLoader(IConfiguration configuration, string aetherDir, ILogger<ConfigLoader> logger, AgentAuthProfiles? authProfiles = null)
    {
        _configuration = configuration;
        _aetherDir = aetherDir;
        _logger = logger;
        _authProfiles = authProfiles;
    }

    public async Task<AetherAppConfig> LoadAsync(
        string? agentName = null,
        Dictionary<string, string>? cliOverrides = null,
        CancellationToken ct = default)
    {
        if (_cachedConfig is not null && agentName is null && cliOverrides is null)
            return _cachedConfig;

        var providers = new ProviderSection();
        var channelDefaults = new ChannelSection();
        var sandbox = new SandboxSection();

        // Layer 1: Framework defaults from appsettings.json (via IConfiguration)
        providers = ApplyFrameworkDefaults(providers);

        // Layer 2: Global user config ~/.aether/config.json
        providers = await ApplyGlobalConfigAsync(providers, ct);

        // Layer 3: Agent-specific <workspace>/.aether.json
        if (agentName is not null)
            providers = await ApplyAgentConfigAsync(providers, agentName, ct);

        // Layer 3.5: Per-agent auth profiles override global provider config
        if (agentName is not null && _authProfiles is not null)
            providers = await ApplyAuthProfilesAsync(providers, agentName, ct);

        // Layer 4: Environment variables
        providers = ApplyEnvOverrides(providers);

        // Layer 5: CLI flags
        if (cliOverrides is not null)
            providers = ApplyCliOverrides(providers, cliOverrides);

        // Resolve meta and wizard from global config
        var meta = new MetaSection();
        var wizard = new WizardSection();
        (meta, wizard) = await LoadMetaAsync(ct);

        // Resolve agents
        var agents = await ResolveAgentsAsync(ct);

        var config = new AetherAppConfig
        {
            Providers = providers,
            ChannelDefaults = channelDefaults,
            Sandbox = sandbox,
            Agents = agents,
            Meta = meta,
            Wizard = wizard
        };

        if (agentName is null && cliOverrides is null)
            _cachedConfig = config;
        return config;
    }

    public AgentEntryConfig? GetAgentConfig(string name)
    {
        if (_cachedAgents is null) return null;
        return _cachedAgents.TryGetValue(name, out var entry) ? entry : null;
    }

    private ProviderSection ApplyFrameworkDefaults(ProviderSection providers)
    {
        return providers with
        {
            OpenRouter = providers.OpenRouter with
            {
                ApiKey = _configuration["llm:api_key"] ?? "",
                Model = _configuration["llm:model"] ?? "",
                BaseUrl = _configuration["llm:base_url"] ?? "https://openrouter.ai/api/v1",
                TimeoutSeconds = int.TryParse(_configuration["llm:timeout_seconds"], out var t) ? t : 90
            },
            Anthropic = providers.Anthropic with
            {
                ApiKey = _configuration["anthropic:api_key"] ?? "",
                Model = _configuration["anthropic:model"] ?? "claude-3-5-sonnet-20241022"
            },
            Fireworks = providers.Fireworks with
            {
                ApiKey = _configuration["fireworks:api_key"] ?? "",
                Model = _configuration["fireworks:model"] ?? "accounts/fireworks/models/deepseek-v3-0324"
            }
        };
    }

    private async Task<ProviderSection> ApplyGlobalConfigAsync(ProviderSection providers, CancellationToken ct)
    {
        var configPath = Path.Combine(_aetherDir, "config.json");
        if (!File.Exists(configPath)) return providers;

        try
        {
            var json = await File.ReadAllTextAsync(configPath, ct);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("llm", out var llm))
            {
                if (llm.TryGetProperty("api_key", out var ak))
                    providers = providers with { OpenRouter = providers.OpenRouter with { ApiKey = ak.GetString() ?? "" } };
                if (llm.TryGetProperty("model", out var m))
                    providers = providers with { OpenRouter = providers.OpenRouter with { Model = m.GetString() ?? "" } };
                if (llm.TryGetProperty("base_url", out var bu))
                    providers = providers with { OpenRouter = providers.OpenRouter with { BaseUrl = bu.GetString() ?? "" } };
                if (llm.TryGetProperty("timeout_seconds", out var ts) && ts.TryGetInt32(out var tsv))
                    providers = providers with { OpenRouter = providers.OpenRouter with { TimeoutSeconds = tsv } };
            }

            if (root.TryGetProperty("anthropic", out var anthro))
            {
                if (anthro.TryGetProperty("api_key", out var ak))
                    providers = providers with { Anthropic = providers.Anthropic with { ApiKey = ak.GetString() ?? "" } };
            }

            if (root.TryGetProperty("fireworks", out var fw))
            {
                if (fw.TryGetProperty("api_key", out var ak))
                    providers = providers with { Fireworks = providers.Fireworks with { ApiKey = ak.GetString() ?? "" } };
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load ~/.aether/config.json");
        }

        return providers;
    }

    private async Task<ProviderSection> ApplyAgentConfigAsync(ProviderSection providers, string agentName, CancellationToken ct)
    {
        var configPath = Path.Combine(_aetherDir, "config.json");
        string? workspacePath = null;
        if (File.Exists(configPath))
        {
            try
            {
                var json = await File.ReadAllTextAsync(configPath, ct);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("agents", out var agents) &&
                    agents.TryGetProperty(agentName, out var agent) &&
                    agent.TryGetProperty("workspace", out var ws))
                {
                    workspacePath = ws.GetString();
                }
            }
            catch { }
        }

        if (workspacePath is null)
            workspacePath = Path.Combine(_aetherDir, "workspaces", agentName);

        var agentConfigPath = Path.Combine(workspacePath, ".aether.json");
        if (!File.Exists(agentConfigPath)) return providers;

        try
        {
            var json = await File.ReadAllTextAsync(agentConfigPath, ct);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("llm", out var llm))
            {
                if (llm.TryGetProperty("api_key", out var ak))
                    providers = providers with { OpenRouter = providers.OpenRouter with { ApiKey = ak.GetString() ?? "" } };
                if (llm.TryGetProperty("model", out var m))
                    providers = providers with { OpenRouter = providers.OpenRouter with { Model = m.GetString() ?? "" } };
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load agent config for {Agent}", agentName);
        }

        return providers;
    }

    private async Task<ProviderSection> ApplyAuthProfilesAsync(ProviderSection providers, string agentName, CancellationToken ct)
    {
        try
        {
            var auth = await _authProfiles!.LoadAuthProfilesAsync(agentName, ct);

            if (!string.IsNullOrEmpty(auth.State.ActiveModel))
                providers = providers with { OpenRouter = providers.OpenRouter with { Model = auth.State.ActiveModel } };

            if (auth.State.ActiveProvider is not null && auth.Profiles.TryGetValue(auth.State.ActiveProvider, out var profile))
            {
                if (!string.IsNullOrEmpty(profile.ApiKey))
                    providers = providers with { OpenRouter = providers.OpenRouter with { ApiKey = profile.ApiKey } };
            }

            if (!string.IsNullOrEmpty(auth.Model.Primary))
                providers = providers with { OpenRouter = providers.OpenRouter with { Model = auth.Model.Primary } };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load auth profiles for {Agent}", agentName);
        }

        return providers;
    }

    private static ProviderSection ApplyEnvOverrides(ProviderSection providers)
    {
        // Only check actual environment variables, not IConfiguration
        // (IConfiguration includes appsettings.json values)
        var envApiKey = Environment.GetEnvironmentVariable("AETHER_llm__api_key")
                        ?? Environment.GetEnvironmentVariable("AETHER_llm_api_key");
        if (!string.IsNullOrEmpty(envApiKey))
            providers = providers with { OpenRouter = providers.OpenRouter with { ApiKey = envApiKey } };

        var envModel = Environment.GetEnvironmentVariable("AETHER_llm__model")
                       ?? Environment.GetEnvironmentVariable("AETHER_llm_model");
        if (!string.IsNullOrEmpty(envModel))
            providers = providers with { OpenRouter = providers.OpenRouter with { Model = envModel } };

        var envTimeout = Environment.GetEnvironmentVariable("AETHER_llm__timeout_seconds")
                         ?? Environment.GetEnvironmentVariable("AETHER_llm_timeout_seconds");
        if (int.TryParse(envTimeout, out var t))
            providers = providers with { OpenRouter = providers.OpenRouter with { TimeoutSeconds = t } };

        return providers;
    }

    private static ProviderSection ApplyCliOverrides(ProviderSection providers, Dictionary<string, string> cliOverrides)
    {
        if (cliOverrides.TryGetValue("llm:model", out var model))
            providers = providers with { OpenRouter = providers.OpenRouter with { Model = model } };
        if (cliOverrides.TryGetValue("llm:api_key", out var key))
            providers = providers with { OpenRouter = providers.OpenRouter with { ApiKey = key } };
        return providers;
    }

    private async Task<(MetaSection, WizardSection)> LoadMetaAsync(CancellationToken ct)
    {
        var meta = new MetaSection();
        var wizard = new WizardSection();
        var configPath = Path.Combine(_aetherDir, "config.json");
        if (!File.Exists(configPath)) return (meta, wizard);

        try
        {
            var json = await File.ReadAllTextAsync(configPath, ct);
            using var doc = JsonDocument.Parse(json);

            if (doc.RootElement.TryGetProperty("meta", out var metaEl))
            {
                meta = meta with { LastTouchedVersion = metaEl.TryGetProperty("lastTouchedVersion", out var ltv) ? ltv.GetString() : null };
            }

            if (doc.RootElement.TryGetProperty("wizard", out var wizardEl))
            {
                wizard = new WizardSection
                {
                    LastRunAt = wizardEl.TryGetProperty("lastRunAt", out var lra) ? lra.GetString() : null,
                    LastRunVersion = wizardEl.TryGetProperty("lastRunVersion", out var lrv) ? lrv.GetString() : null,
                    LastRunCommand = wizardEl.TryGetProperty("lastRunCommand", out var lrc) ? lrc.GetString() : null
                };
            }
        }
        catch { }

        return (meta, wizard);
    }

    private async Task<Dictionary<string, AgentEntryConfig>> ResolveAgentsAsync(CancellationToken ct)
    {
        var configPath = Path.Combine(_aetherDir, "config.json");
        if (!File.Exists(configPath))
        {
            _cachedAgents = new Dictionary<string, AgentEntryConfig>(StringComparer.OrdinalIgnoreCase);
            return _cachedAgents;
        }

        try
        {
            var json = await File.ReadAllTextAsync(configPath, ct);
            using var doc = JsonDocument.Parse(json);
            var agents = new Dictionary<string, AgentEntryConfig>(StringComparer.OrdinalIgnoreCase);

            if (doc.RootElement.TryGetProperty("agents", out var agentsEl))
            {
                foreach (var prop in agentsEl.EnumerateObject())
                {
                    var entry = ParseAgentEntry(prop.Name, prop.Value);
                    if (entry is not null)
                        agents[prop.Name] = entry;
                }
            }

            _cachedAgents = agents;
            return agents;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to resolve agents from config.json");
            _cachedAgents = new Dictionary<string, AgentEntryConfig>(StringComparer.OrdinalIgnoreCase);
            return _cachedAgents;
        }
    }

    private static AgentEntryConfig? ParseAgentEntry(string name, JsonElement el)
    {
        try
        {
            var entry = new AgentEntryConfig { Name = name };
            if (el.TryGetProperty("workspace", out var ws))
                entry = entry with { Workspace = ws.GetString() ?? "" };
            if (el.TryGetProperty("enabled", out var en))
                entry = entry with { Enabled = en.GetBoolean() };
            if (el.TryGetProperty("displayName", out var dn))
                entry = entry with { DisplayName = dn.GetString() };
            if (el.TryGetProperty("emoji", out var em))
                entry = entry with { Emoji = em.GetString() };
            if (el.TryGetProperty("bindings", out var bindings) && bindings.ValueKind == JsonValueKind.Array)
                entry = entry with { Bindings = bindings.EnumerateArray().Select(b => b.GetString() ?? "").ToList() };
            if (el.TryGetProperty("model", out var model))
                entry = entry with { Model = ParseAgentModel(model) };
            return entry;
        }
        catch
        {
            return null;
        }
    }

    private static AgentModelConfig ParseAgentModel(JsonElement el)
    {
        var model = new AgentModelConfig();
        if (el.TryGetProperty("primary", out var p))
            model = model with { Primary = p.GetString() };
        if (el.TryGetProperty("fallbacks", out var fb) && fb.ValueKind == JsonValueKind.Array)
            model = model with { Fallbacks = fb.EnumerateArray().Select(f => f.GetString() ?? "").ToList() };
        return model;
    }
}
