using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using FreshFarm.Web.Bff.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace FreshFarm.Web.Bff.Tests;

public sealed class BffRecommendationEventsControllerTests
{
    [Fact]
    public async Task TrackProductView_ForwardsSessionAndAccessToken_ToOrdering()
    {
        string? postedJson = null;
        AuthenticationHeaderValue? authorization = null;

        var handler = new RecordingHttpMessageHandler(async request =>
        {
            postedJson = await request.Content!.ReadAsStringAsync();
            authorization = request.Headers.Authorization;
            return CreateJsonResponse("""{ "ok": true, "eventId": 501 }""");
        });

        var controller = CreateController(handler, includeAccessToken: true);

        var result = await controller.TrackProductView(new BffRecommendationEventsController.TrackProductViewRequest
        {
            ProductId = 22,
            SellerId = 4,
            SourcePage = "home",
            SourceModule = "today_suggestion"
        });

        var content = Assert.IsType<ContentResult>(result);
        Assert.Equal(200, content.StatusCode);
        Assert.Contains("\"eventId\": 501", content.Content ?? string.Empty);

        Assert.NotNull(postedJson);
        using var payload = JsonDocument.Parse(postedJson!);
        Assert.Equal("session-track-001", payload.RootElement.GetProperty("sessionId").GetString());
        Assert.Equal(22, payload.RootElement.GetProperty("productId").GetInt32());
        Assert.Equal("home", payload.RootElement.GetProperty("sourcePage").GetString());
        Assert.Equal("today_suggestion", payload.RootElement.GetProperty("sourceModule").GetString());
        Assert.Equal("Bearer", authorization?.Scheme);
        Assert.Equal("test-token", authorization?.Parameter);
    }

    [Fact]
    public async Task TrackSearchClick_ReturnsOrderingMessage_WhenOrderingRejectsPayload()
    {
        var handler = new RecordingHttpMessageHandler(_ => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.BadRequest)
        {
            Content = new StringContent("""{ "ok": false, "message": "SearchEventId không tồn tại." }""", Encoding.UTF8, "application/json")
        }));

        var controller = CreateController(handler, includeAccessToken: false);

        var result = await controller.TrackSearchClick(new BffRecommendationEventsController.TrackSearchClickRequest
        {
            SearchEventId = 99,
            ProductId = 14,
            Rank = 3
        });

        var badRequest = Assert.IsType<ObjectResult>(result);
        Assert.Equal(400, badRequest.StatusCode);
        using var payload = JsonDocument.Parse(JsonSerializer.Serialize(badRequest.Value));
        Assert.Equal("SearchEventId không tồn tại.", payload.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task TrackSearch_EstablishesStableRecommendationSessionMarker()
    {
        string? postedJson = null;
        var session = new TestSession("session-track-guest-001");
        var handler = new RecordingHttpMessageHandler(async request =>
        {
            postedJson = await request.Content!.ReadAsStringAsync();
            return CreateJsonResponse("""{ "ok": true, "eventId": 777 }""");
        });

        var controller = CreateController(handler, includeAccessToken: false, session);

        var result = await controller.TrackSearch(new BffRecommendationEventsController.TrackSearchRequest
        {
            Keyword = "rau",
            ResultCount = 5
        });

        var content = Assert.IsType<ContentResult>(result);
        Assert.Equal(200, content.StatusCode);
        Assert.Contains("__recommendation_session_initialized", session.Keys);
        Assert.NotNull(postedJson);
        using var payload = JsonDocument.Parse(postedJson!);
        Assert.Equal("session-track-guest-001", payload.RootElement.GetProperty("sessionId").GetString());
    }

    private static BffRecommendationEventsController CreateController(HttpMessageHandler handler, bool includeAccessToken, TestSession? session = null)
    {
        var controller = new BffRecommendationEventsController(new StaticHttpClientFactory(new Dictionary<string, HttpClient>(StringComparer.OrdinalIgnoreCase)
        {
            ["Ordering"] = new HttpClient(handler)
            {
                BaseAddress = new Uri("https://ordering.test")
            }
        }))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = CreateHttpContext(includeAccessToken, session)
            }
        };

        return controller;
    }

    private static DefaultHttpContext CreateHttpContext(bool includeAccessToken, TestSession? session = null)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Features.Set<ISessionFeature>(new SessionFeature
        {
            Session = session ?? new TestSession("session-track-001")
        });

        if (includeAccessToken)
        {
            httpContext.Session.SetString("ACCESS_TOKEN", "test-token");
        }

        return httpContext;
    }

    private static HttpResponseMessage CreateJsonResponse(string json)
    {
        return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    private sealed class StaticHttpClientFactory(IReadOnlyDictionary<string, HttpClient> clients) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
            => clients.TryGetValue(name, out var client)
                ? client
                : throw new InvalidOperationException($"No HttpClient configured for '{name}'.");
    }

    private sealed class RecordingHttpMessageHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => responder(request);
    }

    private sealed class SessionFeature : ISessionFeature
    {
        public ISession Session { get; set; } = null!;
    }

    private sealed class TestSession(string id) : ISession
    {
        private readonly Dictionary<string, byte[]> _store = new(StringComparer.Ordinal);

        public IEnumerable<string> Keys => _store.Keys;

        public string Id { get; } = id;

        public bool IsAvailable { get; } = true;

        public void Clear() => _store.Clear();

        public Task CommitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public void Remove(string key) => _store.Remove(key);

        public void Set(string key, byte[] value) => _store[key] = value;

        public bool TryGetValue(string key, out byte[] value) => _store.TryGetValue(key, out value!);
    }
}
