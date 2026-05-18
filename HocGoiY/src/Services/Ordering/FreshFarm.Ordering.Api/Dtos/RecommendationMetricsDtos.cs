using System.ComponentModel.DataAnnotations;

namespace FreshFarm.Ordering.Api.Dtos;

public sealed class TrackRecommendationImpressionsRequest
{
    public int? UserId { get; init; }

    [MinLength(1, ErrorMessage = "Items phai co it nhat 1 san pham.")]
    public List<RecommendationImpressionMetricItemDto> Items { get; init; } = new();
}

public sealed class RecommendationImpressionMetricItemDto
{
    [Range(1, int.MaxValue, ErrorMessage = "ProductId phai > 0.")]
    public int ProductId { get; init; }

    [Range(1, int.MaxValue, ErrorMessage = "Position phai > 0.")]
    public int? Position { get; init; }

    [MaxLength(20)]
    public string? RecommendationSource { get; init; }

    [MaxLength(10)]
    public string? ExperimentGroup { get; init; }
}

public sealed class TrackRecommendationClickMetricRequest
{
    public int? UserId { get; init; }

    [Range(1, int.MaxValue, ErrorMessage = "ProductId phai > 0.")]
    public int ProductId { get; init; }

    [Range(1, int.MaxValue, ErrorMessage = "Position phai > 0.")]
    public int? Position { get; init; }

    [MaxLength(10)]
    public string? ExperimentGroup { get; init; }
}

public sealed class TrackRecommendationAddToCartMetricRequest
{
    public int? UserId { get; init; }

    [Range(1, int.MaxValue, ErrorMessage = "ProductId phai > 0.")]
    public int ProductId { get; init; }

    [Range(1, int.MaxValue, ErrorMessage = "Position phai > 0.")]
    public int? Position { get; init; }

    [MaxLength(10)]
    public string? ExperimentGroup { get; init; }
}

public sealed class TrackRecommendationPurchaseMetricRequest
{
    public int? UserId { get; init; }

    [Range(1, int.MaxValue, ErrorMessage = "ProductId phai > 0.")]
    public int ProductId { get; init; }

    [Range(1, int.MaxValue, ErrorMessage = "Position phai > 0.")]
    public int? Position { get; init; }

    [Range(typeof(decimal), "0", "79228162514264337593543950335", ErrorMessage = "Revenue phai >= 0.")]
    public decimal? Revenue { get; init; }

    [MaxLength(10)]
    public string? ExperimentGroup { get; init; }
}

public sealed class RecommendationCtrMetricDto
{
    public string RecommendationSource { get; init; } = string.Empty;

    public string ExperimentGroup { get; init; } = string.Empty;

    public long TotalImpressions { get; init; }

    public long TotalClicks { get; init; }

    public double? CtrPercentage { get; init; }

    public string Status { get; init; } = string.Empty;
}

public sealed class RecommendationObjectiveMetricDto
{
    public int ProductId { get; init; }

    public long TotalImpressions { get; init; }

    public long TotalClicks { get; init; }

    public long TotalAddToCarts { get; init; }

    public long TotalPurchases { get; init; }

    public decimal TotalRevenue { get; init; }

    public double? Ctr { get; init; }

    public double? AddToCartRate { get; init; }

    public double? PurchaseRate { get; init; }

    public decimal? RevenuePerImpression { get; init; }

    public string Status { get; init; } = string.Empty;
}

public sealed class RecommendationObjectiveSummaryDto
{
    public long TotalImpressions { get; init; }

    public long TotalClicks { get; init; }

    public long TotalAddToCarts { get; init; }

    public long TotalPurchases { get; init; }

    public decimal TotalRevenue { get; init; }

    public double? Ctr { get; init; }

    public double? AddToCartRate { get; init; }

    public double? PurchaseRate { get; init; }

    public decimal? RevenuePerImpression { get; init; }

    public string Status { get; init; } = string.Empty;
}

public sealed class RecommendationObjectiveGroupMetricDto
{
    public string RecommendationSource { get; init; } = string.Empty;

    public string ExperimentGroup { get; init; } = string.Empty;

    public long TotalImpressions { get; init; }

    public long TotalClicks { get; init; }

    public long TotalAddToCarts { get; init; }

    public long TotalPurchases { get; init; }

    public decimal TotalRevenue { get; init; }

    public double? Ctr { get; init; }

    public double? AddToCartRate { get; init; }

