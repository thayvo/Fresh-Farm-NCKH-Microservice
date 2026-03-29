using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using FreshFarm.Web.Bff.Areas.Admin.Controllers;
using FreshFarm.Web.Bff.Areas.Admin.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Xunit;

namespace FreshFarm.Web.Bff.Tests;

public sealed class MerchantControllerTests
{
    [Fact]
    public async Task Index_ForwardsReviewFiltersToIdentity()
    {
        var handler = new RecordingHttpMessageHandler(_ =>
            new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("""
                {
                  "page": 1,
                  "pageSize": 20,
                  "total": 0,
                  "totalPages": 1,
                  "stats": {},
                  "filters": {
                    "status": "review",
                    "queue": "approval",
                    "reviewStatus": "pending",
                    "reviewWindow": "7d",
                    "statusOptions": [],
                    "queueOptions": [],
                    "reviewStatusOptions": [],
                    "reviewWindowOptions": [],
                    "rejectReasonTemplates": ["Thiếu ảnh CCCD mặt sau rõ nét."]
                  },
                  "merchants": []
                }
                """)
            });
        var controller = CreateController(handler);

        var result = await controller.Index(q: "seller", status: "review", queue: "approval", reviewStatus: "pending", reviewWindow: "7d", page: 2, selectedSellerId: null);

