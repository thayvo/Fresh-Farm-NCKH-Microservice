using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;
using FreshFarm.Web.Bff.Areas.Admin.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace FreshFarm.Web.Bff.Tests;

public sealed class DeliveryControllerTests
{
    [Fact]
    public async Task List_And_Staffs_DedupeDuplicateShippingRowsAndStaffs_FromOrdering()
    {
        var handler = new RecordingHttpMessageHandler(request =>
        {
            if (request.RequestUri?.AbsolutePath == "/api/orders/admin/reports/shipping")
            {
                var query = request.RequestUri?.Query ?? string.Empty;
                if (query.Contains("status=all"))
                {
                    return CreateJsonResponse("""
                    {
                      "totalRecords": 1,
                      "currentPage": 1,
                      "pageSize": 1,
                      "deliveryStaffs": [
                        { "value": "9", "text": "" },
                        { "value": "9", "text": "Shipper A" }
                      ]
                    }
                    """);
                }

                return CreateJsonResponse("""
                {
                  "totalRecords": 1,
                  "currentPage": 1,
                  "pageSize": 10,
                  "recentShippings": [
                    {
                      "orderCode": "ORD-000555",
                      "customerName": "",
                      "customerPhone": "",
                      "deliveryAddress": "",
                      "shippingDateFormatted": "",
                      "statusText": ""
                    },
                    {
                      "orderCode": "ORD-000555",
                      "customerName": "Nguyen Van A",
                      "customerPhone": "0901234567",
                      "deliveryAddress": "123 Duong Le Loi",
                      "shippingDateFormatted": "24/03/2026",
                      "statusText": "Đang giao"
                    }
                  ]
                }
                """);
            }

            return new HttpResponseMessage(System.Net.HttpStatusCode.NotFound);
        });
        var controller = CreateController(handler);

        var listResult = await controller.List();
        var staffsResult = await controller.Staffs();

        var listPayload = AssertJsonObject(listResult.Value);
        Assert.True(listPayload.GetProperty("success").GetBoolean());
        Assert.Equal(1, listPayload.GetProperty("total").GetInt32());
        var items = listPayload.GetProperty("data");
        Assert.Equal(JsonValueKind.Array, items.ValueKind);
        Assert.Equal(1, items.GetArrayLength());
        var item = items[0];
        Assert.Equal(555, item.GetProperty("orderID").GetInt32());
        Assert.Equal("ORD-000555", item.GetProperty("orderCode").GetString());
        Assert.Equal("Nguyen Van A", item.GetProperty("customerName").GetString());
        Assert.Equal("0901234567", item.GetProperty("customerPhone").GetString());
        Assert.Equal("Shipped", item.GetProperty("status").GetString());
        Assert.Equal("24/03/2026", item.GetProperty("orderDate").GetString());
        Assert.Equal("123 Duong Le Loi", item.GetProperty("address").GetString());
        Assert.True(item.GetProperty("canDeliver").GetBoolean());

        var staffsPayload = AssertJsonObject(staffsResult.Value);
        Assert.True(staffsPayload.GetProperty("success").GetBoolean());
        var staffs = staffsPayload.GetProperty("data");
        Assert.Equal(JsonValueKind.Array, staffs.ValueKind);
        Assert.Equal(1, staffs.GetArrayLength());
        var staff = staffs[0];
        Assert.Equal(9, staff.GetProperty("id").GetInt32());
        Assert.Equal("Shipper A", staff.GetProperty("name").GetString());
        Assert.Equal("Delivery", staff.GetProperty("role").GetString());
    }

    private static DeliveryController CreateController(RecordingHttpMessageHandler handler)
    {
        var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://ordering.test")
        };

        return new DeliveryController(new StaticHttpClientFactory(client))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = CreateHttpContext("7")
            }
        };
    }

    private static JsonElement AssertJsonObject(object? value)
    {
        Assert.NotNull(value);
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(value));
        return document.RootElement.Clone();
    }

    private static HttpContext CreateHttpContext(string userId)
    {
        var token = CreateAccessToken(userId);
        var httpContext = new DefaultHttpContext();
        httpContext.Features.Set<ISessionFeature>(new SessionFeature
        {
            Session = new TestSession()
        });
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, userId),
            new Claim("ff_access_token", token)
        ], "TestAuth"));
        return httpContext;
    }

    private static string CreateAccessToken(string userId)
    {
        var jwt = new JwtSecurityToken(
            issuer: "FreshFarm.Tests",
            audience: "FreshFarm.Tests",
            claims:
            [
                new Claim(JwtRegisteredClaimNames.Sub, userId)
            ]);

        return new JwtSecurityTokenHandler().WriteToken(jwt);
    }

    private static HttpResponseMessage CreateJsonResponse(string json)
    {
        return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent(json)
        };
    }

    private sealed class StaticHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
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
