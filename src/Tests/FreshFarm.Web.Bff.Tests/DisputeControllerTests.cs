using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using FreshFarm.Web.Bff.Areas.Admin.Controllers;
using FreshFarm.Web.Bff.Areas.Admin.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace FreshFarm.Web.Bff.Tests;

public sealed class DisputeControllerTests
{
    [Fact]
    public async Task Index_DedupesDuplicateQueueFiltersRowsAndDetailPayloads_FromOrdering()
    {
        var handler = new RecordingHttpMessageHandler(request =>
        {
            var path = request.RequestUri?.AbsolutePath;

            if (path == "/api/orders/admin/disputes/queue")
            {
                return CreateJsonResponse("""
                {
                  "page": 1,
                  "pageSize": 12,
                  "total": 1,
                  "totalPages": 1,
                  "stats": {
                    "totalCases": 1,
                    "openCases": 1,
                    "pendingCases": 0,
                    "resolvedCases": 0,
                    "breachedCases": 1,
                    "supportCases": 0,
                    "returnCases": 0,
                    "refundCases": 1
                  },
                  "filters": {
                    "q": "",
                    "section": "refunds",
                    "status": "open",
                    "sellerId": 24,
                    "sectionOptions": [
                      { "value": "refunds", "text": "" },
                      { "value": "refunds", "text": "Hoan tien" }
                    ],
                    "statusOptions": [
                      { "value": "open", "text": "" },
                      { "value": "open", "text": "Dang mo" }
                    ],
                    "sellerOptions": [
                      { "value": "24", "text": "" },
                      { "value": "24", "text": "Nong trai 24" }
                    ]
                  },
                  "rows": [
                    {
                      "caseType": "",
                      "caseId": 101,
                      "title": "",
                      "summary": "",
                      "displayStatus": "",
                      "queueStatus": "",
                      "buyerId": 0,
                      "buyerName": "",
                      "buyerPhone": "",
                      "createdAt": "2026-03-20T00:00:00Z"
                    },
                    {
                      "caseType": "refunds",
                      "caseId": 101,
                      "title": "Khieu nai hoan tien don 101",
                      "summary": "Nguoi mua phan anh giao sai san pham",
                      "displayStatus": "Dang mo",
                      "queueStatus": "open",
                      "sellerId": 24,
                      "sellerLabel": "Nong trai 24",
                      "buyerId": 18,
                      "buyerName": "Nguyen Van A",
                      "buyerPhone": "0901234567",
                      "orderId": 555,
                      "amount": 125000,
                      "unreadCount": 2,
                      "assignedOwner": "ops-refund",
                      "targetResolutionAt": "2026-03-25T00:00:00Z",
                      "evidenceCount": 3,
                      "isSlaBreached": true,
                      "createdAt": "2026-03-20T00:00:00Z",
                      "updatedAt": "2026-03-24T01:00:00Z"
                    }
                  ]
                }
                """);
            }

            if (path == "/api/orders/admin/disputes/details")
            {
                return CreateJsonResponse("""
                {
                  "caseType": "refunds",
                  "caseId": 101,
                  "title": "Khieu nai hoan tien don 101",
                  "summary": "Nguoi mua phan anh giao sai san pham",
                  "displayStatus": "Dang mo",
                  "queueStatus": "open",
                  "isSlaBreached": true,
                  "createdAt": "2026-03-20T00:00:00Z",
                  "updatedAt": "2026-03-24T01:00:00Z",
                  "buyer": {
                    "userId": 18,
                    "fullName": "Nguyen Van A",
                    "phone": "0901234567",
                    "email": "buyer@example.com"
                  },
                  "seller": {
                    "sellerId": 24,
                    "sellerLabel": "Nong trai 24"
                  },
                  "order": {
                    "orderId": 555,
                    "totalAmount": 125000,
                    "status": "RefundPending",
                    "orderDate": "2026-03-19T00:00:00Z"
                  },
                  "afterSales": {
                    "reasonCode": "wrong_item",
                    "resolution": "refund_pending",
                    "refundAmount": 125000,
                    "amount": 125000,
                    "channel": "portal",
                    "method": "wallet",
                    "paymentStatus": "pending",
                    "referenceCode": "RF-101"
                  },
                  "assignment": {
                    "ownerLabel": "ops-refund",
                    "assignedAt": "2026-03-24T01:00:00Z"
                  },
                  "sla": {
                    "targetResolutionAt": "2026-03-25T00:00:00Z",
                    "source": "default_policy",
                    "isBreached": true
                  },
                  "timeline": [
                    {
                      "label": "Tiep nhan",
                      "value": "2026-03-20T00:00:00Z",
                      "tone": ""
                    },
                    {
                      "label": "Tiep nhan",
                      "value": "2026-03-20T00:00:00Z",
                      "tone": "info"
                    }
                  ],
                  "activityItems": [
                    {
                      "label": "Ghi chu",
                      "summary": "",
                      "createdAt": "2026-03-24T01:00:00Z"
                    },
                    {
                      "label": "Ghi chu",
                      "summary": "Da lien he seller de xac minh",
                      "createdAt": "2026-03-24T01:00:00Z",
                      "actorUserId": 7
                    }
                  ],
                  "evidenceItems": [
                    {
                      "note": "",
                      "createdAt": "2026-03-24T01:00:00Z"
                    },
                    {
                      "note": "Anh chup san pham nhan duoc",
                      "createdAt": "2026-03-24T01:00:00Z",
                      "actorUserId": 18
                    }
                  ],
                  "messages": [
                    {
                      "sender": "buyer",
                      "content": "",
                      "createdAt": "2026-03-24T01:00:00Z",
                      "isRead": false
                    },
                    {
                      "sender": "buyer",
                      "content": "Toi nhan sai san pham",
                      "createdAt": "2026-03-24T01:00:00Z",
                      "isRead": true
                    }
                  ],
                  "relatedOrders": [
                    {
                      "orderId": 555,
                      "totalAmount": 0,
                      "status": "",
                      "orderDate": null
                    },
                    {
                      "orderId": 555,
                      "totalAmount": 125000,
                      "status": "RefundPending",
                      "orderDate": "2026-03-19T00:00:00Z"
                    }
                  ],
                  "relatedCases": [
                    {
                      "caseType": "",
                      "caseId": 202,
                      "title": "",
                      "status": "",
                      "amount": null,
                      "createdAt": null
                    },
                    {
                      "caseType": "support",
                      "caseId": 202,
                      "title": "Ho tro ticket lien quan",
                      "status": "pending",
                      "amount": 0,
                      "createdAt": "2026-03-22T00:00:00Z"
                    }
                  ]
                }
                """);
            }

            return new HttpResponseMessage(System.Net.HttpStatusCode.NotFound);
        });
        var controller = CreateController(handler);

        var result = await controller.Index(selectedCaseType: "refunds", selectedCaseId: 101);

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<DisputeCenterPageViewModel>(view.Model);
        var section = Assert.Single(model.SectionOptions);
        var status = Assert.Single(model.StatusOptions);
        var seller = Assert.Single(model.SellerOptions);
        var row = Assert.Single(model.Rows);
        Assert.NotNull(model.Details);
        var detail = model.Details!;
        var timeline = Assert.Single(detail.Timeline);
        var activity = Assert.Single(detail.ActivityItems);
        var evidence = Assert.Single(detail.EvidenceItems);
        var message = Assert.Single(detail.Messages);
        var relatedOrder = Assert.Single(detail.RelatedOrders);
        var relatedCase = Assert.Single(detail.RelatedCases);

        Assert.Equal("Hoan tien", section.Text);
        Assert.Equal("Dang mo", status.Text);
        Assert.Equal("Nong trai 24", seller.Text);

        Assert.Equal("refunds", row.CaseType);
        Assert.Equal(101, row.CaseId);
        Assert.Equal("Khieu nai hoan tien don 101", row.Title);
        Assert.Equal("Nguoi mua phan anh giao sai san pham", row.Summary);
        Assert.Equal("Nong trai 24", row.SellerLabel);
        Assert.Equal("Nguyen Van A", row.BuyerName);
        Assert.Equal("0901234567", row.BuyerPhone);
        Assert.Equal(555, row.OrderId);
        Assert.Equal(125000, row.Amount);
        Assert.Equal(2, row.UnreadCount);
        Assert.True(row.IsSlaBreached);

        Assert.Equal("Khieu nai hoan tien don 101", detail.Title);
        Assert.Equal("Nguyen Van A", detail.Buyer!.FullName);
        Assert.Equal("Nong trai 24", detail.Seller!.SellerLabel);
        Assert.Equal("RF-101", detail.AfterSales!.ReferenceCode);
        Assert.Equal("ops-refund", detail.Assignment!.OwnerLabel);
        Assert.Equal("default_policy", detail.Sla!.Source);

        Assert.Equal("Tiep nhan", timeline.Label);
        Assert.Equal("info", timeline.Tone);

        Assert.Equal("Ghi chu", activity.Label);
        Assert.Equal("Da lien he seller de xac minh", activity.Summary);
        Assert.Equal(7, activity.ActorUserId);

        Assert.Equal("Anh chup san pham nhan duoc", evidence.Note);
        Assert.Equal(18, evidence.ActorUserId);

        Assert.Equal("buyer", message.Sender);
        Assert.Equal("Toi nhan sai san pham", message.Content);
        Assert.True(message.IsRead);

        Assert.Equal(555, relatedOrder.OrderId);
        Assert.Equal("RefundPending", relatedOrder.Status);
        Assert.Equal(125000, relatedOrder.TotalAmount);

        Assert.Equal(202, relatedCase.CaseId);
        Assert.Equal("support", relatedCase.CaseType);
        Assert.Equal("Ho tro ticket lien quan", relatedCase.Title);
        Assert.Equal("pending", relatedCase.Status);
    }

    private static DisputeController CreateController(RecordingHttpMessageHandler handler)
    {
        var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://ordering.test")
        };

        return new DisputeController(new StaticHttpClientFactory(client))
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
