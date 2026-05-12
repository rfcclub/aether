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
            "reset" => await ResetToDefaultAsync(router, services.GetRequiredService<ConfigLoader>(), agentId),
            "page" => BuildModelList(router, callback.Data, agentId),
            _ => null
        };
    }

    private static UiDocument BuildProviderList(ProviderRouter router)
    {
        var available = router.GetAvailableModels();
        var effectiveModel = router.EffectiveModel ?? "none";

        // Group models by provider
        var grouped = available
            .GroupBy(m => m.Provider, StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var sections = new List<UiSection>();
        foreach (var group in grouped)
        {
            var providerName = group.Key;
            var models = group.ToList();
            var hasActive = models.Any(m =>
                string.Equals(m.Model, effectiveModel, StringComparison.OrdinalIgnoreCase));

            var items = new List<UiItem>
            {
                new()
                {
                    Id = $"list:{providerName}",
                    Label = providerName,
                    Emoji = ProviderEmoji(providerName),
                    Selected = hasActive,
                    StatusBadge = $"({models.Count})"
                }
            };

            sections.Add(new UiSection
            {
                Title = providerName,
                Items = items
            });
        }

        // Add reset button if user has an override set
        var hasOverride = router.ModelChain is { Count: > 0 } &&
                          !string.Equals(router.ModelChain[0], router.EffectiveModel,
                              StringComparison.OrdinalIgnoreCase);
        // Actually, ModelChain[0] IS the effective model. Check if there's a non-default override.
        // Simplest: always show reset when ModelChain is set (user has chosen something).
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

        // Parse provider name and optional page from data
        // Format: "{provider}" or "{provider}:{page}"
        var parts = data.Split(':', 2);
        var providerName = parts[0];
        var page = parts.Length > 1 && int.TryParse(parts[1], out var p) ? p : 0;

        var models = available
            .Where(m => string.Equals(m.Provider, providerName, StringComparison.OrdinalIgnoreCase))
            .OrderBy(m => m.Model, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (models.Count == 0)
        {
            return new UiDocument
            {
                Text = $"No models found for provider: {providerName}",
                CallbackNamespace = "model"
            };
        }

        var totalPages = (models.Count + ModelsPerPage - 1) / ModelsPerPage;
        page = Math.Clamp(page, 0, totalPages - 1);

        var emoji = ProviderEmoji(providerName);

        if (totalPages <= 1)
        {
            // Single page — no pagination needed
            var items = models.Select(m => new UiItem
            {
                Id = $"select:{m.Model}",
                Label = ShortModelName(m.Model),
                Selected = string.Equals(m.Model, effectiveModel, StringComparison.OrdinalIgnoreCase)
            }).ToList();

            items.Add(new UiItem
            {
                Id = "browse",
                Label = "Back",
                Emoji = "◀️"
            });

            return new UiDocument
            {
                Text = $"{emoji} {providerName} — {models.Count} models\nCurrent: {effectiveModel}",
                Sections = new List<UiSection>
                {
                    new() { Title = providerName, Items = items }
                },
                Layout = UiLayout.List,
                CallbackNamespace = "model"
            };
        }

        // Paginated: each page gets its own section
        var sections = new List<UiSection>();
        for (var i = 0; i < totalPages; i++)
        {
            var pageModels = models.Skip(i * ModelsPerPage).Take(ModelsPerPage).ToList();
            var pageItems = pageModels.Select(m => new UiItem
            {
                Id = $"select:{m.Model}",
                Label = ShortModelName(m.Model),
                Selected = string.Equals(m.Model, effectiveModel, StringComparison.OrdinalIgnoreCase)
            }).ToList();

            // Add Back button to last page only (it's always visible)
            if (i == totalPages - 1)
            {
                pageItems.Add(new UiItem
                {
                    Id = "browse",
                    Label = "Back",
                    Emoji = "◀️"
                });
            }

            sections.Add(new UiSection
            {
                Title = $"{providerName} (page {i + 1}/{totalPages})",
                Items = pageItems
            });
        }

        return new UiDocument
        {
            Text = $"{emoji} {providerName} — {models.Count} models\nCurrent: {effectiveModel}",
            Sections = sections,
            Layout = UiLayout.Paged,
            CallbackNamespace = "model"
        };
    }

    private static async Task<UiDocument?> SelectModelAsync(
        ProviderRouter router, IServiceProvider services,
        string modelId, string agentId, ILogger<ModelSelectionHandler>? logger)
    {
        // Validate model exists
        var provider = router.ResolveModelToProvider(modelId);
        if (provider is null)
        {
            return new UiDocument
            {
                Text = $"Unknown model: {modelId}"
            };
        }

        // No-op if already current
        var effectiveModel = router.EffectiveModel;
        if (string.Equals(modelId, effectiveModel, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        // Update in-memory chain
        var existing = router.ModelChain?.Skip(1).ToList() ?? new List<string>();
        var newChain = new List<string> { modelId };
        newChain.AddRange(existing.Where(f =>
            !string.Equals(f, modelId, StringComparison.OrdinalIgnoreCase)));
        router.ModelChain = newChain;

        // Persist
        var configLoader = services.GetRequiredService<ConfigLoader>();
        await configLoader.UpdateAgentModelAsync(agentId, modelId, CancellationToken.None);

        logger?.LogInformation("Model switched to {Model} for {Agent}", modelId, agentId);

        // Return updated model list for this provider
        return BuildModelList(router, $"list:{provider.Name}", agentId);
    }

    private static async Task<UiDocument?> ResetToDefaultAsync(
        ProviderRouter router, ConfigLoader configLoader, string agentId)
    {
        // Get the first available model as the default
        var available = router.GetAvailableModels();
        var defaultModel = available.FirstOrDefault().Model;
        if (defaultModel is null)
            return new UiDocument { Text = "No default model available." };

        // Set chain to just the default
        router.ModelChain = new List<string> { defaultModel };

        // Persist
        await configLoader.UpdateAgentModelAsync(agentId, defaultModel, CancellationToken.None);

        return BuildProviderList(router);
    }

    private static string ShortModelName(string fullModelId)
    {
        // Take the last segment after the final slash for display
        var idx = fullModelId.LastIndexOf('/');
        return idx >= 0 ? fullModelId[(idx + 1)..] : fullModelId;
    }

    private static string ProviderEmoji(string providerName)
    {
        return providerName.ToLowerInvariant() switch
        {
            "fireworks" or "fireworks-ai" => "🔥",
            "openrouter" => "🌐",
            "anthropic" => "🔷",
            "openai" => "🧪",
            "synthetic" => "🤖",
            "minimax" => "💠",
            _ => "📡"
        };
    }
}
