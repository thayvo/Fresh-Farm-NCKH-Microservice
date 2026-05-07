// Nguon goc: src\Services\Ordering\FreshFarm.Ordering.Api\Services\RecommendationAffinityService.cs
// Duoc sao chep tu: D:\NCKH\DOAN\NCKH-FRESH-FARM\src\Services\Ordering\FreshFarm.Ordering.Api\Services\RecommendationAffinityService.cs
// Thu muc hoc tap: HocGoiY

using FreshFarm.Ordering.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace FreshFarm.Ordering.Api.Services;

public sealed class RecommendationAffinityService
{
    private static readonly string[] SuccessfulOrderStatuses = ["delivered", "completed"];
    private static readonly TimeSpan LookbackWindow = TimeSpan.FromDays(180);
    private static readonly TimeSpan HomePreferenceLookbackWindow = TimeSpan.FromDays(90);
    private static readonly TimeSpan ReplenishmentLookbackWindow = TimeSpan.FromDays(365);
    private const int MaxAffinitiesPerSeed = 48;
    private const int MaxKeywordAffinitiesPerKeyword = 48;
    private const int MaxHomePreferenceSeedsPerScope = 48;
    private const int MaxHomeCollaborativeCandidatesPerScope = 48;
    private const int MaxUserProductScoresPerUser = 96;
    private const int MaxUserCategoryScoresPerUser = 24;
    private const int MaxUserSellerScoresPerUser = 32;
    private const int MaxBasketAffinitiesPerProduct = 48;
    private const int MaxReplenishmentProfilesPerUser = 48;

    private readonly FreshFarmOrderingDBContext _db;
    private readonly CatalogInventoryClient _catalogInventoryClient;
    private readonly ILogger<RecommendationAffinityService> _logger;

    private sealed record SearchSessionKeyword(string SessionId, string Keyword);
    private sealed record SessionProductPair(string SessionId, int ProductId);

    public RecommendationAffinityService(
        FreshFarmOrderingDBContext db,
        CatalogInventoryClient catalogInventoryClient,
        ILogger<RecommendationAffinityService> logger)
    {
        _db = db;
        _catalogInventoryClient = catalogInventoryClient;
        _logger = logger;
    }

