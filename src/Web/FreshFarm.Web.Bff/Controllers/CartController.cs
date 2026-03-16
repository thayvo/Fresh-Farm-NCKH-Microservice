using FreshFarm.Web.Bff.Dtos; // Dùng DTO cart.
using FreshFarm.Web.Bff.Services; // Dùng service cart session.
using Microsoft.AspNetCore.Authorization; // Dùng [Authorize].
using Microsoft.AspNetCore.Mvc; // Dùng Controller/IActionResult.
using System.Text.Json;

namespace FreshFarm.Web.Bff.Controllers; // Namespace controller.

[Authorize] // Giỏ hàng gắn với user đã login.
public sealed class CartController : Controller // Controller MVC cho trang giỏ.
{
    private readonly ICartSessionService _cart; // Service thao tác cart trong session.

    // thêm trong class CartController
    private const string CheckoutSelectedCartItemKeysSessionKey = "CHECKOUT_SELECTED_CART_ITEM_KEYS";

    [HttpPost("/cart/checkout-selected")]
    [ValidateAntiForgeryToken]
    public IActionResult CheckoutSelected([FromForm] string[] selectedCartItemKeys)
    {
        var cartKeys = _cart.GetItems()
            .Select(x => x.CartItemKey)
            .ToHashSet();

        var validSelectedKeys = (selectedCartItemKeys ?? Array.Empty<string>())
            .Where(key => !string.IsNullOrWhiteSpace(key) && cartKeys.Contains(key.Trim()))
            .Select(key => key.Trim())
            .Distinct()
            .ToList();

        if (validSelectedKeys.Count == 0)
        {
            HttpContext.Session.Remove(CheckoutSelectedCartItemKeysSessionKey);
            TempData["CheckoutError"] = "Vui lòng chọn ít nhất 1 sản phẩm để thanh toán.";
            return RedirectToAction(nameof(Index));
        }

        HttpContext.Session.SetString(
            CheckoutSelectedCartItemKeysSessionKey,
            JsonSerializer.Serialize(validSelectedKeys));

        return RedirectToAction("Index", "Checkout");
    }

    public CartController(ICartSessionService cart) // Inject service qua DI.
    {
        _cart = cart; // Gán field để dùng trong action.
    }

    [HttpGet("/cart")] // Route trang giỏ hàng.
    public IActionResult Index() // Render trang giỏ.
    {
        const decimal shippingFee = 15000m; // Mức ship tạm cho MVP.
        var vm = _cart.BuildSummary(shippingFee); // Tính subtotal/grand total.
        return View(vm); // Render Views/Cart/Index.cshtml.
    }

    [HttpPost("/cart/add")] // API thêm item vào giỏ.
    [ValidateAntiForgeryToken] // Chặn CSRF vì thay đổi trạng thái.
    public IActionResult Add([FromForm] AddToCartRequestDto request) // Nhận dữ liệu từ form/home.
    {
        if (request.ProductId <= 0) // Validate product id.
        {
            return BadRequest("ProductId không hợp lệ."); // Trả 400 để frontend biết request sai.
        }

        _cart.AddOrIncrease(request); // Thêm mới hoặc cộng dồn quantity.
        if (WantsJson())
        {
            var cartItems = _cart.GetItems();
            var cartItem = cartItems.FirstOrDefault(x =>
                x.ProductId == request.ProductId &&
                x.SellerId == request.SellerId);

            return Json(new
            {
                success = true,
                message = "Đã thêm sản phẩm vào giỏ hàng.",
                cartItemCount = cartItems.Count,
                cartQuantity = cartItems.Sum(x => Math.Max(0, x.Quantity)),
                quantity = cartItem?.Quantity ?? Math.Max(1, request.Quantity),
                cartUrl = Url.Action(nameof(Index), "Cart") ?? "/cart"
            });
        }

        return RedirectToAction(nameof(Index)); // Mặc định quay về trang cart.
    }

    [HttpPost("/cart/update")] // API cập nhật số lượng.
    [ValidateAntiForgeryToken] // Chặn CSRF.
    public IActionResult Update([FromForm] UpdateCartItemRequestDto request) // Nhận productId + quantity.
    {
        if (request.ProductId <= 0 && string.IsNullOrWhiteSpace(request.CartItemKey)) // Validate input.
        {
            return BadRequest("ProductId không hợp lệ."); // 400 nếu sai.
        }

        _cart.UpdateQuantity(request.ProductId, request.SellerId, request.CartItemKey, request.Quantity); // Update đúng dòng item multi-seller.
        return RedirectToAction(nameof(Index)); // Quay lại cart để thấy kết quả.
    }

    [HttpPost("/cart/remove")] // API xóa 1 item.
    [ValidateAntiForgeryToken] // Chặn CSRF.
    public IActionResult Remove([FromForm] RemoveCartItemRequestDto request) // Nhận productId cần xóa.
    {
        if (request.ProductId <= 0 && string.IsNullOrWhiteSpace(request.CartItemKey)) // Validate input.
        {
            return BadRequest("ProductId không hợp lệ."); // 400 nếu sai.
        }

        _cart.Remove(request.ProductId, request.SellerId, request.CartItemKey); // Xóa đúng dòng item multi-seller.
        return RedirectToAction(nameof(Index)); // Quay lại cart.
    }

    [HttpPost("/cart/clear")] // API xóa toàn bộ giỏ.
    [ValidateAntiForgeryToken] // Chặn CSRF.
    public IActionResult Clear() // Không cần payload.
    {
        _cart.Clear(); // Xóa sạch session cart.
        return RedirectToAction(nameof(Index)); // Quay lại cart rỗng.
    }

    private bool WantsJson()
    {
        var accept = Request.Headers.Accept.ToString();
        if (!string.IsNullOrWhiteSpace(accept) &&
            accept.Contains("application/json", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var requestedWith = Request.Headers["X-Requested-With"].ToString();
        return string.Equals(requestedWith, "XMLHttpRequest", StringComparison.OrdinalIgnoreCase);
    }
}
