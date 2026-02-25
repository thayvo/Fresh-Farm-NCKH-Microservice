using FreshFram.Areas.Admin.Models;
using FreshFarm.Web.Bff.Areas.Seller.Infrastructure;
using FreshFram.Models; // Namespace chứa Entity Framework models
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using System.Data.Entity;

namespace FreshFarm.Web.Bff.Areas.Seller.Controllers
{
    [Authorize]   // bắt buộc đã đăng nhập
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]          // chống back/đọc từ cache
    [Microsoft.AspNetCore.Mvc.Area("Seller")]
    public class HomeController : LegacySellerControllerBase
    {
        private FreshFarmDBEntities db = new FreshFarmDBEntities();

        // GET: Admin/Home/Dashboard
        public ActionResult Dashboard()
        {
            var viewModel = new DashboardViewModel();

            try
            {
                // 1. Tính doanh thu tháng hiện tại
                var currentMonth = DateTime.Now.Month;
                var currentYear = DateTime.Now.Year;

                viewModel.MonthlyRevenue = db.Orders
                    .Where(o => o.OrderDate.Month == currentMonth
                             && o.OrderDate.Year == currentYear
                             && o.Status != "Canceled")  // ✅ Đổi từ "Đã hủy" thành "Canceled"
                    .Sum(o => (decimal?)o.TotalAmount) ?? 0;

                // 2. Đếm đơn hàng mới (Status = "Pending")
                viewModel.NewOrdersCount = db.Orders
                    .Count(o => o.Status == "Pending");  // ✅ Chỉ đếm Pending

                // 3. Tổng số khách hàng
                viewModel.TotalCustomers = db.Users.Count();

                // 4. Sản phẩm sắp hết (StockQuantity < 20)
                viewModel.LowStockProducts = db.Products
                    .Count(p => p.StockQuantity < 20 && p.Status == true);

                // 5. Dữ liệu biểu đồ doanh thu 7 ngày gần nhất
                var last7Days = Enumerable.Range(0, 7)
                    .Select(i => DateTime.Now.Date.AddDays(-6 + i))
                    .ToList();

                viewModel.ChartLabels = last7Days
                    .Select(d => d.ToString("dd/MM"))
                    .ToList();

                viewModel.ChartData = last7Days
                    .Select(d => db.Orders
                        .Where(o => o.OrderDate.Year == d.Year
                                 && o.OrderDate.Month == d.Month
                                 && o.OrderDate.Day == d.Day
                                 && o.Status != "Canceled")  // ✅ Đổi từ "Đã hủy" thành "Canceled"
                        .Sum(o => (decimal?)o.TotalAmount) ?? 0)
                    .ToList();

                // 6. Dữ liệu biểu đồ danh mục bán chạy
                var categorySales = db.OrderDetails
                    .Join(db.Products, od => od.ProductID, p => p.ProductID, (od, p) => new { od, p })
                    .Join(db.Categories, x => x.p.CategoryId, c => c.CategoryID, (x, c) => new { x.od, c })
                    .GroupBy(x => x.c.CategoryName)
                    .Select(g => new {
                        CategoryName = g.Key,
                        TotalQuantity = g.Sum(x => x.od.Quantity)
                    })
                    .OrderByDescending(x => x.TotalQuantity)
                    .Take(4)
                    .ToList();

                viewModel.CategoryLabels = categorySales.Select(c => c.CategoryName).ToList();
                viewModel.CategoryData = categorySales.Select(c => c.TotalQuantity).ToList();

                // Fallback nếu không có dữ liệu
                if (!viewModel.CategoryLabels.Any())
                {
                    viewModel.CategoryLabels = new List<string> { "Rau lá", "Rau ăn hoa", "Rau ăn quả", "Củ & rễ" };
                    viewModel.CategoryData = new List<int> { 0, 0, 0, 0 };
                }

                // 7. Đơn hàng gần đây (5 đơn mới nhất)
                viewModel.RecentOrders = db.Orders
                    .Join(db.Users, o => o.UserID, u => u.UserID, (o, u) => new { o, u })
                    .OrderByDescending(x => x.o.OrderDate)
                    .Take(5)
                    .ToList()
                    .Select(x => new RecentOrderViewModel
                    {
                        OrderCode = $"#DH{x.o.OrderID.ToString().PadLeft(6, '0')}",
                        CustomerName = x.u.FullName,
                        OrderDate = x.o.OrderDate,
                        TotalAmount = x.o.TotalAmount,
                        Status = GetStatusText(x.o.Status),  // ✅ Convert status sang tiếng Việt
                        StatusBadgeClass = GetStatusBadgeClass(x.o.Status)
                    })
                    .ToList();

                // 8. Thống kê chat hỗ trợ
                viewModel.OpenSupportConversations = db.SupportConversations.Count(c => c.Status == "Open");
                viewModel.UnreadSupportMessages = db.SupportMessages
                    .Count(m => m.SenderType == 0 && m.IsRead == false);
            }
            catch (Exception ex)
            {
                // Log error
                ViewBag.ErrorMessage = "Có lỗi xảy ra khi tải dữ liệu: " + ex.Message;
                System.Diagnostics.Debug.WriteLine($"[ERROR] Dashboard: {ex.Message}");
            }

            return View(viewModel);
        }

        // GET: Admin/Home/Profile
        public ActionResult Profile()
        {
            if (!(Session["ADMIN_ID"] is int adminId))
                return RedirectToAction("Login", "AdminAccount", new { area = "Seller" });

            var ua = db.UserAdmins
                       .Include(u => u.Role)
                       .FirstOrDefault(u => u.AdminID == adminId);
            if (ua == null) return HttpNotFound("Không tìm thấy admin đang đăng nhập.");

            var vm = new ProfileViewModel
            {
                UserID = ua.AdminID,
                UserName = ua.UserName,
                FullName = ua.FullName,
                Email = ua.Email,
                Phone = ua.Phone,
                CreatedDate = ua.CreatedDate,
                LastActivity = ua.LastLogin.HasValue
                              ? "Đăng nhập gần nhất: " + ua.LastLogin.Value.ToString("dd/MM/yyyy HH:mm")
                              : "Chưa có hoạt động"
            };

            var activities = new System.Collections.Generic.List<string>();

            if (ua.UpdatedDate.HasValue)
                activities.Add($"Cập nhật hồ sơ lúc {ua.UpdatedDate.Value:dd/MM/yyyy HH:mm}");

            if (ua.LastLogin.HasValue)
                activities.Add($"Đăng nhập gần nhất lúc {ua.LastLogin.Value:dd/MM/yyyy HH:mm}");

            if (TempData["SuccessMessage"]?.ToString().Contains("mật khẩu") == true)
                activities.Insert(0, "Vừa đổi mật khẩu thành công");
            if (TempData["SuccessMessage"]?.ToString().Contains("thông tin") == true)
                activities.Insert(0, "Vừa cập nhật hồ sơ thành công");

            if (!activities.Any())
                activities.Add("Chưa có hoạt động nào gần đây");

            ViewBag.Activities = activities;
            ViewBag.RoleName = ua.Role?.RoleName ?? "—";
            ViewBag.Avatar = string.IsNullOrWhiteSpace(ua.Avatar) ? "no-avatar.jpg" : ua.Avatar;

            return View(vm);
        }

        // POST: Admin/Home/Profile  (cập nhật hồ sơ)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Profile(ProfileViewModel model)
        {
            if (!(Session["ADMIN_ID"] is int adminId))
                return RedirectToAction("Login", "AdminAccount", new { area = "Seller" });

            if (!ModelState.IsValid) return View(model);

            var ua = db.UserAdmins.FirstOrDefault(u => u.AdminID == adminId);
            if (ua == null) return HttpNotFound();

            ua.FullName = model.FullName?.Trim();
            ua.Email = model.Email?.Trim();
            ua.Phone = string.IsNullOrWhiteSpace(model.Phone) ? null : model.Phone.Trim();
            ua.UpdatedDate = DateTime.Now;

            db.SaveChanges();
            TempData["SuccessMessage"] = "Cập nhật thông tin thành công!";
            return RedirectToAction("Profile");
        }

        // POST: Admin/Home/ChangePassword
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult ChangePassword(ChangePasswordViewModel model)
        {
            if (!(Session["ADMIN_ID"] is int adminId))
                return RedirectToAction("Login", "AdminAccount", new { area = "Seller" });

            model.CurrentPassword = model.CurrentPassword?.Trim();
            model.NewPassword = model.NewPassword?.Trim();
            model.ConfirmPassword = model.ConfirmPassword?.Trim();

            if (string.IsNullOrEmpty(model.CurrentPassword) ||
                string.IsNullOrEmpty(model.NewPassword) ||
                string.IsNullOrEmpty(model.ConfirmPassword))
            {
                TempData["ErrorMessage"] = "Vui lòng nhập đầy đủ các trường.";
                return RedirectToAction("Profile");
            }

            if (!string.Equals(model.NewPassword, model.ConfirmPassword, StringComparison.Ordinal))
            {
                TempData["ErrorMessage"] = "Mật khẩu xác nhận không khớp.";
                return RedirectToAction("Profile");
            }

            var ua = db.UserAdmins.FirstOrDefault(u => u.AdminID == adminId);
            if (ua == null) return HttpNotFound();

            if (!string.Equals(ua.Password, model.CurrentPassword, StringComparison.Ordinal))
            {
                TempData["ErrorMessage"] = "Mật khẩu hiện tại không đúng.";
                return RedirectToAction("Profile");
            }

            if (string.Equals(ua.Password, model.NewPassword, StringComparison.Ordinal))
            {
                TempData["ErrorMessage"] = "Mật khẩu mới không được trùng mật khẩu hiện tại.";
                return RedirectToAction("Profile");
            }

            ua.Password = model.NewPassword;
            ua.UpdatedDate = DateTime.Now;
            db.SaveChanges();

            TempData["SuccessMessage"] = "Đổi mật khẩu thành công!";
            return RedirectToAction("Profile");
        }

        // ==================== HELPER METHODS ====================

        /// <summary>
        /// Convert status code sang text tiếng Việt
        /// </summary>
        private string GetStatusText(string status)
        {
            switch (status)
            {
                case "Pending": return "Chờ xử lý";
                case "Processing": return "Đang xử lý";
                case "Shipped": return "Đang giao hàng";
                case "Delivered": return "Đã giao hàng";
                case "Canceled": return "Đã hủy";
                default: return status;
            }
        }

        /// <summary>
        /// Lấy class badge theo status
        /// </summary>
        private string GetStatusBadgeClass(string status)
        {
            switch (status)
            {
                case "Pending":
                    return "bg-secondary";
                case "Processing":
                    return "bg-warning text-dark";
                case "Shipped":
                    return "bg-info";
                case "Delivered":
                    return "bg-success";
                case "Canceled":
                    return "bg-danger";
                default:
                    return "bg-secondary";
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                db.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
