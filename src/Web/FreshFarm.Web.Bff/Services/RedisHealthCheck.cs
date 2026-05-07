using FreshFarm.Web.Bff.Options;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace FreshFarm.Web.Bff.Services;

public sealed class RedisSessionCacheState
{
    public RedisSessionCacheState(
        string configuredProvider,
        bool redisConfigured,
        bool usingRedis,
        string effectiveProvider,
        string? failureReason)
    {
        ConfiguredProvider = configuredProvider;
        RedisConfigured = redisConfigured;
        UsingRedis = usingRedis;
        EffectiveProvider = effectiveProvider;
        FailureReason = failureReason;
    }

    public string ConfiguredProvider { get; }

    public bool RedisConfigured { get; }

    public bool UsingRedis { get; }

    public string EffectiveProvider { get; }

    public string? FailureReason { get; }
}

public sealed class RedisHealthCheck : IHealthCheck
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromMilliseconds(100);

    private readonly RedisSessionCacheState _cacheState;
    private readonly IConnectionMultiplexer? _redisConnection;
    private readonly IOptionsMonitor<SessionStoreOptions> _sessionStoreOptions;

    public RedisHealthCheck(
        RedisSessionCacheState cacheState,
        IEnumerable<IConnectionMultiplexer> redisConnections,
        IOptionsMonitor<SessionStoreOptions> sessionStoreOptions)
    {
        _cacheState = cacheState;
        _redisConnection = redisConnections.FirstOrDefault();
        _sessionStoreOptions = sessionStoreOptions;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var data = new Dictionary<string, object>
        {
            ["configuredProvider"] = _cacheState.ConfiguredProvider,
            ["effectiveProvider"] = _cacheState.EffectiveProvider,
            ["redisConfigured"] = _cacheState.RedisConfigured,
            ["usingRedis"] = _cacheState.UsingRedis,
            ["instanceName"] = _sessionStoreOptions.CurrentValue.InstanceName
        };

        if (!string.IsNullOrWhiteSpace(_cacheState.FailureReason))
        {
            data["failureReason"] = _cacheState.FailureReason!;
        }

        if (!_cacheState.RedisConfigured)
        {
            return HealthCheckResult.Unhealthy(
                "Redis is not configured for this BFF instance.",
                data: data);
        }

        if (!_cacheState.UsingRedis || _redisConnection is null)
        {
            return HealthCheckResult.Unhealthy(
                "Redis was unavailable at startup; in-memory cache fallback is active.",
                data: data);
        }

        if (!_redisConnection.IsConnected)
        {
            return HealthCheckResult.Unhealthy(
                "Redis connection is not connected.",
                data: data);
        }

        try
        {
            var latency = await _redisConnection
                .GetDatabase()
                .PingAsync()
                .WaitAsync(ProbeTimeout, cancellationToken);
            data["latencyMs"] = Math.Round(latency.TotalMilliseconds, 2);

            return HealthCheckResult.Healthy("Redis is reachable.", data);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            return HealthCheckResult.Unhealthy(
                "Redis health probe failed.",
                exception: ex,
                data: data);
        }
    }
}
