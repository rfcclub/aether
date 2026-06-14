using System.Collections.Concurrent;
using System.Threading;

namespace Aether.Channels;

/// <summary>
/// Registry to track active session CancellationTokens for real-time Esc key cancellation.
/// </summary>
public static class SessionCancellationRegistry
{
    private static readonly ConcurrentDictionary<string, CancellationTokenSource> ActiveTokens = new();

    public static void Register(string chatId, CancellationTokenSource cts)
    {
        ActiveTokens[chatId] = cts;
    }

    public static bool TryGet(string chatId, out CancellationTokenSource? cts)
    {
        return ActiveTokens.TryGetValue(chatId, out cts);
    }

    public static void Remove(string chatId)
    {
        ActiveTokens.TryRemove(chatId, out _);
    }
}
