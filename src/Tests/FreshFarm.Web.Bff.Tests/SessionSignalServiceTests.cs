using System.Text;
using System.Text.Json;
using FreshFarm.Web.Bff.Options;
using FreshFarm.Web.Bff.Services;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace FreshFarm.Web.Bff.Tests;

public sealed class SessionSignalServiceTests
{
    private const int UserId = 17;
    private const string CacheKey = "recommendation:session:17";

    [Fact]
    public async Task TrackSearchAsync_DedupesAndKeepsNewestTenSearches()
    {
        var cache = new RecordingDistributedCache();
        var service = CreateService(cache);

        for (var index = 0; index < 12; index++)
        {
            await service.TrackSearchAsync(UserId, $"Rau {index}");
        }

        await service.TrackSearchAsync(UserId, "rau 5");

        using var document = JsonDocument.Parse(cache.GetString(CacheKey)!);
        var searches = document.RootElement.GetProperty("recentSearches").EnumerateArray().ToArray();

        Assert.Equal(10, searches.Length);
        Assert.Equal("rau 5", searches[0].GetProperty("keyword").GetString());
        Assert.Equal(
            searches.Length,
            searches.Select(item => item.GetProperty("keyword").GetString()).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(TimeSpan.FromMinutes(30), cache.Options[CacheKey].SlidingExpiration);
    }

    [Fact]
    public async Task TrackClickAsync_DedupesByProductAndKeepsNewestTwentyClicks()
    {
        var cache = new RecordingDistributedCache();
        var service = CreateService(cache);

        for (var productId = 1; productId <= 22; productId++)
        {
            await service.TrackClickAsync(UserId, productId, sellerId: productId + 100, categoryName: "Rau la");
        }

        await service.TrackClickAsync(UserId, productId: 4, sellerId: 41, categoryName: "Rau lá");

        using var document = JsonDocument.Parse(cache.GetString(CacheKey)!);
        var clicks = document.RootElement.GetProperty("recentClicks").EnumerateArray().ToArray();

        Assert.Equal(20, clicks.Length);
        Assert.Equal(4, clicks[0].GetProperty("productId").GetInt32());
        Assert.Equal(41, clicks[0].GetProperty("sellerId").GetInt32());
        Assert.Equal("Rau lá", clicks[0].GetProperty("categoryName").GetString());
        Assert.Equal(
            clicks.Length,
            clicks.Select(item => item.GetProperty("productId").GetInt32()).Distinct().Count());
    }

    private static SessionSignalService CreateService(RecordingDistributedCache cache)
    {
        return new SessionSignalService(
            cache,
            new StaticOptionsMonitor<SessionAwareRecommendationOptions>(new SessionAwareRecommendationOptions()),
            NullLogger<SessionSignalService>.Instance);
    }

    private sealed class StaticOptionsMonitor<T> : IOptionsMonitor<T>
    {
        public StaticOptionsMonitor(T currentValue)
        {
            CurrentValue = currentValue;
        }

        public T CurrentValue { get; }

        public T Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }

    private sealed class RecordingDistributedCache : IDistributedCache
    {
        private readonly Dictionary<string, byte[]> _values = new(StringComparer.Ordinal);

        public Dictionary<string, DistributedCacheEntryOptions> Options { get; } = new(StringComparer.Ordinal);

        public byte[]? Get(string key)
        {
            return _values.TryGetValue(key, out var value) ? value : null;
        }

        public Task<byte[]?> GetAsync(string key, CancellationToken token = default)
        {
            return Task.FromResult(Get(key));
        }

        public string? GetString(string key)
        {
            return Get(key) is { } value ? Encoding.UTF8.GetString(value) : null;
        }

        public void Set(string key, byte[] value, DistributedCacheEntryOptions options)
        {
            _values[key] = value;
            Options[key] = options;
        }

        public Task SetAsync(
            string key,
            byte[] value,
            DistributedCacheEntryOptions options,
            CancellationToken token = default)
        {
            Set(key, value, options);
            return Task.CompletedTask;
        }

        public void Refresh(string key)
        {
        }

        public Task RefreshAsync(string key, CancellationToken token = default)
        {
            return Task.CompletedTask;
        }

        public void Remove(string key)
        {
            _values.Remove(key);
            Options.Remove(key);
        }

        public Task RemoveAsync(string key, CancellationToken token = default)
        {
            Remove(key);
            return Task.CompletedTask;
        }
    }
}
