// Nguon goc: src\Services\Ordering\FreshFarm.Ordering.Api\Models\RecommendationUserProductScore.cs
// Duoc sao chep tu: D:\NCKH\DOAN\NCKH-FRESH-FARM\src\Services\Ordering\FreshFarm.Ordering.Api\Models\RecommendationUserProductScore.cs
// Thu muc hoc tap: HocGoiY

namespace FreshFarm.Ordering.Api.Models;

public sealed class RecommendationUserProductScore
{
    public int RecommendationUserProductScoreId { get; set; }

    public int UserId { get; set; }

    public int ProductId { get; set; }

    public int ViewCount { get; set; }

    public int SearchClickCount { get; set; }

    public int RecommendationClickCount { get; set; }

    public int PurchaseCount { get; set; }

    public double UserProductScore { get; set; }

    public DateTime? LastInteractedAtUtc { get; set; }

    public DateTime ComputedAt { get; set; }
}

