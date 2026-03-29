using FreshFarm.Web.Bff.Dtos; // DTO checkout.
using FreshFarm.Web.Bff.Services; // Service cart session.
using Microsoft.AspNetCore.Authentication; // SignOutAsync.
using Microsoft.AspNetCore.Authentication.Cookies; // CookieAuthenticationDefaults.
using Microsoft.AspNetCore.Authorization; // [Authorize].
using Microsoft.AspNetCore.Mvc; // Controller + IActionResult.
using Microsoft.AspNetCore.RateLimiting;
using System.Net.Http.Headers; // AuthenticationHeaderValue.
using System.Globalization;
using System.Text;
using System.Text.Json; // thêm ở đầu file
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using FreshFarm.Web.Bff.Models;
using FreshFarm.Web.Bff.Options;
namespace FreshFarm.Web.Bff.Controllers; // Namespace controller.

[Authorize] // Checkout bắt buộc đăng nhập.
public sealed class CheckoutController : Controller // MVC controller cho checkout.
{
    private const string CheckoutSelectedCartItemKeysSessionKey = "CHECKOUT_SELECTED_CART_ITEM_KEYS";
    private const string PendingVnPayCheckoutSessionKey = "PENDING_VNPAY_CHECKOUT";
    private const string AccessTokenSessionKey = "ACCESS_TOKEN"; // Key token trong session.
    private const decimal DefaultShippingFee = 15000m; // Mức ship mặc định cho MVP.
    private const int DefaultVnPayExpireAfterMinutes = 30;

    private readonly IHttpClientFactory _httpClientFactory; // Factory tạo HttpClient.
    private readonly ICartSessionService _cart; // Service thao tác cart session.
    private readonly IGhnSandboxService _ghnSandboxService; // Service doc danh muc dia chi GHN.
    private readonly IVnPayService _vnPayService;
    private readonly VnPayOptions _vnPayOptions;
    private readonly IConfiguration _configuration;
    private readonly ILogger<CheckoutController> _logger;

    public CheckoutController(
        IHttpClientFactory httpClientFactory,
        ICartSessionService cart,
        IGhnSandboxService ghnSandboxService,
        IVnPayService vnPayService,
        IOptions<VnPayOptions> vnPayOptions,
        IConfiguration configuration,
        ILogger<CheckoutController> logger) // Inject dependencies.
    {
        _httpClientFactory = httpClientFactory;
        _cart = cart;
        _ghnSandboxService = ghnSandboxService;
        _vnPayService = vnPayService;
        _vnPayOptions = vnPayOptions.Value;
        _configuration = configuration;
        _logger = logger;
    }

    [HttpGet("/checkout")] // Render checkout từ dữ liệu cart hiện tại.
    [EnableRateLimiting("checkout-read")]
    public async Task<IActionResult> Index()
    {
        await SeedDirectCheckoutItemFromQueryAsync();
        var selectedCartItemKeys = GetSelectedCartItemKeysFromSession();
        var vm = await BuildCheckoutModelFromCartAsync(selectedCartItemKeys);

        if (vm is null)
        {
            HttpContext.Session.Remove(CheckoutSelectedCartItemKeysSessionKey);
            TempData["CheckoutError"] = selectedCartItemKeys.Count > 0
                ? "Các sản phẩm đã chọn không còn trong giỏ hàng."
                : "Giỏ hàng đang trống, vui lòng chọn sản phẩm trước.";
            return RedirectToAction("Index", "Cart");
        }


        var token = HttpContext.Session.GetString(AccessTokenSessionKey); // Lấy JWT từ session.
        if (string.IsNullOrWhiteSpace(token)) // Session mất token.
        {
            HttpContext.Session.Remove(AccessTokenSessionKey); // Dọn session cho đồng bộ trạng thái.
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme); // Cookie còn nhưng token mất -> logout để tránh redirect loop.
            return RedirectToAction("SignIn", "Account", new { returnUrl = "/checkout" }); // Quay về login.
        }

        var (savedAddresses, addressLoadError) = await GetSavedAddressesAsync(token); // Tải sổ địa chỉ đã lưu.
        var selectedAddress = savedAddresses.FirstOrDefault(x => x.IsDefault) ?? savedAddresses.FirstOrDefault(); // Ưu tiên địa chỉ mặc định.
        var selectedAddressId = selectedAddress?.AddressId; // Id địa chỉ được chọn ban đầu.
        var addressMode = selectedAddressId.HasValue ? "saved" : "new"; // Có địa chỉ thì mặc định mode saved.

        if (selectedAddress is not null) // Prefill shipping từ địa chỉ mặc định.
        {
            ApplySavedAddressToShipping(vm, selectedAddress);
        }

