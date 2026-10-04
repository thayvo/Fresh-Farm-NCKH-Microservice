using FreshFarm.Web.Bff.Areas.Seller.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FreshFarm.Web.Bff.Areas.Seller.Controllers;

[Authorize(Roles = "Seller")]
[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
[Area("Seller")]
public class FeedbackController : LegacySellerControllerBase
{
    [HttpGet]
    public IActionResult Index()
    {
        return NotFound();
    }

    [HttpPost]
    public JsonResult UpdateStatus()
    {
        Response.StatusCode = StatusCodes.Status403Forbidden;
        return Json(new { success = false, message = "Trang phan hoi nguoi dung khong kha dung trong kenh Seller." });
    }

    [HttpGet]
    public JsonResult GetFeedback()
    {
        Response.StatusCode = StatusCodes.Status403Forbidden;
        return Json(new { success = false, message = "Trang phan hoi nguoi dung khong kha dung trong kenh Seller." });
    }

    [HttpPost]
    public JsonResult Delete()
    {
        Response.StatusCode = StatusCodes.Status403Forbidden;
        return Json(new { success = false, message = "Trang phan hoi nguoi dung khong kha dung trong kenh Seller." });
    }
}
