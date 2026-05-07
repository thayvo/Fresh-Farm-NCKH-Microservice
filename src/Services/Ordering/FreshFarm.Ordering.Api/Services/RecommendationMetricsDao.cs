using FreshFarm.Ordering.Api.Models;
using FreshFarm.Ordering.Api.Dtos;
using Microsoft.EntityFrameworkCore;
using System.Data;

namespace FreshFarm.Ordering.Api.Services;

public sealed record RecommendationMetricAttribution(
    string RecommendationSource,
    string ExperimentGroup);

public interface IRecommendationMetricsDao
{
    Task AddImpressionsAsync(
        IReadOnlyCollection<RecommendationImpression> impressions,
        CancellationToken cancellationToken = default);

    Task AddClickAsync(
        RecommendationClick click,
        CancellationToken cancellationToken = default);

    Task AddAddToCartAsync(
        RecommendationAddToCart addToCart,
        CancellationToken cancellationToken = default);

    Task AddPurchaseAsync(
        RecommendationPurchase purchase,
        CancellationToken cancellationToken = default);

    Task<RecommendationMetricAttribution?> FindLatestAttributionAsync(
        int? userId,
        int productId,
        int? position,
        DateTime eventTimestampUtc,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RecommendationCtrMetricDto>> GetCtrAsync(
        DateTime fromDateUtc,
        DateTime toDateUtc,
        int minimumImpressions,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RecommendationCtrByPositionMetricDto>> GetCtrByPositionAsync(
        DateTime fromDateUtc,
        DateTime toDateUtc,
        int minimumImpressions,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RecommendationObjectiveMetricDto>> GetObjectiveMetricsAsync(
        DateTime fromDateUtc,
        DateTime toDateUtc,
        IReadOnlyCollection<int> productIds,
        int minimumImpressions,
        CancellationToken cancellationToken = default);

    Task<RecommendationObjectiveSummaryDto> GetObjectiveGlobalMetricsAsync(
        DateTime fromDateUtc,
        DateTime toDateUtc,
        IReadOnlyCollection<int> productIds,
        int minimumImpressions,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RecommendationObjectiveGroupMetricDto>> GetObjectiveGroupMetricsAsync(
        DateTime fromDateUtc,
        DateTime toDateUtc,
        IReadOnlyCollection<int> productIds,
        int minimumImpressions,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RecommendationNegativeFeedbackMetricDto>> GetNegativeFeedbackAsync(
        int? userId,
        DateTime fromDateUtc,
        DateTime toDateUtc,
        IReadOnlyCollection<int> productIds,
        CancellationToken cancellationToken = default);
}

public sealed class RecommendationMetricsDao : IRecommendationMetricsDao
{
    private const string CtrSql = """
        WITH ValidStrategies AS (
            SELECT
                CAST(N'ML' AS nvarchar(20)) AS RecommendationSource,
                CAST(N'A' AS nvarchar(10)) AS ExperimentGroup
            UNION ALL
            SELECT
                CAST(N'Session' AS nvarchar(20)) AS RecommendationSource,
                CAST(N'B' AS nvarchar(10)) AS ExperimentGroup
        ),
        ImpressionCounts AS (
            SELECT
                Impression.RecommendationSource,
                Impression.ExperimentGroup,
                COUNT_BIG(*) AS TotalImpressions
            FROM RecommendationImpression AS Impression
            INNER JOIN ValidStrategies AS Strategy
                ON Strategy.RecommendationSource = Impression.RecommendationSource
                AND Strategy.ExperimentGroup = Impression.ExperimentGroup
            WHERE Impression.[Timestamp] >= @FromDateUtc
                AND Impression.[Timestamp] < @ToDateUtc
            GROUP BY Impression.RecommendationSource, Impression.ExperimentGroup
        ),
        AttributedClickCounts AS (
            SELECT
                ClickSource.RecommendationSource,
                ClickSource.ExperimentGroup,
                COUNT_BIG(*) AS TotalClicks
            FROM RecommendationClick AS ClickMetric
            CROSS APPLY (
                SELECT TOP (1)
                    Impression.RecommendationSource,
                    Impression.ExperimentGroup
                FROM RecommendationImpression AS Impression
                INNER JOIN ValidStrategies AS Strategy
                    ON Strategy.RecommendationSource = Impression.RecommendationSource
                    AND Strategy.ExperimentGroup = Impression.ExperimentGroup
                WHERE Impression.ProductId = ClickMetric.ProductId
                    AND Impression.ExperimentGroup = ClickMetric.ExperimentGroup
                    AND (
                        Impression.UserId = ClickMetric.UserId
                        OR (Impression.UserId IS NULL AND ClickMetric.UserId IS NULL)
                    )
                    AND (
                        Impression.Position = ClickMetric.Position
                        OR (Impression.Position IS NULL AND ClickMetric.Position IS NULL)
                    )
                    AND Impression.[Timestamp] >= @FromDateUtc
                    AND Impression.[Timestamp] < @ToDateUtc
                    AND Impression.[Timestamp] <= ClickMetric.[Timestamp]
                ORDER BY Impression.[Timestamp] DESC, Impression.Id DESC
            ) AS ClickSource
            WHERE ClickMetric.[Timestamp] >= @FromDateUtc
                AND ClickMetric.[Timestamp] < @ToDateUtc
            GROUP BY ClickSource.RecommendationSource, ClickSource.ExperimentGroup
        )
        SELECT
            ValidStrategies.RecommendationSource,
            ValidStrategies.ExperimentGroup,
            CAST(ISNULL(ImpressionCounts.TotalImpressions, 0) AS bigint) AS TotalImpressions,
            CAST(ISNULL(AttributedClickCounts.TotalClicks, 0) AS bigint) AS TotalClicks,
            CAST(
                CASE
                    WHEN ISNULL(ImpressionCounts.TotalImpressions, 0) < @MinimumImpressions THEN NULL
                    ELSE ISNULL(AttributedClickCounts.TotalClicks, 0) * 100.0 / ImpressionCounts.TotalImpressions
                END AS float
            ) AS CtrPercentage,
            CAST(
                CASE
                    WHEN ISNULL(ImpressionCounts.TotalImpressions, 0) < @MinimumImpressions THEN N'insufficient_data'
                    ELSE N'ok'
                END AS nvarchar(30)
            ) AS [Status]
        FROM ValidStrategies
        LEFT JOIN ImpressionCounts
            ON ImpressionCounts.RecommendationSource = ValidStrategies.RecommendationSource
            AND ImpressionCounts.ExperimentGroup = ValidStrategies.ExperimentGroup
        LEFT JOIN AttributedClickCounts
            ON AttributedClickCounts.RecommendationSource = ValidStrategies.RecommendationSource
            AND AttributedClickCounts.ExperimentGroup = ValidStrategies.ExperimentGroup
        ORDER BY
            CASE ValidStrategies.ExperimentGroup
                WHEN N'A' THEN 0
                WHEN N'B' THEN 1
                ELSE 2
            END,
            CASE ValidStrategies.RecommendationSource
                WHEN N'ML' THEN 0
                WHEN N'Session' THEN 1
                ELSE 2
            END;
        """;

    private const string CtrByPositionSql = """
        WITH ValidStrategies AS (
            SELECT
                CAST(N'ML' AS nvarchar(20)) AS RecommendationSource,
                CAST(N'A' AS nvarchar(10)) AS ExperimentGroup
            UNION ALL
            SELECT
                CAST(N'Session' AS nvarchar(20)) AS RecommendationSource,
                CAST(N'B' AS nvarchar(10)) AS ExperimentGroup
        ),
        ImpressionCounts AS (
            SELECT
                Impression.Position,
                Impression.RecommendationSource,
                Impression.ExperimentGroup,
                COUNT_BIG(*) AS TotalImpressions
            FROM RecommendationImpression AS Impression
            INNER JOIN ValidStrategies AS Strategy
                ON Strategy.RecommendationSource = Impression.RecommendationSource
                AND Strategy.ExperimentGroup = Impression.ExperimentGroup
            WHERE Impression.[Timestamp] >= @FromDateUtc
                AND Impression.[Timestamp] < @ToDateUtc
                AND Impression.Position IS NOT NULL
                AND Impression.Position > 0
            GROUP BY
                Impression.Position,
                Impression.RecommendationSource,
                Impression.ExperimentGroup
        ),
        AttributedClickCounts AS (
            SELECT
                ClickSource.Position,
                ClickSource.RecommendationSource,
                ClickSource.ExperimentGroup,
                COUNT_BIG(*) AS TotalClicks
            FROM RecommendationClick AS ClickMetric
            CROSS APPLY (
                SELECT TOP (1)
                    Impression.Position,
                    Impression.RecommendationSource,
                    Impression.ExperimentGroup
                FROM RecommendationImpression AS Impression
                INNER JOIN ValidStrategies AS Strategy
                    ON Strategy.RecommendationSource = Impression.RecommendationSource
                    AND Strategy.ExperimentGroup = Impression.ExperimentGroup
                WHERE Impression.ProductId = ClickMetric.ProductId
                    AND Impression.ExperimentGroup = ClickMetric.ExperimentGroup
                    AND Impression.Position = ClickMetric.Position
                    AND Impression.Position IS NOT NULL
                    AND Impression.Position > 0
                    AND (
                        Impression.UserId = ClickMetric.UserId
                        OR (Impression.UserId IS NULL AND ClickMetric.UserId IS NULL)
                    )
                    AND Impression.[Timestamp] >= @FromDateUtc
                    AND Impression.[Timestamp] < @ToDateUtc
                    AND Impression.[Timestamp] <= ClickMetric.[Timestamp]
                ORDER BY Impression.[Timestamp] DESC, Impression.Id DESC
            ) AS ClickSource
            WHERE ClickMetric.[Timestamp] >= @FromDateUtc
                AND ClickMetric.[Timestamp] < @ToDateUtc
                AND ClickMetric.Position IS NOT NULL
                AND ClickMetric.Position > 0
            GROUP BY
                ClickSource.Position,
                ClickSource.RecommendationSource,
                ClickSource.ExperimentGroup
        )
        SELECT
            ImpressionCounts.Position,
            ImpressionCounts.ExperimentGroup,
            ImpressionCounts.RecommendationSource,
            CAST(ImpressionCounts.TotalImpressions AS bigint) AS TotalImpressions,
            CAST(ISNULL(AttributedClickCounts.TotalClicks, 0) AS bigint) AS TotalClicks,
            CAST(
                CASE
                    WHEN ImpressionCounts.TotalImpressions < @MinimumImpressions THEN NULL
                    ELSE ISNULL(AttributedClickCounts.TotalClicks, 0) * 100.0 / ImpressionCounts.TotalImpressions
                END AS float
            ) AS CtrPercentage,
            CAST(
                CASE
                    WHEN ImpressionCounts.TotalImpressions < @MinimumImpressions THEN N'insufficient_data'
                    ELSE N'ok'
                END AS nvarchar(30)
            ) AS [Status]
        FROM ImpressionCounts
        LEFT JOIN AttributedClickCounts
            ON AttributedClickCounts.Position = ImpressionCounts.Position
            AND AttributedClickCounts.RecommendationSource = ImpressionCounts.RecommendationSource
            AND AttributedClickCounts.ExperimentGroup = ImpressionCounts.ExperimentGroup
        ORDER BY
            ImpressionCounts.Position,
            CASE ImpressionCounts.ExperimentGroup
                WHEN N'A' THEN 0
                WHEN N'B' THEN 1
                ELSE 2
            END,
            CASE ImpressionCounts.RecommendationSource
                WHEN N'ML' THEN 0
                WHEN N'Session' THEN 1
                ELSE 2
            END;
        """;

    private const string ObjectiveMetricsSql = """
        WITH RequestedProducts AS (
            SELECT DISTINCT TRY_CONVERT(int, value) AS ProductId
            FROM STRING_SPLIT(@ProductIdsCsv, N',')
            WHERE TRY_CONVERT(int, value) IS NOT NULL
                AND TRY_CONVERT(int, value) > 0
        ),
        ImpressionCounts AS (
            SELECT
                Impression.ProductId,
                COUNT_BIG(*) AS TotalImpressions
            FROM RecommendationImpression AS Impression
            INNER JOIN RequestedProducts AS Product
                ON Product.ProductId = Impression.ProductId
            WHERE Impression.[Timestamp] >= @FromDateUtc
                AND Impression.[Timestamp] < @ToDateUtc
            GROUP BY Impression.ProductId
        ),
        ClickCounts AS (
            SELECT
                ClickSource.ProductId,
                COUNT_BIG(*) AS TotalClicks
            FROM RecommendationClick AS ClickMetric
            CROSS APPLY (
                SELECT TOP (1)
                    Impression.ProductId
                FROM RecommendationImpression AS Impression
                INNER JOIN RequestedProducts AS Product
                    ON Product.ProductId = Impression.ProductId
                WHERE Impression.ProductId = ClickMetric.ProductId
                    AND (
                        Impression.UserId = ClickMetric.UserId
                        OR (Impression.UserId IS NULL AND ClickMetric.UserId IS NULL)
                    )
                    AND (
                        Impression.Position = ClickMetric.Position
                        OR (Impression.Position IS NULL AND ClickMetric.Position IS NULL)
                    )
                    AND Impression.[Timestamp] >= @FromDateUtc
                    AND Impression.[Timestamp] < @ToDateUtc
                    AND Impression.[Timestamp] <= ClickMetric.[Timestamp]
                ORDER BY Impression.[Timestamp] DESC, Impression.Id DESC
            ) AS ClickSource
            WHERE ClickMetric.[Timestamp] >= @FromDateUtc
                AND ClickMetric.[Timestamp] < @ToDateUtc
            GROUP BY ClickSource.ProductId
        ),
        AddToCartCounts AS (
            SELECT
                AddToCartSource.ProductId,
                COUNT_BIG(*) AS TotalAddToCarts
            FROM RecommendationAddToCart AS AddToCart
            CROSS APPLY (
                SELECT TOP (1)
                    Impression.ProductId
                FROM RecommendationImpression AS Impression
                INNER JOIN RequestedProducts AS Product
                    ON Product.ProductId = Impression.ProductId
                WHERE Impression.ProductId = AddToCart.ProductId
                    AND (
                        Impression.UserId = AddToCart.UserId
                        OR (Impression.UserId IS NULL AND AddToCart.UserId IS NULL)
                    )
                    AND (
                        Impression.Position = AddToCart.Position
                        OR (Impression.Position IS NULL AND AddToCart.Position IS NULL)
                    )
                    AND Impression.[Timestamp] >= @FromDateUtc
                    AND Impression.[Timestamp] < @ToDateUtc
                    AND Impression.[Timestamp] <= AddToCart.[Timestamp]
                ORDER BY Impression.[Timestamp] DESC, Impression.Id DESC
            ) AS AddToCartSource
            WHERE AddToCart.[Timestamp] >= @FromDateUtc
                AND AddToCart.[Timestamp] < @ToDateUtc
            GROUP BY AddToCartSource.ProductId
        ),
        PurchaseCounts AS (
            SELECT
                PurchaseSource.ProductId,
                COUNT_BIG(*) AS TotalPurchases,
                SUM(CAST(PurchaseMetric.Revenue AS decimal(18, 2))) AS TotalRevenue
            FROM RecommendationPurchase AS PurchaseMetric
            CROSS APPLY (
                SELECT TOP (1)
                    Impression.ProductId
                FROM RecommendationImpression AS Impression
                INNER JOIN RequestedProducts AS Product
                    ON Product.ProductId = Impression.ProductId
                WHERE Impression.ProductId = PurchaseMetric.ProductId
                    AND (
                        Impression.UserId = PurchaseMetric.UserId
                        OR (Impression.UserId IS NULL AND PurchaseMetric.UserId IS NULL)
                    )
                    AND (
                        Impression.Position = PurchaseMetric.Position
                        OR (Impression.Position IS NULL AND PurchaseMetric.Position IS NULL)
                    )
                    AND Impression.[Timestamp] >= @FromDateUtc
                    AND Impression.[Timestamp] < @ToDateUtc
                    AND Impression.[Timestamp] <= PurchaseMetric.[Timestamp]
                ORDER BY Impression.[Timestamp] DESC, Impression.Id DESC
            ) AS PurchaseSource
            WHERE PurchaseMetric.[Timestamp] >= @FromDateUtc
                AND PurchaseMetric.[Timestamp] < @ToDateUtc
            GROUP BY PurchaseSource.ProductId
        )
        SELECT
            Product.ProductId,
            CAST(ISNULL(ImpressionCounts.TotalImpressions, 0) AS bigint) AS TotalImpressions,
            CAST(ISNULL(ClickCounts.TotalClicks, 0) AS bigint) AS TotalClicks,
            CAST(ISNULL(AddToCartCounts.TotalAddToCarts, 0) AS bigint) AS TotalAddToCarts,
            CAST(ISNULL(PurchaseCounts.TotalPurchases, 0) AS bigint) AS TotalPurchases,
            CAST(ISNULL(PurchaseCounts.TotalRevenue, 0) AS decimal(18, 2)) AS TotalRevenue,
            CAST(
                CASE
                    WHEN ISNULL(ImpressionCounts.TotalImpressions, 0) < @MinimumImpressions THEN NULL
                    ELSE ISNULL(ClickCounts.TotalClicks, 0) * 1.0 / ImpressionCounts.TotalImpressions
                END AS float
            ) AS Ctr,
            CAST(
                CASE
                    WHEN ISNULL(ImpressionCounts.TotalImpressions, 0) < @MinimumImpressions THEN NULL
                    ELSE ISNULL(AddToCartCounts.TotalAddToCarts, 0) * 1.0 / ImpressionCounts.TotalImpressions
                END AS float
            ) AS AddToCartRate,
            CAST(
                CASE
                    WHEN ISNULL(ImpressionCounts.TotalImpressions, 0) < @MinimumImpressions THEN NULL
                    ELSE ISNULL(PurchaseCounts.TotalPurchases, 0) * 1.0 / ImpressionCounts.TotalImpressions
                END AS float
            ) AS PurchaseRate,
            CAST(
                CASE
                    WHEN ISNULL(ImpressionCounts.TotalImpressions, 0) < @MinimumImpressions THEN NULL
                    ELSE ISNULL(PurchaseCounts.TotalRevenue, 0) / ImpressionCounts.TotalImpressions
                END AS decimal(18, 6)
            ) AS RevenuePerImpression,
            CAST(
                CASE
                    WHEN ISNULL(ImpressionCounts.TotalImpressions, 0) < @MinimumImpressions THEN N'insufficient_data'
                    ELSE N'ok'
                END AS nvarchar(30)
            ) AS [Status]
        FROM RequestedProducts AS Product
        LEFT JOIN ImpressionCounts
            ON ImpressionCounts.ProductId = Product.ProductId
        LEFT JOIN ClickCounts
            ON ClickCounts.ProductId = Product.ProductId
        LEFT JOIN AddToCartCounts
            ON AddToCartCounts.ProductId = Product.ProductId
        LEFT JOIN PurchaseCounts
            ON PurchaseCounts.ProductId = Product.ProductId
        ORDER BY Product.ProductId;
        """;

    private const string ObjectiveGlobalMetricsSql = """
        WITH RequestedProducts AS (
            SELECT DISTINCT TRY_CONVERT(int, value) AS ProductId
            FROM STRING_SPLIT(@ProductIdsCsv, N',')
            WHERE TRY_CONVERT(int, value) IS NOT NULL
                AND TRY_CONVERT(int, value) > 0
        ),
        ValidStrategies AS (
            SELECT
                CAST(N'ML' AS nvarchar(20)) AS RecommendationSource,
                CAST(N'A' AS nvarchar(10)) AS ExperimentGroup
            UNION ALL
            SELECT
                CAST(N'Session' AS nvarchar(20)) AS RecommendationSource,
                CAST(N'B' AS nvarchar(10)) AS ExperimentGroup
        ),
        ImpressionCounts AS (
            SELECT COUNT_BIG(*) AS TotalImpressions
            FROM RecommendationImpression AS Impression
            INNER JOIN ValidStrategies AS Strategy
                ON Strategy.RecommendationSource = Impression.RecommendationSource
                AND Strategy.ExperimentGroup = Impression.ExperimentGroup
            WHERE Impression.[Timestamp] >= @FromDateUtc
                AND Impression.[Timestamp] < @ToDateUtc
                AND (
                    @ProductIdsCsv = N''
                    OR EXISTS (
                        SELECT 1
                        FROM RequestedProducts AS Product
                        WHERE Product.ProductId = Impression.ProductId
                    )
                )
        ),
        ClickCounts AS (
            SELECT COUNT_BIG(*) AS TotalClicks
            FROM RecommendationClick AS ClickMetric
            CROSS APPLY (
                SELECT TOP (1)
                    Impression.RecommendationSource,
                    Impression.ExperimentGroup
                FROM RecommendationImpression AS Impression
                INNER JOIN ValidStrategies AS Strategy
                    ON Strategy.RecommendationSource = Impression.RecommendationSource
                    AND Strategy.ExperimentGroup = Impression.ExperimentGroup
                WHERE Impression.ProductId = ClickMetric.ProductId
                    AND (
                        Impression.UserId = ClickMetric.UserId
                        OR (Impression.UserId IS NULL AND ClickMetric.UserId IS NULL)
                    )
                    AND (
                        Impression.Position = ClickMetric.Position
                        OR (Impression.Position IS NULL AND ClickMetric.Position IS NULL)
                    )
                    AND (
                        @ProductIdsCsv = N''
                        OR EXISTS (
                            SELECT 1
                            FROM RequestedProducts AS Product
                            WHERE Product.ProductId = Impression.ProductId
                        )
                    )
                    AND Impression.[Timestamp] >= @FromDateUtc
                    AND Impression.[Timestamp] < @ToDateUtc
                    AND Impression.[Timestamp] <= ClickMetric.[Timestamp]
                ORDER BY Impression.[Timestamp] DESC, Impression.Id DESC
            ) AS ClickSource
            WHERE ClickMetric.[Timestamp] >= @FromDateUtc
                AND ClickMetric.[Timestamp] < @ToDateUtc
        ),
        AddToCartCounts AS (
            SELECT COUNT_BIG(*) AS TotalAddToCarts
            FROM RecommendationAddToCart AS AddToCart
            CROSS APPLY (
                SELECT TOP (1)
                    Impression.RecommendationSource,
                    Impression.ExperimentGroup
                FROM RecommendationImpression AS Impression
                INNER JOIN ValidStrategies AS Strategy
                    ON Strategy.RecommendationSource = Impression.RecommendationSource
                    AND Strategy.ExperimentGroup = Impression.ExperimentGroup
                WHERE Impression.ProductId = AddToCart.ProductId
                    AND (
                        Impression.UserId = AddToCart.UserId
                        OR (Impression.UserId IS NULL AND AddToCart.UserId IS NULL)
                    )
                    AND (
                        Impression.Position = AddToCart.Position
                        OR (Impression.Position IS NULL AND AddToCart.Position IS NULL)
                    )
                    AND (
                        @ProductIdsCsv = N''
                        OR EXISTS (
                            SELECT 1
                            FROM RequestedProducts AS Product
                            WHERE Product.ProductId = Impression.ProductId
                        )
                    )
                    AND Impression.[Timestamp] >= @FromDateUtc
                    AND Impression.[Timestamp] < @ToDateUtc
                    AND Impression.[Timestamp] <= AddToCart.[Timestamp]
                ORDER BY Impression.[Timestamp] DESC, Impression.Id DESC
            ) AS AddToCartSource
            WHERE AddToCart.[Timestamp] >= @FromDateUtc
                AND AddToCart.[Timestamp] < @ToDateUtc
        ),
        PurchaseCounts AS (
            SELECT
                COUNT_BIG(*) AS TotalPurchases,
                SUM(CAST(PurchaseMetric.Revenue AS decimal(18, 2))) AS TotalRevenue
            FROM RecommendationPurchase AS PurchaseMetric
            CROSS APPLY (
                SELECT TOP (1)
                    Impression.RecommendationSource,
                    Impression.ExperimentGroup
                FROM RecommendationImpression AS Impression
                INNER JOIN ValidStrategies AS Strategy
                    ON Strategy.RecommendationSource = Impression.RecommendationSource
                    AND Strategy.ExperimentGroup = Impression.ExperimentGroup
                WHERE Impression.ProductId = PurchaseMetric.ProductId
                    AND (
                        Impression.UserId = PurchaseMetric.UserId
                        OR (Impression.UserId IS NULL AND PurchaseMetric.UserId IS NULL)
                    )
                    AND (
                        Impression.Position = PurchaseMetric.Position
                        OR (Impression.Position IS NULL AND PurchaseMetric.Position IS NULL)
                    )
                    AND (
                        @ProductIdsCsv = N''
                        OR EXISTS (
                            SELECT 1
                            FROM RequestedProducts AS Product
                            WHERE Product.ProductId = Impression.ProductId
                        )
                    )
                    AND Impression.[Timestamp] >= @FromDateUtc
                    AND Impression.[Timestamp] < @ToDateUtc
                    AND Impression.[Timestamp] <= PurchaseMetric.[Timestamp]
                ORDER BY Impression.[Timestamp] DESC, Impression.Id DESC
            ) AS PurchaseSource
            WHERE PurchaseMetric.[Timestamp] >= @FromDateUtc
                AND PurchaseMetric.[Timestamp] < @ToDateUtc
        )
        SELECT
            CAST(ISNULL(ImpressionCounts.TotalImpressions, 0) AS bigint) AS TotalImpressions,
            CAST(ISNULL(ClickCounts.TotalClicks, 0) AS bigint) AS TotalClicks,
            CAST(ISNULL(AddToCartCounts.TotalAddToCarts, 0) AS bigint) AS TotalAddToCarts,
            CAST(ISNULL(PurchaseCounts.TotalPurchases, 0) AS bigint) AS TotalPurchases,
            CAST(ISNULL(PurchaseCounts.TotalRevenue, 0) AS decimal(18, 2)) AS TotalRevenue,
            CAST(
                CASE
                    WHEN ISNULL(ImpressionCounts.TotalImpressions, 0) < @MinimumImpressions THEN NULL
                    ELSE ISNULL(ClickCounts.TotalClicks, 0) * 1.0 / ImpressionCounts.TotalImpressions
                END AS float
            ) AS Ctr,
            CAST(
                CASE
                    WHEN ISNULL(ImpressionCounts.TotalImpressions, 0) < @MinimumImpressions THEN NULL
                    ELSE ISNULL(AddToCartCounts.TotalAddToCarts, 0) * 1.0 / ImpressionCounts.TotalImpressions
                END AS float
            ) AS AddToCartRate,
            CAST(
                CASE
                    WHEN ISNULL(ImpressionCounts.TotalImpressions, 0) < @MinimumImpressions THEN NULL
                    ELSE ISNULL(PurchaseCounts.TotalPurchases, 0) * 1.0 / ImpressionCounts.TotalImpressions
                END AS float
            ) AS PurchaseRate,
            CAST(
                CASE
                    WHEN ISNULL(ImpressionCounts.TotalImpressions, 0) < @MinimumImpressions THEN NULL
                    ELSE ISNULL(PurchaseCounts.TotalRevenue, 0) / ImpressionCounts.TotalImpressions
                END AS decimal(18, 6)
            ) AS RevenuePerImpression,
            CAST(
                CASE
                    WHEN ISNULL(ImpressionCounts.TotalImpressions, 0) < @MinimumImpressions THEN N'insufficient_data'
                    ELSE N'ok'
                END AS nvarchar(30)
            ) AS [Status]
        FROM ImpressionCounts
        CROSS JOIN ClickCounts
        CROSS JOIN AddToCartCounts
        CROSS JOIN PurchaseCounts;
        """;

    private const string ObjectiveGroupMetricsSql = """
        WITH RequestedProducts AS (
            SELECT DISTINCT TRY_CONVERT(int, value) AS ProductId
            FROM STRING_SPLIT(@ProductIdsCsv, N',')
            WHERE TRY_CONVERT(int, value) IS NOT NULL
                AND TRY_CONVERT(int, value) > 0
        ),
        ValidStrategies AS (
            SELECT
                CAST(N'ML' AS nvarchar(20)) AS RecommendationSource,
                CAST(N'A' AS nvarchar(10)) AS ExperimentGroup
            UNION ALL
            SELECT
                CAST(N'Session' AS nvarchar(20)) AS RecommendationSource,
                CAST(N'B' AS nvarchar(10)) AS ExperimentGroup
        ),
        ImpressionCounts AS (
            SELECT
                Impression.RecommendationSource,
                Impression.ExperimentGroup,
                COUNT_BIG(*) AS TotalImpressions
            FROM RecommendationImpression AS Impression
            INNER JOIN ValidStrategies AS Strategy
                ON Strategy.RecommendationSource = Impression.RecommendationSource
                AND Strategy.ExperimentGroup = Impression.ExperimentGroup
            WHERE Impression.[Timestamp] >= @FromDateUtc
                AND Impression.[Timestamp] < @ToDateUtc
                AND (
                    @ProductIdsCsv = N''
                    OR EXISTS (
                        SELECT 1
                        FROM RequestedProducts AS Product
                        WHERE Product.ProductId = Impression.ProductId
                    )
                )
            GROUP BY Impression.RecommendationSource, Impression.ExperimentGroup
        ),
        ClickCounts AS (
            SELECT
                ClickSource.RecommendationSource,
                ClickSource.ExperimentGroup,
                COUNT_BIG(*) AS TotalClicks
            FROM RecommendationClick AS ClickMetric
            CROSS APPLY (
                SELECT TOP (1)
                    Impression.RecommendationSource,
                    Impression.ExperimentGroup
                FROM RecommendationImpression AS Impression
                INNER JOIN ValidStrategies AS Strategy
                    ON Strategy.RecommendationSource = Impression.RecommendationSource
                    AND Strategy.ExperimentGroup = Impression.ExperimentGroup
                WHERE Impression.ProductId = ClickMetric.ProductId
                    AND (
                        Impression.UserId = ClickMetric.UserId
                        OR (Impression.UserId IS NULL AND ClickMetric.UserId IS NULL)
                    )
                    AND (
                        Impression.Position = ClickMetric.Position
                        OR (Impression.Position IS NULL AND ClickMetric.Position IS NULL)
                    )
                    AND (
                        @ProductIdsCsv = N''
                        OR EXISTS (
                            SELECT 1
                            FROM RequestedProducts AS Product
                            WHERE Product.ProductId = Impression.ProductId
                        )
                    )
                    AND Impression.[Timestamp] >= @FromDateUtc
                    AND Impression.[Timestamp] < @ToDateUtc
                    AND Impression.[Timestamp] <= ClickMetric.[Timestamp]
                ORDER BY Impression.[Timestamp] DESC, Impression.Id DESC
            ) AS ClickSource
            WHERE ClickMetric.[Timestamp] >= @FromDateUtc
                AND ClickMetric.[Timestamp] < @ToDateUtc
            GROUP BY ClickSource.RecommendationSource, ClickSource.ExperimentGroup
        ),
        AddToCartCounts AS (
            SELECT
                AddToCartSource.RecommendationSource,
                AddToCartSource.ExperimentGroup,
                COUNT_BIG(*) AS TotalAddToCarts
            FROM RecommendationAddToCart AS AddToCart
            CROSS APPLY (
                SELECT TOP (1)
                    Impression.RecommendationSource,
                    Impression.ExperimentGroup
                FROM RecommendationImpression AS Impression
                INNER JOIN ValidStrategies AS Strategy
                    ON Strategy.RecommendationSource = Impression.RecommendationSource
                    AND Strategy.ExperimentGroup = Impression.ExperimentGroup
                WHERE Impression.ProductId = AddToCart.ProductId
                    AND (
                        Impression.UserId = AddToCart.UserId
                        OR (Impression.UserId IS NULL AND AddToCart.UserId IS NULL)
                    )
                    AND (
                        Impression.Position = AddToCart.Position
                        OR (Impression.Position IS NULL AND AddToCart.Position IS NULL)
                    )
                    AND (
                        @ProductIdsCsv = N''
                        OR EXISTS (
                            SELECT 1
                            FROM RequestedProducts AS Product
                            WHERE Product.ProductId = Impression.ProductId
                        )
                    )
                    AND Impression.[Timestamp] >= @FromDateUtc
                    AND Impression.[Timestamp] < @ToDateUtc
                    AND Impression.[Timestamp] <= AddToCart.[Timestamp]
                ORDER BY Impression.[Timestamp] DESC, Impression.Id DESC
            ) AS AddToCartSource
            WHERE AddToCart.[Timestamp] >= @FromDateUtc
                AND AddToCart.[Timestamp] < @ToDateUtc
            GROUP BY AddToCartSource.RecommendationSource, AddToCartSource.ExperimentGroup
        ),
        PurchaseCounts AS (
            SELECT
                PurchaseSource.RecommendationSource,
                PurchaseSource.ExperimentGroup,
                COUNT_BIG(*) AS TotalPurchases,
                SUM(CAST(PurchaseMetric.Revenue AS decimal(18, 2))) AS TotalRevenue
            FROM RecommendationPurchase AS PurchaseMetric
            CROSS APPLY (
                SELECT TOP (1)
                    Impression.RecommendationSource,
                    Impression.ExperimentGroup
                FROM RecommendationImpression AS Impression
                INNER JOIN ValidStrategies AS Strategy
                    ON Strategy.RecommendationSource = Impression.RecommendationSource
                    AND Strategy.ExperimentGroup = Impression.ExperimentGroup
                WHERE Impression.ProductId = PurchaseMetric.ProductId
                    AND (
                        Impression.UserId = PurchaseMetric.UserId
                        OR (Impression.UserId IS NULL AND PurchaseMetric.UserId IS NULL)
                    )
                    AND (
                        Impression.Position = PurchaseMetric.Position
                        OR (Impression.Position IS NULL AND PurchaseMetric.Position IS NULL)
                    )
                    AND (
                        @ProductIdsCsv = N''
                        OR EXISTS (
                            SELECT 1
                            FROM RequestedProducts AS Product
                            WHERE Product.ProductId = Impression.ProductId
                        )
                    )
                    AND Impression.[Timestamp] >= @FromDateUtc
                    AND Impression.[Timestamp] < @ToDateUtc
                    AND Impression.[Timestamp] <= PurchaseMetric.[Timestamp]
                ORDER BY Impression.[Timestamp] DESC, Impression.Id DESC
            ) AS PurchaseSource
            WHERE PurchaseMetric.[Timestamp] >= @FromDateUtc
                AND PurchaseMetric.[Timestamp] < @ToDateUtc
            GROUP BY PurchaseSource.RecommendationSource, PurchaseSource.ExperimentGroup
        )
        SELECT
            ValidStrategies.RecommendationSource,
            ValidStrategies.ExperimentGroup,
            CAST(ISNULL(ImpressionCounts.TotalImpressions, 0) AS bigint) AS TotalImpressions,
            CAST(ISNULL(ClickCounts.TotalClicks, 0) AS bigint) AS TotalClicks,
            CAST(ISNULL(AddToCartCounts.TotalAddToCarts, 0) AS bigint) AS TotalAddToCarts,
            CAST(ISNULL(PurchaseCounts.TotalPurchases, 0) AS bigint) AS TotalPurchases,
            CAST(ISNULL(PurchaseCounts.TotalRevenue, 0) AS decimal(18, 2)) AS TotalRevenue,
            CAST(
                CASE
                    WHEN ISNULL(ImpressionCounts.TotalImpressions, 0) < @MinimumImpressions THEN NULL
                    ELSE ISNULL(ClickCounts.TotalClicks, 0) * 1.0 / ImpressionCounts.TotalImpressions
                END AS float
            ) AS Ctr,
            CAST(
                CASE
                    WHEN ISNULL(ImpressionCounts.TotalImpressions, 0) < @MinimumImpressions THEN NULL
                    ELSE ISNULL(AddToCartCounts.TotalAddToCarts, 0) * 1.0 / ImpressionCounts.TotalImpressions
                END AS float
            ) AS AddToCartRate,
            CAST(
                CASE
                    WHEN ISNULL(ImpressionCounts.TotalImpressions, 0) < @MinimumImpressions THEN NULL
                    ELSE ISNULL(PurchaseCounts.TotalPurchases, 0) * 1.0 / ImpressionCounts.TotalImpressions
                END AS float
            ) AS PurchaseRate,
            CAST(
                CASE
                    WHEN ISNULL(ImpressionCounts.TotalImpressions, 0) < @MinimumImpressions THEN NULL
                    ELSE ISNULL(PurchaseCounts.TotalRevenue, 0) / ImpressionCounts.TotalImpressions
                END AS decimal(18, 6)
            ) AS RevenuePerImpression,
            CAST(
                CASE
                    WHEN ISNULL(ImpressionCounts.TotalImpressions, 0) < @MinimumImpressions THEN N'insufficient_data'
                    ELSE N'ok'
                END AS nvarchar(30)
            ) AS [Status]
        FROM ValidStrategies
        LEFT JOIN ImpressionCounts
            ON ImpressionCounts.RecommendationSource = ValidStrategies.RecommendationSource
            AND ImpressionCounts.ExperimentGroup = ValidStrategies.ExperimentGroup
        LEFT JOIN ClickCounts
            ON ClickCounts.RecommendationSource = ValidStrategies.RecommendationSource
            AND ClickCounts.ExperimentGroup = ValidStrategies.ExperimentGroup
        LEFT JOIN AddToCartCounts
            ON AddToCartCounts.RecommendationSource = ValidStrategies.RecommendationSource
            AND AddToCartCounts.ExperimentGroup = ValidStrategies.ExperimentGroup
        LEFT JOIN PurchaseCounts
            ON PurchaseCounts.RecommendationSource = ValidStrategies.RecommendationSource
            AND PurchaseCounts.ExperimentGroup = ValidStrategies.ExperimentGroup
        ORDER BY
            CASE ValidStrategies.ExperimentGroup
                WHEN N'A' THEN 0
                WHEN N'B' THEN 1
                ELSE 2
            END,
            CASE ValidStrategies.RecommendationSource
                WHEN N'ML' THEN 0
                WHEN N'Session' THEN 1
                ELSE 2
            END;
        """;

    private readonly FreshFarmOrderingDBContext _db;

    public RecommendationMetricsDao(FreshFarmOrderingDBContext db)
    {
        _db = db;
    }

    public async Task AddImpressionsAsync(
        IReadOnlyCollection<RecommendationImpression> impressions,
        CancellationToken cancellationToken = default)
    {
        if (impressions.Count == 0)
        {
            return;
        }

        _db.RecommendationImpressions.AddRange(impressions);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task AddClickAsync(
        RecommendationClick click,
        CancellationToken cancellationToken = default)
    {
        _db.RecommendationClicks.Add(click);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task AddAddToCartAsync(
        RecommendationAddToCart addToCart,
        CancellationToken cancellationToken = default)
    {
        _db.RecommendationAddToCarts.Add(addToCart);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task AddPurchaseAsync(
        RecommendationPurchase purchase,
        CancellationToken cancellationToken = default)
    {
        _db.RecommendationPurchases.Add(purchase);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<RecommendationMetricAttribution?> FindLatestAttributionAsync(
        int? userId,
        int productId,
        int? position,
        DateTime eventTimestampUtc,
        CancellationToken cancellationToken = default)
    {
        if (productId <= 0)
        {
            return null;
        }

        var matched = await _db.RecommendationImpressions
            .AsNoTracking()
            .Where(impression =>
                impression.ProductId == productId
                && impression.UserId == userId
                && impression.Position == position
                && impression.Timestamp <= eventTimestampUtc)
            .OrderByDescending(impression => impression.Timestamp)
            .ThenByDescending(impression => impression.Id)
            .Select(impression => new
            {
                impression.RecommendationSource,
                impression.ExperimentGroup
            })
            .FirstOrDefaultAsync(cancellationToken);

        return matched is null
            ? null
            : new RecommendationMetricAttribution(
                string.IsNullOrWhiteSpace(matched.RecommendationSource)
                    ? "ML"
                    : matched.RecommendationSource,
                string.IsNullOrWhiteSpace(matched.ExperimentGroup)
                    ? "A"
                    : matched.ExperimentGroup);
    }

    public async Task<IReadOnlyList<RecommendationCtrMetricDto>> GetCtrAsync(
        DateTime fromDateUtc,
        DateTime toDateUtc,
        int minimumImpressions,
        CancellationToken cancellationToken = default)
    {
        var results = new List<RecommendationCtrMetricDto>();
        var connection = _db.Database.GetDbConnection();
        var shouldCloseConnection = connection.State == ConnectionState.Closed;

        if (shouldCloseConnection)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = CtrSql;
            command.CommandType = CommandType.Text;
            AddParameter(command, "@FromDateUtc", DbType.DateTime2, fromDateUtc);
            AddParameter(command, "@ToDateUtc", DbType.DateTime2, toDateUtc);
            AddParameter(command, "@MinimumImpressions", DbType.Int32, minimumImpressions);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                results.Add(new RecommendationCtrMetricDto
                {
                    RecommendationSource = reader.GetString(0),
                    ExperimentGroup = reader.GetString(1),
                    TotalImpressions = reader.GetInt64(2),
                    TotalClicks = reader.GetInt64(3),
                    CtrPercentage = reader.IsDBNull(4) ? null : reader.GetDouble(4),
                    Status = reader.GetString(5)
                });
            }
        }
        finally
        {
            if (shouldCloseConnection)
            {
                await connection.CloseAsync();
            }
        }

        return results;
    }

    public async Task<IReadOnlyList<RecommendationCtrByPositionMetricDto>> GetCtrByPositionAsync(
        DateTime fromDateUtc,
        DateTime toDateUtc,
        int minimumImpressions,
        CancellationToken cancellationToken = default)
    {
        var results = new List<RecommendationCtrByPositionMetricDto>();
        var connection = _db.Database.GetDbConnection();
        var shouldCloseConnection = connection.State == ConnectionState.Closed;

        if (shouldCloseConnection)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = CtrByPositionSql;
            command.CommandType = CommandType.Text;
            AddParameter(command, "@FromDateUtc", DbType.DateTime2, fromDateUtc);
            AddParameter(command, "@ToDateUtc", DbType.DateTime2, toDateUtc);
            AddParameter(command, "@MinimumImpressions", DbType.Int32, minimumImpressions);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                results.Add(new RecommendationCtrByPositionMetricDto
                {
                    Position = reader.GetInt32(0),
                    ExperimentGroup = reader.GetString(1),
                    RecommendationSource = reader.GetString(2),
                    TotalImpressions = reader.GetInt64(3),
                    TotalClicks = reader.GetInt64(4),
                    CtrPercentage = reader.IsDBNull(5) ? null : reader.GetDouble(5),
                    Status = reader.GetString(6)
                });
            }
        }
        finally
        {
            if (shouldCloseConnection)
            {
                await connection.CloseAsync();
            }
        }

        return results;
    }

    public async Task<IReadOnlyList<RecommendationObjectiveMetricDto>> GetObjectiveMetricsAsync(
        DateTime fromDateUtc,
        DateTime toDateUtc,
        IReadOnlyCollection<int> productIds,
        int minimumImpressions,
        CancellationToken cancellationToken = default)
    {
        var normalizedProductIds = productIds
            .Where(productId => productId > 0)
            .Distinct()
            .Take(500)
            .ToArray();
        if (normalizedProductIds.Length == 0)
        {
            return Array.Empty<RecommendationObjectiveMetricDto>();
        }

        var results = new List<RecommendationObjectiveMetricDto>();
        var connection = _db.Database.GetDbConnection();
        var shouldCloseConnection = connection.State == ConnectionState.Closed;

        if (shouldCloseConnection)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = ObjectiveMetricsSql;
            command.CommandType = CommandType.Text;
            AddParameter(command, "@FromDateUtc", DbType.DateTime2, fromDateUtc);
            AddParameter(command, "@ToDateUtc", DbType.DateTime2, toDateUtc);
            AddParameter(command, "@ProductIdsCsv", DbType.String, string.Join(',', normalizedProductIds));
            AddParameter(command, "@MinimumImpressions", DbType.Int32, minimumImpressions);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                results.Add(new RecommendationObjectiveMetricDto
                {
                    ProductId = reader.GetInt32(0),
                    TotalImpressions = reader.GetInt64(1),
                    TotalClicks = reader.GetInt64(2),
                    TotalAddToCarts = reader.GetInt64(3),
                    TotalPurchases = reader.GetInt64(4),
                    TotalRevenue = reader.GetDecimal(5),
                    Ctr = reader.IsDBNull(6) ? null : reader.GetDouble(6),
                    AddToCartRate = reader.IsDBNull(7) ? null : reader.GetDouble(7),
                    PurchaseRate = reader.IsDBNull(8) ? null : reader.GetDouble(8),
                    RevenuePerImpression = reader.IsDBNull(9) ? null : reader.GetDecimal(9),
                    Status = reader.GetString(10)
                });
            }
        }
        finally
        {
            if (shouldCloseConnection)
            {
                await connection.CloseAsync();
            }
        }

        return results;
    }

    public async Task<RecommendationObjectiveSummaryDto> GetObjectiveGlobalMetricsAsync(
        DateTime fromDateUtc,
        DateTime toDateUtc,
        IReadOnlyCollection<int> productIds,
        int minimumImpressions,
        CancellationToken cancellationToken = default)
    {
        var normalizedProductIds = NormalizeProductIds(productIds);
        var connection = _db.Database.GetDbConnection();
        var shouldCloseConnection = connection.State == ConnectionState.Closed;

        if (shouldCloseConnection)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = ObjectiveGlobalMetricsSql;
            command.CommandType = CommandType.Text;
            AddParameter(command, "@FromDateUtc", DbType.DateTime2, fromDateUtc);
            AddParameter(command, "@ToDateUtc", DbType.DateTime2, toDateUtc);
            AddParameter(command, "@ProductIdsCsv", DbType.String, string.Join(',', normalizedProductIds));
            AddParameter(command, "@MinimumImpressions", DbType.Int32, minimumImpressions);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            return await reader.ReadAsync(cancellationToken)
                ? new RecommendationObjectiveSummaryDto
                {
                    TotalImpressions = reader.GetInt64(0),
                    TotalClicks = reader.GetInt64(1),
                    TotalAddToCarts = reader.GetInt64(2),
                    TotalPurchases = reader.GetInt64(3),
                    TotalRevenue = reader.GetDecimal(4),
                    Ctr = reader.IsDBNull(5) ? null : reader.GetDouble(5),
                    AddToCartRate = reader.IsDBNull(6) ? null : reader.GetDouble(6),
                    PurchaseRate = reader.IsDBNull(7) ? null : reader.GetDouble(7),
                    RevenuePerImpression = reader.IsDBNull(8) ? null : reader.GetDecimal(8),
                    Status = reader.GetString(9)
                }
                : new RecommendationObjectiveSummaryDto
                {
                    Status = "insufficient_data"
                };
        }
        finally
        {
            if (shouldCloseConnection)
            {
                await connection.CloseAsync();
            }
        }
    }

    public async Task<IReadOnlyList<RecommendationObjectiveGroupMetricDto>> GetObjectiveGroupMetricsAsync(
        DateTime fromDateUtc,
        DateTime toDateUtc,
        IReadOnlyCollection<int> productIds,
        int minimumImpressions,
        CancellationToken cancellationToken = default)
    {
        var normalizedProductIds = NormalizeProductIds(productIds);
        var results = new List<RecommendationObjectiveGroupMetricDto>();
        var connection = _db.Database.GetDbConnection();
        var shouldCloseConnection = connection.State == ConnectionState.Closed;

        if (shouldCloseConnection)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = ObjectiveGroupMetricsSql;
            command.CommandType = CommandType.Text;
            AddParameter(command, "@FromDateUtc", DbType.DateTime2, fromDateUtc);
            AddParameter(command, "@ToDateUtc", DbType.DateTime2, toDateUtc);
            AddParameter(command, "@ProductIdsCsv", DbType.String, string.Join(',', normalizedProductIds));
            AddParameter(command, "@MinimumImpressions", DbType.Int32, minimumImpressions);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                results.Add(new RecommendationObjectiveGroupMetricDto
                {
                    RecommendationSource = reader.GetString(0),
                    ExperimentGroup = reader.GetString(1),
                    TotalImpressions = reader.GetInt64(2),
                    TotalClicks = reader.GetInt64(3),
                    TotalAddToCarts = reader.GetInt64(4),
                    TotalPurchases = reader.GetInt64(5),
                    TotalRevenue = reader.GetDecimal(6),
                    Ctr = reader.IsDBNull(7) ? null : reader.GetDouble(7),
                    AddToCartRate = reader.IsDBNull(8) ? null : reader.GetDouble(8),
                    PurchaseRate = reader.IsDBNull(9) ? null : reader.GetDouble(9),
                    RevenuePerImpression = reader.IsDBNull(10) ? null : reader.GetDecimal(10),
                    Status = reader.GetString(11)
                });
            }
        }
        finally
        {
            if (shouldCloseConnection)
            {
                await connection.CloseAsync();
            }
        }

        return results;
    }

