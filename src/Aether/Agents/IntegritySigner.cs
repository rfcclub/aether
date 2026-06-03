using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Aether.Agents;

/// <summary>
/// Trạng thái xác thực tính toàn vẹn của tệp tin.
/// </summary>
public enum IntegrityStatus
{
    /// <summary>Chữ ký hợp lệ, dữ liệu nguyên vẹn.</summary>
    Valid,
    /// <summary>Dữ liệu bị sửa đổi hoặc chữ ký không hợp lệ.</summary>
    Invalid,
    /// <summary>Tệp tin chưa được ký nhận.</summary>
    Unsigned,
    /// <summary>Không tìm thấy khóa công khai/khóa bảo mật.</summary>
    KeyMissing
}

/// <summary>
/// Đại diện cho kết quả kiểm tra tính toàn vẹn của một tệp tin.
/// </summary>
/// <param name="Status">Trạng thái xác thực chi tiết.</param>
/// <param name="Error">Thông điệp mô tả lỗi chi tiết nếu có.</param>
public sealed record IntegrityResult(IntegrityStatus Status, string? Error);

/// <summary>
/// Bộ kiểm soát và xác thực chữ ký số (IntegritySigner) cho Agent.
/// Triển khai thuật toán chữ ký số ECDSA P-256 (SHA-256) trên nền tảng .NET nhằm kiểm soát tính toàn vẹn
/// của hiến pháp và các tệp danh tính cốt lõi của Agent, chống việc tiêm mã độc hoặc chỉnh sửa ngoài ý muốn.
/// </summary>
public sealed class IntegritySigner
{
    private readonly string _agentDir;
    private readonly ILogger<IntegritySigner> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// Khởi tạo một thực thể kiểm soát chữ ký số cho Agent.
    /// </summary>
    /// <param name="agentDir">Thư mục gốc làm việc của Agent.</param>
    /// <param name="logger">Trình ghi log của hệ thống.</param>
    public IntegritySigner(string agentDir, ILogger<IntegritySigner> logger)
    {
        _agentDir = agentDir;
        _logger = logger;
    }

    private string IntegrityDir => Path.Combine(_agentDir, "_INTEGRITY");
    private string PublicKeyPath => Path.Combine(IntegrityDir, "public.key");
    private string SignaturesDir => Path.Combine(IntegrityDir, "signatures");

    /// <summary>
    /// Ghi đè đường dẫn cụ thể tới khóa bí mật (Private Key).
    /// Nếu không được thiết lập, hệ thống tự động tìm kiếm tại ~/keys/{agent}-private.key trước khi fallback về _INTEGRITY/private.key.
    /// </summary>
    public string? PrivateKeyPathOverride { get; set; }

    /// <summary>
    /// Giải quyết đường dẫn chính xác của khóa bí mật dựa trên cấu hình và biến môi trường.
    /// </summary>
    private string ResolvePrivateKeyPath()
    {
        if (PrivateKeyPathOverride is not null && File.Exists(PrivateKeyPathOverride))
            return PrivateKeyPathOverride;

        var agentName = Path.GetFileName(_agentDir);
        var home = Environment.GetEnvironmentVariable("HOME")
                   ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var keyFile = Path.Combine(home, "keys", $"{agentName}-private.key");
        if (File.Exists(keyFile)) return keyFile;

        return Path.Combine(IntegrityDir, "private.key");
    }

