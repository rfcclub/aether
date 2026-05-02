using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace Aether.Channels;

public enum AccessResult
{
    Allowed,
    NeedsPairing,
    Denied
}

public sealed record ChannelAccessState
{
    public string Mode { get; init; } = "pairing"; // open | pairing | allowlist
    public HashSet<string> Allowed { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, PendingPairing> Pending { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed record PendingPairing
{
    public string SenderId { get; init; } = "";
    public string Timestamp { get; init; } = "";
}

public sealed class ChannelAccess
{
    private readonly string _channelName;
    private readonly string _baseDir;
    private readonly ILogger<ChannelAccess> _logger;
    private ChannelAccessState _state = new();
    private readonly SemaphoreSlim _lock = new(1, 1);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public ChannelAccess(string channelName, string aetherDir, ILogger<ChannelAccess> logger)
    {
        _channelName = channelName;
        _baseDir = Path.Combine(aetherDir, "channels", channelName);
        _logger = logger;
    }

    public async Task LoadAsync(CancellationToken ct = default)
    {
        Directory.CreateDirectory(_baseDir);

        var path = GetAccessPath();
        if (!File.Exists(path))
        {
            _state = new ChannelAccessState();
            await SaveAsync(ct);
            return;
        }

        var json = await File.ReadAllTextAsync(path, ct);
        var state = JsonSerializer.Deserialize<ChannelAccessState>(json, JsonOptions);
        _state = state ?? new ChannelAccessState();
    }

    public async Task<AccessResult> CheckAccessAsync(string senderId, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            if (_state.Allowed.Contains(senderId))
                return AccessResult.Allowed;

            return _state.Mode switch
            {
                "open" => AccessResult.Allowed,
                "pairing" => AccessResult.NeedsPairing,
                "allowlist" => AccessResult.Denied,
                _ => AccessResult.NeedsPairing
            };
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<string> RequestPairingAsync(string senderId, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            // Remove any existing pending pairings for this sender
            var existing = _state.Pending
                .Where(kvp => string.Equals(kvp.Value.SenderId, senderId, StringComparison.OrdinalIgnoreCase))
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var key in existing)
                _state.Pending.Remove(key);

            var code = GeneratePairingCode();
            _state.Pending[code] = new PendingPairing
            {
                SenderId = senderId,
                Timestamp = DateTimeOffset.UtcNow.ToString("o")
            };

            await SaveAsync(ct);
            return code;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<bool> ApprovePairingAsync(string code, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            if (!_state.Pending.TryGetValue(code, out var pairing))
                return false;

            _state.Pending.Remove(code);
            _state.Allowed.Add(pairing.SenderId);

            // First pairing auto-transitions from open to allowlist
            if (_state.Mode == "open")
                _state = _state with { Mode = "allowlist" };

            await SaveAsync(ct);
            return true;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task SetModeAsync(string mode, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            _state = _state with
            {
                Mode = mode switch
                {
                    "open" => "open",
                    "pairing" => "pairing",
                    "allowlist" => "allowlist",
                    _ => throw new ArgumentException($"Invalid mode: {mode}. Use open, pairing, or allowlist.")
                }
            };
            await SaveAsync(ct);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<bool> IsAllowedAsync(string senderId, CancellationToken ct = default)
    {
        var result = await CheckAccessAsync(senderId, ct);
        return result == AccessResult.Allowed;
    }

    private async Task SaveAsync(CancellationToken ct)
    {
        var path = GetAccessPath();
        var json = JsonSerializer.Serialize(_state, JsonOptions);
        await File.WriteAllTextAsync(path, json, ct);
    }

    private string GetAccessPath() => Path.Combine(_baseDir, "access.json");

    private static string GeneratePairingCode()
    {
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        var code = new char[6];
        var bytes = RandomNumberGenerator.GetBytes(6);
        for (var i = 0; i < 6; i++)
            code[i] = chars[bytes[i] % chars.Length];
        return new string(code);
    }
}
