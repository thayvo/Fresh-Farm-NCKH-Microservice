using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using FreshFarm.Web.Bff.Areas.Admin.Controllers;
using FreshFarm.Web.Bff.Areas.Seller.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace FreshFarm.Web.Bff.Tests;

public sealed class ReportControllerTests
{
    [Fact]
    public async Task Shipping_DedupesDuplicateStaffPerformanceDistributionsRecentRowsAndDeliveryStaffs()
    {
        var handler = new RecordingHttpMessageHandler(request =>
        {
            if (request.RequestUri?.AbsolutePath == "/api/orders/admin/reports/shipping")
            {
                return CreateJsonResponse("""
                {
                  "currentPage": 1,
                  "pageSize": 10,
                  "totalPages": 1,
                  "staffPerformances": [
                    { "staffName": "", "totalOrders": 0, "successRate": 0 },
                    { "staffName": "Shipper A", "totalOrders": 12, "successRate": 98.5 }
                  ],
                  "deliveryTimeDistributions": [
                    { "timeRange": "0-2 ngay", "orderCount": 1 },
                    { "timeRange": "0-2 ngay", "orderCount": 8 }
                  ],
                  "recentShippings": [
                    {
                      "orderCode": "ORD-000555",
                      "customerName": "",
                      "customerPhone": "",
                      "deliveryStaffName": "",
                      "deliveryAddress": "",
                      "shippingDateFormatted": "",
                      "expectedDeliveryDateFormatted": "",
                      "statusText": ""
                    },
                    {
                      "orderCode": "ORD-000555",
                      "customerName": "Nguyen Van A",
                      "customerPhone": "0901234567",
                      "deliveryStaffName": "Shipper A",
                      "deliveryAddress": "123 Duong Le Loi",
                      "shippingDateFormatted": "24/03/2026",
                      "expectedDeliveryDateFormatted": "25/03/2026",
                      "statusText": "Đang giao",
                      "actualDeliveryDays": 1
                    }
                  ],
                  "deliveryStaffs": [
                    { "value": "9", "text": "" },
                    { "value": "9", "text": "Shipper A" }
                  ]
                }
                """);
            }

            return new HttpResponseMessage(System.Net.HttpStatusCode.NotFound);
        });
        var controller = CreateController(handler);

        var result = await controller.Shipping(null, null, null, null, null, null, 1, 10);

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<ShippingReportViewModel>(view.Model);
        var staffPerformance = Assert.Single(model.StaffPerformances);
        var distribution = Assert.Single(model.DeliveryTimeDistributions);
        var recent = Assert.Single(model.RecentShippings);
        var staffOption = Assert.Single(model.DeliveryStaffs);

        Assert.Equal("Shipper A", staffPerformance.StaffName);
        Assert.Equal(12, staffPerformance.TotalOrders);
        Assert.Equal(98.5m, staffPerformance.SuccessRate);

        Assert.Equal("0-2 ngay", distribution.TimeRange);
        Assert.Equal(8, distribution.OrderCount);

        Assert.Equal("ORD-000555", recent.OrderCode);
        Assert.Equal("Nguyen Van A", recent.CustomerName);
        Assert.Equal("0901234567", recent.CustomerPhone);
        Assert.Equal("Shipper A", recent.DeliveryStaffName);
        Assert.Equal("123 Duong Le Loi", recent.DeliveryAddress);
        Assert.Equal("24/03/2026", recent.ShippingDateFormatted);
        Assert.Equal("25/03/2026", recent.ExpectedDeliveryDateFormatted);
        Assert.Equal("Đang giao", recent.StatusText);
        Assert.Equal(1, recent.ActualDeliveryDays);

        Assert.Equal("9", staffOption.Value);
        Assert.Equal("Shipper A", staffOption.Text);
    }

