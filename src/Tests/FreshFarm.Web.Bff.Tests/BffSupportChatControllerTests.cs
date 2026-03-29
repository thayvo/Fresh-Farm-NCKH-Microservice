using System.Security.Claims;
using System.Text;
using System.Text.Json;
using FreshFarm.Web.Bff.Areas.Seller.Hubs;
using FreshFarm.Web.Bff.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Xunit;

namespace FreshFarm.Web.Bff.Tests;

public sealed class BffSupportChatControllerTests
{
    [Fact]
    public async Task SendMessage_BroadcastsRealtimeEvents_ToSellerAndConversationGroups()
    {
        string? postedJson = null;
        var handler = new RecordingHttpMessageHandler(async request =>
        {
            if (request.RequestUri?.AbsolutePath == "/api/orders/support-chat/conversations/88/messages")
            {
                postedJson = await request.Content!.ReadAsStringAsync();
                return CreateJsonResponse("""
                {
                  "ok": true,
                  "message": {
                    "messageId": 501,
                    "from": "buyer",
                    "content": "Xin chao shop",
                    "createdAt": "2026-03-24T10:00:00Z",
                    "isDeleted": false
                  }
                }
                """);
            }

            return new HttpResponseMessage(System.Net.HttpStatusCode.NotFound);
        });

        var hubContext = new RecordingHubContext();
        var controller = CreateController(handler, hubContext);

        var result = await controller.SendMessage(88, new BffSupportChatController.SendSupportMessageBridgeRequest
        {
            Content = "Xin chao shop",
            SellerId = 14
        });

        var content = Assert.IsType<ContentResult>(result);
        Assert.Equal(200, content.StatusCode);
        Assert.Contains("\"from\": \"buyer\"", content.Content ?? string.Empty);

        Assert.NotNull(postedJson);
        using (var posted = JsonDocument.Parse(postedJson!))
        {
            Assert.Equal("Xin chao shop", posted.RootElement.GetProperty("content").GetString());
            Assert.False(posted.RootElement.TryGetProperty("sellerId", out _));
        }

        var sellerCall = Assert.Single(hubContext.ClientsRecorder.GroupCalls, x => x.GroupName == SupportChatHubGroups.Seller(14));
        Assert.Equal("newConversationOrMessage", sellerCall.Method);
        using (var sellerPayload = JsonDocument.Parse(JsonSerializer.Serialize(sellerCall.Args[0])))
        {
            Assert.Equal(88, sellerPayload.RootElement.GetProperty("conversationId").GetInt32());
            Assert.Equal("message", sellerPayload.RootElement.GetProperty("eventType").GetString());
        }

        var conversationCall = Assert.Single(hubContext.ClientsRecorder.GroupCalls, x => x.GroupName == SupportChatHubGroups.Conversation(88));
        Assert.Equal("receiveMessage", conversationCall.Method);
        using var conversationPayload = JsonDocument.Parse(JsonSerializer.Serialize(conversationCall.Args[0]));
        Assert.Equal(88, conversationPayload.RootElement.GetProperty("conversationId").GetInt32());
        Assert.Equal("Xin chao shop", conversationPayload.RootElement.GetProperty("message").GetProperty("content").GetString());
    }