    public async Task<IReadOnlyList<RecommendationNegativeFeedbackMetricDto>> GetNegativeFeedbackAsync(
        int? userId,
        DateTime fromDateUtc,
        DateTime toDateUtc,
        IReadOnlyCollection<int> productIds,
        CancellationToken cancellationToken = default)
    {
        var normalizedProductIds = NormalizeProductIds(productIds);
        if (normalizedProductIds.Length == 0)
        {
            return Array.Empty<RecommendationNegativeFeedbackMetricDto>();
        }

        var scores = await RecommendationNegativeFeedbackScoring.LoadScoresAsync(
            _db,
            fromDateUtc,
            toDateUtc,
            DateTime.UtcNow,
            userId,
            normalizedProductIds,
            cancellationToken);

        return scores
            .Select(score => new RecommendationNegativeFeedbackMetricDto
            {
                ProductId = score.ProductId,
                ImpressionNoClickCount = score.ImpressionNoClickCount,
                RepeatedImpressionCount = score.RepeatedImpressionCount,
                BounceCount = score.BounceCount,
                PenaltyScore = score.PenaltyScore,
                LastSignalAtUtc = score.LastSignalAtUtc,
                Status = score.PenaltyScore < 0d ? "ok" : "no_negative_signal"
            })
            .OrderBy(item => item.ProductId)
            .ToArray();
    }

    private static void AddParameter(
        IDbCommand command,
        string name,
        DbType type,
        object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.DbType = type;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    private static int[] NormalizeProductIds(IReadOnlyCollection<int> productIds)
    {
        return productIds
            .Where(productId => productId > 0)
            .Distinct()
            .Take(500)
            .ToArray();
    }
}
