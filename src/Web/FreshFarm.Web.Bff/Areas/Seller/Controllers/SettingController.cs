using FreshFarm.Web.Bff.Areas.Seller.Infrastructure;
using FreshFram.Models;
using System;
using System.Data.Entity;
using System.Linq;
using Microsoft.AspNetCore.Mvc;

namespace FreshFarm.Web.Bff.Areas.Seller.Controllers
{
    [Authorize]   // bắt buộc đã đăng nhập
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]          // chống back/đọc từ cache
    [Microsoft.AspNetCore.Mvc.Area("Seller")]
    public class SettingController : LegacySellerControllerBase
    {
        private FreshFarmDBEntities db = new FreshFarmDBEntities();

        // GET: Admin/Setting
        public ActionResult Index()
        {
            try
            {
                var setting = db.Settings.FirstOrDefault();

                if (setting == null)
                {
                    // Tạo cài đặt mặc định nếu chưa có
                    setting = new Setting
                    {
                        StoreName = "Fresh Farm",
                        StoreAddress = "123 Đường ABC, Quận 1, TP.HCM",
                        StoreEmail = "support@freshfram.vn",
                        StorePhone = "1900 1234",
                        IsCODEnabled = true,
                        BankTransferInstructions = "Vui lòng chuyển khoản với nội dung: TT [Mã đơn hàng]",
                        BankAccountInfo = "Ngân hàng: Vietcombank...",
                        DefaultShippingFee = 30000,
                        FreeShippingThreshold = 500000,
                        IsEmailNewOrderEnabled = true,
                        IsEmailDeliveredEnabled = true,
                        IsEmailCancelledEnabled = true,
                        AdminNotificationEmail = "admin@freshfram.vn",
                        CreatedAt = DateTime.Now,
                        UpdatedAt = DateTime.Now
                    };
                    db.Settings.Add(setting);
                    db.SaveChanges();
                }

                return View(setting);
            }
            catch (Exception ex)
            {
                ViewBag.Error = "Lỗi khi tải cài đặt: " + ex.Message;
                return View();
            }
        }

        // POST: Admin/Setting/Save
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Save(Setting model)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return Json(new { success = false, message = "Dữ liệu không hợp lệ" });
                }

                var setting = db.Settings.FirstOrDefault();

                if (setting == null)
                {
                    // Tạo mới nếu chưa có
                    model.CreatedAt = DateTime.Now;
                    model.UpdatedAt = DateTime.Now;
                    db.Settings.Add(model);
                }
                else
                {
                    // Cập nhật cài đặt chung
                    setting.StoreName = model.StoreName;
                    setting.StoreAddress = model.StoreAddress;
                    setting.StoreEmail = model.StoreEmail;
                    setting.StorePhone = model.StorePhone;

                    // Cập nhật cài đặt thanh toán
                    setting.IsCODEnabled = model.IsCODEnabled;
                    setting.BankTransferInstructions = model.BankTransferInstructions;
                    setting.BankAccountInfo = model.BankAccountInfo;

                    // Cập nhật cài đặt vận chuyển
                    setting.DefaultShippingFee = model.DefaultShippingFee;
                    setting.FreeShippingThreshold = model.FreeShippingThreshold;

                    // Cập nhật cài đặt thông báo
                    setting.IsEmailNewOrderEnabled = model.IsEmailNewOrderEnabled;
                    setting.IsEmailDeliveredEnabled = model.IsEmailDeliveredEnabled;
                    setting.IsEmailCancelledEnabled = model.IsEmailCancelledEnabled;
                    setting.AdminNotificationEmail = model.AdminNotificationEmail;

                    setting.UpdatedAt = DateTime.Now;
                    db.Entry(setting).State = EntityState.Modified;
                }

                db.SaveChanges();
                return Json(new { success = true, message = "Lưu cài đặt thành công!" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Lỗi: " + ex.Message });
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
