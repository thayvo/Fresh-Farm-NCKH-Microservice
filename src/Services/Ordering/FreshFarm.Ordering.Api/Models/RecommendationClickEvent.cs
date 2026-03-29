namespace FreshFarm.Ordering.Api.Models;

public partial class RecommendationClickEvent
{
    public int RecommendationClickEventId { get; set; }

    public int? RecommendationImpressionEventId { get; set; }

    public int? UserId { get; set; }

    public string SessionId { get; set; } = string.Empty;

    public int ProductId { get; set; }

    public string Placement { get; set; } = string.Empty;

    public string Algorithm { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }
}
