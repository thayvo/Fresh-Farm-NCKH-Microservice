using Microsoft.AspNetCore.Authorization; // [Authorize].
using Microsoft.AspNetCore.Mvc; // ControllerBase + IActionResult.
using System.Net.Http.Headers; // AuthenticationHeaderValue.

namespace FreshFarm.Web.Bff.Controllers; // Namespace controller.

[ApiController] // API controller mode.
[Route("bff/orders")] // Prefix route cho order proxy.
[Authorize] // Chi user login moi duoc goi order proxy.
public sealed class BffOrdersController : ControllerBase // API proxy controller.
{
    private const string AccessTokenSessionKey = "ACCESS_TOKEN"; // Key trung voi AccountController.
    private readonly IHttpClientFactory _httpClientFactory; // Factory tao HttpClient.

    public BffOrdersController(IHttpClientFactory httpClientFactory) // Inject qua DI.
    {
        _httpClientFactory = httpClientFactory; // Gan field.
    }

    [HttpGet("my")] // GET /bff/orders/my
    public async Task<IActionResult> GetMyOrders() // Lay don cua user hien tai.
    {
        var token = HttpContext.Session.GetString(AccessTokenSessionKey); // Lay token tu session.
        if (string.IsNullOrWhiteSpace(token)) // Mat token.
        {
            return Unauthorized("Khong tim thay token trong session."); // Tra 401.
        }

        var orderingClient = _httpClientFactory.CreateClient("Ordering"); // Client cho Ordering API.
        orderingClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token); // Gan bearer.

        var response = await orderingClient.GetAsync("/api/orders/my"); // Goi endpoint goc.
        var body = await response.Content.ReadAsStringAsync(); // Doc body de tra nguyen ve client.

        return ForwardJson(response, body);
    }

    [HttpGet("{id:int}")] // GET /bff/orders/{id}
    public async Task<IActionResult> GetById(int id) // Lay chi tiet 1 don.
    {
        var token = HttpContext.Session.GetString(AccessTokenSessionKey); // Lay token.
        if (string.IsNullOrWhiteSpace(token)) // Mat token.
        {
            return Unauthorized("Khong tim thay token trong session."); // 401.
        }

        var orderingClient = _httpClientFactory.CreateClient("Ordering"); // Client cho Ordering API.
        orderingClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token); // Gan token.

        var response = await orderingClient.GetAsync($"/api/orders/{id}"); // Forward request.
        var body = await response.Content.ReadAsStringAsync(); // Doc body.

        return ForwardJson(response, body);
    }

    [HttpPost] // POST /bff/orders
    public async Task<IActionResult> Create([FromBody] object request) // Nhan payload tao don.
    {
        var token = HttpContext.Session.GetString(AccessTokenSessionKey); // Lay token.
        if (string.IsNullOrWhiteSpace(token)) // Mat token.
        {
            return Unauthorized("Khong tim thay token trong session."); // 401.
        }

        var orderingClient = _httpClientFactory.CreateClient("Ordering"); // Client cho Ordering API.
        orderingClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token); // Gan token.

        var response = await orderingClient.PostAsJsonAsync("/api/orders", request); // Forward POST tao don.
        var body = await response.Content.ReadAsStringAsync(); // Doc response body.

        return ForwardJson(response, body); // Tra nguyen trang thai + body.
    }
    private static IActionResult ForwardJson(HttpResponseMessage response, string body) // Helper pass-through json.
    {
        return new ContentResult // Tra response voi content type ro rang.
        {
            StatusCode = (int)response.StatusCode, // Giu nguyen status code goc.
            Content = body, // Body json goc.
            ContentType = "application/json" // Bat client parse dung JSON.
        };
    }
}