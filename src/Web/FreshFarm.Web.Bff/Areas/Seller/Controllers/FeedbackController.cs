using System;
using System.Linq;
using FreshFarm.Web.Bff.Areas.Seller.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using FreshFram.Areas.Admin.Data;
using Microsoft.AspNetCore.Authorization;
using FreshFram.Models;

namespace FreshFarm.Web.Bff.Areas.Seller.Controllers
{
    [Authorize]   // bắt buộc đã đăng nhập
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]          // chống back/đọc từ cache
    [Microsoft.AspNetCore.Mvc.Area("Seller")]
    public class FeedbackController : LegacySellerControllerBase
    {
        private FreshFarmDBEntities db = new FreshFarmDBEntities();

        // GET: Admin/Feedback
        public ActionResult Index()
        {
            var feedbacks = db.ContactMessages
                .OrderByDescending(x => x.CreatedAt)
                .Select(x => new FeedbackViewModel
                {
                    Id = x.Id,
                    SenderName = x.SenderName,
                    SenderEmail = x.SenderEmail,
                    SenderPhone = x.SenderPhone,
                    Subject = x.Subject,
                    Message = x.Message,
                    CreatedAt = x.CreatedAt,
                    Status = x.Status,
                    AdminNote = x.AdminNote
                })
                .ToList();

            return View(feedbacks);
        }

        [HttpPost]
        public JsonResult UpdateStatus(int id, string status = "processed")
        {
            try
            {
                var feedback = db.ContactMessages.FirstOrDefault(x => x.Id == id);
                if (feedback == null)
                {
                    return Json(new { success = false, message = "Không tìm thấy phản hồi." });
                }

                var statusProperty = feedback.GetType().GetProperty("Status");
                if (statusProperty is not null && statusProperty.CanWrite)
                {
                    var isProcessed =
                        string.Equals(status, "processed", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(status, "resolved", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(status, "done", StringComparison.OrdinalIgnoreCase);
                    var converted = ConvertStatusValue(statusProperty.PropertyType, isProcessed, status);
                    statusProperty.SetValue(feedback, converted);
                }

                db.SaveChanges();
                return Json(new { success = true, message = "Đã cập nhật trạng thái phản hồi." });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Lỗi: " + ex.Message });
            }
        }

        [HttpPost]
        public JsonResult Delete(int id)
        {
            try
            {
                var feedback = db.ContactMessages.FirstOrDefault(x => x.Id == id);
                if (feedback == null)
                {
                    return Json(new { success = false, message = "Phản hồi không tồn tại hoặc đã bị xóa." });
                }

                db.ContactMessages.Remove(feedback);
                db.SaveChanges();
                return Json(new { success = true, message = "Đã xóa phản hồi thành công." });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Lỗi: " + ex.Message });
            }
        }

        private static object? ConvertStatusValue(Type targetType, bool isProcessed, string rawStatus)
        {
            var actualType = Nullable.GetUnderlyingType(targetType) ?? targetType;

            if (actualType == typeof(string))
            {
                return string.IsNullOrWhiteSpace(rawStatus)
                    ? (isProcessed ? "processed" : "new")
                    : rawStatus;
            }

            if (actualType == typeof(bool))
            {
                return isProcessed;
            }

            if (actualType == typeof(int))
            {
                return isProcessed ? 1 : 0;
            }

            if (actualType.IsEnum)
            {
                if (!string.IsNullOrWhiteSpace(rawStatus))
                {
                    foreach (var enumName in Enum.GetNames(actualType))
                    {
                        if (string.Equals(enumName, rawStatus, StringComparison.OrdinalIgnoreCase))
                        {
                            return Enum.Parse(actualType, enumName);
                        }
                    }
                }

                var fallbackName = Enum.GetNames(actualType).FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(fallbackName))
                {
                    return Enum.Parse(actualType, fallbackName);
                }
            }

            return rawStatus;
        }
    }
}
