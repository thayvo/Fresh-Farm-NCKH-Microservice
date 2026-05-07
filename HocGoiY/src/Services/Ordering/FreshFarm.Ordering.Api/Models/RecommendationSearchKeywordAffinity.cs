// Nguon goc: src\Services\Ordering\FreshFarm.Ordering.Api\Models\RecommendationSearchKeywordAffinity.cs
// Duoc sao chep tu: D:\NCKH\DOAN\NCKH-FRESH-FARM\src\Services\Ordering\FreshFarm.Ordering.Api\Models\RecommendationSearchKeywordAffinity.cs
// Thu muc hoc tap: HocGoiY

namespace FreshFarm.Ordering.Api.Models;

public partial class RecommendationSearchKeywordAffinity
{
    public int RecommendationSearchKeywordAffinityId { get; set; }

    public string Keyword { get; set; } = string.Empty;

    public int ProductId { get; set; }

    public int SearchClickCount { get; set; }

    public int SearchClickSessionCount { get; set; }

    public int SearchViewSessionCount { get; set; }

    public int SearchRecommendationClickCount { get; set; }

    public double HybridSearchScore { get; set; }

    public DateTime ComputedAt { get; set; }
}

