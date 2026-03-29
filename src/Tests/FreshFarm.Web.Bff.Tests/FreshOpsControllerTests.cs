using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using FreshFarm.Web.Bff.Areas.Admin.Controllers;
using FreshFarm.Web.Bff.Areas.Admin.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace FreshFarm.Web.Bff.Tests;

public sealed class FreshOpsControllerTests
{
    [Fact]
    public async Task Index_DedupesDuplicateSellerStatusLotRecallAndFefoRows_FromCatalog()
    {
        var handler = new RecordingHttpMessageHandler(request =>
        {
            if (request.RequestUri?.AbsolutePath == "/api/catalog/admin/fresh-ops/center")
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("""
                    {
                      "stats": {
                        "totalLots": 1,
                        "expiringSoonLots": 1,
                        "expiredLots": 0,
                        "openRecalls": 1,
                        "traceCoverage": 100
                      },
                      "filters": {
                        "q": "",
                        "sellerId": 24,
                        "status": "active",
                        "statusOptions": [
                          { "value": "active", "text": "" },
                          { "value": "active", "text": "Lô đang hoạt động" }
                        ],
                        "sellers": [
                          { "sellerId": 24, "text": "" },
                          { "sellerId": 24, "text": "Nong trai huu co 24" }
                        ]
                      },
                      "lots": [
                        {
                          "freshInventoryLotId": 501,
                          "productId": 0,
                          "productName": "",
                          "sku": "",
                          "sellerId": 24,
                          "lotCode": "",
                          "receivedAt": "2026-03-20T00:00:00Z",
                          "initialQuantity": 0,
                          "remainingQuantity": 0
                        },
                        {
                          "freshInventoryLotId": 501,
                          "productId": 901,
                          "productName": "Xa lach huu co",
                          "sku": "XL-901",
                          "sellerId": 24,
                          "lotCode": "LOT-501",
                          "traceCode": "TRACE-501",
                          "farmName": "Farm 24",
                          "originRegion": "Da Lat",
                          "receivedAt": "2026-03-24T01:00:00Z",
                          "expiresAt": "2026-03-26T00:00:00Z",
                          "initialQuantity": 100,
                          "remainingQuantity": 40,
                          "unitCost": 12500,
                          "status": "active",
                          "qualityStatus": "ok",
                          "notes": "Can uu tien xuat truoc",
                          "daysToExpiry": 2
                        }
                      ],
                      "recalls": [
                        {
                          "freshQualityRecallId": 77,
                          "recallCode": "",
                          "startedAt": "2026-03-20T00:00:00Z"
                        },
                        {
                          "freshQualityRecallId": 77,
                          "recallCode": "RCL-077",
                          "productId": 901,
                          "productName": "Xa lach huu co",
                          "freshInventoryLotId": 501,
                          "lotCode": "LOT-501",
                          "sellerId": 24,
                          "recallType": "quality_issue",
                          "severity": "high",
                          "status": "open",
                          "title": "Canh bao chat luong",
                          "reason": "Lo hang co dau hieu giam chat luong",
                          "actionRequired": "Tam dung ban",
                          "startedAt": "2026-03-24T01:00:00Z"
                        }
                      ],
                      "fefoQueue": [
                        {
                          "freshInventoryLotId": 501,
                          "productName": "",
                          "sku": "",
                          "sellerId": 24,
                          "lotCode": "",
                          "remainingQuantity": 0,
                          "expiresAt": "2026-03-24T00:00:00Z",
                          "daysToExpiry": 0,
                          "qualityStatus": ""
                        },
                        {
                          "freshInventoryLotId": 501,
                          "productName": "Xa lach huu co",
                          "sku": "XL-901",
                          "sellerId": 24,
                          "lotCode": "LOT-501",
                          "remainingQuantity": 40,
                          "expiresAt": "2026-03-26T00:00:00Z",
                          "daysToExpiry": 2,
                          "qualityStatus": "ok"
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
        var model = Assert.IsType<FreshOpsPageViewModel>(view.Model);
        var seller = Assert.Single(model.SellerOptions);
        var status = Assert.Single(model.StatusOptions);
        var lot = Assert.Single(model.Lots);
        var recall = Assert.Single(model.Recalls);
        var fefo = Assert.Single(model.FefoQueue);

        Assert.Equal(24, model.SellerId);
        Assert.Equal("active", model.Status);
        Assert.Equal("Nong trai huu co 24", seller.Text);
        Assert.Equal("Lô đang hoạt động", status.Text);

        Assert.Equal(501, lot.FreshInventoryLotId);
        Assert.Equal("Xa lach huu co", lot.ProductName);
        Assert.Equal("XL-901", lot.Sku);
        Assert.Equal("LOT-501", lot.LotCode);
        Assert.Equal("TRACE-501", lot.TraceCode);
        Assert.Equal("Farm 24", lot.FarmName);
        Assert.Equal("Da Lat", lot.OriginRegion);
        Assert.Equal(100, lot.InitialQuantity);
        Assert.Equal(40, lot.RemainingQuantity);
        Assert.Equal("active", lot.Status);
        Assert.Equal("ok", lot.QualityStatus);

        Assert.Equal(77, recall.FreshQualityRecallId);
        Assert.Equal("RCL-077", recall.RecallCode);
        Assert.Equal("Xa lach huu co", recall.ProductName);
        Assert.Equal("LOT-501", recall.LotCode);
        Assert.Equal("Canh bao chat luong", recall.Title);
        Assert.Equal("Tam dung ban", recall.ActionRequired);

        Assert.Equal(501, fefo.FreshInventoryLotId);
        Assert.Equal("Xa lach huu co", fefo.ProductName);
        Assert.Equal("XL-901", fefo.Sku);
        Assert.Equal("LOT-501", fefo.LotCode);
        Assert.Equal(40, fefo.RemainingQuantity);
        Assert.Equal(2, fefo.DaysToExpiry);
        Assert.Equal("ok", fefo.QualityStatus);
        Assert.Equal(24, model.NewLot.SellerId);
    }

    private static FreshOpsController CreateController(RecordingHttpMessageHandler handler)
    {
        var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://catalog.test")
        };

        return new FreshOpsController(new StaticHttpClientFactory(client))
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
