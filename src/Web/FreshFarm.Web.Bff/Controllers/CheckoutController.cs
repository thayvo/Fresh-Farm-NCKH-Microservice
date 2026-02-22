using FreshFarm.Web.Bff.Dtos; // DTO checkout.
using FreshFarm.Web.Bff.Services; // Service cart session.
using Microsoft.AspNetCore.Authorization; // [Authorize].
using Microsoft.AspNetCore.Mvc; // Controller + IActionResult.

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
    public IActionResult Index()
    {
        var vm = BuildCheckoutModelFromCart(); // Tạo model checkout nhiều item từ cart.
        if (vm is null) // Cart rỗng.
        {
            TempData["CheckoutError"] = "Giỏ hàng đang trống, vui lòng chọn sản phẩm trước.";
            return RedirectToAction("Index", "Cart"); // Quay về cart.
        }

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
            return RedirectToAction("SignIn", "Account");
        }

        request = NormalizeRequest(request); // Chuẩn hóa payload để tránh dữ liệu bẩn.

        if (request.Items.Count == 0)
        {
            ModelState.AddModelError(string.Empty, "Đơn hàng cần ít nhất 1 sản phẩm.");
            return View(request);
        }

        if (string.IsNullOrWhiteSpace(request.Shipping.FullName)
            || string.IsNullOrWhiteSpace(request.Shipping.Phone)
            || string.IsNullOrWhiteSpace(request.Shipping.AddressDetail))
        {
            ModelState.AddModelError(string.Empty, "Vui lòng nhập đầy đủ thông tin giao hàng.");
            return View(request);
        }

        var orderingClient = _httpClientFactory.CreateClient("Ordering");
        orderingClient.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var response = await orderingClient.PostAsJsonAsync("/api/orders", request);
        if (!response.IsSuccessStatusCode)
        {
            var errorText = await response.Content.ReadAsStringAsync();
            ModelState.AddModelError(string.Empty, $"Tạo đơn thất bại: {errorText}");
            return View(request);
        }

        var created = await response.Content.ReadFromJsonAsync<CreateOrderResultDto>();

        if (created is not null)
        {
            TempData["OrderId"] = created.OrderId;
            TempData["OrderCode"] = $"FF{created.OrderId:D6}";
            TempData["TotalAmount"] = created.TotalAmount;
        }

        TempData["OrderDate"] = DateTime.Now;
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

    private sealed class CreateOrderResultDto
    {
        public int OrderId { get; set; }
        public decimal TotalAmount { get; set; }
    }
}
