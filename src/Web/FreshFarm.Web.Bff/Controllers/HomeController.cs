using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FreshFarm.Web.Bff.Controllers
{
    public class HomeController : Controller
    {
        private const string AccessTokenSessionKey = "ACCESS_TOKEN";
        private readonly IHttpClientFactory _httpClientFactory;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        public HomeController(IHttpClientFactory httpClientFactory)
        {
            _httpClientFactory = httpClientFactory;
        }

        [EnableRateLimiting("public-read")]
        public IActionResult Index()
        {
            return View();
        }

        [HttpGet("/home/notification-popup")]
        [EnableRateLimiting("public-read")]
        public async Task<IActionResult> NotificationPopup()
        {
            var token = HttpContext.Session.GetString(AccessTokenSessionKey);
            if (string.IsNullOrWhiteSpace(token))
            {
                return NoContent();
            }

            try
            {
                var client = _httpClientFactory.CreateClient("Ordering");
                client.DefaultRequestHeaders.Remove("Authorization");
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

                var response = await client.GetAsync("/api/orders/notifications/me/home-popup");
                if (response.StatusCode == System.Net.HttpStatusCode.NoContent)
                {
                    return NoContent();
                }

                if (!response.IsSuccessStatusCode)
                {
                    return NoContent();
                }

                var popup = await response.Content.ReadFromJsonAsync<HomeNotificationPopupDto>(JsonOptions);
                if (popup is null || popup.NotificationId <= 0)
                {
                    return NoContent();
                }

                return Ok(popup);
            }
            catch
            {
                return NoContent();
            }
        }

        [HttpGet("/products")]
        [HttpGet("/search")]
        [HttpGet("/home/search")]
        [EnableRateLimiting("search-read")]
        public IActionResult Search(string? q)
        {
            ViewData["SearchKeyword"] = q ?? string.Empty;
            return View();
        }

        [HttpGet("/products/{id:int}")]
        [HttpGet("/product/{id:int}")]
        [EnableRateLimiting("public-read")]
        public IActionResult Product(int id, string? q)
        {
            ViewData["ProductId"] = id;
            ViewData["SearchKeyword"] = q ?? string.Empty;
            return View();
        }

        [HttpGet("/shop")]
        [HttpGet("/shops")]
        [EnableRateLimiting("public-read")]
        public IActionResult Shops(string? q)
        {
            ViewData["SearchKeyword"] = q ?? string.Empty;
            return View();
        }

        [HttpGet("/shop/{sellerId:int}")]
        [EnableRateLimiting("public-read")]
        public IActionResult Shop(int sellerId)
        {
            ViewData["SellerId"] = sellerId;
            return View();
        }

        private sealed class HomeNotificationPopupDto
        {
            public int NotificationId { get; set; }

            public string? NotificationType { get; set; }

            public string? Title { get; set; }

            public string? Message { get; set; }

            public string? PopupType { get; set; }

            public string? PopupImageUrl { get; set; }

            public DateTime? CreatedAt { get; set; }

            public DateTime? ExpiresAt { get; set; }
        }
    }
}
