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
    }
}
