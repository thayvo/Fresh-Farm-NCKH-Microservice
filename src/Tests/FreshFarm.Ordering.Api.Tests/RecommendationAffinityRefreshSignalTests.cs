using FreshFarm.Ordering.Api.Services;
using Xunit;

namespace FreshFarm.Ordering.Api.Tests;

public sealed class RecommendationAffinityRefreshSignalTests
{
    [Fact]
    public async Task WaitForRefreshSignalAsync_CanConsumeMultipleRefreshRequestsSequentially()
    {
        var signal = new RecommendationAffinityRefreshSignal();

        signal.RequestRefresh();
        var first = await signal.WaitForRefreshSignalAsync(TimeSpan.FromMilliseconds(100), CancellationToken.None);

        signal.RequestRefresh();
        var second = await signal.WaitForRefreshSignalAsync(TimeSpan.FromMilliseconds(100), CancellationToken.None);

        Assert.True(first);
        Assert.True(second);
        Assert.Equal(2, signal.RequestCount);
    }
}