        Assert.IsType<ViewResult>(result);
        Assert.Single(handler.Requests);
        Assert.Equal("https://identity.test/auth/admin/merchants?page=2&pageSize=20&search=seller&status=review&queue=approval&reviewStatus=pending&reviewWindow=7d", handler.Requests[0].RequestUri?.ToString());
    }

    [Fact]
    public async Task Index_DedupesDuplicateMerchants_AndKeepsMostCompleteRow()
    {
        var handler = new RecordingHttpMessageHandler(request =>
        {
            if (request.RequestUri?.AbsolutePath == "/auth/admin/merchants")
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("""
                    {
                      "page": 1,
                      "pageSize": 20,
                      "total": 2,
                      "totalPages": 1,
                      "stats": {},
                      "filters": {
                        "statusOptions": [],
                        "queueOptions": [],
                        "reviewStatusOptions": [],
                        "reviewWindowOptions": [],
                        "rejectReasonTemplates": []
                      },
                      "merchants": [
                        {
                          "sellerId": 48,
                          "reviewStatus": "pending"
                        },
                        {
                          "sellerId": 48,
                          "shopName": "Reject Store 48",
                          "userName": "seller48",
                          "fullName": "Seller 48",
                          "email": "seller48@example.com",
                          "phone": "0912345678",
                          "storeName": "Reject Store 48",
                          "storeAddress": "12 Nguyen Trai",
                          "addressSummary": "12 Nguyen Trai, Quan 1",
                          "reviewStatus": "pending",
                          "reviewStatusLabel": "Chờ duyệt",
                          "profileScore": 85,
                          "queueBucket": "approval",
                          "recommendedAction": "Review KYC"
                        }
                      ]
                    }
                    """)
                };
            }

            if (request.RequestUri?.AbsolutePath == "/auth/admin/merchants/48")
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("""
                    {
                      "sellerId": 48,
                      "shopName": "Reject Store 48",
                      "userName": "seller48",
                      "fullName": "Seller 48",
                      "email": "seller48@example.com",
                      "reviewStatus": "pending",
                      "reviewStatusLabel": "Chờ duyệt"
                    }
                    """)
                };
            }

            return new HttpResponseMessage(System.Net.HttpStatusCode.NotFound);
        });
        var controller = CreateController(handler);

        var result = await controller.Index();

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<MerchantManagementPageViewModel>(view.Model);
        var merchant = Assert.Single(model.Merchants);
        Assert.Equal(48, merchant.SellerId);
        Assert.Equal("Reject Store 48", merchant.ShopName);
        Assert.Equal("seller48@example.com", merchant.Email);
        Assert.Equal("Chờ duyệt", merchant.ReviewStatusLabel);
        Assert.Equal(48, model.SelectedSellerId);
        Assert.NotNull(model.Details);
        Assert.Equal(48, model.Details!.SellerId);
    }

    [Fact]
    public async Task Approve_SetsErrorMessage_WhenIdentityRejectsIncompleteKyc()
    {
        var handler = new RecordingHttpMessageHandler(_ =>
            new HttpResponseMessage(System.Net.HttpStatusCode.BadRequest)
            {
                Content = new StringContent("{\"message\":\"Hồ sơ KYC chưa đầy đủ. Cần đủ thông tin CCCD và ảnh CCCD hai mặt trước khi duyệt người bán.\"}")
            });
        var controller = CreateController(handler);

        var result = await controller.Approve(88, q: "seller", status: "review", queue: "approval", reviewStatus: "pending", reviewWindow: "7d", page: 2, selectedSellerId: 88);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(MerchantController.Index), redirect.ActionName);
        Assert.Equal("Hồ sơ KYC chưa đầy đủ. Cần đủ thông tin CCCD và ảnh CCCD hai mặt trước khi duyệt người bán.", controller.TempData["ErrorMessage"]);

        Assert.Single(handler.Requests);
        var request = handler.Requests[0];
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://identity.test/auth/admin/merchants/88/approve", request.RequestUri?.ToString());
        Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
    }

    [Fact]
    public async Task Approve_SetsSuccessMessage_WhenIdentityApprovesSeller()
    {
        var handler = new RecordingHttpMessageHandler(_ =>
            new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("{\"message\":\"Đã duyệt hồ sơ và cấp quyền người bán.\"}")
            });
        var controller = CreateController(handler);

        var result = await controller.Approve(89, q: null, status: null, queue: null, reviewStatus: null, reviewWindow: null, page: 1, selectedSellerId: 89);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(MerchantController.Index), redirect.ActionName);
        Assert.Equal("Đã duyệt hồ sơ và cấp quyền người bán.", controller.TempData["SuccessMessage"]);

        Assert.Single(handler.Requests);
        Assert.Equal("https://identity.test/auth/admin/merchants/89/approve", handler.Requests[0].RequestUri?.ToString());
    }

    [Fact]
    public async Task Reject_SetsErrorMessage_WhenReasonMissing()
    {
        var handler = new RecordingHttpMessageHandler(_ => throw new InvalidOperationException("Should not call Identity"));
        var controller = CreateController(handler);

        var result = await controller.Reject(90, reason: "   ", q: "seller", status: "review", queue: "profile_fix", reviewStatus: "rejected", reviewWindow: "30d", page: 3, selectedSellerId: 90);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(MerchantController.Index), redirect.ActionName);
        Assert.Equal("Cần nhập lý do từ chối để nhà bán hàng biết phải bổ sung gì.", controller.TempData["ErrorMessage"]);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Reject_SetsSuccessMessage_AndSendsReason_WhenIdentityRejectsSeller()
    {
        var handler = new RecordingHttpMessageHandler(_ =>
            new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("{\"message\":\"Đã từ chối hồ sơ người bán và gửi lại yêu cầu bổ sung.\"}")
            });
        var controller = CreateController(handler);

        var result = await controller.Reject(91, "Thiếu ảnh CCCD mặt sau.", q: null, status: "review", queue: "profile_fix", reviewStatus: "pending", reviewWindow: "today", page: 1, selectedSellerId: 91);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(MerchantController.Index), redirect.ActionName);
        Assert.Equal("Đã từ chối hồ sơ người bán và gửi lại yêu cầu bổ sung.", controller.TempData["SuccessMessage"]);

        Assert.Single(handler.Requests);
        var request = handler.Requests[0];
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://identity.test/auth/admin/merchants/91/reject", request.RequestUri?.ToString());
        var body = await request.Content!.ReadAsStringAsync();
        using var json = System.Text.Json.JsonDocument.Parse(body);
        Assert.Equal("Thiếu ảnh CCCD mặt sau.", json.RootElement.GetProperty("reason").GetString());
    }

    private static MerchantController CreateController(RecordingHttpMessageHandler handler)
    {
        var token = CreateAccessToken("7");
        var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://identity.test")
        };

        var httpContext = new DefaultHttpContext();
        httpContext.Features.Set<ISessionFeature>(new SessionFeature
        {
            Session = new TestSession()
        });
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, "7"),
            new Claim("ff_access_token", token)
        ], "TestAuth"));

        return new MerchantController(new StaticHttpClientFactory(client))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = httpContext
            },
            TempData = new TempDataDictionary(httpContext, new TestTempDataProvider())
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
            Requests.Add(CloneRequest(request));
            return Task.FromResult(responder(request));
        }

        private static HttpRequestMessage CloneRequest(HttpRequestMessage request)
        {
            var clone = new HttpRequestMessage(request.Method, request.RequestUri);
            foreach (var header in request.Headers)
            {
                clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            if (request.Content is not null)
            {
                var body = request.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                clone.Content = new StringContent(body);
                foreach (var header in request.Content.Headers)
                {
                    clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }
            }

            return clone;
        }
    }

    private sealed class TestTempDataProvider : ITempDataProvider
    {
        private Dictionary<string, object> _values = new();

        public IDictionary<string, object> LoadTempData(HttpContext context) => _values;

        public void SaveTempData(HttpContext context, IDictionary<string, object> values)
        {
            _values = new Dictionary<string, object>(values);
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
