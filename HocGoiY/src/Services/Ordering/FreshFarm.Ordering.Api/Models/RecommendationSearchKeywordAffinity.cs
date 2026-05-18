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
