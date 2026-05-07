// Nguon goc: src\Services\Ordering\FreshFarm.Ordering.Api\Models\RecommendationProductAffinity.cs
// Duoc sao chep tu: D:\NCKH\DOAN\NCKH-FRESH-FARM\src\Services\Ordering\FreshFarm.Ordering.Api\Models\RecommendationProductAffinity.cs
// Thu muc hoc tap: HocGoiY

namespace FreshFarm.Ordering.Api.Models;

public partial class RecommendationProductAffinity
{
    public int RecommendationProductAffinityId { get; set; }

    public int SeedProductId { get; set; }

    public int CandidateProductId { get; set; }

    public int CoPurchaseOrderCount { get; set; }

    public int CoViewSessionCount { get; set; }

    public int CoClickSessionCount { get; set; }

    public double AffinityScore { get; set; }

    public DateTime ComputedAt { get; set; }
}

