using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using FreshFarm.Web.Bff.Areas.Seller.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Xunit;
using AdminCategoryController = FreshFarm.Web.Bff.Areas.Admin.Controllers.CategoryController;
using AdminUnitController = FreshFarm.Web.Bff.Areas.Admin.Controllers.UnitController;

namespace FreshFarm.Web.Bff.Tests;

public sealed class CategoryUnitControllerTests
{
    [Fact]
    public async Task AdminManageCategories_DedupesDuplicateCatalogCategories()
    {
        var handler = new RecordingHttpMessageHandler(request =>
        {
            if (request.RequestUri?.AbsolutePath == "/api/categories")
            {
                return CreateJsonResponse("""
                [
                  {
                    "categoryId": 41,
                    "categoryName": "",
                    "description": "",
                    "slug": "",
                    "isActive": false,
                    "createdDate": "2026-03-20T00:00:00Z"
                  },
                  {
                    "categoryId": 41,
                    "categoryName": "Trai cay huu co",
                    "description": "Nhom trai cay tot cho suc khoe",
                    "imageCategoriesName": "fruit.png",
                    "slug": "trai-cay-huu-co",
                    "isActive": true,
                    "createdDate": "2026-03-20T00:00:00Z",
                    "updatedDate": "2026-03-24T01:00:00Z"
                  }
                ]
                """);
            }

            return new HttpResponseMessage(System.Net.HttpStatusCode.NotFound);
        });
        var controller = CreateCategoryController(handler);

        var result = await controller.ManageCategories(page: 1, search: null);

        var view = Assert.IsType<ViewResult>(result);
        var categories = Assert.IsType<List<Category>>(view.Model);
        var category = Assert.Single(categories);

        Assert.Equal(41, category.CategoryID);
        Assert.Equal("Trai cay huu co", category.CategoryName);
        Assert.Equal("Nhom trai cay tot cho suc khoe", category.Description);
        Assert.Equal("fruit.png", category.ImageCategoriesName);
        Assert.Equal("trai-cay-huu-co", category.Slug);
        Assert.True(category.IsActive);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task AdminUnitIndex_DedupesDuplicateCatalogUnits()
    {
        var handler = new RecordingHttpMessageHandler(request =>
        {
            if (request.RequestUri?.AbsolutePath == "/api/units")
            {
                return CreateJsonResponse("""
                [
                  {
                    "unitId": 8,
                    "unitName": "",
                    "symbol": "",
                    "description": "",
                    "isActive": false,
                    "createdDate": "2026-03-18T00:00:00Z"
                  },
                  {
                    "unitId": 8,
                    "unitName": "Hop 500g",
                    "symbol": "hop",
                    "description": "Dong goi 500 gram",
                    "isActive": true,
                    "createdDate": "2026-03-24T01:00:00Z"
                  }
                ]
                """);
            }

            return new HttpResponseMessage(System.Net.HttpStatusCode.NotFound);
        });
        var controller = CreateUnitController(handler);

        var result = await controller.Index();

        var view = Assert.IsType<ViewResult>(result);
        var units = Assert.IsType<List<UnitViewModel>>(view.Model);
        var unit = Assert.Single(units);

        Assert.Equal(8, unit.UnitID);
        Assert.Equal("Hop 500g", unit.UnitName);
        Assert.Equal("hop", unit.Symbol);
        Assert.Equal("Dong goi 500 gram", unit.Description);
        Assert.True(unit.IsActive);
        Assert.Equal("Khác", unit.UnitType);
        Assert.Single(handler.Requests);
    }

    private static AdminCategoryController CreateCategoryController(RecordingHttpMessageHandler handler)
    {
        return new AdminCategoryController(CreateHttpClientFactory(handler))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = CreateHttpContext("7")
            }
        };
    }

    private static AdminUnitController CreateUnitController(RecordingHttpMessageHandler handler)
    {
        return new AdminUnitController(CreateHttpClientFactory(handler))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = CreateHttpContext("7")
            }
        };
    }

    private static IHttpClientFactory CreateHttpClientFactory(RecordingHttpMessageHandler handler)
    {
        var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://catalog.test")
        };

        return new StaticHttpClientFactory(client);
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

    private static HttpResponseMessage CreateJsonResponse(string json)
    {
        return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent(json)
        };
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
        public List<HttpRequestMessage> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(new HttpRequestMessage(request.Method, request.RequestUri));
            return Task.FromResult(responder(request));
        }
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

        public bool TryGetValue(string key, out byte[] value)
        {
            if (_store.TryGetValue(key, out var stored))
            {
                value = stored;
                return true;
            }

            value = null!;
            return false;
        }
    }
}
