using Microsoft.AspNetCore.Mvc;

namespace FreshFarm.Web.Bff.Controllers
{
    public class HomeController : Controller
    {
        public IActionResult Index()
        {
            return View();
        }

        [HttpGet("/search")]
        [HttpGet("/home/search")]
        public IActionResult Search(string? q)
        {
            ViewData["SearchKeyword"] = q ?? string.Empty;
            return View();
        }

        [HttpGet("/products/{id:int}")]
        [HttpGet("/product/{id:int}")]
        public IActionResult Product(int id, string? q)
        {
            ViewData["ProductId"] = id;
            ViewData["SearchKeyword"] = q ?? string.Empty;
            return View();
        }

        [HttpGet("/shop")]
        [HttpGet("/shops")]
        public IActionResult Shops(string? q)
        {
            ViewData["SearchKeyword"] = q ?? string.Empty;
            return View();
        }

        [HttpGet("/shop/{sellerId:int}")]
        public IActionResult Shop(int sellerId)
        {
            ViewData["SellerId"] = sellerId;
            return View();
        }
    }
}
