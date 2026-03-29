using FreshFarm.Web.Bff.Areas.Seller.Infrastructure;
using FreshFarm.Web.Bff.Areas.Seller.Models;
using FreshFarm.Web.Bff.Options;
using FreshFarm.Web.Bff.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Extensions.Options;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using System.Text.Json;

namespace FreshFarm.Web.Bff.Areas.Seller.Controllers;

[Authorize(Roles = "Seller")]
[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
[Area("Seller")]
public class ShippingController : LegacySellerControllerBase
{
    private const string AccessTokenSessionKey = "ACCESS_TOKEN";

    private static readonly string[] PaidStatuses = { "Đã thanh toán", "Da thanh toan", "Hoàn tất", "Hoan tat" };
    private static readonly Regex VietnamPhoneRegex = new(@"^(0(3|5|7|8|9)\d{8}|\+84(3|5|7|8|9)\d{8})$", RegexOptions.Compiled);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IGhnSandboxService _ghnSandboxService;
    private readonly OrderingServiceOptions _orderingServiceOptions;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public ShippingController(
        IHttpClientFactory httpClientFactory,
        IGhnSandboxService ghnSandboxService,
        IOptions<OrderingServiceOptions> orderingServiceOptions)
    {
        _httpClientFactory = httpClientFactory;
        _ghnSandboxService = ghnSandboxService;
        _orderingServiceOptions = orderingServiceOptions.Value;
    }

    public async Task<IActionResult> ManageShipping(
        string status = "",
        int? staffId = null,
        string q = "",
        string sort = "date_desc",
        int page = 1,
        int pageSize = 10,
        string deliveredState = "")
    {
        ViewBag.CurrentPage = page;
        ViewBag.TotalPages = 1;
        ViewBag.TotalItems = 0;
        ViewBag.PageSize = pageSize;
        ViewBag.CurrentStatus = status;
        ViewBag.CurrentStaffId = staffId;
        ViewBag.CurrentQuery = q;
        ViewBag.CurrentSort = sort;
        ViewBag.DeliveredState = deliveredState;
        ViewBag.PaidStatuses = PaidStatuses;
        ViewBag.DeliveryStaffs = new SelectList(Enumerable.Empty<SelectListItem>(), "Value", "Text");
        ViewBag.Orders = new SelectList(Enumerable.Empty<SelectListItem>(), "Value", "Text");
        ViewBag.Provinces = new SelectList(Enumerable.Empty<SelectListItem>(), "Value", "Text");
        ViewBag.GhnSandboxConfigured = _ghnSandboxService.IsConfigured;
        ViewBag.GhnSandboxShopId = _ghnSandboxService.ShopId;
        ViewBag.SellerGhnStoreName = string.Empty;
        ViewBag.SellerGhnHasOrigin = false;

        try
        {
            var currentStoreSettings = await GetCurrentStoreSettingsAsync(CancellationToken.None);
            ViewBag.SellerGhnStoreName = currentStoreSettings?.StoreName ?? string.Empty;
            ViewBag.SellerGhnHasOrigin = currentStoreSettings?.HasGhnOrigin ?? false;

            var client = CreateAuthorizedClient("Ordering");

            var query = new List<string>
            {
                $"status={Uri.EscapeDataString(status ?? string.Empty)}",
                $"q={Uri.EscapeDataString(q ?? string.Empty)}",
                $"sort={Uri.EscapeDataString(sort ?? string.Empty)}",
                $"page={page}",
                $"pageSize={pageSize}",
                $"deliveredState={Uri.EscapeDataString(deliveredState ?? string.Empty)}"
            };

            if (staffId.HasValue)
            {
                query.Add($"staffId={staffId.Value}");
            }

            var response = await client.GetAsync($"/api/orders/admin/shippings?{string.Join("&", query)}");
            if (!response.IsSuccessStatusCode)
            {
                TempData["Error"] = "Co loi khi tai danh sach van chuyen: " + await ReadApiErrorAsync(response, "Unknown error");
                return View(new List<Shipping>());
            }

            var payload = await response.Content.ReadFromJsonAsync<ShippingListApiResponse>(JsonOptions);
            var data = payload?.data;

            var items = data?.items?.Select(MapShipping).ToList() ?? new List<Shipping>();

            ViewBag.CurrentPage = data?.currentPage ?? page;
            ViewBag.TotalPages = data?.totalPages ?? 1;
            ViewBag.TotalItems = data?.totalItems ?? items.Count;
            ViewBag.PageSize = data?.pageSize ?? pageSize;
            ViewBag.CurrentStatus = data?.currentStatus ?? status;
            ViewBag.CurrentStaffId = data?.currentStaffId;
            ViewBag.CurrentQuery = data?.currentQuery ?? q;
            ViewBag.CurrentSort = data?.currentSort ?? sort;
            ViewBag.DeliveredState = data?.deliveredState ?? deliveredState;
            ViewBag.PaidStatuses = PaidStatuses;
            ViewBag.GhnSandboxConfigured = _ghnSandboxService.IsConfigured;
            ViewBag.GhnSandboxShopId = _ghnSandboxService.ShopId;

            var staffs = (data?.deliveryStaffs ?? new List<DeliveryStaffDto>())
                .Select(s => new SelectListItem
                {
                    Value = s.adminID.ToString(),
                    Text = string.IsNullOrWhiteSpace(s.name) ? $"NV #{s.adminID:D4}" : s.name
                })
                .ToList();
            ViewBag.DeliveryStaffs = new SelectList(staffs, "Value", "Text", (data?.currentStaffId)?.ToString());

            var orders = (data?.orders ?? new List<OrderOptionDto>())
                .Select(o => new
                {
                    o.orderID,
                    displayText = string.IsNullOrWhiteSpace(o.displayText)
                        ? "Don hang #DH" + o.orderID.ToString().PadLeft(5, '0')
                        : o.displayText
                })
                .ToList();
            ViewBag.Orders = new SelectList(orders, "orderID", "displayText");

            var provinces = (data?.provinces ?? new List<ProvinceDto>())
                .Select(p => new SelectListItem
                {
                    Value = p.provinceId.ToString(),
                    Text = p.provinceName ?? string.Empty
                })
                .ToList();
            ViewBag.Provinces = new SelectList(provinces, "Value", "Text");

            return View(items);
        }
        catch (Exception ex)
        {
            TempData["Error"] = "Co loi khi tai danh sach van chuyen: " + ex.Message;
            return View(new List<Shipping>());
        }
    }

    [HttpGet]
    public async Task<JsonResult> GetGhnSellerOrigin(CancellationToken cancellationToken)
    {
        var settings = await GetCurrentStoreSettingsAsync(cancellationToken);
        var origin = BuildOriginOverride(settings);

        return Json(new
        {
            success = settings is not null,
            configured = _ghnSandboxService.IsConfigured,
            shopId = _ghnSandboxService.ShopId,
            storeName = settings?.StoreName ?? string.Empty,
            pickupName = settings?.GhnPickupName ?? settings?.StoreName ?? string.Empty,
            pickupPhone = settings?.GhnPickupPhone ?? settings?.StorePhone ?? string.Empty,
            pickupAddress = settings?.GhnPickupAddress ?? string.Empty,
            provinceId = settings?.GhnProvinceId,
            provinceName = settings?.GhnProvinceName ?? string.Empty,
            districtId = settings?.GhnDistrictId,
            districtName = settings?.GhnDistrictName ?? string.Empty,
            wardCode = settings?.GhnWardCode ?? string.Empty,
            wardName = settings?.GhnWardName ?? string.Empty,
            hasGhnOrigin = settings?.HasGhnOrigin ?? false,
            message = origin is null
                ? "Bạn chưa lưu đủ địa chỉ lấy hàng. Hãy vào Cài đặt cửa hàng để cập nhật trước."
                : "Địa chỉ lấy hàng đã sẵn sàng để dùng."
        });
    }

    [HttpGet]
    public async Task<JsonResult> GetGhnProvinces(CancellationToken cancellationToken)
    {
        var items = await _ghnSandboxService.GetProvincesAsync(cancellationToken);
        return Json(new
        {
            success = true,
            count = items.Count,
            items
        });
    }

    [HttpGet]
    public async Task<JsonResult> GetGhnDistricts(int provinceId, CancellationToken cancellationToken)
    {
        if (provinceId <= 0)
        {
            return Json(new
            {
                success = false,
                message = "Thiếu mã tỉnh/thành GHN."
            });
        }

        var items = await _ghnSandboxService.GetDistrictsAsync(provinceId, cancellationToken);
        return Json(new
        {
            success = true,
            provinceId,
            count = items.Count,
            items
        });
    }

    [HttpGet]
    public async Task<JsonResult> GetGhnWards(int districtId, CancellationToken cancellationToken)
    {
        if (districtId <= 0)
        {
            return Json(new
            {
                success = false,
                message = "Thiếu mã quận/huyện GHN."
            });
        }

        var items = await _ghnSandboxService.GetWardsAsync(districtId, cancellationToken);
        return Json(new
        {
            success = true,
            districtId,
            count = items.Count,
            items
        });
    }

    [HttpGet]
    public async Task<JsonResult> PreviewGhnFee([FromQuery] PreviewGhnFeeRequest? request, CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return Json(new
            {
                success = false,
                message = "Thiếu dữ liệu yêu cầu tính phí GHN."
            });
        }

        var originOverride = await GetCurrentOriginOverrideAsync(cancellationToken);
        var result = await _ghnSandboxService.CalculateFeeAsync(new GhnSandboxFeeRequest
        {
            ToDistrictId = request.ToDistrictId,
            ToWardCode = request.ToWardCode ?? string.Empty,
            ServiceTypeId = request.ServiceTypeId,
            Height = request.Height,
            Length = request.Length,
            Width = request.Width,
            Weight = request.Weight,
            InsuranceValue = request.InsuranceValue,
            ItemName = request.ItemName ?? string.Empty,
            ItemQuantity = request.ItemQuantity,
            OriginOverride = originOverride
        }, cancellationToken);

        return Json(new
        {
            success = result.Success,
            assumedShopId = result.ShopId,
            serviceId = result.ServiceId,
            serviceName = result.ServiceName,
            totalFee = result.TotalFee,
            mainServiceFee = result.MainServiceFee,
            insuranceFee = result.InsuranceFee,
            fromDistrictId = result.FromDistrictId,
            fromWardCode = result.FromWardCode,
            message = originOverride is null && !result.Success
                ? "Seller chưa cấu hình origin GHN. Hãy cập nhật Cài đặt cửa hàng trước khi tính phí."
                : result.Message
        });
    }

    [HttpGet]
    public async Task<JsonResult> PreviewGhnLeadTime([FromQuery] PreviewGhnLeadTimeRequest? request, CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return Json(new
            {
                success = false,
                message = "Thiếu dữ liệu yêu cầu leadtime GHN."
            });
        }

        var originOverride = await GetCurrentOriginOverrideAsync(cancellationToken);
        var result = await _ghnSandboxService.CalculateLeadTimeAsync(new GhnSandboxLeadTimeRequest
        {
            ToDistrictId = request.ToDistrictId,
            ToWardCode = request.ToWardCode ?? string.Empty,
            ServiceTypeId = request.ServiceTypeId,
            OriginOverride = originOverride
        }, cancellationToken);

        return Json(new
        {
            success = result.Success,
            assumedShopId = result.ShopId,
            serviceId = result.ServiceId,
            serviceName = result.ServiceName,
            fromDistrictId = result.FromDistrictId,
            fromWardCode = result.FromWardCode,
            leadTimeUnix = result.LeadTimeUnix,
            leadTime = result.LeadTime?.ToString("O"),
            orderDateUnix = result.OrderDateUnix,
            orderDate = result.OrderDate?.ToString("O"),
            message = originOverride is null && !result.Success
                ? "Seller chưa cấu hình origin GHN. Hãy cập nhật Cài đặt cửa hàng trước khi lấy leadtime."
                : result.Message
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<JsonResult> CreateGhnSandboxOrder([FromBody] CreateGhnSandboxOrderRequest? request, CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return Json(new
            {
                success = false,
                message = "Thiếu dữ liệu yêu cầu tạo đơn GHN."
            });
        }

        if (request.OrderId <= 0)
        {
            return Json(new
            {
                success = false,
                message = "Bạn cần chọn một đơn hàng có sẵn trước khi tạo vận đơn."
            });
        }

        var orderInfo = await GetOrderInfoPayloadAsync(request.OrderId, cancellationToken);
        if (orderInfo is null || !orderInfo.success)
        {
            return Json(new
            {
                success = false,
                message = "Không tìm thấy đơn hàng hợp lệ để tạo vận đơn."
            });
        }

        if (!string.IsNullOrWhiteSpace(orderInfo.ghnOrderCode) || !string.IsNullOrWhiteSpace(orderInfo.ghnClientOrderCode))
        {
            return Json(new
            {
                success = false,
                message = "Đơn này đã có vận đơn GHN. Hãy dùng tra cứu hoặc đồng bộ thay vì tạo thêm vận đơn mới."
            });
        }

        if (string.IsNullOrWhiteSpace(orderInfo.fullName)
            || string.IsNullOrWhiteSpace(orderInfo.phone)
            || string.IsNullOrWhiteSpace(orderInfo.address))
        {
            return Json(new
            {
                success = false,
                message = "Đơn hàng chưa có đủ thông tin người nhận để tạo vận đơn GHN."
            });
        }

        if (request.ToDistrictId <= 0 || string.IsNullOrWhiteSpace(request.ToWardCode))
        {
            return Json(new
            {
                success = false,
                message = "Không xác định được quận/phường GHN cho đơn hàng này."
            });
        }

        var originOverride = await GetCurrentOriginOverrideAsync(cancellationToken);
        if (originOverride is null)
        {
            return Json(new
            {
                success = false,
                message = "Bạn chưa lưu đủ thông tin lấy hàng hợp lệ. Hãy cập nhật địa chỉ và số điện thoại trong Cài đặt cửa hàng trước."
            });
        }

        var boundedContent = BuildBoundShipmentContent(request.OrderId);
        var boundedQuantity = NormalizePositiveInt(orderInfo.totalQuantity, fallback: 1);
        var boundedItemPrice = ConvertCurrencyAmountToInt(orderInfo.itemsAmount);
        var boundedInsuranceValue = ConvertCurrencyAmountToInt(orderInfo.totalAmount);
        var boundedCodAmount = orderInfo.isCod
            ? ConvertCurrencyAmountToInt(orderInfo.codAmount ?? orderInfo.totalAmount)
            : 0;
        var packageHeight = NormalizePositiveInt(request.Height, fallback: 10);
        var packageLength = NormalizePositiveInt(request.Length, fallback: 20);
        var packageWidth = NormalizePositiveInt(request.Width, fallback: 20);
        var packageWeight = NormalizePositiveInt(request.Weight, fallback: 500);

        var result = await _ghnSandboxService.CreateOrderAsync(new GhnSandboxCreateOrderRequest
        {
            ToName = orderInfo.fullName.Trim(),
            ToPhone = orderInfo.phone.Trim(),
            ToAddress = orderInfo.address.Trim(),
            ToDistrictId = request.ToDistrictId,
            ToWardCode = request.ToWardCode ?? string.Empty,
            ServiceTypeId = request.ServiceTypeId,
            ClientOrderCode = BuildBoundClientOrderCode(request.OrderId),
            Content = boundedContent,
            Note = request.Note ?? string.Empty,
            RequiredNote = request.RequiredNote ?? string.Empty,
            PaymentTypeId = request.PaymentTypeId,
            CodAmount = boundedCodAmount,
            InsuranceValue = boundedInsuranceValue,
            Height = packageHeight,
            Length = packageLength,
            Width = packageWidth,
            Weight = packageWeight,
            OriginOverride = originOverride,
            Items = new List<GhnSandboxOrderItemRequest>
            {
                new()
                {
                    Name = string.IsNullOrWhiteSpace(orderInfo.itemSummary) ? "Gói hàng tổng hợp" : orderInfo.itemSummary.Trim(),
                    Code = BuildBoundItemCode(request.OrderId),
                    Quantity = boundedQuantity,
                    Price = boundedItemPrice,
                    Height = packageHeight,
                    Length = packageLength,
                    Width = packageWidth,
                    Weight = packageWeight
                }
            }
        }, cancellationToken);

        if (result.Success)
        {
            var metadataPersisted = await PersistGhnMetadataAsync(request.OrderId, new GhnMetadataUpsertRequest
            {
                OrderCode = result.OrderCode,
                ClientOrderCode = result.ClientOrderCode,
                TotalFee = result.TotalFee,
                CreatedAt = DateTime.UtcNow,
                ExpectedDeliveryTime = result.ExpectedDeliveryTime?.UtcDateTime,
                LastSyncedAt = DateTime.UtcNow
            }, cancellationToken);

            if (!metadataPersisted)
            {
                result.Message = string.IsNullOrWhiteSpace(result.Message)
                    ? "Tạo đơn GHN sandbox thành công nhưng chưa lưu được metadata nội bộ."
                    : $"{result.Message} Tuy nhiên metadata GHN nội bộ chưa được lưu ngay.";
            }
        }

        return Json(new
        {
            success = result.Success,
            assumedShopId = result.ShopId,
            clientOrderCode = result.ClientOrderCode,
            orderCode = result.OrderCode,
            sortCode = result.SortCode,
            serviceId = result.ServiceId,
            serviceName = result.ServiceName,
            totalFee = result.TotalFee,
            expectedDeliveryTimeUnix = result.ExpectedDeliveryTimeUnix,
            expectedDeliveryTime = result.ExpectedDeliveryTime?.ToString("O"),
            fromDistrictId = result.FromDistrictId,
            fromWardCode = result.FromWardCode,
            message = result.Message
        });
    }

    [HttpGet]
    public async Task<JsonResult> GetGhnSandboxOrder([FromQuery] GetGhnSandboxOrderRequest? request, CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return Json(new
            {
                success = false,
                message = "Thiếu dữ liệu yêu cầu tra cứu đơn GHN."
            });
        }

        var result = await _ghnSandboxService.GetOrderTrackingAsync(new GhnSandboxOrderTrackingRequest
        {
            OrderCode = request.OrderCode ?? string.Empty,
            ClientOrderCode = request.ClientOrderCode ?? string.Empty
        }, cancellationToken);

        var selectedOrderId = ParseNullableInt(Request.Query["orderId"].ToString());
        if (result.Success && selectedOrderId.HasValue)
        {
            await PersistGhnMetadataAsync(selectedOrderId.Value, new GhnMetadataUpsertRequest
            {
                OrderCode = result.OrderCode,
                ClientOrderCode = result.ClientOrderCode,
                Status = result.Status,
                StatusLabel = result.StatusLabel,
                TotalFee = result.TotalFee,
                CreatedAt = result.CreatedDate?.UtcDateTime,
                ExpectedDeliveryTime = result.LeadTime?.UtcDateTime,
                LastSyncedAt = DateTime.UtcNow
            }, cancellationToken);
        }

        return Json(new
        {
            success = result.Success,
            assumedShopId = result.ShopId,
            orderCode = result.OrderCode,
            clientOrderCode = result.ClientOrderCode,
            status = result.Status,
            statusLabel = result.StatusLabel,
            serviceName = result.ServiceName,
            toName = result.ToName,
            toPhone = result.ToPhone,
            toAddress = result.ToAddress,
            codAmount = result.CodAmount,
            totalFee = result.TotalFee,
            createdDate = result.CreatedDate?.ToString("O"),
            leadTime = result.LeadTime?.ToString("O"),
            finishedDate = result.FinishedDate?.ToString("O"),
            logs = result.Logs.Select(log => new
            {
                status = log.Status,
                statusLabel = log.StatusLabel,
                updatedAt = log.UpdatedAt?.ToString("O")
            }),
            message = result.Message
        });
    }

    [HttpGet]
    public async Task<JsonResult> GetCommunes(int provinceId)
    {
        try
        {
            var client = CreateAuthorizedClient("Ordering");
            var response = await client.GetAsync($"/api/orders/admin/shippings/communes?provinceId={provinceId}");

            if (!response.IsSuccessStatusCode)
            {
                return Json(new { error = await ReadApiErrorAsync(response, "Khong the tai danh sach xa/phuong") });
            }

            var communes = await response.Content.ReadFromJsonAsync<List<CommuneOptionDto>>(JsonOptions)
                ?? new List<CommuneOptionDto>();

            return Json(communes.Select(c => new { value = c.value, text = c.text }));
        }
        catch (Exception ex)
        {
            return Json(new { error = ex.Message });
        }
    }

    [HttpGet]
    public async Task<JsonResult> GetOrderInfo(int orderId)
    {
        try
        {
            var client = CreateAuthorizedClient("Ordering");
            var response = await client.GetAsync($"/api/orders/admin/shippings/order-info/{orderId}");
            if (!response.IsSuccessStatusCode)
            {
                return Json(new { success = false, message = await ReadApiErrorAsync(response, "Khong tim thay don hang") });
            }

            var payload = await response.Content.ReadFromJsonAsync<OrderInfoApiResponse>(JsonOptions);
            if (payload is null)
            {
                return Json(new { success = false, message = "Khong doc duoc thong tin don hang" });
            }

            return Json(payload);
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = ex.Message });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public JsonResult Create(Shipping shipping)
    {
        return Json(new
        {
            success = false,
            message = "Đơn hàng đã có dòng vận chuyển tự tạo từ lúc checkout. Seller không thể thêm vận chuyển thủ công nữa; hãy chỉnh sửa bản ghi hiện có hoặc tạo vận đơn GHN theo đơn đã chọn."
        });
    }

    [HttpGet]
    public async Task<JsonResult> Edit(int id)
    {
        try
        {
            var client = CreateAuthorizedClient("Ordering");
            var response = await client.GetAsync($"/api/orders/admin/shippings/{id}");

            if (!response.IsSuccessStatusCode)
            {
                return Json(new { success = false, message = await ReadApiErrorAsync(response, "Khong tim thay thong tin van chuyen") });
            }

            var payload = await response.Content.ReadFromJsonAsync<ShippingEditApiResponse>(JsonOptions);
            if (payload is null)
            {
                return Json(new { success = false, message = "Khong doc duoc du lieu" });
            }

            return Json(payload);
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = ex.Message });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<JsonResult> Edit(Shipping shipping)
    {
        try
        {
            var client = CreateAuthorizedClient("Ordering");
            var response = await client.PutAsJsonAsync($"/api/orders/admin/shippings/{shipping.ShippingID}", new
            {
                orderID = shipping.OrderID,
                shippingType = shipping.ShippingType,
                fullName = shipping.FullName,
                phone = shipping.Phone,
                email = shipping.Email,
                addressDetail = shipping.AddressDetail,
                provinceId = shipping.ProvinceId,
                communeId = shipping.CommuneId,
                isStorePickup = shipping.IsStorePickup,
                storeAddress = shipping.StoreAddress,
                deliveryStaffId = ParseNullableInt(Request.Form["DeliveryStaffId"].ToString())
            });

            if (!response.IsSuccessStatusCode)
            {
                return Json(new { success = false, message = await ReadApiErrorAsync(response, "Khong the cap nhat van chuyen") });
            }

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var root = doc.RootElement;

            return Json(new
            {
                success = root.TryGetProperty("success", out var success) && success.GetBoolean(),
                message = root.TryGetProperty("message", out var message) ? message.GetString() : "Cap nhat van chuyen thanh cong"
            });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = "Loi: " + ex.Message });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<JsonResult> Delete(int id)
    {
        try
        {
            var client = CreateAuthorizedClient("Ordering");
            var response = await client.DeleteAsync($"/api/orders/admin/shippings/{id}");

            if (!response.IsSuccessStatusCode)
            {
                return Json(new { success = false, message = await ReadApiErrorAsync(response, "Khong the xoa thong tin van chuyen") });
            }

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var root = doc.RootElement;

            return Json(new
            {
                success = root.TryGetProperty("success", out var success) && success.GetBoolean(),
                message = root.TryGetProperty("message", out var message) ? message.GetString() : "Xoa thong tin van chuyen thanh cong"
            });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = "Loi: " + ex.Message });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<JsonResult> ReconcileCod(int orderId)
    {
        try
        {
            var client = CreateAuthorizedClient("Ordering");
            var response = await client.PostAsJsonAsync("/api/orders/admin/shippings/reconcile-cod", new
            {
                orderId
            });

            if (!response.IsSuccessStatusCode)
            {
                return Json(new { success = false, message = await ReadApiErrorAsync(response, "Khong the doi soat COD") });
            }

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var root = doc.RootElement;

            return Json(new
            {
                success = root.TryGetProperty("success", out var success) && success.GetBoolean(),
                message = root.TryGetProperty("message", out var message) ? message.GetString() : "Da doi soat COD"
            });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = "Loi: " + ex.Message });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<JsonResult> UnreconcileCod(int orderId)
    {
        try
        {
            var client = CreateAuthorizedClient("Ordering");
            var response = await client.PostAsJsonAsync("/api/orders/admin/shippings/unreconcile-cod", new
            {
                orderId
            });

            if (!response.IsSuccessStatusCode)
            {
                return Json(new { success = false, message = await ReadApiErrorAsync(response, "Khong the bo doi soat COD") });
            }

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var root = doc.RootElement;

            return Json(new
            {
                success = root.TryGetProperty("success", out var success) && success.GetBoolean(),
                message = root.TryGetProperty("message", out var message) ? message.GetString() : "Da bo doi soat COD"
            });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = "Loi: " + ex.Message });
        }
    }

    private static int? ParseNullableInt(string? value)
    {
        return int.TryParse(value, out var result) && result > 0 ? result : null;
    }

    private HttpClient CreateAuthorizedClient(string clientName)
    {
        var client = _httpClientFactory.CreateClient(clientName);

        client.DefaultRequestHeaders.Remove("Authorization");
        client.DefaultRequestHeaders.Authorization = null;

        var token = GetAccessToken(AccessTokenSessionKey);
        if (!string.IsNullOrWhiteSpace(token))
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return client;
    }

    private async Task<SellerSettingViewModel?> GetCurrentStoreSettingsAsync(CancellationToken cancellationToken)
    {
        try
        {
            var client = CreateAuthorizedClient("Identity");
            var response = await client.GetAsync("/auth/admin/settings/store", cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            return await response.Content.ReadFromJsonAsync<SellerSettingViewModel>(JsonOptions, cancellationToken);
        }
        catch
        {
            return null;
        }
    }

    private async Task<GhnSandboxOriginOverride?> GetCurrentOriginOverrideAsync(CancellationToken cancellationToken)
    {
        var settings = await GetCurrentStoreSettingsAsync(cancellationToken);
        return BuildOriginOverride(settings);
    }

    private async Task<OrderInfoApiResponse?> GetOrderInfoPayloadAsync(int orderId, CancellationToken cancellationToken)
    {
        if (orderId <= 0)
        {
            return null;
        }

        try
        {
            var client = CreateAuthorizedClient("Ordering");
            var response = await client.GetAsync($"/api/orders/admin/shippings/order-info/{orderId}", cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            return await response.Content.ReadFromJsonAsync<OrderInfoApiResponse>(JsonOptions, cancellationToken);
        }
        catch
        {
            return null;
        }
    }

    private static string BuildBoundClientOrderCode(int orderId)
    {
        return $"FF-ORD-{orderId:D6}";
    }

    private static string BuildBoundItemCode(int orderId)
    {
        return $"ORDER-{orderId}";
    }

    private static string BuildBoundShipmentContent(int orderId)
    {
        return $"Đơn giao vận cho #DH{orderId:D5}";
    }

    private static int NormalizePositiveInt(int? value, int fallback)
    {
        return value.HasValue && value.Value > 0 ? value.Value : fallback;
    }

    private static int ConvertCurrencyAmountToInt(decimal? value)
    {
        if (!value.HasValue || value.Value <= 0)
        {
            return 0;
        }

        return (int)Math.Round(value.Value, MidpointRounding.AwayFromZero);
    }

    private async Task<bool> PersistGhnMetadataAsync(int orderId, GhnMetadataUpsertRequest request, CancellationToken cancellationToken)
    {
        if (orderId <= 0)
        {
            return false;
        }

        try
        {
            var client = CreateAuthorizedClient("Ordering");
            if (!string.IsNullOrWhiteSpace(_orderingServiceOptions.InternalServiceKey))
            {
                using var internalMessage = new HttpRequestMessage(HttpMethod.Post, $"/api/orders/admin/shippings/internal/{orderId}/ghn-metadata")
                {
                    Content = JsonContent.Create(request)
                };
                internalMessage.Headers.Add("X-Internal-Service-Key", _orderingServiceOptions.InternalServiceKey);

                using var internalResponse = await client.SendAsync(internalMessage, cancellationToken);
                if (internalResponse.IsSuccessStatusCode)
                {
                    return true;
                }
            }

            using var fallbackResponse = await client.PostAsJsonAsync($"/api/orders/admin/shippings/{orderId}/ghn-metadata", request, cancellationToken);
            return fallbackResponse.IsSuccessStatusCode;
        }
        catch
        {
            // Best-effort sync only. UI flow should not fail because metadata persistence is unavailable.
            return false;
        }
    }

    private static GhnSandboxOriginOverride? BuildOriginOverride(SellerSettingViewModel? settings)
    {
        var pickupPhone = !string.IsNullOrWhiteSpace(settings?.GhnPickupPhone)
            ? settings.GhnPickupPhone.Trim()
            : settings?.StorePhone.Trim();

        if (settings is null
            || !settings.GhnDistrictId.HasValue
            || settings.GhnDistrictId.Value <= 0
            || string.IsNullOrWhiteSpace(settings.GhnWardCode)
            || string.IsNullOrWhiteSpace(settings.GhnPickupAddress)
            || !IsVietnamPhone(pickupPhone))
        {
            return null;
        }

        return new GhnSandboxOriginOverride
        {
            FromDistrictId = settings.GhnDistrictId,
            FromWardCode = settings.GhnWardCode.Trim(),
            ReturnAddress = settings.GhnPickupAddress.Trim(),
            ReturnPhone = pickupPhone!,
            PickupName = !string.IsNullOrWhiteSpace(settings.GhnPickupName)
                ? settings.GhnPickupName.Trim()
                : settings.StoreName.Trim()
        };
    }

    private static bool IsVietnamPhone(string? value)
    {
        return !string.IsNullOrWhiteSpace(value) && VietnamPhoneRegex.IsMatch(value.Trim());
    }

    private static Shipping MapShipping(ShippingItemDto item)
    {
        return new Shipping
        {
            ShippingID = item.shippingID,
            OrderID = item.orderID,
            ShippingType = item.shippingType ?? "Giao noi bo",
            FullName = item.fullName ?? string.Empty,
            Phone = item.phone ?? string.Empty,
            Email = item.email,
            AddressDetail = item.addressDetail,
            ProvinceId = item.provinceId,
            CommuneId = item.communeId,
            GhnOrderCode = item.ghnOrderCode,
            GhnClientOrderCode = item.ghnClientOrderCode,
            GhnStatus = item.ghnStatus,
            GhnStatusLabel = item.ghnStatusLabel,
            GhnTotalFee = item.ghnTotalFee,
            GhnCreatedAt = item.ghnCreatedAt,
            GhnExpectedDeliveryTime = item.ghnExpectedDeliveryTime,
            GhnLastSyncedAt = item.ghnLastSyncedAt,
            IsStorePickup = item.isStorePickup,
            StoreAddress = item.storeAddress,
            Province = item.province is null
                ? null
                : new Province
                {
                    ProvinceId = item.province.provinceId,
                    ProvinceName = item.province.provinceName ?? string.Empty
                },
            Commune = item.commune is null
                ? null
                : new Commune
                {
                    CommuneId = item.commune.communeId,
                    CommuneName = item.commune.communeName ?? string.Empty
                },
            Order = item.order is null
                ? null
                : new Order
                {
                    OrderID = item.order.orderID,
                    Status = item.order.status ?? string.Empty,
                    ShippingFee = item.order.shippingFee,
                    ItemsAmount = item.order.itemsAmount,
                    TotalAmount = item.order.totalAmount,
                    TotalQuantity = item.order.totalQuantity,
                    ItemSummary = item.order.itemSummary,
                    Payments = item.order.payments?.Select(p => new Payment
                    {
                        PaymentID = p.paymentID,
                        PaymentMethod = p.paymentMethod ?? string.Empty,
                        PaymentStatus = p.paymentStatus,
                        PaymentDate = p.paymentDate
                    }).ToList() ?? new List<Payment>(),
                    DeliveryAssignments = new List<DeliveryAssignment>()
                }
        };
    }

    private static async Task<string> ReadApiErrorAsync(HttpResponseMessage response, string fallback)
    {
        var body = await response.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(body))
        {
            return fallback;
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            if (root.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.String)
            {
                return message.GetString() ?? fallback;
            }

            if (root.TryGetProperty("detail", out var detail) && detail.ValueKind == JsonValueKind.String)
            {
                return detail.GetString() ?? fallback;
            }

            if (root.TryGetProperty("title", out var title) && title.ValueKind == JsonValueKind.String)
            {
                return title.GetString() ?? fallback;
            }
        }
        catch
        {
            // ignore parse failure
        }

        return fallback;
    }

    private sealed class ShippingListApiResponse
    {
        public bool success { get; set; }

        public ShippingListDataDto? data { get; set; }
    }

    private sealed class ShippingListDataDto
    {
        public List<ShippingItemDto>? items { get; set; }

        public int totalItems { get; set; }

        public int totalPages { get; set; }

        public int currentPage { get; set; }

        public int pageSize { get; set; }

        public string? currentStatus { get; set; }

        public int? currentStaffId { get; set; }

        public string? currentQuery { get; set; }

        public string? currentSort { get; set; }

        public string? deliveredState { get; set; }

        public List<ProvinceDto>? provinces { get; set; }

        public List<DeliveryStaffDto>? deliveryStaffs { get; set; }

        public List<OrderOptionDto>? orders { get; set; }
    }

    private sealed class ShippingItemDto
    {
        public int shippingID { get; set; }

        public int orderID { get; set; }

        public string? shippingType { get; set; }

        public string? fullName { get; set; }

        public string? phone { get; set; }

        public string? email { get; set; }

        public string? addressDetail { get; set; }

        public int? provinceId { get; set; }

        public int? communeId { get; set; }

        public string? ghnOrderCode { get; set; }

        public string? ghnClientOrderCode { get; set; }

        public string? ghnStatus { get; set; }

        public string? ghnStatusLabel { get; set; }

        public decimal? ghnTotalFee { get; set; }

        public DateTime? ghnCreatedAt { get; set; }

        public DateTime? ghnExpectedDeliveryTime { get; set; }

        public DateTime? ghnLastSyncedAt { get; set; }

        public bool isStorePickup { get; set; }

        public string? storeAddress { get; set; }

        public ProvinceDto? province { get; set; }

        public CommuneDto? commune { get; set; }

        public ShippingOrderDto? order { get; set; }
    }

    private sealed class ShippingOrderDto
    {
        public int orderID { get; set; }

        public string? status { get; set; }

        public decimal? shippingFee { get; set; }

        public decimal? itemsAmount { get; set; }

        public decimal? totalAmount { get; set; }

        public int? totalQuantity { get; set; }

        public string? itemSummary { get; set; }

        public List<ShippingPaymentDto>? payments { get; set; }
    }

    private sealed class ShippingPaymentDto
    {
        public int paymentID { get; set; }

        public string? paymentMethod { get; set; }

        public string? paymentStatus { get; set; }

        public DateTime? paymentDate { get; set; }
    }

    private sealed class ProvinceDto
    {
        public int provinceId { get; set; }

        public string? provinceName { get; set; }
    }

    private sealed class CommuneDto
    {
        public int communeId { get; set; }

        public string? communeName { get; set; }
    }

    private sealed class DeliveryStaffDto
    {
        public int adminID { get; set; }

        public string? name { get; set; }
    }

    private sealed class OrderOptionDto
    {
        public int orderID { get; set; }

        public string? displayText { get; set; }
    }

    private sealed class CommuneOptionDto
    {
        public int value { get; set; }

        public string? text { get; set; }
    }

    private sealed class OrderInfoApiResponse
    {
        public bool success { get; set; }

        public string? fullName { get; set; }

        public string? phone { get; set; }

        public string? email { get; set; }

        public string? address { get; set; }

        public int? provinceId { get; set; }

        public int? communeId { get; set; }

        public decimal? shippingFee { get; set; }

        public decimal? itemsAmount { get; set; }

        public decimal? totalAmount { get; set; }

        public int? totalQuantity { get; set; }

        public string? itemSummary { get; set; }

        public string? ghnOrderCode { get; set; }

        public string? ghnClientOrderCode { get; set; }

        public string? ghnStatus { get; set; }

        public string? ghnStatusLabel { get; set; }

        public decimal? ghnTotalFee { get; set; }

        public DateTime? ghnCreatedAt { get; set; }

        public DateTime? ghnExpectedDeliveryTime { get; set; }

        public DateTime? ghnLastSyncedAt { get; set; }

        public string? paymentMethod { get; set; }

        public bool isCod { get; set; }

        public decimal? codAmount { get; set; }
    }

    private sealed class ShippingEditApiResponse
    {
        public bool success { get; set; }

        public int shippingID { get; set; }

        public int orderID { get; set; }

        public string? shippingType { get; set; }

        public string? fullName { get; set; }

        public string? phone { get; set; }

        public string? email { get; set; }

        public string? addressDetail { get; set; }

        public int? provinceId { get; set; }

        public int? communeId { get; set; }

        public bool isStorePickup { get; set; }

        public string? storeAddress { get; set; }

        public int? deliveryStaffId { get; set; }
    }

    public sealed class PreviewGhnFeeRequest
    {
        public int ToDistrictId { get; set; }

        public string? ToWardCode { get; set; }

        public int? ServiceTypeId { get; set; }

        public int Height { get; set; } = 10;

        public int Length { get; set; } = 20;

        public int Width { get; set; } = 20;

        public int Weight { get; set; } = 500;

        public int InsuranceValue { get; set; }

        public string? ItemName { get; set; }

        public int ItemQuantity { get; set; } = 1;
    }

    public sealed class PreviewGhnLeadTimeRequest
    {
        public int ToDistrictId { get; set; }

        public string? ToWardCode { get; set; }

        public int? ServiceTypeId { get; set; }
    }

    public sealed class CreateGhnSandboxOrderRequest
    {
        public int OrderId { get; set; }

        public string? ToName { get; set; }

        public string? ToPhone { get; set; }

        public string? ToAddress { get; set; }

        public int ToDistrictId { get; set; }

        public string? ToWardCode { get; set; }

        public int? ServiceTypeId { get; set; }

        public string? ClientOrderCode { get; set; }

        public string? Content { get; set; }

        public string? Note { get; set; }

        public string? RequiredNote { get; set; }

        public int PaymentTypeId { get; set; } = 2;

        public int CodAmount { get; set; }

        public int InsuranceValue { get; set; }

        public int Height { get; set; } = 10;

        public int Length { get; set; } = 20;

        public int Width { get; set; } = 20;

        public int Weight { get; set; } = 500;

        public List<CreateGhnSandboxOrderItemRequest>? Items { get; set; }
    }

    public sealed class CreateGhnSandboxOrderItemRequest
    {
        public string? Name { get; set; }

        public string? Code { get; set; }

        public int Quantity { get; set; } = 1;

        public int Price { get; set; }

        public int Height { get; set; } = 10;

        public int Length { get; set; } = 20;

        public int Width { get; set; } = 20;

        public int Weight { get; set; } = 500;
    }

    public sealed class GetGhnSandboxOrderRequest
    {
        public string? OrderCode { get; set; }

        public string? ClientOrderCode { get; set; }
    }

    private sealed class GhnMetadataUpsertRequest
    {
        public string? OrderCode { get; set; }

        public string? ClientOrderCode { get; set; }

        public string? Status { get; set; }

        public string? StatusLabel { get; set; }

        public decimal? TotalFee { get; set; }

        public DateTime? CreatedAt { get; set; }

        public DateTime? ExpectedDeliveryTime { get; set; }

        public DateTime? LastSyncedAt { get; set; }
    }
}
