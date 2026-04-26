using Aether.Agent;
using Aether.Data;
using Aether.Memory;
using Aether.Providers;
using Aether.Routing;
using Aether.Sessions;
using Aether.Tooling;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

if (args.Contains("--smoke", StringComparer.OrdinalIgnoreCase))
{
    return;
}

if (args.Contains("--debug-args", StringComparer.OrdinalIgnoreCase))
{
    Console.WriteLine(string.Join(Environment.NewLine, args.Select((arg, index) => $"{index}: {arg}")));
    return;
}

var traceStartup = args.Contains("--trace-startup", StringComparer.OrdinalIgnoreCase);
var prompt = GetOption(args, "--prompt");
if (prompt is not null)
{
    await RunPromptHarnessAsync(args, prompt, traceStartup);
    return;
}

Trace("before builder");
var builder = Host.CreateApplicationBuilder();
Trace("after builder");

builder.Configuration
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
    .AddEnvironmentVariables("AETHER_");

builder.Services.AddSingleton<AetherHostMarker>();
builder.Services.AddSingleton(provider =>
{
    var configuration = provider.GetRequiredService<IConfiguration>();
    var databasePath = configuration["database:path"] ?? "store/aether.db";
    var schemaPath = ResolvePath(configuration["database:schema"] ?? Path.Combine("Data", "Schema.sql"));
    return new AetherDb(databasePath, schemaPath);
});
builder.Services.AddSingleton<IMessageQueue, ChannelMessageQueue>();
builder.Services.AddSingleton<MessageRouter>();
builder.Services.AddSingleton<ISessionManager, SessionManager>();
builder.Services.AddSingleton<IToolRegistry, ToolRegistry>();
builder.Services.AddSingleton<IToolExecutor, ToolExecutor>();
builder.Services.AddSingleton<IHostedService, AetherInitializationService>();

// LLM Providers
builder.Services.AddHttpClient<ILLMProvider, OpenRouterProvider>((provider, client) =>
{
    var configuration = provider.GetRequiredService<IConfiguration>();
    var baseUrl = configuration["llm:base_url"] ?? "https://openrouter.ai/api/v1";
    client.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
});
builder.Services.AddHttpClient<ILLMProvider, FireworksProvider>((provider, client) =>
{
    var configuration = provider.GetRequiredService<IConfiguration>();
    var baseUrl = configuration["fireworks:base_url"] ?? "https://api.fireworks.ai/inference/v1";
    client.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
});
builder.Services.AddHttpClient<ILLMProvider, AnthropicProvider>((provider, client) =>
{
    var configuration = provider.GetRequiredService<IConfiguration>();
    var baseUrl = configuration["anthropic:base_url"] ?? "https://api.anthropic.com";
    client.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
});

// Provider options
builder.Services.AddSingleton(provider =>
{
    var configuration = provider.GetRequiredService<IConfiguration>();
    return new OpenRouterOptions(
        ApiKey: configuration["llm:api_key"] ?? "",
        Model: configuration["llm:model"] ?? "anthropic/claude-3-5-sonnet",
        BaseUrl: configuration["llm:base_url"] ?? "https://openrouter.ai/api/v1");
});
builder.Services.AddSingleton(provider =>
{
    var configuration = provider.GetRequiredService<IConfiguration>();
    return new FireworksOptions(
        ApiKey: configuration["fireworks:api_key"] ?? "",
        Model: configuration["fireworks:model"] ?? "accounts/fireworks/models/deepseek-v3-0324",
        BaseUrl: configuration["fireworks:base_url"] ?? "https://api.fireworks.ai/inference/v1");
});
builder.Services.AddSingleton(provider =>
{
    var configuration = provider.GetRequiredService<IConfiguration>();
    return new AnthropicOptions(
        ApiKey: configuration["anthropic:api_key"] ?? "",
        Model: configuration["anthropic:model"] ?? "claude-3-5-sonnet-20241022",
        BaseUrl: configuration["anthropic:base_url"] ?? "https://api.anthropic.com");
});

// Provider health monitor
builder.Services.AddSingleton<ProviderHealthMonitor>();

// Provider routing
builder.Services.AddSingleton(provider =>
{
    var providers = provider.GetRequiredService<IEnumerable<ILLMProvider>>().ToList();
    var db = provider.GetRequiredService<AetherDb>();
    var logger = provider.GetRequiredService<ILogger<ProviderRouter>>();
    var options = new ProviderRoutingOptions
    {
        ProviderPriorities = new Dictionary<string, int>
        {
            ["fireworks"] = 1,
            ["openrouter"] = 2,
            ["anthropic"] = 3
        }
    };
    return new ProviderRouter(providers, options, db, logger);
});

builder.Services.AddSingleton<AetherSoul>();

Trace("before host build");
using var host = builder.Build();
Trace("after host build");

