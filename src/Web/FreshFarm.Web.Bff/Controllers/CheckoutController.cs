using FreshFarm.Web.Bff.Dtos; // DTO checkout.
using Microsoft.AspNetCore.Authorization; // [Authorize].
using Microsoft.AspNetCore.Mvc; // Controller + IActionResult.

namespace FreshFarm.Web.Bff.Controllers; // Namespace controller.

[Authorize] // Checkout bat buoc dang nhap.
public sealed class CheckoutController : Controller // MVC controller cho trang checkout.
{
    private const string AccessTokenSessionKey = "ACCESS_TOKEN"; // Key token trong session.
    private readonly IHttpClientFactory _httpClientFactory; // HttpClient factory.

    public CheckoutController(IHttpClientFactory httpClientFactory) // Inject qua DI.
    {
        _httpClientFactory = httpClientFactory; // Gan vao field.
    }

    [HttpGet("/checkout")] // GET trang checkout.
    public IActionResult Index() // Render form checkout.
    {
        var vm = new CheckoutSubmitRequestDto // Tao model mac dinh.
        {
            Payment = new CheckoutPaymentInputDto { PaymentMethod = "COD" } // Mac dinh COD.
        };
        return View(vm); // Render Views/Checkout/Index.cshtml.
    }
    [HttpGet("/checkout/success")]
    public IActionResult Success()
    {
        ViewBag.OrderCode = TempData["OrderCode"];
        ViewBag.OrderDate = TempData["OrderDate"];
        ViewBag.TotalAmount = TempData["TotalAmount"];
        ViewBag.PaymentMethod = TempData["PaymentMethod"];
        ViewBag.RecipientName = TempData["RecipientName"];
        ViewBag.RecipientPhone = TempData["RecipientPhone"];
        ViewBag.RecipientAddress = TempData["RecipientAddress"];
        return View();
    }


    [HttpPost("/checkout")] // POST submit checkout.
    [ValidateAntiForgeryToken] // Bao ve CSRF.
    public async Task<IActionResult> Index(CheckoutSubmitRequestDto request) // Nhan form model.
    {
        var token = HttpContext.Session.GetString(AccessTokenSessionKey); // Lay JWT.
        if (string.IsNullOrWhiteSpace(token)) // Mat token.
        {
            return RedirectToAction("SignIn", "Account"); // Ve login.
        }

        if (request.Items == null || request.Items.Count == 0) // Chan submit don rong.
        {
            ModelState.AddModelError(string.Empty, "Don hang can it nhat 1 san pham."); // Bao loi.
            return View(request); // Render lai form.
        }

        var orderingClient = _httpClientFactory.CreateClient("Ordering"); // Client goi Ordering.
        orderingClient.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token); // Gan bearer.

        var response = await orderingClient.PostAsJsonAsync("/api/orders", request); // Goi tao don.
        if (!response.IsSuccessStatusCode) // Tao don that bai.
        {
            var errorText = await response.Content.ReadAsStringAsync(); // Doc loi.
            ModelState.AddModelError(string.Empty, $"Tao don that bai: {errorText}"); // Show loi.
            return View(request); // O lai form.
        }
        var created = await response.Content.ReadFromJsonAsync<CreateOrderResultDto>();

        if (created is not null)
        {
            TempData["OrderCode"] = $"FF{created.OrderId:D6}";
            TempData["TotalAmount"] = created.TotalAmount;
        }
        TempData["OrderDate"] = DateTime.Now;
        TempData["PaymentMethod"] = request.Payment?.PaymentMethod ?? "COD";
        TempData["RecipientName"] = request.Shipping.FullName;
        TempData["RecipientPhone"] = request.Shipping.Phone;
        TempData["RecipientAddress"] = request.Shipping.AddressDetail ?? "Chưa cập nhật";

        return RedirectToAction(nameof(Success)); // Thanh cong -> ve lich su don.
    }
    private sealed class CreateOrderResultDto
    {
        public int OrderId { get; set; }
        public decimal TotalAmount { get; set; }
    }

}