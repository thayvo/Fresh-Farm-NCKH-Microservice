using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using FreshFarm.Web.Bff.Areas.Admin.Controllers;
using FreshFarm.Web.Bff.Areas.Admin.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace FreshFarm.Web.Bff.Tests;

public sealed class CatalogModerationControllerTests
{
    [Fact]
    public async Task Index_DedupesDuplicateCatalogProducts_AndKeepsMostCompleteRow()
    {
        var handler = new RecordingHttpMessageHandler(request =>
        {
            if (request.RequestUri?.AbsolutePath == "/api/products")
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("""
                    [
                      {
                        "productId": 901,
                        "productName": "",
                        "sku": "",
                        "price": 0,
                        "status": true,
                        "stockQuantity": 0,
                        "categoryName": "",
                        "createdDate": "2026-03-20T00:00:00Z"
                      },
                      {
                        "productId": 901,
                        "productName": "Ruou nho ngoai nhap",
                        "sku": "RN-901",
                        "price": 450000,
                        "status": true,
                        "stockQuantity": 5,
                        "imageFileName": "wine.png",
                        "shortDescription": "San pham can kiem duyet",
                        "longDescription": "Mo ta co chua tu khoa nhay cam ruou",
                        "categoryName": "Do uong",
                        "createdDate": "2026-03-20T00:00:00Z",
                        "updatedDate": "2026-03-24T01:00:00Z"
                      }
                    ]
                    """)
                };
            }

            return new HttpResponseMessage(System.Net.HttpStatusCode.NotFound);
        });
        var controller = CreateController(handler);

        var result = await controller.Index();

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<ProductModerationPageViewModel>(view.Model);
        var row = Assert.Single(model.Rows);

        Assert.Equal(1, model.TotalProducts);
        Assert.Equal(1, model.FlaggedProducts);
        Assert.Equal(1, model.HighRiskProducts);
        Assert.Equal(0, model.HiddenProducts);
        Assert.Equal("Ruou nho ngoai nhap", row.ProductName);
        Assert.Equal("RN-901", row.Sku);
        Assert.Equal("Do uong", row.CategoryName);
        Assert.Equal("flagged", row.ModerationState);
        Assert.Equal("high", row.RiskLevel);
        Assert.Contains("ruou", row.MatchedKeywords, StringComparer.OrdinalIgnoreCase);
    }

    private static CatalogModerationController CreateController(RecordingHttpMessageHandler handler)
    {
        var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://catalog.test")
        };

        return new CatalogModerationController(new StaticHttpClientFactory(client))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = CreateHttpContext("7")
            }
        };
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
