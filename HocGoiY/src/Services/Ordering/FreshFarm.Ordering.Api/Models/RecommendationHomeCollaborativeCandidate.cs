namespace FreshFarm.Ordering.Api.Models;

public partial class RecommendationHomeCollaborativeCandidate
{
    public int RecommendationHomeCollaborativeCandidateId { get; set; }

    public string ScopeType { get; set; } = string.Empty;

    public string ScopeKey { get; set; } = string.Empty;

    public int? UserId { get; set; }

    public int ProductId { get; set; }

    public int CoPurchaseOrderCount { get; set; }

    public int CoViewSessionCount { get; set; }

    public int CoClickSessionCount { get; set; }

    public double CollaborativeScore { get; set; }

    public DateTime ComputedAt { get; set; }
}