    [Fact]
    public async Task Revenue_Product_And_Review_DedupeRepresentativeCollections()
    {
        var handler = new RecordingHttpMessageHandler(request =>
        {
            var path = request.RequestUri?.AbsolutePath;

            if (path == "/api/orders/admin/reports/revenue")
            {
                return CreateJsonResponse("""
                {
                  "topProducts": [
                    { "productName": "Ca chua", "totalRevenue": 10000 },
                    { "productName": "Ca chua", "totalRevenue": 25000 }
                  ],
                  "dailyRevenues": [
                    { "date": "2026-03-24T00:00:00Z", "dateFormatted": "", "totalOrders": 0, "totalProducts": 0, "revenue": 0, "profit": 0 },
                    { "date": "2026-03-24T00:00:00Z", "dateFormatted": "24/03/2026", "totalOrders": 5, "totalProducts": 8, "revenue": 250000, "profit": 75000 }
                  ]
                }
                """);
            }

            if (path == "/api/orders/admin/reports/products")
            {
                return CreateJsonResponse("""
                {
                  "topSellingProducts": [
                    { "productName": "Rau muong", "quantitySold": 5 },
                    { "productName": "Rau muong", "quantitySold": 12 }
                  ],
                  "categoryRevenues": [
                    { "categoryName": "Rau cu", "revenue": 100000 },
                    { "categoryName": "Rau cu", "revenue": 250000 }
                  ],
                  "productPerformances": [
                    {
                      "imageFileName": null,
                      "productName": "Rau muong",
                      "sku": "",
                      "categoryName": "",
                      "quantitySold": 0,
                      "stockStatus": "",
                      "totalRevenue": 0
                    },
                    {
                      "imageFileName": "rau-muong.jpg",
                      "productName": "Rau muong",
                      "sku": "RM-01",
                      "categoryName": "Rau cu",
                      "quantitySold": 12,
                      "stockStatus": "Con hang",
                      "totalRevenue": 250000
                    }
                  ]
                }
                """);
            }

            if (path == "/api/orders/admin/reports/reviews")
            {
                return CreateJsonResponse("""
                {
                  "starDistributions": [
                    { "label": "5 sao", "count": 1 },
                    { "label": "5 sao", "count": 8 }
                  ],
                  "topMentionedTopics": [ "tuoi", "TƯƠI", "giao nhanh" ],
                  "recentReviews": [
                    {
                      "reviewId": 501,
                      "customerName": "",
                      "productName": "",
                      "rating": 0,
                      "comment": "",
                      "createdAt": "2026-03-20T00:00:00Z",
                      "createdAtFormatted": ""
                    },
                    {
                      "reviewId": 501,
                      "userId": 18,
                      "customerName": "Nguyen Van A",
                      "productName": "Rau muong",
                      "productImageFileName": "rau-muong.jpg",
                      "rating": 5,
                      "starDisplay": "★★★★★",
                      "comment": "Rau tuoi va giao nhanh",
                      "createdAt": "2026-03-24T00:00:00Z",
                      "createdAtFormatted": "24/03/2026"
                    }
                  ]
                }
                """);
            }

            return new HttpResponseMessage(System.Net.HttpStatusCode.NotFound);
        });
        var revenueController = CreateController(handler);
        var productController = CreateController(handler);
        var reviewController = CreateController(handler);

        var revenueResult = await revenueController.Revenue(null, null, "day");
        var productResult = await productController.Product(null, null, null, 1);
        var reviewResult = await reviewController.Review(null, null, null, 1, 10);

        var revenueView = Assert.IsType<ViewResult>(revenueResult);
        var revenueModel = Assert.IsType<RevenueReportViewModel>(revenueView.Model);
        var revenueTop = Assert.Single(revenueModel.TopProducts);
        var daily = Assert.Single(revenueModel.DailyRevenues);

        Assert.Equal("Ca chua", revenueTop.ProductName);
        Assert.Equal(25000, revenueTop.TotalRevenue);
        Assert.Equal("24/03/2026", daily.DateFormatted);
        Assert.Equal(5, daily.TotalOrders);
        Assert.Equal(8, daily.TotalProducts);
        Assert.Equal(250000, daily.Revenue);
        Assert.Equal(75000, daily.Profit);

        var productView = Assert.IsType<ViewResult>(productResult);
        var productModel = Assert.IsType<ProductReportViewModel>(productView.Model);
        var topSelling = Assert.Single(productModel.TopSellingProducts);
        var categoryRevenue = Assert.Single(productModel.CategoryRevenues);
        var performance = Assert.Single(productModel.ProductPerformances);

        Assert.Equal("Rau muong", topSelling.ProductName);
        Assert.Equal(12, topSelling.QuantitySold);
        Assert.Equal("Rau cu", categoryRevenue.CategoryName);
        Assert.Equal(250000, categoryRevenue.Revenue);
        Assert.Equal("rau-muong.jpg", performance.ImageFileName);
        Assert.Equal("RM-01", performance.Sku);
        Assert.Equal("Rau cu", performance.CategoryName);
        Assert.Equal(12, performance.QuantitySold);
        Assert.Equal("Con hang", performance.StockStatus);
        Assert.Equal(250000, performance.TotalRevenue);

        var reviewView = Assert.IsType<ViewResult>(reviewResult);
        var reviewModel = Assert.IsType<ReviewReportViewModel>(reviewView.Model);
        var starDistribution = Assert.Single(reviewModel.StarDistributions);
        var recentReview = Assert.Single(reviewModel.RecentReviews);

        Assert.Equal("5 sao", starDistribution.Label);
        Assert.Equal(8, starDistribution.Count);
        Assert.Equal(2, reviewModel.TopMentionedTopics.Count);
        Assert.Contains("tuoi", reviewModel.TopMentionedTopics, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("giao nhanh", reviewModel.TopMentionedTopics, StringComparer.OrdinalIgnoreCase);

        Assert.Equal(501, recentReview.ReviewID);
        Assert.Equal(18, recentReview.UserID);
        Assert.Equal("Nguyen Van A", recentReview.CustomerName);
        Assert.Equal("Rau muong", recentReview.ProductName);
        Assert.Equal("rau-muong.jpg", recentReview.ProductImageFileName);
        Assert.Equal(5, recentReview.Rating);
        Assert.Equal("★★★★★", recentReview.StarDisplay);
        Assert.Equal("Rau tuoi va giao nhanh", recentReview.Comment);
        Assert.Equal("24/03/2026", recentReview.CreatedAtFormatted);
    }

    private static ReportController CreateController(RecordingHttpMessageHandler handler)
    {
        var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://ordering.test")
        };

        return new ReportController(new StaticHttpClientFactory(client))
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
