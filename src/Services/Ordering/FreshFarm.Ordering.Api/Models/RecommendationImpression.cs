namespace FreshFarm.Ordering.Api.Models;

public sealed class RecommendationImpression
{
    public int Id { get; set; }

    public int? UserId { get; set; }

    public int ProductId { get; set; }

    public int? Position { get; set; }

    public string RecommendationSource { get; set; } = "ML";

    public string? ExperimentGroup { get; set; } = "A";

    public DateTime Timestamp { get; set; }
}
