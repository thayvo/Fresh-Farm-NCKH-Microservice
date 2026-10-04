using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;
using AdminOrderController = FreshFarm.Web.Bff.Areas.Admin.Controllers.OrderController;
using SellerOrderController = FreshFarm.Web.Bff.Areas.Seller.Controllers.OrderController;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace FreshFarm.Web.Bff.Tests;

public sealed class OrderControllerTests
{
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task AdminGetOrdersPaged_DedupesDuplicateScopedRows()
    {
        var controller = CreateAdminController(new RecordingHttpMessageHandler(request =>
        {
            if (request.RequestUri?.AbsolutePath == "/api/orders/admin/paged")
            {
                return JsonResponse("""
                {
                  "success": true,
                  "data": [
                    { "orderId": 101, "status": "Pending" },
                    {
                      "orderId": 101,
                      "orderCode": "#000101",
                      "buyerFullName": "Nguyen Van A",
                      "orderDate": "2026-03-24T01:00:00Z",
                      "total": 125000,
                      "status": "Pending",
                      "statusText": "Chờ xử lý",
                      "statusBadgeClass": "bg-warning"
                    },
                    {
                      "orderId": 202,
                      "orderCode": "#000202",
                      "buyerFullName": "Khong thuoc scope",
                      "total": 99000,
                      "status": "Delivered"
                    }
                  ],
                  "page": 1,
                  "pageSize": 10,
                  "total": 3
                }
                """);
            }

            if (request.RequestUri?.AbsolutePath == "/api/orders/admin/order-ids")
            {
                return JsonResponse("""
                {
                  "success": true,
                  "data": [101, 101]
                }
                """);
            }

            return new HttpResponseMessage(System.Net.HttpStatusCode.NotFound);
        }));

        var result = await controller.GetOrdersPaged(page: 1, pageSize: 10);

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(result.Value, WebJson));
        Assert.True(json.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal(1, json.RootElement.GetProperty("total").GetInt32());

        var order = json.RootElement.GetProperty("data").EnumerateArray().Single();
        Assert.Equal(101, order.GetProperty("OrderID").GetInt32());
        Assert.Equal("#000101", order.GetProperty("OrderCode").GetString());
        Assert.Equal("Nguyen Van A", order.GetProperty("CustomerName").GetString());
        Assert.Equal("Chờ xử lý", order.GetProperty("StatusText").GetString());
    }

    [Fact]
    public async Task SellerGetOrdersPaged_DedupesDuplicateScopedRows()
    {
        var controller = CreateSellerController(new RecordingHttpMessageHandler(request =>
        {
            if (request.RequestUri?.AbsolutePath == "/api/orders/admin/paged")
            {
                return JsonResponse("""
                {
                  "success": true,
                  "data": [
                    { "orderId": 303, "status": "Processing" },
                    {
                      "orderId": 303,
                      "orderCode": "#000303",
                      "customerName": "Tran Thi B",
                      "orderDate": "2026-03-24T02:00:00Z",
                      "totalAmount": 275000,
                      "status": "Processing",
                      "statusText": "Đang xử lý",
                      "statusBadgeClass": "bg-info"
                    },
                    {
                      "orderId": 404,
                      "orderCode": "#000404",
                      "customerName": "Ngoai scope",
                      "totalAmount": 88000,
                      "status": "Canceled"
                    }
                  ],
                  "page": 1,
                  "pageSize": 10,
                  "total": 3
                }
                """);
            }

            if (request.RequestUri?.AbsolutePath == "/api/orders/admin/order-ids")
            {
                return JsonResponse("""
                {
                  "success": true,
                  "data": [303, 303]
                }
                """);
            }

            return new HttpResponseMessage(System.Net.HttpStatusCode.NotFound);
        }));

        var result = await controller.GetOrdersPaged(page: 1, pageSize: 10);

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(result.Value, WebJson));
        Assert.True(json.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal(1, json.RootElement.GetProperty("total").GetInt32());

        var order = json.RootElement.GetProperty("data").EnumerateArray().Single();
        Assert.Equal(303, order.GetProperty("OrderID").GetInt32());
        Assert.Equal("#000303", order.GetProperty("OrderCode").GetString());
        Assert.Equal("Tran Thi B", order.GetProperty("CustomerName").GetString());
        Assert.Equal("Đang xử lý", order.GetProperty("StatusText").GetString());
    }

    private static AdminOrderController CreateAdminController(RecordingHttpMessageHandler orderingHandler)
    {
        var httpContext = CreateHttpContext(userId: 9001);
        return new AdminOrderController(CreateHttpClientFactory(orderingHandler))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = httpContext
            }
        };
    }

    private static SellerOrderController CreateSellerController(RecordingHttpMessageHandler orderingHandler)
    {
        var httpContext = CreateHttpContext(userId: 9002);
        return new SellerOrderController(CreateHttpClientFactory(orderingHandler))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = httpContext
            }
        };
    }

    private static IHttpClientFactory CreateHttpClientFactory(RecordingHttpMessageHandler orderingHandler)
    {
        var orderingClient = new HttpClient(orderingHandler)
        {
            BaseAddress = new Uri("https://ordering.test")
        };

        return new NamedHttpClientFactory(new Dictionary<string, HttpClient>(StringComparer.OrdinalIgnoreCase)
        {
            ["Ordering"] = orderingClient
        });
    }

    private static DefaultHttpContext CreateHttpContext(int userId)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Features.Set<ISessionFeature>(new SessionFeature
        {
            Session = new TestSession()
        });

        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, userId.ToString())
        ], "TestAuth");
        httpContext.User = new ClaimsPrincipal(identity);
        httpContext.Session.SetString("ACCESS_TOKEN", CreateJwt(userId));
        return httpContext;
    }

    private static string CreateJwt(int userId)
    {
        var token = new JwtSecurityToken(
            claims:
            [
                new Claim(JwtRegisteredClaimNames.Sub, userId.ToString())
            ]);

        return new JwtSecurityTokenHandler().WriteToken(token);
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
