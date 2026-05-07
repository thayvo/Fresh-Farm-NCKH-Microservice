// Nguon goc: src\Services\Ordering\FreshFarm.Ordering.Api\Models\RecommendationReplenishmentProfile.cs
// Duoc sao chep tu: D:\NCKH\DOAN\NCKH-FRESH-FARM\src\Services\Ordering\FreshFarm.Ordering.Api\Models\RecommendationReplenishmentProfile.cs
// Thu muc hoc tap: HocGoiY

namespace FreshFarm.Ordering.Api.Models;

public sealed class RecommendationReplenishmentProfile
{
    public int RecommendationReplenishmentProfileId { get; set; }

    public int UserId { get; set; }

    public int ProductId { get; set; }

    public int PurchaseCount { get; set; }

    public DateTime LastPurchasedAtUtc { get; set; }

    public double AverageRepurchaseDays { get; set; }

    public DateTime? ExpectedReorderAtUtc { get; set; }

    public double ReplenishmentScore { get; set; }

    public DateTime ComputedAt { get; set; }
}

