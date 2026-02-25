using FreshFarm.Web.Bff.Areas.Seller.Infrastructure;
using FreshFram.Models;
using System;
using System.Linq;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace FreshFarm.Web.Bff.Areas.Seller.Controllers
{
    [Authorize]   // bắt buộc đã đăng nhập
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]          // chống back/đọc từ cache
    [Microsoft.AspNetCore.Mvc.Area("Seller")]
    public class AdminAccountController : LegacySellerControllerBase
    {
        private readonly FreshFarmDBEntities db = new FreshFarmDBEntities();

        // GET: /Admin/AdminAccount/Login
        [HttpGet]
        [AllowAnonymous]
        public ActionResult Login(string returnUrl)
        {
            ViewBag.ReturnUrl = returnUrl;
            return View(new AdminLoginVM());
        }

        // POST: /Admin/AdminAccount/Login
        [HttpPost]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        public ActionResult Login(AdminLoginVM vm, string returnUrl)
        {
            if (!ModelState.IsValid) return View(vm);

            var admin = db.UserAdmins
                          .FirstOrDefault(a => a.UserName == vm.UserName && a.Password == vm.Password);
            if (admin == null)
            {
                ModelState.AddModelError("", "Sai tài khoản hoặc mật khẩu.");
                return View(vm);
            }
            if (!admin.IsActive)
            {
                ModelState.AddModelError("", "Tài khoản đã bị khóa.");
                return View(vm);
            }

            admin.LastLogin = DateTime.Now;
            db.SaveChanges();

            // Lưu phiên
            Session["ADMIN_ID"] = admin.AdminID;
            Session["ADMIN_NAME"] = admin.FullName;
            Session["ADMIN_ROLEID"] = admin.RoleID;

            // Cookie phụ trợ cho SignalR (WebSocket không có Session)
            var adminCookieOptions = new CookieOptions
            {
                HttpOnly = true,
                Expires = DateTimeOffset.UtcNow.AddDays(7)
            };
            Response.Cookies.Append("ADMIN_ID", admin.AdminID.ToString(), adminCookieOptions);

            // (tuỳ chọn) nhớ đăng nhập 7 ngày
            if (vm.RememberMe)
            {
                var rememberOptions = new CookieOptions
                {
                    HttpOnly = true,
                    Expires = DateTimeOffset.UtcNow.AddDays(7)
                };
                Response.Cookies.Append("ADMIN_REMEMBER", admin.UserName ?? string.Empty, rememberOptions);
            }

            if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
                return Redirect(returnUrl);

            // Dashboard trong Area Admin
            return RedirectToAction("Dashboard", "Home", new { area = "Seller" });
        }

        // POST: /Admin/AdminAccount/Logout
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Logout()
        {
            // Xoá session
            Session.Clear();
            Session.Abandon();

            // Xoá cookies
            Response.Cookies.Delete("ASP.NET_SessionId");
            Response.Cookies.Delete("ADMIN_REMEMBER");
            Response.Cookies.Delete("ADMIN_ID");

            HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme)
                .GetAwaiter()
                .GetResult();

            // Chống Back sau khi logout
            Response.Headers["Cache-Control"] = "no-store, no-cache, max-age=0";
            Response.Headers["Pragma"] = "no-cache";
            Response.Headers["Expires"] = "0";

            return RedirectToAction("Login", "AdminAccount", new { area = "Seller" });
        }
    }
}
