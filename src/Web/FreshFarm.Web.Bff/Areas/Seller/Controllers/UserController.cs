using FreshFarm.Web.Bff.Areas.Seller.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace FreshFarm.Web.Bff.Areas.Seller.Controllers;

[Authorize(Roles = "Seller")]
[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
[Area("Seller")]
public class UserController : LegacySellerControllerBase
{
    private const string SellerUserManagementDisabledMessage = "Tinh nang quan ly nguoi dung he thong da bi khoa cho Seller.";

    public UserController()
    { }

    [HttpGet]
    public async Task<IActionResult> ManageUsers()
    {
        TempData["ErrorMessage"] = SellerUserManagementDisabledMessage;
        await Task.CompletedTask;
        return RedirectToAction("Profile", "Home", new { area = "Seller" });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SearchUsers(string searchTerm)
    {
        TempData["ErrorMessage"] = SellerUserManagementDisabledMessage;
        await Task.CompletedTask;
        return RedirectToAction("Profile", "Home", new { area = "Seller" });
    }

    [HttpGet]
    public async Task<JsonResult> GetUserById(int id)
    {
        Response.StatusCode = StatusCodes.Status403Forbidden;
        await Task.CompletedTask;
        return Json(new { success = false, message = SellerUserManagementDisabledMessage });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<JsonResult> CreateUser(string userName, string fullName, string email, string password, string? phone, string? avatar, bool? isActive, int? roleId)
    {
        Response.StatusCode = StatusCodes.Status403Forbidden;
        await Task.CompletedTask;
        return Json(new { success = false, message = SellerUserManagementDisabledMessage });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<JsonResult> UpdateUser(int userId, string userName, string fullName, string email, string? phone, string? avatar, bool? isActive, int? roleId, string? newPassword = null)
    {
        Response.StatusCode = StatusCodes.Status403Forbidden;
        await Task.CompletedTask;
        return Json(new { success = false, message = SellerUserManagementDisabledMessage });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<JsonResult> DeleteUser(int userId)
    {
        Response.StatusCode = StatusCodes.Status403Forbidden;
        await Task.CompletedTask;
        return Json(new { success = false, message = SellerUserManagementDisabledMessage });
    }
}
