using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using FreshFarm.Web.Bff.Areas.Admin.Controllers;
using FreshFarm.Web.Bff.Areas.Seller.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace FreshFarm.Web.Bff.Tests;

public sealed class LoyaltyControllerTests
{
    [Fact]
    public async Task Index_DedupesDuplicateTopUsersAndRecentActivities_FromOrdering()
    {
        var handler = new RecordingHttpMessageHandler(request =>
        {
            if (request.RequestUri?.AbsolutePath == "/api/orders/admin/loyalty/dashboard")
            {
                return CreateJsonResponse("""
                {
                  "totalEarned": 500,
                  "totalRedeemed": 120,
                  "usersWithPoints": 12,
                  "currentYear": 2026,
                  "currentQuarter": 1,
                  "topQuarterUsers": [
                    { "userID": 21, "fullName": "", "totalPoints": 0, "quarterPoints": 0, "rankName": "" },
                    { "userID": 21, "fullName": "Nguyen Van A", "totalPoints": 150, "quarterPoints": 80, "rankName": "Gold" }
                  ],
                  "recentActivities": [
                    {
                      "createdAt": "2026-03-24T09:00:00Z",
                      "userID": 21,
                      "userName": "",
                      "points": 10,
                      "direction": "earn",
                      "reason": ""
                    },
                    {
                      "createdAt": "2026-03-24T09:00:00Z",
                      "userID": 21,
                      "userName": "Nguyen Van A",
                      "orderID": 501,
                      "points": 10,
                      "direction": "earn",
                      "reason": "Thanh toan don hang"
                    }
                  ]
                }
                """);
            }

            return new HttpResponseMessage(System.Net.HttpStatusCode.NotFound);
        });
        var controller = CreateController(handler);

        var result = await controller.Index();

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<LoyaltyDashboardVM>(view.Model);
        var topUser = Assert.Single(model.TopQuarterUsers);
        var activity = Assert.Single(model.RecentActivities);

        Assert.Equal(21, topUser.UserID);
        Assert.Equal("Nguyen Van A", topUser.FullName);
        Assert.Equal(150, topUser.TotalPoints);
        Assert.Equal(80, topUser.QuarterPoints);
        Assert.Equal("Gold", topUser.RankName);

        Assert.Equal(21, activity.UserID);
        Assert.Equal("Nguyen Van A", activity.UserName);
        Assert.Equal(501, activity.OrderID);
        Assert.Equal(10, activity.Points);
        Assert.Equal("earn", activity.Direction);
        Assert.Equal("Thanh toan don hang", activity.Reason);
    }

    [Fact]
    public async Task Users_And_History_DedupeDuplicateRows_FromOrdering()
    {
        var handler = new RecordingHttpMessageHandler(request =>
        {
            var path = request.RequestUri?.AbsolutePath;

            if (path == "/api/orders/admin/loyalty/users")
            {
                return CreateJsonResponse("""
                {
                  "query": "",
                  "page": 1,
                  "pageSize": 10,
                  "total": 2,
                  "rows": [
                    { "userID": 31, "fullName": "", "totalPoints": 0, "currentQuarterPoints": 0, "rankName": "" },
                    { "userID": 31, "fullName": "Tran Thi B", "totalPoints": 220, "currentQuarterPoints": 90, "rankName": "Platinum" }
                  ]
                }
                """);
            }

            if (path == "/api/orders/admin/loyalty/history")
            {
                return CreateJsonResponse("""
                {
                  "page": 1,
                  "pageSize": 20,
                  "total": 2,
                  "rows": [
                    {
                      "createdAt": "2026-03-23T10:00:00Z",
                      "userID": 31,
                      "userName": "",
                      "points": -20,
                      "direction": "redeem",
                      "reason": ""
                    },
                    {
                      "createdAt": "2026-03-23T10:00:00Z",
                      "userID": 31,
                      "userName": "Tran Thi B",
                      "orderID": 777,
                      "points": -20,
                      "direction": "redeem",
                      "reason": "Doi voucher"
                    }
                  ]
                }
                """);
            }

            return new HttpResponseMessage(System.Net.HttpStatusCode.NotFound);
        });

        var usersController = CreateController(handler);
        var historyController = CreateController(handler);

        var usersResult = await usersController.Users(null, 1, 10);
        var historyResult = await historyController.History(31, "redeem", null, null, 1, 20);

        var usersView = Assert.IsType<ViewResult>(usersResult);
        var usersModel = Assert.IsType<LoyaltyUsersVM>(usersView.Model);
        var user = Assert.Single(usersModel.Rows);

        Assert.Equal(31, user.UserID);
        Assert.Equal("Tran Thi B", user.FullName);
        Assert.Equal(220, user.TotalPoints);
        Assert.Equal(90, user.CurrentQuarterPoints);
        Assert.Equal("Platinum", user.RankName);

        var historyView = Assert.IsType<ViewResult>(historyResult);
        var historyModel = Assert.IsType<LoyaltyHistoryVM>(historyView.Model);
        var row = Assert.Single(historyModel.Rows);

        Assert.Equal(31, row.UserID);
        Assert.Equal("Tran Thi B", row.UserName);
        Assert.Equal(777, row.OrderID);
        Assert.Equal(-20, row.Points);
        Assert.Equal("redeem", row.Direction);
        Assert.Equal("Doi voucher", row.Reason);
    }

    private static LoyaltyController CreateController(RecordingHttpMessageHandler handler)
    {
        var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://ordering.test")
        };

        return new LoyaltyController(new StaticHttpClientFactory(client))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = CreateHttpContext("3")
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

        public bool IsAvailable { get; } = true;

        public void Clear() => _store.Clear();

        public Task CommitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public void Remove(string key) => _store.Remove(key);

        public void Set(string key, byte[] value) => _store[key] = value;

        public bool TryGetValue(string key, out byte[] value) => _store.TryGetValue(key, out value!);
    }
}
