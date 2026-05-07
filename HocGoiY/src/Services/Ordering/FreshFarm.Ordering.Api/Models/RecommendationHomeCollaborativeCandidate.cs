// Nguon goc: src\Services\Ordering\FreshFarm.Ordering.Api\Models\RecommendationHomeCollaborativeCandidate.cs
// Duoc sao chep tu: D:\NCKH\DOAN\NCKH-FRESH-FARM\src\Services\Ordering\FreshFarm.Ordering.Api\Models\RecommendationHomeCollaborativeCandidate.cs
// Thu muc hoc tap: HocGoiY

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

