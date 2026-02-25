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
    public class StatusController : LegacySellerControllerBase
    {
        private FreshFarmDBEntities db = new FreshFarmDBEntities();

        #region Status Management

        // GET: Admin/Status
        public ActionResult Status(string searchTerm = "", int? statusTypeID = null, int page = 1)
        {
            int pageSize = 6;

            var query = db.Status.Include(s => s.StatusType).Where(s => s.IsActive).AsQueryable();

            // Lọc theo từ khóa tìm kiếm
            if (!string.IsNullOrEmpty(searchTerm))
            {
                query = query.Where(s => s.StatusName.Contains(searchTerm));
            }

            // Lọc theo loại trạng thái
            if (statusTypeID.HasValue && statusTypeID.Value > 0)
            {
                query = query.Where(s => s.StatusTypeID == statusTypeID.Value);
            }

            // Tổng số mục
            int totalItems = query.Count();
            int totalPages = (int)Math.Ceiling((double)totalItems / pageSize);

            // Đảm bảo page hợp lệ
            if (page < 1) page = 1;
            if (page > totalPages && totalPages > 0) page = totalPages;

            var statusList = query
                .OrderBy(s => s.DisplayOrder)
                .ThenBy(s => s.StatusName)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToList();

            // Thông tin phân trang
            ViewBag.CurrentPage = page;
            ViewBag.TotalPages = totalPages;
            ViewBag.TotalItems = totalItems;
            ViewBag.PageSize = pageSize;

            // Lấy danh sách loại trạng thái cho dropdown
            ViewBag.StatusTypes = new SelectList(
                db.StatusTypes.Where(st => st.IsActive).OrderBy(st => st.StatusTypeName).ToList(),
                "StatusTypeID",
                "StatusTypeName"
            );

            // Giữ giá trị tìm kiếm
            ViewBag.SearchTerm = searchTerm;
            ViewBag.StatusTypeID = statusTypeID;

            return View(statusList);
        }

        // POST: Admin/Status/CreateStatus
        [HttpPost]
        [ValidateAntiForgeryToken]
        public JsonResult CreateStatus(Status status)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(status.StatusName))
                {
                    return Json(new { success = false, message = "Vui lòng nhập tên trạng thái!" });
                }

                if (status.StatusTypeID <= 0)
                {
                    return Json(new { success = false, message = "Vui lòng chọn loại trạng thái!" });
                }

                // Kiểm tra trùng tên
                if (db.Status.Any(s => s.StatusName == status.StatusName && s.StatusTypeID == status.StatusTypeID && s.IsActive))
                {
                    return Json(new { success = false, message = "Tên trạng thái đã tồn tại trong loại này!" });
                }

                status.ColorCode = string.IsNullOrWhiteSpace(status.ColorCode) ? "#28a745" : status.ColorCode;
                status.IsActive = true;
                status.CreatedDate = DateTime.Now;

                db.Status.Add(status);
                db.SaveChanges();

                return Json(new { success = true, message = "Thêm trạng thái thành công!" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Có lỗi xảy ra: " + ex.Message });
            }
        }

        // GET: Admin/Status/GetStatusDetail
        [HttpGet]
        public JsonResult GetStatusDetail(int id)
        {
            try
            {
                var status = db.Status
                    .Where(s => s.StatusID == id)
                    .Select(s => new
                    {
                        s.StatusID,
                        s.StatusName,
                        s.StatusTypeID,
                        s.ColorCode,
                        s.Note,
                        s.DisplayOrder
                    })
                    .FirstOrDefault();

                if (status != null)
                {
                    return Json(new { success = true, data = status });
                }

                return Json(new { success = false, message = "Không tìm thấy trạng thái" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // POST: Admin/Status/EditStatus
        [HttpPost]
        [ValidateAntiForgeryToken]
        public JsonResult EditStatus(Status status)
        {
            try
            {
                var existingStatus = db.Status.Find(status.StatusID);
                if (existingStatus == null)
                {
                    return Json(new { success = false, message = "Không tìm thấy trạng thái!" });
                }

                if (string.IsNullOrWhiteSpace(status.StatusName))
                {
                    return Json(new { success = false, message = "Vui lòng nhập tên trạng thái!" });
                }

                // Kiểm tra trùng tên (trừ chính nó)
                if (db.Status.Any(s => s.StatusName == status.StatusName
                                    && s.StatusTypeID == status.StatusTypeID
                                    && s.StatusID != status.StatusID
                                    && s.IsActive))
                {
                    return Json(new { success = false, message = "Tên trạng thái đã tồn tại trong loại này!" });
                }

                existingStatus.StatusName = status.StatusName;
                existingStatus.StatusTypeID = status.StatusTypeID;
                existingStatus.ColorCode = status.ColorCode;
                existingStatus.Note = status.Note;
                existingStatus.DisplayOrder = status.DisplayOrder;

                db.SaveChanges();

                return Json(new { success = true, message = "Cập nhật trạng thái thành công!" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Có lỗi xảy ra: " + ex.Message });
            }
        }

        // POST: Admin/Status/DeleteStatus
        [HttpPost]
        [ValidateAntiForgeryToken]
        public JsonResult DeleteStatus(int id)
        {
            try
            {
                var status = db.Status.Find(id);
                if (status == null)
                {
                    return Json(new { success = false, message = "Không tìm thấy trạng thái!" });
                }

                // Soft delete
                status.IsActive = false;
                db.SaveChanges();

                return Json(new { success = true, message = "Xóa trạng thái thành công!" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Có lỗi xảy ra: " + ex.Message });
            }
        }

        #endregion

        #region StatusType Management

        // GET: Admin/Status/StatusType
        public ActionResult StatusType(string searchTerm = "", int page = 1)
        {
            int pageSize = 6;

            var query = db.StatusTypes.Where(st => st.IsActive).AsQueryable();

            // Lọc theo từ khóa tìm kiếm
            if (!string.IsNullOrEmpty(searchTerm))
            {
                query = query.Where(st => st.StatusTypeName.Contains(searchTerm)
                                       || st.Description.Contains(searchTerm));
            }

            // Tổng số mục
            int totalItems = query.Count();
            int totalPages = (int)Math.Ceiling((double)totalItems / pageSize);

            // Đảm bảo page hợp lệ
            if (page < 1) page = 1;
            if (page > totalPages && totalPages > 0) page = totalPages;

            var statusTypeList = query
                .OrderBy(st => st.StatusTypeName)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToList();

            // Thông tin phân trang
            ViewBag.CurrentPage = page;
            ViewBag.TotalPages = totalPages;
            ViewBag.TotalItems = totalItems;
            ViewBag.PageSize = pageSize;
            ViewBag.SearchTerm = searchTerm;

            return View(statusTypeList);
        }

        // POST: Admin/Status/CreateStatusType
        [HttpPost]
        [ValidateAntiForgeryToken]
        public JsonResult CreateStatusType(StatusType statusType)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(statusType.StatusTypeName))
                {
                    return Json(new { success = false, message = "Vui lòng nhập tên loại trạng thái!" });
                }

                // Kiểm tra trùng tên
                if (db.StatusTypes.Any(st => st.StatusTypeName == statusType.StatusTypeName && st.IsActive))
                {
                    return Json(new { success = false, message = "Tên loại trạng thái đã tồn tại!" });
                }

                statusType.IsActive = true;
                statusType.CreatedDate = DateTime.Now;

                db.StatusTypes.Add(statusType);
                db.SaveChanges();

                return Json(new { success = true, message = "Thêm loại trạng thái thành công!" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Có lỗi xảy ra: " + ex.Message });
            }
        }

        // GET: Admin/Status/GetStatusTypeDetail
        [HttpGet]
        public JsonResult GetStatusTypeDetail(int id)
        {
            try
            {
                var statusType = db.StatusTypes
                    .Where(st => st.StatusTypeID == id)
                    .Select(st => new
                    {
                        st.StatusTypeID,
                        st.StatusTypeName,
                        st.Description
                    })
                    .FirstOrDefault();

                if (statusType != null)
                {
                    return Json(new { success = true, data = statusType });
                }

                return Json(new { success = false, message = "Không tìm thấy loại trạng thái" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // POST: Admin/Status/EditStatusType
        [HttpPost]
        [ValidateAntiForgeryToken]
        public JsonResult EditStatusType(StatusType statusType)
        {
            try
            {
                var existingStatusType = db.StatusTypes.Find(statusType.StatusTypeID);
                if (existingStatusType == null)
                {
                    return Json(new { success = false, message = "Không tìm thấy loại trạng thái!" });
                }

                if (string.IsNullOrWhiteSpace(statusType.StatusTypeName))
                {
                    return Json(new { success = false, message = "Vui lòng nhập tên loại trạng thái!" });
                }

                // Kiểm tra trùng tên (trừ chính nó)
                if (db.StatusTypes.Any(st => st.StatusTypeName == statusType.StatusTypeName
                                         && st.StatusTypeID != statusType.StatusTypeID
                                         && st.IsActive))
                {
                    return Json(new { success = false, message = "Tên loại trạng thái đã tồn tại!" });
                }

                existingStatusType.StatusTypeName = statusType.StatusTypeName;
                existingStatusType.Description = statusType.Description;

                db.SaveChanges();

                return Json(new { success = true, message = "Cập nhật loại trạng thái thành công!" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Có lỗi xảy ra: " + ex.Message });
            }
        }

        // POST: Admin/Status/DeleteStatusType
        [HttpPost]
        [ValidateAntiForgeryToken]
        public JsonResult DeleteStatusType(int id)
        {
            try
            {
                var statusType = db.StatusTypes.Find(id);
                if (statusType == null)
                {
                    return Json(new { success = false, message = "Không tìm thấy loại trạng thái!" });
                }

                // Kiểm tra có trạng thái nào đang sử dụng không
                var statusCount = db.Status.Count(s => s.StatusTypeID == id && s.IsActive);
                if (statusCount > 0)
                {
                    return Json(new { success = false, message = "Không thể xóa! Loại trạng thái đang được sử dụng bởi " + statusCount + " trạng thái." });
                }

                // Soft delete
                statusType.IsActive = false;
                db.SaveChanges();

                return Json(new { success = true, message = "Xóa loại trạng thái thành công!" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Có lỗi xảy ra: " + ex.Message });
            }
        }

        #endregion

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
