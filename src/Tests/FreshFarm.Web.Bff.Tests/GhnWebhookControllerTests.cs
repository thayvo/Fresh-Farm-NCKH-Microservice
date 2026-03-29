using System.Net;
using System.Text.Json;
using FreshFarm.Web.Bff.Controllers;
using FreshFarm.Web.Bff.Options;
using FreshFarm.Web.Bff.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace FreshFarm.Web.Bff.Tests;

public sealed class GhnWebhookControllerTests
{
    [Fact]
    public async Task ReceiveOrderStatus_ReturnsUnauthorized_WhenSecretInvalid()
    {
        var orderingHandler = new RecordingHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var controller = CreateController(
            orderingHandler,
            new OrderingServiceOptions
            {
                BaseUrl = "https://ordering.test",
                InternalServiceKey = "internal-key"
            },
            new GhnOrderStatusWebhookOptions
            {
                Enabled = true,
                Secret = "expected-secret",
                DeduplicationWindowSeconds = 300
            });

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = BuildHttpContext("?secret=wrong-secret")
        };

        var result = await controller.ReceiveOrderStatus(new GhnWebhookController.GhnOrderStatusWebhookRequest
        {
            OrderCode = "ORDER-1",
            ClientOrderCode = "CLIENT-1",
            Status = "picking",
            Type = "switch_status"
        }, CancellationToken.None);

        var unauthorized = Assert.IsType<UnauthorizedObjectResult>(result);
        Assert.Equal(401, unauthorized.StatusCode);
        Assert.Empty(orderingHandler.Requests);
    }

    [Fact]
    public async Task ReceiveOrderStatus_SkipsDuplicateWebhook_AndPersistsOnlyOnce()
    {
        var orderingHandler = new RecordingHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"success\":true}")
            });
        var controller = CreateController(
            orderingHandler,
            new OrderingServiceOptions
            {
                BaseUrl = "https://ordering.test",
                InternalServiceKey = "internal-key"
            },
            new GhnOrderStatusWebhookOptions
            {
                Enabled = true,
                Secret = "expected-secret",
                DeduplicationWindowSeconds = 300
            });

        var payload = new GhnWebhookController.GhnOrderStatusWebhookRequest
        {
            OrderCode = "ORDER-1",
            ClientOrderCode = "CLIENT-1",
            Status = "picking",
            Type = "switch_status",
            TotalFee = 20900,
            Time = JsonDocument.Parse("\"2026-03-22T10:15:00Z\"").RootElement.Clone()
        };

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = BuildHttpContext("?secret=expected-secret")
        };

        var firstResult = await controller.ReceiveOrderStatus(payload, CancellationToken.None);
        var secondResult = await controller.ReceiveOrderStatus(payload, CancellationToken.None);

        var firstOk = Assert.IsType<OkObjectResult>(firstResult);
        var secondOk = Assert.IsType<OkObjectResult>(secondResult);

        Assert.False(ReadBooleanProperty(firstOk.Value, "duplicate"));
        Assert.True(ReadBooleanProperty(secondOk.Value, "duplicate"));

        Assert.Single(orderingHandler.Requests);
        var request = orderingHandler.Requests[0];
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://ordering.test/api/orders/admin/shippings/internal/ghn-metadata/by-code", request.RequestUri?.ToString());
        Assert.True(request.Headers.TryGetValues("X-Internal-Service-Key", out var keyValues));
        Assert.Contains("internal-key", keyValues);
    }

    private static GhnWebhookController CreateController(
        RecordingHttpMessageHandler orderingHandler,
        OrderingServiceOptions orderingOptions,
        GhnOrderStatusWebhookOptions webhookOptions)
    {
        var orderingClient = new HttpClient(orderingHandler)
        {
            BaseAddress = new Uri(orderingOptions.BaseUrl)
        };

        return new GhnWebhookController(
            new StaticHttpClientFactory(orderingClient),
            new FakeGhnSandboxService(),
            new StaticOptionsMonitor<OrderingServiceOptions>(orderingOptions),
            new StaticOptionsMonitor<GhnOrderStatusWebhookOptions>(webhookOptions),
            new MemoryCache(new MemoryCacheOptions()),
            NullLogger<GhnWebhookController>.Instance);
    }

    private static DefaultHttpContext BuildHttpContext(string queryString)
    {
        var context = new DefaultHttpContext();
        context.Request.QueryString = new QueryString(queryString);
        return context;
    }

    private static bool ReadBooleanProperty(object? value, string propertyName)
    {
        Assert.NotNull(value);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(value));
        return json.RootElement.GetProperty(propertyName).GetBoolean();
    }

    private sealed class StaticHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class StaticOptionsMonitor<T>(T currentValue) : IOptionsMonitor<T>
    {
        public T CurrentValue => currentValue;

        public T Get(string? name) => currentValue;

        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }

    private sealed class FakeGhnSandboxService : IGhnSandboxService
    {
        public bool IsConfigured => false;

        public int? ShopId => null;

        public Task<GhnSandboxConnectionResult> TestConnectionAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<GhnSandboxShopProfileResult> GetShopProfileAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<GhnSandboxLocationItem>> GetProvincesAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<GhnSandboxLocationItem>> GetDistrictsAsync(int provinceId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<GhnSandboxLocationItem>> GetWardsAsync(int districtId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<GhnSandboxFeeResult> CalculateFeeAsync(GhnSandboxFeeRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<GhnSandboxLeadTimeResult> CalculateLeadTimeAsync(GhnSandboxLeadTimeRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<GhnSandboxCreateOrderResult> CreateOrderAsync(GhnSandboxCreateOrderRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<GhnSandboxOrderTrackingResult> GetOrderTrackingAsync(GhnSandboxOrderTrackingRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class RecordingHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(CloneRequest(request));
            return Task.FromResult(responder(request));
        }

        private static HttpRequestMessage CloneRequest(HttpRequestMessage request)
        {
            var clone = new HttpRequestMessage(request.Method, request.RequestUri);
            foreach (var header in request.Headers)
            {
                clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            if (request.Content is not null)
            {
                var body = request.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                clone.Content = new StringContent(body);
                foreach (var header in request.Content.Headers)
                {
                    clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }
            }

            return clone;
        }
    }
}
