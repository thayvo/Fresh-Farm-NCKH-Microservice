using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FreshFarm.Web.Bff.Controllers
{
    public class HomeController : Controller
    {
        [EnableRateLimiting("public-read")]
        public IActionResult Index()
        {
            return View();
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
    }
}
