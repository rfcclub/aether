using Aether.Agent;
using Aether.Plugins;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Aether.Agents;

/// <summary>
/// Dịch vụ phát nhịp sinh học (Heartbeat) định kỳ chạy ngầm dưới dạng IHostedService của .NET.
/// Đảm nhận việc đọc tệp tin HEARTBEAT.md định kỳ và gửi nội dung của nó qua AetherSoul để xử lý.
/// Hiện thực hóa mô hình nhịp sinh học OC: Thăm dò (poll) → Thực thi tác vụ (execute tasks) → Báo cáo (report).
/// </summary>
public sealed class AgentHeartbeatService : IHostedService, IDisposable
{
    private readonly AgentProfile _profile;
    private readonly AetherSoul _soul;
    private readonly AgentConfig _config;
    private readonly ILogger<AgentHeartbeatService> _logger;
    private readonly HookEngine? _hooks;
    private readonly TimeSpan _interval;
    private int _tickNumber;
    private DateTimeOffset? _lastTickAt;
    private Timer? _timer;
    private CancellationTokenSource? _cts;

    /// <summary>
    /// Khởi tạo một thực thể mới của dịch vụ nhịp tim sinh học AgentHeartbeatService.
    /// </summary>
    /// <param name="profile">Hồ sơ thông tin danh tính của Agent đang active.</param>
    /// <param name="soul">Lõi xử lý tư duy AetherSoul để gửi prompt nhịp tim xử lý.</param>
    /// <param name="config">Cấu hình chứa đường dẫn tệp tin nhịp tim.</param>
    /// <param name="logger">Trình ghi log của dịch vụ.</param>
    /// <param name="interval">Khoảng thời gian định kỳ giữa các nhịp (mặc định: 30 phút).</param>
    /// <param name="hooks">Engine chạy Hook điều phối hệ thống plugins.</param>
    public AgentHeartbeatService(
        AgentProfile profile,
        AetherSoul soul,
        AgentConfig config,
        ILogger<AgentHeartbeatService> logger,
        TimeSpan? interval = null,
        HookEngine? hooks = null)
    {
        _profile = profile;
        _soul = soul;
        _config = config;
        _logger = logger;
        _hooks = hooks;
        _interval = interval ?? TimeSpan.FromMinutes(30);
    }

    /// <summary>
    /// Bắt đầu dịch vụ nhịp sinh học chạy ngầm định kỳ.
    /// </summary>
    /// <param name="cancellationToken">CancellationToken đồng bộ vòng đời khởi động hệ thống.</param>
    /// <returns>Một tác vụ đại diện cho tiến trình khởi chạy.</returns>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (_config.HeartbeatFile is null)
        {
            _logger.LogInformation("Heartbeat disabled for agent {AgentName}", _profile.Name);
            return Task.CompletedTask;
        }

        _logger.LogInformation("Heartbeat starting for agent {AgentName} every {Interval}",
            _profile.Name, _interval);

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _timer = new Timer(async _ => await TickAsync(_cts.Token), null,
            TimeSpan.Zero, _interval);

        return Task.CompletedTask;
    }

    /// <summary>
    /// Dừng dịch vụ nhịp sinh học chạy ngầm một cách an toàn.
    /// </summary>
    /// <param name="cancellationToken">CancellationToken kiểm soát tiến trình dừng.</param>
    /// <returns>Một tác vụ đại diện cho việc dừng.</returns>
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Heartbeat stopping for agent {AgentName}", _profile.Name);
        _timer?.Change(Timeout.Infinite, 0);
        _cts?.Cancel();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Được kích hoạt ở mỗi chu kỳ Timer để nạp HEARTBEAT.md, kích hoạt Hook OnHeartbeatTick,
    /// và gửi tin nhắn tự suy ngẫm ngầm qua AetherSoul.
    /// </summary>
    /// <param name="ct">Token hủy bỏ tác vụ nếu hệ thống shutdown.</param>
    private async Task TickAsync(CancellationToken ct)
    {
        try
        {
            var now = DateTimeOffset.UtcNow;
            var tickNumber = Interlocked.Increment(ref _tickNumber);
            var timeSinceLastTick = _lastTickAt is null ? TimeSpan.Zero : now - _lastTickAt.Value;
            _lastTickAt = now;

            var heartbeatContent = await _profile.LoadFileAsync(_config.HeartbeatFile!, ct);
            if (heartbeatContent is null)
            {
                _logger.LogDebug("No heartbeat file found for {AgentName}", _profile.Name);
                return;
            }

            if (_hooks is not null)
            {
                var ctx = new OnHeartbeatTickContext
                {
                    AgentName = _profile.Name,
                    WorkspacePath = _profile.AgentDirectory,
                    TickNumber = tickNumber,
                    TimeSinceLastTick = timeSinceLastTick,
                    HeartbeatContent = heartbeatContent
                };
                await _hooks.RunAllAsync(HookPoint.OnHeartbeatTick, ctx, ct);
            }

            _logger.LogDebug("Heartbeat tick for {AgentName}", _profile.Name);
            // Gửi HEARTBEAT.md dưới dạng trạng thái công việc (working state), tránh nhầm lẫn với prompt của người dùng.
            // Điều này giúp Agent có thể xử lý các tác vụ nền định kỳ một cách chủ động.
            var response = await _soul.ProcessTaskAsync(_profile.Name, "Heartbeat tick.", heartbeatContent, ct);

            if (!response.Content.Contains("HEARTBEAT_OK"))
            {
                _logger.LogInformation("Heartbeat produced actionable output for {AgentName}", _profile.Name);
            }
        }
        catch (OperationCanceledException)
        {
            // Đang tắt dịch vụ
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Heartbeat tick failed for {AgentName}", _profile.Name);
        }
    }

    /// <summary>
    /// Giải phóng các tài nguyên Timer và CancellationToken của dịch vụ.
    /// </summary>
    public void Dispose()
    {
        _timer?.Dispose();
        _cts?.Dispose();
    }
}
