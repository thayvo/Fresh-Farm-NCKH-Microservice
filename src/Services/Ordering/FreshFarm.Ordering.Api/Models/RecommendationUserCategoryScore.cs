namespace FreshFarm.Ordering.Api.Models;

public partial class RecommendationUserCategoryScore
{
    public int RecommendationUserCategoryScoreId { get; set; }

    public int UserId { get; set; }

    public int CategoryId { get; set; }

    public string CategoryName { get; set; } = string.Empty;

    public int ViewCount { get; set; }

    public int SearchClickCount { get; set; }

    public int RecommendationClickCount { get; set; }

    public int PurchaseCount { get; set; }

    public double UserCategoryScore { get; set; }

    public DateTime? LastInteractedAtUtc { get; set; }

    public DateTime ComputedAt { get; set; }
}