    public double? PurchaseRate { get; init; }

    public decimal? RevenuePerImpression { get; init; }

    public string Status { get; init; } = string.Empty;
}

public sealed class RecommendationObjectiveComparisonDto
{
    public string BaselineRecommendationSource { get; init; } = "ML";

    public string BaselineExperimentGroup { get; init; } = "A";

    public long BaselineImpressions { get; init; }

    public double? BaselinePurchaseRate { get; init; }

    public decimal? BaselineRevenuePerImpression { get; init; }

    public string VariantRecommendationSource { get; init; } = "Session";

    public string VariantExperimentGroup { get; init; } = "B";

    public long VariantImpressions { get; init; }

    public double? VariantPurchaseRate { get; init; }

    public decimal? VariantRevenuePerImpression { get; init; }

    public double? PurchaseRateUplift { get; init; }

    public decimal? RevenuePerImpressionUplift { get; init; }

    public string Status { get; init; } = string.Empty;
}

public sealed class RecommendationObjectiveMetricsReportDto
{
    public DateTime FromDateUtc { get; init; }

    public DateTime ToDateUtc { get; init; }

    public int MinimumImpressions { get; init; }

    public RecommendationObjectiveSummaryDto Global { get; init; } = new();

    public IReadOnlyList<RecommendationObjectiveGroupMetricDto> Groups { get; init; } =
        Array.Empty<RecommendationObjectiveGroupMetricDto>();

    public RecommendationObjectiveComparisonDto Comparison { get; init; } = new();

    public IReadOnlyList<RecommendationObjectiveMetricDto> Metrics { get; init; } =
        Array.Empty<RecommendationObjectiveMetricDto>();
}

public sealed class RecommendationNegativeFeedbackMetricDto
{
    public int ProductId { get; init; }

    public long ImpressionNoClickCount { get; init; }

    public long RepeatedImpressionCount { get; init; }

    public long BounceCount { get; init; }

    public double PenaltyScore { get; init; }

    public DateTime? LastSignalAtUtc { get; init; }

    public string Status { get; init; } = string.Empty;
}

public sealed class RecommendationNegativeFeedbackReportDto
{
    public DateTime FromDateUtc { get; init; }

    public DateTime ToDateUtc { get; init; }

    public int? UserId { get; init; }

    public IReadOnlyList<RecommendationNegativeFeedbackMetricDto> Metrics { get; init; } =
        Array.Empty<RecommendationNegativeFeedbackMetricDto>();
}

public sealed class RecommendationCtrReportDto
{
    public DateTime FromDateUtc { get; init; }

    public DateTime ToDateUtc { get; init; }

    public int MinimumImpressions { get; init; }

    public IReadOnlyList<RecommendationCtrMetricDto> Metrics { get; init; } =
        Array.Empty<RecommendationCtrMetricDto>();

    public RecommendationCtrComparisonDto Comparison { get; init; } = new();
}

public sealed class RecommendationCtrByPositionMetricDto
{
    public int Position { get; init; }

    public string ExperimentGroup { get; init; } = string.Empty;

    public string RecommendationSource { get; init; } = string.Empty;

    public long TotalImpressions { get; init; }

    public long TotalClicks { get; init; }

    public double? CtrPercentage { get; init; }

    public string Status { get; init; } = string.Empty;
}

public sealed class RecommendationCtrByPositionReportDto
{
    public DateTime FromDateUtc { get; init; }

    public DateTime ToDateUtc { get; init; }

    public int MinimumImpressions { get; init; }

    public IReadOnlyList<RecommendationCtrByPositionMetricDto> Metrics { get; init; } =
        Array.Empty<RecommendationCtrByPositionMetricDto>();
}

public sealed class RecommendationCtrComparisonDto
{
    public string BaselineRecommendationSource { get; init; } = "ML";

    public string BaselineExperimentGroup { get; init; } = "A";

    public long BaselineImpressions { get; init; }

    public long BaselineClicks { get; init; }

    public double? BaselineCtrPercentage { get; init; }

    public string VariantRecommendationSource { get; init; } = "Session";

    public string VariantExperimentGroup { get; init; } = "B";

    public long VariantImpressions { get; init; }

    public long VariantClicks { get; init; }

    public double? VariantCtrPercentage { get; init; }

    public double? AbsoluteUpliftPercentagePoints { get; init; }

    public double? PercentageUplift { get; init; }

    public string Status { get; init; } = string.Empty;
}
