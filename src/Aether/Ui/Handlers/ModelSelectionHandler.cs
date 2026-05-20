using Aether.Config;
using Aether.Providers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Aether.Ui.Handlers;

public class ModelSelectionHandler : IUiCallbackHandler
{
    public string Namespace => "model";

    private const int ModelsPerPage = 8;

    public async Task<UiDocument?> HandleAsync(UiCallback callback, IServiceProvider services, string agentId)
    {
        var router = services.GetRequiredService<ProviderRouter>();
        var logger = services.GetService<ILogger<ModelSelectionHandler>>();

        return callback.Action switch
        {
            "browse" => BuildProviderList(router),
            "list" => BuildModelList(router, callback.Data, agentId),
            "select" => await SelectModelAsync(router, services, callback.Data, agentId, logger),
            "selectat" => await SelectModelAtAsync(router, services, callback.Data, agentId, logger),
            "reset" => await ResetToDefaultAsync(router, services.GetRequiredService<ConfigLoader>(), agentId),
            "page" => BuildModelList(router, callback.Data, agentId), // data = "{baseProvider}:{page}"
            _ => null
        };
    }

    /// <summary>
    /// Extract the base provider key from a provider name of the form "{entryKey}/{modelId}".
    /// The model is the ILLMProvider.Model, so the base is everything before "/{model}".
    /// </summary>
    private static string BaseProvider(string fullProviderName, string model)
    {
        var suffix = "/" + model;
        if (fullProviderName.EndsWith(suffix, StringComparison.Ordinal))
            return fullProviderName[..^suffix.Length];
        return fullProviderName;
    }

