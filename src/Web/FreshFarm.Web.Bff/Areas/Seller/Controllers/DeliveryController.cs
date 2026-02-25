using FreshFarm.Web.Bff.Areas.Seller.Infrastructure;
using FreshFram.Models;
using System;
using System.Data.Entity;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using System.Data.SqlClient;

namespace FreshFarm.Web.Bff.Areas.Seller.Controllers
{
    [Authorize]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    [Microsoft.AspNetCore.Mvc.Area("Seller")]
    public class DeliveryController : LegacySellerControllerBase
    {
        private readonly FreshFarmDBEntities db = new FreshFarmDBEntities();

        // GET: Admin/Delivery
        [HttpGet]
        public ActionResult Index()
        {
            return View();
        }

        // GET: Admin/Delivery/List
        // List orders with shipping info for staff to deliver
        [HttpGet]
        public JsonResult List(string status = "Processing", int page = 1, int pageSize = 10)
        {
            try
            {
                if (page < 1) page = 1;
                if (pageSize <= 0) pageSize = 10;

                var query = db.Orders
                    .Include(o => o.User)
                    .Include(o => o.Shippings)
                    .Include(o => o.Shippings.Select(s => s.Commune))
                    .Include(o => o.Shippings.Select(s => s.Province))
                    .AsQueryable();

                // Only orders that have shipping snapshot (delivery required)
                query = query.Where(o => o.Shippings.Any());

                if (!string.IsNullOrWhiteSpace(status) && status != "All")
                    query = query.Where(o => o.Status == status);

                var total = query.Count();
                var items = query
                    .OrderByDescending(o => o.OrderDate)
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .ToList()
                    .Select(o => new
                    {
                        orderID = o.OrderID,
                        orderCode = "#" + o.OrderID.ToString("D6"),
                        customerName = o.User?.FullName ?? "",
                        customerPhone = o.User?.Phone ?? "",
                        status = o.Status,
                        orderDate = o.OrderDate.ToString("dd/MM/yyyy HH:mm"),
                        address = o.Shippings.Select(s =>
                            (s.IsStorePickup ? (s.StoreAddress ?? "") : ((s.AddressDetail ?? "")
                                + (s.Commune != null ? ", " + s.Commune.CommuneName : "")
                                + (s.Province != null ? ", " + s.Province.ProvinceName : ""))
                            )).FirstOrDefault() ?? "",
                        canAccept = (o.Status == "Processing"),
                        canDeliver = (o.Status == "Processing" || o.Status == "Shipped"),
                        canCancel = (o.Status == "Processing" || o.Status == "Shipped")
                    })
                    .ToList();

                return Json(new { success = true, data = items, total = total, page = page, pageSize = pageSize });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) db.Dispose();
            base.Dispose(disposing);
        }

        // GET: Admin/Delivery/Staffs
        [HttpGet]
        public JsonResult Staffs()
        {
            try
            {
                var staffs = db.UserAdmins
                    .Include(a => a.Role)
                    .Where(a => a.IsActive)
                    .OrderBy(a => a.FullName)
                    .Select(a => new
                    {
                        id = a.AdminID,
                        name = a.FullName,
                        role = a.Role != null ? a.Role.RoleName : null
                    })
                    .ToList();
                return Json(new { success = true, data = staffs });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // POST: Admin/Delivery/Assign
        [HttpPost]
        [ValidateAntiForgeryToken]
        public JsonResult Assign(int orderId, int staffId, string notes = null, DateTime? expectedPickupAt = null, DateTime? expectedDeliveryAt = null)
        {
            try
            {
                var adminIdObj = Session["ADMIN_ID"];
                var adminId = adminIdObj is int value ? value : (int?)null;
                if (!adminId.HasValue)
                    return Json(new { success = false, message = "Chưa đăng nhập quản trị." });

                db.Database.ExecuteSqlCommand(
                    "EXEC dbo.usp_Delivery_AssignOrder @p0, @p1, @p2, @p3, @p4, @p5",
                    orderId, staffId, adminId.Value, (object)notes ?? DBNull.Value,
                    (object)expectedPickupAt ?? DBNull.Value,
                    (object)expectedDeliveryAt ?? DBNull.Value);

                return Json(new { success = true, message = "Đã chỉ định nhân viên giao hàng." });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }
    }
}
