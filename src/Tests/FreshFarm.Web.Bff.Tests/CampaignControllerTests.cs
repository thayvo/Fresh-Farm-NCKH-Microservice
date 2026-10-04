using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using FreshFarm.Web.Bff.Areas.Admin.Controllers;
using FreshFarm.Web.Bff.Areas.Admin.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace FreshFarm.Web.Bff.Tests;

public sealed class CampaignControllerTests
{
    [Fact]
    public async Task Index_DedupesDuplicateOverviewListAdsAndDetailPayloads_FromOrdering()
    {
        var handler = new RecordingHttpMessageHandler(request =>
        {
            var path = request.RequestUri?.AbsolutePath;

            if (path == "/api/orders/admin/campaigns/overview")
            {
                return CreateJsonResponse("""
                {
                  "totalCampaigns": 1,
                  "runningCampaigns": 1,
                  "registrationOpenCampaigns": 0,
                  "scheduledCampaigns": 0,
                  "totalParticipations": 3,
                  "approvedParticipations": 2,
                  "pendingParticipations": 1,
                  "totalSlots": 4,
                  "approvedSlots": 3,
                  "liveSlots": 1,
                  "totalBudget": 2000000,
                  "upcoming": [
                    {
                      "campaignId": 101,
                      "name": "",
                      "campaignType": "",
                      "status": "",
                      "startAt": "2026-03-30T00:00:00Z",
                      "endAt": "2026-04-02T00:00:00Z",
                      "registrationEndAt": "2026-03-28T00:00:00Z"
                    },
                    {
                      "campaignId": 101,
                      "name": "Mega sale thang 4",
                      "campaignType": "flash_sale",
                      "status": "running",
                      "startAt": "2026-03-30T00:00:00Z",
                      "endAt": "2026-04-02T00:00:00Z",
                      "registrationEndAt": "2026-03-29T00:00:00Z"
                    }
                  ]
                }
                """);
            }

            if (path == "/api/orders/admin/campaigns")
            {
                return CreateJsonResponse("""
                {
                  "filters": {
                    "q": "",
                    "status": "running",
                    "type": "flash_sale"
                  },
                  "statusOptions": [
                    { "value": "running", "text": "" },
                    { "value": "running", "text": "Dang chay" }
                  ],
                  "typeOptions": [
                    { "value": "flash_sale", "text": "" },
                    { "value": "flash_sale", "text": "Flash sale" }
                  ],
                  "rows": [
                    {
                      "campaignId": 101,
                      "name": "",
                      "campaignType": "",
                      "description": null,
                      "status": "",
                      "budgetAmount": null,
                      "isFeatured": false,
                      "registrationStartAt": "2026-03-24T00:00:00Z",
                      "registrationEndAt": "2026-03-28T00:00:00Z",
                      "startAt": "2026-03-30T00:00:00Z",
                      "endAt": "2026-04-02T00:00:00Z",
                      "createdAt": "2026-03-20T00:00:00Z",
                      "participationCount": 0,
                      "approvedParticipationCount": 0,
                      "productSlotCount": 0,
                      "approvedSlotCount": 0
                    },
                    {
                      "campaignId": 101,
                      "name": "Mega sale thang 4",
                      "campaignType": "flash_sale",
                      "description": "Day hang nong san theo campaign flash sale",
                      "status": "running",
                      "budgetAmount": 2000000,
                      "isFeatured": true,
                      "voucherCouponId": 88,
                      "voucherCode": "APRIL88",
                      "registrationStartAt": "2026-03-24T00:00:00Z",
                      "registrationEndAt": "2026-03-28T00:00:00Z",
                      "startAt": "2026-03-30T00:00:00Z",
                      "endAt": "2026-04-02T00:00:00Z",
                      "createdAt": "2026-03-20T00:00:00Z",
                      "updatedAt": "2026-03-24T01:00:00Z",
                      "participationCount": 12,
                      "approvedParticipationCount": 10,
                      "productSlotCount": 20,
                      "approvedSlotCount": 18
                    }
                  ]
                }
                """);
            }

            if (path == "/api/orders/admin/campaigns/ads/overview")
            {
                return CreateJsonResponse("""
                {
                  "stats": {
                    "walletCount": 1,
                    "activeWalletCount": 1,
                    "runningAdsCampaigns": 1,
                    "totalBalance": 500000,
                    "totalReserved": 150000,
                    "totalAvailable": 350000,
                    "totalTopup": 700000,
                    "totalSpend": 200000
                  },
                  "filters": {
                    "statusOptions": [
                      { "value": "active", "text": "" },
                      { "value": "active", "text": "Hoat dong" }
                    ]
                  },
                  "wallets": [
                    {
                      "walletId": 301,
                      "sellerId": 0,
                      "balance": 0,
                      "reservedBalance": 0,
                      "availableBalance": 0,
                      "totalTopup": 0,
                      "totalSpend": 0,
                      "status": "",
                      "createdAt": "2026-03-20T00:00:00Z"
                    },
                    {
                      "walletId": 301,
                      "sellerId": 24,
                      "balance": 500000,
                      "reservedBalance": 150000,
                      "availableBalance": 350000,
                      "totalTopup": 700000,
                      "totalSpend": 200000,
                      "status": "active",
                      "createdAt": "2026-03-20T00:00:00Z",
                      "updatedAt": "2026-03-24T01:00:00Z"
                    }
                  ],
                  "adsCampaigns": [
                    {
                      "adsCampaignId": 401,
                      "walletId": 0,
                      "sellerId": 0,
                      "campaignId": null,
                      "name": "",
                      "channel": "",
                      "status": "",
                      "dailyBudget": 0,
                      "totalBudget": 0,
                      "spendToDate": 0,
                      "startAt": "2026-03-30T00:00:00Z",
                      "endAt": "2026-04-02T00:00:00Z",
                      "createdAt": "2026-03-20T00:00:00Z"
                    },
                    {
                      "adsCampaignId": 401,
                      "walletId": 301,
                      "sellerId": 24,
                      "campaignId": 101,
                      "name": "Onsite push mega sale",
                      "channel": "onsite",
                      "status": "active",
                      "dailyBudget": 100000,
                      "totalBudget": 500000,
                      "spendToDate": 150000,
                      "startAt": "2026-03-30T00:00:00Z",
                      "endAt": "2026-04-02T00:00:00Z",
                      "createdAt": "2026-03-24T01:00:00Z"
                    }
                  ],
                  "recentTopups": [
                    {
                      "topupId": 501,
                      "walletId": 0,
                      "sellerId": 0,
                      "amount": 0,
                      "status": "",
                      "createdAt": "2026-03-20T00:00:00Z"
                    },
                    {
                      "topupId": 501,
                      "walletId": 301,
                      "sellerId": 24,
                      "amount": 200000,
                      "status": "completed",
                      "paymentMethod": "bank_transfer",
                      "referenceCode": "TOPUP-501",
                      "createdAt": "2026-03-24T01:00:00Z"
                    }
                  ]
                }
                """);
            }

            if (path == "/api/orders/admin/campaigns/101")
            {
                return CreateJsonResponse("""
                {
                  "campaignId": 101,
                  "name": "Mega sale thang 4",
                  "campaignType": "flash_sale",
                  "description": "Day hang nong san theo campaign flash sale",
                  "status": "running",
                  "budgetAmount": 2000000,
                  "isFeatured": true,
                  "voucherCouponId": 88,
                  "voucherCode": "APRIL88",
                  "registrationStartAt": "2026-03-24T00:00:00Z",
                  "registrationEndAt": "2026-03-28T00:00:00Z",
                  "startAt": "2026-03-30T00:00:00Z",
                  "endAt": "2026-04-02T00:00:00Z",
                  "createdAt": "2026-03-20T00:00:00Z",
                  "updatedAt": "2026-03-24T01:00:00Z",
                  "approvedAt": "2026-03-24T01:00:00Z",
                  "participations": [
                    {
                      "participationId": 601,
                      "sellerId": 0,
                      "status": "",
                      "requestedAt": "2026-03-21T00:00:00Z"
                    },
                    {
                      "participationId": 601,
                      "sellerId": 24,
                      "status": "approved",
                      "notes": "Duyet seller chu luc 1",
                      "discountPercent": 15,
                      "requestedSlots": 10,
                      "approvedSlots": 8,
                      "requestedAt": "2026-03-21T00:00:00Z",
                      "reviewedAt": "2026-03-24T01:00:00Z",
                      "reviewedBy": 7
                    }
                  ],
                  "slots": [
                    {
                      "slotId": 701,
                      "participationId": null,
                      "sellerId": 0,
                      "productId": 0,
                      "status": "",
                      "createdAt": "2026-03-21T00:00:00Z"
                    },
                    {
                      "slotId": 701,
                      "participationId": 601,
                      "sellerId": 24,
                      "productId": 901,
                      "status": "approved",
                      "flashSalePrice": 89000,
                      "inventoryLimit": 40,
                      "createdAt": "2026-03-21T00:00:00Z",
                      "approvedAt": "2026-03-24T01:00:00Z"
                    }
                  ]
                }
                """);
            }

            return new HttpResponseMessage(System.Net.HttpStatusCode.NotFound);
        });
        var controller = CreateController(handler);

        var result = await controller.Index(campaignId: 101);

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<CampaignCenterPageViewModel>(view.Model);
        var upcoming = Assert.Single(model.Overview.Upcoming);
        var statusOption = Assert.Single(model.StatusOptions);
        var typeOption = Assert.Single(model.TypeOptions);
        var campaign = Assert.Single(model.Campaigns);
        var walletStatusOption = Assert.Single(model.WalletStatusOptions);
        var wallet = Assert.Single(model.AdsWallets);
        var adsCampaign = Assert.Single(model.AdsCampaigns);
        var topup = Assert.Single(model.RecentTopups);
        Assert.NotNull(model.SelectedCampaign);
        var detail = model.SelectedCampaign!;
        var participation = Assert.Single(detail.Participations);
        var slot = Assert.Single(detail.Slots);

        Assert.Equal("Mega sale thang 4", upcoming.Name);
        Assert.Equal("running", upcoming.Status);
        Assert.Equal("Đang chạy", statusOption.Text);
        Assert.Equal("Flash sale", typeOption.Text);

        Assert.Equal(101, campaign.CampaignId);
        Assert.Equal("Mega sale thang 4", campaign.Name);
        Assert.Equal("Day hang nong san theo campaign flash sale", campaign.Description);
        Assert.Equal("running", campaign.Status);
        Assert.Equal("APRIL88", campaign.VoucherCode);
        Assert.Equal(12, campaign.ParticipationCount);
        Assert.Equal(18, campaign.ApprovedSlotCount);

        Assert.Equal("Hoạt động", walletStatusOption.Text);
        Assert.Equal(301, wallet.WalletId);
        Assert.Equal(24, wallet.SellerId);
        Assert.Equal(350000, wallet.AvailableBalance);
        Assert.Equal("active", wallet.Status);

        Assert.Equal(401, adsCampaign.AdsCampaignId);
        Assert.Equal(101, adsCampaign.CampaignId);
        Assert.Equal("Onsite push mega sale", adsCampaign.Name);
        Assert.Equal("onsite", adsCampaign.Channel);
        Assert.Equal("active", adsCampaign.Status);

        Assert.Equal(501, topup.TopupId);
        Assert.Equal("completed", topup.Status);
        Assert.Equal("bank_transfer", topup.PaymentMethod);
        Assert.Equal("TOPUP-501", topup.ReferenceCode);

        Assert.Equal(101, detail.CampaignId);
        Assert.Equal("Mega sale thang 4", detail.Name);
        Assert.Equal("APRIL88", detail.VoucherCode);
        Assert.Equal(601, participation.ParticipationId);
        Assert.Equal(24, participation.SellerId);
        Assert.Equal("approved", participation.Status);
        Assert.Equal("Duyet seller chu luc 1", participation.Notes);
        Assert.Equal(8, participation.ApprovedSlots);

        Assert.Equal(701, slot.SlotId);
        Assert.Equal(601, slot.ParticipationId);
        Assert.Equal(24, slot.SellerId);
        Assert.Equal(901, slot.ProductId);
        Assert.Equal("approved", slot.Status);
        Assert.Equal(89000, slot.FlashSalePrice);

        Assert.Equal(101, model.Editor.CampaignId);
        Assert.Equal("flash_sale", model.Editor.CampaignType);
        Assert.Equal("running", model.Editor.Status);
    }

    private static CampaignController CreateController(RecordingHttpMessageHandler handler)
    {
        var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://ordering.test")
        };

        return new CampaignController(new StaticHttpClientFactory(client))
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
