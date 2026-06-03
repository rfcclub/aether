namespace Aether.Agents;

/// <summary>
/// Các trạng thái vòng đời của một mảnh ký ức (Memory Entity) trong hệ thống nhận thức FEOFALLS.
/// </summary>
public enum MemoryLifecycleState
{
    /// <summary>Ký ức đang hoạt động, có độ nóng hổi cao, thường xuyên được truy xuất.</summary>
    Active,
    /// <summary>Ký ức phai màu, ít truy cập theo thời gian, bắt đầu giảm tính quan trọng.</summary>
    Decaying,
    /// <summary>Ký ức đã lưu trữ dài hạn, được nén lại để tối ưu hóa không gian ngữ cảnh.</summary>
    Archived,
    /// <summary>Ký ức hợp nhất, đã được tổng hợp thành kinh nghiệm/kiến thức nền tảng của Agent.</summary>
    Consolidated
}

/// <summary>
/// Máy trạng thái vòng đời ký ức (LifecycleStateMachine).
/// Hiện thực hóa quy trình chuyển đổi trạng thái của các thực thể ký ức: ACTIVE → DECAYING → ARCHIVED → CONSOLIDATED.
/// Tính toán mức độ nổi bật (Salience score) của ký ức hao mòn theo hàm logarit log(access_count + 1) và hàm số mũ thời gian.
/// </summary>
public sealed class LifecycleStateMachine
{
    private readonly BootConfig _config;

    /// <summary>
    /// Khởi tạo LifecycleStateMachine dựa trên cấu hình BootConfig.
    /// </summary>
    /// <param name="config">Cấu hình chứa các tham số đếm số ngày chuyển đổi trạng thái ký ức.</param>
    public LifecycleStateMachine(BootConfig config)
    {
        _config = config;
    }

    /// <summary>
    /// Phân tích và tính toán trạng thái hiện tại của một thực thể ký ức dựa trên tuổi thọ và tần suất truy cập.
    /// </summary>
    /// <param name="createdAt">Thời điểm ký ức được tạo lập.</param>
    /// <param name="lastAccessed">Lần cuối cùng ký ức được truy xuất.</param>
    /// <param name="accessCount">Tổng số lần ký ức được truy xuất.</param>
    /// <returns>Trạng thái vòng đời mới của ký ức (Active, Decaying, hoặc Archived).</returns>
    public MemoryLifecycleState ComputeState(
        DateTime createdAt,
        DateTime lastAccessed,
        int accessCount)
    {
        var sinceAccess = DateTime.UtcNow - lastAccessed;

        if (sinceAccess.TotalDays > _config.DecayingToArchivedDays)
            return MemoryLifecycleState.Archived;

        if (sinceAccess.TotalDays > _config.ActiveToDecayingDays && accessCount < 2)
            return MemoryLifecycleState.Decaying;

        return MemoryLifecycleState.Active;
    }

    /// <summary>
    /// Tính toán chỉ số nổi bật (Salience score) của ký ức. Chỉ số này đại diện cho tầm quan trọng của thông tin
    /// và được sử dụng để lọc ký ức khi lắp ráp ngữ cảnh.
    /// </summary>
    /// <param name="accessCount">Số lần truy cập ký ức.</param>
    /// <param name="lastAccessAge">Độ tuổi của lần truy cập cuối cùng.</param>
    /// <returns>Chỉ số nổi bật nằm trong khoảng từ [0.0, 1.0].</returns>
    public double ComputeSalience(int accessCount, TimeSpan lastAccessAge)
    {
        var rawScore = Math.Log2(accessCount + 1) / Math.Log2(100);
        var decayDays = Math.Max(0, lastAccessAge.TotalDays);
        var decay = Math.Exp(-decayDays / 90.0);
        return Math.Clamp(rawScore * decay, 0.0, 1.0);
    }
}
