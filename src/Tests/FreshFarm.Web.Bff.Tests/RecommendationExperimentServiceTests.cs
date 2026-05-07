using FreshFarm.Web.Bff.Options;
using FreshFarm.Web.Bff.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Xunit;

namespace FreshFarm.Web.Bff.Tests;

public sealed class RecommendationExperimentServiceTests
{
    [Fact]
    public void ResolveGroup_UsesStableUserAssignmentAndWritesCookie()
    {
        var service = new RecommendationExperimentService();
        var firstContext = CreateContext();
        var secondContext = CreateContext();

        var firstGroup = service.ResolveGroup(firstContext, userId: 17);
        var secondGroup = service.ResolveGroup(secondContext, userId: 17);

        Assert.Contains(firstGroup, new[] { "A", "B" });
        Assert.Equal(firstGroup, secondGroup);
        Assert.Contains(
            $"{RecommendationExperimentService.CookieName}={firstGroup}",
            firstContext.Response.Headers.SetCookie.ToString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public void ResolveGroup_UsesExistingAnonymousCookie()
    {
        var service = new RecommendationExperimentService();
        var context = CreateContext();
        context.Request.Headers.Cookie = $"{RecommendationExperimentService.CookieName}=B";

        var group = service.ResolveGroup(context, userId: null);

        Assert.Equal("B", group);
        Assert.Contains(
            $"{RecommendationExperimentService.CookieName}=B",
            context.Response.Headers.SetCookie.ToString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public void ResolveGroup_UsesConfiguredTrafficPercentForAuthenticatedUsers()
    {
        var allMlService = new RecommendationExperimentService(
            new StaticOptionsMonitor<RecommendationExperimentOptions>(new RecommendationExperimentOptions
            {
                SessionRerankTrafficPercent = 0
            }));
        var allSessionService = new RecommendationExperimentService(
            new StaticOptionsMonitor<RecommendationExperimentOptions>(new RecommendationExperimentOptions
            {
                SessionRerankTrafficPercent = 100
            }));

        Assert.Equal("A", allMlService.ResolveGroup(CreateContext(), userId: 17));
        Assert.Equal("B", allSessionService.ResolveGroup(CreateContext(), userId: 17));
    }

    private static DefaultHttpContext CreateContext()
    {
        var context = new DefaultHttpContext();
        context.Request.Scheme = "https";
        return context;
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
}
