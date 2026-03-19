using System.Collections.Concurrent;

namespace FreshFarm.Web.Bff.Services;

public interface IRateLimitTelemetryService
{
    void RecordRejectedRequest(
        string endpoint,
        string method,
        string path,
        string client,
        string? traceId,
        string? retryAfter,
        string? userAgent);

    RateLimitTelemetrySnapshot CreateSnapshot();
}

public sealed class RateLimitTelemetryService : IRateLimitTelemetryService
{
    private const int MaxRecentEvents = 200;

    private readonly ConcurrentQueue<RateLimitRejectionEvent> _recentEvents = new();
    private readonly ConcurrentDictionary<string, int> _pathCounts = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, int> _endpointCounts = new(StringComparer.OrdinalIgnoreCase);

    public void RecordRejectedRequest(
        string endpoint,
        string method,
        string path,
        string client,
        string? traceId,
        string? retryAfter,
        string? userAgent)
    {
        var now = DateTimeOffset.UtcNow;
        var safeEndpoint = string.IsNullOrWhiteSpace(endpoint) ? "unknown-endpoint" : endpoint.Trim();
        var safeMethod = string.IsNullOrWhiteSpace(method) ? "UNKNOWN" : method.Trim().ToUpperInvariant();
        var safePath = string.IsNullOrWhiteSpace(path) ? "/" : path.Trim();
        var safeClient = string.IsNullOrWhiteSpace(client) ? "anonymous" : client.Trim();

        _pathCounts.AddOrUpdate(safePath, 1, static (_, current) => current + 1);
        _endpointCounts.AddOrUpdate(safeEndpoint, 1, static (_, current) => current + 1);

        _recentEvents.Enqueue(new RateLimitRejectionEvent
        {
            RejectedAtUtc = now,
            Endpoint = safeEndpoint,
            Method = safeMethod,
            Path = safePath,
            Client = safeClient,
            TraceId = traceId?.Trim(),
            RetryAfter = retryAfter?.Trim(),
            UserAgent = userAgent?.Trim()
        });

        while (_recentEvents.Count > MaxRecentEvents && _recentEvents.TryDequeue(out _))
        {
        }
    }

    public RateLimitTelemetrySnapshot CreateSnapshot()
    {
        var recentEvents = _recentEvents.ToArray()
            .OrderByDescending(x => x.RejectedAtUtc)
            .ToList();
        var last10MinutesThreshold = DateTimeOffset.UtcNow.AddMinutes(-10);

        return new RateLimitTelemetrySnapshot
        {
            GeneratedAtUtc = DateTimeOffset.UtcNow,
            TotalRejectedRequests = _pathCounts.Values.Sum(),
            RejectedLast10Minutes = recentEvents.Count(x => x.RejectedAtUtc >= last10MinutesThreshold),
            UniqueClientsInRecentBuffer = recentEvents
                .Select(x => x.Client)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count(),
            TopPaths = _pathCounts
                .OrderByDescending(x => x.Value)
                .ThenBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
                .Take(10)
                .Select(x => new RateLimitAggregateRow
                {
                    Key = x.Key,
                    Count = x.Value
                })
                .ToList(),
            TopEndpoints = _endpointCounts
                .OrderByDescending(x => x.Value)
                .ThenBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
                .Take(10)
                .Select(x => new RateLimitAggregateRow
                {
                    Key = x.Key,
                    Count = x.Value
                })
                .ToList(),
            RecentEvents = recentEvents
                .Take(20)
                .ToList()
        };
    }
}

public sealed class RateLimitTelemetrySnapshot
{
    public DateTimeOffset GeneratedAtUtc { get; set; }

    public int TotalRejectedRequests { get; set; }

    public int RejectedLast10Minutes { get; set; }

    public int UniqueClientsInRecentBuffer { get; set; }

    public List<RateLimitAggregateRow> TopPaths { get; set; } = new();

    public List<RateLimitAggregateRow> TopEndpoints { get; set; } = new();

    public List<RateLimitRejectionEvent> RecentEvents { get; set; } = new();
}

public sealed class RateLimitAggregateRow
{
    public string Key { get; set; } = string.Empty;

    public int Count { get; set; }
}

public sealed class RateLimitRejectionEvent
{
    public DateTimeOffset RejectedAtUtc { get; set; }

    public string Endpoint { get; set; } = string.Empty;

    public string Method { get; set; } = string.Empty;

    public string Path { get; set; } = string.Empty;

    public string Client { get; set; } = string.Empty;

    public string? TraceId { get; set; }

    public string? RetryAfter { get; set; }

    public string? UserAgent { get; set; }
}
