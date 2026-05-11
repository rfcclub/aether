namespace Aether.Plugins;

using Aether.Channels;
using Aether.Scheduling;
using Aether.Skills;
using Aether.Tooling;

public record PluginLoadResult(
    IReadOnlyList<IHook> Hooks,
    IReadOnlyList<IToolImplementation> Tools,
    IReadOnlyList<IChannel> Channels,
    IReadOnlyList<ISkillProvider> SkillProviders,
    IReadOnlyList<ICronTaskProvider> CronProviders,
    IReadOnlyList<IPluginLifecycle> LifecycleHandlers,
    IReadOnlyList<PluginManifest> Manifests);