    public async Task<RecommendationAffinityRefreshResult> RebuildAsync(CancellationToken cancellationToken)
    {
        var lookbackFromUtc = DateTime.UtcNow.Subtract(LookbackWindow);
        var affinityByPair = new Dictionary<(int SeedProductId, int CandidateProductId), RecommendationProductAffinity>(capacity: 2048);

        void AddSignal(int seedProductId, int candidateProductId, Action<RecommendationProductAffinity> apply)
        {
            if (seedProductId <= 0 || candidateProductId <= 0 || seedProductId == candidateProductId)
            {
                return;
            }

            var key = (seedProductId, candidateProductId);
            if (!affinityByPair.TryGetValue(key, out var affinity))
            {
                affinity = new RecommendationProductAffinity
                {
                    SeedProductId = seedProductId,
                    CandidateProductId = candidateProductId
                };
                affinityByPair[key] = affinity;
            }

            apply(affinity);
        }

        var searchEvents = await _db.SearchEvents
            .AsNoTracking()
            .Where(item =>
                item.CreatedAt >= lookbackFromUtc
                && item.SessionId != string.Empty
                && item.Keyword != string.Empty)
            .Select(item => new SearchSessionKeyword(item.SessionId, item.Keyword))
            .ToListAsync(cancellationToken);

        var searchClickPairs = await _db.SearchClickEvents
            .AsNoTracking()
            .Where(item => item.CreatedAt >= lookbackFromUtc && item.ProductId > 0 && item.SessionId != string.Empty)
            .Select(item => new SessionProductPair(item.SessionId, item.ProductId))
            .ToListAsync(cancellationToken);

        var recommendationClickPairs = await _db.RecommendationClickEvents
            .AsNoTracking()
            .Where(item => item.CreatedAt >= lookbackFromUtc && item.ProductId > 0 && item.SessionId != string.Empty)
            .Select(item => new SessionProductPair(item.SessionId, item.ProductId))
            .ToListAsync(cancellationToken);

        var productViewPairs = await _db.ProductViewEvents
            .AsNoTracking()
            .Where(item => item.CreatedAt >= lookbackFromUtc && item.ProductId > 0 && item.SessionId != string.Empty)
            .Select(item => new SessionProductPair(item.SessionId, item.ProductId))
            .ToListAsync(cancellationToken);

        var coPurchaseGroups = await (
                from detail in _db.OrderDetails.AsNoTracking()
                join order in _db.Orders.AsNoTracking() on detail.OrderId equals order.OrderId
                where detail.ProductId > 0
                      && order.OrderDate >= lookbackFromUtc
                      && SuccessfulOrderStatuses.Contains((order.Status ?? string.Empty).Trim().ToLower())
                group detail by detail.OrderId
                into grouped
                select grouped.Select(item => item.ProductId).Distinct().ToArray())
            .ToListAsync(cancellationToken);

        foreach (var productIds in coPurchaseGroups)
        {
            for (var i = 0; i < productIds.Length; i++)
            {
                for (var j = 0; j < productIds.Length; j++)
                {
                    if (i == j)
                    {
                        continue;
                    }

                    AddSignal(productIds[i], productIds[j], affinity => affinity.CoPurchaseOrderCount++);
                }
            }
        }

        var coViewGroups = await _db.ProductViewEvents
            .AsNoTracking()
            .Where(item => item.CreatedAt >= lookbackFromUtc && item.ProductId > 0 && item.SessionId != string.Empty)
            .GroupBy(item => item.SessionId)
            .Select(grouped => grouped.Select(item => item.ProductId).Distinct().ToArray())
            .ToListAsync(cancellationToken);

        foreach (var productIds in coViewGroups)
        {
            for (var i = 0; i < productIds.Length; i++)
            {
                for (var j = 0; j < productIds.Length; j++)
                {
                    if (i == j)
                    {
                        continue;
                    }

                    AddSignal(productIds[i], productIds[j], affinity => affinity.CoViewSessionCount++);
                }
            }
        }

        var coClickGroups = searchClickPairs
            .Concat(recommendationClickPairs)
            .GroupBy(item => item.SessionId, StringComparer.Ordinal)
            .Select(grouped => grouped.Select(item => item.ProductId).Distinct().ToArray())
            .ToList();

        foreach (var productIds in coClickGroups)
        {
            for (var i = 0; i < productIds.Length; i++)
            {
                for (var j = 0; j < productIds.Length; j++)
                {
                    if (i == j)
                    {
                        continue;
                    }

                    AddSignal(productIds[i], productIds[j], affinity => affinity.CoClickSessionCount++);
                }
            }
        }

        var computedAt = DateTime.UtcNow;
        var materializedRows = affinityByPair.Values
            .Select(affinity =>
            {
                affinity.AffinityScore =
                    (affinity.CoPurchaseOrderCount * 32d)
                    + (affinity.CoClickSessionCount * 14d)
                    + (affinity.CoViewSessionCount * 6d);
                affinity.ComputedAt = computedAt;
                return affinity;
            })
            .Where(affinity => affinity.AffinityScore > 0d)
            .GroupBy(affinity => affinity.SeedProductId)
            .SelectMany(group => group
                .OrderByDescending(item => item.AffinityScore)
                .ThenByDescending(item => item.CoPurchaseOrderCount)
                .ThenByDescending(item => item.CoClickSessionCount)
                .ThenByDescending(item => item.CoViewSessionCount)
                .ThenBy(item => item.CandidateProductId)
                .Take(MaxAffinitiesPerSeed))
            .ToList();

        var keywordAffinities = BuildKeywordAffinities(
            searchEvents,
            searchClickPairs,
            recommendationClickPairs,
            productViewPairs,
            computedAt);
        var userProductScores = await BuildUserProductScoresAsync(
            DateTime.UtcNow.Subtract(ReplenishmentLookbackWindow),
            computedAt,
            cancellationToken);
        var userCategoryScores = await BuildUserCategoryScoresAsync(
            userProductScores,
            computedAt,
            cancellationToken);
        var userSellerScores = await BuildUserSellerScoresAsync(
            DateTime.UtcNow.Subtract(ReplenishmentLookbackWindow),
            computedAt,
            cancellationToken);
        var basketAffinities = BuildBasketAffinities(materializedRows, computedAt);
        var replenishmentProfiles = await BuildReplenishmentProfilesAsync(
            DateTime.UtcNow.Subtract(ReplenishmentLookbackWindow),
            computedAt,
            cancellationToken);
        var homePreferenceSeeds = await BuildHomePreferenceSeedsAsync(
            DateTime.UtcNow.Subtract(HomePreferenceLookbackWindow),
            computedAt,
            cancellationToken);
        var homeCollaborativeCandidates = BuildHomeCollaborativeCandidates(
            homePreferenceSeeds,
            materializedRows,
            computedAt);

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        _db.RecommendationProductAffinities.RemoveRange(_db.RecommendationProductAffinities);
        _db.RecommendationSearchKeywordAffinities.RemoveRange(_db.RecommendationSearchKeywordAffinities);
        _db.RecommendationUserProductScores.RemoveRange(_db.RecommendationUserProductScores);
        _db.RecommendationUserCategoryScores.RemoveRange(_db.RecommendationUserCategoryScores);
        _db.RecommendationUserSellerScores.RemoveRange(_db.RecommendationUserSellerScores);
        _db.RecommendationBasketAffinities.RemoveRange(_db.RecommendationBasketAffinities);
        _db.RecommendationReplenishmentProfiles.RemoveRange(_db.RecommendationReplenishmentProfiles);
        _db.RecommendationHomePreferenceSeeds.RemoveRange(_db.RecommendationHomePreferenceSeeds);
        _db.RecommendationHomeCollaborativeCandidates.RemoveRange(_db.RecommendationHomeCollaborativeCandidates);
        await _db.SaveChangesAsync(cancellationToken);
        await _db.RecommendationProductAffinities.AddRangeAsync(materializedRows, cancellationToken);
        await _db.RecommendationSearchKeywordAffinities.AddRangeAsync(keywordAffinities, cancellationToken);
        await _db.RecommendationUserProductScores.AddRangeAsync(userProductScores, cancellationToken);
        await _db.RecommendationUserCategoryScores.AddRangeAsync(userCategoryScores, cancellationToken);
        await _db.RecommendationUserSellerScores.AddRangeAsync(userSellerScores, cancellationToken);
        await _db.RecommendationBasketAffinities.AddRangeAsync(basketAffinities, cancellationToken);
        await _db.RecommendationReplenishmentProfiles.AddRangeAsync(replenishmentProfiles, cancellationToken);
        await _db.RecommendationHomePreferenceSeeds.AddRangeAsync(homePreferenceSeeds, cancellationToken);
        await _db.RecommendationHomeCollaborativeCandidates.AddRangeAsync(homeCollaborativeCandidates, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var result = new RecommendationAffinityRefreshResult
        {
            ComputedAtUtc = computedAt,
            MaterializedRowCount = materializedRows.Count,
            DistinctSeedProductCount = materializedRows.Select(item => item.SeedProductId).Distinct().Count(),
            MaterializedKeywordRowCount = keywordAffinities.Count,
            DistinctKeywordCount = keywordAffinities.Select(item => item.Keyword).Distinct(StringComparer.Ordinal).Count(),
            MaterializedUserProductScoreRowCount = userProductScores.Count,
            DistinctUserProductScoreUserCount = userProductScores.Select(item => item.UserId).Distinct().Count(),
            MaterializedUserCategoryScoreRowCount = userCategoryScores.Count,
            DistinctUserCategoryScoreUserCount = userCategoryScores.Select(item => item.UserId).Distinct().Count(),
            MaterializedUserSellerScoreRowCount = userSellerScores.Count,
            DistinctUserSellerScoreUserCount = userSellerScores.Select(item => item.UserId).Distinct().Count(),
            MaterializedBasketAffinityRowCount = basketAffinities.Count,
            DistinctBasketAffinityProductCount = basketAffinities.Select(item => item.ProductId).Distinct().Count(),
            MaterializedReplenishmentProfileRowCount = replenishmentProfiles.Count,
            DistinctReplenishmentUserCount = replenishmentProfiles.Select(item => item.UserId).Distinct().Count(),
            MaterializedHomePreferenceRowCount = homePreferenceSeeds.Count,
            DistinctHomePreferenceScopeCount = homePreferenceSeeds
                .Select(item => $"{item.ScopeType}:{item.ScopeKey}")
                .Distinct(StringComparer.Ordinal)
                .Count(),
            MaterializedHomeCollaborativeRowCount = homeCollaborativeCandidates.Count,
            DistinctHomeCollaborativeScopeCount = homeCollaborativeCandidates
                .Select(item => $"{item.ScopeType}:{item.ScopeKey}")
                .Distinct(StringComparer.Ordinal)
                .Count()
        };

        _logger.LogInformation(
            "Da rebuild recommendation affinity. Seeds={SeedCount}, PairRows={RowCount}, Keywords={KeywordCount}, KeywordRows={KeywordRowCount}, UserScoreUsers={UserScoreUsers}, UserScoreRows={UserScoreRows}, UserCategoryUsers={UserCategoryUsers}, UserCategoryRows={UserCategoryRows}, UserSellerUsers={UserSellerUsers}, UserSellerRows={UserSellerRows}, BasketProducts={BasketProducts}, BasketRows={BasketRows}, ReplenishmentUsers={ReplenishmentUsers}, ReplenishmentRows={ReplenishmentRows}, HomeScopes={HomeScopeCount}, HomeRows={HomeRowCount}, HomeCandidateScopes={HomeCandidateScopeCount}, HomeCandidateRows={HomeCandidateRowCount}, ComputedAt={ComputedAtUtc}",
            result.DistinctSeedProductCount,
            result.MaterializedRowCount,
            result.DistinctKeywordCount,
            result.MaterializedKeywordRowCount,
            result.DistinctUserProductScoreUserCount,
            result.MaterializedUserProductScoreRowCount,
            result.DistinctUserCategoryScoreUserCount,
            result.MaterializedUserCategoryScoreRowCount,
            result.DistinctUserSellerScoreUserCount,
            result.MaterializedUserSellerScoreRowCount,
            result.DistinctBasketAffinityProductCount,
            result.MaterializedBasketAffinityRowCount,
            result.DistinctReplenishmentUserCount,
            result.MaterializedReplenishmentProfileRowCount,
            result.DistinctHomePreferenceScopeCount,
            result.MaterializedHomePreferenceRowCount,
            result.DistinctHomeCollaborativeScopeCount,
            result.MaterializedHomeCollaborativeRowCount,
            result.ComputedAtUtc);

        return result;
    }

    private static List<RecommendationSearchKeywordAffinity> BuildKeywordAffinities(
        IReadOnlyCollection<SearchSessionKeyword> searchEvents,
        IReadOnlyCollection<SessionProductPair> searchClickPairs,
        IReadOnlyCollection<SessionProductPair> recommendationClickPairs,
        IReadOnlyCollection<SessionProductPair> productViewPairs,
        DateTime computedAt)
    {
        var sessionsByKeyword = searchEvents
            .Select(item => new SearchSessionKeyword(item.SessionId.Trim(), NormalizeKeyword(item.Keyword)))
            .Where(item => !string.IsNullOrWhiteSpace(item.SessionId) && !string.IsNullOrWhiteSpace(item.Keyword))
            .GroupBy(item => item.Keyword, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Select(item => item.SessionId).Distinct(StringComparer.Ordinal).ToHashSet(StringComparer.Ordinal),
                StringComparer.Ordinal);

        if (sessionsByKeyword.Count == 0)
        {
            return new List<RecommendationSearchKeywordAffinity>();
        }

        var rows = new List<RecommendationSearchKeywordAffinity>();

        foreach (var (keyword, sessions) in sessionsByKeyword)
        {
            var clickCounts = searchClickPairs
                .Where(item => sessions.Contains(item.SessionId))
                .GroupBy(item => item.ProductId)
                .ToDictionary(
                    group => group.Key,
                    group => new
                    {
                        SearchClickCount = group.Count(),
                        SearchClickSessionCount = group.Select(item => item.SessionId).Distinct(StringComparer.Ordinal).Count()
                    });

            var viewCounts = productViewPairs
                .Where(item => sessions.Contains(item.SessionId))
                .GroupBy(item => item.ProductId)
                .ToDictionary(
                    group => group.Key,
                    group => group.Select(item => item.SessionId).Distinct(StringComparer.Ordinal).Count());

            var recommendationCounts = recommendationClickPairs
                .Where(item => sessions.Contains(item.SessionId))
                .GroupBy(item => item.ProductId)
                .ToDictionary(group => group.Key, group => group.Count());

            var productIds = clickCounts.Keys
                .Concat(viewCounts.Keys)
                .Concat(recommendationCounts.Keys)
                .Distinct()
                .ToArray();

            rows.AddRange(productIds
                .Select(productId =>
                {
                    clickCounts.TryGetValue(productId, out var clickSignal);
                    viewCounts.TryGetValue(productId, out var viewSessionCount);
                    recommendationCounts.TryGetValue(productId, out var recommendationClickCount);

                    var searchClickCount = clickSignal?.SearchClickCount ?? 0;
                    var searchClickSessionCount = clickSignal?.SearchClickSessionCount ?? 0;
                    var searchViewSessionCount = viewSessionCount;
                    var searchRecommendationClickCount = recommendationClickCount;
                    var hybridSearchScore =
                        (searchClickCount * 20d)
                        + (searchRecommendationClickCount * 14d)
                        + (searchViewSessionCount * 6d);

                    return new RecommendationSearchKeywordAffinity
                    {
                        Keyword = keyword,
                        ProductId = productId,
                        SearchClickCount = searchClickCount,
                        SearchClickSessionCount = searchClickSessionCount,
                        SearchViewSessionCount = searchViewSessionCount,
                        SearchRecommendationClickCount = searchRecommendationClickCount,
                        HybridSearchScore = hybridSearchScore,
                        ComputedAt = computedAt
                    };
                })
                .Where(item => item.HybridSearchScore > 0d)
                .OrderByDescending(item => item.HybridSearchScore)
                .ThenByDescending(item => item.SearchClickCount)
                .ThenByDescending(item => item.SearchRecommendationClickCount)
                .ThenByDescending(item => item.SearchViewSessionCount)
                .ThenBy(item => item.ProductId)
                .Take(MaxKeywordAffinitiesPerKeyword));
        }

        return rows;
    }

    private async Task<List<RecommendationHomePreferenceSeed>> BuildHomePreferenceSeedsAsync(
        DateTime lookbackFromUtc,
        DateTime computedAt,
        CancellationToken cancellationToken)
    {
        var seedsByScope = new Dictionary<(string ScopeType, string ScopeKey, int ProductId), RecommendationHomePreferenceSeed>();

        void UpdateSeed(string scopeType, string scopeKey, int? userId, int productId, Action<RecommendationHomePreferenceSeed> apply)
        {
            if (productId <= 0 || string.IsNullOrWhiteSpace(scopeType) || string.IsNullOrWhiteSpace(scopeKey))
            {
                return;
            }

            var key = (scopeType, scopeKey, productId);
            if (!seedsByScope.TryGetValue(key, out var seed))
            {
                seed = new RecommendationHomePreferenceSeed
                {
                    ScopeType = scopeType,
                    ScopeKey = scopeKey,
                    UserId = userId,
                    ProductId = productId,
                    ComputedAt = computedAt
                };
                seedsByScope[key] = seed;
            }

            apply(seed);
        }

        var sessionViewSignals = await _db.ProductViewEvents
            .AsNoTracking()
            .Where(item => item.CreatedAt >= lookbackFromUtc && item.ProductId > 0 && item.SessionId != string.Empty)
            .GroupBy(item => new { item.SessionId, item.ProductId })
            .Select(group => new
            {
                group.Key.SessionId,
                group.Key.ProductId,
                ViewCount = group.Count(),
                LastInteractedAtUtc = group.Max(item => item.CreatedAt)
            })
            .ToListAsync(cancellationToken);

        foreach (var signal in sessionViewSignals)
        {
            UpdateSeed("session", signal.SessionId.Trim(), null, signal.ProductId, seed =>
            {
                seed.ViewCount = signal.ViewCount;
                seed.LastInteractedAtUtc = MaxUtc(seed.LastInteractedAtUtc, signal.LastInteractedAtUtc);
            });
        }

        var userViewSignals = await _db.ProductViewEvents
            .AsNoTracking()
            .Where(item => item.CreatedAt >= lookbackFromUtc && item.ProductId > 0 && item.UserId.HasValue && item.UserId > 0)
            .GroupBy(item => new { item.UserId, item.ProductId })
            .Select(group => new
            {
                UserId = group.Key.UserId!.Value,
                group.Key.ProductId,
                ViewCount = group.Count(),
                LastInteractedAtUtc = group.Max(item => item.CreatedAt)
            })
            .ToListAsync(cancellationToken);

        foreach (var signal in userViewSignals)
        {
            UpdateSeed("user", signal.UserId.ToString(), signal.UserId, signal.ProductId, seed =>
            {
                seed.ViewCount = signal.ViewCount;
                seed.LastInteractedAtUtc = MaxUtc(seed.LastInteractedAtUtc, signal.LastInteractedAtUtc);
            });
        }

        var sessionSearchClickSignals = await _db.SearchClickEvents
            .AsNoTracking()
            .Where(item => item.CreatedAt >= lookbackFromUtc && item.ProductId > 0 && item.SessionId != string.Empty)
            .GroupBy(item => new { item.SessionId, item.ProductId })
            .Select(group => new
            {
                group.Key.SessionId,
                group.Key.ProductId,
                SearchClickCount = group.Count(),
                LastInteractedAtUtc = group.Max(item => item.CreatedAt)
            })
            .ToListAsync(cancellationToken);

        foreach (var signal in sessionSearchClickSignals)
        {
            UpdateSeed("session", signal.SessionId.Trim(), null, signal.ProductId, seed =>
            {
                seed.SearchClickCount = signal.SearchClickCount;
                seed.LastInteractedAtUtc = MaxUtc(seed.LastInteractedAtUtc, signal.LastInteractedAtUtc);
            });
        }

        var userSearchClickSignals = await _db.SearchClickEvents
            .AsNoTracking()
            .Where(item => item.CreatedAt >= lookbackFromUtc && item.ProductId > 0 && item.UserId.HasValue && item.UserId > 0)
            .GroupBy(item => new { item.UserId, item.ProductId })
            .Select(group => new
            {
                UserId = group.Key.UserId!.Value,
                group.Key.ProductId,
                SearchClickCount = group.Count(),
                LastInteractedAtUtc = group.Max(item => item.CreatedAt)
            })
            .ToListAsync(cancellationToken);

        foreach (var signal in userSearchClickSignals)
        {
            UpdateSeed("user", signal.UserId.ToString(), signal.UserId, signal.ProductId, seed =>
            {
                seed.SearchClickCount = signal.SearchClickCount;
                seed.LastInteractedAtUtc = MaxUtc(seed.LastInteractedAtUtc, signal.LastInteractedAtUtc);
            });
        }

        var sessionRecommendationClickSignals = await _db.RecommendationClickEvents
            .AsNoTracking()
            .Where(item => item.CreatedAt >= lookbackFromUtc && item.ProductId > 0 && item.SessionId != string.Empty)
            .GroupBy(item => new { item.SessionId, item.ProductId })
            .Select(group => new
            {
                group.Key.SessionId,
                group.Key.ProductId,
                RecommendationClickCount = group.Count(),
                LastInteractedAtUtc = group.Max(item => item.CreatedAt)
            })
            .ToListAsync(cancellationToken);

        foreach (var signal in sessionRecommendationClickSignals)
        {
            UpdateSeed("session", signal.SessionId.Trim(), null, signal.ProductId, seed =>
            {
                seed.RecommendationClickCount = signal.RecommendationClickCount;
                seed.LastInteractedAtUtc = MaxUtc(seed.LastInteractedAtUtc, signal.LastInteractedAtUtc);
            });
        }

        var userRecommendationClickSignals = await _db.RecommendationClickEvents
            .AsNoTracking()
            .Where(item => item.CreatedAt >= lookbackFromUtc && item.ProductId > 0 && item.UserId.HasValue && item.UserId > 0)
            .GroupBy(item => new { item.UserId, item.ProductId })
            .Select(group => new
            {
                UserId = group.Key.UserId!.Value,
                group.Key.ProductId,
                RecommendationClickCount = group.Count(),
                LastInteractedAtUtc = group.Max(item => item.CreatedAt)
            })
            .ToListAsync(cancellationToken);

        foreach (var signal in userRecommendationClickSignals)
        {
            UpdateSeed("user", signal.UserId.ToString(), signal.UserId, signal.ProductId, seed =>
            {
                seed.RecommendationClickCount = signal.RecommendationClickCount;
                seed.LastInteractedAtUtc = MaxUtc(seed.LastInteractedAtUtc, signal.LastInteractedAtUtc);
            });
        }

        var purchaseSignals = await (
                from order in _db.Orders.AsNoTracking()
                join detail in _db.OrderDetails.AsNoTracking() on order.OrderId equals detail.OrderId
                where order.UserId > 0
                      && detail.ProductId > 0
                      && order.OrderDate >= lookbackFromUtc
                      && SuccessfulOrderStatuses.Contains((order.Status ?? string.Empty).Trim().ToLower())
                group new { order, detail } by new { order.UserId, detail.ProductId }
                into grouped
                select new
                {
                    grouped.Key.UserId,
                    grouped.Key.ProductId,
                    PurchaseCount = grouped.Sum(item => item.detail.Quantity),
                    LastInteractedAtUtc = grouped.Max(item => item.order.OrderDate)
                })
            .ToListAsync(cancellationToken);

        foreach (var signal in purchaseSignals)
        {
            UpdateSeed("user", signal.UserId.ToString(), signal.UserId, signal.ProductId, seed =>
            {
                seed.PurchaseCount = signal.PurchaseCount;
                seed.LastInteractedAtUtc = MaxUtc(seed.LastInteractedAtUtc, signal.LastInteractedAtUtc);
            });
        }

        foreach (var seed in seedsByScope.Values)
        {
            seed.PreferenceScore =
                (seed.ViewCount * 10d)
                + (seed.SearchClickCount * 28d)
                + (seed.RecommendationClickCount * 22d)
                + (seed.PurchaseCount * 35d);
        }

        return seedsByScope.Values
            .Where(seed => seed.PreferenceScore > 0d)
            .GroupBy(seed => (seed.ScopeType, seed.ScopeKey))
            .SelectMany(group => group
                .OrderByDescending(item => item.PreferenceScore)
                .ThenByDescending(item => item.PurchaseCount)
                .ThenByDescending(item => item.SearchClickCount)
                .ThenByDescending(item => item.RecommendationClickCount)
                .ThenByDescending(item => item.ViewCount)
                .ThenByDescending(item => item.LastInteractedAtUtc ?? DateTime.MinValue)
                .ThenBy(item => item.ProductId)
                .Take(MaxHomePreferenceSeedsPerScope))
            .ToList();
    }

    private static List<RecommendationHomeCollaborativeCandidate> BuildHomeCollaborativeCandidates(
        IReadOnlyCollection<RecommendationHomePreferenceSeed> homePreferenceSeeds,
        IReadOnlyCollection<RecommendationProductAffinity> productAffinities,
        DateTime computedAt)
    {
        if (homePreferenceSeeds.Count == 0 || productAffinities.Count == 0)
        {
            return new List<RecommendationHomeCollaborativeCandidate>();
        }

        var affinitiesBySeedProductId = productAffinities
            .Where(item => item.AffinityScore > 0d)
            .GroupBy(item => item.SeedProductId)
            .ToDictionary(group => group.Key, group => group.ToArray());

        var candidatesByScope = new Dictionary<(string ScopeType, string ScopeKey, int ProductId), RecommendationHomeCollaborativeCandidate>();
        var seedProductIdsByScope = homePreferenceSeeds
            .GroupBy(item => (item.ScopeType, item.ScopeKey))
            .ToDictionary(
                group => group.Key,
                group => group.Select(item => item.ProductId).ToHashSet());

        foreach (var seed in homePreferenceSeeds)
        {
            if (!affinitiesBySeedProductId.TryGetValue(seed.ProductId, out var affinityRows))
            {
                continue;
            }

            if (!seedProductIdsByScope.TryGetValue((seed.ScopeType, seed.ScopeKey), out var scopeSeedProductIds))
            {
                continue;
            }

            var seedWeight = 1d + Math.Min(seed.PreferenceScore, 240d) / 120d;
            foreach (var affinity in affinityRows)
            {
                if (scopeSeedProductIds.Contains(affinity.CandidateProductId))
                {
                    continue;
                }

                var key = (seed.ScopeType, seed.ScopeKey, affinity.CandidateProductId);
                if (!candidatesByScope.TryGetValue(key, out var candidate))
                {
                    candidate = new RecommendationHomeCollaborativeCandidate
                    {
                        ScopeType = seed.ScopeType,
                        ScopeKey = seed.ScopeKey,
                        UserId = seed.UserId,
                        ProductId = affinity.CandidateProductId,
                        ComputedAt = computedAt
                    };
                    candidatesByScope[key] = candidate;
                }

                candidate.CoPurchaseOrderCount += affinity.CoPurchaseOrderCount;
                candidate.CoViewSessionCount += affinity.CoViewSessionCount;
                candidate.CoClickSessionCount += affinity.CoClickSessionCount;
                candidate.CollaborativeScore += affinity.AffinityScore * seedWeight;
            }
        }

        return candidatesByScope.Values
            .Where(item => item.CollaborativeScore > 0d)
            .GroupBy(item => (item.ScopeType, item.ScopeKey))
            .SelectMany(group => group
                .OrderByDescending(item => item.CollaborativeScore)
                .ThenByDescending(item => item.CoPurchaseOrderCount)
                .ThenByDescending(item => item.CoClickSessionCount)
                .ThenByDescending(item => item.CoViewSessionCount)
                .ThenBy(item => item.ProductId)
            .Take(MaxHomeCollaborativeCandidatesPerScope))
            .ToList();
    }

    private async Task<List<RecommendationUserProductScore>> BuildUserProductScoresAsync(
        DateTime lookbackFromUtc,
        DateTime computedAt,
        CancellationToken cancellationToken)
    {
        var scoresByUserProduct = new Dictionary<(int UserId, int ProductId), RecommendationUserProductScore>();

        void UpdateScore(int userId, int productId, Action<RecommendationUserProductScore> apply)
        {
            if (userId <= 0 || productId <= 0)
            {
                return;
            }

            var key = (userId, productId);
            if (!scoresByUserProduct.TryGetValue(key, out var score))
            {
                score = new RecommendationUserProductScore
                {
                    UserId = userId,
                    ProductId = productId,
                    ComputedAt = computedAt
                };
                scoresByUserProduct[key] = score;
            }

            apply(score);
        }

        var userViewSignals = await _db.ProductViewEvents
            .AsNoTracking()
            .Where(item => item.CreatedAt >= lookbackFromUtc && item.ProductId > 0 && item.UserId.HasValue && item.UserId.Value > 0)
            .GroupBy(item => new { UserId = item.UserId!.Value, item.ProductId })
            .Select(grouped => new
            {
                grouped.Key.UserId,
                grouped.Key.ProductId,
                ViewCount = grouped.Count(),
                LastInteractedAtUtc = grouped.Max(item => item.CreatedAt)
            })
            .ToListAsync(cancellationToken);

        foreach (var signal in userViewSignals)
        {
            UpdateScore(signal.UserId, signal.ProductId, item =>
            {
                item.ViewCount = signal.ViewCount;
                item.LastInteractedAtUtc = MaxUtc(item.LastInteractedAtUtc, signal.LastInteractedAtUtc);
            });
        }

        var userSearchClickSignals = await _db.SearchClickEvents
            .AsNoTracking()
            .Where(item => item.CreatedAt >= lookbackFromUtc && item.ProductId > 0 && item.UserId.HasValue && item.UserId.Value > 0)
            .GroupBy(item => new { UserId = item.UserId!.Value, item.ProductId })
            .Select(grouped => new
            {
                grouped.Key.UserId,
                grouped.Key.ProductId,
                SearchClickCount = grouped.Count(),
                LastInteractedAtUtc = grouped.Max(item => item.CreatedAt)
            })
            .ToListAsync(cancellationToken);

        foreach (var signal in userSearchClickSignals)
        {
            UpdateScore(signal.UserId, signal.ProductId, item =>
            {
                item.SearchClickCount = signal.SearchClickCount;
                item.LastInteractedAtUtc = MaxUtc(item.LastInteractedAtUtc, signal.LastInteractedAtUtc);
            });
        }

        var userRecommendationClickSignals = await _db.RecommendationClickEvents
            .AsNoTracking()
            .Where(item => item.CreatedAt >= lookbackFromUtc && item.ProductId > 0 && item.UserId.HasValue && item.UserId.Value > 0)
            .GroupBy(item => new { UserId = item.UserId!.Value, item.ProductId })
            .Select(grouped => new
            {
                grouped.Key.UserId,
                grouped.Key.ProductId,
                RecommendationClickCount = grouped.Count(),
                LastInteractedAtUtc = grouped.Max(item => item.CreatedAt)
            })
            .ToListAsync(cancellationToken);

        foreach (var signal in userRecommendationClickSignals)
        {
            UpdateScore(signal.UserId, signal.ProductId, item =>
            {
                item.RecommendationClickCount = signal.RecommendationClickCount;
                item.LastInteractedAtUtc = MaxUtc(item.LastInteractedAtUtc, signal.LastInteractedAtUtc);
            });
        }

        var purchaseSignals = await (
                from order in _db.Orders.AsNoTracking()
                join detail in _db.OrderDetails.AsNoTracking() on order.OrderId equals detail.OrderId
                where order.UserId > 0
                    && detail.ProductId > 0
                    && order.OrderDate >= lookbackFromUtc
                    && SuccessfulOrderStatuses.Contains((order.Status ?? string.Empty).Trim().ToLower())
                group new { order, detail } by new { order.UserId, detail.ProductId }
                into grouped
                select new
                {
                    grouped.Key.UserId,
                    grouped.Key.ProductId,
                    PurchaseCount = grouped.Sum(item => item.detail.Quantity),
                    LastInteractedAtUtc = grouped.Max(item => item.order.OrderDate)
                })
            .ToListAsync(cancellationToken);

        foreach (var signal in purchaseSignals)
        {
            UpdateScore(signal.UserId, signal.ProductId, item =>
            {
                item.PurchaseCount = signal.PurchaseCount;
                item.LastInteractedAtUtc = MaxUtc(item.LastInteractedAtUtc, signal.LastInteractedAtUtc);
            });
        }

        foreach (var item in scoresByUserProduct.Values)
        {
            item.UserProductScore =
                (item.PurchaseCount * 42d)
                + (item.SearchClickCount * 22d)
                + (item.RecommendationClickCount * 18d)
                + (item.ViewCount * 8d);

            if (item.LastInteractedAtUtc.HasValue)
            {
                var ageDays = Math.Max(0d, (computedAt - item.LastInteractedAtUtc.Value).TotalDays);
                item.UserProductScore += Math.Max(0d, 18d - Math.Min(ageDays, 18d));
            }
        }

        return scoresByUserProduct.Values
            .Where(item => item.UserProductScore > 0d)
            .GroupBy(item => item.UserId)
            .SelectMany(group => group
                .OrderByDescending(item => item.UserProductScore)
                .ThenByDescending(item => item.PurchaseCount)
                .ThenByDescending(item => item.SearchClickCount)
                .ThenByDescending(item => item.RecommendationClickCount)
                .ThenByDescending(item => item.ViewCount)
                .ThenByDescending(item => item.LastInteractedAtUtc ?? DateTime.MinValue)
                .ThenBy(item => item.ProductId)
                .Take(MaxUserProductScoresPerUser))
            .ToList();
    }

    private async Task<List<RecommendationUserSellerScore>> BuildUserSellerScoresAsync(
        DateTime lookbackFromUtc,
        DateTime computedAt,
        CancellationToken cancellationToken)
    {
        var scoresByUserSeller = new Dictionary<(int UserId, int SellerId), RecommendationUserSellerScore>();

        void UpdateScore(int userId, int sellerId, Action<RecommendationUserSellerScore> apply)
        {
            if (userId <= 0 || sellerId <= 0)
            {
                return;
            }

            var key = (userId, sellerId);
            if (!scoresByUserSeller.TryGetValue(key, out var score))
            {
                score = new RecommendationUserSellerScore
                {
                    UserId = userId,
                    SellerId = sellerId,
                    ComputedAt = computedAt
                };
                scoresByUserSeller[key] = score;
            }

            apply(score);
        }

        var userViewSignals = await _db.ProductViewEvents
            .AsNoTracking()
            .Where(item =>
                item.CreatedAt >= lookbackFromUtc
                && item.UserId.HasValue
                && item.UserId > 0
                && item.SellerId.HasValue
                && item.SellerId > 0)
            .GroupBy(item => new { UserId = item.UserId!.Value, SellerId = item.SellerId!.Value })
            .Select(group => new
            {
                group.Key.UserId,
                group.Key.SellerId,
                ViewCount = group.Count(),
                LastInteractedAtUtc = group.Max(item => item.CreatedAt)
            })
            .ToListAsync(cancellationToken);

        foreach (var signal in userViewSignals)
        {
            UpdateScore(signal.UserId, signal.SellerId, score =>
            {
                score.ViewCount = signal.ViewCount;
                score.LastInteractedAtUtc = MaxUtc(score.LastInteractedAtUtc, signal.LastInteractedAtUtc);
            });
        }

        var userSearchClickSignals = await _db.SearchClickEvents
            .AsNoTracking()
            .Where(item =>
                item.CreatedAt >= lookbackFromUtc
                && item.UserId.HasValue
                && item.UserId > 0
                && item.SellerId.HasValue
                && item.SellerId > 0)
            .GroupBy(item => new { UserId = item.UserId!.Value, SellerId = item.SellerId!.Value })
            .Select(group => new
            {
                group.Key.UserId,
                group.Key.SellerId,
                SearchClickCount = group.Count(),
                LastInteractedAtUtc = group.Max(item => item.CreatedAt)
            })
            .ToListAsync(cancellationToken);

        foreach (var signal in userSearchClickSignals)
        {
            UpdateScore(signal.UserId, signal.SellerId, score =>
            {
                score.SearchClickCount = signal.SearchClickCount;
                score.LastInteractedAtUtc = MaxUtc(score.LastInteractedAtUtc, signal.LastInteractedAtUtc);
            });
        }

        var userPurchaseSignals = await (
                from sellerOrder in _db.SellerOrders.AsNoTracking()
                join order in _db.Orders.AsNoTracking() on sellerOrder.OrderId equals order.OrderId
                join sellerOrderItem in _db.SellerOrderItems.AsNoTracking() on sellerOrder.SellerOrderId equals sellerOrderItem.SellerOrderId
                where order.UserId > 0
                      && sellerOrder.SellerId > 0
                      && order.OrderDate >= lookbackFromUtc
                      && SuccessfulOrderStatuses.Contains((order.Status ?? string.Empty).Trim().ToLower())
                group new { order, sellerOrderItem } by new { order.UserId, sellerOrder.SellerId }
                into grouped
                select new
                {
                    grouped.Key.UserId,
                    grouped.Key.SellerId,
                    PurchaseCount = grouped.Sum(item => item.sellerOrderItem.Quantity),
                    LastInteractedAtUtc = grouped.Max(item => item.order.OrderDate)
                })
            .ToListAsync(cancellationToken);

        foreach (var signal in userPurchaseSignals)
        {
            UpdateScore(signal.UserId, signal.SellerId, score =>
            {
                score.PurchaseCount = signal.PurchaseCount;
                score.LastInteractedAtUtc = MaxUtc(score.LastInteractedAtUtc, signal.LastInteractedAtUtc);
            });
        }

        return scoresByUserSeller.Values
            .Select(score =>
            {
                score.UserSellerScore =
                    (score.PurchaseCount * 42d)
                    + (score.SearchClickCount * 14d)
                    + (score.ViewCount * 4d);
                score.ComputedAt = computedAt;
                return score;
            })
            .Where(score => score.UserSellerScore > 0d)
            .GroupBy(score => score.UserId)
            .SelectMany(group => group
                .OrderByDescending(item => item.UserSellerScore)
                .ThenByDescending(item => item.PurchaseCount)
                .ThenByDescending(item => item.SearchClickCount)
                .ThenByDescending(item => item.ViewCount)
                .ThenByDescending(item => item.LastInteractedAtUtc ?? DateTime.MinValue)
                .ThenBy(item => item.SellerId)
                .Take(MaxUserSellerScoresPerUser))
            .ToList();
    }

    private async Task<List<RecommendationUserCategoryScore>> BuildUserCategoryScoresAsync(
        IReadOnlyCollection<RecommendationUserProductScore> userProductScores,
        DateTime computedAt,
        CancellationToken cancellationToken)
    {
        if (userProductScores.Count == 0)
        {
            return new List<RecommendationUserCategoryScore>();
        }

        IReadOnlyDictionary<int, CatalogProductCategoryMappingItem> categoryMappings;
        try
        {
            categoryMappings = await _catalogInventoryClient.GetProductCategoryMappingsAsync(
                userProductScores.Select(item => item.ProductId),
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Khong lay duoc category mapping tu Catalog khi rebuild UserCategoryScore.");
            return new List<RecommendationUserCategoryScore>();
        }

        if (categoryMappings.Count == 0)
        {
            return new List<RecommendationUserCategoryScore>();
        }

        var scoresByUserCategory = new Dictionary<(int UserId, int CategoryId), RecommendationUserCategoryScore>();

        void UpdateScore(
            int userId,
            int categoryId,
            string categoryName,
            Action<RecommendationUserCategoryScore> apply)
        {
            if (userId <= 0 || categoryId <= 0)
            {
                return;
            }

            var key = (userId, categoryId);
            if (!scoresByUserCategory.TryGetValue(key, out var score))
            {
                score = new RecommendationUserCategoryScore
                {
                    UserId = userId,
                    CategoryId = categoryId,
                    CategoryName = string.IsNullOrWhiteSpace(categoryName)
                        ? $"Category {categoryId}"
                        : categoryName.Trim(),
                    ComputedAt = computedAt
                };
                scoresByUserCategory[key] = score;
            }
            else if (!string.IsNullOrWhiteSpace(categoryName))
            {
                score.CategoryName = categoryName.Trim();
            }

            apply(score);
        }

        foreach (var userProductScore in userProductScores)
        {
            if (!categoryMappings.TryGetValue(userProductScore.ProductId, out var mapping)
                || mapping.CategoryId <= 0)
            {
                continue;
            }

            UpdateScore(userProductScore.UserId, mapping.CategoryId, mapping.CategoryName, score =>
            {
                score.ViewCount += userProductScore.ViewCount;
                score.SearchClickCount += userProductScore.SearchClickCount;
                score.RecommendationClickCount += userProductScore.RecommendationClickCount;
                score.PurchaseCount += userProductScore.PurchaseCount;
                if (userProductScore.LastInteractedAtUtc.HasValue)
                {
                    score.LastInteractedAtUtc = MaxUtc(score.LastInteractedAtUtc, userProductScore.LastInteractedAtUtc.Value);
                }

                score.UserCategoryScore += userProductScore.UserProductScore
                    + CalculateLongTermCategoryLift(userProductScore, computedAt);
            });
        }

        return scoresByUserCategory.Values
            .Where(score => score.UserCategoryScore > 0d)
            .GroupBy(score => score.UserId)
            .SelectMany(group => group
                .OrderByDescending(item => item.UserCategoryScore)
                .ThenByDescending(item => item.PurchaseCount)
                .ThenByDescending(item => item.SearchClickCount)
                .ThenByDescending(item => item.RecommendationClickCount)
                .ThenByDescending(item => item.ViewCount)
                .ThenByDescending(item => item.LastInteractedAtUtc ?? DateTime.MinValue)
                .ThenBy(item => item.CategoryId)
                .Take(MaxUserCategoryScoresPerUser))
            .ToList();
    }

    private static List<RecommendationBasketAffinity> BuildBasketAffinities(
        IReadOnlyCollection<RecommendationProductAffinity> productAffinities,
        DateTime computedAt)
    {
        return productAffinities
            .Where(item => item.CoPurchaseOrderCount > 0)
            .Select(item => new RecommendationBasketAffinity
            {
                ProductId = item.SeedProductId,
                CandidateProductId = item.CandidateProductId,
                CoPurchaseOrderCount = item.CoPurchaseOrderCount,
                BasketScore = (item.CoPurchaseOrderCount * 30d) + (item.CoClickSessionCount * 6d),
                ComputedAt = computedAt
            })
            .Where(item => item.BasketScore > 0d)
            .GroupBy(item => item.ProductId)
            .SelectMany(group => group
                .OrderByDescending(item => item.BasketScore)
                .ThenByDescending(item => item.CoPurchaseOrderCount)
                .ThenBy(item => item.CandidateProductId)
                .Take(MaxBasketAffinitiesPerProduct))
            .ToList();
    }

    private async Task<List<RecommendationReplenishmentProfile>> BuildReplenishmentProfilesAsync(
        DateTime lookbackFromUtc,
        DateTime computedAt,
        CancellationToken cancellationToken)
    {
        var purchaseRows = await (
                from order in _db.Orders.AsNoTracking()
                join detail in _db.OrderDetails.AsNoTracking() on order.OrderId equals detail.OrderId
                where order.UserId > 0
                    && detail.ProductId > 0
                    && order.OrderDate >= lookbackFromUtc
                    && SuccessfulOrderStatuses.Contains((order.Status ?? string.Empty).Trim().ToLower())
                select new
                {
                    order.UserId,
                    detail.ProductId,
                    order.OrderDate,
                    detail.Quantity
                })
            .ToListAsync(cancellationToken);

        var profiles = new List<RecommendationReplenishmentProfile>();
        foreach (var grouped in purchaseRows
                     .GroupBy(item => new { item.UserId, item.ProductId }))
        {
            var orderedPurchases = grouped
                .OrderBy(item => item.OrderDate)
                .ToList();
            if (orderedPurchases.Count == 0)
            {
                continue;
            }

            var purchaseCount = orderedPurchases.Sum(item => item.Quantity);
            var lastPurchasedAtUtc = orderedPurchases[^1].OrderDate;
            var averageRepurchaseDays = 0d;
            DateTime? expectedReorderAtUtc = null;

            if (orderedPurchases.Count >= 2)
            {
                var gaps = new List<double>(orderedPurchases.Count - 1);
                for (var i = 1; i < orderedPurchases.Count; i++)
                {
                    var gap = (orderedPurchases[i].OrderDate - orderedPurchases[i - 1].OrderDate).TotalDays;
                    if (gap > 0d)
                    {
                        gaps.Add(gap);
                    }
                }

                if (gaps.Count > 0)
                {
                    averageRepurchaseDays = gaps.Average();
                    expectedReorderAtUtc = lastPurchasedAtUtc.AddDays(averageRepurchaseDays);
                }
            }

            var daysSinceLastPurchase = Math.Max(0d, (computedAt - lastPurchasedAtUtc).TotalDays);
            var timingScore = expectedReorderAtUtc.HasValue
                ? Math.Max(0d, 30d - Math.Abs((expectedReorderAtUtc.Value - computedAt).TotalDays))
                : Math.Max(0d, 14d - Math.Min(daysSinceLastPurchase, 14d));

            var replenishmentScore = (purchaseCount * 14d) + timingScore;
            if (averageRepurchaseDays > 0d)
            {
                replenishmentScore += Math.Max(0d, 18d - Math.Min(averageRepurchaseDays, 18d));
            }

            profiles.Add(new RecommendationReplenishmentProfile
            {
                UserId = grouped.Key.UserId,
                ProductId = grouped.Key.ProductId,
                PurchaseCount = purchaseCount,
                LastPurchasedAtUtc = lastPurchasedAtUtc,
                AverageRepurchaseDays = Math.Round(averageRepurchaseDays, 2),
                ExpectedReorderAtUtc = expectedReorderAtUtc,
                ReplenishmentScore = Math.Round(replenishmentScore, 2),
                ComputedAt = computedAt
            });
        }

        return profiles
            .Where(item => item.ReplenishmentScore > 0d)
            .GroupBy(item => item.UserId)
            .SelectMany(group => group
                .OrderByDescending(item => item.ReplenishmentScore)
                .ThenByDescending(item => item.PurchaseCount)
                .ThenByDescending(item => item.LastPurchasedAtUtc)
                .ThenBy(item => item.ProductId)
                .Take(MaxReplenishmentProfilesPerUser))
            .ToList();
    }

    private static string NormalizeKeyword(string? keyword)
        => (keyword ?? string.Empty).Trim().ToLowerInvariant();

    private static DateTime? MaxUtc(DateTime? current, DateTime candidate)
        => !current.HasValue || candidate > current.Value
            ? candidate
            : current;

    private static double CalculateLongTermCategoryLift(
        RecommendationUserProductScore userProductScore,
        DateTime computedAt)
    {
        var lift =
            (userProductScore.PurchaseCount * 8d)
            + (userProductScore.SearchClickCount * 4d)
            + (userProductScore.RecommendationClickCount * 3d)
            + (userProductScore.ViewCount * 1.5d);

        if (userProductScore.LastInteractedAtUtc.HasValue)
        {
            var ageDays = Math.Max(0d, (computedAt - userProductScore.LastInteractedAtUtc.Value).TotalDays);
            lift += Math.Max(0d, 12d - Math.Min(ageDays, 12d));
        }

        return lift;
    }
}

public sealed class RecommendationAffinityRefreshResult
{
    public int MaterializedRowCount { get; set; }

    public int DistinctSeedProductCount { get; set; }

    public int MaterializedKeywordRowCount { get; set; }

    public int DistinctKeywordCount { get; set; }

    public int MaterializedUserProductScoreRowCount { get; set; }

    public int DistinctUserProductScoreUserCount { get; set; }

    public int MaterializedUserCategoryScoreRowCount { get; set; }

    public int DistinctUserCategoryScoreUserCount { get; set; }

    public int MaterializedUserSellerScoreRowCount { get; set; }

    public int DistinctUserSellerScoreUserCount { get; set; }

    public int MaterializedBasketAffinityRowCount { get; set; }

    public int DistinctBasketAffinityProductCount { get; set; }

    public int MaterializedReplenishmentProfileRowCount { get; set; }

    public int DistinctReplenishmentUserCount { get; set; }

    public int MaterializedHomePreferenceRowCount { get; set; }

    public int DistinctHomePreferenceScopeCount { get; set; }

    public int MaterializedHomeCollaborativeRowCount { get; set; }

    public int DistinctHomeCollaborativeScopeCount { get; set; }

    public DateTime ComputedAtUtc { get; set; }
}

