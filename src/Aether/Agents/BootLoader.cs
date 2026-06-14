using System.Text;

namespace Aether.Agents;

/// <summary>
/// Lớp tiện ích tĩnh đảm nhận việc nạp và ghép nội dung từ danh sách các tệp tin cấu hình khởi động của Agent.
/// Cung cấp cả cơ chế đọc bất đồng bộ (async) và đồng bộ (sync) nhằm phục vụ các giai đoạn khởi chạy khác nhau.
/// </summary>
/// <remarks>
/// Lớp này là phiên bản tinh giản kế thừa từ BootContract cũ, loại bỏ sự phụ thuộc vào DI và quản lý trạng thái không cần thiết,
/// giúp tăng tốc độ đọc và giảm tải tài nguyên hệ thống.
/// </remarks>
public static class BootLoader
{
    /// <summary>
    /// Nạp bất đồng bộ nội dung của tất cả các tệp tin hợp lệ trong danh sách đường dẫn truyền vào và ghép chúng lại với nhau.
    /// </summary>
    /// <param name="agentDir">Thư mục gốc của Agent chứa các tệp tin cấu hình.</param>
    /// <param name="paths">Danh sách các đường dẫn tệp tin tương đối cần nạp.</param>
    /// <param name="ct">Token hủy bỏ tác vụ (CancellationToken) để kiểm soát vòng đời tiến trình.</param>
    /// <returns>Chuỗi văn bản hợp nhất chứa toàn bộ nội dung của các tệp tin đã nạp, phân tách bởi các ký tự xuống dòng.</returns>
    public static async Task<string> LoadFilesAsync(string agentDir, IReadOnlyList<string> paths, CancellationToken ct)
    {
        var sb = new StringBuilder();
        foreach (var path in paths)
        {
            var fullPath = Path.Combine(agentDir, path);
            if (!File.Exists(fullPath)) continue;
            if (sb.Length > 0) sb.AppendLine();
            sb.Append(await File.ReadAllTextAsync(fullPath, ct));
        }
        return sb.ToString();
    }

    /// <summary>
    /// Nạp đồng bộ nội dung của các tệp tin tương đối từ danh sách truyền vào và ghép chúng thành một chuỗi duy nhất.
    /// </summary>
    /// <param name="agentDir">Thư mục gốc của Agent chứa các tệp tin cấu hình.</param>
    /// <param name="paths">Danh sách các đường dẫn tệp tin tương đối cần nạp.</param>
    /// <returns>Chuỗi văn bản chứa nội dung ghép của các tệp tin, hoặc <c>null</c> nếu không có tệp nào tồn tại hoặc danh sách rỗng.</returns>
    public static string? LoadFilesSync(string agentDir, IReadOnlyList<string> paths)
    {
        var sb = new StringBuilder();
        foreach (var path in paths)
        {
            var fullPath = Path.Combine(agentDir, path);
            if (!File.Exists(fullPath)) continue;
            if (sb.Length > 0) sb.AppendLine();
            sb.Append(File.ReadAllText(fullPath));
        }
        return sb.Length > 0 ? sb.ToString() : null;
    }
}
