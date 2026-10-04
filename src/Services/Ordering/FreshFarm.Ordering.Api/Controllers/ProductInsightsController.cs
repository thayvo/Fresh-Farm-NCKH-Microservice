using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using FreshFarm.Ordering.Api.Models;
using FreshFarm.Ordering.Api.Options;
using FreshFarm.Ordering.Api.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FreshFarm.Ordering.Api.Controllers;

[ApiController]
[Route("api/orders/product-insights")]
public sealed class ProductInsightsController : ControllerBase
{
    private static readonly string[] SuccessfulOrderStatuses = ["delivered", "completed"];
    private const int InteractionLookbackDays = 180;
    private const int HomePreferenceLookbackDays = 90;
    private const string RecommendationSignalSourceHeader = "X-Recommendation-Signal-Source";
    private const string RecommendationFallbackReasonHeader = "X-Recommendation-Fallback-Reason";
    private const string MaterializedCollaborativeSignalSource = "materialized_cf_v1";
    private const string MlnetUserProductSignalSource = "mlnet_user_product_v1";
    private const string AdHocCollaborativeSignalSource = "ad_hoc_cf_v1";
    private const string MaterializedProfileSignalSource = "materialized_profile_v1";
    private const string AdHocProfileSignalSource = "ad_hoc_profile_v1";
    private const string MissingSessionOrUserScopeFallbackReason = "missing_session_or_user_scope";
    private const string NoMaterializedProfileSeedFallbackReason = "no_materialized_profile_seed";
    private const string NoTrackedPreferenceInteractionsFallbackReason = "no_tracked_preference_interactions";
    private const string NoPreferenceSeedForCollaborativeFallbackReason = "no_preference_seed_for_collaborative";
    private const string NoMaterializedCollaborativeCandidatesFallbackReason = "no_materialized_collaborative_candidates";
    private const string NoMaterializedKeywordAffinityFallbackReason = "no_materialized_keyword_affinity";
    private const string NoCollaborativeInteractionsForSeedProductFallbackReason = "no_collaborative_interactions_for_seed_product";
    private const string NoTrackedKeywordSessionsFallbackReason = "no_tracked_keyword_sessions";
    private const string NoBehavioralSignalForKeywordFallbackReason = "no_behavioral_signal_for_keyword";
    private const double PersonalizedSeasonalityBoostMultiplier = 0.12d;
    private const double FallbackSeasonalityBoostMultiplier = 0.24d;
    private static readonly DateTime SqlServerDateTimeFloor = new(1753, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private readonly FreshFarmOrderingDBContext _db;
    private readonly RecommendationAffinityService? _recommendationAffinityService;
    private readonly RecommendationAffinityRefreshSignal? _recommendationAffinityRefreshSignal;
    private readonly InternalServiceAuthOptions? _internalServiceAuthOptions;
    private readonly CatalogInventoryClient? _catalogInventoryClient;

    public ProductInsightsController(FreshFarmOrderingDBContext db)
    {
        _db = db;
    }

    public ProductInsightsController(
        FreshFarmOrderingDBContext db,
        RecommendationAffinityRefreshSignal recommendationAffinityRefreshSignal)
        : this(db)
    {
        _recommendationAffinityRefreshSignal = recommendationAffinityRefreshSignal;
    }

    [ActivatorUtilitiesConstructor]
    public ProductInsightsController(
        FreshFarmOrderingDBContext db,
        RecommendationAffinityService recommendationAffinityService,
        RecommendationAffinityRefreshSignal recommendationAffinityRefreshSignal,
        IOptions<InternalServiceAuthOptions> internalServiceAuthOptions,
        CatalogInventoryClient catalogInventoryClient)
        : this(db)
    {
        _recommendationAffinityService = recommendationAffinityService;
        _recommendationAffinityRefreshSignal = recommendationAffinityRefreshSignal;
        _internalServiceAuthOptions = internalServiceAuthOptions.Value;
        _catalogInventoryClient = catalogInventoryClient;
    }

    [HttpGet("home-profile")]
    public async Task<IActionResult> GetHomeProfile(
        [FromQuery] string? sessionId,
        [FromQuery] int limit = 12,
        CancellationToken cancellationToken = default)
    {
        var normalizedSessionId = NormalizeOptionalText(sessionId, 120);
        var userId = await TryGetUserIdFromTokenAsync();
        var useSessionScope = !string.IsNullOrWhiteSpace(normalizedSessionId);
        var useUserScope = userId.HasValue;

        if (!useSessionScope && !useUserScope)
        {
            SetRecommendationFallbackReason(MissingSessionOrUserScopeFallbackReason);
            return Ok(Array.Empty<HomePreferenceSeedDto>());
        }

        var rankedSignals = await BuildHomePreferenceSeedsAsync(normalizedSessionId, userId, Math.Clamp(limit, 1, 48), cancellationToken);
        rankedSignals = await ApplySeasonalityBoostAsync(
            rankedSignals,
            PersonalizedSeasonalityBoostMultiplier,
            Math.Clamp(limit, 1, 48),
            cancellationToken);
        return Ok(rankedSignals);
    }

    [HttpGet("home-collaborative")]
    public async Task<IActionResult> GetHomeCollaborative(
        [FromQuery] string? sessionId,
        [FromQuery] int limit = 24,
        CancellationToken cancellationToken = default)
    {
        var normalizedSessionId = NormalizeOptionalText(sessionId, 120);
        var userId = await TryGetUserIdFromTokenAsync();
        var useSessionScope = !string.IsNullOrWhiteSpace(normalizedSessionId);
        var useUserScope = userId.HasValue;
        if (!useSessionScope && !useUserScope)
        {
            SetRecommendationFallbackReason(MissingSessionOrUserScopeFallbackReason);
            return Ok(Array.Empty<HomeCollaborativeCandidateDto>());
        }

        var seedSignals = await BuildHomePreferenceSeedsAsync(normalizedSessionId, userId, 8, cancellationToken);
        seedSignals = await ApplySeasonalityBoostAsync(
            seedSignals,
            PersonalizedSeasonalityBoostMultiplier,
            8,
            cancellationToken);
        if (seedSignals.Length == 0)
        {
            SetRecommendationFallbackReason(NoPreferenceSeedForCollaborativeFallbackReason);
            return Ok(Array.Empty<HomeCollaborativeCandidateDto>());
        }

        var seedProductIds = seedSignals.Select(item => item.ProductId).ToHashSet();
        var materializedCandidates = await BuildMaterializedHomeCollaborativeAsync(
            seedSignals,
            seedProductIds,
            normalizedSessionId,
            userId,
            Math.Clamp(limit, 1, 48),
            cancellationToken);
        if (materializedCandidates.Length > 0)
        {
            SetRecommendationMetadata(MaterializedCollaborativeSignalSource);
            materializedCandidates = await ApplySeasonalityBoostAsync(
                materializedCandidates,
                PersonalizedSeasonalityBoostMultiplier,
                Math.Clamp(limit, 1, 48),
                cancellationToken);
            return Ok(materializedCandidates);
        }

        var lookbackFromUtc = DateTime.UtcNow.AddDays(-InteractionLookbackDays);
        var candidatesByProductId = new Dictionary<int, HomeCollaborativeCandidateDto>();

        void UpdateCandidate(int productId, Action<HomeCollaborativeCandidateDto> apply)
        {
            if (productId <= 0 || seedProductIds.Contains(productId))
            {
                return;
            }

            if (!candidatesByProductId.TryGetValue(productId, out var candidate))
            {
                candidate = new HomeCollaborativeCandidateDto
                {
                    ProductId = productId
                };
                candidatesByProductId[productId] = candidate;
            }

            apply(candidate);
        }

        foreach (var seed in seedSignals)
        {
            var seedWeight = 1d + Math.Min(seed.PreferenceScore, 240d) / 120d;

            var seedSuccessfulOrderIds = await (
                    from detail in _db.OrderDetails.AsNoTracking()
                    join order in _db.Orders.AsNoTracking() on detail.OrderId equals order.OrderId
                    where detail.ProductId == seed.ProductId
                        && order.OrderDate >= lookbackFromUtc
                        && SuccessfulOrderStatuses.Contains((order.Status ?? string.Empty).Trim().ToLower())
                    select detail.OrderId)
                .Distinct()
                .Take(5000)
                .ToListAsync(cancellationToken);

            if (seedSuccessfulOrderIds.Count > 0)
            {
                var coPurchaseSignals = await _db.OrderDetails
                    .AsNoTracking()
                    .Where(detail =>
                        seedSuccessfulOrderIds.Contains(detail.OrderId)
                        && detail.ProductId > 0
                        && detail.ProductId != seed.ProductId)
                    .GroupBy(detail => detail.ProductId)
                    .Select(grouped => new
                    {
                        ProductId = grouped.Key,
                        CoPurchaseOrderCount = grouped.Select(item => item.OrderId).Distinct().Count()
                    })
                    .ToListAsync(cancellationToken);

                foreach (var signal in coPurchaseSignals)
                {
                    UpdateCandidate(signal.ProductId, candidate =>
                    {
                        candidate.CoPurchaseOrderCount += signal.CoPurchaseOrderCount;
                        candidate.CollaborativeScore += signal.CoPurchaseOrderCount * 32d * seedWeight;
                    });
                }
            }

            var seedViewSessions = await _db.ProductViewEvents
                .AsNoTracking()
                .Where(view =>
                    view.ProductId == seed.ProductId
                    && view.CreatedAt >= lookbackFromUtc
                    && view.SessionId != string.Empty)
                .Select(view => view.SessionId)
                .Distinct()
                .Take(5000)
                .ToListAsync(cancellationToken);

            if (seedViewSessions.Count > 0)
            {
                var coViewSignals = await _db.ProductViewEvents
                    .AsNoTracking()
                    .Where(view =>
                        view.ProductId != seed.ProductId
                        && view.ProductId > 0
                        && view.CreatedAt >= lookbackFromUtc
                        && seedViewSessions.Contains(view.SessionId))
                    .GroupBy(view => view.ProductId)
                    .Select(grouped => new
                    {
                        ProductId = grouped.Key,
                        CoViewSessionCount = grouped.Select(item => item.SessionId).Distinct().Count()
                    })
                    .ToListAsync(cancellationToken);

                foreach (var signal in coViewSignals)
                {
                    UpdateCandidate(signal.ProductId, candidate =>
                    {
                        candidate.CoViewSessionCount += signal.CoViewSessionCount;
                        candidate.CollaborativeScore += signal.CoViewSessionCount * 6d * seedWeight;
                    });
                }
            }

            var seedSearchClickSessions = await _db.SearchClickEvents
                .AsNoTracking()
                .Where(click =>
                    click.ProductId == seed.ProductId
                    && click.CreatedAt >= lookbackFromUtc
                    && click.SessionId != string.Empty)
                .Select(click => click.SessionId)
                .Distinct()
                .Take(5000)
                .ToListAsync(cancellationToken);

            var seedRecommendationClickSessions = await _db.RecommendationClickEvents
                .AsNoTracking()
                .Where(click =>
                    click.ProductId == seed.ProductId
                    && click.CreatedAt >= lookbackFromUtc
                    && click.SessionId != string.Empty)
                .Select(click => click.SessionId)
                .Distinct()
                .Take(5000)
                .ToListAsync(cancellationToken);

            var seedClickSessions = seedSearchClickSessions
                .Concat(seedRecommendationClickSessions)
                .Distinct(StringComparer.Ordinal)
                .Take(5000)
                .ToArray();

            if (seedClickSessions.Length == 0)
            {
                continue;
            }

            var searchClickSignals = await _db.SearchClickEvents
                .AsNoTracking()
                .Where(click =>
                    click.ProductId != seed.ProductId
                    && click.ProductId > 0
                    && click.CreatedAt >= lookbackFromUtc
                    && seedClickSessions.Contains(click.SessionId))
                .GroupBy(click => click.ProductId)
                .Select(grouped => new
                {
                    ProductId = grouped.Key,
                    CoClickSessionCount = grouped.Select(item => item.SessionId).Distinct().Count()
                })
                .ToListAsync(cancellationToken);

            foreach (var signal in searchClickSignals)
            {
                UpdateCandidate(signal.ProductId, candidate =>
                {
                    candidate.CoClickSessionCount += signal.CoClickSessionCount;
                    candidate.CollaborativeScore += signal.CoClickSessionCount * 14d * seedWeight;
                });
            }

            var recommendationClickSignals = await _db.RecommendationClickEvents
                .AsNoTracking()
                .Where(click =>
                    click.ProductId != seed.ProductId
                    && click.ProductId > 0
                    && click.CreatedAt >= lookbackFromUtc
                    && seedClickSessions.Contains(click.SessionId))
                .GroupBy(click => click.ProductId)
                .Select(grouped => new
                {
                    ProductId = grouped.Key,
                    CoClickSessionCount = grouped.Select(item => item.SessionId).Distinct().Count()
                })
                .ToListAsync(cancellationToken);

            foreach (var signal in recommendationClickSignals)
            {
                UpdateCandidate(signal.ProductId, candidate =>
                {
                    candidate.CoClickSessionCount += signal.CoClickSessionCount;
                    candidate.CollaborativeScore += signal.CoClickSessionCount * 14d * seedWeight;
                });
            }
        }

        var rankedCandidates = candidatesByProductId.Values
            .Where(candidate => candidate.CollaborativeScore > 0d)
            .OrderByDescending(candidate => candidate.CollaborativeScore)
            .ThenByDescending(candidate => candidate.CoPurchaseOrderCount)
            .ThenByDescending(candidate => candidate.CoClickSessionCount)
            .ThenByDescending(candidate => candidate.CoViewSessionCount)
            .ThenBy(candidate => candidate.ProductId)
            .Take(Math.Clamp(limit, 1, 48))
            .ToArray();

        RequestRecommendationAffinityRefresh();
        SetRecommendationMetadata(AdHocCollaborativeSignalSource, NoMaterializedCollaborativeCandidatesFallbackReason);
        rankedCandidates = await ApplySeasonalityBoostAsync(
            rankedCandidates,
            FallbackSeasonalityBoostMultiplier,
            Math.Clamp(limit, 1, 48),
            cancellationToken);
        return Ok(rankedCandidates);
    }

    [HttpGet("stats")]
    public async Task<IActionResult> GetStats(
        [FromQuery] int[]? productIds,
        [FromQuery] int recentWindowDays = 90,
        CancellationToken cancellationToken = default)
    {
        var normalizedProductIds = (productIds ?? Array.Empty<int>())
            .Where(id => id > 0)
            .Distinct()
            .Take(200)
            .ToArray();

        if (normalizedProductIds.Length == 0)
        {
            return Ok(Array.Empty<ProductStatsDto>());
        }

        var normalizedRecentWindowDays = Math.Clamp(recentWindowDays, 30, 90);
        var recentSalesFromUtc = DateTime.UtcNow.AddDays(-normalizedRecentWindowDays);
        var soldCounts = await (
            from detail in _db.OrderDetails.AsNoTracking()
            join order in _db.Orders.AsNoTracking() on detail.OrderId equals order.OrderId
            where normalizedProductIds.Contains(detail.ProductId)
                && SuccessfulOrderStatuses.Contains((order.Status ?? string.Empty).Trim().ToLower())
            group detail by detail.ProductId into grouped
            select new
            {
                ProductId = grouped.Key,
                SoldCount = grouped.Sum(item => item.Quantity)
            })
            .ToListAsync(cancellationToken);

        var recentSoldCounts = await (
            from detail in _db.OrderDetails.AsNoTracking()
            join order in _db.Orders.AsNoTracking() on detail.OrderId equals order.OrderId
            where normalizedProductIds.Contains(detail.ProductId)
                && order.OrderDate >= recentSalesFromUtc
                && SuccessfulOrderStatuses.Contains((order.Status ?? string.Empty).Trim().ToLower())
            group detail by detail.ProductId into grouped
            select new
            {
                ProductId = grouped.Key,
                RecentSoldCount = grouped.Sum(item => item.Quantity)
            })
            .ToListAsync(cancellationToken);

        var approvedReviewStats = await _db.Reviews
            .AsNoTracking()
            .Where(review =>
                normalizedProductIds.Contains(review.ProductId)
                && review.IsApproved
                && !review.IsDeleted
                && review.Rating > 0)
            .GroupBy(review => review.ProductId)
            .Select(grouped => new
            {
                ProductId = grouped.Key,
                ReviewCount = grouped.Count(),
                AverageRating = decimal.Round((decimal)grouped.Average(review => review.Rating), 1)
            })
            .ToListAsync(cancellationToken);

        var statsByProductId = normalizedProductIds.ToDictionary(
            productId => productId,
            productId => new ProductStatsDto
            {
                ProductId = productId
            });

        foreach (var sold in soldCounts)
        {
            statsByProductId[sold.ProductId].SoldCount = sold.SoldCount;
        }

        foreach (var sold in recentSoldCounts)
        {
            statsByProductId[sold.ProductId].RecentSoldCount = sold.RecentSoldCount;
        }

        foreach (var review in approvedReviewStats)
        {
            statsByProductId[review.ProductId].AverageRating = review.AverageRating;
            statsByProductId[review.ProductId].ReviewCount = review.ReviewCount;
        }

        return Ok(statsByProductId.Values.OrderBy(item => item.ProductId));
    }

    [HttpGet("similar")]
    public async Task<IActionResult> GetSimilar(
        [FromQuery] int productId,
        [FromQuery] int[]? candidateProductIds,
        [FromQuery] int limit = 24,
        CancellationToken cancellationToken = default)
    {
        var normalizedCandidateIds = (candidateProductIds ?? Array.Empty<int>())
            .Where(id => id > 0 && id != productId)
            .Distinct()
            .Take(200)
            .ToArray();

        if (productId <= 0 || normalizedCandidateIds.Length == 0)
        {
            return Ok(Array.Empty<ProductSimilarSignalDto>());
        }

        var materializedSignals = await BuildMaterializedSimilarSignalsAsync(
            productId,
            normalizedCandidateIds,
            Math.Clamp(limit, 1, 48),
            cancellationToken);
        if (materializedSignals.Length > 0)
        {
            SetRecommendationMetadata(MaterializedCollaborativeSignalSource);
            return Ok(materializedSignals);
        }

        var lookbackFromUtc = DateTime.UtcNow.AddDays(-InteractionLookbackDays);
        var signalsByProductId = normalizedCandidateIds.ToDictionary(
            candidateProductId => candidateProductId,
            candidateProductId => new ProductSimilarSignalDto
            {
                ProductId = candidateProductId
            });

        var seedSuccessfulOrderIds = await (
            from detail in _db.OrderDetails.AsNoTracking()
            join order in _db.Orders.AsNoTracking() on detail.OrderId equals order.OrderId
            where detail.ProductId == productId
                && SuccessfulOrderStatuses.Contains((order.Status ?? string.Empty).Trim().ToLower())
            select detail.OrderId)
            .Distinct()
            .Take(5000)
            .ToListAsync(cancellationToken);

        if (seedSuccessfulOrderIds.Count > 0)
        {
            var coPurchaseSignals = await _db.OrderDetails
                .AsNoTracking()
                .Where(detail =>
                    seedSuccessfulOrderIds.Contains(detail.OrderId)
                    && normalizedCandidateIds.Contains(detail.ProductId))
                .GroupBy(detail => detail.ProductId)
                .Select(grouped => new
                {
                    ProductId = grouped.Key,
                    CoPurchaseOrderCount = grouped.Select(item => item.OrderId).Distinct().Count()
                })
                .ToListAsync(cancellationToken);

            foreach (var signal in coPurchaseSignals)
            {
                signalsByProductId[signal.ProductId].CoPurchaseOrderCount = signal.CoPurchaseOrderCount;
            }
        }

        var seedViewSessions = await _db.ProductViewEvents
            .AsNoTracking()
            .Where(view =>
                view.ProductId == productId
                && view.CreatedAt >= lookbackFromUtc
                && view.SessionId != string.Empty)
            .Select(view => view.SessionId)
            .Distinct()
            .Take(5000)
            .ToListAsync(cancellationToken);

        if (seedViewSessions.Count > 0)
        {
            var coViewSignals = await _db.ProductViewEvents
                .AsNoTracking()
                .Where(view =>
                    normalizedCandidateIds.Contains(view.ProductId)
                    && view.CreatedAt >= lookbackFromUtc
                    && seedViewSessions.Contains(view.SessionId))
                .GroupBy(view => view.ProductId)
                .Select(grouped => new
                {
                    ProductId = grouped.Key,
                    CoViewSessionCount = grouped.Select(item => item.SessionId).Distinct().Count()
                })
                .ToListAsync(cancellationToken);

            foreach (var signal in coViewSignals)
            {
                signalsByProductId[signal.ProductId].CoViewSessionCount = signal.CoViewSessionCount;
            }
        }

        var seedSearchClickSessions = await _db.SearchClickEvents
            .AsNoTracking()
            .Where(click =>
                click.ProductId == productId
                && click.CreatedAt >= lookbackFromUtc
                && click.SessionId != string.Empty)
            .Select(click => click.SessionId)
            .Distinct()
            .Take(5000)
            .ToListAsync(cancellationToken);

        var seedRecommendationClickSessions = await _db.RecommendationClickEvents
            .AsNoTracking()
            .Where(click =>
                click.ProductId == productId
                && click.CreatedAt >= lookbackFromUtc
                && click.SessionId != string.Empty)
            .Select(click => click.SessionId)
            .Distinct()
            .Take(5000)
            .ToListAsync(cancellationToken);

        var seedClickSessions = seedSearchClickSessions
            .Concat(seedRecommendationClickSessions)
            .Distinct(StringComparer.Ordinal)
            .Take(5000)
            .ToArray();

        if (seedClickSessions.Length > 0)
        {
            var searchClickSignals = await _db.SearchClickEvents
                .AsNoTracking()
                .Where(click =>
                    normalizedCandidateIds.Contains(click.ProductId)
                    && click.CreatedAt >= lookbackFromUtc
                    && seedClickSessions.Contains(click.SessionId))
                .GroupBy(click => click.ProductId)
                .Select(grouped => new
                {
                    ProductId = grouped.Key,
                    CoClickSessionCount = grouped.Select(item => item.SessionId).Distinct().Count()
                })
                .ToListAsync(cancellationToken);

            foreach (var signal in searchClickSignals)
            {
                signalsByProductId[signal.ProductId].CoClickSessionCount += signal.CoClickSessionCount;
            }

            var recommendationClickSignals = await _db.RecommendationClickEvents
                .AsNoTracking()
                .Where(click =>
                    normalizedCandidateIds.Contains(click.ProductId)
                    && click.CreatedAt >= lookbackFromUtc
                    && seedClickSessions.Contains(click.SessionId))
                .GroupBy(click => click.ProductId)
                .Select(grouped => new
                {
                    ProductId = grouped.Key,
                    CoClickSessionCount = grouped.Select(item => item.SessionId).Distinct().Count()
                })
                .ToListAsync(cancellationToken);

            foreach (var signal in recommendationClickSignals)
            {
                signalsByProductId[signal.ProductId].CoClickSessionCount += signal.CoClickSessionCount;
            }
        }

        foreach (var signal in signalsByProductId.Values)
        {
            signal.CollaborativeScore =
                (signal.CoPurchaseOrderCount * 32d)
                + (signal.CoClickSessionCount * 14d)
                + (signal.CoViewSessionCount * 6d);
        }

        var rankedSignals = signalsByProductId.Values
            .Where(signal => signal.CollaborativeScore > 0)
            .OrderByDescending(signal => signal.CollaborativeScore)
            .ThenByDescending(signal => signal.CoPurchaseOrderCount)
            .ThenByDescending(signal => signal.CoClickSessionCount)
            .ThenByDescending(signal => signal.CoViewSessionCount)
            .ThenBy(signal => signal.ProductId)
            .Take(Math.Clamp(limit, 1, 48))
            .ToArray();

        if (rankedSignals.Length == 0)
        {
            SetRecommendationFallbackReason(NoCollaborativeInteractionsForSeedProductFallbackReason);
            return Ok(Array.Empty<ProductSimilarSignalDto>());
        }

        RequestRecommendationAffinityRefresh();
        SetRecommendationMetadata(AdHocCollaborativeSignalSource, NoMaterializedCollaborativeCandidatesFallbackReason);
        return Ok(rankedSignals);
    }

    [HttpGet("search-ranking")]
    public async Task<IActionResult> GetSearchRanking(
        [FromQuery] string? keyword,
        [FromQuery] int[]? productIds,
        [FromQuery] int limit = 48,
        CancellationToken cancellationToken = default)
    {
        var normalizedKeyword = (keyword ?? string.Empty).Trim().ToLowerInvariant();
        var normalizedProductIds = (productIds ?? Array.Empty<int>())
            .Where(id => id > 0)
            .Distinct()
            .Take(200)
            .ToArray();

        if (string.IsNullOrWhiteSpace(normalizedKeyword) || normalizedProductIds.Length == 0)
        {
            return Ok(Array.Empty<SearchRankingSignalDto>());
        }

        var materializedSearchSignals = await BuildMaterializedSearchRankingSignalsAsync(
            normalizedKeyword,
            normalizedProductIds,
            Math.Clamp(limit, 1, 96),
            cancellationToken);
        if (materializedSearchSignals.Length > 0)
        {
            SetRecommendationMetadata(MaterializedCollaborativeSignalSource);
            materializedSearchSignals = await ApplySeasonalityBoostAsync(
                materializedSearchSignals,
                FallbackSeasonalityBoostMultiplier,
                Math.Clamp(limit, 1, 96),
                cancellationToken);
            return Ok(materializedSearchSignals);
        }

        var lookbackFromUtc = DateTime.UtcNow.AddDays(-InteractionLookbackDays);
        var signalsByProductId = normalizedProductIds.ToDictionary(
            productId => productId,
            productId => new SearchRankingSignalDto
            {
                ProductId = productId
            });

        var searchSessions = await _db.SearchEvents
            .AsNoTracking()
            .Where(search =>
                search.CreatedAt >= lookbackFromUtc
                && search.SessionId != string.Empty
                && ((search.Keyword ?? string.Empty).Trim().ToLower()) == normalizedKeyword)
            .Select(search => search.SessionId)
            .Distinct()
            .Take(5000)
            .ToListAsync(cancellationToken);

        if (searchSessions.Count == 0)
        {
            SetRecommendationFallbackReason(NoTrackedKeywordSessionsFallbackReason);
            return Ok(Array.Empty<SearchRankingSignalDto>());
        }

        var searchClickSignals = await _db.SearchClickEvents
            .AsNoTracking()
            .Where(click =>
                normalizedProductIds.Contains(click.ProductId)
                && click.CreatedAt >= lookbackFromUtc
                && searchSessions.Contains(click.SessionId))
            .GroupBy(click => click.ProductId)
            .Select(grouped => new
            {
                ProductId = grouped.Key,
                SearchClickCount = grouped.Count(),
                SearchClickSessionCount = grouped.Select(item => item.SessionId).Distinct().Count()
            })
            .ToListAsync(cancellationToken);

        foreach (var signal in searchClickSignals)
        {
            signalsByProductId[signal.ProductId].SearchClickCount = signal.SearchClickCount;
            signalsByProductId[signal.ProductId].SearchClickSessionCount = signal.SearchClickSessionCount;
        }

        var viewSignals = await _db.ProductViewEvents
            .AsNoTracking()
            .Where(view =>
                normalizedProductIds.Contains(view.ProductId)
                && view.CreatedAt >= lookbackFromUtc
                && searchSessions.Contains(view.SessionId))
            .GroupBy(view => view.ProductId)
            .Select(grouped => new
            {
                ProductId = grouped.Key,
                SearchViewSessionCount = grouped.Select(item => item.SessionId).Distinct().Count()
            })
            .ToListAsync(cancellationToken);

        foreach (var signal in viewSignals)
        {
            signalsByProductId[signal.ProductId].SearchViewSessionCount = signal.SearchViewSessionCount;
        }

        var recommendationClickSignals = await _db.RecommendationClickEvents
            .AsNoTracking()
            .Where(click =>
                normalizedProductIds.Contains(click.ProductId)
                && click.CreatedAt >= lookbackFromUtc
                && searchSessions.Contains(click.SessionId))
            .GroupBy(click => click.ProductId)
            .Select(grouped => new
            {
                ProductId = grouped.Key,
                SearchRecommendationClickCount = grouped.Count()
            })
            .ToListAsync(cancellationToken);

        foreach (var signal in recommendationClickSignals)
        {
            signalsByProductId[signal.ProductId].SearchRecommendationClickCount = signal.SearchRecommendationClickCount;
        }

        foreach (var signal in signalsByProductId.Values)
        {
            signal.HybridSearchScore =
                (signal.SearchClickCount * 20d)
                + (signal.SearchRecommendationClickCount * 14d)
                + (signal.SearchViewSessionCount * 6d);
        }

        var rankedSignals = signalsByProductId.Values
            .Where(signal => signal.HybridSearchScore > 0)
            .OrderByDescending(signal => signal.HybridSearchScore)
            .ThenByDescending(signal => signal.SearchClickCount)
            .ThenByDescending(signal => signal.SearchRecommendationClickCount)
            .ThenByDescending(signal => signal.SearchViewSessionCount)
            .ThenBy(signal => signal.ProductId)
            .Take(Math.Clamp(limit, 1, 96))
            .ToArray();

        if (rankedSignals.Length == 0)
        {
            SetRecommendationFallbackReason(NoBehavioralSignalForKeywordFallbackReason);
            return Ok(Array.Empty<SearchRankingSignalDto>());
        }

        RequestRecommendationAffinityRefresh();
        SetRecommendationMetadata(AdHocCollaborativeSignalSource, NoMaterializedKeywordAffinityFallbackReason);
        rankedSignals = await ApplySeasonalityBoostAsync(
            rankedSignals,
            FallbackSeasonalityBoostMultiplier,
            Math.Clamp(limit, 1, 96),
            cancellationToken);
        return Ok(rankedSignals);
    }

    [HttpGet("user-product")]
    public async Task<IActionResult> GetUserProductScores(
        [FromQuery] int? userId,
        [FromQuery] int[]? productIds,
        [FromQuery] int limit = 24,
        CancellationToken cancellationToken = default)
    {
        var resolvedUserId = userId.GetValueOrDefault() > 0
            ? userId
            : await TryGetUserIdFromTokenAsync();
        if (!resolvedUserId.HasValue || resolvedUserId.Value <= 0)
        {
            return Ok(Array.Empty<UserProductScoreDto>());
        }

        var normalizedProductIds = (productIds ?? Array.Empty<int>())
            .Where(id => id > 0)
            .Distinct()
            .Take(200)
            .ToArray();

        var query = _db.RecommendationUserProductScores
            .AsNoTracking()
            .Where(item => item.UserId == resolvedUserId.Value && item.UserProductScore > 0d);

        if (normalizedProductIds.Length > 0)
        {
            query = query.Where(item => normalizedProductIds.Contains(item.ProductId));
        }

        var rows = await query
            .OrderByDescending(item => item.UserProductScore)
            .ThenByDescending(item => item.PurchaseCount)
            .ThenByDescending(item => item.SearchClickCount)
            .ThenByDescending(item => item.RecommendationClickCount)
            .ThenByDescending(item => item.ViewCount)
            .ThenByDescending(item => item.LastInteractedAtUtc ?? SqlServerDateTimeFloor)
            .ThenBy(item => item.ProductId)
            .Take(Math.Clamp(limit, 1, 96))
            .Select(item => new UserProductScoreDto
            {
                UserId = item.UserId,
                ProductId = item.ProductId,
                ViewCount = item.ViewCount,
                SearchClickCount = item.SearchClickCount,
                RecommendationClickCount = item.RecommendationClickCount,
                PurchaseCount = item.PurchaseCount,
                UserProductScore = item.UserProductScore,
                LastInteractedAtUtc = item.LastInteractedAtUtc,
                ComputedAtUtc = item.ComputedAt
            })
            .ToArrayAsync(cancellationToken);

        if (rows.Length > 0)
        {
            SetRecommendationMetadata(MlnetUserProductSignalSource);
        }

        return Ok(rows);
    }

    [HttpGet("user-seller")]
    public async Task<IActionResult> GetUserSellerScores(
        [FromQuery] int? userId,
        [FromQuery] int[]? sellerIds,
        [FromQuery] int limit = 24,
        CancellationToken cancellationToken = default)
    {
        var resolvedUserId = userId.GetValueOrDefault() > 0
            ? userId
            : await TryGetUserIdFromTokenAsync();
        if (!resolvedUserId.HasValue || resolvedUserId.Value <= 0)
        {
            return Ok(Array.Empty<UserSellerScoreDto>());
        }

        var normalizedSellerIds = (sellerIds ?? Array.Empty<int>())
            .Where(id => id > 0)
            .Distinct()
            .Take(200)
            .ToArray();

        var query = _db.RecommendationUserSellerScores
            .AsNoTracking()
            .Where(item => item.UserId == resolvedUserId.Value && item.UserSellerScore > 0d);

        if (normalizedSellerIds.Length > 0)
        {
            query = query.Where(item => normalizedSellerIds.Contains(item.SellerId));
        }

        var rows = await query
            .OrderByDescending(item => item.UserSellerScore)
            .ThenByDescending(item => item.PurchaseCount)
            .ThenByDescending(item => item.SearchClickCount)
            .ThenByDescending(item => item.ViewCount)
            .ThenByDescending(item => item.LastInteractedAtUtc ?? SqlServerDateTimeFloor)
            .ThenBy(item => item.SellerId)
            .Take(Math.Clamp(limit, 1, 96))
            .Select(item => new UserSellerScoreDto
            {
                UserId = item.UserId,
                SellerId = item.SellerId,
                ViewCount = item.ViewCount,
                SearchClickCount = item.SearchClickCount,
                PurchaseCount = item.PurchaseCount,
                UserSellerScore = item.UserSellerScore,
                LastInteractedAtUtc = item.LastInteractedAtUtc
            })
            .ToArrayAsync(cancellationToken);

        return Ok(rows);
    }

    [HttpGet("user-category")]
    public async Task<IActionResult> GetUserCategoryScores(
        [FromQuery] int? userId,
        [FromQuery] int[]? categoryIds,
        [FromQuery] int limit = 24,
        CancellationToken cancellationToken = default)
    {
        var resolvedUserId = userId.GetValueOrDefault() > 0
            ? userId
            : await TryGetUserIdFromTokenAsync();
        if (!resolvedUserId.HasValue || resolvedUserId.Value <= 0)
        {
            return Ok(Array.Empty<UserCategoryScoreDto>());
        }

        var normalizedCategoryIds = (categoryIds ?? Array.Empty<int>())
            .Where(id => id > 0)
            .Distinct()
            .Take(200)
            .ToArray();

        var query = _db.RecommendationUserCategoryScores
            .AsNoTracking()
            .Where(item => item.UserId == resolvedUserId.Value && item.UserCategoryScore > 0d);

        if (normalizedCategoryIds.Length > 0)
        {
            query = query.Where(item => normalizedCategoryIds.Contains(item.CategoryId));
        }

        var rows = await query
            .OrderByDescending(item => item.UserCategoryScore)
            .ThenByDescending(item => item.PurchaseCount)
            .ThenByDescending(item => item.SearchClickCount)
            .ThenByDescending(item => item.RecommendationClickCount)
            .ThenByDescending(item => item.ViewCount)
            .ThenByDescending(item => item.LastInteractedAtUtc ?? SqlServerDateTimeFloor)
            .ThenBy(item => item.CategoryId)
            .Take(Math.Clamp(limit, 1, 96))
            .Select(item => new UserCategoryScoreDto
            {
                UserId = item.UserId,
                CategoryId = item.CategoryId,
                CategoryName = item.CategoryName,
                ViewCount = item.ViewCount,
                SearchClickCount = item.SearchClickCount,
                RecommendationClickCount = item.RecommendationClickCount,
                PurchaseCount = item.PurchaseCount,
                UserCategoryScore = item.UserCategoryScore,
                LastInteractedAtUtc = item.LastInteractedAtUtc
            })
            .ToArrayAsync(cancellationToken);

        return Ok(rows);
    }

    [HttpGet("basket-affinity")]
    public async Task<IActionResult> GetBasketAffinity(
        [FromQuery] int productId,
        [FromQuery] int[]? candidateProductIds,
        [FromQuery] int limit = 24,
        CancellationToken cancellationToken = default)
    {
        if (productId <= 0)
        {
            return Ok(Array.Empty<BasketAffinityDto>());
        }

        var normalizedCandidateIds = (candidateProductIds ?? Array.Empty<int>())
            .Where(id => id > 0 && id != productId)
            .Distinct()
            .Take(200)
            .ToArray();

        var query = _db.RecommendationBasketAffinities
            .AsNoTracking()
            .Where(item => item.ProductId == productId && item.BasketScore > 0d);

        if (normalizedCandidateIds.Length > 0)
        {
            query = query.Where(item => normalizedCandidateIds.Contains(item.CandidateProductId));
        }

        var rows = await query
            .OrderByDescending(item => item.BasketScore)
            .ThenByDescending(item => item.CoPurchaseOrderCount)
            .ThenBy(item => item.CandidateProductId)
            .Take(Math.Clamp(limit, 1, 96))
            .Select(item => new BasketAffinityDto
            {
                ProductId = item.ProductId,
                CandidateProductId = item.CandidateProductId,
                CoPurchaseOrderCount = item.CoPurchaseOrderCount,
                BasketScore = item.BasketScore
            })
            .ToArrayAsync(cancellationToken);

        return Ok(rows);
    }

    [HttpGet("replenishment-profile")]
    public async Task<IActionResult> GetReplenishmentProfile(
        [FromQuery] int? userId,
        [FromQuery] int[]? productIds,
        [FromQuery] int limit = 24,
        CancellationToken cancellationToken = default)
    {
        var resolvedUserId = userId.GetValueOrDefault() > 0
            ? userId
            : await TryGetUserIdFromTokenAsync();
        if (!resolvedUserId.HasValue || resolvedUserId.Value <= 0)
        {
            return Ok(Array.Empty<ReplenishmentProfileDto>());
        }

        var normalizedProductIds = (productIds ?? Array.Empty<int>())
            .Where(id => id > 0)
            .Distinct()
            .Take(200)
            .ToArray();

        var query = _db.RecommendationReplenishmentProfiles
            .AsNoTracking()
            .Where(item => item.UserId == resolvedUserId.Value && item.ReplenishmentScore > 0d);

        if (normalizedProductIds.Length > 0)
        {
            query = query.Where(item => normalizedProductIds.Contains(item.ProductId));
        }

        var rows = await query
            .OrderByDescending(item => item.ReplenishmentScore)
            .ThenByDescending(item => item.PurchaseCount)
            .ThenByDescending(item => item.LastPurchasedAtUtc)
            .ThenBy(item => item.ProductId)
            .Take(Math.Clamp(limit, 1, 96))
            .Select(item => new ReplenishmentProfileDto
            {
                UserId = item.UserId,
                ProductId = item.ProductId,
                PurchaseCount = item.PurchaseCount,
                LastPurchasedAtUtc = item.LastPurchasedAtUtc,
                AverageRepurchaseDays = item.AverageRepurchaseDays,
                ExpectedReorderAtUtc = item.ExpectedReorderAtUtc,
                ReplenishmentScore = item.ReplenishmentScore
            })
            .ToArrayAsync(cancellationToken);

        return Ok(rows);
    }

    [HttpPost("affinity/rebuild")]
    public async Task<IActionResult> RebuildRecommendationAffinity(CancellationToken cancellationToken = default)
    {
        if (!IsValidInternalServiceRequest())
        {
            return Unauthorized(new { message = "Unauthorized internal recommendation refresh request." });
        }

        if (_recommendationAffinityService is null)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new { message = "Recommendation affinity service is unavailable." });
        }

