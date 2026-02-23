using FreshFarm.Web.Bff.Dtos; // DTO checkout.
using FreshFarm.Web.Bff.Services; // Service cart session.
using Microsoft.AspNetCore.Authentication; // SignOutAsync.
using Microsoft.AspNetCore.Authentication.Cookies; // CookieAuthenticationDefaults.
using Microsoft.AspNetCore.Authorization; // [Authorize].
using Microsoft.AspNetCore.Mvc; // Controller + IActionResult.
using System.Net.Http.Headers; // AuthenticationHeaderValue.

namespace FreshFarm.Web.Bff.Controllers; // Namespace controller.

[Authorize] // Checkout bắt buộc đăng nhập.
public sealed class CheckoutController : Controller // MVC controller cho checkout.
{
    private const string AccessTokenSessionKey = "ACCESS_TOKEN"; // Key token trong session.
    private const decimal DefaultShippingFee = 15000m; // Mức ship mặc định cho MVP.

    private readonly IHttpClientFactory _httpClientFactory; // Factory tạo HttpClient.
    private readonly ICartSessionService _cart; // Service thao tác cart session.

    public CheckoutController(IHttpClientFactory httpClientFactory, ICartSessionService cart) // Inject dependencies.
    {
        _httpClientFactory = httpClientFactory;
        _cart = cart;
    }

    [HttpGet("/checkout")] // Render checkout từ dữ liệu cart hiện tại.
    public async Task<IActionResult> Index()
    {
        var vm = BuildCheckoutModelFromCart(); // Tạo model checkout nhiều item từ cart.
        if (vm is null) // Cart rỗng.
        {
            TempData["CheckoutError"] = "Giỏ hàng đang trống, vui lòng chọn sản phẩm trước.";
            return RedirectToAction("Index", "Cart"); // Quay về cart.
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
        return View();
    }

    [HttpPost("/checkout")] // Submit đặt đơn.
    [ValidateAntiForgeryToken] // Chặn CSRF.
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

        if (string.IsNullOrWhiteSpace(request.Shipping.FullName)
            || string.IsNullOrWhiteSpace(request.Shipping.Phone)
            || string.IsNullOrWhiteSpace(request.Shipping.AddressDetail))
        {
            ModelState.AddModelError(string.Empty, "Vui lòng nhập đầy đủ thông tin giao hàng.");
            PopulateAddressSelectionViewData(savedAddresses, addressMode, selectedAddressId, addressLoadError); // Giữ state khi render lại view.
            return View(request);
        }

        var orderingClient = _httpClientFactory.CreateClient("Ordering");
        orderingClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);

        var response = await orderingClient.PostAsJsonAsync("/api/orders", request);
        if (!response.IsSuccessStatusCode)
        {
            var errorText = await response.Content.ReadAsStringAsync();
            ModelState.AddModelError(string.Empty, $"Tạo đơn thất bại: {errorText}");
            PopulateAddressSelectionViewData(savedAddresses, addressMode, selectedAddressId, addressLoadError); // Giữ state khi render lại view.
            return View(request);
        }

        var created = await response.Content.ReadFromJsonAsync<CreateOrderResultDto>();

        if (created is not null)
        {
            TempData["OrderId"] = created.OrderId;
            TempData["OrderCode"] = $"FF{created.OrderId:D6}";
            TempData["TotalAmount"] = created.TotalAmount.ToString(System.Globalization.CultureInfo.InvariantCulture); // TempData mặc định không serialize decimal.
        }

        TempData["OrderDate"] = DateTime.Now.ToString("O", System.Globalization.CultureInfo.InvariantCulture); // Lưu dạng chuỗi ISO để an toàn serialize TempData.
        TempData["PaymentMethod"] = request.Payment.PaymentMethod;
        TempData["RecipientName"] = request.Shipping.FullName;
        TempData["RecipientPhone"] = request.Shipping.Phone;
        TempData["RecipientAddress"] = request.Shipping.AddressDetail ?? "Chưa cập nhật";

        _cart.Clear(); // Đặt đơn thành công thì xóa cart.
        return RedirectToAction(nameof(Success));
    }

    private CheckoutSubmitRequestDto? BuildCheckoutModelFromCart() // Map cart session -> checkout model.
    {
        var cartItems = _cart.GetItems();
        if (cartItems.Count == 0)
        {
            return null;
        }

        return new CheckoutSubmitRequestDto
        {
            Items = cartItems.Select(x => new CheckoutItemInputDto
            {
                ProductId = x.ProductId,
                Quantity = x.Quantity <= 0 ? 1 : x.Quantity,
                UnitPrice = x.UnitPrice < 0m ? 0m : x.UnitPrice,
                UnitSymbol = string.IsNullOrWhiteSpace(x.UnitSymbol) ? "đơn vị" : x.UnitSymbol
            }).ToList(),
            ShippingFee = DefaultShippingFee,
            Shipping = new CheckoutShippingInputDto { ShippingType = "HomeDelivery" },
            Payment = new CheckoutPaymentInputDto { PaymentMethod = "COD" }
        };
    }

    private static CheckoutSubmitRequestDto NormalizeRequest(CheckoutSubmitRequestDto request) // Chuẩn hóa request trước khi call Ordering.
    {
        request ??= new CheckoutSubmitRequestDto();

        request.Items ??= new List<CheckoutItemInputDto>();
        request.Items = request.Items
            .Where(x => x.ProductId > 0)
            .Select(x => new CheckoutItemInputDto
            {
                ProductId = x.ProductId,
                Quantity = x.Quantity <= 0 ? 1 : x.Quantity,
                UnitPrice = x.UnitPrice < 0m ? 0m : x.UnitPrice,
                UnitSymbol = string.IsNullOrWhiteSpace(x.UnitSymbol) ? "đơn vị" : x.UnitSymbol
            })
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
    }

    private static void ApplySavedAddressToShipping(CheckoutSubmitRequestDto request, ProfileAddressItemDto selectedAddress) // Map address đã lưu -> Shipping.
    {
        request.Shipping ??= new CheckoutShippingInputDto(); // Bảo đảm object Shipping không null.
        request.Shipping.FullName = selectedAddress.RecipientName; // Đổ tên người nhận.
        request.Shipping.Phone = selectedAddress.Phone; // Đổ số điện thoại.
        request.Shipping.AddressDetail = BuildAddressDetail(selectedAddress); // Đổ địa chỉ đầy đủ.
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

    private static string NormalizeAddressMode(string? raw) // Chuẩn hóa mode địa chỉ.
    {
        return string.Equals(raw, "saved", StringComparison.OrdinalIgnoreCase) ? "saved" : "new";
    }

    private static int? ParseAddressId(string? raw) // Parse id địa chỉ an toàn.
    {
        return int.TryParse(raw, out var id) && id > 0 ? id : null;
    }

    private sealed class CreateOrderResultDto
    {
        public int OrderId { get; set; }
        public decimal TotalAmount { get; set; }
    }
}
