using FreshFarm.Web.Bff.Controllers;
using FreshFarm.Web.Bff.Dtos;
using FreshFarm.Web.Bff.Models;
using FreshFarm.Web.Bff.Options;
using FreshFarm.Web.Bff.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace FreshFarm.Web.Bff.Tests;

public sealed class CheckoutControllerTests
{
    [Fact]
    public async Task PreviewShippingFee_PrefersMostCompleteDuplicateSellerOrigin_FromIdentity()
    {
        var catalogHandler = new RecordingHttpMessageHandler(request =>
        {
            if (request.RequestUri?.AbsolutePath == "/api/products/501")
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("""
                    {
                      "productId": 501,
                      "productName": "Rau Muong",
                      "weight": "1kg"
                    }
                    """)
                };
            }

            return new HttpResponseMessage(System.Net.HttpStatusCode.NotFound);
        });
        var identityHandler = new RecordingHttpMessageHandler(request =>
        {
            if (request.RequestUri?.AbsolutePath == "/auth/addresses")
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("[]")
                };
            }

            if (request.RequestUri?.AbsolutePath == "/auth/public/merchants/shipping-origins")
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("""
                    {
                      "origins": [
                        {
                          "sellerId": 77,
                          "shopName": "Shop 77",
                          "hasShippingOrigin": false,
                          "ghnDistrictId": null,
                          "ghnWardCode": null,
                          "pickupAddressSummary": ""
                        },
                        {
                          "sellerId": 77,
                          "shopName": "Shop 77",
                          "hasShippingOrigin": true,
                          "ghnDistrictId": 1442,
                          "ghnWardCode": "20308",
                          "pickupAddressSummary": "12 Nguyen Hue, Phuong Ben Nghe, Quan 1"
                        }
                      ]
                    }
                    """)
                };
            }

            return new HttpResponseMessage(System.Net.HttpStatusCode.NotFound);
        });
        var ghnService = new FakeGhnSandboxService();
        var controller = CreateController(catalogHandler, identityHandler, ghnService);

        var result = await controller.PreviewShippingFee(new CheckoutShippingFeePreviewRequestDto
        {
            AddressMode = "new",
            Items =
            [
                new CheckoutItemInputDto
                {
                    ProductId = 501,
                    SellerId = 77,
                    SellerName = "Shop 77",
                    ProductName = "Rau Muong",
                    Quantity = 2,
                    UnitPrice = 25000m,
                    UnitSymbol = "kg"
                }
            ],
            Shipping = new CheckoutShippingInputDto
            {
                FullName = "Nguyen Van A",
                Phone = "0912345678",
                AddressDetail = "45 Le Loi",
                ProvinceName = "Ho Chi Minh",
                DistrictName = "Quan 1",
                WardName = "Phuong Ben Nghe"
            }
        }, CancellationToken.None);

        var payload = Assert.IsType<CheckoutShippingFeePreviewResultDto>(result.Value);
        Assert.True(payload.Success);
        Assert.Single(ghnService.FeeRequests);
        Assert.Equal(1442, ghnService.FeeRequests[0].OriginOverride?.FromDistrictId);
        Assert.Equal("20308", ghnService.FeeRequests[0].OriginOverride?.FromWardCode);
    }

    private static CheckoutController CreateController(
        RecordingHttpMessageHandler catalogHandler,
        RecordingHttpMessageHandler identityHandler,
        FakeGhnSandboxService ghnService)
    {
        var catalogClient = new HttpClient(catalogHandler)
        {
            BaseAddress = new Uri("https://catalog.test")
        };
        var identityClient = new HttpClient(identityHandler)
        {
            BaseAddress = new Uri("https://identity.test")
        };

        var httpContext = new DefaultHttpContext();
        httpContext.Features.Set<ISessionFeature>(new SessionFeature
        {
            Session = new TestSession()
        });
        httpContext.Session.SetString("ACCESS_TOKEN", "token-123");

        return new CheckoutController(
            new NamedHttpClientFactory(new Dictionary<string, HttpClient>(StringComparer.OrdinalIgnoreCase)
            {
                ["Catalog"] = catalogClient,
                ["Identity"] = identityClient
            }),
            new FakeCartSessionService(),
            ghnService,
            new FakeVnPayService(),
            Microsoft.Extensions.Options.Options.Create(new VnPayOptions()),
            new ConfigurationBuilder().Build(),
            NullLogger<CheckoutController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = httpContext
            }
        };
    }

    private sealed class NamedHttpClientFactory(IReadOnlyDictionary<string, HttpClient> clients) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => clients[name];
    }

    private sealed class FakeCartSessionService : ICartSessionService
    {
        public Task AddOrIncreaseAsync(AddToCartRequestDto request) => Task.CompletedTask;
        public Task<CartSummaryDto> BuildSummaryAsync(decimal shippingFee) => Task.FromResult(new CartSummaryDto());
        public Task ClearAsync() => Task.CompletedTask;
        public Task<List<CartItemDto>> GetItemsAsync() => Task.FromResult(new List<CartItemDto>());
        public Task RemoveAsync(int productId) => Task.CompletedTask;
        public Task RemoveAsync(int productId, int sellerId, string? cartItemKey) => Task.CompletedTask;
        public Task SetItemsAsync(List<CartItemDto> items) => Task.CompletedTask;
        public Task UpdateQuantityAsync(int productId, int quantity) => Task.CompletedTask;
        public Task UpdateQuantityAsync(int productId, int sellerId, string? cartItemKey, int quantity) => Task.CompletedTask;
    }

    private sealed class FakeVnPayService : IVnPayService
    {
        public bool IsConfigured => false;
        public string CreatePaymentUrl(VnPayCreatePaymentRequest request) => string.Empty;
        public VnPayReturnValidationResult ValidateReturn(IQueryCollection query) => VnPayReturnValidationResult.Fail("Unsupported in tests.");
    }

    private sealed class FakeGhnSandboxService : IGhnSandboxService
    {
        public bool IsConfigured => true;
        public int? ShopId => 12345;
        public List<GhnSandboxFeeRequest> FeeRequests { get; } = new();

        public Task<GhnSandboxFeeResult> CalculateFeeAsync(GhnSandboxFeeRequest request, CancellationToken cancellationToken = default)
        {
            FeeRequests.Add(new GhnSandboxFeeRequest
            {
                ToDistrictId = request.ToDistrictId,
                ToWardCode = request.ToWardCode,
                Height = request.Height,
                Length = request.Length,
                Width = request.Width,
                Weight = request.Weight,
                InsuranceValue = request.InsuranceValue,
                ItemName = request.ItemName,
                ItemQuantity = request.ItemQuantity,
                OriginOverride = request.OriginOverride is null
                    ? null
                    : new GhnSandboxOriginOverride
                    {
                        FromDistrictId = request.OriginOverride.FromDistrictId,
                        FromWardCode = request.OriginOverride.FromWardCode,
                        PickupName = request.OriginOverride.PickupName,
                        ReturnAddress = request.OriginOverride.ReturnAddress,
                        ReturnPhone = request.OriginOverride.ReturnPhone
                    }
            });

            return Task.FromResult(new GhnSandboxFeeResult
            {
                Success = true,
                ServiceName = "GHN Express",
                TotalFee = 32000
            });
        }

        public Task<GhnSandboxLeadTimeResult> CalculateLeadTimeAsync(GhnSandboxLeadTimeRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<GhnSandboxCreateOrderResult> CreateOrderAsync(GhnSandboxCreateOrderRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<GhnSandboxLocationItem>> GetDistrictsAsync(int provinceId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<GhnSandboxLocationItem>>(
            [
                new GhnSandboxLocationItem { Id = 1442, Name = "Quan 1" }
            ]);
        public Task<GhnSandboxOrderTrackingResult> GetOrderTrackingAsync(GhnSandboxOrderTrackingRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<GhnSandboxLocationItem>> GetProvincesAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<GhnSandboxLocationItem>>(
            [
                new GhnSandboxLocationItem { Id = 202, Name = "Ho Chi Minh" }
            ]);
        public Task<GhnSandboxShopProfileResult> GetShopProfileAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<GhnSandboxConnectionResult> TestConnectionAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<GhnSandboxLocationItem>> GetWardsAsync(int districtId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<GhnSandboxLocationItem>>(
            [
                new GhnSandboxLocationItem { Id = 1, Code = "20308", Name = "Phuong Ben Nghe" }
            ]);
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
