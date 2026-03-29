using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;
using AdminCouponController = FreshFarm.Web.Bff.Areas.Admin.Controllers.CouponController;
using SellerCouponController = FreshFarm.Web.Bff.Areas.Seller.Controllers.CouponController;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace FreshFarm.Web.Bff.Tests;

public sealed class CouponControllerTests
{
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task AdminGetCustomers_DedupesDuplicateIdentityCustomers()
    {
        var controller = CreateAdminController(
            identityHandler: new RecordingHttpMessageHandler(request =>
            {
                if (request.RequestUri?.AbsolutePath == "/auth/admin/customers")
                {
                    return JsonResponse("""
                    [
                      { "userId": 61, "fullName": "", "email": "", "phone": "" },
                      { "userId": 61, "fullName": "Customer 61", "email": "customer61@example.com", "phone": "0911111111" }
                    ]
                    """);
                }

                return new HttpResponseMessage(System.Net.HttpStatusCode.NotFound);
            }),
            orderingHandler: new RecordingHttpMessageHandler(_ => new HttpResponseMessage(System.Net.HttpStatusCode.NotFound)));

        var result = await controller.GetCustomers();

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(result.Value, WebJson));
        var customer = json.RootElement.EnumerateArray().Single();
        Assert.Equal(61, customer.GetProperty("userID").GetInt32());
        Assert.Equal("Customer 61", customer.GetProperty("fullName").GetString());
        Assert.Equal("customer61@example.com", customer.GetProperty("email").GetString());
    }

    [Fact]
    public async Task SellerGetCustomers_DedupesDuplicateIdentityCustomers_WithinSellerScope()
    {
        var controller = CreateSellerController(
            identityHandler: new RecordingHttpMessageHandler(request =>
            {
                if (request.RequestUri?.AbsolutePath == "/auth/admin/customers")
                {
                    return JsonResponse("""
                    [
                      { "userId": 71, "fullName": "", "email": "", "phone": "" },
                      { "userId": 71, "fullName": "Customer 71", "email": "customer71@example.com", "phone": "0922222222" },
                      { "userId": 88, "fullName": "Out of scope", "email": "out@example.com", "phone": "0933333333" }
                    ]
                    """);
                }

                return new HttpResponseMessage(System.Net.HttpStatusCode.NotFound);
            }),
            orderingHandler: new RecordingHttpMessageHandler(request =>
            {
                if (request.RequestUri?.AbsolutePath == "/api/orders/admin/customers/ids")
                {
                    return JsonResponse("""
                    {
                      "success": true,
                      "data": [71, 71]
                    }
                    """);
                }

                return new HttpResponseMessage(System.Net.HttpStatusCode.NotFound);
            }));

        var result = await controller.GetCustomers();

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(result.Value, WebJson));
        var customer = json.RootElement.EnumerateArray().Single();
        Assert.Equal(71, customer.GetProperty("userID").GetInt32());
        Assert.Equal("Customer 71", customer.GetProperty("fullName").GetString());
        Assert.Equal("customer71@example.com", customer.GetProperty("email").GetString());
    }

    private static AdminCouponController CreateAdminController(
        RecordingHttpMessageHandler identityHandler,
        RecordingHttpMessageHandler orderingHandler)
    {
        var httpContext = CreateHttpContext(userId: 7001);
        return new AdminCouponController(CreateHttpClientFactory(identityHandler, orderingHandler))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = httpContext
            }
        };
    }

    private static SellerCouponController CreateSellerController(
        RecordingHttpMessageHandler identityHandler,
        RecordingHttpMessageHandler orderingHandler)
    {
        var httpContext = CreateHttpContext(userId: 7002);
        return new SellerCouponController(CreateHttpClientFactory(identityHandler, orderingHandler))
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
