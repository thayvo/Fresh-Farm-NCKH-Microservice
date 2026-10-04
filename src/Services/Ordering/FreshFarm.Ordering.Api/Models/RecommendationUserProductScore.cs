namespace FreshFarm.Ordering.Api.Models;

// Bảng điểm user -> sản phẩm.
// Mỗi dòng thể hiện mức độ một user quan tâm tới một sản phẩm cụ thể.
public sealed class RecommendationUserProductScore
{
    public int RecommendationUserProductScoreId { get; set; }

    // Người dùng được chấm điểm sở thích.
    public int UserId { get; set; }

    // Sản phẩm được chấm điểm cho user này.
    public int ProductId { get; set; }

    // Số lượt xem sản phẩm của user.
    public int ViewCount { get; set; }

    // Số lượt user click sản phẩm từ kết quả tìm kiếm.
    public int SearchClickCount { get; set; }

    // Số lượt user click sản phẩm từ vùng gợi ý.
    public int RecommendationClickCount { get; set; }

    // Số lượng/lần mua thành công của user với sản phẩm.
    public int PurchaseCount { get; set; }

    // Điểm sở thích tổng hợp, dùng để xếp hạng sản phẩm cho user.
    public double UserProductScore { get; set; }

    // Lần tương tác gần nhất để ưu tiên tín hiệu mới hơn khi cần.
    public DateTime? LastInteractedAtUtc { get; set; }

    // Thời điểm batch gần nhất tính ra dòng dữ liệu này.
    public DateTime ComputedAt { get; set; }
}
