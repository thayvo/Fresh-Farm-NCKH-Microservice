using System.IdentityModel.Tokens.Jwt;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using FreshFarm.Web.Bff.Areas.Admin.Controllers;
using FreshFarm.Web.Bff.Areas.Admin.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace FreshFarm.Web.Bff.Tests;

public sealed class FinanceControllerTests
{
    [Fact]
    public void SellerFinanceView_UsesProfessionalSettlementLabels()
    {
        var view = File.ReadAllText(GetSellerFinanceViewPath());

        Assert.Contains("Giá trị thanh toán thành công", view);
        Assert.Contains("Phí nền tảng tạm tính", view);
        Assert.Contains("Phí nền tảng đủ điều kiện đối soát", view);
        Assert.Contains("Số tiền người bán tạm tính", view);
        Assert.Contains("Số tiền đủ điều kiện đối soát", view);
        Assert.Contains("Chờ chi trả", view);
        Assert.Contains("Số tiền có thể rút", view);

        Assert.DoesNotContain("Thanh toán đã ghi nhận", view);
        Assert.DoesNotContain("Hoa hồng đã phát sinh", view);
        Assert.DoesNotContain("Hoa hồng đủ đối soát", view);
        Assert.DoesNotContain("Thu nhập đã phát sinh", view);
        Assert.DoesNotContain("Thu nhập đủ đối soát", view);
        Assert.DoesNotContain("Chi trả chờ xử lý", view);
    }