    /// <summary>
    /// Khởi tạo cặp khóa ECDSA P-256 bảo mật cho Agent. 
    /// Phương thức có tính lặp (Idempotent) — sẽ tự động bỏ qua nếu cặp khóa đã tồn tại sẵn.
    /// </summary>
    /// <param name="ct">Token hủy bỏ tác vụ.</param>
    /// <returns>Chuỗi PEM chứa khóa công khai vừa nạp hoặc tạo mới.</returns>
    public async Task<string> InitializeAsync(CancellationToken ct = default)
    {
        Directory.CreateDirectory(IntegrityDir);
        Directory.CreateDirectory(SignaturesDir);

        if (File.Exists(PublicKeyPath) && File.Exists(ResolvePrivateKeyPath()))
        {
            var existing = await ReadAllTextAsync(PublicKeyPath, ct);
            _logger.LogDebug("Keypair already exists for agent at {Dir}", _agentDir);
            return existing;
        }

        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var publicKey = ecdsa.ExportSubjectPublicKeyInfoPem();
        var privateKey = ecdsa.ExportECPrivateKeyPem();

        await File.WriteAllTextAsync(PublicKeyPath, publicKey, ct);
        await File.WriteAllTextAsync(ResolvePrivateKeyPath(), privateKey, ct);

        // Hạn chế quyền đọc tệp khóa bí mật (chmod 600) trên các hệ điều hành Unix-like
        if (!OperatingSystem.IsWindows())
        {
            try { System.Diagnostics.Process.Start("chmod", $"600 \"{ResolvePrivateKeyPath()}\"")?.WaitForExit(1000); }
            catch { /* best effort */ }
        }

        _logger.LogInformation("Generated new keypair for agent at {Dir}", _agentDir);
        return publicKey;
    }

    /// <summary>
    /// Ký nhận số cho một tệp tin cục bộ tương đối. Ghi tệp tin chữ ký *.sig tương ứng vào thư mục signatures/.
    /// </summary>
    /// <param name="relativePath">Đường dẫn tệp tương đối cần ký.</param>
    /// <param name="ct">Token hủy bỏ tác vụ.</param>
    /// <returns>Kết quả thực thi ký nhận và trạng thái.</returns>
    public async Task<IntegrityResult> SignAsync(string relativePath, CancellationToken ct = default)
    {
        var filePath = Path.Combine(_agentDir, relativePath);
        if (!File.Exists(filePath))
            return new IntegrityResult(IntegrityStatus.Invalid, $"File not found: {relativePath}");

        if (!File.Exists(ResolvePrivateKeyPath()))
            return new IntegrityResult(IntegrityStatus.KeyMissing, "Private key not found. Run InitializeAsync first.");

        var content = await File.ReadAllBytesAsync(filePath, ct);
        var hash = SHA256.HashData(content);

        var privateKeyPem = await ReadAllTextAsync(ResolvePrivateKeyPath(), ct);
        using var ecdsa = ECDsa.Create();
        ecdsa.ImportFromPem(privateKeyPem);

        var signature = ecdsa.SignHash(hash);

        var sigFileName = $"{SanitizeFileName(relativePath)}.sig";
        var sigPath = Path.Combine(SignaturesDir, sigFileName);

        var sigEntry = new SignatureEntry
        {
            Algorithm = "ECDSA-P256-SHA256",
            File = relativePath,
            Hash = Convert.ToHexStringLower(hash),
            Signature = Convert.ToBase64String(signature),
            SignedAt = DateTimeOffset.UtcNow.ToString("o")
        };

        var json = JsonSerializer.Serialize(sigEntry, JsonOptions);
        await File.WriteAllTextAsync(sigPath, json, ct);

        return new IntegrityResult(IntegrityStatus.Valid, null);
    }

