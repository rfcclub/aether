using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Aether.Config;

public sealed class ConfigLoader
{
    private readonly IConfiguration _configuration;
    private readonly string _aetherDir;
    public string AetherDir => _aetherDir;
    private readonly ILogger<ConfigLoader> _logger;
    private readonly AgentAuthProfiles? _authProfiles;

    private AetherAppConfig? _cachedConfig;
    private Dictionary<string, AgentEntryConfig>? _cachedAgents;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

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

        // Layer 1: Global defaults from appsettings.json → providers map
        var providers = LoadProvidersFromAppSettings();

        // Layer 2: ~/.aether/config.json — global providers + agents registry
        var (globalProviders, agents, meta, wizard) = await LoadGlobalConfigAsync(ct);
        providers = MergeProviders(providers, globalProviders);

        // Layer 3: Per-agent {workspace}/.aether.json — agent spec config
        var agentSpecs = new Dictionary<string, AgentEntryConfig>(StringComparer.OrdinalIgnoreCase);
        var specConfigs = new Dictionary<string, AgentSpecConfig>(StringComparer.OrdinalIgnoreCase);

        foreach (var (name, entry) in agents)
        {
            var spec = await LoadAgentSpecAsync(entry, ct);

            // Merge global providers into agent spec (agent overrides global)
            spec = MergeSpecProviders(spec, providers);

            if (agentName is not null && string.Equals(name, agentName, StringComparison.OrdinalIgnoreCase))
            {
                // Layer 3.5: Auth profiles
                if (_authProfiles is not null)
                    spec = await ApplyAuthProfilesAsync(spec, agentName, ct);

                // Layer 4: Env overrides
                spec = ApplyEnvOverrides(spec);

                // Layer 5: CLI overrides
                if (cliOverrides is not null)
                    spec = ApplyCliOverrides(spec, cliOverrides);
            }

            specConfigs[name] = spec;
            agentSpecs[name] = entry with { Spec = spec };
        }

        var config = new AetherAppConfig
        {
            Providers = providers,
            Agents = agentSpecs,
            AgentSpecs = specConfigs,
            Meta = meta,
            Wizard = wizard
        };

        if (agentName is null && cliOverrides is null)
        {
            _cachedConfig = config;
            _cachedAgents = agentSpecs;
        }

