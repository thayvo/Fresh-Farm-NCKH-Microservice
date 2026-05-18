namespace FreshFarm.Ordering.Api.Models;

public partial class RecommendationUserSellerScore
{
    public int RecommendationUserSellerScoreId { get; set; }

    public int UserId { get; set; }

    public int SellerId { get; set; }

    public int ViewCount { get; set; }

    public int SearchClickCount { get; set; }

    public int PurchaseCount { get; set; }

    public double UserSellerScore { get; set; }

    public DateTime? LastInteractedAtUtc { get; set; }

    public DateTime ComputedAt { get; set; }
}
