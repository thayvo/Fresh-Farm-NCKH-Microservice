using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using FreshFarm.Web.Bff.Areas.Admin.Controllers;
using FreshFarm.Web.Bff.Areas.Admin.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace FreshFarm.Web.Bff.Tests;

public sealed class AuditControllerTests
{
    [Fact]
    public async Task Index_DedupesDuplicateAuditAndAuthPayloads_FromOrderingAndIdentity()
    {
        var handler = new RecordingHttpMessageHandler(request =>
        {
            var path = request.RequestUri?.AbsolutePath;

            if (path == "/api/orders/admin/audit/center")
            {
                return CreateJsonResponse("""
                {
                  "stats": {
                    "totalActionLogs": 1,
                    "recent24hActions": 1,
                    "moderationCount": 1,
                    "settlementCount": 1,
                    "settlementAmount7d": 250000
                  },
                  "filters": {
                    "q": "",
                    "area": "campaign_center",
                    "type": "campaign",
                    "areaOptions": [
                      { "value": "campaign_center", "text": "" },
                      { "value": "campaign_center", "text": "Trung tam chien dich" }
                    ],
                    "typeOptions": [
                      { "value": "campaign", "text": "" },
                      { "value": "campaign", "text": "Chien dich" }
                    ]
                  },
                  "actions": [
                    {
                      "adminActionLogId": 101,
                      "area": "",
                      "actionName": "",
                      "targetType": "",
                      "createdAt": "2026-03-20T00:00:00Z"
                    },
                    {
                      "adminActionLogId": 101,
                      "area": "campaign_center",
                      "actionName": "approve_campaign",
                      "targetType": "campaign",
                      "targetId": 55,
                      "summary": "Duyet campaign thang 4",
                      "actorUserId": 7,
                      "metadataJson": "{\"campaignId\":55}",
                      "createdAt": "2026-03-24T01:00:00Z"
                    }
                  ],
                  "moderationAudits": [
                    {
                      "moderationAuditId": 201,
                      "createdAt": "2026-03-20T00:00:00Z"
                    },
                    {
                      "moderationAuditId": 201,
                      "adminActionLogId": 101,
                      "subjectType": "campaign",
                      "subjectId": 55,
                      "decision": "approved",
                      "notes": "Dat tieu chi campaign",
                      "actorUserId": 7,
                      "createdAt": "2026-03-24T01:00:00Z"
                    }
                  ],
                  "settlementAudits": [
                    {
                      "settlementAuditId": 301,
                      "createdAt": "2026-03-20T00:00:00Z"
                    },
                    {
                      "settlementAuditId": 301,
                      "adminActionLogId": 101,
                      "auditType": "payout_review",
                      "referenceType": "settlement",
                      "referenceId": 88,
                      "sellerId": 24,
                      "amount": 250000,
                      "currency": "VND",
                      "notes": "Kiem tra doi soat payout",
                      "actorUserId": 7,
                      "createdAt": "2026-03-24T01:00:00Z"
                    }
                  ]
                }
                """);
            }

            if (path == "/admin/auth-audit")
            {
                return CreateJsonResponse("""
                {
                  "stats": {
                    "total24h": 1,
                    "success24h": 1,
                    "failed24h": 0,
                    "locked24h": 0,
                    "suspicious24h": 1,
                    "uniqueIp24h": 1,
                    "foreignBackoffice24h": 0,
                    "admin24h": 1,
                    "seller24h": 0,
                    "customer24h": 0,
                    "unknown24h": 0
                  },
                  "filters": {
                    "q": "",
                    "role": "admin",
                    "outcome": "suspicious",
                    "roleOptions": [
                      { "value": "admin", "text": "" },
                      { "value": "admin", "text": "Admin" }
                    ],
                    "outcomeOptions": [
                      { "value": "suspicious", "text": "" },
                      { "value": "suspicious", "text": "Dang ngo" }
                    ]
                  },
                  "items": [
                    {
                      "authAuditLogId": 401,
                      "success": false,
                      "isSuspicious": false,
                      "occurredAt": "2026-03-20T00:00:00Z"
                    },
                    {
                      "authAuditLogId": 401,
                      "userId": 7,
                      "clientLane": "admin",
                      "roleName": "admin",
                      "identifier": "admin@example.com",
                      "userName": "admin01",
                      "email": "admin@example.com",
                      "eventType": "sign_in",
                      "success": true,
                      "failureReason": "vpn_detected",
                      "failedAttemptCount": 2,
                      "ipAddress": "10.0.0.1",
                      "forwardedFor": "203.0.113.10",
                      "countryCode": "VN",
                      "countryName": "Viet Nam",
                      "regionName": "Ho Chi Minh",
                      "cityName": "Thu Duc",
                      "userAgent": "Mozilla/5.0",
                      "deviceType": "desktop",
                      "browserFamily": "Chrome",
                      "operatingSystem": "Windows",
                      "isSuspicious": true,
                      "suspicionReasons": "vpn_detected",
                      "occurredAt": "2026-03-24T01:00:00Z"
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
        var model = Assert.IsType<AuditCenterPageViewModel>(view.Model);
        var area = Assert.Single(model.AreaOptions);
        var type = Assert.Single(model.TypeOptions);
        var action = Assert.Single(model.ActionLogs);
        var moderation = Assert.Single(model.ModerationAudits);
        var settlement = Assert.Single(model.SettlementAudits);
        var authRole = Assert.Single(model.AuthRoleOptions);
        var authOutcome = Assert.Single(model.AuthOutcomeOptions);
        var authAudit = Assert.Single(model.AuthAudits);

        Assert.Equal("Trung tâm chiến dịch", area.Text);
        Assert.Equal("Chiến dịch", type.Text);

        Assert.Equal(101, action.AdminActionLogId);
        Assert.Equal("campaign_center", action.Area);
        Assert.Equal("approve_campaign", action.ActionName);
        Assert.Equal("campaign", action.TargetType);
        Assert.Equal(55, action.TargetId);
        Assert.Equal("Duyet campaign thang 4", action.Summary);
        Assert.Equal(7, action.ActorUserId);
        Assert.Equal("{\"campaignId\":55}", action.MetadataJson);

        Assert.Equal(201, moderation.ModerationAuditId);
        Assert.Equal(101, moderation.AdminActionLogId);
        Assert.Equal("campaign", moderation.SubjectType);
        Assert.Equal(55, moderation.SubjectId);
        Assert.Equal("approved", moderation.Decision);
        Assert.Equal("Dat tieu chi campaign", moderation.Notes);
        Assert.Equal(7, moderation.ActorUserId);

        Assert.Equal(301, settlement.SettlementAuditId);
        Assert.Equal("payout_review", settlement.AuditType);
        Assert.Equal("settlement", settlement.ReferenceType);
        Assert.Equal(88, settlement.ReferenceId);
        Assert.Equal(24, settlement.SellerId);
        Assert.Equal(250000, settlement.Amount);
        Assert.Equal("VND", settlement.Currency);
        Assert.Equal("Kiem tra doi soat payout", settlement.Notes);

        Assert.Equal("Admin", authRole.Text);
        Assert.Equal("Đáng ngờ", authOutcome.Text);

        Assert.Equal(401, authAudit.AuthAuditLogId);
        Assert.Equal(7, authAudit.UserId);
        Assert.Equal("admin", authAudit.ClientLane);
        Assert.Equal("admin", authAudit.RoleName);
        Assert.Equal("admin@example.com", authAudit.Identifier);
        Assert.Equal("admin01", authAudit.UserName);
        Assert.Equal("admin@example.com", authAudit.Email);
        Assert.Equal("sign_in", authAudit.EventType);
        Assert.True(authAudit.Success);
        Assert.Equal("vpn_detected", authAudit.FailureReason);
        Assert.Equal(2, authAudit.FailedAttemptCount);
        Assert.Equal("10.0.0.1", authAudit.IpAddress);
        Assert.Equal("203.0.113.10", authAudit.ForwardedFor);
        Assert.Equal("VN", authAudit.CountryCode);
        Assert.Equal("Viet Nam", authAudit.CountryName);
        Assert.Equal("Ho Chi Minh", authAudit.RegionName);
        Assert.Equal("Thu Duc", authAudit.CityName);
        Assert.Equal("Mozilla/5.0", authAudit.UserAgent);
        Assert.Equal("desktop", authAudit.DeviceType);
        Assert.Equal("Chrome", authAudit.BrowserFamily);
        Assert.Equal("Windows", authAudit.OperatingSystem);
        Assert.True(authAudit.IsSuspicious);
        Assert.Equal("vpn_detected", authAudit.SuspicionReasons);
        Assert.Equal(250000, model.Stats.SettlementAmount7d);
        Assert.Equal(1, model.AuthStats.Suspicious24h);
    }

    private static AuditController CreateController(RecordingHttpMessageHandler handler)
    {
        var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://service.test")
        };

        return new AuditController(new StaticHttpClientFactory(client))
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
