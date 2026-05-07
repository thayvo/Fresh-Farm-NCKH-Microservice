// Nguon goc: src\Services\Ordering\FreshFarm.Ordering.Api\Models\RecommendationBasketAffinity.cs
// Duoc sao chep tu: D:\NCKH\DOAN\NCKH-FRESH-FARM\src\Services\Ordering\FreshFarm.Ordering.Api\Models\RecommendationBasketAffinity.cs
// Thu muc hoc tap: HocGoiY

namespace FreshFarm.Ordering.Api.Models;

public sealed class RecommendationBasketAffinity
{
    public int RecommendationBasketAffinityId { get; set; }

    public int ProductId { get; set; }

    public int CandidateProductId { get; set; }

    public int CoPurchaseOrderCount { get; set; }

    public double BasketScore { get; set; }

    public DateTime ComputedAt { get; set; }
}

