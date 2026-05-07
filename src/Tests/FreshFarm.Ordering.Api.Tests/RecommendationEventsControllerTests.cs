using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;
using FreshFarm.Ordering.Api.Controllers;
using FreshFarm.Ordering.Api.Models;
using FreshFarm.Ordering.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FreshFarm.Ordering.Api.Tests;

public sealed class RecommendationEventsControllerTests
{
    [Fact]
    public async Task TrackSearch_PersistsSearchEvent_WithUserAndFiltersJson()
    {
        await using var db = CreateDbContext();
        var controller = CreateController(db, 47);

        var result = await controller.TrackSearch(new RecommendationEventsController.TrackSearchRequest
        {
            SessionId = "session-abc",
            Keyword = "rau huu co",
            Filters = JsonDocument.Parse("""{"categoryId":2,"sort":"related"}""").RootElement,
            ResultCount = 12
        }, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        using var payload = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value));
        Assert.True(payload.RootElement.GetProperty("eventId").GetInt32() > 0);

        var entity = await db.SearchEvents.SingleAsync();
        Assert.Equal(47, entity.UserId);
        Assert.Equal("session-abc", entity.SessionId);
        Assert.Equal("rau huu co", entity.Keyword);
        Assert.Contains("\"categoryId\":2", entity.FiltersJson);
        Assert.Equal(12, entity.ResultCount);
    }

    [Fact]
    public async Task TrackRecommendationClick_RejectsUnknownImpressionId()
    {
        await using var db = CreateDbContext();
        var controller = CreateController(db, userId: null);

        var result = await controller.TrackRecommendationClick(new RecommendationEventsController.TrackRecommendationClickRequest
        {
            SessionId = "guest-session",
            RecommendationImpressionEventId = 99,
            ProductId = 17,
            Placement = "home_today",
            Algorithm = "content"
        }, CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        using var payload = JsonDocument.Parse(JsonSerializer.Serialize(badRequest.Value));
        Assert.Equal("RecommendationImpressionEventId không tồn tại.", payload.RootElement.GetProperty("message").GetString());
        Assert.Empty(await db.RecommendationClickEvents.ToListAsync());
    }

    [Fact]
    public async Task TrackSearch_RequestsAffinityRefreshSignal_AfterPersistingEvent()
    {
        await using var db = CreateDbContext();
        var refreshSignal = new RecommendationAffinityRefreshSignal();
        var controller = CreateController(db, 47, refreshSignal);

        var result = await controller.TrackSearch(new RecommendationEventsController.TrackSearchRequest
        {
            SessionId = "session-refresh",
            Keyword = "rau",
            ResultCount = 5
        }, CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal(1, refreshSignal.RequestCount);
    }

    [Fact]
    public async Task TrackRecommendationImpression_DoesNotRequestAffinityRefreshSignal()
    {
        await using var db = CreateDbContext();
        var refreshSignal = new RecommendationAffinityRefreshSignal();
        var controller = CreateController(db, userId: null, refreshSignal);

        var result = await controller.TrackRecommendationImpression(new RecommendationEventsController.TrackRecommendationImpressionRequest
        {
            SessionId = "guest-session",
            Placement = "home_today",
            RecommendationRunId = "run-1",
            ProductId = 17,
            Rank = 1,
            Algorithm = "hybrid_home_v1"
        }, CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal(0, refreshSignal.RequestCount);
    }

    private static RecommendationEventsController CreateController(
        FreshFarmOrderingDBContext db,
        int? userId,
        RecommendationAffinityRefreshSignal? refreshSignal = null)
    {
        var controller = refreshSignal is null
            ? new RecommendationEventsController(db)
            : new RecommendationEventsController(db, refreshSignal);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = BuildHttpContext(userId)
        };
        return controller;
    }

    private static DefaultHttpContext BuildHttpContext(int? userId)
    {
        var context = new DefaultHttpContext();
        if (userId.HasValue)
        {
            context.User = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(JwtRegisteredClaimNames.Sub, userId.Value.ToString())
            ], "Test"));
        }

        return context;
    }

    private static FreshFarmOrderingDBContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<FreshFarmOrderingDBContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new FreshFarmOrderingDBContext(options);
    }
}