        var result = await _recommendationAffinityService.RebuildAsync(cancellationToken);
        return Ok(result);
    }

    public sealed class ProductStatsDto
    {
        public int ProductId { get; set; }

        public decimal AverageRating { get; set; }

        public int ReviewCount { get; set; }

        public int RecentSoldCount { get; set; }

        public int SoldCount { get; set; }
    }

    public sealed class ProductSimilarSignalDto
    {
        public int ProductId { get; set; }

        public int CoPurchaseOrderCount { get; set; }

        public int CoViewSessionCount { get; set; }

        public int CoClickSessionCount { get; set; }

        public double CollaborativeScore { get; set; }

        public double SeasonalityScore { get; set; }

        public string? SeasonalityLabel { get; set; }

        public string? SeasonalityBadgeLabel { get; set; }
    }

    public sealed class HomePreferenceSeedDto
    {
        public int ProductId { get; set; }

        public int ViewCount { get; set; }

        public int SearchClickCount { get; set; }

        public int RecommendationClickCount { get; set; }

        public int PurchaseCount { get; set; }

        public double PreferenceScore { get; set; }

        public DateTime? LastInteractedAtUtc { get; set; }

        public double SeasonalityScore { get; set; }

        public string? SeasonalityLabel { get; set; }

        public string? SeasonalityBadgeLabel { get; set; }
    }

    public sealed class HomeCollaborativeCandidateDto
    {
        public int ProductId { get; set; }

        public int CoPurchaseOrderCount { get; set; }

        public int CoViewSessionCount { get; set; }

        public int CoClickSessionCount { get; set; }

        public double CollaborativeScore { get; set; }

        public double SeasonalityScore { get; set; }

        public string? SeasonalityLabel { get; set; }

        public string? SeasonalityBadgeLabel { get; set; }
    }

    public sealed class SearchRankingSignalDto
    {
        public int ProductId { get; set; }

        public int SearchClickCount { get; set; }

        public int SearchClickSessionCount { get; set; }

        public int SearchViewSessionCount { get; set; }

        public int SearchRecommendationClickCount { get; set; }

        public double HybridSearchScore { get; set; }

        public double SeasonalityScore { get; set; }

        public string? SeasonalityLabel { get; set; }

        public string? SeasonalityBadgeLabel { get; set; }
    }

    public sealed class UserProductScoreDto
    {
        public int UserId { get; set; }

        public int ProductId { get; set; }

        public int ViewCount { get; set; }

        public int SearchClickCount { get; set; }

        public int RecommendationClickCount { get; set; }

        public int PurchaseCount { get; set; }

        public double UserProductScore { get; set; }

        public DateTime? LastInteractedAtUtc { get; set; }

        public DateTime ComputedAtUtc { get; set; }
    }

    public sealed class BasketAffinityDto
    {
        public int ProductId { get; set; }

        public int CandidateProductId { get; set; }

        public int CoPurchaseOrderCount { get; set; }

        public double BasketScore { get; set; }
    }

    public sealed class ReplenishmentProfileDto
    {
        public int UserId { get; set; }

        public int ProductId { get; set; }

        public int PurchaseCount { get; set; }

        public DateTime LastPurchasedAtUtc { get; set; }

        public double AverageRepurchaseDays { get; set; }

        public DateTime? ExpectedReorderAtUtc { get; set; }

        public double ReplenishmentScore { get; set; }
    }

    public sealed class UserSellerScoreDto
    {
        public int UserId { get; set; }

        public int SellerId { get; set; }

        public int ViewCount { get; set; }

        public int SearchClickCount { get; set; }

        public int PurchaseCount { get; set; }

        public double UserSellerScore { get; set; }

        public DateTime? LastInteractedAtUtc { get; set; }
    }

    public sealed class UserCategoryScoreDto
    {
        public int UserId { get; set; }

        public int CategoryId { get; set; }

        public string CategoryName { get; set; } = string.Empty;

        public int ViewCount { get; set; }

        public int SearchClickCount { get; set; }

        public int RecommendationClickCount { get; set; }

        public int PurchaseCount { get; set; }

        public double UserCategoryScore { get; set; }

        public DateTime? LastInteractedAtUtc { get; set; }
    }

    private async Task<int?> TryGetUserIdFromTokenAsync()
    {
        var httpContext = ControllerContext.HttpContext;
        if (httpContext is null)
        {
            return null;
        }

        var principal = User;
        if (principal?.Identity?.IsAuthenticated != true)
        {
            var authService = httpContext.RequestServices?.GetService<IAuthenticationService>();
            if (authService is not null)
            {
                var authResult = await httpContext.AuthenticateAsync(JwtBearerDefaults.AuthenticationScheme);
                if (authResult.Succeeded && authResult.Principal is not null)
                {
                    principal = authResult.Principal;
                }
            }
        }

        if (principal?.Identity is null)
        {
            return null;
        }

        var subject = principal.FindFirstValue(JwtRegisteredClaimNames.Sub)
                      ?? principal.FindFirstValue(ClaimTypes.NameIdentifier)
                      ?? principal.FindFirstValue("sub");

        return int.TryParse(subject, out var userId) && userId > 0
            ? userId
            : null;
    }

    private static string? NormalizeOptionalText(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        if (normalized.Length == 0)
        {
            return null;
        }

        return normalized.Length <= maxLength
            ? normalized
            : normalized[..maxLength];
    }

    private static DateTime? MaxUtc(DateTime? current, DateTime candidate)
        => !current.HasValue || candidate > current.Value
            ? candidate
            : current;

    private async Task<HomePreferenceSeedDto[]> BuildHomePreferenceSeedsAsync(
        string? normalizedSessionId,
        int? userId,
        int limit,
        CancellationToken cancellationToken)
    {
        var useSessionScope = !string.IsNullOrWhiteSpace(normalizedSessionId);
        var useUserScope = userId.HasValue;
        if (!useSessionScope && !useUserScope)
        {
            return Array.Empty<HomePreferenceSeedDto>();
        }

        var materializedSignals = await BuildMaterializedHomePreferenceSeedsAsync(
            normalizedSessionId,
            userId,
            Math.Clamp(limit, 1, 48),
            cancellationToken);
        if (materializedSignals.Length > 0)
        {
            SetRecommendationMetadata(MaterializedProfileSignalSource);
            return materializedSignals;
        }

        var lookbackFromUtc = DateTime.UtcNow.AddDays(-HomePreferenceLookbackDays);
        var signalsByProductId = new Dictionary<int, HomePreferenceSeedDto>();

        void UpdateSignal(int productId, Action<HomePreferenceSeedDto> apply)
        {
            if (productId <= 0)
            {
                return;
            }

            if (!signalsByProductId.TryGetValue(productId, out var signal))
            {
                signal = new HomePreferenceSeedDto
                {
                    ProductId = productId
                };
                signalsByProductId[productId] = signal;
            }

            apply(signal);
        }

        var viewSignals = await _db.ProductViewEvents
            .AsNoTracking()
            .Where(view =>
                view.CreatedAt >= lookbackFromUtc
                && view.ProductId > 0
                && ((useSessionScope && view.SessionId == normalizedSessionId)
                    || (useUserScope && view.UserId == userId)))
            .GroupBy(view => view.ProductId)
            .Select(grouped => new
            {
                ProductId = grouped.Key,
                ViewCount = grouped.Count(),
                LastInteractedAtUtc = grouped.Max(item => item.CreatedAt)
            })
            .ToListAsync(cancellationToken);

        foreach (var signal in viewSignals)
        {
            UpdateSignal(signal.ProductId, item =>
            {
                item.ViewCount = signal.ViewCount;
                item.LastInteractedAtUtc = MaxUtc(item.LastInteractedAtUtc, signal.LastInteractedAtUtc);
            });
        }

        var searchClickSignals = await _db.SearchClickEvents
            .AsNoTracking()
            .Where(click =>
                click.CreatedAt >= lookbackFromUtc
                && click.ProductId > 0
                && ((useSessionScope && click.SessionId == normalizedSessionId)
                    || (useUserScope && click.UserId == userId)))
            .GroupBy(click => click.ProductId)
            .Select(grouped => new
            {
                ProductId = grouped.Key,
                SearchClickCount = grouped.Count(),
                LastInteractedAtUtc = grouped.Max(item => item.CreatedAt)
            })
            .ToListAsync(cancellationToken);

        foreach (var signal in searchClickSignals)
        {
            UpdateSignal(signal.ProductId, item =>
            {
                item.SearchClickCount = signal.SearchClickCount;
                item.LastInteractedAtUtc = MaxUtc(item.LastInteractedAtUtc, signal.LastInteractedAtUtc);
            });
        }

        var recommendationClickSignals = await _db.RecommendationClickEvents
            .AsNoTracking()
            .Where(click =>
                click.CreatedAt >= lookbackFromUtc
                && click.ProductId > 0
                && ((useSessionScope && click.SessionId == normalizedSessionId)
                    || (useUserScope && click.UserId == userId)))
            .GroupBy(click => click.ProductId)
            .Select(grouped => new
            {
                ProductId = grouped.Key,
                RecommendationClickCount = grouped.Count(),
                LastInteractedAtUtc = grouped.Max(item => item.CreatedAt)
            })
            .ToListAsync(cancellationToken);

        foreach (var signal in recommendationClickSignals)
        {
            UpdateSignal(signal.ProductId, item =>
            {
                item.RecommendationClickCount = signal.RecommendationClickCount;
                item.LastInteractedAtUtc = MaxUtc(item.LastInteractedAtUtc, signal.LastInteractedAtUtc);
            });
        }

        if (useUserScope)
        {
            var purchaseSignals = await (
                    from order in _db.Orders.AsNoTracking()
                    join detail in _db.OrderDetails.AsNoTracking() on order.OrderId equals detail.OrderId
                    where order.UserId == userId
                        && detail.ProductId > 0
                        && order.OrderDate >= lookbackFromUtc
                        && SuccessfulOrderStatuses.Contains((order.Status ?? string.Empty).Trim().ToLower())
                    group new { order, detail } by detail.ProductId
                    into grouped
                    select new
                    {
                        ProductId = grouped.Key,
                        PurchaseCount = grouped.Sum(item => item.detail.Quantity),
                        LastInteractedAtUtc = grouped.Max(item => item.order.OrderDate)
                    })
                .ToListAsync(cancellationToken);

            foreach (var signal in purchaseSignals)
            {
                UpdateSignal(signal.ProductId, item =>
                {
                    item.PurchaseCount = signal.PurchaseCount;
                    item.LastInteractedAtUtc = MaxUtc(item.LastInteractedAtUtc, signal.LastInteractedAtUtc);
                });
            }
        }

        foreach (var signal in signalsByProductId.Values)
        {
            signal.PreferenceScore =
                (signal.ViewCount * 10d)
                + (signal.SearchClickCount * 28d)
                + (signal.RecommendationClickCount * 22d)
                + (signal.PurchaseCount * 35d);
        }

        var rankedSignals = signalsByProductId.Values
            .Where(signal => signal.PreferenceScore > 0d)
            .OrderByDescending(signal => signal.PreferenceScore)
            .ThenByDescending(signal => signal.PurchaseCount)
            .ThenByDescending(signal => signal.SearchClickCount)
            .ThenByDescending(signal => signal.RecommendationClickCount)
            .ThenByDescending(signal => signal.ViewCount)
            .ThenByDescending(signal => signal.LastInteractedAtUtc ?? SqlServerDateTimeFloor)
            .Take(Math.Clamp(limit, 1, 48))
            .ToArray();

        if (rankedSignals.Length == 0)
        {
            SetRecommendationFallbackReason(NoTrackedPreferenceInteractionsFallbackReason);
            return Array.Empty<HomePreferenceSeedDto>();
        }

        RequestRecommendationAffinityRefresh();
        SetRecommendationMetadata(AdHocProfileSignalSource, NoMaterializedProfileSeedFallbackReason);
        return rankedSignals;
    }

    private async Task<HomePreferenceSeedDto[]> BuildMaterializedHomePreferenceSeedsAsync(
        string? normalizedSessionId,
        int? userId,
        int limit,
        CancellationToken cancellationToken)
    {
        var normalizedLimit = Math.Clamp(limit, 1, 48);
        var useSessionScope = !string.IsNullOrWhiteSpace(normalizedSessionId);
        var useUserScope = userId.HasValue && userId.Value > 0;
        if (!useSessionScope && !useUserScope)
        {
            return Array.Empty<HomePreferenceSeedDto>();
        }

        var rows = new List<RecommendationHomePreferenceSeed>();
        if (useSessionScope)
        {
            rows.AddRange(await _db.RecommendationHomePreferenceSeeds
                .AsNoTracking()
                .Where(item =>
                    item.ScopeType == "session"
                    && item.ScopeKey == normalizedSessionId
                    && item.PreferenceScore > 0d)
                .ToListAsync(cancellationToken));
        }

        if (useUserScope)
        {
            rows.AddRange(await _db.RecommendationHomePreferenceSeeds
                .AsNoTracking()
                .Where(item =>
                    item.ScopeType == "user"
                    && item.ScopeKey == userId!.Value.ToString()
                    && item.PreferenceScore > 0d)
                .ToListAsync(cancellationToken));
        }

        if (rows.Count == 0)
        {
            return Array.Empty<HomePreferenceSeedDto>();
        }

        var mergedByProductId = new Dictionary<int, HomePreferenceSeedDto>();
        foreach (var row in rows)
        {
            if (!mergedByProductId.TryGetValue(row.ProductId, out var seed))
            {
                seed = new HomePreferenceSeedDto
                {
                    ProductId = row.ProductId
                };
                mergedByProductId[row.ProductId] = seed;
            }

            seed.ViewCount += row.ViewCount;
            seed.SearchClickCount += row.SearchClickCount;
            seed.RecommendationClickCount += row.RecommendationClickCount;
            seed.PurchaseCount += row.PurchaseCount;
            if (row.LastInteractedAtUtc.HasValue)
            {
                seed.LastInteractedAtUtc = MaxUtc(seed.LastInteractedAtUtc, row.LastInteractedAtUtc.Value);
            }
        }

        foreach (var seed in mergedByProductId.Values)
        {
            seed.PreferenceScore =
                (seed.ViewCount * 10d)
                + (seed.SearchClickCount * 28d)
                + (seed.RecommendationClickCount * 22d)
                + (seed.PurchaseCount * 35d);
        }

        return mergedByProductId.Values
            .Where(seed => seed.PreferenceScore > 0d)
            .OrderByDescending(seed => seed.PreferenceScore)
            .ThenByDescending(seed => seed.PurchaseCount)
            .ThenByDescending(seed => seed.SearchClickCount)
            .ThenByDescending(seed => seed.RecommendationClickCount)
            .ThenByDescending(seed => seed.ViewCount)
            .ThenByDescending(seed => seed.LastInteractedAtUtc ?? SqlServerDateTimeFloor)
            .ThenBy(seed => seed.ProductId)
            .Take(normalizedLimit)
            .ToArray();
    }

    private async Task<HomeCollaborativeCandidateDto[]> BuildMaterializedHomeCollaborativeAsync(
        IReadOnlyCollection<HomePreferenceSeedDto> seedSignals,
        ISet<int> seedProductIds,
        string? normalizedSessionId,
        int? userId,
        int limit,
        CancellationToken cancellationToken)
    {
        var normalizedLimit = Math.Clamp(limit, 1, 48);
        var useSessionScope = !string.IsNullOrWhiteSpace(normalizedSessionId);
        var useUserScope = userId.HasValue && userId.Value > 0;
        if (!useSessionScope && !useUserScope)
        {
            return Array.Empty<HomeCollaborativeCandidateDto>();
        }

        var candidateRows = new List<RecommendationHomeCollaborativeCandidate>();
        if (useSessionScope)
        {
            candidateRows.AddRange(await _db.RecommendationHomeCollaborativeCandidates
                .AsNoTracking()
                .Where(item =>
                    item.ScopeType == "session"
                    && item.ScopeKey == normalizedSessionId
                    && !seedProductIds.Contains(item.ProductId)
                    && item.CollaborativeScore > 0d)
                .ToListAsync(cancellationToken));
        }

        if (useUserScope)
        {
            var normalizedUserId = userId!.Value.ToString();
            candidateRows.AddRange(await _db.RecommendationHomeCollaborativeCandidates
                .AsNoTracking()
                .Where(item =>
                    item.ScopeType == "user"
                    && item.ScopeKey == normalizedUserId
                    && !seedProductIds.Contains(item.ProductId)
                    && item.CollaborativeScore > 0d)
                .ToListAsync(cancellationToken));
        }

        if (candidateRows.Count == 0)
        {
            return Array.Empty<HomeCollaborativeCandidateDto>();
        }

        var candidatesByProductId = new Dictionary<int, HomeCollaborativeCandidateDto>();

        foreach (var row in candidateRows)
        {
            if (!candidatesByProductId.TryGetValue(row.ProductId, out var candidate))
            {
                candidate = new HomeCollaborativeCandidateDto
                {
                    ProductId = row.ProductId
                };
                candidatesByProductId[row.ProductId] = candidate;
            }

            candidate.CoPurchaseOrderCount += row.CoPurchaseOrderCount;
            candidate.CoViewSessionCount += row.CoViewSessionCount;
            candidate.CoClickSessionCount += row.CoClickSessionCount;
            candidate.CollaborativeScore += row.CollaborativeScore;
        }

        return candidatesByProductId.Values
            .Where(candidate => candidate.CollaborativeScore > 0d)
            .OrderByDescending(candidate => candidate.CollaborativeScore)
            .ThenByDescending(candidate => candidate.CoPurchaseOrderCount)
            .ThenByDescending(candidate => candidate.CoClickSessionCount)
            .ThenByDescending(candidate => candidate.CoViewSessionCount)
            .ThenBy(candidate => candidate.ProductId)
            .Take(normalizedLimit)
            .ToArray();
    }

    private async Task<ProductSimilarSignalDto[]> BuildMaterializedSimilarSignalsAsync(
        int productId,
        IReadOnlyCollection<int> candidateProductIds,
        int limit,
        CancellationToken cancellationToken)
    {
        var normalizedLimit = Math.Clamp(limit, 1, 48);
        var affinityRows = await _db.RecommendationProductAffinities
            .AsNoTracking()
            .Where(item =>
                item.SeedProductId == productId
                && candidateProductIds.Contains(item.CandidateProductId)
                && item.AffinityScore > 0d)
            .OrderByDescending(item => item.AffinityScore)
            .ThenByDescending(item => item.CoPurchaseOrderCount)
            .ThenByDescending(item => item.CoClickSessionCount)
            .ThenByDescending(item => item.CoViewSessionCount)
            .ThenBy(item => item.CandidateProductId)
            .Take(normalizedLimit)
            .Select(item => new ProductSimilarSignalDto
            {
                ProductId = item.CandidateProductId,
                CoPurchaseOrderCount = item.CoPurchaseOrderCount,
                CoViewSessionCount = item.CoViewSessionCount,
                CoClickSessionCount = item.CoClickSessionCount,
                CollaborativeScore = item.AffinityScore
            })
            .ToArrayAsync(cancellationToken);

        return affinityRows;
    }

    private async Task<SearchRankingSignalDto[]> BuildMaterializedSearchRankingSignalsAsync(
        string normalizedKeyword,
        IReadOnlyCollection<int> productIds,
        int limit,
        CancellationToken cancellationToken)
    {
        var normalizedLimit = Math.Clamp(limit, 1, 96);
        return await _db.RecommendationSearchKeywordAffinities
            .AsNoTracking()
            .Where(item =>
                item.Keyword == normalizedKeyword
                && productIds.Contains(item.ProductId)
                && item.HybridSearchScore > 0d)
            .OrderByDescending(item => item.HybridSearchScore)
            .ThenByDescending(item => item.SearchClickCount)
            .ThenByDescending(item => item.SearchRecommendationClickCount)
            .ThenByDescending(item => item.SearchViewSessionCount)
            .ThenBy(item => item.ProductId)
            .Take(normalizedLimit)
            .Select(item => new SearchRankingSignalDto
            {
                ProductId = item.ProductId,
                SearchClickCount = item.SearchClickCount,
                SearchClickSessionCount = item.SearchClickSessionCount,
                SearchViewSessionCount = item.SearchViewSessionCount,
                SearchRecommendationClickCount = item.SearchRecommendationClickCount,
                HybridSearchScore = item.HybridSearchScore
            })
            .ToArrayAsync(cancellationToken);
    }

    private async Task<HomePreferenceSeedDto[]> ApplySeasonalityBoostAsync(
        HomePreferenceSeedDto[] items,
        double boostMultiplier,
        int limit,
        CancellationToken cancellationToken)
    {
        var seasonalitySignals = await GetSeasonalitySignalsAsync(items.Select(item => item.ProductId), cancellationToken);
        if (seasonalitySignals.Count == 0)
        {
            return items;
        }

        foreach (var item in items)
        {
            if (!seasonalitySignals.TryGetValue(item.ProductId, out var signal))
            {
                continue;
            }

            item.SeasonalityScore = signal.Score;
            item.SeasonalityLabel = signal.SeasonLabel;
            item.SeasonalityBadgeLabel = signal.BadgeLabel;
            item.PreferenceScore += signal.Score * boostMultiplier;
        }

        return items
            .OrderByDescending(item => item.PreferenceScore)
            .ThenByDescending(item => item.PurchaseCount)
            .ThenByDescending(item => item.SearchClickCount)
            .ThenByDescending(item => item.RecommendationClickCount)
            .ThenByDescending(item => item.ViewCount)
            .ThenByDescending(item => item.LastInteractedAtUtc ?? SqlServerDateTimeFloor)
            .ThenBy(item => item.ProductId)
            .Take(Math.Clamp(limit, 1, 48))
            .ToArray();
    }

    private async Task<HomeCollaborativeCandidateDto[]> ApplySeasonalityBoostAsync(
        HomeCollaborativeCandidateDto[] items,
        double boostMultiplier,
        int limit,
        CancellationToken cancellationToken)
    {
        var seasonalitySignals = await GetSeasonalitySignalsAsync(items.Select(item => item.ProductId), cancellationToken);
        if (seasonalitySignals.Count == 0)
        {
            return items;
        }

        foreach (var item in items)
        {
            if (!seasonalitySignals.TryGetValue(item.ProductId, out var signal))
            {
                continue;
            }

            item.SeasonalityScore = signal.Score;
            item.SeasonalityLabel = signal.SeasonLabel;
            item.SeasonalityBadgeLabel = signal.BadgeLabel;
            item.CollaborativeScore += signal.Score * boostMultiplier;
        }

        return items
            .OrderByDescending(item => item.CollaborativeScore)
            .ThenByDescending(item => item.CoPurchaseOrderCount)
            .ThenByDescending(item => item.CoClickSessionCount)
            .ThenByDescending(item => item.CoViewSessionCount)
            .ThenBy(item => item.ProductId)
            .Take(Math.Clamp(limit, 1, 48))
            .ToArray();
    }

    private async Task<SearchRankingSignalDto[]> ApplySeasonalityBoostAsync(
        SearchRankingSignalDto[] items,
        double boostMultiplier,
        int limit,
        CancellationToken cancellationToken)
    {
        var seasonalitySignals = await GetSeasonalitySignalsAsync(items.Select(item => item.ProductId), cancellationToken);
        if (seasonalitySignals.Count == 0)
        {
            return items;
        }

        foreach (var item in items)
        {
            if (!seasonalitySignals.TryGetValue(item.ProductId, out var signal))
            {
                continue;
            }

            item.SeasonalityScore = signal.Score;
            item.SeasonalityLabel = signal.SeasonLabel;
            item.SeasonalityBadgeLabel = signal.BadgeLabel;
            item.HybridSearchScore += signal.Score * boostMultiplier;
        }

        return items
            .OrderByDescending(item => item.HybridSearchScore)
            .ThenByDescending(item => item.SearchClickCount)
            .ThenByDescending(item => item.SearchRecommendationClickCount)
            .ThenByDescending(item => item.SearchViewSessionCount)
            .ThenBy(item => item.ProductId)
            .Take(Math.Clamp(limit, 1, 96))
            .ToArray();
    }

    private async Task<IReadOnlyDictionary<int, ProductSeasonalitySignal>> GetSeasonalitySignalsAsync(
        IEnumerable<int> productIds,
        CancellationToken cancellationToken)
    {
        var normalizedProductIds = productIds
            .Where(id => id > 0)
            .Distinct()
            .Take(200)
            .ToArray();

        if (_catalogInventoryClient is null || normalizedProductIds.Length == 0)
        {
            return new Dictionary<int, ProductSeasonalitySignal>();
        }

        try
        {
            var rows = await _catalogInventoryClient.GetProductSeasonalityAsync(normalizedProductIds, cancellationToken);
            return SeasonalityScoring.BuildBestSignals(
                normalizedProductIds,
                rows,
                SeasonalityScoring.GetVietnamCurrentMonth());
        }
        catch
        {
            return new Dictionary<int, ProductSeasonalitySignal>();
        }
    }

    private bool IsValidInternalServiceRequest()
    {
        var configuredKey = _internalServiceAuthOptions?.InternalServiceKey?.Trim();
        var incomingKey = Request.Headers["X-Internal-Service-Key"].ToString().Trim();

        if (string.IsNullOrWhiteSpace(configuredKey) || string.IsNullOrWhiteSpace(incomingKey))
        {
            return false;
        }

        var configuredBytes = Encoding.UTF8.GetBytes(configuredKey);
        var incomingBytes = Encoding.UTF8.GetBytes(incomingKey);
        return CryptographicOperations.FixedTimeEquals(configuredBytes, incomingBytes);
    }

    private void SetRecommendationMetadata(string source, string? fallbackReason = null)
    {
        if (ControllerContext.HttpContext is null)
        {
            return;
        }

        Response.Headers[RecommendationSignalSourceHeader] = source;
        if (string.IsNullOrWhiteSpace(fallbackReason))
        {
            Response.Headers.Remove(RecommendationFallbackReasonHeader);
        }
        else
        {
            Response.Headers[RecommendationFallbackReasonHeader] = fallbackReason.Trim();
        }
    }

    private void SetRecommendationFallbackReason(string fallbackReason)
    {
        if (ControllerContext.HttpContext is null)
        {
            return;
        }

        Response.Headers.Remove(RecommendationSignalSourceHeader);
        Response.Headers[RecommendationFallbackReasonHeader] = fallbackReason.Trim();
    }

    private void SetRecommendationSignalSource(string source)
    {
        SetRecommendationMetadata(source);
    }

    private void RequestRecommendationAffinityRefresh()
    {
        _recommendationAffinityRefreshSignal?.RequestRefresh();
    }
}