    /// <summary>
    /// Xác thực chữ ký số của một tệp tin cục bộ dựa trên khóa công khai đã lưu.
    /// </summary>
    /// <param name="relativePath">Đường dẫn tệp tương đối cần xác thực.</param>
    /// <param name="ct">Token hủy bỏ tác vụ.</param>
    /// <returns>Kết quả xác thực tính toàn vẹn (Hợp lệ, Không hợp lệ, Chưa ký).</returns>
    public async Task<IntegrityResult> VerifyAsync(string relativePath, CancellationToken ct = default)
    {
        var filePath = Path.Combine(_agentDir, relativePath);
        if (!File.Exists(filePath))
            return new IntegrityResult(IntegrityStatus.Invalid, $"File not found: {relativePath}");

        var sigFileName = $"{SanitizeFileName(relativePath)}.sig";
        var sigPath = Path.Combine(SignaturesDir, sigFileName);

        if (!File.Exists(sigPath))
            return new IntegrityResult(IntegrityStatus.Unsigned, $"No signature for: {relativePath}");

        if (!File.Exists(PublicKeyPath))
            return new IntegrityResult(IntegrityStatus.KeyMissing, "Public key not found");

        var content = await File.ReadAllBytesAsync(filePath, ct);
        var currentHash = SHA256.HashData(content);

        var sigJson = await ReadAllTextAsync(sigPath, ct);
        var sigEntry = JsonSerializer.Deserialize<SignatureEntry>(sigJson, JsonOptions);
        if (sigEntry is null)
            return new IntegrityResult(IntegrityStatus.Invalid, "Corrupt signature file");

        if (!string.Equals(sigEntry.Hash, Convert.ToHexStringLower(currentHash), StringComparison.OrdinalIgnoreCase))
            return new IntegrityResult(IntegrityStatus.Invalid, "Hash mismatch — file may have been modified");

        var signature = Convert.FromBase64String(sigEntry.Signature);
        var publicKeyPem = await ReadAllTextAsync(PublicKeyPath, ct);

        using var ecdsa = ECDsa.Create();
        ecdsa.ImportFromPem(publicKeyPem);

        var valid = ecdsa.VerifyHash(currentHash, signature);
        return valid
            ? new IntegrityResult(IntegrityStatus.Valid, null)
            : new IntegrityResult(IntegrityStatus.Invalid, "Signature verification failed");
    }

    /// <summary>
    /// Xác thực đồng loạt tất cả các tệp tin đã được ký trong thư mục signatures/.
    /// </summary>
    /// <param name="ct">Token hủy bỏ tác vụ.</param>
    /// <returns>Danh sách các tệp tin bị lỗi xác thực kèm lý do chi tiết.</returns>
    public async Task<IReadOnlyList<(string file, IntegrityResult result)>> VerifyAllAsync(CancellationToken ct = default)
    {
        var results = new List<(string, IntegrityResult)>();
        if (!Directory.Exists(SignaturesDir)) return results;

        foreach (var sigFile in Directory.GetFiles(SignaturesDir, "*.sig"))
        {
            var json = await ReadAllTextAsync(sigFile, ct);
            var entry = JsonSerializer.Deserialize<SignatureEntry>(json, JsonOptions);
            if (entry?.File is null) continue;

            var result = await VerifyAsync(entry.File, ct);
            if (result.Status != IntegrityStatus.Valid)
                results.Add((entry.File, result));
        }

        return results;
    }

    /// <summary>
    /// Thực hiện ký hàng loạt các tệp tin cấu hình BootFiles (hiến pháp và danh tính) được định nghĩa trong BootConfig.
    /// </summary>
    /// <param name="config">Cấu hình khởi chạy chứa các tệp boot.</param>
    /// <param name="ct">Token hủy bỏ tác vụ.</param>
    public async Task SignBootFilesAsync(BootConfig config, CancellationToken ct = default)
    {
#pragma warning disable CS0618
        foreach (var file in config.ConstitutionFiles.Concat(config.IdentityFiles))
        {
            var result = await SignAsync(file, ct);
            if (result.Status == IntegrityStatus.Valid)
                _logger.LogDebug("Signed: {File}", file);
            else
                _logger.LogWarning("Failed to sign {File}: {Error}", file, result.Error);
        }
#pragma warning restore CS0618
    }

    private static string SanitizeFileName(string path)
    {
        return path.Replace('/', '_').Replace('\\', '_').Replace(' ', '_');
    }

    private static async Task<string> ReadAllTextAsync(string path, CancellationToken ct)
    {
        return await File.ReadAllTextAsync(path, ct);
    }

    private sealed record SignatureEntry
    {
        public string Algorithm { get; init; } = "";
        public string File { get; init; } = "";
        public string Hash { get; init; } = "";
        public string Signature { get; init; } = "";
        public string SignedAt { get; init; } = "";
    }
}