        PopulateAddressSelectionViewData(savedAddresses, addressMode, selectedAddressId, addressLoadError); // Đẩy state sang view.
        return View(vm);
    }

    [HttpGet("/checkout/success")] // Trang success sau đặt đơn.
    [EnableRateLimiting("checkout-read")]
    public IActionResult Success()
    {
        ViewBag.OrderId = TempData["OrderId"];
        ViewBag.OrderCode = TempData["OrderCode"];
        ViewBag.OrderDate = TempData["OrderDate"];
        ViewBag.TotalAmount = TempData["TotalAmount"];
        ViewBag.PaymentMethod = TempData["PaymentMethod"];
        ViewBag.RecipientName = TempData["RecipientName"];
        ViewBag.RecipientPhone = TempData["RecipientPhone"];
        ViewBag.RecipientAddress = TempData["RecipientAddress"];
        ViewBag.AddressBookNotice = TempData["AddressBookNotice"];
        return View();
    }

    [AllowAnonymous]
    [HttpGet("/checkout/vnpay/return")]
    public async Task<IActionResult> VnPayReturn(CancellationToken cancellationToken)
    {
        var validation = _vnPayService.ValidateReturn(Request.Query);
        if (!validation.IsValid)
        {
            _logger.LogWarning(
                "VNPay return khong hop le tai BFF. Message={Message}, QueryString={QueryString}.",
                validation.Message,
                Request.QueryString.Value);
            return View("PaymentResult", BuildPaymentResultViewModel(null, false, validation.Message));
        }

        var token = HttpContext.Session.GetString(AccessTokenSessionKey);
        if (string.IsNullOrWhiteSpace(token))
        {
            _logger.LogWarning(
                "VNPay return hop le nhung session buyer da het han. OrderId={OrderId}, TxnRef={TxnRef}, TransactionNo={TransactionNo}.",
                validation.OrderId,
                validation.TxnRef,
                validation.TransactionNo);
            return View("PaymentResult", BuildPaymentResultViewModel(
                validation.OrderId,
                false,
                "Phiên đăng nhập đã hết hạn nên chưa thể chốt thanh toán VNPay cho đơn này. Vui lòng đăng nhập lại và kiểm tra đơn hàng."));
        }

        var orderingClient = CreateAuthorizedOrderingClient(token);
        var finalizeResponse = await orderingClient.PostAsJsonAsync(
            $"/api/orders/{validation.OrderId}/payments/vnpay/finalize",
            BuildFinalizeVnPayRequest(validation),
            cancellationToken);

        if (!finalizeResponse.IsSuccessStatusCode)
        {
            var errorText = await finalizeResponse.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning(
                "VNPay finalize that bai o Ordering. OrderId={OrderId}, TxnRef={TxnRef}, StatusCode={StatusCode}, Response={Response}.",
                validation.OrderId,
                validation.TxnRef,
                (int)finalizeResponse.StatusCode,
                errorText);
            return View("PaymentResult", BuildPaymentResultViewModel(
                validation.OrderId,
                false,
                string.IsNullOrWhiteSpace(errorText)
                    ? "VNPay đã quay về nhưng chưa cập nhật được trạng thái thanh toán trong hệ thống."
                    : $"VNPay đã quay về nhưng chưa cập nhật được trạng thái thanh toán: {errorText}"));
        }

        var finalizeResult = await finalizeResponse.Content.ReadFromJsonAsync<FinalizeVnPayPaymentResultDto>(cancellationToken: cancellationToken);
        _logger.LogInformation(
            "VNPay callback da duoc Ordering xu ly. OrderId={OrderId}, TxnRef={TxnRef}, Success={Success}, AlreadyProcessed={AlreadyProcessed}, PaymentStatus={PaymentStatus}, OrderStatus={OrderStatus}.",
            validation.OrderId,
            validation.TxnRef,
            finalizeResult?.Success ?? validation.IsSuccess,
            finalizeResult?.AlreadyProcessed ?? false,
            finalizeResult?.PaymentStatus,
            finalizeResult?.OrderStatus);
        if (validation.IsSuccess)
        {
            var pendingCheckout = GetPendingVnPayCheckoutFromSession();
            if (pendingCheckout is not null && pendingCheckout.OrderId == validation.OrderId)
            {
                var (savedAddresses, _) = await GetSavedAddressesAsync(token);
                if (string.Equals(pendingCheckout.AddressMode, "new", StringComparison.OrdinalIgnoreCase))
                {
                    var saveAddressResult = await SaveNewCheckoutAddressAsync(token, savedAddresses, pendingCheckout.Request);
                    if (!string.IsNullOrWhiteSpace(saveAddressResult))
                    {
                        TempData["AddressBookNotice"] = saveAddressResult;
                    }
                }

                await RemovePurchasedCartItemsAsync(pendingCheckout.PurchasedCartItemKeys);
                HttpContext.Session.Remove(CheckoutSelectedCartItemKeysSessionKey);
            }

            ClearPendingVnPayCheckout();
            await HydrateSuccessTempDataAsync(orderingClient, validation.OrderId, cancellationToken);
            TempData["PaymentMethod"] = "VNPay";
            return RedirectToAction(nameof(Success));
        }

        ClearPendingVnPayCheckout();
        return View("PaymentResult", BuildPaymentResultViewModel(
            validation.OrderId,
            false,
            finalizeResult?.Message ?? validation.Message));
    }

    [AllowAnonymous]
    [HttpGet("/checkout/vnpay/ipn")]
    public async Task<IActionResult> VnPayIpn(CancellationToken cancellationToken)
    {
        var validation = _vnPayService.ValidateReturn(Request.Query);
        if (!validation.IsValid)
        {
            _logger.LogWarning(
                "VNPay IPN khong hop le. Message={Message}, QueryString={QueryString}.",
                validation.Message,
                Request.QueryString.Value);
            return Json(new { RspCode = "97", Message = validation.Message });
        }

        var orderingClient = CreateInternalOrderingClient();
        var finalizeResponse = await orderingClient.PostAsJsonAsync(
            $"/api/orders/internal/{validation.OrderId}/payments/vnpay/finalize",
            BuildFinalizeVnPayRequest(validation),
            cancellationToken);

        var finalizeBody = await finalizeResponse.Content.ReadAsStringAsync(cancellationToken);
        if (!finalizeResponse.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "VNPay IPN finalize that bai. OrderId={OrderId}, TxnRef={TxnRef}, StatusCode={StatusCode}, Response={Response}.",
                validation.OrderId,
                validation.TxnRef,
                (int)finalizeResponse.StatusCode,
                finalizeBody);

            return Json(new
            {
                RspCode = "99",
                Message = "Khong the doi soat IPN VNPay voi Ordering."
            });
        }

        _logger.LogInformation(
            "VNPay IPN da duoc xu ly. OrderId={OrderId}, TxnRef={TxnRef}, TransactionNo={TransactionNo}.",
            validation.OrderId,
            validation.TxnRef,
            validation.TransactionNo);

        return Json(new
        {
            RspCode = "00",
            Message = "Confirm Success"
        });
    }

    [HttpGet("/checkout/ghn/provinces")]
    [EnableRateLimiting("ghn-read")]
    public async Task<JsonResult> GetGhnProvinces(CancellationToken cancellationToken)
    {
        var items = await _ghnSandboxService.GetProvincesAsync(cancellationToken);
        return Json(new
        {
            success = true,
            configured = _ghnSandboxService.IsConfigured,
            items = items.Select(x => new
            {
                id = x.Id,
                code = x.Code,
                name = x.Name
            })
        });
    }

    [HttpGet("/checkout/ghn/districts")]
    [EnableRateLimiting("ghn-read")]
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
            configured = _ghnSandboxService.IsConfigured,
            provinceId,
            items = items.Select(x => new
            {
                id = x.Id,
                code = x.Code,
                name = x.Name
            })
        });
    }

    [HttpGet("/checkout/ghn/wards")]
    [EnableRateLimiting("ghn-read")]
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
            configured = _ghnSandboxService.IsConfigured,
            districtId,
            items = items.Select(x => new
            {
                id = x.Id,
                code = x.Code,
                name = x.Name
            })
        });
    }

    [HttpPost("/checkout/ghn/preview-fee")]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("checkout-write")]
    public async Task<JsonResult> PreviewShippingFee(
        [FromForm] CheckoutShippingFeePreviewRequestDto request,
        CancellationToken cancellationToken)
    {
        var token = HttpContext.Session.GetString(AccessTokenSessionKey);
        if (string.IsNullOrWhiteSpace(token))
        {
            return Json(new CheckoutShippingFeePreviewResultDto
            {
                Success = false,
                Message = "Phiên đăng nhập đã hết hạn. Vui lòng đăng nhập lại để tiếp tục thanh toán."
            });
        }

        var (savedAddresses, _) = await GetSavedAddressesAsync(token);
        var normalizedRequest = NormalizeShippingFeePreviewRequest(request);
        var preview = await PreviewShippingFeeInternalAsync(
            normalizedRequest.AddressMode,
            normalizedRequest.SelectedAddressId,
            normalizedRequest.Items,
            normalizedRequest.Shipping,
            savedAddresses,
            cancellationToken);

        return Json(preview);
    }

    [HttpPost("/checkout")] // Submit đặt đơn.
    [ValidateAntiForgeryToken] // Chặn CSRF.
    [EnableRateLimiting("checkout-write")]
    public async Task<IActionResult> Index(CheckoutSubmitRequestDto request)
    {
        var token = HttpContext.Session.GetString(AccessTokenSessionKey); // Lấy JWT.
        if (string.IsNullOrWhiteSpace(token))
        {
            HttpContext.Session.Remove(AccessTokenSessionKey); // Dọn session cho đồng bộ trạng thái.
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme); // Tránh lặp redirect nếu cookie vẫn còn.
            return RedirectToAction("SignIn", "Account", new { returnUrl = "/checkout" });
        }

        var (savedAddresses, addressLoadError) = await GetSavedAddressesAsync(token); // Tải địa chỉ để xử lý mode saved/new.
        var addressMode = NormalizeAddressMode(Request.Form["addressMode"]); // Mode từ form.
        var selectedAddressId = ParseAddressId(Request.Form["selectedAddressId"]); // Id địa chỉ được chọn.

        if (savedAddresses.Count == 0) // Không có địa chỉ đã lưu thì chỉ cho nhập mới.
        {
            addressMode = "new";
            selectedAddressId = null;
        }

        if (addressMode == "saved") // User chọn dùng địa chỉ đã lưu.
        {
            var selectedAddress = selectedAddressId.HasValue
                ? savedAddresses.FirstOrDefault(x => x.AddressId == selectedAddressId.Value)
                : (savedAddresses.FirstOrDefault(x => x.IsDefault) ?? savedAddresses.FirstOrDefault()); // Fallback default.

            if (selectedAddress is null) // Không tìm thấy địa chỉ hợp lệ.
            {
                ModelState.AddModelError(string.Empty, "Không tìm thấy địa chỉ đã lưu. Vui lòng chọn lại hoặc nhập địa chỉ mới.");
            }
            else
            {
                selectedAddressId = selectedAddress.AddressId; // Đồng bộ id để render lại đúng lựa chọn.
                ApplySavedAddressToShipping(request, selectedAddress); // Đổ shipping từ địa chỉ đã chọn.
            }
        }

        request = NormalizeRequest(request); // Chuẩn hóa payload để tránh dữ liệu bẩn.
        var paymentMethod = NormalizePaymentMethod(request.Payment?.PaymentMethod);
        request.Payment ??= new CheckoutPaymentInputDto();
        request.Payment.PaymentMethod = paymentMethod;
        var selectedCartItemKeys = GetSelectedCartItemKeysFromSession();
        if (selectedCartItemKeys.Count > 0)
        {
            request.Items = request.Items
                .Where(x => !string.IsNullOrWhiteSpace(x.CartItemKey) && selectedCartItemKeys.Contains(x.CartItemKey.Trim()))
                .ToList();
        }

        if (!ModelState.IsValid) // Nếu đã có lỗi trước đó (ví dụ địa chỉ đã lưu không hợp lệ).
        {
            PopulateAddressSelectionViewData(savedAddresses, addressMode, selectedAddressId, addressLoadError); // Giữ state khi render lại view.
            return View(request);
        }

        if (request.Items.Count == 0)
        {
            ModelState.AddModelError(string.Empty, "Đơn hàng cần ít nhất 1 sản phẩm.");
            PopulateAddressSelectionViewData(savedAddresses, addressMode, selectedAddressId, addressLoadError); // Giữ state khi render lại view.
            return View(request);
        }

        if (string.Equals(paymentMethod, "VNPay", StringComparison.OrdinalIgnoreCase) && !_vnPayService.IsConfigured)
        {
            ModelState.AddModelError(string.Empty, "VNPay sandbox chưa được cấu hình cho môi trường dev. Vui lòng điền TmnCode và HashSecret trước khi test.");
            PopulateAddressSelectionViewData(savedAddresses, addressMode, selectedAddressId, addressLoadError);
            return View(request);
        }

        if (string.IsNullOrWhiteSpace(request.Shipping.FullName)
            || string.IsNullOrWhiteSpace(request.Shipping.Phone)
            || string.IsNullOrWhiteSpace(request.Shipping.AddressDetail))
        {
            ModelState.AddModelError(string.Empty, "Vui lòng nhập đầy đủ thông tin giao hàng.");
            PopulateAddressSelectionViewData(savedAddresses, addressMode, selectedAddressId, addressLoadError); // Giữ state khi render lại view.
            return View(request);
        }

        if (addressMode == "new")
        {
            ValidateAndComposeManualShippingAddress(request);
            ValidateAddressNotDuplicated(savedAddresses, request.Shipping);
            if (!ModelState.IsValid)
            {
                PopulateAddressSelectionViewData(savedAddresses, addressMode, selectedAddressId, addressLoadError);
                return View(request);
            }
        }

        if (_ghnSandboxService.IsConfigured)
        {
            var shippingPreview = await PreviewShippingFeeInternalAsync(
                addressMode,
                selectedAddressId,
                request.Items,
                request.Shipping,
                savedAddresses,
                HttpContext.RequestAborted);

            if (!shippingPreview.Success)
            {
                ModelState.AddModelError(string.Empty, shippingPreview.Message);
                PopulateAddressSelectionViewData(savedAddresses, addressMode, selectedAddressId, addressLoadError);
                return View(request);
            }

            request.ShippingFee = shippingPreview.ShippingFee;
            request.SellerShippingBreakdowns = shippingPreview.SellerBreakdowns
                .Select(x => new CheckoutSellerShippingInputDto
                {
                    SellerId = x.SellerId,
                    SellerName = x.SellerName,
                    ShippingFee = x.ShippingFee,
                    ServiceName = x.ServiceName,
                    ShippingOriginLabel = x.ShippingOriginLabel
                })
                .ToList();
        }

        var orderingClient = CreateAuthorizedOrderingClient(token);

        var response = await orderingClient.PostAsJsonAsync("/api/orders", request);
        if (!response.IsSuccessStatusCode)
        {
            var errorText = await response.Content.ReadAsStringAsync();
            ModelState.AddModelError(string.Empty, $"Tạo đơn thất bại: {errorText}");
            PopulateAddressSelectionViewData(savedAddresses, addressMode, selectedAddressId, addressLoadError); // Giữ state khi render lại view.
            return View(request);
        }

        var created = await response.Content.ReadFromJsonAsync<CreateOrderResultDto>();

        var purchasedKeys = request.Items
            .Select(x => x.CartItemKey?.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (created is null || created.OrderId <= 0)
        {
            ModelState.AddModelError(string.Empty, "Tạo đơn thành công nhưng không đọc được mã đơn để xử lý thanh toán.");
            PopulateAddressSelectionViewData(savedAddresses, addressMode, selectedAddressId, addressLoadError);
            return View(request);
        }

        if (string.Equals(paymentMethod, "VNPay", StringComparison.OrdinalIgnoreCase))
        {
            SavePendingVnPayCheckout(new PendingVnPayCheckoutSessionDto
            {
                OrderId = created.OrderId,
                AddressMode = addressMode,
                PurchasedCartItemKeys = purchasedKeys,
                Request = request
            });

            var paymentUrl = _vnPayService.CreatePaymentUrl(new VnPayCreatePaymentRequest(
                created.OrderId,
                created.TotalAmount,
                $"Thanh toan don hang FreshFarm #{created.OrderId}",
                created.OrderId.ToString(CultureInfo.InvariantCulture),
                ResolveClientIpAddress(),
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow.AddMinutes(GetVnPayExpireAfterMinutes())));

            return Redirect(paymentUrl);
        }

        TempData["OrderId"] = created.OrderId;
        TempData["OrderCode"] = $"FF{created.OrderId:D6}";
        TempData["TotalAmount"] = created.TotalAmount.ToString(System.Globalization.CultureInfo.InvariantCulture); // TempData mặc định không serialize decimal.
        TempData["OrderDate"] = DateTime.Now.ToString("O", System.Globalization.CultureInfo.InvariantCulture); // Lưu dạng chuỗi ISO để an toàn serialize TempData.
        TempData["PaymentMethod"] = paymentMethod;
        TempData["RecipientName"] = request.Shipping?.FullName ?? "Khách hàng";
        TempData["RecipientPhone"] = request.Shipping?.Phone ?? string.Empty;
        TempData["RecipientAddress"] = request.Shipping?.AddressDetail ?? "Chưa cập nhật";
        TempData["AddressBookNotice"] = string.Empty;

        if (addressMode == "new")
        {
            var saveAddressResult = await SaveNewCheckoutAddressAsync(token, savedAddresses, request);
            if (!string.IsNullOrWhiteSpace(saveAddressResult))
            {
                TempData["AddressBookNotice"] = saveAddressResult;
            }
        }

        await RemovePurchasedCartItemsAsync(purchasedKeys);
        HttpContext.Session.Remove(CheckoutSelectedCartItemKeysSessionKey);
        ClearPendingVnPayCheckout();
        return RedirectToAction(nameof(Success));

    }

    private async Task<CheckoutSubmitRequestDto?> BuildCheckoutModelFromCartAsync(IReadOnlyCollection<string>? selectedCartItemKeys = null)
    {
        var cartItems = await _cart.GetItemsAsync();

        if (selectedCartItemKeys is { Count: > 0 })
        {
            cartItems = cartItems
                .Where(x => selectedCartItemKeys.Contains(x.CartItemKey, StringComparer.OrdinalIgnoreCase))
                .ToList();
        }

        if (cartItems.Count == 0)
        {
            return null;
        }

        return new CheckoutSubmitRequestDto
        {
            Items = cartItems.Select(x => new CheckoutItemInputDto
            {
                ProductId = x.ProductId,
                SellerId = x.SellerId,
                SellerName = x.SellerName,
                ProductName = x.ProductName,
                CartItemKey = x.CartItemKey,
                Quantity = x.Quantity <= 0 ? 1 : x.Quantity,
                UnitPrice = x.UnitPrice < 0m ? 0m : x.UnitPrice,
                UnitSymbol = string.IsNullOrWhiteSpace(x.UnitSymbol) ? "đơn vị" : x.UnitSymbol
            }).ToList(),
            SellerShippingBreakdowns = new List<CheckoutSellerShippingInputDto>(),
            ShippingFee = DefaultShippingFee,
            Shipping = new CheckoutShippingInputDto { ShippingType = "HomeDelivery" },
            Payment = new CheckoutPaymentInputDto { PaymentMethod = "COD" }
        };
    }


    private static CheckoutSubmitRequestDto NormalizeRequest(CheckoutSubmitRequestDto request) // Chuẩn hóa request trước khi call Ordering.
    {
        request ??= new CheckoutSubmitRequestDto();

        request.Items ??= new List<CheckoutItemInputDto>();
        request.SellerShippingBreakdowns ??= new List<CheckoutSellerShippingInputDto>();
        request.Items = request.Items
            .Where(x => x.ProductId > 0)
            .Select(x => new CheckoutItemInputDto
            {
                ProductId = x.ProductId,
                SellerId = x.SellerId > 0 ? x.SellerId : 0,
                SellerName = string.IsNullOrWhiteSpace(x.SellerName) ? string.Empty : x.SellerName.Trim(),
                ProductName = string.IsNullOrWhiteSpace(x.ProductName) ? $"Sản phẩm #{x.ProductId}" : x.ProductName.Trim(),
                CartItemKey = string.IsNullOrWhiteSpace(x.CartItemKey)
                    ? CartItemDto.BuildCartItemKey(x.ProductId, x.SellerId)
                    : x.CartItemKey.Trim(),
                Quantity = x.Quantity <= 0 ? 1 : x.Quantity,
                UnitPrice = x.UnitPrice < 0m ? 0m : x.UnitPrice,
                UnitSymbol = string.IsNullOrWhiteSpace(x.UnitSymbol) ? "đơn vị" : x.UnitSymbol
            })
            .ToList();
        request.SellerShippingBreakdowns = request.SellerShippingBreakdowns
            .Where(x => x.SellerId > 0)
            .Select(x => new CheckoutSellerShippingInputDto
            {
                SellerId = x.SellerId,
                SellerName = string.IsNullOrWhiteSpace(x.SellerName) ? string.Empty : x.SellerName.Trim(),
                ShippingFee = x.ShippingFee < 0m ? 0m : x.ShippingFee,
                ServiceName = string.IsNullOrWhiteSpace(x.ServiceName) ? null : x.ServiceName.Trim(),
                ShippingOriginLabel = string.IsNullOrWhiteSpace(x.ShippingOriginLabel) ? null : x.ShippingOriginLabel.Trim()
            })
            .GroupBy(x => x.SellerId)
            .Select(g => g.OrderByDescending(x => x.ShippingFee).First())
            .ToList();

        request.ShippingFee = request.ShippingFee < 0m ? 0m : request.ShippingFee;

        request.Payment ??= new CheckoutPaymentInputDto();
        if (string.IsNullOrWhiteSpace(request.Payment.PaymentMethod))
        {
            request.Payment.PaymentMethod = "COD";
        }

        request.Shipping ??= new CheckoutShippingInputDto();
        if (string.IsNullOrWhiteSpace(request.Shipping.ShippingType))
        {
            request.Shipping.ShippingType = "HomeDelivery";
        }

        request.Shipping.FullName = request.Shipping.FullName?.Trim() ?? string.Empty;
        request.Shipping.Phone = request.Shipping.Phone?.Trim() ?? string.Empty;
        request.Shipping.Email = string.IsNullOrWhiteSpace(request.Shipping.Email) ? null : request.Shipping.Email.Trim();
        request.Shipping.AddressDetail = request.Shipping.AddressDetail?.Trim();
        request.Shipping.ProvinceName = string.IsNullOrWhiteSpace(request.Shipping.ProvinceName) ? null : request.Shipping.ProvinceName.Trim();
        request.Shipping.DistrictName = string.IsNullOrWhiteSpace(request.Shipping.DistrictName) ? null : request.Shipping.DistrictName.Trim();
        request.Shipping.WardName = string.IsNullOrWhiteSpace(request.Shipping.WardName) ? null : request.Shipping.WardName.Trim();
        request.Shipping.WardCode = string.IsNullOrWhiteSpace(request.Shipping.WardCode) ? null : request.Shipping.WardCode.Trim();

        return request;
    }

    private static CheckoutShippingFeePreviewRequestDto NormalizeShippingFeePreviewRequest(CheckoutShippingFeePreviewRequestDto request)
    {
        request ??= new CheckoutShippingFeePreviewRequestDto();
        request.AddressMode = NormalizeAddressMode(request.AddressMode);
        request.Items ??= new List<CheckoutItemInputDto>();
        request.Items = request.Items
            .Where(x => x.ProductId > 0)
            .Select(x => new CheckoutItemInputDto
            {
                ProductId = x.ProductId,
                SellerId = x.SellerId > 0 ? x.SellerId : 0,
                SellerName = string.IsNullOrWhiteSpace(x.SellerName) ? string.Empty : x.SellerName.Trim(),
                ProductName = string.IsNullOrWhiteSpace(x.ProductName) ? $"Sản phẩm #{x.ProductId}" : x.ProductName.Trim(),
                CartItemKey = string.IsNullOrWhiteSpace(x.CartItemKey)
                    ? CartItemDto.BuildCartItemKey(x.ProductId, x.SellerId)
                    : x.CartItemKey.Trim(),
                Quantity = x.Quantity <= 0 ? 1 : x.Quantity,
                UnitPrice = x.UnitPrice < 0m ? 0m : x.UnitPrice,
                UnitSymbol = string.IsNullOrWhiteSpace(x.UnitSymbol) ? "đơn vị" : x.UnitSymbol
            })
            .ToList();
        request.Shipping ??= new CheckoutShippingInputDto();
        request.Shipping.AddressDetail = request.Shipping.AddressDetail?.Trim();
        request.Shipping.ProvinceName = string.IsNullOrWhiteSpace(request.Shipping.ProvinceName) ? null : request.Shipping.ProvinceName.Trim();
        request.Shipping.DistrictName = string.IsNullOrWhiteSpace(request.Shipping.DistrictName) ? null : request.Shipping.DistrictName.Trim();
        request.Shipping.WardName = string.IsNullOrWhiteSpace(request.Shipping.WardName) ? null : request.Shipping.WardName.Trim();
        request.Shipping.WardCode = string.IsNullOrWhiteSpace(request.Shipping.WardCode) ? null : request.Shipping.WardCode.Trim();
        return request;
    }

    private async Task<(List<ProfileAddressItemDto> Addresses, string? Error)> GetSavedAddressesAsync(string token) // Lấy sổ địa chỉ từ Identity.
    {
        var identityClient = _httpClientFactory.CreateClient("Identity"); // Client gọi Identity API.
        identityClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token); // Gắn bearer token.

        var response = await identityClient.GetAsync("/auth/addresses"); // Gọi endpoint địa chỉ user.
        if (!response.IsSuccessStatusCode) // API lỗi thì vẫn cho checkout nhập tay.
        {
            var errorBody = await response.Content.ReadAsStringAsync(); // Đọc body lỗi.
            var message = string.IsNullOrWhiteSpace(errorBody)
                ? $"Không tải được sổ địa chỉ ({(int)response.StatusCode})."
                : $"Không tải được sổ địa chỉ: {errorBody}";
            return (new List<ProfileAddressItemDto>(), message);
        }

        var addresses = await response.Content.ReadFromJsonAsync<List<ProfileAddressItemDto>>() // Parse JSON -> list DTO.
                      ?? new List<ProfileAddressItemDto>(); // Fallback list rỗng.
        return (addresses, null);
    }

    private async Task<string?> SaveNewCheckoutAddressAsync(
        string token,
        IReadOnlyCollection<ProfileAddressItemDto> savedAddresses,
        CheckoutSubmitRequestDto request)
    {
        if (request.Shipping is null)
        {
            return null;
        }

        if (IsDuplicateAddress(savedAddresses, request.Shipping))
        {
            return "Địa chỉ giao hàng này đã có sẵn trong sổ địa chỉ, nên hệ thống không lưu thêm bản sao.";
        }

        var identityClient = _httpClientFactory.CreateClient("Identity");
        identityClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await identityClient.PostAsJsonAsync("/auth/addresses", new
        {
            recipientName = request.Shipping.FullName,
            phone = request.Shipping.Phone,
            addressDetail = request.Shipping.AddressDetail,
            province = request.Shipping.ProvinceName,
            district = request.Shipping.DistrictName,
            ward = request.Shipping.WardName,
            isDefault = request.SaveAddressAsDefault
        });

        if (response.IsSuccessStatusCode)
        {
            return request.SaveAddressAsDefault
                ? "Địa chỉ giao hàng mới đã được lưu và đặt làm mặc định trong sổ địa chỉ."
                : "Địa chỉ giao hàng mới đã được lưu vào sổ địa chỉ của bạn.";
        }

        var errorBody = await response.Content.ReadAsStringAsync();
        if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
        {
            return string.IsNullOrWhiteSpace(errorBody)
                ? "Địa chỉ giao hàng này đã có sẵn trong sổ địa chỉ, nên hệ thống không lưu thêm bản sao."
                : errorBody;
        }

        return string.IsNullOrWhiteSpace(errorBody)
            ? $"Đơn hàng đã tạo thành công nhưng chưa lưu được địa chỉ mới ({(int)response.StatusCode})."
            : $"Đơn hàng đã tạo thành công nhưng chưa lưu được địa chỉ mới: {errorBody}";
    }

    private HttpClient CreateAuthorizedOrderingClient(string token)
    {
        var orderingClient = _httpClientFactory.CreateClient("Ordering");
        orderingClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return orderingClient;
    }

    private HttpClient CreateInternalOrderingClient()
    {
        var orderingClient = _httpClientFactory.CreateClient("Ordering");
        var internalServiceKey = _configuration["Services:Ordering:InternalServiceKey"]?.Trim();
        if (string.IsNullOrWhiteSpace(internalServiceKey))
        {
            throw new InvalidOperationException("Services:Ordering:InternalServiceKey chưa được cấu hình cho IPN VNPay.");
        }

        orderingClient.DefaultRequestHeaders.Remove("X-Internal-Service-Key");
        orderingClient.DefaultRequestHeaders.Add("X-Internal-Service-Key", internalServiceKey);
        return orderingClient;
    }

    private static FinalizeVnPayPaymentRequestDto BuildFinalizeVnPayRequest(VnPayReturnValidationResult validation)
    {
        return new FinalizeVnPayPaymentRequestDto
        {
            TxnRef = validation.TxnRef,
            ResponseCode = validation.ResponseCode,
            TransactionStatus = validation.TransactionStatus,
            Amount = validation.Amount,
            TransactionNo = validation.TransactionNo,
            BankCode = validation.BankCode,
            BankTransactionNo = validation.BankTransactionNo,
            OrderInfo = validation.OrderInfo,
            PaidAt = validation.PaidAt,
            IsSuccess = validation.IsSuccess
        };
    }

    private string ResolveClientIpAddress()
    {
        var forwardedFor = Request.Headers["X-Forwarded-For"].ToString();
        if (!string.IsNullOrWhiteSpace(forwardedFor))
        {
            return forwardedFor.Split(',', StringSplitOptions.RemoveEmptyEntries)[0].Trim();
        }

        var remoteIp = HttpContext.Connection.RemoteIpAddress?.ToString();
        return string.IsNullOrWhiteSpace(remoteIp) ? "127.0.0.1" : remoteIp;
    }

    private void SavePendingVnPayCheckout(PendingVnPayCheckoutSessionDto pendingCheckout)
    {
        HttpContext.Session.SetString(
            PendingVnPayCheckoutSessionKey,
            JsonSerializer.Serialize(pendingCheckout));
    }

    private PendingVnPayCheckoutSessionDto? GetPendingVnPayCheckoutFromSession()
    {
        var raw = HttpContext.Session.GetString(PendingVnPayCheckoutSessionKey);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<PendingVnPayCheckoutSessionDto>(raw);
        }
        catch
        {
            HttpContext.Session.Remove(PendingVnPayCheckoutSessionKey);
            return null;
        }
    }

    private void ClearPendingVnPayCheckout()
    {
        HttpContext.Session.Remove(PendingVnPayCheckoutSessionKey);
    }

    private async Task HydrateSuccessTempDataAsync(HttpClient orderingClient, int orderId, CancellationToken cancellationToken)
    {
        using var response = await orderingClient.GetAsync($"/api/orders/{orderId}", cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            TempData["OrderId"] = orderId;
            TempData["OrderCode"] = $"FF{orderId:D6}";
            TempData["OrderDate"] = DateTime.Now.ToString("O", CultureInfo.InvariantCulture);
            TempData["AddressBookNotice"] ??= string.Empty;
            return;
        }

        var detail = await response.Content.ReadFromJsonAsync<OrderDetailResponseDto>(cancellationToken: cancellationToken);
        if (detail is null)
        {
            TempData["OrderId"] = orderId;
            TempData["OrderCode"] = $"FF{orderId:D6}";
            TempData["OrderDate"] = DateTime.Now.ToString("O", CultureInfo.InvariantCulture);
            TempData["AddressBookNotice"] ??= string.Empty;
            return;
        }

        var shipping = detail.Shippings.FirstOrDefault();
        TempData["OrderId"] = detail.OrderId;
        TempData["OrderCode"] = $"FF{detail.OrderId:D6}";
        TempData["OrderDate"] = detail.OrderDate.ToString("O", CultureInfo.InvariantCulture);
        TempData["TotalAmount"] = detail.TotalAmount.ToString(CultureInfo.InvariantCulture);
        TempData["PaymentMethod"] = detail.Payments.FirstOrDefault()?.PaymentMethod ?? "VNPay";
        TempData["RecipientName"] = shipping?.FullName ?? detail.BuyerFullName ?? "Khách hàng";
        TempData["RecipientPhone"] = shipping?.Phone ?? detail.BuyerPhone ?? string.Empty;
        TempData["RecipientAddress"] = shipping?.AddressDetail ?? "Chưa cập nhật";
        TempData["AddressBookNotice"] ??= string.Empty;
    }

    private static string NormalizePaymentMethod(string? paymentMethod)
    {
        if (string.IsNullOrWhiteSpace(paymentMethod))
        {
            return "COD";
        }

        return paymentMethod.Trim();
    }

    private static CheckoutPaymentResultViewModel BuildPaymentResultViewModel(int? orderId, bool isSuccess, string message)
    {
        return new CheckoutPaymentResultViewModel
        {
            OrderId = orderId,
            IsSuccess = isSuccess,
            Message = message
        };
    }

    private void PopulateAddressSelectionViewData(
        List<ProfileAddressItemDto> savedAddresses,
        string addressMode,
        int? selectedAddressId,
        string? addressLoadError) // Đẩy state chọn địa chỉ sang view.
    {
        ViewBag.SavedAddresses = savedAddresses; // Danh sách địa chỉ user.
        ViewBag.AddressMode = addressMode; // Mode saved/new.
        ViewBag.SelectedAddressId = selectedAddressId; // Id địa chỉ đang chọn.
        ViewBag.AddressLoadError = addressLoadError; // Lỗi tải địa chỉ (nếu có).
        ViewBag.GhnSandboxConfigured = _ghnSandboxService.IsConfigured; // Checkout co duoc phep load dia chi GHN hay khong.
        ViewBag.VnPayEnabled = _vnPayService.IsConfigured;
    }

    private static void ApplySavedAddressToShipping(CheckoutSubmitRequestDto request, ProfileAddressItemDto selectedAddress) // Map address đã lưu -> Shipping.
    {
        request.Shipping ??= new CheckoutShippingInputDto(); // Bảo đảm object Shipping không null.
        request.Shipping.FullName = selectedAddress.RecipientName; // Đổ tên người nhận.
        request.Shipping.Phone = selectedAddress.Phone; // Đổ số điện thoại.
        request.Shipping.AddressDetail = BuildAddressDetail(selectedAddress); // Đổ địa chỉ đầy đủ.
        request.Shipping.ProvinceName = selectedAddress.Province?.Trim();
        request.Shipping.DistrictName = selectedAddress.District?.Trim();
        request.Shipping.WardName = selectedAddress.Ward?.Trim();
    }

    private static string BuildAddressDetail(ProfileAddressItemDto address) // Ghép địa chỉ từ nhiều thành phần.
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(address.AddressDetail))
        {
            parts.Add(address.AddressDetail.Trim());
        }
        if (!string.IsNullOrWhiteSpace(address.Ward))
        {
            parts.Add(address.Ward.Trim());
        }
        if (!string.IsNullOrWhiteSpace(address.District))
        {
            parts.Add(address.District.Trim());
        }
        if (!string.IsNullOrWhiteSpace(address.Province))
        {
            parts.Add(address.Province.Trim());
        }

        return string.Join(", ", parts); // Trả về 1 dòng địa chỉ hoàn chỉnh.
    }

    private void ValidateAndComposeManualShippingAddress(CheckoutSubmitRequestDto request)
    {
        var shipping = request.Shipping ??= new CheckoutShippingInputDto();
        var streetAddress = shipping.AddressDetail?.Trim() ?? string.Empty;
        var provinceName = shipping.ProvinceName?.Trim() ?? string.Empty;
        var districtName = shipping.DistrictName?.Trim() ?? string.Empty;
        var wardName = shipping.WardName?.Trim() ?? string.Empty;
        var wardCode = shipping.WardCode?.Trim() ?? string.Empty;

        if (shipping.ProvinceId.GetValueOrDefault() <= 0)
        {
            ModelState.AddModelError("Shipping.ProvinceId", "Vui lòng chọn tỉnh/thành.");
        }

        if (shipping.DistrictId.GetValueOrDefault() <= 0)
        {
            ModelState.AddModelError("Shipping.DistrictId", "Vui lòng chọn quận/huyện.");
        }

        if (string.IsNullOrWhiteSpace(wardCode))
        {
            ModelState.AddModelError("Shipping.WardCode", "Vui lòng chọn phường/xã.");
        }

        if (string.IsNullOrWhiteSpace(provinceName) || string.IsNullOrWhiteSpace(districtName) || string.IsNullOrWhiteSpace(wardName))
        {
            ModelState.AddModelError(string.Empty, "Không đọc được đầy đủ thông tin địa chỉ GHN. Vui lòng chọn lại tỉnh, quận và phường.");
            return;
        }

        var fullAddress = string.Join(", ", new[] { streetAddress, wardName, districtName, provinceName }.Where(x => !string.IsNullOrWhiteSpace(x)));
        if (string.IsNullOrWhiteSpace(fullAddress))
        {
            ModelState.AddModelError("Shipping.AddressDetail", "Vui lòng nhập địa chỉ giao hàng chi tiết.");
            return;
        }

        if (fullAddress.Length > 255)
        {
            ModelState.AddModelError("Shipping.AddressDetail", "Địa chỉ giao hàng đầy đủ tối đa 255 ký tự.");
            return;
        }

        shipping.AddressDetail = fullAddress;
        if (shipping.CommuneId.GetValueOrDefault() <= 0 && int.TryParse(wardCode, out var wardId) && wardId > 0)
        {
            shipping.CommuneId = wardId;
        }
    }

    private void ValidateAddressNotDuplicated(IReadOnlyCollection<ProfileAddressItemDto> savedAddresses, CheckoutShippingInputDto shipping)
    {
        if (savedAddresses.Count == 0)
        {
            return;
        }

        if (!IsDuplicateAddress(savedAddresses, shipping))
        {
            return;
        }

        ModelState.AddModelError(string.Empty, "Địa chỉ này đã có trong sổ địa chỉ. Vui lòng chọn địa chỉ đã lưu hoặc nhập địa chỉ khác.");
    }

    private static bool IsDuplicateAddress(IReadOnlyCollection<ProfileAddressItemDto> savedAddresses, CheckoutShippingInputDto shipping)
    {
        var street = NormalizeAddressPart(ExtractStreetAddress(shipping.AddressDetail));
        var province = NormalizeAddressPart(shipping.ProvinceName);
        var district = NormalizeAddressPart(shipping.DistrictName);
        var ward = NormalizeAddressPart(shipping.WardName);

        return savedAddresses.Any(address =>
            NormalizeAddressPart(address.AddressDetail) == street &&
            NormalizeAddressPart(address.Province) == province &&
            NormalizeAddressPart(address.District) == district &&
            NormalizeAddressPart(address.Ward) == ward);
    }

    private static string NormalizeAddressPart(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return string.Join(" ", value.Trim().ToLowerInvariant().Split(new[] { ' ', ',', ';' }, StringSplitOptions.RemoveEmptyEntries));
    }

    private static string ExtractStreetAddress(string? fullAddress)
    {
        if (string.IsNullOrWhiteSpace(fullAddress))
        {
            return string.Empty;
        }

        var parts = fullAddress
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        return parts.Count > 0 ? parts[0] : fullAddress.Trim();
    }

    private static string NormalizeAddressMode(string? raw) // Chuẩn hóa mode địa chỉ.
    {
        return string.Equals(raw, "saved", StringComparison.OrdinalIgnoreCase) ? "saved" : "new";
    }

    private static int? ParseAddressId(string? raw) // Parse id địa chỉ an toàn.
    {
        return int.TryParse(raw, out var id) && id > 0 ? id : null;
    }

    private async Task<CheckoutShippingFeePreviewResultDto> PreviewShippingFeeInternalAsync(
        string addressMode,
        int? selectedAddressId,
        IReadOnlyCollection<CheckoutItemInputDto> items,
        CheckoutShippingInputDto? shipping,
        IReadOnlyCollection<ProfileAddressItemDto> savedAddresses,
        CancellationToken cancellationToken)
    {
        if (!_ghnSandboxService.IsConfigured)
        {
            return new CheckoutShippingFeePreviewResultDto
            {
                Success = true,
                ShippingFee = DefaultShippingFee,
                Message = "GHN chưa bật cấu hình preview, hệ thống đang dùng phí mặc định."
            };
        }

        var normalizedItems = items
            .Where(x => x.ProductId > 0)
            .Select(x => new CheckoutItemInputDto
            {
                ProductId = x.ProductId,
                SellerId = x.SellerId > 0 ? x.SellerId : 0,
                SellerName = string.IsNullOrWhiteSpace(x.SellerName) ? string.Empty : x.SellerName.Trim(),
                ProductName = string.IsNullOrWhiteSpace(x.ProductName) ? $"Sản phẩm #{x.ProductId}" : x.ProductName.Trim(),
                CartItemKey = string.IsNullOrWhiteSpace(x.CartItemKey)
                    ? CartItemDto.BuildCartItemKey(x.ProductId, x.SellerId)
                    : x.CartItemKey.Trim(),
                Quantity = x.Quantity <= 0 ? 1 : x.Quantity,
                UnitPrice = x.UnitPrice < 0m ? 0m : x.UnitPrice,
                UnitSymbol = string.IsNullOrWhiteSpace(x.UnitSymbol) ? "đơn vị" : x.UnitSymbol
            })
            .ToList();

        if (normalizedItems.Count == 0)
        {
            return new CheckoutShippingFeePreviewResultDto
            {
                Success = false,
                Message = "Không có sản phẩm để tính phí vận chuyển."
            };
        }

        var destination = await ResolveCheckoutDestinationAsync(addressMode, selectedAddressId, shipping, savedAddresses, cancellationToken);
        if (!destination.Success)
        {
            return new CheckoutShippingFeePreviewResultDto
            {
                Success = false,
                RequiresAddress = destination.RequiresAddress,
                Message = destination.Message
            };
        }

        var sellerOrigins = await GetSellerShippingOriginsAsync(
            normalizedItems.Select(x => x.SellerId).Where(x => x > 0).Distinct().ToList(),
            cancellationToken);

        var groupedItems = normalizedItems
            .GroupBy(x => x.SellerId > 0 ? x.SellerId : 0)
            .ToList();
        var breakdowns = new List<CheckoutSellerShippingFeeBreakdownDto>();
        decimal totalFee = 0m;

        foreach (var sellerGroup in groupedItems)
        {
            var sellerId = sellerGroup.Key;
            var groupItems = sellerGroup.ToList();
            var origin = sellerId > 0
                ? sellerOrigins.FirstOrDefault(x => x.SellerId == sellerId)
                : null;

            if (sellerId > 0 && (origin is null || !origin.HasShippingOrigin))
            {
                var fallbackSellerName = groupItems.Select(x => x.SellerName).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
                return new CheckoutShippingFeePreviewResultDto
                {
                    Success = false,
                    RequiresAddress = false,
                    Message = $"Shop {(string.IsNullOrWhiteSpace(fallbackSellerName) ? $"#{sellerId}" : fallbackSellerName)} chưa cấu hình địa chỉ lấy hàng GHN. Vui lòng liên hệ shop hoặc chọn sản phẩm khác."
                };
            }

            var package = await BuildShippingPackageAsync(groupItems, cancellationToken);
            var feeResult = await _ghnSandboxService.CalculateFeeAsync(new GhnSandboxFeeRequest
            {
                ToDistrictId = destination.DistrictId,
                ToWardCode = destination.WardCode,
                Height = package.Height,
                Length = package.Length,
                Width = package.Width,
                Weight = package.Weight,
                InsuranceValue = package.InsuranceValue,
                ItemName = package.ItemName,
                ItemQuantity = package.ItemQuantity,
                OriginOverride = origin is null || sellerId <= 0
                    ? null
                    : new GhnSandboxOriginOverride
                    {
                        FromDistrictId = origin.GhnDistrictId,
                        FromWardCode = origin.GhnWardCode ?? string.Empty
                    }
            }, cancellationToken);

            if (!feeResult.Success)
            {
                return new CheckoutShippingFeePreviewResultDto
                {
                    Success = false,
                    Message = string.IsNullOrWhiteSpace(feeResult.Message)
                        ? "Chưa tính được phí vận chuyển GHN. Vui lòng thử lại."
                        : feeResult.Message
                };
            }

            totalFee += feeResult.TotalFee;
            breakdowns.Add(new CheckoutSellerShippingFeeBreakdownDto
            {
                SellerId = sellerId,
                SellerName = ResolveSellerDisplayName(groupItems, origin),
                ItemCount = groupItems.Sum(x => Math.Max(1, x.Quantity)),
                ProductCount = groupItems.Count,
                ShippingFee = feeResult.TotalFee,
                ServiceName = feeResult.ServiceName,
                ShippingOriginLabel = origin?.PickupAddressSummary ?? "FreshFarm"
            });
        }

        return new CheckoutShippingFeePreviewResultDto
        {
            Success = true,
            ShippingFee = totalFee,
            ServiceName = breakdowns.Select(x => x.ServiceName).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? string.Empty,
            SellerBreakdowns = breakdowns,
            Message = breakdowns.Count <= 1
                ? $"Đã cập nhật phí vận chuyển GHN{(string.IsNullOrWhiteSpace(breakdowns.FirstOrDefault()?.ServiceName) ? string.Empty : $" - {breakdowns.First().ServiceName}")}."
                : $"Đã cập nhật phí vận chuyển GHN cho {breakdowns.Count} shop."
        };
    }

    private async Task<CheckoutDestinationResolutionResult> ResolveCheckoutDestinationAsync(
        string addressMode,
        int? selectedAddressId,
        CheckoutShippingInputDto? shipping,
        IReadOnlyCollection<ProfileAddressItemDto> savedAddresses,
        CancellationToken cancellationToken)
    {
        if (string.Equals(addressMode, "saved", StringComparison.OrdinalIgnoreCase))
        {
            var selectedAddress = selectedAddressId.HasValue
                ? savedAddresses.FirstOrDefault(x => x.AddressId == selectedAddressId.Value)
                : (savedAddresses.FirstOrDefault(x => x.IsDefault) ?? savedAddresses.FirstOrDefault());

            if (selectedAddress is null)
            {
                return CheckoutDestinationResolutionResult.Fail("Không tìm thấy địa chỉ đã lưu để tính phí vận chuyển.");
            }

            return await ResolveDestinationByNamesAsync(
                selectedAddress.Province,
                selectedAddress.District,
                selectedAddress.Ward,
                cancellationToken);
        }

        if (shipping is null)
        {
            return CheckoutDestinationResolutionResult.Fail("Vui lòng nhập địa chỉ giao hàng để tính phí vận chuyển.", requiresAddress: true);
        }

        if (shipping.DistrictId.GetValueOrDefault() > 0 && !string.IsNullOrWhiteSpace(shipping.WardCode))
        {
            return CheckoutDestinationResolutionResult.Ok(
                shipping.DistrictId!.Value,
                shipping.WardCode!,
                shipping.ProvinceName,
                shipping.DistrictName,
                shipping.WardName);
        }

        if (shipping.ProvinceId.GetValueOrDefault() <= 0
            && string.IsNullOrWhiteSpace(shipping.ProvinceName)
            && string.IsNullOrWhiteSpace(shipping.DistrictName)
            && string.IsNullOrWhiteSpace(shipping.WardName))
        {
            return CheckoutDestinationResolutionResult.Fail("Vui lòng chọn tỉnh, quận và phường để tính phí vận chuyển.", requiresAddress: true);
        }

        return await ResolveDestinationByNamesAsync(
            shipping.ProvinceName,
            shipping.DistrictName,
            shipping.WardName,
            cancellationToken);
    }

    private async Task<CheckoutDestinationResolutionResult> ResolveDestinationByNamesAsync(
        string? provinceName,
        string? districtName,
        string? wardName,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(provinceName)
            || string.IsNullOrWhiteSpace(districtName)
            || string.IsNullOrWhiteSpace(wardName))
        {
            return CheckoutDestinationResolutionResult.Fail("Vui lòng chọn đầy đủ tỉnh, quận và phường để tính phí vận chuyển.", requiresAddress: true);
        }

        var provinces = await _ghnSandboxService.GetProvincesAsync(cancellationToken);
        var province = FindLocationByName(provinces, provinceName);
        if (province is null)
        {
            return CheckoutDestinationResolutionResult.Fail("Không khớp được tỉnh/thành với dữ liệu GHN. Vui lòng chọn lại địa chỉ.", requiresAddress: true);
        }

        var districts = await _ghnSandboxService.GetDistrictsAsync(province.Id, cancellationToken);
        var district = FindLocationByName(districts, districtName);
        if (district is null)
        {
            return CheckoutDestinationResolutionResult.Fail("Không khớp được quận/huyện với dữ liệu GHN. Vui lòng chọn lại địa chỉ.", requiresAddress: true);
        }

        var wards = await _ghnSandboxService.GetWardsAsync(district.Id, cancellationToken);
        var ward = FindLocationByName(wards, wardName);
        if (ward is null || string.IsNullOrWhiteSpace(ward.Code))
        {
            return CheckoutDestinationResolutionResult.Fail("Không khớp được phường/xã với dữ liệu GHN. Vui lòng chọn lại địa chỉ.", requiresAddress: true);
        }

        return CheckoutDestinationResolutionResult.Ok(district.Id, ward.Code, province.Name, district.Name, ward.Name);
    }

    private static GhnSandboxLocationItem? FindLocationByName(IReadOnlyList<GhnSandboxLocationItem> items, string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var normalizedTarget = NormalizeLocationName(name);
        return items.FirstOrDefault(item => NormalizeLocationName(item.Name) == normalizedTarget)
            ?? items.FirstOrDefault(item => NormalizeLocationName(item.Name).Contains(normalizedTarget, StringComparison.Ordinal))
            ?? items.FirstOrDefault(item => normalizedTarget.Contains(NormalizeLocationName(item.Name), StringComparison.Ordinal));
    }

    private async Task<CheckoutShippingPackageSnapshot> BuildShippingPackageAsync(
        IReadOnlyCollection<CheckoutItemInputDto> items,
        CancellationToken cancellationToken)
    {
        var catalogClient = _httpClientFactory.CreateClient("Catalog");
        var productTasks = items
            .Where(x => x.ProductId > 0)
            .Select(x => GetProductSnapshotAsync(catalogClient, x.ProductId, cancellationToken))
            .ToList();

        var productSnapshots = productTasks.Count > 0
            ? await Task.WhenAll(productTasks)
            : Array.Empty<CheckoutCatalogProductSnapshot?>();

        var productById = productSnapshots
            .Where(x => x is not null)
            .Cast<CheckoutCatalogProductSnapshot>()
            .GroupBy(x => x.ProductId)
            .ToDictionary(x => x.Key, x => x.First());

        var totalQuantity = 0;
        var totalWeight = 0;
        var itemNames = new List<string>();
        decimal subtotal = 0m;

        foreach (var item in items)
        {
            var quantity = item.Quantity <= 0 ? 1 : item.Quantity;
            totalQuantity += quantity;
            subtotal += item.UnitPrice * quantity;

            if (productById.TryGetValue(item.ProductId, out var product))
            {
                if (!string.IsNullOrWhiteSpace(product.ProductName))
                {
                    itemNames.Add(product.ProductName.Trim());
                }

                var weightPerUnit = ParseWeightToGrams(product.Weight);
                totalWeight += (weightPerUnit ?? 500) * quantity;
            }
            else
            {
                itemNames.Add($"Sản phẩm #{item.ProductId}");
                totalWeight += 500 * quantity;
            }
        }

        totalQuantity = Math.Max(1, totalQuantity);
        totalWeight = Math.Clamp(totalWeight, 200, 30000);
        var distinctNames = itemNames
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(2)
            .ToList();

        var itemName = distinctNames.Count switch
        {
            0 => "Nông sản FreshFarm",
            1 => distinctNames[0],
            _ => $"{distinctNames[0]} và {Math.Max(0, itemNames.Count - 1)} sản phẩm khác"
        };

        return new CheckoutShippingPackageSnapshot
        {
            Weight = totalWeight,
            Length = Math.Clamp(20 + ((totalQuantity - 1) * 2), 20, 60),
            Width = Math.Clamp(18 + Math.Min(items.Count, 5) * 2, 18, 35),
            Height = Math.Clamp(10 + totalQuantity, 10, 40),
            InsuranceValue = decimal.ToInt32(Math.Clamp(subtotal, 0m, 5000000m)),
            ItemName = itemName,
            ItemQuantity = totalQuantity
        };
    }

    private async Task<CheckoutCatalogProductSnapshot?> GetProductSnapshotAsync(
        HttpClient catalogClient,
        int productId,
        CancellationToken cancellationToken)
    {
        using var response = await catalogClient.GetAsync($"/api/products/{productId}", cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var product = await response.Content.ReadFromJsonAsync<CheckoutCatalogProductSnapshot>(cancellationToken: cancellationToken);
        return product;
    }

    private static int? ParseWeightToGrams(string? rawWeight)
    {
        if (string.IsNullOrWhiteSpace(rawWeight))
        {
            return null;
        }

        var normalized = rawWeight.Trim().ToLowerInvariant().Replace(',', '.');
        var numericBuilder = new StringBuilder();
        foreach (var ch in normalized)
        {
            if (char.IsDigit(ch) || ch == '.')
            {
                numericBuilder.Append(ch);
            }
        }

        if (!decimal.TryParse(numericBuilder.ToString(), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value) || value <= 0)
        {
            return null;
        }

        if (normalized.Contains("kg", StringComparison.Ordinal))
        {
            return decimal.ToInt32(Math.Round(value * 1000m, MidpointRounding.AwayFromZero));
        }

        if (normalized.Contains("gram", StringComparison.Ordinal) || normalized.Contains("g", StringComparison.Ordinal))
        {
            return decimal.ToInt32(Math.Round(value, MidpointRounding.AwayFromZero));
        }

        return null;
    }

    private static string NormalizeLocationName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        foreach (var ch in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(ch))
            {
                builder.Append(ch);
                continue;
            }

            if (char.IsWhiteSpace(ch))
            {
                builder.Append(' ');
            }
        }

        return string.Join(" ", builder.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private sealed class CreateOrderResultDto
    {
        public int OrderId { get; set; }
        public decimal TotalAmount { get; set; }
    }

    private sealed class FinalizeVnPayPaymentRequestDto
    {
        public string TxnRef { get; set; } = string.Empty;
        public string ResponseCode { get; set; } = string.Empty;
        public string? TransactionStatus { get; set; }
        public decimal? Amount { get; set; }
        public string? TransactionNo { get; set; }
        public string? BankCode { get; set; }
        public string? BankTransactionNo { get; set; }
        public string? OrderInfo { get; set; }
        public DateTimeOffset? PaidAt { get; set; }
        public bool IsSuccess { get; set; }
    }

    private sealed class FinalizeVnPayPaymentResultDto
    {
        public bool Success { get; set; }
        public bool AlreadyProcessed { get; set; }
        public int OrderId { get; set; }
        public string PaymentStatus { get; set; } = string.Empty;
        public string OrderStatus { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
    }

    private sealed class OrderDetailResponseDto
    {
        public int OrderId { get; set; }
        public DateTime OrderDate { get; set; }
        public decimal TotalAmount { get; set; }
        public string? BuyerFullName { get; set; }
        public string? BuyerPhone { get; set; }
        public List<OrderDetailShippingResponseDto> Shippings { get; set; } = new();
        public List<OrderDetailPaymentResponseDto> Payments { get; set; } = new();
    }

    private sealed class OrderDetailShippingResponseDto
    {
        public string? FullName { get; set; }
        public string? Phone { get; set; }
        public string? AddressDetail { get; set; }
    }

    private sealed class OrderDetailPaymentResponseDto
    {
        public string? PaymentMethod { get; set; }
    }

    private sealed class PendingVnPayCheckoutSessionDto
    {
        public int OrderId { get; set; }
        public string AddressMode { get; set; } = "new";
        public List<string> PurchasedCartItemKeys { get; set; } = new();
        public CheckoutSubmitRequestDto Request { get; set; } = new();
    }

    private sealed class CheckoutCatalogProductSnapshot
    {
        public int ProductId { get; set; }
        public string? ProductName { get; set; }
        public List<CheckoutCatalogProductInfoSnapshot>? ProductInfos { get; set; }

        public string? Weight => ProductInfos?
            .Select(x => x.Weight)
            .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
    }

    private sealed class CheckoutCatalogProductInfoSnapshot
    {
        public string? Weight { get; set; }
    }

    private sealed class CheckoutShippingPackageSnapshot
    {
        public int Weight { get; set; }
        public int Length { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public int InsuranceValue { get; set; }
        public string ItemName { get; set; } = "Nông sản FreshFarm";
        public int ItemQuantity { get; set; }
    }

    private sealed class CheckoutDestinationResolutionResult
    {
        public bool Success { get; private set; }
        public bool RequiresAddress { get; private set; }
        public int DistrictId { get; private set; }
        public string WardCode { get; private set; } = string.Empty;
        public string ProvinceName { get; private set; } = string.Empty;
        public string DistrictName { get; private set; } = string.Empty;
        public string WardName { get; private set; } = string.Empty;
        public string Message { get; private set; } = string.Empty;

        public static CheckoutDestinationResolutionResult Ok(
            int districtId,
            string wardCode,
            string? provinceName,
            string? districtName,
            string? wardName)
        {
            return new CheckoutDestinationResolutionResult
            {
                Success = true,
                DistrictId = districtId,
                WardCode = wardCode?.Trim() ?? string.Empty,
                ProvinceName = provinceName?.Trim() ?? string.Empty,
                DistrictName = districtName?.Trim() ?? string.Empty,
                WardName = wardName?.Trim() ?? string.Empty
            };
        }

        public static CheckoutDestinationResolutionResult Fail(string message, bool requiresAddress = false)
        {
            return new CheckoutDestinationResolutionResult
            {
                Success = false,
                RequiresAddress = requiresAddress,
                Message = message
            };
        }
    }
    private IReadOnlyCollection<string> GetSelectedCartItemKeysFromSession()
    {
        var raw = HttpContext.Session.GetString(CheckoutSelectedCartItemKeysSessionKey);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return Array.Empty<string>();
        }

        try
        {
            var keys = JsonSerializer.Deserialize<List<string>>(raw) ?? new List<string>();
            return keys
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch
        {
            HttpContext.Session.Remove(CheckoutSelectedCartItemKeysSessionKey);
            return Array.Empty<string>();
        }
    }

    private async Task SeedDirectCheckoutItemFromQueryAsync()
    {
        var directItem = ParseDirectCheckoutItemFromQuery();
        if (directItem is null)
        {
            return;
        }

        var items = await _cart.GetItemsAsync();
        var existing = items.FirstOrDefault(x =>
            x.ProductId == directItem.ProductId &&
            x.SellerId == directItem.SellerId);
        if (existing is null)
        {
            items.Add(directItem);
        }
        else
        {
            existing.SellerId = directItem.SellerId;
            existing.SellerName = string.IsNullOrWhiteSpace(directItem.SellerName) ? existing.SellerName : directItem.SellerName;
            existing.ProductName = string.IsNullOrWhiteSpace(directItem.ProductName) ? existing.ProductName : directItem.ProductName;
            existing.UnitPrice = directItem.UnitPrice;
            existing.UnitSymbol = string.IsNullOrWhiteSpace(directItem.UnitSymbol) ? existing.UnitSymbol : directItem.UnitSymbol;
            existing.Quantity = directItem.Quantity;
        }

        await _cart.SetItemsAsync(items);
        HttpContext.Session.SetString(
            CheckoutSelectedCartItemKeysSessionKey,
            JsonSerializer.Serialize(new[] { directItem.CartItemKey }));
    }

    private CartItemDto? ParseDirectCheckoutItemFromQuery()
    {
        if (!Request.Query.TryGetValue("productId", out var productIdValues)
            || !int.TryParse(productIdValues.ToString(), out var productId)
            || productId <= 0)
        {
            return null;
        }

        var productName = Request.Query["productName"].ToString().Trim();
        var unitSymbol = Request.Query["unitSymbol"].ToString().Trim();
        var sellerName = Request.Query["sellerName"].ToString().Trim();
        var quantityRaw = Request.Query["quantity"].ToString();
        var unitPriceRaw = Request.Query["unitPrice"].ToString();
        var sellerIdRaw = Request.Query["sellerId"].ToString();

        var quantity = int.TryParse(quantityRaw, out var parsedQuantity) && parsedQuantity > 0
            ? parsedQuantity
            : 1;

        var unitPrice = decimal.TryParse(unitPriceRaw, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsedPrice)
            ? Math.Max(0m, parsedPrice)
            : 0m;
        var sellerId = int.TryParse(sellerIdRaw, out var parsedSellerId) && parsedSellerId > 0
            ? parsedSellerId
            : 0;

        return new CartItemDto
        {
            ProductId = productId,
            SellerId = sellerId,
            SellerName = string.IsNullOrWhiteSpace(sellerName) ? string.Empty : sellerName,
            ProductName = string.IsNullOrWhiteSpace(productName) ? $"Sản phẩm #{productId}" : productName,
            UnitPrice = unitPrice,
            UnitSymbol = string.IsNullOrWhiteSpace(unitSymbol) ? "đơn vị" : unitSymbol,
            Quantity = quantity
        };
    }

    private async Task RemovePurchasedCartItemsAsync(IReadOnlyCollection<string> purchasedKeys)
    {
        if (purchasedKeys.Count == 0)
        {
            return;
        }

        var remainingItems = (await _cart.GetItemsAsync())
            .Where(x => !purchasedKeys.Contains(x.CartItemKey, StringComparer.OrdinalIgnoreCase))
            .ToList();

        if (remainingItems.Count == 0)
        {
            await _cart.ClearAsync();
            return;
        }

        await _cart.SetItemsAsync(remainingItems);
    }

    private int GetVnPayExpireAfterMinutes()
    {
        var configuredMinutes = _vnPayOptions.ExpireAfterMinutes;
        return configuredMinutes > 0 ? configuredMinutes : DefaultVnPayExpireAfterMinutes;
    }

    private async Task<List<SellerShippingOriginSnapshot>> GetSellerShippingOriginsAsync(
        IReadOnlyCollection<int> sellerIds,
        CancellationToken cancellationToken)
    {
        var normalizedIds = sellerIds
            .Where(x => x > 0)
            .Distinct()
            .ToList();

        if (normalizedIds.Count == 0)
        {
            return new List<SellerShippingOriginSnapshot>();
        }

        var identityClient = _httpClientFactory.CreateClient("Identity");
        var query = string.Join("&", normalizedIds.Select(id => $"sellerIds={id}"));
        using var response = await identityClient.GetAsync($"/auth/public/merchants/shipping-origins?{query}", cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return new List<SellerShippingOriginSnapshot>();
        }

        var payload = await response.Content.ReadFromJsonAsync<PublicMerchantShippingOriginListDto>(cancellationToken: cancellationToken);
        return payload?.Origins?
            .Where(x => x is not null && x.SellerId > 0)
            .Select(x => new SellerShippingOriginSnapshot
            {
                SellerId = x.SellerId,
                ShopName = x.ShopName?.Trim() ?? string.Empty,
                HasShippingOrigin = x.HasShippingOrigin,
                GhnDistrictId = x.GhnDistrictId,
                GhnWardCode = x.GhnWardCode?.Trim(),
                PickupAddressSummary = x.PickupAddressSummary?.Trim() ?? string.Empty
            })
            .GroupBy(x => x.SellerId)
            .Select(group => SelectPreferredSellerShippingOrigin(group))
            .ToList()
            ?? new List<SellerShippingOriginSnapshot>();
    }

    private static SellerShippingOriginSnapshot SelectPreferredSellerShippingOrigin(IEnumerable<SellerShippingOriginSnapshot> origins)
    {
        return origins
            .OrderByDescending(CalculateSellerShippingOriginScore)
            .ThenByDescending(origin => origin.HasShippingOrigin)
            .ThenByDescending(origin => origin.GhnDistrictId.HasValue)
            .ThenByDescending(origin => !string.IsNullOrWhiteSpace(origin.GhnWardCode))
            .ThenByDescending(origin => !string.IsNullOrWhiteSpace(origin.PickupAddressSummary))
            .ThenByDescending(origin => !string.IsNullOrWhiteSpace(origin.ShopName))
            .First();
    }

    private static int CalculateSellerShippingOriginScore(SellerShippingOriginSnapshot origin)
    {
        var score = 0;
        if (origin.HasShippingOrigin)
        {
            score += 5;
        }

        if (origin.GhnDistrictId.HasValue)
        {
            score += 3;
        }

        if (!string.IsNullOrWhiteSpace(origin.GhnWardCode))
        {
            score += 3;
        }

        if (!string.IsNullOrWhiteSpace(origin.PickupAddressSummary))
        {
            score += 2;
        }

        if (!string.IsNullOrWhiteSpace(origin.ShopName))
        {
            score += 1;
        }

        return score;
    }

    private static string ResolveSellerDisplayName(
        IReadOnlyCollection<CheckoutItemInputDto> items,
        SellerShippingOriginSnapshot? origin)
    {
        var explicitName = items
            .Select(x => x.SellerName?.Trim())
            .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));

        if (!string.IsNullOrWhiteSpace(explicitName))
        {
            return explicitName;
        }

        if (!string.IsNullOrWhiteSpace(origin?.ShopName))
        {
            return origin.ShopName;
        }

        var sellerId = items.Select(x => x.SellerId).FirstOrDefault(x => x > 0);
        return sellerId > 0 ? $"FreshFarm Seller {sellerId}" : "FreshFarm";
    }

    private sealed class PublicMerchantShippingOriginListDto
    {
        public List<PublicMerchantShippingOriginDto> Origins { get; set; } = new();
    }

    private sealed class PublicMerchantShippingOriginDto
    {
        public int SellerId { get; set; }
        public string? ShopName { get; set; }
        public bool HasShippingOrigin { get; set; }
        public int? GhnDistrictId { get; set; }
        public string? GhnWardCode { get; set; }
        public string? PickupAddressSummary { get; set; }
    }

    private sealed class SellerShippingOriginSnapshot
    {
        public int SellerId { get; set; }
        public string ShopName { get; set; } = string.Empty;
        public bool HasShippingOrigin { get; set; }
        public int? GhnDistrictId { get; set; }
        public string? GhnWardCode { get; set; }
        public string PickupAddressSummary { get; set; } = string.Empty;
    }

}