var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Aether");
logger.LogInformation("Aether host initialized.");

await host.RunAsync();

void Trace(string message)
{
    if (traceStartup)
    {
        Console.Error.WriteLine($"[startup] {message}");
    }
}

static async Task RunPromptHarnessAsync(string[] args, string prompt, bool traceStartup)
{
    void HarnessTrace(string message)
    {
        if (traceStartup)
        {
            Console.Error.WriteLine($"[prompt] {message}");
        }
    }

    HarnessTrace("before config");
    var configuration = new ConfigurationBuilder()
        .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
        .AddEnvironmentVariables("AETHER_")
        .Build();
    HarnessTrace("after config");

    var timeoutSeconds = GetIntOption(args, "--timeout-seconds")
        ?? configuration.GetValue("llm:timeout_seconds", 90);
    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));

    try
    {
        var group = GetOption(args, "--group") ?? "main";
        var model = GetOption(args, "--model")
            ?? ConfigValue(configuration, "llm:model", "anthropic/claude-3-5-sonnet");
        Console.Error.WriteLine($"Aether calling model '{model}' for group '{group}' with {timeoutSeconds}s timeout...");

        var databasePath = GetOption(args, "--database-path")
            ?? ConfigValue(configuration, "database:path", "store/aether.db");
        var schemaPath = ResolvePath(ConfigValue(configuration, "database:schema", Path.Combine("Data", "Schema.sql")));
        var db = new AetherDb(databasePath, schemaPath);
        HarnessTrace("before database init");
        await db.InitializeAsync(cts.Token);
        HarnessTrace("after database init");

        using var httpClient = new HttpClient
        {
            BaseAddress = new Uri(ConfigValue(configuration, "llm:base_url", "https://openrouter.ai/api/v1").TrimEnd('/') + "/")
        };
        var provider = new OpenRouterProvider(
            httpClient,
            new OpenRouterOptions(
                ApiKey: ReadApiKey(args, configuration),
                Model: model,
                BaseUrl: ConfigValue(configuration, "llm:base_url", "https://openrouter.ai/api/v1")));
        var memory = new FileMemory(ConfigValue(configuration, "groups:path", "groups"));
        var sessions = new SessionManager(db);
        var toolExecutor = new ToolExecutor(configuration);
        var soul = new AetherSoul(provider, memory, toolExecutor, sessions);

        var response = await soul.ProcessAsync(group, prompt, cts.Token);
        Console.WriteLine(response.Content);
    }
    catch (OperationCanceledException) when (cts.IsCancellationRequested)
    {
        Console.Error.WriteLine($"Aether prompt timed out after {timeoutSeconds}s.");
        Environment.ExitCode = 124;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine(ex.Message);
        Environment.ExitCode = 1;
    }
}

static string ResolvePath(string path)
{
    if (Path.IsPathRooted(path))
    {
        return path;
    }

    var cwdPath = Path.GetFullPath(path);
    if (File.Exists(cwdPath))
    {
        return cwdPath;
    }

    return Path.Combine(AppContext.BaseDirectory, path);
}

static string ConfigValue(IConfiguration configuration, string key, string fallback)
{
    var envKey = "AETHER_" + key.Replace(':', '_');
    var doubleUnderscoreEnvKey = "AETHER_" + key.Replace(":", "__", StringComparison.Ordinal);
    var envValue = Environment.GetEnvironmentVariable(doubleUnderscoreEnvKey)
        ?? Environment.GetEnvironmentVariable(doubleUnderscoreEnvKey.ToUpperInvariant())
        ?? Environment.GetEnvironmentVariable(envKey)
        ?? Environment.GetEnvironmentVariable(envKey.ToUpperInvariant());
    if (!string.IsNullOrWhiteSpace(envValue))
    {
        return envValue;
    }

    var direct = configuration[key];
    if (!string.IsNullOrWhiteSpace(direct) && !direct.StartsWith("${", StringComparison.Ordinal))
    {
        return direct;
    }

    return fallback;
}

static string ReadApiKey(string[] args, IConfiguration configuration)
{
    var apiKeyFile = GetOption(args, "--api-key-file");
    if (!string.IsNullOrWhiteSpace(apiKeyFile))
    {
        return File.ReadAllText(apiKeyFile).Trim();
    }

    return ConfigValue(configuration, "llm:api_key", "");
}

static string? GetOption(string[] args, string name)
{
    for (var i = 0; i < args.Length; i++)
    {
        if (!string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
        {
            continue;
        }

        return i + 1 < args.Length ? args[i + 1] : "";
    }

    return null;
}

static int? GetIntOption(string[] args, string name)
{
    var value = GetOption(args, name);
    if (value is null)
    {
        return null;
    }

    return int.TryParse(value, out var parsed) ? parsed : throw new ArgumentException($"{name} must be an integer.");
}

internal sealed class AetherHostMarker;
