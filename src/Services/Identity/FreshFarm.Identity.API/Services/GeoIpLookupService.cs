using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using FreshFarm.Identity.Api.Options;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace FreshFarm.Identity.Api.Services;

public interface IGeoIpLookupService
{
    Task<GeoIpLookupResult> ResolveAsync(string? remoteIp, string? forwardedFor, CancellationToken cancellationToken = default);
}

public sealed record GeoIpLookupResult(
    string? EffectiveIp,
    string? CountryCode,
    string? CountryName,
    string? RegionName,
    string? CityName);

public sealed class GeoIpLookupService : IGeoIpLookupService
{
    private readonly HttpClient _httpClient;
    private readonly IMemoryCache _cache;
    private readonly ILogger<GeoIpLookupService> _logger;
    private readonly GeoIpOptions _options;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public GeoIpLookupService(
        HttpClient httpClient,
        IMemoryCache cache,
        IOptions<GeoIpOptions> options,
        ILogger<GeoIpLookupService> logger)
    {
        _httpClient = httpClient;
        _cache = cache;
        _logger = logger;
        _options = options.Value;
    }

    public async Task<GeoIpLookupResult> ResolveAsync(string? remoteIp, string? forwardedFor, CancellationToken cancellationToken = default)
    {
        var effectiveIp = SelectBestIp(remoteIp, forwardedFor);
        if (string.IsNullOrWhiteSpace(effectiveIp))
        {
            return new GeoIpLookupResult(null, null, null, null, null);
        }

        if (_cache.TryGetValue(GetCacheKey(effectiveIp), out GeoIpLookupResult? cached) && cached is not null)
        {
            return cached with { EffectiveIp = effectiveIp };
        }

        try
        {
            using var response = await _httpClient.GetAsync(Uri.EscapeDataString(effectiveIp), cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogDebug("GeoIP lookup that bai cho IP {Ip}. StatusCode={StatusCode}", effectiveIp, response.StatusCode);
                var fallback = new GeoIpLookupResult(effectiveIp, null, null, null, null);
                CacheResult(effectiveIp, fallback);
                return fallback;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var payload = await JsonSerializer.DeserializeAsync<IpWhoIsResponse>(stream, JsonOptions, cancellationToken);
            if (payload is null || payload.Success == false)
            {
                _logger.LogDebug("GeoIP lookup khong hop le cho IP {Ip}. Message={Message}", effectiveIp, payload?.Message);
                var fallback = new GeoIpLookupResult(effectiveIp, null, null, null, null);
                CacheResult(effectiveIp, fallback);
                return fallback;
            }

            var result = new GeoIpLookupResult(
                effectiveIp,
                TrimToLength(payload.CountryCode?.ToUpperInvariant(), 8),
                TrimToLength(payload.Country, 120),
                TrimToLength(payload.Region, 120),
                TrimToLength(payload.City, 120));

            CacheResult(effectiveIp, result);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Khong the tra cuu GeoIP cho IP {Ip}.", effectiveIp);
            var fallback = new GeoIpLookupResult(effectiveIp, null, null, null, null);
            CacheResult(effectiveIp, fallback);
            return fallback;
        }
    }

    private void CacheResult(string ip, GeoIpLookupResult result)
    {
        _cache.Set(GetCacheKey(ip), result, TimeSpan.FromHours(Math.Max(1, _options.CacheHours)));
    }

    private static string GetCacheKey(string ip) => "geoip:" + ip;

    private static string? SelectBestIp(string? remoteIp, string? forwardedFor)
    {
        foreach (var candidate in EnumerateForwardedCandidates(forwardedFor))
        {
            if (IsPublicIp(candidate))
            {
                return candidate;
            }
        }

        if (IsPublicIp(remoteIp))
        {
            return remoteIp;
        }

        foreach (var candidate in EnumerateForwardedCandidates(forwardedFor))
        {
            if (!string.IsNullOrWhiteSpace(candidate))
            {
                return candidate;
            }
        }

        return string.IsNullOrWhiteSpace(remoteIp) ? null : remoteIp;
    }

    private static IEnumerable<string> EnumerateForwardedCandidates(string? forwardedFor)
    {
        if (string.IsNullOrWhiteSpace(forwardedFor))
        {
            yield break;
        }

        foreach (var part in forwardedFor.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!string.IsNullOrWhiteSpace(part))
            {
                yield return part;
            }
        }
    }

    private static bool IsPublicIp(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || !IPAddress.TryParse(value, out var ip))
        {
            return false;
        }

        if (IPAddress.IsLoopback(ip))
        {
            return false;
        }

        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = ip.GetAddressBytes();
            if (bytes[0] == 10 ||
                (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) ||
                (bytes[0] == 192 && bytes[1] == 168) ||
                (bytes[0] == 169 && bytes[1] == 254) ||
                bytes[0] == 127)
            {
                return false;
            }
        }

        if (ip.AddressFamily == AddressFamily.InterNetworkV6 &&
            (ip.IsIPv6LinkLocal || ip.IsIPv6Multicast || ip.IsIPv6SiteLocal || ip.IsIPv6Teredo))
        {
            return false;
        }

        return true;
    }

    private static string? TrimToLength(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }

    private sealed class IpWhoIsResponse
    {
        public bool Success { get; set; }
        public string? Message { get; set; }
        public string? Country { get; set; }
        public string? CountryCode { get; set; }
        public string? Region { get; set; }
        public string? City { get; set; }
    }
}
