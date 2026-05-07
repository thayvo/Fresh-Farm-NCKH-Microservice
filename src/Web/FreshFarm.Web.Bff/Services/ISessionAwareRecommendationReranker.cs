using System.Text.Json.Serialization;

namespace FreshFarm.Web.Bff.Services;

public interface ISessionAwareRecommendationReranker
{
    Task<IReadOnlyList<SessionAwareRerankedProduct>> RerankAsync(
        int userId,
        IReadOnlyList<SessionAwareRecommendationCandidate> candidates,
        SessionAwareRecommendationContext? context = null,
        CancellationToken cancellationToken = default);
}

public sealed record SessionAwareRecommendationCandidate
{
    public int ProductId { get; init; }

    public string? ProductName { get; init; }

    public string? CategoryName { get; init; }

    public string? Origin { get; init; }

    public string? Standard { get; init; }

    public int? PrimarySellerId { get; init; }

    public string? SellerShopName { get; init; }

    public string? SellerAddressSummary { get; init; }

    public int AvailableStock { get; init; }

    public double BaseScore { get; init; }

    public double? SellerDistanceKm { get; init; }

    public bool? IsNearbyShop { get; init; }

    public IReadOnlyCollection<string> SearchableTerms { get; init; } = Array.Empty<string>();
}

public sealed record SessionAwareRecommendationContext
{
    public IReadOnlySet<int> NearbySellerIds { get; init; } = new HashSet<int>();

    public string? DeliveryProvince { get; init; }

    public string? DeliveryDistrict { get; init; }

    public string? DeliveryWard { get; init; }
}

public sealed record SessionAwareRerankedProduct
{
    public required SessionAwareRecommendationCandidate Product { get; init; }

    [JsonPropertyName("final_score")]
    public double FinalScore { get; init; }

    public double BaseScore { get; init; }

    public string SignalSource { get; init; } = "redis_session_rerank_v1";

    public IReadOnlyList<string> AppliedSignals { get; init; } = Array.Empty<string>();
}
