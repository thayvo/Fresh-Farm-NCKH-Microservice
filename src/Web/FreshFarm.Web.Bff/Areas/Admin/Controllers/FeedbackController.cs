using FreshFarm.Web.Bff.Areas.Seller.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FreshFarm.Web.Bff.Areas.Admin.Controllers;

[Authorize(Policy = "AdminOnly")]
[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
[Area("Admin")]
public sealed class FeedbackController : LegacySellerControllerBase
{
    [HttpGet]
    public IActionResult Index()
    {
        return NotFound();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public JsonResult UpdateStatus()
    {
        Response.StatusCode = StatusCodes.Status403Forbidden;
        return Json(new { success = false, message = "Trang phan hoi khach hang khong kha dung trong khu vuc Admin." });
    }

    [HttpGet]
    public JsonResult GetFeedback()
    {
        Response.StatusCode = StatusCodes.Status403Forbidden;
        return Json(new { success = false, message = "Trang phan hoi khach hang khong kha dung trong khu vuc Admin." });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public JsonResult Delete()
    {
        Response.StatusCode = StatusCodes.Status403Forbidden;
        return Json(new { success = false, message = "Trang phan hoi khach hang khong kha dung trong khu vuc Admin." });
    }
}
