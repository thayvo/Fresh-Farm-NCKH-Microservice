using Microsoft.AspNetCore.Mvc;

namespace FreshFarm.Web.Bff.Controllers
{
    public class HomeController : Controller
    {
        public IActionResult Index()
        {
            return View();
        }
    }
}
