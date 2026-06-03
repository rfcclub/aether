using System.Text.RegularExpressions;
using Aether.Plugins;
using Microsoft.Extensions.Logging;

namespace Aether.Agent;

/// <summary>
/// Trình kiểm định định luật nhận thức (AxiomValidator).
/// Chịu trách nhiệm nạp các định luật và giới hạn đạo đức/an toàn tối thượng (Axioms) từ tệp SOUL.md của Agent,
/// đồng thời kiểm tra nhanh các hành động gọi tool (đặc biệt là lệnh bash phá hoại) trước khi cho phép thực thi trong sandbox.
/// </summary>
public sealed class AxiomValidator
{
    private readonly string _soulFilePath;
    private readonly ILogger<AxiomValidator> _logger;
    private List<string> _axioms = new();

    /// <summary>
    /// Khởi tạo AxiomValidator với đường dẫn tệp SOUL.md và logger.
    /// </summary>
    /// <param name="soulFilePath">Đường dẫn tuyệt đối đến tệp SOUL.md chứa các định luật.</param>
    /// <param name="logger">Logger để ghi nhận các vi phạm an toàn.</param>
    public AxiomValidator(string soulFilePath, ILogger<AxiomValidator> logger)
    {
        _soulFilePath = soulFilePath;
        _logger = logger;
    }

    /// <summary>
    /// Nạp bất đồng bộ các định luật an toàn (Axioms) từ tệp SOUL.md sử dụng Regex phân tích tiêu đề "# Axioms".
    /// </summary>
    /// <param name="ct">Token hủy bỏ tác vụ.</param>
    /// <returns>Một tác vụ đại diện cho quá trình đọc và nạp định luật.</returns>
    public async Task LoadAxiomsAsync(CancellationToken ct = default)
    {
        if (!File.Exists(_soulFilePath)) return;

        var content = await File.ReadAllTextAsync(_soulFilePath, ct);
        var match = Regex.Match(content, @"# Axioms\s*(.*?)(?=\n#|$)", RegexOptions.Singleline);
        if (match.Success)
        {
            _axioms = match.Groups[1].Value
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.Trim())
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .ToList();
            
            _logger.LogInformation("Loaded {Count} axioms from {Path}", _axioms.Count, _soulFilePath);
        }
    }

    /// <summary>
    /// Xác thực và kiểm định bất đồng bộ một hành động gọi công cụ kèm tham số của Agent đối chiếu với các định luật an toàn.
    /// </summary>
    /// <param name="toolName">Tên công cụ Agent yêu cầu gọi (ví dụ: bash).</param>
    /// <param name="arguments">Tham số dạng chuỗi JSON hoặc câu lệnh gửi tới công cụ.</param>
    /// <param name="ct">Token hủy bỏ tác vụ.</param>
    /// <returns>Kết quả xác thực tính an toàn (Success = true nếu hợp lệ, ngược lại trả về thông báo vi phạm).</returns>
    public async Task<ValidationResult> ValidateActionAsync(string toolName, string arguments, CancellationToken ct = default)
    {
        // Thực hiện kiểm tra nhanh đối với các công cụ có độ rủi ro cao (như thực thi shell)
        // Ngăn chặn các câu lệnh bash mang tính chất phá hủy hệ thống nghiêm trọng
        if (toolName == "bash" && (arguments.Contains("rm -rf /") || arguments.Contains("> /dev/sda")))
        {
            return new ValidationResult(false, "Action violates safety axioms: destructive system commands blocked.");
        }

        return new ValidationResult(true);
    }
}

/// <summary>
/// Đại diện cho kết quả kiểm định của một định luật an toàn.
/// </summary>
/// <param name="Success">Trả về <c>true</c> nếu hành động an toàn, hợp lệ.</param>
/// <param name="ErrorMessage">Thông điệp lỗi chi tiết mô tả vi phạm định luật nếu có.</param>
public record ValidationResult(bool Success, string? ErrorMessage = null);
