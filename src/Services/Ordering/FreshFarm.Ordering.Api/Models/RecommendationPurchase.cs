namespace FreshFarm.Ordering.Api.Models;

public sealed class RecommendationPurchase
{
    public int Id { get; set; }

    public int? UserId { get; set; }

    public int ProductId { get; set; }

    public int? Position { get; set; }

    public string? ExperimentGroup { get; set; } = "A";

    public decimal Revenue { get; set; }

    public DateTime Timestamp { get; set; }
}
