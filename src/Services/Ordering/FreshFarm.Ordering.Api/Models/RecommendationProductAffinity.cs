namespace FreshFarm.Ordering.Api.Models;

// Bảng gợi ý sản phẩm -> sản phẩm.
// Mỗi dòng nói rằng từ SeedProductId có thể gợi ý CandidateProductId dựa trên mua/xem/click cùng nhau.
public partial class RecommendationProductAffinity
{
    public int RecommendationProductAffinityId { get; set; }

    // Sản phẩm gốc, ví dụ sản phẩm user đang xem.
    public int SeedProductId { get; set; }

    // Sản phẩm ứng viên được gợi ý kèm theo sản phẩm gốc.
    public int CandidateProductId { get; set; }

    // Số đơn hàng trong đó hai sản phẩm xuất hiện cùng nhau.
    public int CoPurchaseOrderCount { get; set; }

    // Số phiên mà hai sản phẩm được xem cùng nhau.
    public int CoViewSessionCount { get; set; }

    // Số phiên mà hai sản phẩm được click cùng nhau.
    public int CoClickSessionCount { get; set; }

    // Điểm tổng hợp để xếp hạng quan hệ sản phẩm -> sản phẩm.
    public double AffinityScore { get; set; }

    // Thời điểm batch gần nhất tính ra dòng dữ liệu này.
    public DateTime ComputedAt { get; set; }
}
