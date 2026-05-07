using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using FreshFarm.Web.Bff.Options;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace FreshFarm.Web.Bff.Services;

public sealed class SessionAwareRecommendationReranker : ISessionAwareRecommendationReranker
{
    private readonly IDistributedCache _cache;
    private readonly IOptionsMonitor<SessionAwareRecommendationOptions> _options;
    private readonly IRecommendationMetricsClient _metricsClient;
    private readonly ILogger<SessionAwareRecommendationReranker> _logger;

    public SessionAwareRecommendationReranker(
        IDistributedCache cache,
        IOptionsMonitor<SessionAwareRecommendationOptions> options,
        ILogger<SessionAwareRecommendationReranker> logger)
        : this(cache, options, NoopRecommendationMetricsClient.Instance, logger)
    {
    }

    [ActivatorUtilitiesConstructor]
    public SessionAwareRecommendationReranker(
        IDistributedCache cache,
        IOptionsMonitor<SessionAwareRecommendationOptions> options,
        IRecommendationMetricsClient metricsClient,
        ILogger<SessionAwareRecommendationReranker> logger)
    {
        _cache = cache;
        _options = options;
        _metricsClient = metricsClient;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SessionAwareRerankedProduct>> RerankAsync(
        int userId,
        IReadOnlyList<SessionAwareRecommendationCandidate> candidates,
        SessionAwareRecommendationContext? context = null,
        CancellationToken cancellationToken = default)
    {
        if (userId <= 0 || candidates.Count == 0)
        {
            return CreateBaseScoreResults(candidates);
        }

        var startedAt = Stopwatch.GetTimestamp();
        var options = _options.CurrentValue;
        var normalizedContext = context ?? new SessionAwareRecommendationContext();
        var cappedCandidates = candidates
            .Where(item => item.ProductId > 0)
            .Take(Math.Clamp(options.MaxCandidates, 1, 2_000))
            .ToArray();
        var sessionSignals = await LoadSessionSignalsAsync(userId, options, cancellationToken);
        var hasSessionSignals = sessionSignals.RecentSearches.Count > 0 || sessionSignals.RecentClicks.Count > 0;
        if (!hasSessionSignals)
        {
            return CreateBaseScoreResults(cappedCandidates);
        }

        var searchTokens = BuildRecentSearchTokenSet(sessionSignals, options);
        var clickedProductIds = sessionSignals.RecentClicks
            .Where(item => item.ProductId > 0)
            .Take(options.MaxRecentClicks)
            .Select(item => item.ProductId)
            .ToHashSet();
        var clickedSellerIds = sessionSignals.RecentClicks
            .Where(item => item.SellerId is > 0)
            .Take(options.MaxRecentClicks)
            .Select(item => item.SellerId!.Value)
            .ToHashSet();
        var clickedCategories = sessionSignals.RecentClicks
            .Select(item => NormalizeText(item.CategoryName))
            .Where(item => item.Length > 0)
            .Take(options.MaxRecentClicks)
            .ToHashSet(StringComparer.Ordinal);

        if (!HasActionableSessionMatch(cappedCandidates, searchTokens, clickedProductIds, clickedSellerIds, clickedCategories))
        {
            return CreateBaseScoreResults(cappedCandidates);
        }

        var objectiveScoreTask = LoadNormalizedObjectiveScoresAsync(cappedCandidates, options, cancellationToken);
        var negativeFeedbackTask = LoadNegativeFeedbackScoresAsync(userId, cappedCandidates, options, cancellationToken);
        await Task.WhenAll(objectiveScoreTask, negativeFeedbackTask);
        var normalizedObjectiveScores = await objectiveScoreTask;
        var negativeFeedbackScores = await negativeFeedbackTask;

        var ranked = cappedCandidates
            .Select((item, index) => ScoreCandidate(
                item,
                originalPosition: index + 1,
                searchTokens,
                clickedProductIds,
                clickedSellerIds,
                clickedCategories,
                normalizedObjectiveScores,
                negativeFeedbackScores,
                normalizedContext,
                options))
            .OrderByDescending(item => item.FinalScore)
            .ThenByDescending(item => item.Product.AvailableStock > 0)
            .ThenBy(item => item.Product.ProductId)
            .ToArray();

        var elapsedMs = Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;
        if (elapsedMs > options.WarnIfSlowerThanMilliseconds)
        {
            _logger.LogWarning(
                "Session-aware recommendation rerank exceeded latency budget. UserId={UserId}, CandidateCount={CandidateCount}, ElapsedMs={ElapsedMs:0.##}",
                userId,
                candidates.Count,
                elapsedMs);
        }

        return ranked;
    }

    private async Task<SessionRecommendationSignals> LoadSessionSignalsAsync(
        int userId,
        SessionAwareRecommendationOptions options,
        CancellationToken cancellationToken)
    {
        var key = $"{options.RedisKeyPrefix}{userId}";
        string? payload;
        try
        {
            var timeout = GetSessionCacheReadTimeout(options);
            using var timeoutToken = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutToken.CancelAfter(timeout);
            payload = await _cache
                .GetStringAsync(key, timeoutToken.Token)
                .WaitAsync(timeout, cancellationToken);
        }
        catch (Exception ex)
        {
            LogCacheFailure(ex, "Cannot load recommendation session signals from cache. UserId={UserId}, Key={RedisKey}", userId, key);
            return SessionRecommendationSignals.Empty;
        }

        if (string.IsNullOrWhiteSpace(payload))
        {
            return SessionRecommendationSignals.Empty;
        }

        try
        {
            return ParseSessionSignals(payload, options);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Invalid recommendation session signal payload. UserId={UserId}, Key={RedisKey}", userId, key);
            return SessionRecommendationSignals.Empty;
        }
    }

    private static TimeSpan GetSessionCacheReadTimeout(SessionAwareRecommendationOptions options)
    {
        return TimeSpan.FromMilliseconds(Math.Clamp(options.SessionCacheReadTimeoutMilliseconds, 1, 30));
    }

    private void LogCacheFailure(Exception ex, string message, params object[] args)
    {
        if (ex is OperationCanceledException)
        {
            _logger.LogDebug(ex, message, args);
            return;
        }

        _logger.LogWarning(ex, message, args);
    }

    private static SessionRecommendationSignals ParseSessionSignals(
        string payload,
        SessionAwareRecommendationOptions options)
    {
        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;
        var searches = new List<SessionSearchSignal>();
        var clicks = new List<SessionClickSignal>();

        if (TryGetProperty(root, out var searchArray, "recentSearches", "recent_searches", "searches"))
        {
            foreach (var item in EnumerateArray(searchArray).Take(options.MaxRecentSearches))
            {
                var keyword = item.ValueKind == JsonValueKind.String
                    ? item.GetString()
                    : ReadString(item, "keyword", "query", "term", "searchText", "search_text");
                if (!string.IsNullOrWhiteSpace(keyword))
                {
                    searches.Add(new SessionSearchSignal(keyword));
                }
            }
        }

        if (TryGetProperty(root, out var clickArray, "recentClicks", "recent_clicks", "clicks"))
        {
            foreach (var item in EnumerateArray(clickArray).Take(options.MaxRecentClicks))
            {
                if (item.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                clicks.Add(new SessionClickSignal(
                    ReadInt(item, "productId", "product_id") ?? 0,
                    ReadInt(item, "sellerId", "seller_id", "primarySellerId", "primary_seller_id"),
                    ReadString(item, "categoryName", "category_name")));
            }
        }

        return new SessionRecommendationSignals(searches, clicks);
    }

    private SessionAwareRerankedProduct ScoreCandidate(
        SessionAwareRecommendationCandidate candidate,
        int originalPosition,
        HashSet<string> searchTokens,
        HashSet<int> clickedProductIds,
        HashSet<int> clickedSellerIds,
        HashSet<string> clickedCategories,
        IReadOnlyDictionary<int, NormalizedObjectiveScores> normalizedObjectiveScores,
        IReadOnlyDictionary<int, RecommendationNegativeFeedbackMetric> negativeFeedbackScores,
        SessionAwareRecommendationContext context,
        SessionAwareRecommendationOptions options)
    {
        var score = double.IsFinite(candidate.BaseScore) ? candidate.BaseScore : 0;
        var sessionBoost = 0d;
        var signals = new List<string>(capacity: 6);

        if (normalizedObjectiveScores.TryGetValue(candidate.ProductId, out var objectiveScores))
        {
            score += CalculateObjectiveScore(objectiveScores, options, signals);
        }

        if (negativeFeedbackScores.TryGetValue(candidate.ProductId, out var negativeFeedback)
            && negativeFeedback.PenaltyScore < 0d)
        {
            var penalty = negativeFeedback.PenaltyScore * ResolveNegativeFeedbackPenaltyScale(options);
            score += penalty;
            signals.Add($"negative_feedback_penalty:{penalty:0.####}");
        }

        var keywordHits = CountKeywordHits(candidate, searchTokens);
        if (keywordHits > 0)
        {
            sessionBoost += keywordHits * options.SearchKeywordBoost;
            signals.Add($"recent_search_match:{keywordHits}");
        }

        if (clickedProductIds.Contains(candidate.ProductId))
        {
            sessionBoost += options.RecentClickedProductBoost;
            signals.Add("recent_product_click");
        }

        if (candidate.PrimarySellerId is > 0 && clickedSellerIds.Contains(candidate.PrimarySellerId.Value))
        {
            sessionBoost += options.RecentClickedSellerBoost;
            signals.Add("recent_seller_click");
        }

        var normalizedCategory = NormalizeText(candidate.CategoryName);
        if (normalizedCategory.Length > 0 && clickedCategories.Contains(normalizedCategory))
        {
            sessionBoost += options.RecentClickedCategoryBoost;
            signals.Add("recent_category_click");
        }

        if (sessionBoost > 0d)
        {
            var positionBoostFactor = ResolvePositionBoostFactor(originalPosition, options);
            score += sessionBoost * positionBoostFactor;
            signals.Add($"position_session_boost_factor:{positionBoostFactor:0.####}");
        }

        if (candidate.AvailableStock > 0)
        {
            score += options.InStockBoost;
            signals.Add("in_stock");
        }
        else
        {
            score -= options.OutOfStockPenalty;
            signals.Add("out_of_stock_penalty");
        }

        if (IsNearbyShop(candidate, context, options))
        {
            score += options.NearbyShopBoost;
            signals.Add("nearby_shop");
        }

        return CreateResult(candidate, Math.Round(score, 4), signals);
    }

    private async Task<IReadOnlyDictionary<int, RecommendationNegativeFeedbackMetric>> LoadNegativeFeedbackScoresAsync(
        int userId,
        IReadOnlyList<SessionAwareRecommendationCandidate> candidates,
        SessionAwareRecommendationOptions options,
        CancellationToken cancellationToken)
    {
        var productIds = candidates
            .Where(candidate => candidate.ProductId > 0)
            .Take(Math.Clamp(options.MaxCandidates, 1, 2_000))
            .Select(candidate => candidate.ProductId)
            .Distinct()
            .ToArray();
        if (productIds.Length == 0)
        {
            return new Dictionary<int, RecommendationNegativeFeedbackMetric>();
        }

        var toDateUtc = DateTime.UtcNow;
        var lookbackDays = Math.Clamp(options.NegativeFeedbackLookbackDays, 1, 30);
        var fromDateUtc = toDateUtc.AddDays(-lookbackDays);
        return await _metricsClient.GetNegativeFeedbackAsync(
            userId,
            productIds,
            fromDateUtc,
            toDateUtc,
            cancellationToken);
    }

    private async Task<IReadOnlyDictionary<int, NormalizedObjectiveScores>> LoadNormalizedObjectiveScoresAsync(
        IReadOnlyList<SessionAwareRecommendationCandidate> candidates,
        SessionAwareRecommendationOptions options,
        CancellationToken cancellationToken)
    {
        var productIds = candidates
            .Where(candidate => candidate.ProductId > 0)
            .Take(Math.Clamp(options.MaxCandidates, 1, 2_000))
            .Select(candidate => candidate.ProductId)
            .Distinct()
            .ToArray();
        if (productIds.Length == 0)
        {
            return new Dictionary<int, NormalizedObjectiveScores>();
        }

        var toDateUtc = DateTime.UtcNow;
        var lookbackDays = Math.Clamp(options.ObjectiveMetricsLookbackDays, 1, 30);
        var fromDateUtc = toDateUtc.AddDays(-lookbackDays);
        var rawMetrics = await _metricsClient.GetObjectiveMetricsAsync(
            productIds,
            fromDateUtc,
            toDateUtc,
            cancellationToken);

        return NormalizeObjectiveMetrics(rawMetrics);
    }

    private static IReadOnlyDictionary<int, NormalizedObjectiveScores> NormalizeObjectiveMetrics(
        IReadOnlyDictionary<int, RecommendationObjectiveMetric> metrics)
    {
        if (metrics.Count == 0)
        {
            return new Dictionary<int, NormalizedObjectiveScores>();
        }

        var eligible = metrics.Values
            .Where(metric => string.Equals(metric.Status, "ok", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (eligible.Length == 0)
        {
            return new Dictionary<int, NormalizedObjectiveScores>();
        }

        var ctrRange = BuildRange(eligible.Select(metric => metric.Ctr));
        var addToCartRange = BuildRange(eligible.Select(metric => metric.AddToCartRate));
        var purchaseRange = BuildRange(eligible.Select(metric => metric.PurchaseRate));
        var revenueRange = BuildRange(eligible.Select(metric => metric.RevenuePerImpression.HasValue
            ? (double?)metric.RevenuePerImpression.Value
            : null));

        return eligible
            .Select(metric => new
            {
                metric.ProductId,
                Scores = new NormalizedObjectiveScores(
                    NormalizeMetric(metric.Ctr, ctrRange),
                    NormalizeMetric(metric.AddToCartRate, addToCartRange),
                    NormalizeMetric(metric.PurchaseRate, purchaseRange),
                    NormalizeMetric(
                        metric.RevenuePerImpression.HasValue
                            ? (double?)metric.RevenuePerImpression.Value
                            : null,
                        revenueRange))
            })
            .Where(item => item.Scores.HasAnyMetric)
            .ToDictionary(item => item.ProductId, item => item.Scores);
    }

    private static IReadOnlyList<SessionAwareRerankedProduct> CreateBaseScoreResults(
        IEnumerable<SessionAwareRecommendationCandidate> candidates)
    {
        return candidates
            .Where(item => item.ProductId > 0)
            .OrderByDescending(item => item.BaseScore)
            .ThenBy(item => item.ProductId)
            .Select(item => CreateResult(item, item.BaseScore, Array.Empty<string>()))
            .ToArray();
    }

    private static bool HasActionableSessionMatch(
        IReadOnlyList<SessionAwareRecommendationCandidate> candidates,
        HashSet<string> searchTokens,
        HashSet<int> clickedProductIds,
        HashSet<int> clickedSellerIds,
        HashSet<string> clickedCategories)
    {
        if (candidates.Count == 0
            || (searchTokens.Count == 0
                && clickedProductIds.Count == 0
                && clickedSellerIds.Count == 0
                && clickedCategories.Count == 0))
        {
            return false;
        }

        foreach (var candidate in candidates)
        {
            if (CountKeywordHits(candidate, searchTokens) > 0)
            {
                return true;
            }

            if (clickedProductIds.Contains(candidate.ProductId))
            {
                return true;
            }

            if (candidate.PrimarySellerId is > 0 && clickedSellerIds.Contains(candidate.PrimarySellerId.Value))
            {
                return true;
            }

            var normalizedCategory = NormalizeText(candidate.CategoryName);
            if (normalizedCategory.Length > 0 && clickedCategories.Contains(normalizedCategory))
            {
                return true;
            }
        }

        return false;
    }

    private static double CalculateObjectiveScore(
        NormalizedObjectiveScores scores,
        SessionAwareRecommendationOptions options,
        List<string> signals)
    {
        var weightedScore = 0d;
        var weightSum = 0d;
        AddWeightedObjectiveSignal(
            scores.CtrScore,
            options.CtrWeight,
            "objective_ctr",
            signals,
            ref weightedScore,
            ref weightSum);
        AddWeightedObjectiveSignal(
            scores.AddToCartScore,
            options.AddToCartWeight,
            "objective_add_to_cart",
            signals,
            ref weightedScore,
            ref weightSum);
        AddWeightedObjectiveSignal(
            scores.PurchaseScore,
            options.PurchaseWeight,
            "objective_purchase",
            signals,
            ref weightedScore,
            ref weightSum);
        AddWeightedObjectiveSignal(
            scores.RevenueScore,
            options.RevenueWeight,
            "objective_revenue",
            signals,
            ref weightedScore,
            ref weightSum);

        if (weightSum <= 0d)
        {
            return 0d;
        }

        var normalizedScore = weightedScore / weightSum;
        var scaledScore = normalizedScore * ResolveObjectiveScoreScale(options);
        if (scaledScore > 0d)
        {
            signals.Add($"objective_score:{scaledScore:0.####}");
        }

        return scaledScore;
    }

    private static void AddWeightedObjectiveSignal(
        double? normalizedValue,
        double weight,
        string signalName,
        List<string> signals,
        ref double weightedScore,
        ref double weightSum)
    {
        if (!normalizedValue.HasValue || !double.IsFinite(normalizedValue.Value) || !double.IsFinite(weight))
        {
            return;
        }

        var clampedWeight = Math.Clamp(weight, 0d, 1d);
        if (clampedWeight <= 0d)
        {
            return;
        }

        var clampedValue = Math.Clamp(normalizedValue.Value, 0d, 1d);
        weightedScore += clampedValue * clampedWeight;
        weightSum += clampedWeight;
        if (clampedValue > 0d)
        {
            signals.Add($"{signalName}:{clampedValue:0.####}");
        }
    }

    private static double ResolveObjectiveScoreScale(SessionAwareRecommendationOptions options)
    {
        return double.IsFinite(options.ObjectiveScoreScale) && options.ObjectiveScoreScale > 0d
            ? options.ObjectiveScoreScale
            : 62d;
    }

    private static double ResolveNegativeFeedbackPenaltyScale(SessionAwareRecommendationOptions options)
    {
        return double.IsFinite(options.NegativeFeedbackPenaltyScale) && options.NegativeFeedbackPenaltyScale > 0d
            ? Math.Clamp(options.NegativeFeedbackPenaltyScale, 1d, 100d)
            : 24d;
    }

    private static ObjectiveMetricRange BuildRange(IEnumerable<double?> values)
    {
        var normalizedValues = values
            .Where(value => value.HasValue && double.IsFinite(value.Value))
            .Select(value => value!.Value)
            .ToArray();
        return normalizedValues.Length == 0
            ? ObjectiveMetricRange.Empty
            : new ObjectiveMetricRange(normalizedValues.Min(), normalizedValues.Max(), true);
    }

    private static double? NormalizeMetric(double? value, ObjectiveMetricRange range)
    {
        if (!value.HasValue || !double.IsFinite(value.Value) || !range.HasValues)
        {
            return null;
        }

        if (range.Max <= range.Min)
        {
            return value.Value > 0d ? 1d : 0d;
        }

        return Math.Clamp((value.Value - range.Min) / (range.Max - range.Min), 0d, 1d);
    }

    private static double ResolvePositionBoostFactor(
        int originalPosition,
        SessionAwareRecommendationOptions options)
    {
        var rawFactor = originalPosition is > 0 and <= 2
            ? options.TopPositionBoostFactor
            : options.MidPositionBoostFactor;

        return double.IsFinite(rawFactor)
            ? Math.Clamp(rawFactor, 0d, 3d)
            : 1d;
    }

    private static SessionAwareRerankedProduct CreateResult(
        SessionAwareRecommendationCandidate candidate,
        double finalScore,
        IReadOnlyList<string> signals)
    {
        return new SessionAwareRerankedProduct
        {
            Product = candidate,
            BaseScore = candidate.BaseScore,
            FinalScore = finalScore,
            AppliedSignals = signals
        };
    }

    private static HashSet<string> BuildRecentSearchTokenSet(
        SessionRecommendationSignals sessionSignals,
        SessionAwareRecommendationOptions options)
    {
        var tokens = new HashSet<string>(StringComparer.Ordinal);
        foreach (var search in sessionSignals.RecentSearches.Take(options.MaxRecentSearches))
        {
            foreach (var token in Tokenize(search.Keyword))
            {
                if (tokens.Count >= options.MaxSearchTokens)
                {
                    return tokens;
                }

                tokens.Add(token);
            }
        }

        return tokens;
    }

    private static int CountKeywordHits(
        SessionAwareRecommendationCandidate candidate,
        HashSet<string> searchTokens)
    {
        if (searchTokens.Count == 0)
        {
            return 0;
        }

        var candidateTokens = new HashSet<string>(StringComparer.Ordinal);
        AddTokens(candidateTokens, candidate.ProductName);
        AddTokens(candidateTokens, candidate.CategoryName);
        AddTokens(candidateTokens, candidate.Origin);
        AddTokens(candidateTokens, candidate.Standard);
        AddTokens(candidateTokens, candidate.SellerShopName);
        foreach (var term in candidate.SearchableTerms)
        {
            AddTokens(candidateTokens, term);
        }

        return searchTokens.Count(candidateTokens.Contains);
    }

    private static bool IsNearbyShop(
        SessionAwareRecommendationCandidate candidate,
        SessionAwareRecommendationContext context,
        SessionAwareRecommendationOptions options)
    {
        if (candidate.IsNearbyShop == true)
        {
            return true;
        }

        if (candidate.SellerDistanceKm is >= 0 && candidate.SellerDistanceKm <= options.NearbyShopRadiusKm)
        {
            return true;
        }

        if (candidate.PrimarySellerId is > 0 && context.NearbySellerIds.Contains(candidate.PrimarySellerId.Value))
        {
            return true;
        }

        var address = NormalizeText(candidate.SellerAddressSummary);
        return address.Length > 0
            && (ContainsLocation(address, context.DeliveryWard)
                || ContainsLocation(address, context.DeliveryDistrict)
                || ContainsLocation(address, context.DeliveryProvince));
    }

    private static bool ContainsLocation(string normalizedAddress, string? location)
    {
        var normalizedLocation = NormalizeText(location);
        return normalizedLocation.Length > 0 && normalizedAddress.Contains(normalizedLocation, StringComparison.Ordinal);
    }

    private static IEnumerable<JsonElement> EnumerateArray(JsonElement element)
    {
        return element.ValueKind == JsonValueKind.Array
            ? element.EnumerateArray()
            : Array.Empty<JsonElement>();
    }

    private static bool TryGetProperty(JsonElement element, out JsonElement value, params string[] names)
    {
        foreach (var name in names)
        {
            if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out value))
            {
                return true;
            }
        }

        value = default;
        return false;
    }

    private static string? ReadString(JsonElement element, params string[] names)
    {
        return TryGetProperty(element, out var value, names) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static int? ReadInt(JsonElement element, params string[] names)
    {
        if (!TryGetProperty(element, out var value, names))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
        {
            return number;
        }

        return value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out number)
            ? number
            : null;
    }

    private static void AddTokens(HashSet<string> destination, string? value)
    {
        foreach (var token in Tokenize(value))
        {
            destination.Add(token);
        }
    }

    private static IEnumerable<string> Tokenize(string? value)
    {
        var normalized = NormalizeText(value);
        if (normalized.Length == 0)
        {
            yield break;
        }

        var builder = new StringBuilder();
        foreach (var character in normalized)
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(character);
                continue;
            }

            if (builder.Length >= 2)
            {
                yield return builder.ToString();
            }

            builder.Clear();
        }

        if (builder.Length >= 2)
        {
            yield return builder.ToString();
        }
    }

