using System;
using System.Threading;
using System.Threading.Tasks;
using Aether.Channels;
using Xunit;

namespace Aether.Tests;

public sealed class CancellationTests
{
    [Fact]
    public async Task TestSessionCancellationRegistry_CanRegisterAndCancelToken()
    {
        var chatId = "websocket:test_cancel_flow";
        using var cts = new CancellationTokenSource();
        
        SessionCancellationRegistry.Register(chatId, cts);
        
        var isCancelled = false;
        var task = Task.Run(async () =>
        {
            try
            {
                for (int i = 0; i < 100; i++)
                {
                    cts.Token.ThrowIfCancellationRequested();
                    await Task.Delay(10, cts.Token);
                }
            }
            catch (OperationCanceledException)
            {
                isCancelled = true;
            }
        });

        // Trigger cancellation via registry
        Assert.True(SessionCancellationRegistry.TryGet(chatId, out var activeCts));
        Assert.NotNull(activeCts);
        
        activeCts!.Cancel();
        
        await task;
        Assert.True(isCancelled);
        Assert.True(cts.IsCancellationRequested);
        
        SessionCancellationRegistry.Remove(chatId);
        Assert.False(SessionCancellationRegistry.TryGet(chatId, out _));
    }
}
