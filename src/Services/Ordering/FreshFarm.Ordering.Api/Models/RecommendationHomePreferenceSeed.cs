namespace FreshFarm.Ordering.Api.Models;

// Bảng seed sở thích cho trang chủ.
// Seed là sản phẩm mà user hoặc session vừa thể hiện quan tâm mạnh, dùng làm điểm xuất phát để mở rộng gợi ý.
public partial class RecommendationHomePreferenceSeed
{
    public int RecommendationHomePreferenceSeedId { get; set; }

    // Loại phạm vi: thường là "user" hoặc "session".
    public string ScopeType { get; set; } = string.Empty;

    // Khóa của phạm vi, ví dụ user id dạng chuỗi hoặc session id.
    public string ScopeKey { get; set; } = string.Empty;

    // User thật nếu scope là user; null nếu chỉ có session ẩn danh.
    public int? UserId { get; set; }

    // Sản phẩm seed thể hiện sở thích ban đầu.
    public int ProductId { get; set; }

    // Số lượt xem sản phẩm seed.
    public int ViewCount { get; set; }

    // Số lượt click từ tìm kiếm vào sản phẩm seed.
    public int SearchClickCount { get; set; }

    // Số lượt click từ vùng gợi ý vào sản phẩm seed.
    public int RecommendationClickCount { get; set; }

    // Số lượt mua sản phẩm seed.
    public int PurchaseCount { get; set; }

    // Điểm sở thích của seed trong phạm vi hiện tại.
    public double PreferenceScore { get; set; }

    // Lần tương tác gần nhất với seed.
    public DateTime? LastInteractedAtUtc { get; set; }

    // Thời điểm batch gần nhất tính ra dòng dữ liệu này.
    public DateTime ComputedAt { get; set; }
}