    private static UiDocument BuildProviderList(ProviderRouter router)
    {
        var available = router.GetAvailableModels();
        var effectiveModel = router.EffectiveModel ?? "none";

        // Group by base provider (entry key), not full provider name
        var grouped = available
            .GroupBy(m => BaseProvider(m.Provider, m.Model), StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var sections = new List<UiSection>();
        foreach (var group in grouped)
        {
            var baseName = group.Key;
            var models = group.ToList();
            var hasActive = models.Any(m =>
                string.Equals(m.Model, effectiveModel, StringComparison.OrdinalIgnoreCase));

            var items = new List<UiItem>
            {
                new()
                {
                    Id = $"list:{baseName}",
                    Label = baseName,
                    Emoji = ProviderEmoji(baseName),
                    Selected = hasActive,
                    StatusBadge = $"({models.Count})"
                }
            };

            sections.Add(new UiSection
            {
                Title = baseName,
                Items = items
            });
        }

        if (router.ModelChain is { Count: > 0 })
        {
            sections.Add(new UiSection
            {
                Title = "Actions",
                Items = new List<UiItem>
                {
                    new()
                    {
                        Id = "reset",
                        Label = "Reset to Default",
                        Emoji = "🔄"
                    }
                }
            });
        }

        return new UiDocument
        {
            Text = $"🧠 Model Selection\nCurrent: {effectiveModel}",
            Sections = sections,
            Layout = UiLayout.List,
            CallbackNamespace = "model"
        };
    }

    private static UiDocument BuildModelList(ProviderRouter router, string data, string agentId)
    {
        var effectiveModel = router.EffectiveModel ?? "none";
        var available = router.GetAvailableModels();

        // Parse: "{baseProvider}" or "{baseProvider}:{page}"
        var parts = data.Split(':', 2);
        var baseProvider = parts[0];
        var page = parts.Length > 1 && int.TryParse(parts[1], out var p) ? p : 0;

        // Filter: models whose full provider name belongs to this base provider
        var models = available
            .Where(m => string.Equals(BaseProvider(m.Provider, m.Model), baseProvider,
                StringComparison.OrdinalIgnoreCase))
            .OrderBy(m => m.Model, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (models.Count == 0)
        {
            return new UiDocument
            {
                Text = $"No models found for provider: {baseProvider}",
                CallbackNamespace = "model"
            };
        }

        var totalPages = (models.Count + ModelsPerPage - 1) / ModelsPerPage;
        page = Math.Clamp(page, 0, totalPages - 1);

        var emoji = ProviderEmoji(baseProvider);

        var startIndex = page * ModelsPerPage;
        var pageModels = models
            .Skip(startIndex)
            .Take(ModelsPerPage)
            .Select((model, index) => (Model: model, AbsoluteIndex: startIndex + index));

        var items = pageModels.Select(m => new UiItem
        {
            Id = $"selectat:{baseProvider}:{m.AbsoluteIndex}",
            Label = ShortModelName(m.Model.Model),
            Selected = string.Equals(m.Model.Model, effectiveModel, StringComparison.OrdinalIgnoreCase)
        }).ToList();

        items.Add(new UiItem
        {
            Id = "browse",
            Label = "Back",
            Emoji = "◀️"
        });

        return new UiDocument
        {
            Text = $"{emoji} {baseProvider} — {models.Count} models\nCurrent: {effectiveModel}",
            Sections = new List<UiSection>
            {
                new()
                {
                    Title = totalPages <= 1
                        ? baseProvider
                        : $"{baseProvider} (page {page + 1}/{totalPages})",
                    Items = items
                }
            },
            Layout = totalPages > 1 ? UiLayout.Paged : UiLayout.List,
            CallbackNamespace = "model",
            PageContext = baseProvider,
            PageIndex = page,
            TotalPages = totalPages
        };
    }

    private static async Task<UiDocument?> SelectModelAtAsync(
        ProviderRouter router, IServiceProvider services,
        string data, string agentId, ILogger<ModelSelectionHandler>? logger)
    {
        var parts = data.Split(':', 2);
        if (parts.Length != 2 || !int.TryParse(parts[1], out var index))
            return new UiDocument { Text = "Invalid model selection." };

        var baseProvider = parts[0];
        var models = router.GetAvailableModels()
            .Where(m => string.Equals(BaseProvider(m.Provider, m.Model), baseProvider,
                StringComparison.OrdinalIgnoreCase))
            .OrderBy(m => m.Model, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (index < 0 || index >= models.Count)
            return new UiDocument { Text = "Invalid model selection." };

        return await SelectModelAsync(router, services, models[index].Model, agentId, logger);
    }

    private static async Task<UiDocument?> SelectModelAsync(
        ProviderRouter router, IServiceProvider services,
        string modelId, string agentId, ILogger<ModelSelectionHandler>? logger)
    {
        var provider = router.ResolveModelToProvider(modelId);
        if (provider is null)
        {
            return new UiDocument { Text = $"Unknown model: {modelId}" };
        }

        var effectiveModel = router.EffectiveModel;
        if (string.Equals(modelId, effectiveModel, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var existing = router.ModelChain?.Skip(1).ToList() ?? new List<string>();
        var newChain = new List<string> { modelId };
        newChain.AddRange(existing.Where(f =>
            !string.Equals(f, modelId, StringComparison.OrdinalIgnoreCase)));
        router.ModelChain = newChain;

        var configLoader = services.GetRequiredService<ConfigLoader>();
        await configLoader.UpdateAgentModelAsync(agentId, modelId, CancellationToken.None);

        logger?.LogInformation("Model switched to {Model} for {Agent}", modelId, agentId);

        var baseProviderName = BaseProvider(provider.Name, provider.Model);
        return new UiDocument
        {
            Text = $"✅ Model switched to: **{ShortModelName(modelId)}**\nProvider: {baseProviderName}",
            CallbackNamespace = "model"
        };
    }

    private static async Task<UiDocument?> ResetToDefaultAsync(
        ProviderRouter router, ConfigLoader configLoader, string agentId)
    {
        var available = router.GetAvailableModels();
        var defaultModel = available.FirstOrDefault().Model;
        if (defaultModel is null)
            return new UiDocument { Text = "No default model available." };

        router.ModelChain = new List<string> { defaultModel };
        await configLoader.UpdateAgentModelAsync(agentId, defaultModel, CancellationToken.None);

        return BuildProviderList(router);
    }

    private static string ShortModelName(string fullModelId)
    {
        var idx = fullModelId.LastIndexOf('/');
        return idx >= 0 ? fullModelId[(idx + 1)..] : fullModelId;
    }

    private static string ProviderEmoji(string name)
    {
        var lower = name.ToLowerInvariant();
        if (lower.StartsWith("fireworks") || lower == "fireworks-ai") return "🔥";
        if (lower.StartsWith("openrouter")) return "🌐";
        if (lower.StartsWith("anthropic")) return "🔷";
        if (lower.StartsWith("openai")) return "🧪";
        if (lower.StartsWith("synthetic")) return "🤖";
        if (lower.StartsWith("minimax")) return "💠";
        if (lower.StartsWith("crof")) return "🐸";
        return "📡";
    }
}
