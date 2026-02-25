using FreshFarm.Web.Bff.Areas.Seller.Infrastructure;
using FreshFram.Models;
using System;
using System.Data.Entity;
using System.Linq;
using Microsoft.AspNetCore.Mvc;

namespace FreshFarm.Web.Bff.Areas.Seller.Controllers
{
    [Authorize]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    [Microsoft.AspNetCore.Mvc.Area("Seller")]
    public class ReviewController : LegacySellerControllerBase
    {
        private FreshFarmDBEntities db = new FreshFarmDBEntities();

        // GET: Admin/Review
        // Trả về toàn bộ review (gốc + phản hồi) để view tự lọc/nhóm
        public ActionResult ManageReview()
        {
            var reviews = db.Reviews
                .Include(r => r.Product)
                .Include(r => r.User)
                .OrderByDescending(r => r.CreatedAt)
                .ToList();

            return View(reviews);
        }

        // Lọc review theo từ khóa, rating, trạng thái (IsApproved)
        [HttpGet]
        public ActionResult FilterReviews(string search, int? rating, int? status)
        {
            var q = db.Reviews
                .Include(r => r.Product)
                .Include(r => r.User)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(search))
            {
                var s = search.Trim().ToLower();
                q = q.Where(r =>
                    (r.User != null && ((r.User.FullName ?? r.User.UserName).ToLower().Contains(s))) ||
                    (r.Product != null && r.Product.ProductName.ToLower().Contains(s)) ||
                    (r.Comment != null && r.Comment.ToLower().Contains(s))
                );
            }

            if (rating.HasValue && rating.Value >= 1 && rating.Value <= 5)
            {
                q = q.Where(r => r.Rating == rating.Value || (r.ReplyTo != null && r.Rating == 0));
            }

            if (status.HasValue)
            {
                if (status.Value == 1) q = q.Where(r => r.IsApproved == true);
                else if (status.Value == 0) q = q.Where(r => r.IsApproved == false);
            }

            var list = q.OrderByDescending(r => r.CreatedAt).ToList();
            return View("ManageReview", list);
        }

        public class ReportedReviewRow
        {
            public int ReviewID { get; set; }
            public int OpenCount { get; set; }
            public DateTime FirstReportAt { get; set; }

            // Thông tin review để hiển thị
            public string CustomerName { get; set; }
            public string ProductName { get; set; }
            public string ProductImageFileName { get; set; }
            public int Rating { get; set; }
            public string Comment { get; set; }
            public DateTime CreatedAt { get; set; }
        }

        // =============== REPORTED ===============
        // Danh sách bình luận bị báo cáo (group theo Review)
        public ActionResult ReportedReviews()
        {
            var grouped = db.ReviewReports
                .Where(rp => rp.Status == 0)
                .GroupBy(rp => rp.ReviewID)
                .Select(g => new
                {
                    ReviewID = g.Key,
                    OpenCount = g.Count(),
                    FirstReportAt = g.Min(x => x.CreatedAt)
                })
                .ToList();

            var ids = grouped.Select(g => g.ReviewID).ToList();

            var reviews = db.Reviews
                .Include(r => r.Product)
                .Include(r => r.User)
                .Where(r => ids.Contains(r.ReviewID))
                .ToList();

            var data = grouped
                .Join(reviews, g => g.ReviewID, r => r.ReviewID, (g, r) => new ReportedReviewRow
                {
                    ReviewID = g.ReviewID,
                    OpenCount = g.OpenCount,
                    FirstReportAt = g.FirstReportAt,

                    CustomerName = (r.User != null ? (r.User.FullName ?? r.User.UserName) : "-"),
                    ProductName = r.Product != null ? r.Product.ProductName : "-",
                    ProductImageFileName = r.Product != null ? r.Product.ImageFileName : null,
                    Rating = r.Rating,
                    Comment = r.Comment,
                    CreatedAt = r.CreatedAt
                })
                .OrderByDescending(x => x.OpenCount)
                .ThenByDescending(x => x.FirstReportAt)
                .ToList();

            return View(data); // model IEnumerable<ReportedReviewRow>
        }

        // Admin quyết định xử lý report
        [HttpPost]
        public JsonResult ResolveReport(int reviewId, string decision)
        {
            var review = db.Reviews
                .Include(r => r.Product)
                .FirstOrDefault(r => r.ReviewID == reviewId);

            if (review == null) return Json(new { success = false, message = "Không tìm thấy bình luận." });

            var openReports = db.ReviewReports.Where(x => x.ReviewID == reviewId && x.Status == 0).ToList();

            try
            {
                switch ((decision ?? "").ToLower())
                {
                    case "dismiss":
                        foreach (var rp in openReports) rp.Status = 1; // Dismissed
                        db.SaveChanges();
                        return Json(new { success = true, message = "Đã bỏ qua các báo cáo." });

                    case "hide":
                        review.IsApproved = false; // ẩn bình luận
                        foreach (var rp in openReports) rp.Status = 2; // ActionTaken
                        db.SaveChanges();
                        return Json(new { success = true, message = "Đã ẩn bình luận và cập nhật báo cáo." });

                    case "delete":
                        var replies = db.Reviews.Where(r => r.ReplyTo == reviewId).ToList();
                        db.Reviews.RemoveRange(replies);
                        db.Reviews.Remove(review);
                        foreach (var rp in openReports) rp.Status = 2; // ActionTaken
                        db.SaveChanges();
                        return Json(new { success = true, message = "Đã xóa bình luận và cập nhật báo cáo." });

                    default:
                        return Json(new { success = false, message = "Quyết định không hợp lệ." });
                }
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Lỗi: " + ex.Message });
            }
        }

