using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using FreshFarm.Web.Bff.Areas.Seller.Controllers;
using FreshFarm.Web.Bff.Options;
using FreshFarm.Web.Bff.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace FreshFarm.Web.Bff.Tests;

public sealed class SellerShippingControllerTests
{
    [Fact]
    public void Create_Rejects_LegacyManualShippingCreation()
    {
        var orderingHandler = new RecordingHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var identityHandler = new RecordingHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var controller = CreateController(orderingHandler, identityHandler, new FakeGhnSandboxService());

        var result = controller.Create(new FreshFarm.Web.Bff.Areas.Seller.Models.Shipping
        {
            OrderID = 321
        });

        var payload = ReadJsonResult(result);
        Assert.False(payload.GetProperty("success").GetBoolean());
        Assert.Contains("không thể thêm vận chuyển thủ công", payload.GetProperty("message").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Empty(orderingHandler.Requests);
    }

    [Fact]
    public async Task CreateGhnSandboxOrder_BindsRecipientAndPricing_FromOrderScope()
    {
        var orderingHandler = new RecordingHttpMessageHandler(request =>
        {
            if (request.RequestUri?.AbsolutePath == "/api/orders/admin/shippings/order-info/321")
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new
                    {
                        success = true,
                        fullName = "Nguyen Van Buyer",
                        phone = "0901234567",
                        email = "buyer@example.com",
                        address = "45 Nguyen Van Cu, Phuong 1",
                        shippingFee = 30000m,
                        itemsAmount = 145000m,
                        totalAmount = 175000m,
                        totalQuantity = 3,
                        itemSummary = "Rau cu tong hop",
                        paymentMethod = "COD",
                        isCod = true,
                        codAmount = 175000m
                    })
                };
            }

            if (request.RequestUri?.AbsolutePath == "/api/orders/admin/shippings/321/ghn-metadata")
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new { success = true })
                };
            }

            if (request.RequestUri?.AbsolutePath == "/api/orders/admin/shippings/internal/321/ghn-metadata")
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new { success = true })
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var identityHandler = new RecordingHttpMessageHandler(request =>
        {
            if (request.RequestUri?.AbsolutePath == "/auth/admin/settings/store")
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new
                    {
                        storeName = "FreshFarm Seller",
                        storeAddress = "Kho A",
                        storeEmail = "seller@example.com",
                        storePhone = "0911222333",
                        adminNotificationEmail = "seller@example.com",
                        ghnPickupName = "FreshFarm Seller",
                        ghnPickupPhone = "0911222333",
                        ghnPickupAddress = "12 Nguyen Hue",
                        ghnDistrictId = 1442,
                        ghnWardCode = "20308",
                        hasGhnOrigin = true
                    })
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var ghnService = new FakeGhnSandboxService
        {
            CreateOrderResult = new GhnSandboxCreateOrderResult
            {
                Success = true,
                OrderCode = "GHN-321",
                ClientOrderCode = "FF-ORD-000321",
                Message = "OK"
            }
        };

        var controller = CreateController(orderingHandler, identityHandler, ghnService);

        var result = await controller.CreateGhnSandboxOrder(new ShippingController.CreateGhnSandboxOrderRequest
        {
            OrderId = 321,
            ToName = "Manual Receiver",
            ToPhone = "0000000000",
            ToAddress = "Manual Address",
            ToDistrictId = 1482,
            ToWardCode = "90777",
            ClientOrderCode = "MANUAL-CODE",
            Content = "Manual content",
            CodAmount = 123,
            InsuranceValue = 456,
            Height = 11,
            Length = 22,
            Width = 33,
            Weight = 444,
            Items = new List<ShippingController.CreateGhnSandboxOrderItemRequest>
            {
                new()
                {
                    Name = "Manual item",
                    Code = "MANUAL-ITEM",
                    Quantity = 99,
                    Price = 1
                }
            }
        }, CancellationToken.None);

        var payload = ReadJsonResult(result);
        Assert.True(payload.GetProperty("success").GetBoolean());
        Assert.Equal("GHN-321", payload.GetProperty("orderCode").GetString());

        var createRequest = Assert.Single(ghnService.CreateOrderRequests);
        Assert.Equal("Nguyen Van Buyer", createRequest.ToName);
        Assert.Equal("0901234567", createRequest.ToPhone);
        Assert.Equal("45 Nguyen Van Cu, Phuong 1", createRequest.ToAddress);
        Assert.Equal(1482, createRequest.ToDistrictId);
        Assert.Equal("90777", createRequest.ToWardCode);
        Assert.Equal("FF-ORD-000321", createRequest.ClientOrderCode);
        Assert.Equal("Đơn giao vận cho #DH00321", createRequest.Content);
        Assert.Equal(175000, createRequest.CodAmount);
        Assert.Equal(175000, createRequest.InsuranceValue);
        Assert.Equal(11, createRequest.Height);
        Assert.Equal(22, createRequest.Length);
        Assert.Equal(33, createRequest.Width);
        Assert.Equal(444, createRequest.Weight);
        Assert.Single(createRequest.Items);
        Assert.Equal("Rau cu tong hop", createRequest.Items[0].Name);
        Assert.Equal("ORDER-321", createRequest.Items[0].Code);
        Assert.Equal(3, createRequest.Items[0].Quantity);
        Assert.Equal(145000, createRequest.Items[0].Price);

        Assert.Contains(orderingHandler.Requests, request =>
            request.Method == HttpMethod.Post &&
            request.RequestUri?.AbsolutePath == "/api/orders/admin/shippings/internal/321/ghn-metadata" &&
            request.Headers.Contains("X-Internal-Service-Key"));
    }

    [Fact]
    public async Task CreateGhnSandboxOrder_Rejects_WhenOrderAlreadyHasBoundGhnCode()
    {
        var orderingHandler = new RecordingHttpMessageHandler(request =>
        {
            if (request.RequestUri?.AbsolutePath == "/api/orders/admin/shippings/order-info/321")
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new
                    {
                        success = true,
                        fullName = "Nguyen Van Buyer",
                        phone = "0901234567",
                        address = "45 Nguyen Van Cu, Phuong 1",
                        itemSummary = "Rau cu tong hop",
                        totalQuantity = 3,
                        itemsAmount = 145000m,
                        totalAmount = 175000m,
                        ghnOrderCode = "GHN-EXISTING"
                    })
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var identityHandler = new RecordingHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    storeName = "FreshFarm Seller",
                    storeAddress = "Kho A",
                    storeEmail = "seller@example.com",
                    storePhone = "0911222333",
                    adminNotificationEmail = "seller@example.com",
                    ghnPickupName = "FreshFarm Seller",
                    ghnPickupPhone = "0911222333",
                    ghnPickupAddress = "12 Nguyen Hue",
                    ghnDistrictId = 1442,
                    ghnWardCode = "20308",
                    hasGhnOrigin = true
                })
            });

        var ghnService = new FakeGhnSandboxService();
        var controller = CreateController(orderingHandler, identityHandler, ghnService);

        var result = await controller.CreateGhnSandboxOrder(new ShippingController.CreateGhnSandboxOrderRequest
        {
            OrderId = 321,
            ToDistrictId = 1482,
            ToWardCode = "90777"
        }, CancellationToken.None);

        var payload = ReadJsonResult(result);
        Assert.False(payload.GetProperty("success").GetBoolean());
        Assert.Contains("đã có vận đơn GHN", payload.GetProperty("message").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Empty(ghnService.CreateOrderRequests);
        Assert.DoesNotContain(orderingHandler.Requests, request =>
            request.Method == HttpMethod.Post &&
            request.RequestUri?.AbsolutePath == "/api/orders/admin/shippings/321/ghn-metadata");
    }

    [Fact]
    public async Task CreateGhnSandboxOrder_ReturnsFriendlyMessage_WhenSellerOriginPhoneInvalid()
    {
        var orderingHandler = new RecordingHttpMessageHandler(request =>
        {
            if (request.RequestUri?.AbsolutePath == "/api/orders/admin/shippings/order-info/321")
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new
                    {
                        success = true,
                        fullName = "Nguyen Van Buyer",
                        phone = "0901234567",
                        address = "45 Nguyen Van Cu, Phuong 1",
                        itemSummary = "Rau cu tong hop",
                        totalQuantity = 3,
                        itemsAmount = 145000m,
                        totalAmount = 175000m
                    })
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var identityHandler = new RecordingHttpMessageHandler(request =>
        {
            if (request.RequestUri?.AbsolutePath == "/auth/admin/settings/store")
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new
                    {
                        storeName = "FreshFarm Seller",
                        storeAddress = "Kho A",
                        storeEmail = "seller@example.com",
                        storePhone = "0123456789",
                        adminNotificationEmail = "seller@example.com",
                        ghnPickupName = "FreshFarm Seller",
                        ghnPickupPhone = "12345",
                        ghnPickupAddress = "12 Nguyen Hue",
                        ghnDistrictId = 1442,
                        ghnWardCode = "20308",
                        hasGhnOrigin = true
                    })
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var ghnService = new FakeGhnSandboxService();
        var controller = CreateController(orderingHandler, identityHandler, ghnService);

        var result = await controller.CreateGhnSandboxOrder(new ShippingController.CreateGhnSandboxOrderRequest
        {
            OrderId = 321,
            ToDistrictId = 1482,
            ToWardCode = "90777"
        }, CancellationToken.None);

        var payload = ReadJsonResult(result);
        Assert.False(payload.GetProperty("success").GetBoolean());
        Assert.Contains("thông tin lấy hàng hợp lệ", payload.GetProperty("message").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Empty(ghnService.CreateOrderRequests);
    }

    [Fact]
    public async Task GetGhnSellerOrigin_AcceptsSandboxPhoneLikePickupNumber()
    {
        var orderingHandler = new RecordingHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var identityHandler = new RecordingHttpMessageHandler(request =>
        {
            if (request.RequestUri?.AbsolutePath == "/auth/admin/settings/store")
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new
                    {
                        storeName = "FreshFarm Seller",
                        storeAddress = "Kho A",
                        storeEmail = "seller@example.com",
                        storePhone = "0123456789",
                        adminNotificationEmail = "seller@example.com",
                        ghnPickupName = "FreshFarm Seller",
                        ghnPickupPhone = "0123456789",
                        ghnPickupAddress = "12 Nguyen Hue",
                        ghnProvinceId = 202,
                        ghnDistrictId = 1442,
                        ghnWardCode = "20308",
                        hasGhnOrigin = true
                    })
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var controller = CreateController(orderingHandler, identityHandler, new FakeGhnSandboxService());

        var result = await controller.GetGhnSellerOrigin(CancellationToken.None);

        var payload = ReadJsonResult(result);
        Assert.True(payload.GetProperty("success").GetBoolean());
        Assert.True(payload.GetProperty("hasGhnOrigin").GetBoolean());
        Assert.Equal("Địa chỉ lấy hàng đã sẵn sàng để dùng.", payload.GetProperty("message").GetString());
    }

    [Fact]
    public async Task MarkReadyForPickup_CreatesGhnWaybill_ThenMarksOrderReady()
    {
        var orderingHandler = new RecordingHttpMessageHandler(request =>
        {
            if (request.RequestUri?.AbsolutePath == "/api/orders/admin/shippings/order-info/321")
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new
                    {
                        success = true,
                        fullName = "Nguyen Van Buyer",
                        phone = "0901234567",
                        email = "buyer@example.com",
                        address = "45 Nguyen Van Cu, Phuong 1",
                        shippingFee = 30000m,
                        itemsAmount = 145000m,
                        totalAmount = 175000m,
                        totalQuantity = 3,
                        itemSummary = "Rau cu tong hop",
                        paymentMethod = "COD",
                        isCod = true,
                        codAmount = 175000m
                    })
                };
            }

            if (request.RequestUri?.AbsolutePath == "/api/orders/admin/shippings/internal/321/ghn-metadata")
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new { success = true })
                };
            }

            if (request.RequestUri?.AbsolutePath == "/api/orders/admin/321/status")
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new { success = true })
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var identityHandler = CreateValidSellerOriginHandler();
        var ghnService = new FakeGhnSandboxService
        {
            CreateOrderResult = new GhnSandboxCreateOrderResult
            {
                Success = true,
                OrderCode = "GHN-321",
                ClientOrderCode = "FF-ORD-000321",
                TotalFee = 30000,
                Message = "GHN OK"
            }
        };

        var controller = CreateController(orderingHandler, identityHandler, ghnService);

        var result = await controller.MarkReadyForPickup(new ShippingController.MarkReadyForPickupRequest
        {
            OrderId = 321,
            CurrentStatus = "Pending",
            ToDistrictId = 1482,
            ToWardCode = "90777",
            Height = 13,
            Length = 24,
            Width = 22,
            Weight = 1500
        }, CancellationToken.None);

        var payload = ReadJsonResult(result);
        Assert.True(payload.GetProperty("success").GetBoolean());
        Assert.Equal("Ready", payload.GetProperty("status").GetString());
        Assert.Equal("GHN-321", payload.GetProperty("orderCode").GetString());

        var createRequest = Assert.Single(ghnService.CreateOrderRequests);
        Assert.Equal("Nguyen Van Buyer", createRequest.ToName);
        Assert.Equal(1482, createRequest.ToDistrictId);
        Assert.Equal("90777", createRequest.ToWardCode);
        Assert.Equal(13, createRequest.Height);
        Assert.Equal(24, createRequest.Length);
        Assert.Equal(22, createRequest.Width);
        Assert.Equal(1500, createRequest.Weight);

        var metadataIndex = orderingHandler.Requests.FindIndex(request =>
            request.Method == HttpMethod.Post &&
            request.RequestUri?.AbsolutePath == "/api/orders/admin/shippings/internal/321/ghn-metadata");
        var processingIndex = orderingHandler.Requests.FindIndex(request =>
            request.Method == HttpMethod.Post &&
            request.RequestUri?.AbsolutePath == "/api/orders/admin/321/status" &&
            RequestBodyContains(request, "\"newStatus\":\"Processing\""));
        var readyIndex = orderingHandler.Requests.FindIndex(request =>
            request.Method == HttpMethod.Post &&
            request.RequestUri?.AbsolutePath == "/api/orders/admin/321/status" &&
            RequestBodyContains(request, "\"newStatus\":\"Ready\""));

        Assert.InRange(metadataIndex, 0, int.MaxValue);
        Assert.True(metadataIndex < processingIndex);
        Assert.True(processingIndex < readyIndex);
    }

    [Fact]
    public async Task MarkReadyForPickup_CreatesGhnWaybill_WhenAlreadyReadyButMissingGhnCode()
    {
        var orderingHandler = new RecordingHttpMessageHandler(request =>
        {
            if (request.RequestUri?.AbsolutePath == "/api/orders/admin/shippings/order-info/321")
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new
                    {
                        success = true,
                        fullName = "Nguyen Van Buyer",
                        phone = "0901234567",
                        email = "buyer@example.com",
                        address = "45 Nguyen Van Cu, Phuong 1",
                        shippingFee = 30000m,
                        itemsAmount = 145000m,
                        totalAmount = 175000m,
                        totalQuantity = 3,
                        itemSummary = "Rau cu tong hop",
                        paymentMethod = "COD",
                        isCod = true,
                        codAmount = 175000m
                    })
                };
            }

            if (request.RequestUri?.AbsolutePath == "/api/orders/admin/shippings/internal/321/ghn-metadata")
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new { success = true })
                };
            }

            if (request.RequestUri?.AbsolutePath == "/api/orders/admin/321/status")
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new { success = true })
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var identityHandler = CreateValidSellerOriginHandler();
        var ghnService = new FakeGhnSandboxService
        {
            CreateOrderResult = new GhnSandboxCreateOrderResult
            {
                Success = true,
                OrderCode = "GHN-READY-321",
                ClientOrderCode = "FF-ORD-000321",
                TotalFee = 30000,
                Message = "GHN OK"
            }
        };

        var controller = CreateController(orderingHandler, identityHandler, ghnService);

        var result = await controller.MarkReadyForPickup(new ShippingController.MarkReadyForPickupRequest
        {
            OrderId = 321,
            CurrentStatus = "Ready",
            ToDistrictId = 1482,
            ToWardCode = "90777",
            Height = 13,
            Length = 24,
            Width = 22,
            Weight = 1500
        }, CancellationToken.None);

        var payload = ReadJsonResult(result);
        Assert.True(payload.GetProperty("success").GetBoolean());
        Assert.Equal("Ready", payload.GetProperty("status").GetString());
        Assert.Equal("GHN-READY-321", payload.GetProperty("orderCode").GetString());
        Assert.True(payload.GetProperty("ghnCreated").GetBoolean());

        var createRequest = Assert.Single(ghnService.CreateOrderRequests);
        Assert.Equal("Nguyen Van Buyer", createRequest.ToName);
        Assert.Equal(1482, createRequest.ToDistrictId);
        Assert.Equal("90777", createRequest.ToWardCode);
        Assert.Equal(13, createRequest.Height);
        Assert.Equal(24, createRequest.Length);
        Assert.Equal(22, createRequest.Width);
        Assert.Equal(1500, createRequest.Weight);

        Assert.Contains(orderingHandler.Requests, request =>
            request.Method == HttpMethod.Post &&
            request.RequestUri?.AbsolutePath == "/api/orders/admin/shippings/internal/321/ghn-metadata");
        Assert.DoesNotContain(orderingHandler.Requests, request =>
            request.Method == HttpMethod.Post &&
            request.RequestUri?.AbsolutePath == "/api/orders/admin/321/status");
    }

    [Fact]
    public async Task MarkReadyForPickup_DoesNotMarkReady_WhenGhnWaybillCreationFails()
    {
        var orderingHandler = new RecordingHttpMessageHandler(request =>
        {
            if (request.RequestUri?.AbsolutePath == "/api/orders/admin/shippings/order-info/321")
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new
                    {
                        success = true,
                        fullName = "Nguyen Van Buyer",
                        phone = "0901234567",
                        email = "buyer@example.com",
                        address = "45 Nguyen Van Cu, Phuong 1",
                        shippingFee = 30000m,
                        itemsAmount = 145000m,
                        totalAmount = 175000m,
                        totalQuantity = 3,
                        itemSummary = "Rau cu tong hop",
                        paymentMethod = "COD",
                        isCod = true,
                        codAmount = 175000m
                    })
                };
            }

            if (request.RequestUri?.AbsolutePath == "/api/orders/admin/321/status")
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new { success = true })
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var identityHandler = CreateValidSellerOriginHandler();
        var ghnService = new FakeGhnSandboxService
        {
            CreateOrderResult = new GhnSandboxCreateOrderResult
            {
                Success = false,
                Message = "GHN báo địa chỉ nhận hàng không hợp lệ."
            }
        };

        var controller = CreateController(orderingHandler, identityHandler, ghnService);

        var result = await controller.MarkReadyForPickup(new ShippingController.MarkReadyForPickupRequest
        {
            OrderId = 321,
            CurrentStatus = "Pending",
            ToDistrictId = 1482,
            ToWardCode = "90777",
            Height = 13,
            Length = 24,
            Width = 22,
            Weight = 1500
        }, CancellationToken.None);

        var payload = ReadJsonResult(result);
        Assert.False(payload.GetProperty("success").GetBoolean());
        Assert.Contains("GHN", payload.GetProperty("message").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Single(ghnService.CreateOrderRequests);
        Assert.DoesNotContain(orderingHandler.Requests, request =>
            request.Method == HttpMethod.Post &&
            request.RequestUri?.AbsolutePath == "/api/orders/admin/321/status");
    }

    private static ShippingController CreateController(
        RecordingHttpMessageHandler orderingHandler,
        RecordingHttpMessageHandler identityHandler,
        FakeGhnSandboxService ghnService)
    {
        var orderingClient = new HttpClient(orderingHandler)
        {
            BaseAddress = new Uri("https://ordering.test")
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

        var token = CreateAccessToken("15");
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, "15"),
            new Claim("sub", "15"),
            new Claim("ff_access_token", token),
            new Claim(ClaimTypes.Role, "Seller")
        ], "TestAuth"));

        return new ShippingController(
            new NamedHttpClientFactory(new Dictionary<string, HttpClient>(StringComparer.OrdinalIgnoreCase)
            {
                ["Ordering"] = orderingClient,
                ["Identity"] = identityClient
            }),
            ghnService,
            Microsoft.Extensions.Options.Options.Create(new OrderingServiceOptions
            {
                BaseUrl = "https://ordering.test",
                InternalServiceKey = "internal-key"
            }))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = httpContext
            }
        };
    }

    private static RecordingHttpMessageHandler CreateValidSellerOriginHandler()
        => new(request =>
        {
            if (request.RequestUri?.AbsolutePath == "/auth/admin/settings/store")
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new
                    {
                        storeName = "FreshFarm Seller",
                        storeAddress = "Kho A",
                        storeEmail = "seller@example.com",
                        storePhone = "0911222333",
                        adminNotificationEmail = "seller@example.com",
                        ghnPickupName = "FreshFarm Seller",
                        ghnPickupPhone = "0911222333",
                        ghnPickupAddress = "12 Nguyen Hue",
                        ghnDistrictId = 1442,
                        ghnWardCode = "20308",
                        hasGhnOrigin = true
                    })
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

    private static JsonElement ReadJsonResult(JsonResult result)
    {
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(result.Value));
        return json.RootElement.Clone();
    }

    private static bool RequestBodyContains(HttpRequestMessage request, string expected)
        => request.Content?.ReadAsStringAsync().GetAwaiter().GetResult().Contains(expected, StringComparison.Ordinal) == true;

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

    private sealed class NamedHttpClientFactory(IReadOnlyDictionary<string, HttpClient> clients) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => clients[name];
    }

    private sealed class FakeGhnSandboxService : IGhnSandboxService
    {
        public bool IsConfigured => true;

        public int? ShopId => 12345;

        public GhnSandboxCreateOrderResult CreateOrderResult { get; set; } = new()
        {
            Success = true,
            OrderCode = "GHN-DEFAULT",
            ClientOrderCode = "FF-ORD-DEFAULT"
        };

        public List<GhnSandboxCreateOrderRequest> CreateOrderRequests { get; } = new();

        public Task<GhnSandboxConnectionResult> TestConnectionAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<GhnSandboxShopProfileResult> GetShopProfileAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<GhnSandboxLocationItem>> GetProvincesAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<GhnSandboxLocationItem>> GetDistrictsAsync(int provinceId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<GhnSandboxLocationItem>> GetWardsAsync(int districtId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<GhnSandboxFeeResult> CalculateFeeAsync(GhnSandboxFeeRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<GhnSandboxLeadTimeResult> CalculateLeadTimeAsync(GhnSandboxLeadTimeRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<GhnSandboxCreateOrderResult> CreateOrderAsync(GhnSandboxCreateOrderRequest request, CancellationToken cancellationToken = default)
        {
            CreateOrderRequests.Add(new GhnSandboxCreateOrderRequest
            {
                ToName = request.ToName,
                ToPhone = request.ToPhone,
                ToAddress = request.ToAddress,
                ToDistrictId = request.ToDistrictId,
                ToWardCode = request.ToWardCode,
                ServiceTypeId = request.ServiceTypeId,
                ClientOrderCode = request.ClientOrderCode,
                Content = request.Content,
                Note = request.Note,
                RequiredNote = request.RequiredNote,
                PaymentTypeId = request.PaymentTypeId,
                CodAmount = request.CodAmount,
                InsuranceValue = request.InsuranceValue,
                Height = request.Height,
                Length = request.Length,
                Width = request.Width,
                Weight = request.Weight,
                OriginOverride = request.OriginOverride,
                Items = request.Items
                    .Select(item => new GhnSandboxOrderItemRequest
                    {
                        Name = item.Name,
                        Code = item.Code,
                        Quantity = item.Quantity,
                        Price = item.Price,
                        Height = item.Height,
                        Length = item.Length,
                        Width = item.Width,
                        Weight = item.Weight
                    })
                    .ToList()
            });

            return Task.FromResult(CreateOrderResult);
        }

        public Task<GhnSandboxOrderTrackingResult> GetOrderTrackingAsync(GhnSandboxOrderTrackingRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
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

        public void Set(string key, byte[] value) => _store[key] = value.ToArray();

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
