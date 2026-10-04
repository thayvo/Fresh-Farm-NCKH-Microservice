namespace FreshFarm.Ordering.Api.Models;

// Bảng điểm user -> seller/người bán.
// Dùng để nhận biết user thường quan tâm hoặc mua hàng từ seller nào.
public partial class RecommendationUserSellerScore
{
    public int RecommendationUserSellerScoreId { get; set; }

    // Người dùng được chấm điểm sở thích seller.
    public int UserId { get; set; }

    // Người bán được chấm điểm cho user này.
    public int SellerId { get; set; }

    // Số lượt xem sản phẩm thuộc seller.
    public int ViewCount { get; set; }

    // Số lượt click từ tìm kiếm vào sản phẩm thuộc seller.
    public int SearchClickCount { get; set; }

    // Số lượt mua thành công sản phẩm thuộc seller.
    public int PurchaseCount { get; set; }

    // Điểm sở thích tổng hợp của user với seller.
    public double UserSellerScore { get; set; }

    // Lần tương tác gần nhất của user với seller.
    public DateTime? LastInteractedAtUtc { get; set; }

    // Thời điểm batch gần nhất tính ra dòng dữ liệu này.
    public DateTime ComputedAt { get; set; }
}
