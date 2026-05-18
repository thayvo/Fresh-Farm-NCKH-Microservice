using System.Globalization;
using System.Text;
using System.Text.Json;
using FreshFarm.Web.Bff.Options;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;

namespace FreshFarm.Web.Bff.Services;

public sealed class SessionSignalService : ISessionSignalService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IDistributedCache _cache;
    private readonly IOptionsMonitor<SessionAwareRecommendationOptions> _options;
    private readonly ILogger<SessionSignalService> _logger;

    public SessionSignalService(
        IDistributedCache cache,
        IOptionsMonitor<SessionAwareRecommendationOptions> options,
        ILogger<SessionSignalService> logger)
    {
        _cache = cache;
        _options = options;
        _logger = logger;
    }

    public async Task TrackSearchAsync(
        int userId,
        string keyword,
        CancellationToken cancellationToken = default)
    {
        var normalizedKeyword = keyword.Trim();
        if (userId <= 0 || string.IsNullOrWhiteSpace(normalizedKeyword))
        {
            return;
        }

        try
        {
            var options = _options.CurrentValue;
            var envelope = await LoadSignalsAsync(userId, options, cancellationToken);
            envelope.RecentSearches.RemoveAll(item =>
                string.Equals(NormalizeKeywordKey(item.Keyword), NormalizeKeywordKey(normalizedKeyword), StringComparison.Ordinal));
            envelope.RecentSearches.Insert(0, new SessionSearchSignal(normalizedKeyword, DateTime.UtcNow));
            envelope.RecentSearches = envelope.RecentSearches
                .Where(item => !string.IsNullOrWhiteSpace(item.Keyword))
                .OrderByDescending(item => item.Timestamp)
                .Take(Math.Clamp(options.MaxRecentSearches, 1, 100))
                .ToList();

            await StoreSignalsAsync(userId, envelope, options, cancellationToken);
        }
        catch (Exception ex)
        {
            LogCacheFailure(ex, "Cannot track recommendation search session signal. UserId={UserId}", userId);
        }
    }

    public async Task TrackClickAsync(
        int userId,
        int productId,
        int sellerId,
        string? categoryName,
        CancellationToken cancellationToken = default)
    {
        if (userId <= 0 || productId <= 0)
        {
            return;
        }

        try
        {
            var options = _options.CurrentValue;
            var envelope = await LoadSignalsAsync(userId, options, cancellationToken);
            envelope.RecentClicks.RemoveAll(item => item.ProductId == productId);
            envelope.RecentClicks.Insert(0, new SessionClickSignal(
                productId,
                Math.Max(0, sellerId),
                categoryName?.Trim() ?? string.Empty,
                DateTime.UtcNow));
            envelope.RecentClicks = envelope.RecentClicks
                .Where(item => item.ProductId > 0)
                .OrderByDescending(item => item.Timestamp)
                .Take(Math.Clamp(options.MaxRecentClicks, 1, 200))
                .ToList();

            await StoreSignalsAsync(userId, envelope, options, cancellationToken);
        }
        catch (Exception ex)
        {
            LogCacheFailure(
                ex,
                "Cannot track recommendation click session signal. UserId={UserId}, ProductId={ProductId}",
                userId,
                productId);
        }
    }

    private async Task<SessionSignalEnvelope> LoadSignalsAsync(
        int userId,
        SessionAwareRecommendationOptions options,
        CancellationToken cancellationToken)
    {
        var key = BuildKey(userId, options);
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
            LogCacheFailure(ex, "Cannot read recommendation session signal cache. UserId={UserId}, Key={RedisKey}", userId, key);
            return new SessionSignalEnvelope();
        }

        if (string.IsNullOrWhiteSpace(payload))
        {
            return new SessionSignalEnvelope();
        }

        try
        {
            return JsonSerializer.Deserialize<SessionSignalEnvelope>(payload, JsonOptions) ?? new SessionSignalEnvelope();
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Invalid recommendation session signal cache payload. UserId={UserId}, Key={RedisKey}", userId, key);
            return new SessionSignalEnvelope();
        }
    }

    private async Task StoreSignalsAsync(
        int userId,
        SessionSignalEnvelope envelope,
        SessionAwareRecommendationOptions options,
        CancellationToken cancellationToken)
    {
        var key = BuildKey(userId, options);
        var payload = JsonSerializer.Serialize(envelope, JsonOptions);
        var timeout = GetSessionCacheReadTimeout(options);
        using var timeoutToken = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutToken.CancelAfter(timeout);
        await _cache
            .SetStringAsync(
                key,
                payload,
                new DistributedCacheEntryOptions
                {
                    SlidingExpiration = TimeSpan.FromMinutes(Math.Clamp(options.SessionSignalTtlMinutes, 1, 24 * 60))
                },
                timeoutToken.Token)
            .WaitAsync(timeout, cancellationToken);
    }

    private static string BuildKey(int userId, SessionAwareRecommendationOptions options)
    {
        return $"{options.RedisKeyPrefix}{userId}";
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

    private static string NormalizeKeywordKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        foreach (var character in normalized)
        {
            if (char.IsWhiteSpace(character))
            {
                builder.Append(' ');
                continue;
            }

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

        return string.Join(
            ' ',
            builder
                .ToString()
                .Normalize(NormalizationForm.FormC)
                .Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private sealed record SessionSignalEnvelope
    {
        public List<SessionSearchSignal> RecentSearches { get; set; } = new();

        public List<SessionClickSignal> RecentClicks { get; set; } = new();
    }

    private sealed record SessionSearchSignal(string Keyword, DateTime Timestamp);

    private sealed record SessionClickSignal(int ProductId, int SellerId, string CategoryName, DateTime Timestamp);
}