    private static string NormalizeText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        foreach (var character in normalized)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(character);
            if (category == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            builder.Append(character switch
            {
                'đ' => 'd',
                'Đ' => 'd',
                _ => character
            });
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    private sealed record ObjectiveMetricRange(double Min, double Max, bool HasValues)
    {
        public static readonly ObjectiveMetricRange Empty = new(0d, 0d, false);
    }

    private sealed record NormalizedObjectiveScores(
        double? CtrScore,
        double? AddToCartScore,
        double? PurchaseScore,
        double? RevenueScore)
    {
        public bool HasAnyMetric =>
            CtrScore.HasValue
            || AddToCartScore.HasValue
            || PurchaseScore.HasValue
            || RevenueScore.HasValue;

        public bool HasPositiveSignal =>
            CtrScore is > 0d
            || AddToCartScore is > 0d
            || PurchaseScore is > 0d
            || RevenueScore is > 0d;
    }

    private sealed record SessionRecommendationSignals(
        IReadOnlyList<SessionSearchSignal> RecentSearches,
        IReadOnlyList<SessionClickSignal> RecentClicks)
    {
        public static readonly SessionRecommendationSignals Empty = new(
            Array.Empty<SessionSearchSignal>(),
            Array.Empty<SessionClickSignal>());
    }

    private sealed record SessionSearchSignal(string Keyword);

    private sealed record SessionClickSignal(int ProductId, int? SellerId, string? CategoryName);
}
