using System.Security.Cryptography;
using System.Text;
using FreshFarm.Web.Bff.Options;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace FreshFarm.Web.Bff.Services;

public interface IRecommendationExperimentService
{
    string ResolveGroup(HttpContext httpContext, int? userId);
}

public static class RecommendationExperimentGroups
{
    public const string MlOnly = "A";
    public const string SessionRerank = "B";

    public static bool IsSessionRerankEnabled(string? group)
    {
        return string.Equals(group, SessionRerank, StringComparison.OrdinalIgnoreCase);
    }

    public static string Normalize(string? group)
    {
        return IsSessionRerankEnabled(group) ? SessionRerank : MlOnly;
    }
}

public sealed class RecommendationExperimentService : IRecommendationExperimentService
{
    public const string CookieName = "FreshFarm.RecommendationExperimentGroup";

    private const string ExperimentKey = "home_recommendation_strategy_v1";
    private static readonly TimeSpan CookieLifetime = TimeSpan.FromDays(180);
    private readonly IOptionsMonitor<RecommendationExperimentOptions> _options;

    public RecommendationExperimentService()
        : this(new StaticRecommendationExperimentOptionsMonitor(new RecommendationExperimentOptions()))
    {
    }

    public RecommendationExperimentService(IOptionsMonitor<RecommendationExperimentOptions> options)
    {
        _options = options;
    }

    public string ResolveGroup(HttpContext httpContext, int? userId)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        var sessionRerankTrafficPercent = ResolveTrafficPercent();

        var group = userId is > 0
            ? AssignUserGroup(userId.Value, sessionRerankTrafficPercent)
            : ResolveAnonymousGroup(httpContext, sessionRerankTrafficPercent);

        PersistCookie(httpContext, group);
        return group;
    }

    private int ResolveTrafficPercent()
    {
        return Math.Clamp(_options.CurrentValue.SessionRerankTrafficPercent, 0, 100);
    }

    private static string AssignUserGroup(int userId, int sessionRerankTrafficPercent)
    {
        if (sessionRerankTrafficPercent <= 0)
        {
            return RecommendationExperimentGroups.MlOnly;
        }

        if (sessionRerankTrafficPercent >= 100)
        {
            return RecommendationExperimentGroups.SessionRerank;
        }

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{ExperimentKey}:{userId}"));
        var bucket = BitConverter.ToUInt32(bytes, 0) % 100;
        return bucket < (uint)sessionRerankTrafficPercent
            ? RecommendationExperimentGroups.SessionRerank
            : RecommendationExperimentGroups.MlOnly;
    }

    private static string ResolveAnonymousGroup(HttpContext httpContext, int sessionRerankTrafficPercent)
    {
        if (httpContext.Request.Cookies.TryGetValue(CookieName, out var cookieGroup))
        {
            var normalizedCookieGroup = RecommendationExperimentGroups.Normalize(cookieGroup);
            if (string.Equals(cookieGroup, normalizedCookieGroup, StringComparison.OrdinalIgnoreCase))
            {
                return normalizedCookieGroup;
            }
        }

        return RandomNumberGenerator.GetInt32(100) < sessionRerankTrafficPercent
            ? RecommendationExperimentGroups.SessionRerank
            : RecommendationExperimentGroups.MlOnly;
    }

    private static void PersistCookie(HttpContext httpContext, string group)
    {
        if (httpContext.Response.HasStarted)
        {
            return;
        }

        httpContext.Response.Cookies.Append(
            CookieName,
            RecommendationExperimentGroups.Normalize(group),
            new CookieOptions
            {
                HttpOnly = true,
                IsEssential = true,
                MaxAge = CookieLifetime,
                SameSite = SameSiteMode.Lax,
                Secure = httpContext.Request.IsHttps
            });
    }

    private sealed class StaticRecommendationExperimentOptionsMonitor : IOptionsMonitor<RecommendationExperimentOptions>
    {
        public StaticRecommendationExperimentOptionsMonitor(RecommendationExperimentOptions value)
        {
            CurrentValue = value;
        }

        public RecommendationExperimentOptions CurrentValue { get; }

        public RecommendationExperimentOptions Get(string? name)
        {
            return CurrentValue;
        }

        public IDisposable? OnChange(Action<RecommendationExperimentOptions, string?> listener)
        {
            return null;
        }
    }
}
