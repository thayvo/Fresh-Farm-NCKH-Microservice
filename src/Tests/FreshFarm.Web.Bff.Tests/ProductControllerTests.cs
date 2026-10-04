using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using FreshFarm.Web.Bff.Areas.Seller.Models;
using FreshFarm.Web.Bff.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Xunit;
using AdminProductController = FreshFarm.Web.Bff.Areas.Admin.Controllers.ProductController;
using SellerProductController = FreshFarm.Web.Bff.Areas.Seller.Controllers.ProductController;

namespace FreshFarm.Web.Bff.Tests;

public sealed class ProductControllerTests
{
    [Fact]
    public async Task AdminManageProducts_DedupesDuplicateProductsCategoriesAndUnits_FromCatalog()
    {
        var handler = new RecordingHttpMessageHandler(CreateCatalogResponder());
        var controller = CreateAdminController(handler);

        var result = await controller.ManageProducts(
            searchTerm: null,
            categoryId: null,
            unitId: null,
            status: null,
            sortBy: null);

        var view = Assert.IsType<ViewResult>(result);
        var products = Assert.IsType<List<Product>>(view.Model);
        var product = Assert.Single(products);
        var categories = Assert.IsType<List<Category>>(view.ViewData["Categories"]);
        var category = Assert.Single(categories);
        var units = Assert.IsType<List<Unit>>(view.ViewData["Units"]);
        var unit = Assert.Single(units);

        Assert.Equal(301, product.ProductID);
        Assert.Equal("Cam sanh dac biet", product.ProductName);
        Assert.Equal("CAM-301", product.Sku);
        Assert.Equal("Trai cay huu co", product.Category.CategoryName);
        Assert.Equal("Hop 500g", product.Unit.UnitName);
        Assert.Equal("Mo ta ngan hon", product.ShortDescription);
        Assert.Single(product.ProductInfoes);

        Assert.Equal(41, category.CategoryID);
        Assert.Equal("Trai cay huu co", category.CategoryName);
        Assert.True(category.IsActive);

        Assert.Equal(8, unit.UnitID);
        Assert.Equal("Hop 500g", unit.UnitName);
        Assert.Equal("hop", unit.Symbol);
        Assert.True(unit.IsActive);

        Assert.Equal(3, handler.Requests.Count);
    }

    [Fact]
    public async Task SellerManageProducts_DedupesDuplicateProductsCategoriesAndUnits_FromCatalog()
    {
        var handler = new RecordingHttpMessageHandler(CreateCatalogResponder());
        var controller = CreateSellerController(handler);

        var result = await controller.ManageProducts(
            searchTerm: null,
            categoryId: null,
            unitId: null,
            status: null,
            sortBy: null);

        var view = Assert.IsType<ViewResult>(result);
        var products = Assert.IsType<List<Product>>(view.Model);
        var product = Assert.Single(products);
        var categories = Assert.IsType<List<Category>>(view.ViewData["Categories"]);
        var category = Assert.Single(categories);
        var units = Assert.IsType<List<Unit>>(view.ViewData["Units"]);
        var unit = Assert.Single(units);

        Assert.Equal(301, product.ProductID);
        Assert.Equal("Cam sanh dac biet", product.ProductName);
        Assert.Equal("CAM-301", product.Sku);
        Assert.Equal("Trai cay huu co", product.Category.CategoryName);
        Assert.Equal("Hop 500g", product.Unit.UnitName);
        Assert.Equal("Mo ta ngan hon", product.ShortDescription);
        Assert.Single(product.ProductInfoes);

        Assert.Equal(41, category.CategoryID);
        Assert.Equal("Trai cay huu co", category.CategoryName);
        Assert.True(category.IsActive);

        Assert.Equal(8, unit.UnitID);
        Assert.Equal("Hop 500g", unit.UnitName);
        Assert.Equal("hop", unit.Symbol);
        Assert.True(unit.IsActive);

        Assert.Equal(3, handler.Requests.Count);
    }

    private static Func<HttpRequestMessage, HttpResponseMessage> CreateCatalogResponder()
    {
        return request =>
        {
            if (request.RequestUri?.AbsolutePath == "/api/products")
            {
                return CreateJsonResponse("""
                [
                  {
                    "productId": 301,
                    "productName": "",
                    "sku": "",
                    "price": 0,
                    "status": false,
                    "stockQuantity": 0,
                    "categoryId": 41,
                    "unitId": 8,
                    "createdDate": "2026-03-22T00:00:00Z"
                  },
                  {
                    "productId": 301,
                    "productName": "Cam sanh dac biet",
                    "sku": "CAM-301",
                    "price": 125000,
                    "status": true,
                    "stockQuantity": 12,
                    "imageFileName": "cam.jpg",
                    "createdDate": "2026-03-22T00:00:00Z",
                    "updatedDate": "2026-03-24T01:00:00Z",
                    "shortDescription": "Mo ta ngan hon",
                    "longDescription": "Mo ta dai va day du hon",
                    "categoryId": 41,
                    "categoryName": "Trai cay huu co",
                    "unitId": 8,
                    "unitName": "Hop 500g",
                    "unitSymbol": "hop",
                    "productInfos": [
                      {
                        "weight": "500g",
                        "origin": "Viet Nam",
                        "standard": "VietGAP",
                        "preservation": "Bao quan lanh"
                      }
                    ]
                  }
                ]
                """);
            }

            if (request.RequestUri?.AbsolutePath == "/api/categories")
            {
                return CreateJsonResponse("""
                [
                  {
                    "categoryId": 41,
                    "categoryName": "",
                    "description": "",
                    "isActive": false,
                    "createdDate": "2026-03-20T00:00:00Z"
                  },
                  {
                    "categoryId": 41,
                    "categoryName": "Trai cay huu co",
                    "description": "Nhom trai cay tu nhien",
                    "isActive": true,
                    "createdDate": "2026-03-20T00:00:00Z",
                    "updatedDate": "2026-03-24T01:00:00Z"
                  }
                ]
                """);
            }

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
        };
    }

    private static HttpResponseMessage CreateJsonResponse(string json)
    {
        return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent(json)
        };
    }

    private static AdminProductController CreateAdminController(RecordingHttpMessageHandler handler)
    {
        return new AdminProductController(CreateHttpClientFactory(handler), new FakeProductImageStorageService())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = CreateHttpContext("7", "Admin")
            }
        };
    }

    private static SellerProductController CreateSellerController(RecordingHttpMessageHandler handler)
    {
        return new SellerProductController(CreateHttpClientFactory(handler), new FakeProductImageStorageService())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = CreateHttpContext("19", "Seller")
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

    private static HttpContext CreateHttpContext(string userId, string role)
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
            new Claim(ClaimTypes.Role, role),
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

    private sealed class FakeProductImageStorageService : IProductImageStorageService
    {
        public (bool isValid, string errorMessage) Validate(IFormFile file) => (true, string.Empty);

        public string Save(IFormFile file) => "saved-image.jpg";

        public void Delete(string? imageFileName)
        {
        }
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
