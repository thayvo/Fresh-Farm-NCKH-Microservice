using System.Text.Json;
using AdminCustomerController = FreshFarm.Web.Bff.Areas.Admin.Controllers.CustomerController;
using SellerCustomerController = FreshFarm.Web.Bff.Areas.Seller.Controllers.CustomerController;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace FreshFarm.Web.Bff.Tests;

public sealed class CustomerControllerTests
{
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task AdminSearch_DedupesDuplicateCustomerMetricsPayload()
    {
        var identityHandler = new RecordingHttpMessageHandler(request =>
        {
            if (request.RequestUri?.AbsolutePath == "/auth/admin/customers")
            {
                return JsonResponse("""
                [
                  {
                    "userId": 41,
                    "userName": "customer41",
                    "fullName": "Customer 41",
                    "email": "customer41@example.com",
                    "phone": "0911111111",
                    "createdAt": "2026-03-24T00:00:00Z",
                    "address": "1 Nguyen Trai"
                  }
                ]
                """);
            }

            return new HttpResponseMessage(System.Net.HttpStatusCode.NotFound);
        });
        var orderingHandler = new RecordingHttpMessageHandler(request =>
        {
            if (request.RequestUri?.AbsolutePath == "/api/orders/admin/customers/metrics")
            {
                return JsonResponse("""
                {
                  "success": true,
                  "data": [
                    { "userId": 41, "orderCount": 0, "totalSpent": 0 },
                    { "userId": 41, "orderCount": 3, "totalSpent": 1250000 }
                  ]
                }
                """);
            }

            return new HttpResponseMessage(System.Net.HttpStatusCode.NotFound);
        });
        var controller = CreateAdminController(identityHandler, orderingHandler);

        var result = await controller.Search(new AdminCustomerController.SearchRequest
        {
            keyword = "customer"
        });

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(result.Value, WebJson));
        Assert.True(json.RootElement.GetProperty("success").GetBoolean());
        var customer = json.RootElement.GetProperty("data").EnumerateArray().Single();
        Assert.Equal(41, customer.GetProperty("userID").GetInt32());
        Assert.Equal(3, customer.GetProperty("orderCount").GetInt32());
        Assert.Equal(1250000m, customer.GetProperty("totalSpent").GetDecimal());
    }

    [Fact]
    public async Task SellerSearch_DedupesDuplicateCustomerMetricsPayload()
    {
        var identityHandler = new RecordingHttpMessageHandler(request =>
        {
            if (request.RequestUri?.AbsolutePath == "/auth/admin/customers")
            {
                return JsonResponse("""
                [
                  {
                    "userId": 52,
                    "userName": "customer52",
                    "fullName": "Customer 52",
                    "email": "customer52@example.com",
                    "phone": "0922222222",
                    "createdAt": "2026-03-24T00:00:00Z",
                    "address": "2 Le Loi"
                  }
                ]
                """);
            }

            return new HttpResponseMessage(System.Net.HttpStatusCode.NotFound);
        });
        var orderingHandler = new RecordingHttpMessageHandler(request =>
        {
            if (request.RequestUri?.AbsolutePath == "/api/orders/admin/customers/ids")
            {
                return JsonResponse("""
                {
                  "success": true,
                  "data": [52]
                }
                """);
            }

            if (request.RequestUri?.AbsolutePath == "/api/orders/admin/customers/metrics")
            {
                return JsonResponse("""
                {
                  "success": true,
                  "data": [
                    { "userId": 52, "orderCount": 1, "totalSpent": 0 },
                    { "userId": 52, "orderCount": 2, "totalSpent": 450000 }
                  ]
                }
                """);
            }

            return new HttpResponseMessage(System.Net.HttpStatusCode.NotFound);
        });
        var controller = CreateSellerController(identityHandler, orderingHandler);

        var result = await controller.Search(new SellerCustomerController.SearchRequest
        {
            keyword = "customer"
        });

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(result.Value, WebJson));
        Assert.True(json.RootElement.GetProperty("success").GetBoolean());
        var customer = json.RootElement.GetProperty("data").EnumerateArray().Single();
        Assert.Equal(52, customer.GetProperty("userID").GetInt32());
        Assert.Equal(2, customer.GetProperty("orderCount").GetInt32());
        Assert.Equal(450000m, customer.GetProperty("totalSpent").GetDecimal());
    }

    private static AdminCustomerController CreateAdminController(
        RecordingHttpMessageHandler identityHandler,
        RecordingHttpMessageHandler orderingHandler)
    {
        var httpContext = CreateHttpContext();
        return new AdminCustomerController(CreateHttpClientFactory(identityHandler, orderingHandler))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = httpContext
            }
        };
    }

    private static SellerCustomerController CreateSellerController(
        RecordingHttpMessageHandler identityHandler,
        RecordingHttpMessageHandler orderingHandler)
    {
        var httpContext = CreateHttpContext();
        return new SellerCustomerController(CreateHttpClientFactory(identityHandler, orderingHandler))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = httpContext
            }
        };
    }

    private static IHttpClientFactory CreateHttpClientFactory(
        RecordingHttpMessageHandler identityHandler,
        RecordingHttpMessageHandler orderingHandler)
    {
        var identityClient = new HttpClient(identityHandler)
        {
            BaseAddress = new Uri("https://identity.test")
        };
        var orderingClient = new HttpClient(orderingHandler)
        {
            BaseAddress = new Uri("https://ordering.test")
        };

        return new NamedHttpClientFactory(new Dictionary<string, HttpClient>(StringComparer.OrdinalIgnoreCase)
        {
            ["Identity"] = identityClient,
            ["Ordering"] = orderingClient
        });
    }

    private static DefaultHttpContext CreateHttpContext()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Features.Set<ISessionFeature>(new SessionFeature
        {
            Session = new TestSession()
        });
        httpContext.Session.SetString("ACCESS_TOKEN", "token-123");
        return httpContext;
    }

    private static HttpResponseMessage JsonResponse(string json)
    {
        return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent(json)
        };
    }

    private sealed class NamedHttpClientFactory(IReadOnlyDictionary<string, HttpClient> clients) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => clients[name];
    }

    private sealed class RecordingHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(responder(request));
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
        public bool IsAvailable => true;
        public void Clear() => _store.Clear();
        public Task CommitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Remove(string key) => _store.Remove(key);
        public void Set(string key, byte[] value) => _store[key] = value;
        public bool TryGetValue(string key, out byte[] value) => _store.TryGetValue(key, out value!);
    }
}
