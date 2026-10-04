using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using FreshFarm.Web.Bff.Areas.Admin.Controllers;
using FreshFarm.Web.Bff.Areas.Admin.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace FreshFarm.Web.Bff.Tests;

public sealed class RiskControllerTests
{
    [Fact]
    public async Task Index_DedupesDuplicateOverviewFiltersRowsAndDetailPayloads_FromOrdering()
    {
        var handler = new RecordingHttpMessageHandler(request =>
        {
            var path = request.RequestUri?.AbsolutePath;

            if (path == "/api/orders/admin/risk/overview")
            {
                return CreateJsonResponse("""
                {
                  "stats": {
                    "totalCases": 1,
                    "openCases": 1,
                    "reviewCases": 0,
                    "escalatedCases": 1,
                    "highSeverityCases": 1,
                    "voucherAbuseCases": 1,
                    "returnSpikeCases": 0,
                    "decisionsLast7d": 2
                  },
                  "recent": [
                    {
                      "riskCaseId": 101,
                      "caseType": "",
                      "title": "",
                      "status": "",
                      "severity": "",
                      "signalCount": 0
                    },
                    {
                      "riskCaseId": 101,
                      "caseType": "voucher_abuse",
                      "title": "Voucher bi nghi bi lam dung",
                      "status": "open",
                      "severity": "high",
                      "signalCount": 3,
                      "updatedAt": "2026-03-24T01:00:00Z",
                      "lastSignalAt": "2026-03-24T01:00:00Z"
                    }
                  ]
                }
                """);
            }

            if (path == "/api/orders/admin/risk/cases")
            {
                return CreateJsonResponse("""
                {
                  "filters": {
                    "q": "",
                    "type": "voucher_abuse",
                    "status": "open",
                    "severity": "high",
                    "typeOptions": [
                      { "value": "voucher_abuse", "text": "" },
                      { "value": "voucher_abuse", "text": "Lạm dụng voucher" }
                    ],
                    "statusOptions": [
                      { "value": "open", "text": "" },
                      { "value": "open", "text": "Mở" }
                    ],
                    "severityOptions": [
                      { "value": "high", "text": "" },
                      { "value": "high", "text": "Cao" }
                    ]
                  },
                  "rows": [
                    {
                      "riskCaseId": 101,
                      "caseType": "",
                      "title": "",
                      "status": "",
                      "severity": "",
                      "signalCount": 0,
                      "createdAt": "2026-03-20T00:00:00Z"
                    },
                    {
                      "riskCaseId": 101,
                      "caseType": "voucher_abuse",
                      "title": "Voucher bi nghi bi lam dung",
                      "summary": "Can review coupon va nguoi mua",
                      "status": "open",
                      "severity": "high",
                      "sellerId": 24,
                      "buyerId": 18,
                      "orderId": 555,
                      "campaignId": 12,
                      "voucherCouponId": 88,
                      "signalCount": 3,
                      "isEscalated": true,
                      "createdAt": "2026-03-20T00:00:00Z",
                      "updatedAt": "2026-03-24T01:00:00Z",
                      "lastSignalAt": "2026-03-24T01:00:00Z"
                    }
                  ]
                }
                """);
            }

            if (path == "/api/orders/admin/risk/cases/101")
            {
                return CreateJsonResponse("""
                {
                  "riskCaseId": 101,
                  "referenceKey": "RISK-101",
                  "caseType": "voucher_abuse",
                  "title": "Voucher bi nghi bi lam dung",
                  "summary": "Can review coupon va nguoi mua",
                  "status": "open",
                  "severity": "high",
                  "sellerId": 24,
                  "buyerId": 18,
                  "orderId": 555,
                  "campaignId": 12,
                  "voucherCouponId": 88,
                  "voucherCode": "SALE88",
                  "signalCount": 3,
                  "isEscalated": true,
                  "createdAt": "2026-03-20T00:00:00Z",
                  "updatedAt": "2026-03-24T01:00:00Z",
                  "lastSignalAt": "2026-03-24T01:00:00Z",
                  "signals": [
                    {
                      "riskSignalId": 401,
                      "signalType": "",
                      "signalCode": "",
                      "severity": "",
                      "source": "",
                      "score": 0,
                      "triggeredAt": "2026-03-20T00:00:00Z"
                    },
                    {
                      "riskSignalId": 401,
                      "signalType": "coupon",
                      "signalCode": "ABUSE-401",
                      "severity": "high",
                      "source": "heuristic",
                      "score": 9.5,
                      "metadataJson": "{\"coupon\":\"SALE88\"}",
                      "triggeredAt": "2026-03-24T01:00:00Z"
                    }
                  ],
                  "decisions": [
                    {
                      "riskDecisionId": 601,
                      "decisionType": "",
                      "createdAt": "2026-03-20T00:00:00Z"
                    },
                    {
                      "riskDecisionId": 601,
                      "decisionType": "monitor",
                      "notes": "Theo doi them 24h",
                      "createdBy": 7,
                      "createdAt": "2026-03-24T01:00:00Z"
                    }
                  ],
                  "voucherAbuseCases": [
                    {
                      "voucherAbuseCaseId": 701,
                      "couponId": 0,
                      "createdAt": "2026-03-20T00:00:00Z"
                    },
                    {
                      "voucherAbuseCaseId": 701,
                      "couponId": 88,
                      "buyerId": 18,
                      "sellerId": 24,
                      "campaignId": 12,
                      "orderId": 555,
                      "abuseType": "multi_account",
                      "suspectedBenefitAmount": 125000,
                      "status": "open",
                      "createdAt": "2026-03-20T00:00:00Z",
                      "reviewedAt": "2026-03-24T01:00:00Z"
                    }
                  ],
                  "decisionOptions": [
                    { "value": "monitor", "text": "" },
                    { "value": "monitor", "text": "Theo dõi" }
                  ]
                }
                """);
            }

            return new HttpResponseMessage(System.Net.HttpStatusCode.NotFound);
        });
        var controller = CreateController(handler);

        var result = await controller.Index();

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<RiskCenterPageViewModel>(view.Model);
        var recent = Assert.Single(model.Overview.Recent);
        var typeOption = Assert.Single(model.TypeOptions);
        var statusOption = Assert.Single(model.StatusOptions);
        var severityOption = Assert.Single(model.SeverityOptions);
        var row = Assert.Single(model.Rows);
        Assert.NotNull(model.SelectedCase);
        var detail = model.SelectedCase!;
        var signal = Assert.Single(detail!.Signals);
        var decision = Assert.Single(detail.Decisions);
        var abuse = Assert.Single(detail.VoucherAbuseCases);
        var decisionOption = Assert.Single(detail.DecisionOptions);

        Assert.Equal("Voucher bi nghi bi lam dung", recent.Title);
        Assert.Equal("Lạm dụng voucher", typeOption.Text);
        Assert.Equal("Mở", statusOption.Text);
        Assert.Equal("Cao", severityOption.Text);

        Assert.Equal(101, row.RiskCaseId);
        Assert.Equal("voucher_abuse", row.CaseType);
        Assert.Equal("Voucher bi nghi bi lam dung", row.Title);
        Assert.Equal("Can review coupon va nguoi mua", row.Summary);
        Assert.True(row.IsEscalated);

        Assert.Equal("RISK-101", detail.ReferenceKey);
        Assert.Equal("SALE88", detail.VoucherCode);
        Assert.Equal("ABUSE-401", signal.SignalCode);
        Assert.Equal("heuristic", signal.Source);
        Assert.Equal("monitor", decision.DecisionType);
        Assert.Equal("Theo doi them 24h", decision.Notes);
        Assert.Equal("multi_account", abuse.AbuseType);
        Assert.Equal("Theo dõi", decisionOption.Text);
        Assert.Equal("monitor", model.DecisionEditor.DecisionType);
    }

    private static RiskController CreateController(RecordingHttpMessageHandler handler)
    {
        var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://ordering.test")
        };

        return new RiskController(new StaticHttpClientFactory(client))
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
