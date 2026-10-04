namespace FreshFarm.Ordering.Api.Models;

// Bảng điểm user -> danh mục.
// Dữ liệu được suy ra từ các sản phẩm user đã xem/click/mua và mapping sản phẩm sang danh mục.
public partial class RecommendationUserCategoryScore
{
    public int RecommendationUserCategoryScoreId { get; set; }

    // Người dùng được chấm điểm sở thích danh mục.
    public int UserId { get; set; }

    // Danh mục sản phẩm.
    public int CategoryId { get; set; }

    // Tên danh mục tại thời điểm tính điểm để phục vụ đọc/hiển thị nhanh.
    public string CategoryName { get; set; } = string.Empty;

    // Tổng view của các sản phẩm thuộc danh mục này.
    public int ViewCount { get; set; }

    // Tổng click từ tìm kiếm của các sản phẩm thuộc danh mục này.
    public int SearchClickCount { get; set; }

    // Tổng click từ vùng gợi ý của các sản phẩm thuộc danh mục này.
    public int RecommendationClickCount { get; set; }

    // Tổng mua thành công của các sản phẩm thuộc danh mục này.
    public int PurchaseCount { get; set; }

    // Điểm sở thích tổng hợp của user với danh mục.
    public double UserCategoryScore { get; set; }

    // Lần tương tác gần nhất của user với danh mục.
    public DateTime? LastInteractedAtUtc { get; set; }

    // Thời điểm batch gần nhất tính ra dòng dữ liệu này.
    public DateTime ComputedAt { get; set; }
}
