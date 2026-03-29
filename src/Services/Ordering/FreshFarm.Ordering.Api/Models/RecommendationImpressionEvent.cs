namespace FreshFarm.Ordering.Api.Models;

public partial class RecommendationImpressionEvent
{
    public int RecommendationImpressionEventId { get; set; }

    public int? UserId { get; set; }

    public string SessionId { get; set; } = string.Empty;

    public string Placement { get; set; } = string.Empty;

    public string? RecommendationRunId { get; set; }

    public int ProductId { get; set; }

    public int Rank { get; set; }

    public string Algorithm { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }
}
