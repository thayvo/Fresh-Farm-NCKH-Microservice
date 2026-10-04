using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using FreshFarm.Web.Bff.Areas.Admin.Controllers;
using FreshFarm.Web.Bff.Areas.Admin.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace FreshFarm.Web.Bff.Tests;

public sealed class CatalogReadinessControllerTests
{
    [Fact]
    public async Task Index_DedupesDuplicateCategoriesAttributesAndReadinessRows_FromCatalog()
    {
        var handler = new RecordingHttpMessageHandler(request =>
        {
            if (request.RequestUri?.AbsolutePath == "/api/catalog/admin/readiness/center")
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("""
                    {
                      "stats": {
                        "totalCategories": 1,
                        "totalAttributes": 1,
                        "requiredAttributes": 1,
                        "productsReady": 0,
                        "productsMissing": 1,
                        "facetAttributes": 1
                      },
                      "filters": {
                        "q": "",
                        "categoryId": 41,
                        "state": "missing",
                        "stateOptions": [
                          { "value": "missing", "text": "" },
                          { "value": "missing", "text": "Thiếu thuộc tính" }
                        ],
                        "categories": [
                          { "categoryId": 41, "categoryName": "", "isActive": false },
                          { "categoryId": 41, "categoryName": "Trai cay huu co", "isActive": true }
                        ]
                      },
                      "attributes": [
                        {
                          "categoryAttributeId": 71,
                          "categoryId": 41,
                          "categoryName": "",
                          "attributeKey": "",
                          "displayName": "",
                          "inputType": "",
                          "isRequired": false,
                          "isFacet": false,
                          "sortOrder": 0,
                          "isActive": false,
                          "createdAt": "2026-03-20T00:00:00Z"
                        },
                        {
                          "categoryAttributeId": 71,
                          "categoryId": 41,
                          "categoryName": "Trai cay huu co",
                          "attributeKey": "origin_region",
                          "displayName": "Vung trong",
                          "inputType": "text",
                          "isRequired": true,
                          "isFacet": true,
                          "sortOrder": 2,
                          "placeholder": "Nhap vung trong",
                          "isActive": true,
                          "createdAt": "2026-03-20T00:00:00Z",
                          "updatedAt": "2026-03-24T01:00:00Z"
                        }
                      ],
                      "readiness": [
                        {
                          "productId": 901,
                          "productName": "",
                          "sku": "",
                          "status": true,
                          "categoryId": 41,
                          "categoryName": "",
                          "createdDate": "2026-03-20T00:00:00Z",
                          "totalRequired": 0,
                          "readyCount": 0,
                          "missingCount": 0,
                          "readinessScore": 0,
                          "missingAttributes": []
                        },
                        {
                          "productId": 901,
                          "productName": "Cam sanh huu co",
                          "sku": "CAM-901",
                          "status": true,
                          "categoryId": 41,
                          "categoryName": "Trai cay huu co",
                          "createdDate": "2026-03-24T01:00:00Z",
                          "totalRequired": 3,
                          "readyCount": 2,
                          "missingCount": 1,
                          "readinessScore": 67,
                          "missingAttributes": [ "Vung trong" ]
                        }
                      ]
                    }
                    """)
                };
            }

            return new HttpResponseMessage(System.Net.HttpStatusCode.NotFound);
        });
        var controller = CreateController(handler);

        var result = await controller.Index();

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<CatalogReadinessPageViewModel>(view.Model);
        var category = Assert.Single(model.CategoryOptions);
        var state = Assert.Single(model.StateOptions);
        var attribute = Assert.Single(model.Attributes);
        var readiness = Assert.Single(model.ReadinessRows);

        Assert.Equal(41, model.CategoryId);
        Assert.Equal("missing", model.State);
        Assert.Equal("Trai cay huu co", category.Text);
        Assert.True(category.IsActive);
        Assert.Equal("Thiếu thuộc tính", state.Text);
        Assert.Equal(71, attribute.CategoryAttributeId);
        Assert.Equal("origin_region", attribute.AttributeKey);
        Assert.Equal("Vung trong", attribute.DisplayName);
        Assert.True(attribute.IsRequired);
        Assert.True(attribute.IsFacet);
        Assert.Equal(901, readiness.ProductId);
        Assert.Equal("Cam sanh huu co", readiness.ProductName);
        Assert.Equal("CAM-901", readiness.Sku);
        Assert.Equal("Trai cay huu co", readiness.CategoryName);
        Assert.Equal(3, readiness.TotalRequired);
        Assert.Equal(1, readiness.MissingCount);
        Assert.Contains("Vung trong", readiness.MissingAttributes);
    }

    private static CatalogReadinessController CreateController(RecordingHttpMessageHandler handler)
    {
        var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://catalog.test")
        };

        return new CatalogReadinessController(new StaticHttpClientFactory(client))
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
