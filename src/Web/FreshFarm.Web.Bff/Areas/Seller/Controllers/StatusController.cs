using FreshFarm.Web.Bff.Areas.Seller.Infrastructure;
using FreshFarm.Web.Bff.Areas.Seller.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace FreshFarm.Web.Bff.Areas.Seller.Controllers;

[Authorize(Roles = "Seller")]
[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
[Area("Seller")]
public sealed class StatusController : LegacySellerControllerBase
{
    private const string SellerStatusAdminDisabledMessage = "Tinh nang quan ly trang thai he thong da bi khoa cho Seller.";

    [HttpGet]
    public IActionResult Status(string searchTerm = "", int? statusTypeID = null, int page = 1)
        => RedirectToSellerDashboard();

    [HttpPost]
    [ValidateAntiForgeryToken]
    public JsonResult CreateStatus(SellerStatusViewModel status)
        => CreateDisabledJsonResult();

    [HttpGet]
    public JsonResult GetStatusDetail(int id)
        => CreateDisabledJsonResult();

    [HttpPost]
    [ValidateAntiForgeryToken]
    public JsonResult EditStatus(SellerStatusViewModel status)
        => CreateDisabledJsonResult();

    [HttpPost]
    [ValidateAntiForgeryToken]
    public JsonResult DeleteStatus(int id)
        => CreateDisabledJsonResult();

    [HttpGet]
    public IActionResult StatusType(string searchTerm = "", int page = 1)
        => RedirectToSellerDashboard();

    [HttpPost]
    [ValidateAntiForgeryToken]
    public JsonResult CreateStatusType(SellerStatusTypeViewModel statusType)
        => CreateDisabledJsonResult();

    [HttpGet]
    public JsonResult GetStatusTypeDetail(int id)
        => CreateDisabledJsonResult();

    [HttpPost]
    [ValidateAntiForgeryToken]
    public JsonResult EditStatusType(SellerStatusTypeViewModel statusType)
        => CreateDisabledJsonResult();

    [HttpPost]
    [ValidateAntiForgeryToken]
    public JsonResult DeleteStatusType(int id)
        => CreateDisabledJsonResult();

    private IActionResult RedirectToSellerDashboard()
    {
        TempData["ErrorMessage"] = SellerStatusAdminDisabledMessage;
        return RedirectToAction("Dashboard", "Home", new { area = "Seller" });
    }

    private static JsonResult CreateDisabledJsonResult()
    {
        return new JsonResult(new
        {
            success = false,
            message = SellerStatusAdminDisabledMessage
        })
        {
            StatusCode = StatusCodes.Status403Forbidden
        };
    }
}