    [Fact]
    public async Task Index_DedupesDuplicateFiltersRowsAndOwnerSummary_FromOrdering()
    {
        var handler = new RecordingHttpMessageHandler(request =>
        {
            if (request.RequestUri?.AbsolutePath == "/api/orders/admin/finance/console")
            {
                return CreateJsonResponse("""
                {
                  "scope": "admin",
                  "section": "payouts",
                  "page": 1,
                  "pageSize": 12,
                  "total": 1,
                  "totalPages": 1,
                  "stats": {
                    "grossMerchandiseValue": 1000000,
                    "capturedPayments": 900000,
                    "platformCommission": 100000,
                    "sellerEarning": 900000,
                    "reconciliationGrossMerchandiseValue": 700000,
                    "reconciliationPlatformCommission": 70000,
                    "reconciliationSellerEarning": 630000,
                    "withdrawableAmount": 380000,
                    "pendingPayoutAmount": 250000,
                    "refundedAmount": 50000,
                    "openReturns": 1,
                    "openRefunds": 1,
                    "sellerCount": 1,
                    "overdueFollowUps": 1,
                    "dueSoonFollowUps": 1,
                    "noFollowUpCount": 0
                  },
                  "sectionCounts": {
                    "payouts": 1,
                    "refunds": 1,
                    "returns": 0
                  },
                  "filters": {
                    "q": "",
                    "status": "pending",
                    "followUpBucket": "overdue",
                    "sellerId": 24,
                    "sellerOptions": [
                      { "value": "24", "text": "" },
                      { "value": "24", "text": "Nong trai 24" }
                    ],
                    "statusOptions": [
                      { "value": "pending", "text": "" },
                      { "value": "pending", "text": "Cho xu ly" }
                    ]
                  },
                  "rows": [
                    {
                      "recordId": 101,
                      "createdAt": "2026-03-20T00:00:00Z"
                    },
                    {
                      "recordId": 101,
                      "orderId": 555,
                      "sellerId": 24,
                      "sellerLabel": "Nong trai 24",
                      "orderCount": 3,
                      "amountGross": 500000,
                      "feeAmount": 50000,
                      "amountNet": 450000,
                      "refundAmount": 25000,
                      "status": "pending",
                      "method": "bank_transfer",
                      "provider": "vcb",
                      "referenceCode": "PAYOUT-101",
                      "reasonCode": "settlement_due",
                      "resolution": "reviewing",
                      "itemName": "Thanh toan dot 1",
                      "createdAt": "2026-03-20T00:00:00Z",
                      "scheduledAt": "2026-03-24T00:00:00Z",
                      "processedAt": "2026-03-24T01:00:00Z",
                      "paidAt": "2026-03-24T02:00:00Z",
                      "reconciliationStatus": "matched",
                      "nextStep": "xac nhan payout",
                      "assignedOwner": "finance-ops",
                      "exportedAt": "2026-03-24T03:00:00Z",
                      "followUpAt": "2026-03-25T00:00:00Z",
                      "followUpNote": "Theo doi den khi seller xac nhan",
                      "reminderSentAt": "2026-03-24T04:00:00Z",
                      "reminderNote": "Da nhac qua email",
                      "priorityKey": "p1",
                      "priorityLabel": "Cao",
                      "priorityReason": "Sap qua han doi soat",
                      "lastActionSummary": "Da gui bang ke payout"
                    }
                  ],
                  "ownerSummary": [
                    {
                      "ownerLabel": "finance-ops",
                      "itemCount": 0,
                      "overdueCount": 0,
                      "dueSoonCount": 0,
                      "noFollowUpCount": 0
                    },
                    {
                      "ownerLabel": "finance-ops",
                      "itemCount": 4,
                      "overdueCount": 1,
                      "dueSoonCount": 2,
                      "noFollowUpCount": 1
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
        var model = Assert.IsType<FinanceConsolePageViewModel>(view.Model);
        var seller = Assert.Single(model.SellerOptions);
        var status = Assert.Single(model.StatusOptions);
        var row = Assert.Single(model.Rows);
        var owner = Assert.Single(model.OwnerSummary);

        Assert.Equal("Nong trai 24", seller.Text);
        Assert.Equal("Cho xu ly", status.Text);

        Assert.Equal(101, row.RecordId);
        Assert.Equal(555, row.OrderId);
        Assert.Equal(24, row.SellerId);
        Assert.Equal("Nong trai 24", row.SellerLabel);
        Assert.Equal(3, row.OrderCount);
        Assert.Equal(500000, row.AmountGross);
        Assert.Equal(50000, row.FeeAmount);
        Assert.Equal(450000, row.AmountNet);
        Assert.Equal(25000, row.RefundAmount);
        Assert.Equal("pending", row.Status);
        Assert.Equal("bank_transfer", row.Method);
        Assert.Equal("vcb", row.Provider);
        Assert.Equal("PAYOUT-101", row.ReferenceCode);
        Assert.Equal("settlement_due", row.ReasonCode);
        Assert.Equal("reviewing", row.Resolution);
        Assert.Equal("Thanh toan dot 1", row.ItemName);
        Assert.Equal("matched", row.ReconciliationStatus);
        Assert.Equal("xac nhan payout", row.NextStep);
        Assert.Equal("finance-ops", row.AssignedOwner);
        Assert.Equal("Theo doi den khi seller xac nhan", row.FollowUpNote);
        Assert.Equal("Da nhac qua email", row.ReminderNote);
        Assert.Equal("p1", row.PriorityKey);
        Assert.Equal("Cao", row.PriorityLabel);
        Assert.Equal("Sap qua han doi soat", row.PriorityReason);
        Assert.Equal("Da gui bang ke payout", row.LastActionSummary);

        Assert.Equal("finance-ops", owner.OwnerLabel);
        Assert.Equal(4, owner.ItemCount);
        Assert.Equal(1, owner.OverdueCount);
        Assert.Equal(2, owner.DueSoonCount);
        Assert.Equal(1, owner.NoFollowUpCount);
        Assert.Equal(700000, model.Stats.ReconciliationGrossMerchandiseValue);
        Assert.Equal(70000, model.Stats.ReconciliationPlatformCommission);
        Assert.Equal(630000, model.Stats.ReconciliationSellerEarning);
        Assert.Equal(380000, model.Stats.WithdrawableAmount);
        Assert.Equal(250000, model.Stats.PendingPayoutAmount);
    }

    private static FinanceController CreateController(RecordingHttpMessageHandler handler)
    {
        var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://ordering.test")
        };

        return new FinanceController(new StaticHttpClientFactory(client))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = CreateHttpContext("7")
            }
        };
    }

    private static string GetSellerFinanceViewPath([CallerFilePath] string testFilePath = "")
    {
        var testDirectory = Path.GetDirectoryName(testFilePath)
            ?? throw new InvalidOperationException("Cannot resolve test source directory.");
        var srcDirectory = Directory.GetParent(testDirectory)?.Parent?.FullName
            ?? throw new InvalidOperationException("Cannot resolve source directory.");
        return Path.Combine(
            srcDirectory,
            "Web",
            "FreshFarm.Web.Bff",
            "Areas",
            "Seller",
            "Views",
            "Finance",
            "Index.cshtml");
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
