namespace FreshFarm.Ordering.Api.Models;

// Bảng hồ sơ mua lại định kỳ user -> sản phẩm.
// Dùng để gợi ý mua lại khi user có lịch sử mua lặp lại một sản phẩm.
public sealed class RecommendationReplenishmentProfile
{
    public int RecommendationReplenishmentProfileId { get; set; }

    // Người dùng có lịch sử mua lặp lại.
    public int UserId { get; set; }

    // Sản phẩm có khả năng cần mua lại.
    public int ProductId { get; set; }

    // Tổng số lần mua thành công trong khoảng phân tích.
    public int PurchaseCount { get; set; }

    // Lần mua gần nhất.
    public DateTime LastPurchasedAtUtc { get; set; }

    // Số ngày trung bình giữa các lần mua.
    public double AverageRepurchaseDays { get; set; }

    // Dự đoán thời điểm user có thể cần mua lại.
    public DateTime? ExpectedReorderAtUtc { get; set; }

    // Điểm ưu tiên gợi ý mua lại.
    public double ReplenishmentScore { get; set; }

    // Thời điểm batch gần nhất tính ra dòng dữ liệu này.
    public DateTime ComputedAt { get; set; }
}