        // =============== DELETE (Admin) ===============
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize]
        public JsonResult DeleteReview(int reviewId)
        {
            var review = db.Reviews.FirstOrDefault(r => r.ReviewID == reviewId);
            if (review == null) return Json(new { success = false, message = "Không tìm thấy bình luận." });

            try
            {
                var replies = db.Reviews.Where(r => r.ReplyTo == reviewId).ToList();
                if (replies.Any()) db.Reviews.RemoveRange(replies);
                db.Reviews.Remove(review);
                db.SaveChanges();
                return Json(new { success = true, message = "Đã xóa bình luận." });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize]
        public JsonResult ApproveReview(int reviewId)
        {
            var review = db.Reviews.FirstOrDefault(r => r.ReviewID == reviewId);
            if (review == null) return Json(new { success = false, message = "Không tìm thấy đánh giá." });

            review.IsApproved = true;
            db.SaveChanges();
            return Json(new { success = true, message = "Đã duyệt đánh giá." });
        }

        // Ẩn/Bỏ ẩn bình luận (toggle IsApproved)
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize]
        public JsonResult ToggleVisibility(int reviewId)
        {
            var review = db.Reviews.FirstOrDefault(r => r.ReviewID == reviewId);
            if (review == null) return Json(new { success = false, message = "Không tìm thấy bình luận." });

            review.IsApproved = !review.IsApproved;
            db.SaveChanges();

            return Json(new
            {
                success = true,
                approved = review.IsApproved,
                message = review.IsApproved ? "Đã bỏ ẩn bình luận." : "Đã ẩn bình luận."
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize]
        public JsonResult ReplyReview(int reviewId, string content)
        {
            if (string.IsNullOrWhiteSpace(content))
                return Json(new { success = false, message = "Nội dung phản hồi trống." });

            var parent = db.Reviews.FirstOrDefault(r => r.ReviewID == reviewId);
            if (parent == null) return Json(new { success = false, message = "Không tìm thấy đánh giá." });

            // Lấy thông tin admin từ Session
            var adminIdObj = Session["ADMIN_ID"];
            var adminName = (Session["ADMIN_NAME"] as string) ?? "Admin";
            if (adminIdObj == null) return Json(new { success = false, message = "Phiên admin hết hạn, vui lòng đăng nhập lại." });
            var adminId = (int)adminIdObj;

            // Tìm/tạo user mirror cho admin trong bảng Users
            var mirrorUserName = "ADMIN_" + adminId;
            var adminUser = db.Users.FirstOrDefault(u => u.UserName == mirrorUserName);
            if (adminUser == null)
            {
                adminUser = new User
                {
                    UserName = mirrorUserName,
                    FullName = adminName,
                    Email = $"{mirrorUserName.ToLower()}@local",
                    Password = "!",
                    CreatedDate = DateTime.Now
                };
                db.Users.Add(adminUser);
                db.SaveChanges();
            }

            var reply = new Review
            {
                ProductID = parent.ProductID,
                UserID = adminUser.UserID,
                Rating = 0,
                Comment = content?.Trim(),
                CreatedAt = DateTime.Now,
                IsApproved = true,
                ReplyTo = parent.ReviewID,
                IsEdited = false,
                UpdatedAt = null
            };
            db.Reviews.Add(reply);
            db.SaveChanges();
            return Json(new { success = true, message = "Đã gửi phản hồi." });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize]
        public JsonResult UpdateReview(int id, string comment, bool? isApproved)
        {
            var review = db.Reviews.FirstOrDefault(r => r.ReviewID == id);
            if (review == null) return Json(new { success = false, message = "Không tìm thấy bình luận." });

            if (!string.IsNullOrWhiteSpace(comment))
            {
                review.Comment = comment.Trim();
                review.IsEdited = true;
                review.UpdatedAt = DateTime.Now;
            }
            if (isApproved.HasValue)
            {
                review.IsApproved = isApproved.Value;
            }
            db.SaveChanges();
            return Json(new { success = true, message = "Cập nhật thành công." });
        }
        [HttpGet]
        public JsonResult GetReviewReports(int reviewId, int page = 1)
        {
            const int pageSize = 5;

            var query = db.ReviewReports
                .Where(rp => rp.ReviewID == reviewId)
                .OrderByDescending(rp => rp.CreatedAt);

            var total = query.Count();
            var totalPages = (int)Math.Ceiling((double)total / pageSize);

            // ⚠️ Chuyển sang LINQ to Objects để dùng ToString("format")
            var data = query
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .AsEnumerable()
                .Select(rp => new
                {
                    reporter = rp.ReporterUserID,
                    reason = rp.Reason,
                    note = rp.Note,
                    createdAt = rp.CreatedAt.ToString("dd/MM/yyyy HH:mm")
                })
                .ToList();

            return Json(new
            {
                success = true,
                data,
                page,
                totalPages
            });
        }



        protected override void Dispose(bool disposing)
        {
            if (disposing) db.Dispose();
            base.Dispose(disposing);
        }
    }
}
