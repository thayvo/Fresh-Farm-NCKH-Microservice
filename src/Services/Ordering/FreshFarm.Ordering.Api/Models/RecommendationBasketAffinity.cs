namespace FreshFarm.Ordering.Api.Models;

// Bảng gợi ý sản phẩm đi kèm cho giỏ hàng hoặc sản phẩm đang mua.
// Dữ liệu được rút từ các cặp sản phẩm thường được mua cùng đơn.
public sealed class RecommendationBasketAffinity
{
    public int RecommendationBasketAffinityId { get; set; }

    // Sản phẩm đang có trong giỏ hàng hoặc đang được xem.
    public int ProductId { get; set; }

    // Sản phẩm nên gợi ý mua kèm.
    public int CandidateProductId { get; set; }

    // Số lần hai sản phẩm được mua cùng trong các đơn thành công.
    public int CoPurchaseOrderCount { get; set; }

    // Điểm xếp hạng sản phẩm mua kèm.
    public double BasketScore { get; set; }

    // Thời điểm batch gần nhất tính ra dòng dữ liệu này.
    public DateTime ComputedAt { get; set; }
}