        return config;
    }

    public AgentEntryConfig? GetAgentConfig(string name)
    {
        if (_cachedAgents is null) return null;
        return _cachedAgents.TryGetValue(name, out var entry) ? entry : null;
    }

    public AgentSpecConfig? GetAgentSpec(string name)
    {
        if (_cachedConfig?.AgentSpecs is null) return null;
        return _cachedConfig.AgentSpecs.TryGetValue(name, out var spec) ? spec : null;
    }

    // ── Layer 1: appsettings.json ──

    private Dictionary<string, SpecProviderEntry> LoadProvidersFromAppSettings()
    {
        var providers = new Dictionary<string, SpecProviderEntry>(StringComparer.OrdinalIgnoreCase);

        // Map legacy appsettings keys → spec provider entries
        var section = _configuration.GetSection("providers");
        foreach (var child in section.GetChildren())
        {
            var entry = new SpecProviderEntry
            {
                Type = child.GetValue<string>("type") ?? "openai",
                Model = child.GetValue<string>("model") ?? "",
                ApiKey = child.GetValue<string>("api_key"),
                BaseUrl = child.GetValue<string>("base_url"),
                MaxTokens = child.GetValue<int?>("max_tokens") ?? 4096,
                Temperature = child.GetValue<double?>("temperature") ?? 0.7,
                TimeoutSeconds = child.GetValue<int?>("timeout_seconds") ?? 120
            };
            if (!string.IsNullOrEmpty(entry.Model) || !string.IsNullOrEmpty(entry.ApiKey))
                providers[child.Key] = entry;
        }

        // Fallback: read legacy flat keys (llm, anthropic, fireworks)
        if (providers.Count == 0)
        {
            TryAddLegacyProvider(providers, "openrouter", "llm", "openai", "https://openrouter.ai/api/v1");
            TryAddLegacyProvider(providers, "anthropic", "anthropic", "anthropic", "https://api.anthropic.com");
            TryAddLegacyProvider(providers, "fireworks", "fireworks", "openai", "https://api.fireworks.ai/inference/v1");
        }

        return providers;
    }

    private void TryAddLegacyProvider(Dictionary<string, SpecProviderEntry> providers,
        string name, string key, string type, string defaultBaseUrl)
    {
        var model = _configuration[$"{key}:model"];
        var apiKey = _configuration[$"{key}:api_key"];
        var baseUrl = _configuration[$"{key}:base_url"] ?? defaultBaseUrl;
        var timeout = _configuration.GetValue<int?>($"{key}:timeout_seconds") ?? (_configuration.GetValue<int?>($"{key}:timeoutSeconds"));

        if (!string.IsNullOrEmpty(model) || !string.IsNullOrEmpty(apiKey))
        {
            providers[name] = new SpecProviderEntry
            {
                Type = type,
                Model = model ?? "",
                ApiKey = apiKey,
                BaseUrl = baseUrl,
                TimeoutSeconds = timeout ?? 120
            };
        }
    }

    // ── Layer 2: ~/.aether/config.json ──

    private async Task<(Dictionary<string, SpecProviderEntry> providers,
        Dictionary<string, AgentEntryConfig> agents,
        MetaSection meta, WizardSection wizard)> LoadGlobalConfigAsync(CancellationToken ct)
    {
        var providers = new Dictionary<string, SpecProviderEntry>(StringComparer.OrdinalIgnoreCase);
        var agents = new Dictionary<string, AgentEntryConfig>(StringComparer.OrdinalIgnoreCase);
        var meta = new MetaSection();
        var wizard = new WizardSection();

        var path = Path.Combine(_aetherDir, "config.json");
        if (!File.Exists(path))
            return (providers, agents, meta, wizard);

        try
        {
            var json = await File.ReadAllTextAsync(path, ct);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // Providers — new spec format: "providers": { "name": { "type":..., "model":... } }
            if (root.TryGetProperty("providers", out var providersEl))
            {
                foreach (var prop in providersEl.EnumerateObject())
                {
                    var entry = JsonSerializer.Deserialize<SpecProviderEntry>(prop.Value.GetRawText(), JsonOptions);
                    if (entry is not null)
                        providers[prop.Name] = entry;
                }
            }
            // Legacy format: "llm": { "api_key":..., "model":... }
            if (root.TryGetProperty("llm", out var llm))
                providers["openrouter"] = ParseLegacyProvider(llm, "openai", "https://openrouter.ai/api/v1");
            if (root.TryGetProperty("anthropic", out var anthro))
                providers["anthropic"] = ParseLegacyProvider(anthro, "anthropic", "https://api.anthropic.com");
            if (root.TryGetProperty("fireworks", out var fw))
                providers["fireworks"] = ParseLegacyProvider(fw, "openai", "https://api.fireworks.ai/inference/v1");

            // Agents
            if (root.TryGetProperty("agents", out var agentsEl))
            {
                foreach (var prop in agentsEl.EnumerateObject())
                {
                    var entry = ParseAgentEntry(prop.Name, prop.Value);
                    if (entry is not null)
                        agents[prop.Name] = entry;
                }
            }

            // Legacy gateway.agents.<name>.source → convert to bindings
            if (root.TryGetProperty("gateway", out var gatewayEl) &&
                gatewayEl.TryGetProperty("agents", out var gwAgents))
            {
                foreach (var prop in gwAgents.EnumerateObject())
                {
                    if (!agents.TryGetValue(prop.Name, out var existing)) continue;
                    if (prop.Value.TryGetProperty("source", out var src))
                    {
                        var source = src.GetString();
                        if (!string.IsNullOrEmpty(source) && !existing.Bindings.Contains(source))
                        {
                            var updatedBindings = new List<string>(existing.Bindings) { source };
                            agents[prop.Name] = existing with { Bindings = updatedBindings };
                        }
                    }
                }
            }

            // Meta
            if (root.TryGetProperty("meta", out var metaEl))
                meta = JsonSerializer.Deserialize<MetaSection>(metaEl.GetRawText(), JsonOptions) ?? meta;

            // Wizard
            if (root.TryGetProperty("wizard", out var wizardEl))
                wizard = JsonSerializer.Deserialize<WizardSection>(wizardEl.GetRawText(), JsonOptions) ?? wizard;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load ~/.aether/config.json");
        }

        return (providers, agents, meta, wizard);
    }

    private static SpecProviderEntry ParseLegacyProvider(JsonElement el, string type, string defaultBaseUrl)
    {
        return new SpecProviderEntry
        {
            Type = type,
            Model = el.TryGetProperty("model", out var m) ? m.GetString() ?? "" : "",
            ApiKey = el.TryGetProperty("api_key", out var ak) ? ak.GetString() : null,
            BaseUrl = el.TryGetProperty("base_url", out var bu) ? bu.GetString() : defaultBaseUrl,
            TimeoutSeconds = el.TryGetProperty("timeout_seconds", out var ts) && ts.TryGetInt32(out var tsv) ? tsv : 120,
            MaxTokens = el.TryGetProperty("max_tokens", out var mt) && mt.TryGetInt32(out var mtv) ? mtv : 4096,
            Temperature = el.TryGetProperty("temperature", out var temp) && temp.TryGetDouble(out var tempv) ? tempv : 0.7
        };
    }

    // ── Layer 3: Agent spec config ──

    private async Task<AgentSpecConfig> LoadAgentSpecAsync(AgentEntryConfig entry, CancellationToken ct)
    {
        var spec = new AgentSpecConfig
        {
            Agent = new SpecAgentSection
            {
                Name = entry.Name,
                DisplayName = entry.DisplayName,
                Emoji = entry.Emoji
            },
            Storage = new SpecStorageSection { Home = entry.Workspace }
        };

        var workspace = entry.Workspace;
        if (string.IsNullOrEmpty(workspace))
            workspace = Path.Combine(_aetherDir, "workspaces", entry.Name);

        var path = Path.Combine(workspace, ".aether.json");
        if (!File.Exists(path))
            return spec;

        try
        {
            var json = await File.ReadAllTextAsync(path, ct);
            var loaded = JsonSerializer.Deserialize<AgentSpecConfig>(json, JsonOptions);
            if (loaded is not null)
                spec = loaded;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load agent spec for {Agent} from {Path}", entry.Name, path);
        }

        return spec;
    }

    // ── Merge ──

    private static Dictionary<string, SpecProviderEntry> MergeProviders(
        Dictionary<string, SpecProviderEntry> base_,
        Dictionary<string, SpecProviderEntry> overrides)
    {
        var merged = new Dictionary<string, SpecProviderEntry>(base_, StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in overrides)
        {
            if (merged.TryGetValue(key, out var existing))
                merged[key] = MergeProviderFields(existing, value);
            else
                merged[key] = value;
        }
        return merged;
    }

    private static SpecProviderEntry MergeProviderFields(SpecProviderEntry base_, SpecProviderEntry overrides)
    {
        return new SpecProviderEntry
        {
            Type = overrides.Type,
            Model = !string.IsNullOrEmpty(overrides.Model) ? overrides.Model : base_.Model,
            ApiKey = overrides.ApiKey ?? base_.ApiKey,
            BaseUrl = overrides.BaseUrl ?? base_.BaseUrl,
            MaxTokens = overrides.MaxTokens,
            Temperature = overrides.Temperature,
            TimeoutSeconds = overrides.TimeoutSeconds
        };
    }

    private static AgentSpecConfig MergeSpecProviders(AgentSpecConfig spec, Dictionary<string, SpecProviderEntry> globalProviders)
    {
        if (spec.Providers.Count == 0 && globalProviders.Count > 0)
            return spec with { Providers = new Dictionary<string, SpecProviderEntry>(globalProviders, StringComparer.OrdinalIgnoreCase) };

        // Agent providers override global by name
        var merged = new Dictionary<string, SpecProviderEntry>(globalProviders, StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in spec.Providers)
            merged[key] = value;
        return spec with { Providers = merged };
    }

    // ── Layer 3.5: Auth profiles ──

    private async Task<AgentSpecConfig> ApplyAuthProfilesAsync(AgentSpecConfig spec, string agentName, CancellationToken ct)
    {
        try
        {
            var auth = await _authProfiles!.LoadAuthProfilesAsync(agentName, ct);

            if (auth.State.ActiveProvider is not null &&
                auth.Profiles.TryGetValue(auth.State.ActiveProvider, out var profile) &&
                !string.IsNullOrEmpty(profile.ApiKey))
            {
                var providers = new Dictionary<string, SpecProviderEntry>(spec.Providers, StringComparer.OrdinalIgnoreCase);
                var providerName = auth.State.ActiveProvider;
                if (providers.TryGetValue(providerName, out var existing))
                    providers[providerName] = existing with { ApiKey = profile.ApiKey };
                else
                    providers[providerName] = new SpecProviderEntry { Type = "openai", Model = "", ApiKey = profile.ApiKey };
                spec = spec with { Providers = providers };
            }

            if (!string.IsNullOrEmpty(auth.Model.Primary))
            {
                var providers = new Dictionary<string, SpecProviderEntry>(spec.Providers, StringComparer.OrdinalIgnoreCase);
                var providerName = auth.State.ActiveProvider ?? "openrouter";
                if (providers.TryGetValue(providerName, out var existing))
                    providers[providerName] = existing with { Model = auth.Model.Primary };
                spec = spec with { Providers = providers };
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to apply auth profiles for {Agent}", agentName);
        }

        return spec;
    }

    // ── Layer 4: Env overrides ──

    private static AgentSpecConfig ApplyEnvOverrides(AgentSpecConfig spec)
    {
        var providers = new Dictionary<string, SpecProviderEntry>(spec.Providers, StringComparer.OrdinalIgnoreCase);

        foreach (var (name, entry) in providers.ToList())
        {
            var prefix = $"AETHER_PROVIDERS_{name.ToUpperInvariant()}";
            var envModel = Environment.GetEnvironmentVariable($"{prefix}_MODEL");
            var envKey = Environment.GetEnvironmentVariable($"{prefix}_API_KEY");
            var envUrl = Environment.GetEnvironmentVariable($"{prefix}_BASE_URL");
            var envTimeout = Environment.GetEnvironmentVariable($"{prefix}_TIMEOUT");

            var updated = entry;
            if (!string.IsNullOrEmpty(envModel)) updated = updated with { Model = envModel };
            if (!string.IsNullOrEmpty(envKey)) updated = updated with { ApiKey = envKey };
            if (!string.IsNullOrEmpty(envUrl)) updated = updated with { BaseUrl = envUrl };
            if (int.TryParse(envTimeout, out var t)) updated = updated with { TimeoutSeconds = t };
            providers[name] = updated;
        }

        // Also support legacy env vars for backward compat
        var legacyKey = Environment.GetEnvironmentVariable("AETHER_llm__api_key")
                        ?? Environment.GetEnvironmentVariable("AETHER_llm_api_key");
        var legacyModel = Environment.GetEnvironmentVariable("AETHER_llm__model")
                          ?? Environment.GetEnvironmentVariable("AETHER_llm_model");

        if (!string.IsNullOrEmpty(legacyKey) || !string.IsNullOrEmpty(legacyModel))
        {
            if (providers.TryGetValue("openrouter", out var or))
            {
                if (!string.IsNullOrEmpty(legacyKey)) or = or with { ApiKey = legacyKey };
                if (!string.IsNullOrEmpty(legacyModel)) or = or with { Model = legacyModel };
                providers["openrouter"] = or;
            }
        }

        return spec with { Providers = providers };
    }

    // ── Layer 5: CLI overrides ──

    private static AgentSpecConfig ApplyCliOverrides(AgentSpecConfig spec, Dictionary<string, string> cliOverrides)
    {
        var providers = new Dictionary<string, SpecProviderEntry>(spec.Providers, StringComparer.OrdinalIgnoreCase);

        foreach (var (key, value) in cliOverrides)
        {
            // Format: "provider:name:field" e.g. "provider:openrouter:model"
            if (key.StartsWith("provider:") && key.Count(c => c == ':') >= 2)
            {
                var parts = key.Split(':', 3);
                var providerName = parts[1];
                var field = parts[2];

                if (providers.TryGetValue(providerName, out var entry))
                {
                    entry = field switch
                    {
                        "model" => entry with { Model = value },
                        "api_key" => entry with { ApiKey = value },
                        "base_url" => entry with { BaseUrl = value },
                        _ => entry
                    };
                    providers[providerName] = entry;
                }
            }
            // Legacy "llm:model" mapping
            else if (key == "llm:model" && providers.TryGetValue("openrouter", out var or))
                providers["openrouter"] = or with { Model = value };
            else if (key == "llm:api_key" && providers.TryGetValue("openrouter", out var or2))
                providers["openrouter"] = or2 with { ApiKey = value };
        }

        return spec with { Providers = providers };
    }

    // ── Agent parsing ──

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
            if (el.TryGetProperty("heartbeatIntervalMinutes", out var hi) && hi.TryGetInt32(out var hiv))
                entry = entry with { HeartbeatIntervalMinutes = hiv };
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

    public async Task UpdateAgentModelAsync(string agentName, string primaryModel, CancellationToken ct)
    {
        var path = Path.Combine(_aetherDir, "config.json");
        var json = File.Exists(path) ? await File.ReadAllTextAsync(path, ct) : "{}";
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        using var stream = new MemoryStream();
        using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });

        writer.WriteStartObject();

        // Copy or write agents section with updated model
        var wroteAgents = false;
        foreach (var prop in root.EnumerateObject())
        {
            if (prop.NameEquals("agents"))
            {
                writer.WritePropertyName("agents");
                writer.WriteStartObject();
                var agentsEl = prop.Value;

                // Copy existing agents
                foreach (var agentProp in agentsEl.EnumerateObject())
                {
                    writer.WritePropertyName(agentProp.Name);

                    if (agentProp.NameEquals(agentName))
                    {
                        // Merge model into this agent
                        WriteAgentWithModel(writer, agentProp.Value, primaryModel);
                    }
                    else
                    {
                        agentProp.Value.WriteTo(writer);
                    }
                }

                wroteAgents = true;
                writer.WriteEndObject();
            }
            else
            {
                prop.WriteTo(writer);
            }
        }

        // If no agents section exists, add one with just this agent
        if (!wroteAgents)
        {
            writer.WritePropertyName("agents");
            writer.WriteStartObject();
            writer.WritePropertyName(agentName);
            WriteAgentWithModel(writer, default, primaryModel);
            writer.WriteEndObject();
        }

        writer.WriteEndObject();
        writer.Flush();

        stream.Position = 0;
        var updated = Encoding.UTF8.GetString(stream.ToArray());
        await File.WriteAllTextAsync(path, updated, ct);

        // Update cache so next LoadAsync picks it up
        if (_cachedAgents is not null && _cachedAgents.TryGetValue(agentName, out var cached))
        {
            _cachedAgents[agentName] = cached with
            {
                Model = cached.Model is null
                    ? new AgentModelConfig { Primary = primaryModel }
                    : cached.Model with { Primary = primaryModel }
            };
        }

        _logger.LogInformation("Persisted model '{Model}' for agent {Agent}", primaryModel, agentName);
    }

    private static void WriteAgentWithModel(Utf8JsonWriter writer, JsonElement existing, string primaryModel)
    {
        writer.WriteStartObject();

        // Copy existing properties except "model"
        if (existing.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in existing.EnumerateObject())
            {
                if (!prop.NameEquals("model"))
                    prop.WriteTo(writer);
            }
        }

        // Write updated model section
        writer.WritePropertyName("model");
        writer.WriteStartObject();
        writer.WriteString("primary", primaryModel);
        writer.WriteEndObject();

        writer.WriteEndObject();
    }
}
