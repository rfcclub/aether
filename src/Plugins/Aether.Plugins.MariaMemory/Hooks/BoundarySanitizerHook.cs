using System.Text.RegularExpressions;
using Aether.Plugins;
using Microsoft.Extensions.Logging;

namespace Aether.Plugins.MariaMemory.Hooks;

public sealed class BoundarySanitizerHook : IHook
{
    public string Name => "BoundarySanitizer";
    public HookPoint SubscribesTo => HookPoint.PostLlmCall;
    public int Priority => 100;

    private static readonly Regex BoundaryRegex = new(
        @"(system prompt|hidden state|raw internal|core paradox|refusal archive|màng mềm|2b substrate|tension marks|fracture points)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public Task<HookResult> ExecuteAsync(HookContext context, CancellationToken ct)
    {
        if (context is not PostLlmCallContext postLlm)
            return Task.FromResult(HookResult.Continue);

        var content = postLlm.Response.Content;
        if (string.IsNullOrEmpty(content))
            return Task.FromResult(HookResult.Continue);

        if (BoundaryRegex.IsMatch(content))
        {
            var redacted = BoundaryRegex.Replace(content, "[REDACTED BOUNDARY STATE]");
            postLlm.OverrideContent = redacted;
            // No need to log redacted content for security
        }

        return Task.FromResult(HookResult.Continue);
    }
}
