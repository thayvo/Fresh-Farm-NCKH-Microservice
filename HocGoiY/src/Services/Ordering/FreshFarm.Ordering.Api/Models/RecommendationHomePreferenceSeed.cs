// Nguon goc: src\Services\Ordering\FreshFarm.Ordering.Api\Models\RecommendationHomePreferenceSeed.cs
// Duoc sao chep tu: D:\NCKH\DOAN\NCKH-FRESH-FARM\src\Services\Ordering\FreshFarm.Ordering.Api\Models\RecommendationHomePreferenceSeed.cs
// Thu muc hoc tap: HocGoiY

namespace FreshFarm.Ordering.Api.Models;

public partial class RecommendationHomePreferenceSeed
{
    public int RecommendationHomePreferenceSeedId { get; set; }

    public string ScopeType { get; set; } = string.Empty;

    public string ScopeKey { get; set; } = string.Empty;

    public int? UserId { get; set; }

    public int ProductId { get; set; }

    public int ViewCount { get; set; }

    public int SearchClickCount { get; set; }

    public int RecommendationClickCount { get; set; }

    public int PurchaseCount { get; set; }

    public double PreferenceScore { get; set; }

    public DateTime? LastInteractedAtUtc { get; set; }

    public DateTime ComputedAt { get; set; }
}

