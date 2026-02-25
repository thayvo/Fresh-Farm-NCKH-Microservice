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
    public class UserController : LegacySellerControllerBase
    {
        private readonly FreshFarmDBEntities db = new FreshFarmDBEntities();

        private IQueryable<UserAdmin> AdminsWithRole()
            => db.UserAdmins.Include("Role");

        private void LoadRoles()
            => ViewBag.Roles = db.Roles
                                 .Where(r => r.IsActive) // chỉ show role đang hoạt động
                                 .OrderBy(r => r.RoleName)
                                 .ToList();

        // GET: Admin/User
        public ActionResult ManageUsers()
        {
            var list = AdminsWithRole()
                       .OrderByDescending(a => a.CreatedDate)
                       .ToList();
            LoadRoles();
            return View(list); // View: IEnumerable<UserAdmin>
        }

        // POST: Tìm kiếm
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult SearchUsers(string searchTerm)
        {
            searchTerm = (searchTerm ?? "").Trim().ToLower();

            var q = AdminsWithRole();
            if (!string.IsNullOrEmpty(searchTerm))
            {
                q = q.Where(a =>
                    ((a.UserName ?? "").ToLower().Contains(searchTerm)) ||
                    ((a.FullName ?? "").ToLower().Contains(searchTerm)) ||
                    ((a.Email ?? "").ToLower().Contains(searchTerm)) ||
                    ((a.Phone ?? "").ToLower().Contains(searchTerm))
                );
            }

            var list = q.OrderByDescending(a => a.CreatedDate).ToList();
            LoadRoles();
            return View("ManageUsers", list);
        }

        // GET: Lấy 1 admin theo ID (JSON cho modal Edit)
        [HttpGet]
        public JsonResult GetUserById(int id)
        {
            var a = AdminsWithRole().FirstOrDefault(x => x.AdminID == id);
            if (a == null)
                return Json(new { success = false, message = "Không tìm thấy quản trị viên" });

            return Json(new
            {
                success = true,
                data = new
                {
                    userId = a.AdminID,
                    userName = a.UserName,
                    fullName = a.FullName,
                    email = a.Email,
                    phone = a.Phone,
                    avatar = a.Avatar,
                    roleId = a.RoleID,
                    roleName = a.Role != null ? a.Role.RoleName : null,
                    isActive = a.IsActive,
                    lastLogin = a.LastLogin?.ToString("yyyy-MM-ddTHH:mm"),
                    created = a.CreatedDate.ToString("yyyy-MM-ddTHH:mm"),
                    updated = a.UpdatedDate?.ToString("yyyy-MM-ddTHH:mm")
                }
            });
        }

        // POST: Thêm admin
        [HttpPost]
        [ValidateAntiForgeryToken]
        public JsonResult CreateUser(string userName, string fullName, string email, string password, string phone, string avatar, bool? isActive, int roleId)
        {
            try
            {
                userName = (userName ?? "").Trim();
                fullName = (fullName ?? "").Trim();
                email = (email ?? "").Trim();
                password = (password ?? "").Trim();
                phone = (phone ?? "").Trim();
                avatar = string.IsNullOrWhiteSpace(avatar) ? null : avatar.Trim();

                if (string.IsNullOrWhiteSpace(userName))
                    return Json(new { success = false, message = "Tên đăng nhập không được để trống!" });
                if (string.IsNullOrWhiteSpace(fullName))
                    return Json(new { success = false, message = "Họ tên không được để trống!" });
                if (string.IsNullOrWhiteSpace(email))
                    return Json(new { success = false, message = "Email không được để trống!" });
                if (string.IsNullOrWhiteSpace(password))
                    return Json(new { success = false, message = "Mật khẩu không được để trống!" });

                if (db.UserAdmins.Any(a => a.UserName == userName))
                    return Json(new { success = false, message = "Tên đăng nhập đã tồn tại!" });
                if (db.UserAdmins.Any(a => a.Email == email))
                    return Json(new { success = false, message = "Email đã được sử dụng!" });

                //if (roleId.HasValue && !db.Roles.Any(r => r.RoleID == roleId.Value && r.IsActive))
                //    return Json(new { success = false, message = "Quyền không hợp lệ!" });

                var now = DateTime.Now;
                var admin = new UserAdmin
                {
                    UserName = userName,
                    FullName = fullName,
                    Email = email,
                    Password = password, // lưu plain-text theo yêu cầu demo
                    Phone = string.IsNullOrEmpty(phone) ? null : phone,
                    Avatar = avatar,
                    RoleID = roleId,
                    IsActive = isActive ?? true,
                    CreatedDate = now,
                    UpdatedDate = now,
                    CreatedBy = null
                };

                db.UserAdmins.Add(admin);
                db.SaveChanges();

                return Json(new { success = true, message = "Thêm quản trị viên thành công!" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Lỗi: " + ex.Message });
            }
        }

        // POST: Cập nhật admin
        [HttpPost]
        [ValidateAntiForgeryToken]
        public JsonResult UpdateUser(int userId, string userName, string fullName, string email, string phone, string avatar, bool? isActive, int roleId, string newPassword = null)
        {
            try
            {
                userName = (userName ?? "").Trim();
                fullName = (fullName ?? "").Trim();
                email = (email ?? "").Trim();
                phone = (phone ?? "").Trim();
                avatar = string.IsNullOrWhiteSpace(avatar) ? null : avatar.Trim();

                var a = db.UserAdmins.FirstOrDefault(x => x.AdminID == userId);
                if (a == null)
                    return Json(new { success = false, message = "Không tìm thấy quản trị viên" });

                if (string.IsNullOrWhiteSpace(userName))
                    return Json(new { success = false, message = "Tên đăng nhập không được để trống!" });
                if (string.IsNullOrWhiteSpace(fullName))
                    return Json(new { success = false, message = "Họ tên không được để trống!" });
                if (string.IsNullOrWhiteSpace(email))
                    return Json(new { success = false, message = "Email không được để trống!" });

                if (db.UserAdmins.Any(x => x.UserName == userName && x.AdminID != userId))
                    return Json(new { success = false, message = "Tên đăng nhập đã tồn tại!" });
                if (db.UserAdmins.Any(x => x.Email == email && x.AdminID != userId))
                    return Json(new { success = false, message = "Email đã được sử dụng!" });

                //if (roleId.HasValue && !db.Roles.Any(r => r.RoleID == roleId.Value && r.IsActive))
                //    return Json(new { success = false, message = "Quyền không hợp lệ!" });

                a.UserName = userName;
                a.FullName = fullName;
                a.Email = email;
                a.Phone = string.IsNullOrEmpty(phone) ? null : phone;
                a.Avatar = avatar;
                a.IsActive = isActive ?? a.IsActive;
                a.RoleID = roleId;
                a.UpdatedDate = DateTime.Now;

                if (!string.IsNullOrWhiteSpace(newPassword))
                    a.Password = newPassword;

                db.SaveChanges();

                return Json(new { success = true, message = "Cập nhật quản trị viên thành công!" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Lỗi: " + ex.Message });
            }
        }

        // POST: Xóa admin (tối thiểu)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public JsonResult DeleteUser(int userId)
        {
            try
            {
                var a = db.UserAdmins.FirstOrDefault(x => x.AdminID == userId);
                if (a == null)
                    return Json(new { success = false, message = "Không tìm thấy quản trị viên" });

                db.UserAdmins.Remove(a);
                db.SaveChanges();

                return Json(new { success = true, message = "Xóa quản trị viên thành công!" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Lỗi: " + ex.Message });
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) db.Dispose();
            base.Dispose(disposing);
        }
    }
}