    [Fact]
    public async Task Summaries_WithoutSellerIds_EnrichesShopMetadata_FromIdentity()
    {
        var orderingHandler = new RecordingHttpMessageHandler(request =>
        {
            Assert.Equal("/api/orders/support-chat/summaries", request.RequestUri?.AbsolutePath);
            return Task.FromResult(CreateJsonResponse("""
            {
              "ok": true,
              "summaries": [
                {
                  "sellerId": 14,
                  "conversationId": 88,
                  "status": "Open",
                  "startedAt": "2026-03-24T09:00:00Z",
                  "closedAt": null,
                  "lastContent": "Shop da phan hoi",
                  "lastTime": "2026-03-24T10:00:00Z",
                  "hasUnread": true
                }
              ]
            }
            """));
        });

        var identityHandler = new RecordingHttpMessageHandler(request =>
        {
            Assert.Equal("/auth/public/merchants", request.RequestUri?.AbsolutePath);
            Assert.Contains("sellerIds=14", request.RequestUri?.Query ?? string.Empty);
            return Task.FromResult(CreateJsonResponse("""
            {
              "merchants": [
                {
                  "sellerId": 14,
                  "shopName": "Nong san nha Lam",
                  "userName": "lamshop",
                  "avatar": "https://cdn.test/lam.png",
                  "addressSummary": "Quan 7, TP.HCM",
                  "joinedAt": "2026-01-10T00:00:00Z"
                }
              ]
            }
            """));
        });

        var hubContext = new RecordingHubContext();
        var controller = CreateController(
            new Dictionary<string, HttpClient>(StringComparer.OrdinalIgnoreCase)
            {
                ["Ordering"] = CreateClient(orderingHandler, "https://ordering.test"),
                ["Identity"] = CreateClient(identityHandler, "https://identity.test")
            },
            hubContext);

        var result = await controller.Summaries(null);

        var ok = Assert.IsType<OkObjectResult>(result);
        using var payload = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value));
        var summaries = payload.RootElement.GetProperty("summaries");
        Assert.Single(summaries.EnumerateArray());
        Assert.Equal("Nong san nha Lam", summaries[0].GetProperty("shopName").GetString());
        Assert.Equal("https://cdn.test/lam.png", summaries[0].GetProperty("avatar").GetString());
        Assert.Equal("Quan 7, TP.HCM", summaries[0].GetProperty("addressSummary").GetString());
    }

    private static BffSupportChatController CreateController(RecordingHttpMessageHandler handler, RecordingHubContext hubContext)
        => CreateController(
            new Dictionary<string, HttpClient>(StringComparer.OrdinalIgnoreCase)
            {
                ["Ordering"] = CreateClient(handler, "https://ordering.test"),
                ["Identity"] = CreateClient(new RecordingHttpMessageHandler(_ =>
                    Task.FromResult(CreateJsonResponse("""{ "merchants": [] }"""))), "https://identity.test")
            },
            hubContext);

    private static BffSupportChatController CreateController(
        IReadOnlyDictionary<string, HttpClient> clients,
        RecordingHubContext hubContext)
    {
        var controller = new BffSupportChatController(new StaticHttpClientFactory(clients), hubContext)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = CreateHttpContext()
            }
        };

        controller.ControllerContext.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, "18"),
            new Claim(ClaimTypes.Role, "Customer")
        ], "TestAuth"));

        return controller;
    }

    private static HttpClient CreateClient(HttpMessageHandler handler, string baseAddress)
    {
        return new HttpClient(handler)
        {
            BaseAddress = new Uri(baseAddress)
        };
    }

    private static DefaultHttpContext CreateHttpContext()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Features.Set<ISessionFeature>(new SessionFeature
        {
            Session = new TestSession()
        });
        httpContext.Session.SetString("ACCESS_TOKEN", "test-token");
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

    private sealed class RecordingHubContext : IHubContext<SupportChatHub>
    {
        public RecordingHubClients ClientsRecorder { get; } = new();

        public IHubClients Clients => ClientsRecorder;

        public IGroupManager Groups { get; } = new RecordingGroupManager();
    }

    private sealed class RecordingHubClients : IHubClients
    {
        private readonly RecordingClientProxy _all = new("__all__");
        public List<RecordedGroupCall> GroupCalls { get; } = new();

        public IClientProxy All => _all;

        public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => _all;

        public IClientProxy Client(string connectionId) => _all;

        public IClientProxy Clients(IReadOnlyList<string> connectionIds) => _all;

        public IClientProxy Group(string groupName) => new RecordingClientProxy(groupName, GroupCalls);

        public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => new RecordingClientProxy(groupName, GroupCalls);

        public IClientProxy Groups(IReadOnlyList<string> groupNames) => _all;

        public IClientProxy User(string userId) => _all;

        public IClientProxy Users(IReadOnlyList<string> userIds) => _all;
    }

    private sealed class RecordingClientProxy(string target, List<RecordedGroupCall>? sink = null) : IClientProxy
    {
        public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
        {
            sink?.Add(new RecordedGroupCall(target, method, args));
            return Task.CompletedTask;
        }
    }

    private sealed record RecordedGroupCall(string GroupName, string Method, object?[] Args);

    private sealed class RecordingGroupManager : IGroupManager
    {
        public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class SessionFeature : ISessionFeature
    {
        public ISession Session { get; set; } = null!;
    }

    private sealed class TestSession : ISession
    {
        private readonly Dictionary<string, byte[]> _store = new(StringComparer.Ordinal);

        public IEnumerable<string> Keys => _store.Keys;

        public string Id { get; } = Guid.NewGuid().ToString("N");

        public bool IsAvailable { get; } = true;

        public void Clear() => _store.Clear();

        public Task CommitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public void Remove(string key) => _store.Remove(key);

        public void Set(string key, byte[] value) => _store[key] = value;

        public bool TryGetValue(string key, out byte[] value) => _store.TryGetValue(key, out value!);
    }
}
