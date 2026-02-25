using FreshFram.Areas.Admin.Data;
using FreshFarm.Web.Bff.Areas.Seller.Infrastructure;
using FreshFram.Models;
using OfficeOpenXml;
using OfficeOpenXml.Style;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Drawing;
using System.Linq;
using Microsoft.AspNetCore.Mvc;

namespace FreshFarm.Web.Bff.Areas.Seller.Controllers
{
    [Authorize]   // bắt buộc đã đăng nhập
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]          // chống back/đọc từ cache
    [Microsoft.AspNetCore.Mvc.Area("Seller")]
    public class ReportController : LegacySellerControllerBase
    {
        private FreshFarmDBEntities db = new FreshFarmDBEntities();
        // AddressBook OwnerType for User (customer)
        private const byte OwnerTypeUser = 2;


        // GET: Admin/Report/Customer
        public ActionResult Customer(DateTime? registerFromDate, DateTime? registerToDate, string customerSegment)
        {
            var model = new CustomerReportViewModel
            {
                RegisterFromDate = registerFromDate,
                RegisterToDate = registerToDate,
                CustomerSegment = customerSegment ?? "all"
            };

            try
            {
                var customers = db.Users.ToList();
                var allOrders = db.Orders.ToList();
                var thirtyDaysAgo = DateTime.Now.AddDays(-30);

                // Áp dụng bộ lọc ngày
                if (registerFromDate.HasValue)
                {
                    customers = customers.Where(u => u.CreatedDate >= registerFromDate.Value).ToList();
                }
                if (registerToDate.HasValue)
                {
                    customers = customers.Where(u => u.CreatedDate <= registerToDate.Value.AddDays(1)).ToList();
                }

                // Thống kê cơ bản
                model.TotalCustomers = customers.Count;
                model.NewCustomers = customers.Count(c => c.CreatedDate >= thirtyDaysAgo);

                if (allOrders.Any())
                {
                    // Lấy danh sách đơn hàng đã thanh toán
                    var paidOrders = (from o in db.Orders
                                      join p in db.Payments on o.OrderID equals p.OrderID
                                      where p.PaymentStatus == "Đã thanh toán" || p.PaymentStatus == "Hoàn tất"
                                      select o).ToList();

                    // Thống kê chi tiêu chỉ từ các đơn hàng đã thanh toán
                    var customerOrderCounts = paidOrders
                        .GroupBy(o => o.UserID)
                        .Select(g => new {
                            UserID = g.Key,
                            OrderCount = g.Count(),
                            TotalSpent = g.Sum(o => o.TotalAmount)
                        })
                        .ToList();


                    var returningCustomers = customerOrderCounts.Count(c => c.OrderCount >= 2);
                    model.ReturnRate = model.TotalCustomers > 0 ? (decimal)returningCustomers / model.TotalCustomers * 100 : 0;

                    var totalRevenue = allOrders.Sum(o => o.TotalAmount);
                    model.AverageSpending = model.TotalCustomers > 0 ? totalRevenue / model.TotalCustomers : 0;

                    // Phân khúc khách hàng
                    model.NewCustomerCount = model.NewCustomers;
                    model.ReturningCustomerCount = customerOrderCounts.Count(c => c.OrderCount >= 2 && c.OrderCount < 5);
                    model.VIPCustomerCount = customerOrderCounts.Count(c => c.OrderCount >= 5 || c.TotalSpent >= 10000000);
                    model.OtherCustomerCount = model.TotalCustomers - model.NewCustomerCount - model.ReturningCustomerCount - model.VIPCustomerCount;
                    if (model.OtherCustomerCount < 0) model.OtherCustomerCount = 0;

                    // Top khách hàng
                    var topCustomers = customerOrderCounts.OrderByDescending(c => c.TotalSpent).Take(10).ToList();
                    var rank = 1;
                    foreach (var customer in topCustomers)
                    {
                        var user = customers.FirstOrDefault(u => u.UserID == customer.UserID);
                        if (user != null)
                        {
                            model.TopCustomers.Add(new TopCustomerViewModel
                            {
                                Rank = rank++,
                                UserID = user.UserID,
                                FullName = user.FullName,
                                AvatarUrl = Url.Action("AvatarById", "Account", new { area = "", id = user.UserID }),
                                CreatedDate = user.CreatedDate,
                                TotalOrders = customer.OrderCount,
                                TotalSpent = customer.TotalSpent
                            });
                        }
                    }
                }

                // Dữ liệu biểu đồ
                var sevenMonthsAgo = DateTime.Now.AddMonths(-6).Date;
                var growthData = customers
                    .Where(c => c.CreatedDate >= sevenMonthsAgo)
                    .GroupBy(c => new { c.CreatedDate.Year, c.CreatedDate.Month })
                    .OrderBy(g => g.Key.Year).ThenBy(g => g.Key.Month)
                    .Select(g => new { Label = "Tháng " + g.Key.Month, Count = g.Count() })
                    .ToList();

                if (growthData.Any())
                {
                    model.GrowthLabels = growthData.Select(d => d.Label).ToList();
                    model.GrowthData = growthData.Select(d => d.Count).ToList();
                }
                else
                {
                    for (int i = 6; i >= 0; i--)
                    {
                        var month = DateTime.Now.AddMonths(-i);
                        model.GrowthLabels.Add("Tháng " + month.Month);
                        model.GrowthData.Add(0);
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.ErrorMessage = "Lỗi: " + ex.Message;
            }

            // note: paging only applies in Review() action, not here
            return View(model);
        }

        // GET: Admin/Report/ExportCustomerExcel
        [HttpGet]
        public ActionResult ExportCustomerExcel(DateTime? registerFromDate, DateTime? registerToDate, string customerSegment)
        {
            try
            {
                // Set license context cho EPPlus (bắt buộc từ version 5.0+)
                // Từ EPPlus 8.0+, sử dụng ExcelPackage.License thay vì LicenseContext
                OfficeOpenXml.ExcelPackage.LicenseContext = OfficeOpenXml.LicenseContext.NonCommercial;

                var customers = db.Users.ToList();
                var allOrders = db.Orders.ToList();
                var thirtyDaysAgo = DateTime.Now.AddDays(-30);

                // Áp dụng bộ lọc ngày
                if (registerFromDate.HasValue)
                {
                    customers = customers.Where(u => u.CreatedDate >= registerFromDate.Value).ToList();
                }
                if (registerToDate.HasValue)
                {
                    customers = customers.Where(u => u.CreatedDate <= registerToDate.Value.AddDays(1)).ToList();
                }

                // Lấy danh sách đơn hàng đã thanh toán
                var paidOrders = (from o in db.Orders
                                  join p in db.Payments on o.OrderID equals p.OrderID
                                  where p.PaymentStatus == "Đã thanh toán" || p.PaymentStatus == "Hoàn tất"
                                  select o).ToList();

                // Thống kê chi tiêu chỉ từ các đơn hàng đã thanh toán
                var customerOrderCounts = paidOrders
                    .GroupBy(o => o.UserID)
                    .Select(g => new {
                        UserID = g.Key,
                        OrderCount = g.Count(),
                        TotalSpent = g.Sum(o => o.TotalAmount)
                    })
                    .ToList();


                var filteredCustomers = customers;
                if (!string.IsNullOrEmpty(customerSegment) && customerSegment != "all")
                {
                    switch (customerSegment)
                    {
                        case "new":
                            filteredCustomers = customers.Where(c => c.CreatedDate >= thirtyDaysAgo).ToList();
                            break;
                        case "returning":
                            var returningUserIds = customerOrderCounts
                                .Where(c => c.OrderCount >= 2 && c.OrderCount < 5)
                                .Select(c => c.UserID)
                                .ToList();
                            filteredCustomers = customers.Where(c => returningUserIds.Contains(c.UserID)).ToList();
                            break;
                        case "vip":
                            var vipUserIds = customerOrderCounts
                                .Where(c => c.OrderCount >= 5 || c.TotalSpent >= 10000000)
                                .Select(c => c.UserID)
                                .ToList();
                            filteredCustomers = customers.Where(c => vipUserIds.Contains(c.UserID)).ToList();
                            break;
                    }
                }

                // Tính toán thống kê
                var totalCustomers = filteredCustomers.Count;
                var newCustomers = filteredCustomers.Count(c => c.CreatedDate >= thirtyDaysAgo);
                var returningCustomers = customerOrderCounts.Count(c => c.OrderCount >= 2);
                var returnRate = totalCustomers > 0 ? (decimal)returningCustomers / totalCustomers * 100 : 0;
                var totalRevenue = allOrders.Where(o => filteredCustomers.Any(c => c.UserID == o.UserID)).Sum(o => o.TotalAmount);
                var avgSpending = totalCustomers > 0 ? totalRevenue / totalCustomers : 0;

                // Tạo danh sách top khách hàng
                var topCustomers = customerOrderCounts
                    .Where(c => filteredCustomers.Any(fc => fc.UserID == c.UserID))
                    .OrderByDescending(c => c.TotalSpent)
                    .Select((c, index) => new
                    {
                        Rank = index + 1,
                        Customer = filteredCustomers.FirstOrDefault(u => u.UserID == c.UserID),
                        OrderCount = c.OrderCount,
                        TotalSpent = c.TotalSpent
                    })
                    .Where(c => c.Customer != null)
                    .ToList();

                // Tạo file Excel
                using (var package = new ExcelPackage())
                {
                    // Tạo worksheet
                    var worksheet = package.Workbook.Worksheets.Add("Báo Cáo Khách Hàng");

                    // ===== TIÊU ĐỀ BÁO CÁO =====
                    worksheet.Cells["A1:G1"].Merge = true;
                    worksheet.Cells["A1"].Value = "BÁO CÁO KHÁCH HÀNG - FRESH FARM";
                    worksheet.Cells["A1"].Style.Font.Size = 18;
                    worksheet.Cells["A1"].Style.Font.Bold = true;
                    worksheet.Cells["A1"].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
                    worksheet.Cells["A1"].Style.VerticalAlignment = ExcelVerticalAlignment.Center;
                    worksheet.Cells["A1"].Style.Fill.PatternType = ExcelFillStyle.Solid;
                    worksheet.Cells["A1"].Style.Fill.BackgroundColor.SetColor(Color.FromArgb(79, 129, 189));
                    worksheet.Cells["A1"].Style.Font.Color.SetColor(Color.White);
                    worksheet.Row(1).Height = 30;

                    // ===== THÔNG TIN BỘ LỌC =====
                    int currentRow = 2;
                    worksheet.Cells[$"A{currentRow}:G{currentRow}"].Merge = true;
                    var filterText = $"Ngày xuất: {DateTime.Now:dd/MM/yyyy HH:mm:ss}";
                    worksheet.Cells[$"A{currentRow}"].Value = filterText;
                    worksheet.Cells[$"A{currentRow}"].Style.Font.Italic = true;
                    worksheet.Cells[$"A{currentRow}"].Style.Font.Size = 10;
                    currentRow++;

                    if (registerFromDate.HasValue || registerToDate.HasValue || !string.IsNullOrEmpty(customerSegment))
                    {
                        worksheet.Cells[$"A{currentRow}:G{currentRow}"].Merge = true;
                        var filterInfo = "Bộ lọc: ";
                        if (registerFromDate.HasValue)
                            filterInfo += $"Từ {registerFromDate.Value:dd/MM/yyyy} ";
                        if (registerToDate.HasValue)
                            filterInfo += $"đến {registerToDate.Value:dd/MM/yyyy} ";
                        if (!string.IsNullOrEmpty(customerSegment) && customerSegment != "all")
                        {
                            var segmentName = customerSegment == "new" ? "Khách hàng mới" :
                                            customerSegment == "returning" ? "Khách quay lại" :
                                            customerSegment == "vip" ? "Khách VIP" : "";
                            filterInfo += $"- Phân khúc: {segmentName}";
                        }
                        worksheet.Cells[$"A{currentRow}"].Value = filterInfo;
                        worksheet.Cells[$"A{currentRow}"].Style.Font.Italic = true;
                        worksheet.Cells[$"A{currentRow}"].Style.Font.Size = 10;
                        currentRow++;
                    }

                    // ===== THỐNG KÊ TỔNG QUAN =====
                    currentRow++;
                    worksheet.Cells[$"A{currentRow}:G{currentRow}"].Merge = true;
                    worksheet.Cells[$"A{currentRow}"].Value = "THỐNG KÊ TỔNG QUAN";
                    worksheet.Cells[$"A{currentRow}"].Style.Font.Bold = true;
                    worksheet.Cells[$"A{currentRow}"].Style.Font.Size = 14;
                    worksheet.Cells[$"A{currentRow}"].Style.Fill.PatternType = ExcelFillStyle.Solid;
                    worksheet.Cells[$"A{currentRow}"].Style.Fill.BackgroundColor.SetColor(Color.FromArgb(242, 242, 242));
                    currentRow++;

                    // Tạo bảng thống kê 2 cột
                    var statsStartRow = currentRow;

                    worksheet.Cells[$"A{currentRow}"].Value = "Tổng khách hàng:";
                    worksheet.Cells[$"A{currentRow}"].Style.Font.Bold = true;
                    worksheet.Cells[$"B{currentRow}"].Value = totalCustomers;
                    worksheet.Cells[$"B{currentRow}"].Style.Font.Bold = true;
                    worksheet.Cells[$"B{currentRow}"].Style.Font.Color.SetColor(Color.FromArgb(0, 112, 192));

                    worksheet.Cells[$"D{currentRow}"].Value = "Khách hàng mới (30 ngày):";
                    worksheet.Cells[$"D{currentRow}"].Style.Font.Bold = true;
                    worksheet.Cells[$"E{currentRow}"].Value = newCustomers;
                    worksheet.Cells[$"E{currentRow}"].Style.Font.Color.SetColor(Color.FromArgb(0, 176, 80));
                    currentRow++;

                    worksheet.Cells[$"A{currentRow}"].Value = "Tỷ lệ quay lại:";
                    worksheet.Cells[$"A{currentRow}"].Style.Font.Bold = true;
                    worksheet.Cells[$"B{currentRow}"].Value = $"{returnRate:F2}%";
                    worksheet.Cells[$"B{currentRow}"].Style.Font.Color.SetColor(Color.FromArgb(0, 112, 192));

                    worksheet.Cells[$"D{currentRow}"].Value = "Chi tiêu TB/Khách:";
                    worksheet.Cells[$"D{currentRow}"].Style.Font.Bold = true;
                    worksheet.Cells[$"E{currentRow}"].Value = avgSpending;
                    worksheet.Cells[$"E{currentRow}"].Style.Numberformat.Format = "#,##0 ₫";
                    worksheet.Cells[$"E{currentRow}"].Style.Font.Color.SetColor(Color.FromArgb(255, 192, 0));
                    currentRow++;

                    // Border cho phần thống kê
                    using (var range = worksheet.Cells[$"A{statsStartRow}:E{currentRow - 1}"])
                    {
                        range.Style.Border.Top.Style = ExcelBorderStyle.Thin;
                        range.Style.Border.Left.Style = ExcelBorderStyle.Thin;
                        range.Style.Border.Right.Style = ExcelBorderStyle.Thin;
                        range.Style.Border.Bottom.Style = ExcelBorderStyle.Thin;
                    }

                    // ===== BẢNG TOP KHÁCH HÀNG =====
                    currentRow += 2;
                    worksheet.Cells[$"A{currentRow}:G{currentRow}"].Merge = true;
                    worksheet.Cells[$"A{currentRow}"].Value = "TOP KHÁCH HÀNG CHI TIÊU NHIỀU NHẤT";
                    worksheet.Cells[$"A{currentRow}"].Style.Font.Bold = true;
                    worksheet.Cells[$"A{currentRow}"].Style.Font.Size = 14;
                    worksheet.Cells[$"A{currentRow}"].Style.Fill.PatternType = ExcelFillStyle.Solid;
                    worksheet.Cells[$"A{currentRow}"].Style.Fill.BackgroundColor.SetColor(Color.FromArgb(242, 242, 242));
                    currentRow++;

                    // Header bảng
                    int headerRow = currentRow;
                    worksheet.Cells[$"A{headerRow}"].Value = "Hạng";
                    worksheet.Cells[$"B{headerRow}"].Value = "Tên khách hàng";
                    worksheet.Cells[$"C{headerRow}"].Value = "Email";
                    worksheet.Cells[$"D{headerRow}"].Value = "Số điện thoại";
                    worksheet.Cells[$"E{headerRow}"].Value = "Địa chỉ";
                    worksheet.Cells[$"F{headerRow}"].Value = "Ngày đăng ký";
                    worksheet.Cells[$"G{headerRow}"].Value = "Tổng đơn";
                    worksheet.Cells[$"H{headerRow}"].Value = "Tổng chi tiêu";

                    // Format header
                    using (var range = worksheet.Cells[$"A{headerRow}:H{headerRow}"])
                    {
                        range.Style.Font.Bold = true;
                        range.Style.Fill.PatternType = ExcelFillStyle.Solid;
                        range.Style.Fill.BackgroundColor.SetColor(Color.FromArgb(217, 225, 242));
                        range.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
                        range.Style.VerticalAlignment = ExcelVerticalAlignment.Center;
                        range.Style.Border.Top.Style = ExcelBorderStyle.Thin;
                        range.Style.Border.Left.Style = ExcelBorderStyle.Thin;
                        range.Style.Border.Right.Style = ExcelBorderStyle.Thin;
                        range.Style.Border.Bottom.Style = ExcelBorderStyle.Thin;
                    }
                    worksheet.Row(headerRow).Height = 20;

                    // Dữ liệu khách hàng
                    currentRow++;
                    int dataStartRow = currentRow;
                    foreach (var customer in topCustomers)
                    {
                        worksheet.Cells[$"A{currentRow}"].Value = customer.Rank;
                        worksheet.Cells[$"A{currentRow}"].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;

                        worksheet.Cells[$"B{currentRow}"].Value = customer.Customer.FullName;

                        worksheet.Cells[$"C{currentRow}"].Value = customer.Customer.Email;

                        worksheet.Cells[$"D{currentRow}"].Value = customer.Customer.Phone ?? "N/A";

                        // Use shared AddressBooks for default address (schema changed)
                        var _addr = db.AddressBooks
                            .Include("Province")
                            .Include("Commune")
                            .Where(a => a.OwnerType == OwnerTypeUser && a.OwnerId == customer.Customer.UserID && a.IsActive)
                            .OrderByDescending(a => a.IsDefault)
                            .ThenByDescending(a => a.CreatedAt)
                            .FirstOrDefault();

                        string _addrText = null;
                        if (_addr != null)
                        {
                            var parts = new List<string>();
                            if (!string.IsNullOrWhiteSpace(_addr.AddressDetail)) parts.Add(_addr.AddressDetail);
                            if (_addr.Commune != null && !string.IsNullOrWhiteSpace(_addr.Commune.CommuneName)) parts.Add(_addr.Commune.CommuneName);
                            if (_addr.Province != null && !string.IsNullOrWhiteSpace(_addr.Province.ProvinceName)) parts.Add(_addr.Province.ProvinceName);
                            _addrText = parts.Count > 0 ? string.Join(", ", parts) : null;
                        }

                        worksheet.Cells[$"E{currentRow}"].Value = string.IsNullOrWhiteSpace(_addrText) ? "N/A" : _addrText;

                        worksheet.Cells[$"F{currentRow}"].Value = customer.Customer.CreatedDate.ToString("dd/MM/yyyy");
                        worksheet.Cells[$"F{currentRow}"].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;

                        worksheet.Cells[$"G{currentRow}"].Value = customer.OrderCount;
                        worksheet.Cells[$"G{currentRow}"].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;

                        worksheet.Cells[$"H{currentRow}"].Value = customer.TotalSpent;
                        worksheet.Cells[$"H{currentRow}"].Style.Numberformat.Format = "#,##0 ₫";
                        worksheet.Cells[$"H{currentRow}"].Style.Font.Bold = true;
                        worksheet.Cells[$"H{currentRow}"].Style.Font.Color.SetColor(Color.FromArgb(0, 128, 0));

                        // Highlight top 3
                        if (customer.Rank <= 3)
                        {
                            using (var range = worksheet.Cells[$"A{currentRow}:H{currentRow}"])
                            {
                                range.Style.Fill.PatternType = ExcelFillStyle.Solid;
                                range.Style.Fill.BackgroundColor.SetColor(Color.FromArgb(255, 242, 204));
                            }
                        }

                        currentRow++;
                    }

                    // Format bảng dữ liệu
                    if (topCustomers.Any())
                    {
                        using (var range = worksheet.Cells[$"A{dataStartRow}:H{currentRow - 1}"])
                        {
                            range.Style.Border.Top.Style = ExcelBorderStyle.Thin;
                            range.Style.Border.Left.Style = ExcelBorderStyle.Thin;
                            range.Style.Border.Right.Style = ExcelBorderStyle.Thin;
                            range.Style.Border.Bottom.Style = ExcelBorderStyle.Thin;
                        }

                        // Tổng cộng
                        currentRow++;
                        worksheet.Cells[$"A{currentRow}:F{currentRow}"].Merge = true;
                        worksheet.Cells[$"A{currentRow}"].Value = "TỔNG CỘNG";
                        worksheet.Cells[$"A{currentRow}"].Style.Font.Bold = true;
                        worksheet.Cells[$"A{currentRow}"].Style.HorizontalAlignment = ExcelHorizontalAlignment.Right;

                        worksheet.Cells[$"G{currentRow}"].Value = topCustomers.Sum(c => c.OrderCount);
                        worksheet.Cells[$"G{currentRow}"].Style.Font.Bold = true;
                        worksheet.Cells[$"G{currentRow}"].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;

                        worksheet.Cells[$"H{currentRow}"].Value = topCustomers.Sum(c => c.TotalSpent);
                        worksheet.Cells[$"H{currentRow}"].Style.Numberformat.Format = "#,##0 ₫";
                        worksheet.Cells[$"H{currentRow}"].Style.Font.Bold = true;
                        worksheet.Cells[$"H{currentRow}"].Style.Font.Color.SetColor(Color.FromArgb(192, 0, 0));

                        using (var range = worksheet.Cells[$"A{currentRow}:H{currentRow}"])
                        {
                            range.Style.Fill.PatternType = ExcelFillStyle.Solid;
                            range.Style.Fill.BackgroundColor.SetColor(Color.FromArgb(217, 225, 242));
                            range.Style.Border.Top.Style = ExcelBorderStyle.Double;
                            range.Style.Border.Bottom.Style = ExcelBorderStyle.Double;
                        }
                    }
                    else
                    {
                        worksheet.Cells[$"A{currentRow}:H{currentRow}"].Merge = true;
                        worksheet.Cells[$"A{currentRow}"].Value = "Không có dữ liệu khách hàng";
                        worksheet.Cells[$"A{currentRow}"].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
                        worksheet.Cells[$"A{currentRow}"].Style.Font.Italic = true;
                        currentRow++;
                    }

                    // ===== FOOTER =====
                    currentRow += 2;
                    worksheet.Cells[$"A{currentRow}:H{currentRow}"].Merge = true;
                    worksheet.Cells[$"A{currentRow}"].Value = "© Fresh Farm - Hệ thống quản lý bán hàng";
                    worksheet.Cells[$"A{currentRow}"].Style.Font.Italic = true;
                    worksheet.Cells[$"A{currentRow}"].Style.Font.Size = 9;
                    worksheet.Cells[$"A{currentRow}"].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
                    worksheet.Cells[$"A{currentRow}"].Style.Font.Color.SetColor(Color.Gray);

                    // ===== TỰ ĐỘNG ĐIỀU CHỈNH ĐỘ RỘNG CỘT =====
                    worksheet.Column(1).Width = 8;   // Hạng
                    worksheet.Column(2).Width = 25;  // Tên
                    worksheet.Column(3).Width = 30;  // Email
                    worksheet.Column(4).Width = 15;  // Phone
                    worksheet.Column(5).Width = 35;  // Địa chỉ
                    worksheet.Column(6).Width = 15;  // Ngày đăng ký
                    worksheet.Column(7).Width = 12;  // Tổng đơn
                    worksheet.Column(8).Width = 18;  // Tổng chi tiêu

                    // Căn giữa toàn bộ worksheet theo chiều dọc
                    worksheet.Cells[worksheet.Dimension.Address].Style.VerticalAlignment = ExcelVerticalAlignment.Center;

                    // Xuất file
                    var fileContents = package.GetAsByteArray();
                    var fileName = $"BaoCaoKhachHang_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";

                    return File(fileContents,
                        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                        fileName);
                }
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = "Lỗi xuất Excel: " + ex.Message;
                return RedirectToAction("Customer", new { registerFromDate, registerToDate, customerSegment });
            }
        }

        // Phương thức Order đã được sửa lỗi
        public ActionResult Order(DateTime? fromDate, DateTime? toDate, string orderStatus)
        {
            var model = new OrderReportViewModel
            {
                FromDate = fromDate,
                ToDate = toDate,
                OrderStatus = orderStatus ?? "all"
            };

            try
            {
                // Lấy tất cả đơn hàng
                var orders = db.Orders.ToList();
                // --- Đồng bộ trạng thái đơn hàng dựa trên bảng Payment ---
                // --- Đồng bộ trạng thái đơn hàng dựa theo PaymentStatus ---
foreach (var order in orders)
{
    var payment = db.Payments.FirstOrDefault(p => p.OrderID == order.OrderID);
    if (payment != null)
    {
        switch (payment.PaymentStatus.Trim().ToLower())
        {
            case "đã thanh toán":
            case "hoàn tất":
            case "paid":
                order.Status = "Completed";
                break;

            case "chờ thanh toán":
            case "đang xử lý":
            case "pending":
                order.Status = "Pending";
                break;

            case "chưa thanh toán":
            case "failed":
            case "hủy":
            case "cancelled":
                order.Status = "Cancelled";
                break;

            default:
                // Nếu không rõ, giữ nguyên trạng thái cũ
                break;
        }
    }
}


                // Áp dụng bộ lọc ngày
                if (fromDate.HasValue)
                {
                    orders = orders.Where(o => o.OrderDate >= fromDate.Value).ToList();
                }
                if (toDate.HasValue)
                {
                    orders = orders.Where(o => o.OrderDate <= toDate.Value.AddDays(1)).ToList();
                }

                // --- Đồng bộ trạng thái với bảng Payment ---
                foreach (var order in orders)
                {
                    var payment = db.Payments.FirstOrDefault(p => p.OrderID == order.OrderID);
                    if (payment != null)
                    {
                        // Nếu PaymentStatus = "Đã thanh toán" → Completed
                        if (payment.PaymentStatus.Equals("Đã thanh toán", StringComparison.OrdinalIgnoreCase))
                        {
                            order.Status = "Completed";
                        }
                        // Nếu PaymentStatus = "Chờ thanh toán" → Pending
                        else if (payment.PaymentStatus.Equals("Chờ thanh toán", StringComparison.OrdinalIgnoreCase))
                        {
                            order.Status = "Pending";
                        }
                        // Nếu PaymentStatus = "Chưa thanh toán" → Cancelled (hoặc Pending tùy hệ thống)
                        else if (payment.PaymentStatus.Equals("Chưa thanh toán", StringComparison.OrdinalIgnoreCase))
                        {
                            order.Status = "Cancelled";
                        }
                    }
                }


                // Áp dụng bộ lọc trạng thái (sau khi đồng bộ)
                if (!string.IsNullOrEmpty(orderStatus) && orderStatus != "all")
                {
                    orders = orders.Where(o => o.Status.Equals(orderStatus, StringComparison.OrdinalIgnoreCase)).ToList();
                }


                // Tính toán thống kê tổng quan
                model.TotalOrders = orders.Count;
                model.SuccessOrders = orders.Count(o => o.Status.Equals("Completed", StringComparison.OrdinalIgnoreCase));
                model.CancelledOrders = orders.Count(o => o.Status.Equals("Cancelled", StringComparison.OrdinalIgnoreCase));
                model.CancelRate = model.TotalOrders > 0
                    ? (decimal)model.CancelledOrders / model.TotalOrders * 100
                    : 0;

                // Dữ liệu biểu đồ xu hướng (7 ngày gần nhất)
                var sevenDaysAgo = DateTime.Now.AddDays(-6).Date;
                for (int i = 6; i >= 0; i--)
                {
                    var date = DateTime.Now.AddDays(-i).Date;
                    string dayName = "";

                    switch (date.DayOfWeek)
                    {
                        case DayOfWeek.Monday:
                            dayName = "T2";
                            break;
                        case DayOfWeek.Tuesday:
                            dayName = "T3";
                            break;
                        case DayOfWeek.Wednesday:
                            dayName = "T4";
                            break;
                        case DayOfWeek.Thursday:
                            dayName = "T5";
                            break;
                        case DayOfWeek.Friday:
                            dayName = "T6";
                            break;
                        case DayOfWeek.Saturday:
                            dayName = "T7";
                            break;
                        case DayOfWeek.Sunday:
                            dayName = "CN";
                            break;
                    }

                    model.TrendLabels.Add(dayName);

                    var dayOrders = orders.Where(o => o.OrderDate.Date == date).ToList();
                    model.TrendTotalData.Add(dayOrders.Count);
                    model.TrendSuccessData.Add(dayOrders.Count(o => o.Status.Equals("Completed", StringComparison.OrdinalIgnoreCase)));
                    model.TrendCancelledData.Add(dayOrders.Count(o => o.Status.Equals("Cancelled", StringComparison.OrdinalIgnoreCase)));
                }

                // Dữ liệu biểu đồ trạng thái
                model.StatusSuccessCount = orders.Count(o => o.Status.Equals("Completed", StringComparison.OrdinalIgnoreCase));
                model.StatusShippingCount = orders.Count(o => o.Status.Equals("Shipped", StringComparison.OrdinalIgnoreCase));
                model.StatusPendingCount = orders.Count(o => o.Status.Equals("Pending", StringComparison.OrdinalIgnoreCase));
                model.StatusCancelledCount = orders.Count(o => o.Status.Equals("Cancelled", StringComparison.OrdinalIgnoreCase));

                // Lấy danh sách đơn hàng gần đây (10 đơn mới nhất)
                var recentOrders = orders.OrderByDescending(o => o.OrderDate).Take(10).ToList();

                foreach (var order in recentOrders)
                {
                    var user = db.Users.FirstOrDefault(u => u.UserID == order.UserID);
                    var statusInfo = GetOrderStatusInfo(order.Status);

                    model.RecentOrders.Add(new OrderReportItemViewModel
                    {
                        OrderID = order.OrderID,
                        OrderCode = "#" + order.OrderID,
                        CustomerName = user?.FullName ?? "N/A",
                        OrderDate = order.OrderDate,
                        OrderDateFormatted = order.OrderDate.ToString("dd/MM/yyyy"),
                        TotalAmount = order.TotalAmount,
                        Status = order.Status,
                        StatusBadgeClass = statusInfo.Item1,
                        StatusText = statusInfo.Item2
                    });
                }
            }
            catch (Exception ex)
            {
                ViewBag.ErrorMessage = "Lỗi: " + ex.Message;
            }

            return View(model);
        }

        // Action xuất Excel báo cáo đơn hàng
        public ActionResult ExportOrderExcel(DateTime? fromDate, DateTime? toDate, string orderStatus)
        {
            try
            {
                OfficeOpenXml.ExcelPackage.LicenseContext = OfficeOpenXml.LicenseContext.NonCommercial;

                // Lấy dữ liệu đơn hàng
                var orders = db.Orders.ToList();

                // Áp dụng bộ lọc
                if (fromDate.HasValue)
                {
                    orders = orders.Where(o => o.OrderDate >= fromDate.Value).ToList();
                }
                if (toDate.HasValue)
                {
                    orders = orders.Where(o => o.OrderDate <= toDate.Value.AddDays(1)).ToList();
                }
                if (!string.IsNullOrEmpty(orderStatus) && orderStatus != "all")
                {
                    string statusFilter = "";
                    switch (orderStatus.ToLower())
                    {
                        case "chờ xử lý":
                            statusFilter = "Pending";
                            break;
                        case "đang giao":
                            statusFilter = "Shipped";
                            break;
                        case "thành công":
                            statusFilter = "Completed";
                            break;
                        case "đã hủy":
                            statusFilter = "Cancelled";
                            break;
                    }
                    if (!string.IsNullOrEmpty(statusFilter))
                    {
                        orders = orders.Where(o => o.Status == statusFilter).ToList();
                    }
                }

                // Tính toán thống kê
                var totalOrders = orders.Count;
                var successOrders = orders.Count(o => o.Status == "Completed");
                var cancelledOrders = orders.Count(o => o.Status == "Cancelled");
                var cancelRate = totalOrders > 0 ? (decimal)cancelledOrders / totalOrders * 100 : 0;
                var totalRevenue = orders.Where(o => o.Status == "Completed").Sum(o => o.TotalAmount);

                using (var package = new ExcelPackage())
                {
                    var worksheet = package.Workbook.Worksheets.Add("Báo Cáo Đơn Hàng");

                    // TIÊU ĐỀ
                    worksheet.Cells["A1:I1"].Merge = true;
                    worksheet.Cells["A1"].Value = "BÁO CÁO ĐỜN HÀNG - FRESH FARM";
                    worksheet.Cells["A1"].Style.Font.Size = 18;
                    worksheet.Cells["A1"].Style.Font.Bold = true;
                    worksheet.Cells["A1"].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
                    worksheet.Cells["A1"].Style.Fill.PatternType = ExcelFillStyle.Solid;
                    worksheet.Cells["A1"].Style.Fill.BackgroundColor.SetColor(Color.FromArgb(79, 129, 189));
                    worksheet.Cells["A1"].Style.Font.Color.SetColor(Color.White);
                    worksheet.Row(1).Height = 30;

                    // THÔNG TIN BỘ LỌC
                    int currentRow = 2;
                    worksheet.Cells[$"A{currentRow}:I{currentRow}"].Merge = true;
                    worksheet.Cells[$"A{currentRow}"].Value = $"Ngày xuất: {DateTime.Now:dd/MM/yyyy HH:mm:ss}";
                    worksheet.Cells[$"A{currentRow}"].Style.Font.Italic = true;
                    currentRow++;

                    if (fromDate.HasValue || toDate.HasValue || !string.IsNullOrEmpty(orderStatus))
                    {
                        worksheet.Cells[$"A{currentRow}:I{currentRow}"].Merge = true;
                        var filterInfo = "Bộ lọc: ";
                        if (fromDate.HasValue)
                            filterInfo += $"Từ {fromDate.Value:dd/MM/yyyy} ";
                        if (toDate.HasValue)
                            filterInfo += $"đến {toDate.Value:dd/MM/yyyy} ";
                        if (!string.IsNullOrEmpty(orderStatus) && orderStatus != "all")
                            filterInfo += $"- Trạng thái: {orderStatus}";
                        worksheet.Cells[$"A{currentRow}"].Value = filterInfo;
                        worksheet.Cells[$"A{currentRow}"].Style.Font.Italic = true;
                        currentRow++;
                    }

                    // THỐNG KÊ TỔNG QUAN
                    currentRow++;
                    worksheet.Cells[$"A{currentRow}:I{currentRow}"].Merge = true;
                    worksheet.Cells[$"A{currentRow}"].Value = "THỐNG KÊ TỔNG QUAN";
                    worksheet.Cells[$"A{currentRow}"].Style.Font.Bold = true;
                    worksheet.Cells[$"A{currentRow}"].Style.Font.Size = 14;
                    worksheet.Cells[$"A{currentRow}"].Style.Fill.PatternType = ExcelFillStyle.Solid;
                    worksheet.Cells[$"A{currentRow}"].Style.Fill.BackgroundColor.SetColor(Color.FromArgb(242, 242, 242));
                    currentRow++;

                    var statsStartRow = currentRow;

                    worksheet.Cells[$"A{currentRow}"].Value = "Tổng đơn hàng:";
                    worksheet.Cells[$"A{currentRow}"].Style.Font.Bold = true;
                    worksheet.Cells[$"B{currentRow}"].Value = totalOrders;
                    worksheet.Cells[$"B{currentRow}"].Style.Font.Bold = true;
                    worksheet.Cells[$"B{currentRow}"].Style.Font.Color.SetColor(Color.FromArgb(0, 112, 192));

                    worksheet.Cells[$"D{currentRow}"].Value = "Đơn thành công:";
                    worksheet.Cells[$"D{currentRow}"].Style.Font.Bold = true;
                    worksheet.Cells[$"E{currentRow}"].Value = successOrders;
                    worksheet.Cells[$"E{currentRow}"].Style.Font.Color.SetColor(Color.FromArgb(0, 176, 80));
                    currentRow++;

                    worksheet.Cells[$"A{currentRow}"].Value = "Đơn bị hủy:";
                    worksheet.Cells[$"A{currentRow}"].Style.Font.Bold = true;
                    worksheet.Cells[$"B{currentRow}"].Value = cancelledOrders;
                    worksheet.Cells[$"B{currentRow}"].Style.Font.Color.SetColor(Color.FromArgb(192, 0, 0));

                    worksheet.Cells[$"D{currentRow}"].Value = "Tỷ lệ hủy:";
                    worksheet.Cells[$"D{currentRow}"].Style.Font.Bold = true;
                    worksheet.Cells[$"E{currentRow}"].Value = $"{cancelRate:F2}%";
                    worksheet.Cells[$"E{currentRow}"].Style.Font.Color.SetColor(Color.FromArgb(255, 192, 0));
                    currentRow++;

                    worksheet.Cells[$"A{currentRow}"].Value = "Tổng doanh thu:";
                    worksheet.Cells[$"A{currentRow}"].Style.Font.Bold = true;
                    worksheet.Cells[$"B{currentRow}"].Value = totalRevenue;
                    worksheet.Cells[$"B{currentRow}"].Style.Numberformat.Format = "#,##0 ₫";
                    worksheet.Cells[$"B{currentRow}"].Style.Font.Bold = true;
                    worksheet.Cells[$"B{currentRow}"].Style.Font.Color.SetColor(Color.FromArgb(0, 176, 80));
                    currentRow++;

                    using (var range = worksheet.Cells[$"A{statsStartRow}:E{currentRow - 1}"])
                    {
                        range.Style.Border.Top.Style = ExcelBorderStyle.Thin;
                        range.Style.Border.Left.Style = ExcelBorderStyle.Thin;
                        range.Style.Border.Right.Style = ExcelBorderStyle.Thin;
                        range.Style.Border.Bottom.Style = ExcelBorderStyle.Thin;
                    }

                    // BẢNG CHI TIẾT ĐƠN HÀNG
                    currentRow += 2;
                    worksheet.Cells[$"A{currentRow}:I{currentRow}"].Merge = true;
                    worksheet.Cells[$"A{currentRow}"].Value = "CHI TIẾT ĐƠN HÀNG";
                    worksheet.Cells[$"A{currentRow}"].Style.Font.Bold = true;
                    worksheet.Cells[$"A{currentRow}"].Style.Font.Size = 14;
                    worksheet.Cells[$"A{currentRow}"].Style.Fill.PatternType = ExcelFillStyle.Solid;
                    worksheet.Cells[$"A{currentRow}"].Style.Fill.BackgroundColor.SetColor(Color.FromArgb(242, 242, 242));
                    currentRow++;

                    // Header bảng
                    int headerRow = currentRow;
                    worksheet.Cells[$"A{headerRow}"].Value = "Mã ĐH";
                    worksheet.Cells[$"B{headerRow}"].Value = "Khách hàng";
                    worksheet.Cells[$"C{headerRow}"].Value = "Email";
                    worksheet.Cells[$"D{headerRow}"].Value = "Số điện thoại";
                    worksheet.Cells[$"E{headerRow}"].Value = "Ngày đặt";
                    worksheet.Cells[$"F{headerRow}"].Value = "Phí ship";
                    worksheet.Cells[$"G{headerRow}"].Value = "Tổng tiền";
                    worksheet.Cells[$"H{headerRow}"].Value = "Trạng thái";
                    worksheet.Cells[$"I{headerRow}"].Value = "Ghi chú";

                    using (var range = worksheet.Cells[$"A{headerRow}:I{headerRow}"])
                    {
                        range.Style.Font.Bold = true;
                        range.Style.Fill.PatternType = ExcelFillStyle.Solid;
                        range.Style.Fill.BackgroundColor.SetColor(Color.FromArgb(217, 225, 242));
                        range.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
                        range.Style.Border.Top.Style = ExcelBorderStyle.Thin;
                        range.Style.Border.Left.Style = ExcelBorderStyle.Thin;
                        range.Style.Border.Right.Style = ExcelBorderStyle.Thin;
                        range.Style.Border.Bottom.Style = ExcelBorderStyle.Thin;
                    }
                    worksheet.Row(headerRow).Height = 20;

                    // Dữ liệu đơn hàng
                    currentRow++;
                    int dataStartRow = currentRow;
                    var ordersWithUser = orders.OrderByDescending(o => o.OrderDate).ToList();

                    foreach (var order in ordersWithUser)
                    {
                        var user = db.Users.FirstOrDefault(u => u.UserID == order.UserID);
                        var statusInfo = GetOrderStatusInfo(order.Status);

                        worksheet.Cells[$"A{currentRow}"].Value = $"#{GenerateOrderCode(order.OrderID)}";
                        worksheet.Cells[$"B{currentRow}"].Value = user?.FullName ?? "N/A";
                        worksheet.Cells[$"C{currentRow}"].Value = user?.Email ?? "N/A";
                        worksheet.Cells[$"D{currentRow}"].Value = user?.Phone ?? "N/A";
                        worksheet.Cells[$"E{currentRow}"].Value = order.OrderDate.ToString("dd/MM/yyyy");
                        worksheet.Cells[$"E{currentRow}"].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;

                        worksheet.Cells[$"F{currentRow}"].Value = order.ShippingFee;
                        worksheet.Cells[$"F{currentRow}"].Style.Numberformat.Format = "#,##0 ₫";

                        worksheet.Cells[$"G{currentRow}"].Value = order.TotalAmount;
                        worksheet.Cells[$"G{currentRow}"].Style.Numberformat.Format = "#,##0 ₫";
                        worksheet.Cells[$"G{currentRow}"].Style.Font.Bold = true;

                        worksheet.Cells[$"H{currentRow}"].Value = statusInfo.Item2;
                        worksheet.Cells[$"H{currentRow}"].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;

                        // Tô màu theo trạng thái
                        Color statusColor;
                        switch (order.Status)
                        {
                            case "Completed":
                                statusColor = Color.FromArgb(198, 239, 206);
                                break;
                            case "Shipped":
                                statusColor = Color.FromArgb(217, 234, 250);
                                break;
                            case "Pending":
                                statusColor = Color.FromArgb(255, 243, 205);
                                break;
                            case "Cancelled":
                                statusColor = Color.FromArgb(255, 199, 206);
                                break;
                            default:
                                statusColor = Color.White;
                                break;
                        }
                        worksheet.Cells[$"H{currentRow}"].Style.Fill.PatternType = ExcelFillStyle.Solid;
                        worksheet.Cells[$"H{currentRow}"].Style.Fill.BackgroundColor.SetColor(statusColor);

                        worksheet.Cells[$"I{currentRow}"].Value = order.OrderNote ?? "";

                        currentRow++;
                    }

                    // Format bảng dữ liệu
                    if (ordersWithUser.Any())
                    {
                        using (var range = worksheet.Cells[$"A{dataStartRow}:I{currentRow - 1}"])
                        {
                            range.Style.Border.Top.Style = ExcelBorderStyle.Thin;
                            range.Style.Border.Left.Style = ExcelBorderStyle.Thin;
                            range.Style.Border.Right.Style = ExcelBorderStyle.Thin;
                            range.Style.Border.Bottom.Style = ExcelBorderStyle.Thin;
                        }
                    }
                    else
                    {
                        worksheet.Cells[$"A{currentRow}:I{currentRow}"].Merge = true;
                        worksheet.Cells[$"A{currentRow}"].Value = "Không có dữ liệu đơn hàng";
                        worksheet.Cells[$"A{currentRow}"].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
                        worksheet.Cells[$"A{currentRow}"].Style.Font.Italic = true;
                        currentRow++;
                    }

                    // FOOTER
                    currentRow += 2;
                    worksheet.Cells[$"A{currentRow}:I{currentRow}"].Merge = true;
                    worksheet.Cells[$"A{currentRow}"].Value = "© Fresh Farm - Hệ thống quản lý bán hàng";
                    worksheet.Cells[$"A{currentRow}"].Style.Font.Italic = true;
                    worksheet.Cells[$"A{currentRow}"].Style.Font.Size = 9;
                    worksheet.Cells[$"A{currentRow}"].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
                    worksheet.Cells[$"A{currentRow}"].Style.Font.Color.SetColor(Color.Gray);

                    // Điều chỉnh độ rộng cột
                    worksheet.Column(1).Width = 12;  // Mã ĐH
                    worksheet.Column(2).Width = 25;  // Khách hàng
                    worksheet.Column(3).Width = 30;  // Email
                    worksheet.Column(4).Width = 15;  // Phone
                    worksheet.Column(5).Width = 13;  // Ngày đặt
                    worksheet.Column(6).Width = 12;  // Phí ship
                    worksheet.Column(7).Width = 15;  // Tổng tiền
                    worksheet.Column(8).Width = 15;  // Trạng thái
                    worksheet.Column(9).Width = 30;  // Ghi chú

                    worksheet.Cells[worksheet.Dimension.Address].Style.VerticalAlignment = ExcelVerticalAlignment.Center;

                    var fileContents = package.GetAsByteArray();
                    var fileName = $"BaoCaoDonHang_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";

                    return File(fileContents,
                        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                        fileName);
                }
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = "Lỗi xuất Excel: " + ex.Message;
                return RedirectToAction("Order", new { fromDate, toDate, orderStatus });
            }
        }

        // Helper method: Lấy mã đơn hàng thực tế
        private string GenerateOrderCode(int orderId)
        {
            // Vì DB không có cột OrderCode, nên ta hiển thị # + ID
            return $"#{orderId}";
        }


        // Helper method: Lấy thông tin hiển thị trạng thái đơn hàng
        private Tuple<string, string> GetOrderStatusInfo(string status)
        {
            if (string.IsNullOrEmpty(status))
                return Tuple.Create("bg-secondary", "Không xác định");

            status = status.Trim().ToLower();

            switch (status)
            {
                case "completed":
                case "đã thanh toán":
                case "hoàn tất":
                case "paid":
                    return Tuple.Create("bg-success", "Đã thanh toán");

                case "shipped":
                case "đang giao":
                    return Tuple.Create("bg-info", "Đang giao");

                case "pending":
                case "chờ thanh toán":
                case "chờ xử lý":
                    return Tuple.Create("bg-warning text-dark", "Chờ thanh toán");

                case "cancelled":
                case "đã hủy":
                case "failed":
                case "chưa thanh toán":
                    return Tuple.Create("bg-danger", "Đã hủy");

                default:
                    return Tuple.Create("bg-secondary", "Không xác định");
            }
        }


        // --- BÁO CÁO DOANH THU ---
        public ActionResult Revenue(DateTime? fromDate, DateTime? toDate, string viewBy = "day")
        {
            var model = new RevenueReportViewModel
            {
                FromDate = fromDate,
                ToDate = toDate,
                ViewBy = viewBy
            };

            // Lọc ngày
            DateTime start = fromDate ?? DateTime.Today.AddDays(-7);
            DateTime end = toDate ?? DateTime.Today;

            // Lấy đơn hàng có PaymentStatus = "Đã thanh toán" hoặc "Hoàn tất"
            var paidOrders = from o in db.Orders
                             join p in db.Payments on o.OrderID equals p.OrderID
                             where p.PaymentStatus == "Đã thanh toán" || p.PaymentStatus == "Hoàn tất"
                             && o.OrderDate >= start && o.OrderDate <= end
                             select new
                             {
                                 o.OrderID,
                                 o.OrderDate,
                                 o.TotalAmount
                             };

            // --- Tổng quan ---
            model.TotalOrders = paidOrders.Count();
            model.TotalRevenue = paidOrders.Sum(o => (decimal?)o.TotalAmount) ?? 0;
            model.AverageOrderValue = model.TotalOrders > 0 ? model.TotalRevenue / model.TotalOrders : 0;
            model.EstimatedProfit = model.TotalRevenue * 0.2m; // giả định 20% lợi nhuận

            // --- Biểu đồ xu hướng ---
            var grouped = paidOrders
            .GroupBy(o => DbFunctions.TruncateTime(o.OrderDate))
            .Select(g => new
            {
                Date = g.Key.Value,
                Revenue = g.Sum(x => x.TotalAmount),
                Orders = g.Count()
            })
            .OrderBy(g => g.Date)
            .ToList();

            foreach (var g in grouped)
            {
                model.TrendLabels.Add(g.Date.ToString("dd/MM"));
                model.TrendData.Add(g.Revenue);

                model.DailyRevenues.Add(new DailyRevenueViewModel
                {
                    Date = g.Date,
                    DateFormatted = g.Date.ToString("dd/MM/yyyy"),
                    TotalOrders = g.Orders,
                    TotalProducts = db.OrderDetails
                        .Where(d => d.OrderID == g.Date.Day) // chỉ để có ví dụ
                        .Sum(d => (int?)d.Quantity) ?? 0,
                    Revenue = g.Revenue,
                    Profit = g.Revenue * 0.2m
                });
            }

            // --- Top sản phẩm theo doanh thu ---
            var topProducts = (from d in db.OrderDetails
                               join o in db.Orders on d.OrderID equals o.OrderID
                               join p in db.Payments on o.OrderID equals p.OrderID
                               join pr in db.Products on d.ProductID equals pr.ProductID
                               where p.PaymentStatus == "Đã thanh toán" || p.PaymentStatus == "Hoàn tất"
                               && o.OrderDate >= start && o.OrderDate <= end
                               group new { d, pr } by new { d.ProductID, pr.ProductName } into g
                               orderby g.Sum(x => x.d.Quantity * x.d.UnitPrice) descending
                               select new TopProductRevenueViewModel
                               {
                                   ProductID = g.Key.ProductID,
                                   ProductName = g.Key.ProductName,
                                   TotalQuantity = g.Sum(x => x.d.Quantity),
                                   TotalRevenue = g.Sum(x => x.d.Quantity * x.d.UnitPrice)
                               }).Take(5).ToList();

            model.TopProducts = topProducts;

            return View(model);
        }

        // GET: Admin/Report/ExportRevenueExcel
        public ActionResult ExportRevenueExcel(DateTime? fromDate, DateTime? toDate, string viewBy)
        {
            try
            {
                OfficeOpenXml.ExcelPackage.LicenseContext = OfficeOpenXml.LicenseContext.NonCommercial;

                // Lấy dữ liệu đơn hàng đã hoàn thành
                var completedOrders = db.Orders
                    .Where(o => o.Status == "Completed")
                    .ToList();

                // Áp dụng bộ lọc
                if (fromDate.HasValue)
                {
                    completedOrders = completedOrders.Where(o => o.OrderDate >= fromDate.Value).ToList();
                }
                if (toDate.HasValue)
                {
                    completedOrders = completedOrders.Where(o => o.OrderDate <= toDate.Value.AddDays(1)).ToList();
                }

                // Tính toán thống kê
                var totalRevenue = completedOrders.Sum(o => o.TotalAmount);
                var totalOrders = completedOrders.Count;
                var avgOrderValue = totalOrders > 0 ? totalRevenue / totalOrders : 0;
                var estimatedProfit = totalRevenue * 0.3m;

                // Top sản phẩm
                var completedOrderIds = completedOrders.Select(o => o.OrderID).ToList();
                var orderDetails = db.OrderDetails
                    .Where(od => completedOrderIds.Contains(od.OrderID))
                    .ToList();

                var topProducts = orderDetails
                    .GroupBy(od => od.ProductID)
                    .Select(g => new
                    {
                        ProductID = g.Key,
                        Product = db.Products.FirstOrDefault(p => p.ProductID == g.Key),
                        TotalRevenue = g.Sum(od => od.Quantity * od.UnitPrice),
                        TotalQuantity = g.Sum(od => od.Quantity)
                    })
                    .Where(p => p.Product != null)
                    .OrderByDescending(p => p.TotalRevenue)
                    .Take(10)
                    .ToList();

                // Chi tiết doanh thu theo ngày
                var dailyRevenues = completedOrders
                    .GroupBy(o => o.OrderDate.Date)
                    .Select(g => new
                    {
                        Date = g.Key,
                        TotalOrders = g.Count(),
                        Revenue = g.Sum(o => o.TotalAmount),
                        OrderIDs = g.Select(o => o.OrderID).ToList()
                    })
                    .OrderByDescending(d => d.Date)
                    .ToList();

                using (var package = new ExcelPackage())
                {
                    var worksheet = package.Workbook.Worksheets.Add("Báo Cáo Doanh Thu");

                    // TIÊU ĐỀ
                    worksheet.Cells["A1:H1"].Merge = true;
                    worksheet.Cells["A1"].Value = "BÁO CÁO DOANH THU - FRESH FARM";
                    worksheet.Cells["A1"].Style.Font.Size = 18;
                    worksheet.Cells["A1"].Style.Font.Bold = true;
                    worksheet.Cells["A1"].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
                    worksheet.Cells["A1"].Style.Fill.PatternType = ExcelFillStyle.Solid;
                    worksheet.Cells["A1"].Style.Fill.BackgroundColor.SetColor(Color.FromArgb(79, 129, 189));
                    worksheet.Cells["A1"].Style.Font.Color.SetColor(Color.White);
                    worksheet.Row(1).Height = 30;

                    // THÔNG TIN BỘ LỌC
                    int currentRow = 2;
                    worksheet.Cells[$"A{currentRow}:H{currentRow}"].Merge = true;
                    worksheet.Cells[$"A{currentRow}"].Value = $"Ngày xuất: {DateTime.Now:dd/MM/yyyy HH:mm:ss}";
                    worksheet.Cells[$"A{currentRow}"].Style.Font.Italic = true;
                    currentRow++;

                    if (fromDate.HasValue || toDate.HasValue)
                    {
                        worksheet.Cells[$"A{currentRow}:H{currentRow}"].Merge = true;
                        var filterInfo = "Bộ lọc: ";
                        if (fromDate.HasValue)
                            filterInfo += $"Từ {fromDate.Value:dd/MM/yyyy} ";
                        if (toDate.HasValue)
                            filterInfo += $"đến {toDate.Value:dd/MM/yyyy}";
                        worksheet.Cells[$"A{currentRow}"].Value = filterInfo;
                        worksheet.Cells[$"A{currentRow}"].Style.Font.Italic = true;
                        currentRow++;
                    }

                    // THỐNG KÊ TỔNG QUAN
                    currentRow++;
                    worksheet.Cells[$"A{currentRow}:H{currentRow}"].Merge = true;
                    worksheet.Cells[$"A{currentRow}"].Value = "THỐNG KÊ TỔNG QUAN";
                    worksheet.Cells[$"A{currentRow}"].Style.Font.Bold = true;
                    worksheet.Cells[$"A{currentRow}"].Style.Font.Size = 14;
                    worksheet.Cells[$"A{currentRow}"].Style.Fill.PatternType = ExcelFillStyle.Solid;
                    worksheet.Cells[$"A{currentRow}"].Style.Fill.BackgroundColor.SetColor(Color.FromArgb(242, 242, 242));
                    currentRow++;

                    var statsStartRow = currentRow;

                    worksheet.Cells[$"A{currentRow}"].Value = "Tổng doanh thu:";
                    worksheet.Cells[$"A{currentRow}"].Style.Font.Bold = true;
                    worksheet.Cells[$"B{currentRow}"].Value = totalRevenue;
                    worksheet.Cells[$"B{currentRow}"].Style.Numberformat.Format = "#,##0 ₫";
                    worksheet.Cells[$"B{currentRow}"].Style.Font.Bold = true;
                    worksheet.Cells[$"B{currentRow}"].Style.Font.Color.SetColor(Color.FromArgb(0, 138, 79));

                    worksheet.Cells[$"D{currentRow}"].Value = "Tổng đơn hàng:";
                    worksheet.Cells[$"D{currentRow}"].Style.Font.Bold = true;
                    worksheet.Cells[$"E{currentRow}"].Value = totalOrders;
                    worksheet.Cells[$"E{currentRow}"].Style.Font.Color.SetColor(Color.FromArgb(0, 112, 192));
                    currentRow++;

                    worksheet.Cells[$"A{currentRow}"].Value = "Giá trị đơn trung bình:";
                    worksheet.Cells[$"A{currentRow}"].Style.Font.Bold = true;
                    worksheet.Cells[$"B{currentRow}"].Value = avgOrderValue;
                    worksheet.Cells[$"B{currentRow}"].Style.Numberformat.Format = "#,##0 ₫";
                    worksheet.Cells[$"B{currentRow}"].Style.Font.Color.SetColor(Color.FromArgb(23, 162, 184));

                    worksheet.Cells[$"D{currentRow}"].Value = "Lợi nhuận ước tính:";
                    worksheet.Cells[$"D{currentRow}"].Style.Font.Bold = true;
                    worksheet.Cells[$"E{currentRow}"].Value = estimatedProfit;
                    worksheet.Cells[$"E{currentRow}"].Style.Numberformat.Format = "#,##0 ₫";
                    worksheet.Cells[$"E{currentRow}"].Style.Font.Color.SetColor(Color.FromArgb(255, 193, 7));
                    currentRow++;

                    using (var range = worksheet.Cells[$"A{statsStartRow}:E{currentRow - 1}"])
                    {
                        range.Style.Border.Top.Style = ExcelBorderStyle.Thin;
                        range.Style.Border.Left.Style = ExcelBorderStyle.Thin;
                        range.Style.Border.Right.Style = ExcelBorderStyle.Thin;
                        range.Style.Border.Bottom.Style = ExcelBorderStyle.Thin;
                    }

                    // TOP SẢN PHẨM
                    currentRow += 2;
                    worksheet.Cells[$"A{currentRow}:H{currentRow}"].Merge = true;
                    worksheet.Cells[$"A{currentRow}"].Value = "TOP 10 SẢN PHẨM THEO DOANH THU";
                    worksheet.Cells[$"A{currentRow}"].Style.Font.Bold = true;
                    worksheet.Cells[$"A{currentRow}"].Style.Font.Size = 14;
                    worksheet.Cells[$"A{currentRow}"].Style.Fill.PatternType = ExcelFillStyle.Solid;
                    worksheet.Cells[$"A{currentRow}"].Style.Fill.BackgroundColor.SetColor(Color.FromArgb(242, 242, 242));
                    currentRow++;

                    int headerRow = currentRow;
                    worksheet.Cells[$"A{headerRow}"].Value = "STT";
                    worksheet.Cells[$"B{headerRow}"].Value = "Tên sản phẩm";
                    worksheet.Cells[$"C{headerRow}"].Value = "Mã SKU";
                    worksheet.Cells[$"D{headerRow}"].Value = "Số lượng bán";
                    worksheet.Cells[$"E{headerRow}"].Value = "Doanh thu";

                    using (var range = worksheet.Cells[$"A{headerRow}:E{headerRow}"])
                    {
                        range.Style.Font.Bold = true;
                        range.Style.Fill.PatternType = ExcelFillStyle.Solid;
                        range.Style.Fill.BackgroundColor.SetColor(Color.FromArgb(217, 225, 242));
                        range.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
                        range.Style.Border.Top.Style = ExcelBorderStyle.Thin;
                        range.Style.Border.Left.Style = ExcelBorderStyle.Thin;
                        range.Style.Border.Right.Style = ExcelBorderStyle.Thin;
                        range.Style.Border.Bottom.Style = ExcelBorderStyle.Thin;
                    }
                    worksheet.Row(headerRow).Height = 20;

                    currentRow++;
                    int dataStartRow = currentRow;
                    int rank = 1;

                    foreach (var product in topProducts)
                    {
                        worksheet.Cells[$"A{currentRow}"].Value = rank;
                        worksheet.Cells[$"A{currentRow}"].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;

                        worksheet.Cells[$"B{currentRow}"].Value = product.Product.ProductName;
                        worksheet.Cells[$"C{currentRow}"].Value = product.Product.Sku ?? "N/A";
                        worksheet.Cells[$"C{currentRow}"].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;

                        worksheet.Cells[$"D{currentRow}"].Value = product.TotalQuantity;
                        worksheet.Cells[$"D{currentRow}"].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;

                        worksheet.Cells[$"E{currentRow}"].Value = product.TotalRevenue;
                        worksheet.Cells[$"E{currentRow}"].Style.Numberformat.Format = "#,##0 ₫";
                        worksheet.Cells[$"E{currentRow}"].Style.Font.Bold = true;

                        if (rank <= 3)
                        {
                            using (var range = worksheet.Cells[$"A{currentRow}:E{currentRow}"])
                            {
                                range.Style.Fill.PatternType = ExcelFillStyle.Solid;
                                range.Style.Fill.BackgroundColor.SetColor(Color.FromArgb(255, 242, 204));
                            }
                        }

                        rank++;
                        currentRow++;
                    }

                    if (topProducts.Any())
                    {
                        using (var range = worksheet.Cells[$"A{dataStartRow}:E{currentRow - 1}"])
                        {
                            range.Style.Border.Top.Style = ExcelBorderStyle.Thin;
                            range.Style.Border.Left.Style = ExcelBorderStyle.Thin;
                            range.Style.Border.Right.Style = ExcelBorderStyle.Thin;
                            range.Style.Border.Bottom.Style = ExcelBorderStyle.Thin;
                        }
                    }

                    // CHI TIẾT DOANH THU THEO NGÀY
                    currentRow += 2;
                    worksheet.Cells[$"A{currentRow}:H{currentRow}"].Merge = true;
                    worksheet.Cells[$"A{currentRow}"].Value = "CHI TIẾT DOANH THU THEO NGÀY";
                    worksheet.Cells[$"A{currentRow}"].Style.Font.Bold = true;
                    worksheet.Cells[$"A{currentRow}"].Style.Font.Size = 14;
                    worksheet.Cells[$"A{currentRow}"].Style.Fill.PatternType = ExcelFillStyle.Solid;
                    worksheet.Cells[$"A{currentRow}"].Style.Fill.BackgroundColor.SetColor(Color.FromArgb(242, 242, 242));
                    currentRow++;

                    headerRow = currentRow;
                    worksheet.Cells[$"A{headerRow}"].Value = "Ngày";
                    worksheet.Cells[$"B{headerRow}"].Value = "Tổng đơn";
                    worksheet.Cells[$"C{headerRow}"].Value = "Sản phẩm bán ra";
                    worksheet.Cells[$"D{headerRow}"].Value = "Doanh thu";
                    worksheet.Cells[$"E{headerRow}"].Value = "Lợi nhuận";

                    using (var range = worksheet.Cells[$"A{headerRow}:E{headerRow}"])
                    {
                        range.Style.Font.Bold = true;
                        range.Style.Fill.PatternType = ExcelFillStyle.Solid;
                        range.Style.Fill.BackgroundColor.SetColor(Color.FromArgb(217, 225, 242));
                        range.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
                        range.Style.Border.Top.Style = ExcelBorderStyle.Thin;
                        range.Style.Border.Left.Style = ExcelBorderStyle.Thin;
                        range.Style.Border.Right.Style = ExcelBorderStyle.Thin;
                        range.Style.Border.Bottom.Style = ExcelBorderStyle.Thin;
                    }
                    worksheet.Row(headerRow).Height = 20;

                    currentRow++;
                    dataStartRow = currentRow;

                    foreach (var daily in dailyRevenues)
                    {
                        var productsCount = orderDetails
                            .Where(od => daily.OrderIDs.Contains(od.OrderID))
                            .Sum(od => od.Quantity);

                        worksheet.Cells[$"A{currentRow}"].Value = daily.Date.ToString("dd/MM/yyyy");
                        worksheet.Cells[$"A{currentRow}"].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;

                        worksheet.Cells[$"B{currentRow}"].Value = daily.TotalOrders;
                        worksheet.Cells[$"B{currentRow}"].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;

                        worksheet.Cells[$"C{currentRow}"].Value = productsCount;
                        worksheet.Cells[$"C{currentRow}"].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;

                        worksheet.Cells[$"D{currentRow}"].Value = daily.Revenue;
                        worksheet.Cells[$"D{currentRow}"].Style.Numberformat.Format = "#,##0 ₫";

                        worksheet.Cells[$"E{currentRow}"].Value = daily.Revenue * 0.3m;
                        worksheet.Cells[$"E{currentRow}"].Style.Numberformat.Format = "#,##0 ₫";
                        worksheet.Cells[$"E{currentRow}"].Style.Font.Color.SetColor(Color.FromArgb(0, 176, 80));

                        currentRow++;
                    }

                    if (dailyRevenues.Any())
                    {
                        using (var range = worksheet.Cells[$"A{dataStartRow}:E{currentRow - 1}"])
                        {
                            range.Style.Border.Top.Style = ExcelBorderStyle.Thin;
                            range.Style.Border.Left.Style = ExcelBorderStyle.Thin;
                            range.Style.Border.Right.Style = ExcelBorderStyle.Thin;
                            range.Style.Border.Bottom.Style = ExcelBorderStyle.Thin;
                        }

                        // Tổng cộng
                        currentRow++;
                        worksheet.Cells[$"A{currentRow}:C{currentRow}"].Merge = true;
                        worksheet.Cells[$"A{currentRow}"].Value = "TỔNG CỘNG";
                        worksheet.Cells[$"A{currentRow}"].Style.Font.Bold = true;
                        worksheet.Cells[$"A{currentRow}"].Style.HorizontalAlignment = ExcelHorizontalAlignment.Right;

                        worksheet.Cells[$"D{currentRow}"].Value = dailyRevenues.Sum(d => d.Revenue);
                        worksheet.Cells[$"D{currentRow}"].Style.Numberformat.Format = "#,##0 ₫";
                        worksheet.Cells[$"D{currentRow}"].Style.Font.Bold = true;

                        worksheet.Cells[$"E{currentRow}"].Value = dailyRevenues.Sum(d => d.Revenue) * 0.3m;
                        worksheet.Cells[$"E{currentRow}"].Style.Numberformat.Format = "#,##0 ₫";
                        worksheet.Cells[$"E{currentRow}"].Style.Font.Bold = true;

                        using (var range = worksheet.Cells[$"A{currentRow}:E{currentRow}"])
                        {
                            range.Style.Fill.PatternType = ExcelFillStyle.Solid;
                            range.Style.Fill.BackgroundColor.SetColor(Color.FromArgb(217, 225, 242));
                            range.Style.Border.Top.Style = ExcelBorderStyle.Double;
                            range.Style.Border.Bottom.Style = ExcelBorderStyle.Double;
                        }
                    }

                    // FOOTER
                    currentRow += 2;
                    worksheet.Cells[$"A{currentRow}:H{currentRow}"].Merge = true;
                    worksheet.Cells[$"A{currentRow}"].Value = "© Fresh Farm - Hệ thống quản lý bán hàng";
                    worksheet.Cells[$"A{currentRow}"].Style.Font.Italic = true;
                    worksheet.Cells[$"A{currentRow}"].Style.Font.Size = 9;
                    worksheet.Cells[$"A{currentRow}"].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
                    worksheet.Cells[$"A{currentRow}"].Style.Font.Color.SetColor(Color.Gray);

                    // Điều chỉnh độ rộng cột
                    worksheet.Column(1).Width = 15;
                    worksheet.Column(2).Width = 35;
                    worksheet.Column(3).Width = 15;
                    worksheet.Column(4).Width = 18;
                    worksheet.Column(5).Width = 20;

                    worksheet.Cells[worksheet.Dimension.Address].Style.VerticalAlignment = ExcelVerticalAlignment.Center;

                    var fileContents = package.GetAsByteArray();
                    var fileName = $"BaoCaoDoanhThu_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";

                    return File(fileContents,
                        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                        fileName);
                }
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = "Lỗi xuất Excel: " + ex.Message;
                return RedirectToAction("Revenue", new { fromDate, toDate, viewBy });
            }
        }
        // ==========================
        // 📦 BÁO CÁO SẢN PHẨM (CHỈ TÍNH ĐƠN ĐÃ THANH TOÁN)
        // ==========================
        public ActionResult Product(DateTime? fromDate, DateTime? toDate, string productCategory, int page = 1)
        {
            int pageSize = 10;
            var model = new ProductReportViewModel
            {
                FromDate = fromDate,
                ToDate = toDate,
                ProductCategory = productCategory ?? "all",
                CurrentPage = page,
                PageSize = pageSize
            };

            try
            {
                // 🔹 1. Lấy các đơn hàng đã thanh toán hoặc hoàn tất
                var paidOrders = (from o in db.Orders
                                  join p in db.Payments on o.OrderID equals p.OrderID
                                  where p.PaymentStatus == "Đã thanh toán" || p.PaymentStatus == "Hoàn tất"
                                  select o).ToList();

                // 🔹 2. Áp dụng bộ lọc ngày (nếu có)
                if (fromDate.HasValue)
                {
                    paidOrders = paidOrders.Where(o => o.OrderDate >= fromDate.Value).ToList();
                }
                if (toDate.HasValue)
                {
                    paidOrders = paidOrders.Where(o => o.OrderDate <= toDate.Value.AddDays(1)).ToList();
                }

                // 🔹 3. Lấy chi tiết đơn hàng của các đơn đã thanh toán
                var paidOrderIds = paidOrders.Select(o => o.OrderID).ToList();
                var orderDetails = db.OrderDetails
                    .Where(od => paidOrderIds.Contains(od.OrderID))
                    .ToList();

                // 🔹 4. Lấy danh sách sản phẩm
                var allProducts = db.Products.ToList();

                // 🔹 5. Áp dụng bộ lọc danh mục sản phẩm
                if (!string.IsNullOrEmpty(productCategory) && productCategory != "all")
                {
                    var category = db.Categories.FirstOrDefault(c => c.CategoryName == productCategory);
                    if (category != null)
                    {
                        allProducts = allProducts.Where(p => p.CategoryId == category.CategoryID).ToList();
                    }
                }

                // 🔹 6. Tính toán thống kê sản phẩm bán được (chỉ từ đơn đã thanh toán)
                var productSales = orderDetails
                    .Where(od => allProducts.Any(p => p.ProductID == od.ProductID))
                    .GroupBy(od => od.ProductID)
                    .Select(g => new
                    {
                        ProductID = g.Key,
                        QuantitySold = g.Sum(od => od.Quantity),
                        TotalRevenue = g.Sum(od => od.Quantity * od.UnitPrice)
                    })
                    .ToList();

                // 🔹 7. Thống kê tổng quan
                var bestSelling = productSales.OrderByDescending(p => p.QuantitySold).FirstOrDefault();
                if (bestSelling != null)
                {
                    var product = allProducts.FirstOrDefault(p => p.ProductID == bestSelling.ProductID);
                    model.BestSellingProduct = product?.ProductName ?? "N/A";
                }

                model.TotalProductsSold = productSales.Sum(p => p.QuantitySold);
                model.AverageRevenuePerProduct = productSales.Any() ? productSales.Average(p => p.TotalRevenue) : 0;
                model.LowStockProducts = allProducts.Count(p => p.StockQuantity < 50);

                // 🔹 8. Top 10 sản phẩm bán chạy
                var top10Products = productSales
                    .OrderByDescending(p => p.QuantitySold)
                    .Take(10)
                    .ToList();

                foreach (var item in top10Products)
                {
                    var product = allProducts.FirstOrDefault(p => p.ProductID == item.ProductID);
                    if (product != null)
                    {
                        model.TopSellingProducts.Add(new TopSellingProductViewModel
                        {
                            ProductName = product.ProductName,
                            QuantitySold = item.QuantitySold
                        });
                    }
                }

                // 🔹 9. Tỷ trọng doanh thu theo danh mục (chỉ đơn đã thanh toán)
                var categoryRevenues = orderDetails
                    .Join(allProducts, od => od.ProductID, p => p.ProductID, (od, p) => new { od, p })
                    .Join(db.Categories, x => x.p.CategoryId, c => c.CategoryID, (x, c) => new
                    {
                        CategoryName = c.CategoryName,
                        Revenue = x.od.Quantity * x.od.UnitPrice
                    })
                    .GroupBy(x => x.CategoryName)
                    .Select(g => new CategoryRevenueViewModel
                    {
                        CategoryName = g.Key,
                        Revenue = g.Sum(x => x.Revenue)
                    })
                    .ToList();

                model.CategoryRevenues = categoryRevenues;

                // 🔹 10. Hiệu suất từng sản phẩm (có phân trang)
                var productPerformances = new List<ProductPerformanceViewModel>();

                foreach (var product in allProducts)
                {
                    var category = db.Categories.FirstOrDefault(c => c.CategoryID == product.CategoryId);
                    var sales = productSales.FirstOrDefault(p => p.ProductID == product.ProductID);

                    int quantitySold = sales?.QuantitySold ?? 0;
                    decimal revenue = sales?.TotalRevenue ?? 0;

                    // Tình trạng kho
                    string stockStatus;
                    string stockBadgeClass;

                    if (product.StockQuantity == 0)
                    {
                        stockStatus = "Hết hàng (0)";
                        stockBadgeClass = "bg-danger";
                    }
                    else if (product.StockQuantity < 50)
                    {
                        stockStatus = $"Sắp hết ({product.StockQuantity})";
                        stockBadgeClass = "bg-warning text-dark";
                    }
                    else
                    {
                        stockStatus = $"Còn hàng ({product.StockQuantity})";
                        stockBadgeClass = "bg-success";
                    }

                    productPerformances.Add(new ProductPerformanceViewModel
                    {
                        ProductID = product.ProductID,
                        ProductName = product.ProductName,
                        Sku = product.Sku ?? "N/A",
                        ImageFileName = product.ImageFileName ?? "placeholder.jpg",
                        CategoryName = category?.CategoryName ?? "N/A",
                        QuantitySold = quantitySold,
                        StockQuantity = product.StockQuantity,
                        StockStatus = stockStatus,
                        StockBadgeClass = stockBadgeClass,
                        TotalRevenue = revenue
                    });
                }

                // 🔹 11. Sắp xếp & phân trang
                productPerformances = productPerformances.OrderByDescending(p => p.QuantitySold).ToList();

                var totalItems = productPerformances.Count;
                model.TotalPages = (int)Math.Ceiling((double)totalItems / pageSize);
                model.ProductPerformances = productPerformances
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .ToList();

                if (model.CurrentPage < 1) model.CurrentPage = 1;
                if (model.CurrentPage > model.TotalPages && model.TotalPages > 0)
                    model.CurrentPage = model.TotalPages;
            }
            catch (Exception ex)
            {
                ViewBag.ErrorMessage = "Lỗi: " + ex.Message;
            }

            return View(model);
        }


        // Action xuất Excel báo cáo sản phẩm
        public ActionResult ExportProductExcel(DateTime? fromDate, DateTime? toDate, string productCategory)
        {
            try
            {
                OfficeOpenXml.ExcelPackage.LicenseContext = OfficeOpenXml.LicenseContext.NonCommercial;

                // Lấy dữ liệu (tương tự như trong action Product)
                var completedOrders = db.Orders
                    .Where(o => o.Status == "Completed")
                    .ToList();

                if (fromDate.HasValue)
                {
                    completedOrders = completedOrders.Where(o => o.OrderDate >= fromDate.Value).ToList();
                }
                if (toDate.HasValue)
                {
                    completedOrders = completedOrders.Where(o => o.OrderDate <= toDate.Value.AddDays(1)).ToList();
                }

                var completedOrderIds = completedOrders.Select(o => o.OrderID).ToList();
                var orderDetails = db.OrderDetails
                    .Where(od => completedOrderIds.Contains(od.OrderID))
                    .ToList();

                var allProducts = db.Products.ToList();

                if (!string.IsNullOrEmpty(productCategory) && productCategory != "all")
                {
                    var category = db.Categories.FirstOrDefault(c => c.CategoryName == productCategory);
                    if (category != null)
                    {
                        allProducts = allProducts.Where(p => p.CategoryId == category.CategoryID).ToList();
                    }
                }

                var productSales = orderDetails
                    .Where(od => allProducts.Any(p => p.ProductID == od.ProductID))
                    .GroupBy(od => od.ProductID)
                    .Select(g => new
                    {
                        ProductID = g.Key,
                        QuantitySold = g.Sum(od => od.Quantity),
                        TotalRevenue = g.Sum(od => od.Quantity * od.UnitPrice)
                    })
                    .ToList();

                // Tính toán thống kê
                var bestSelling = productSales.OrderByDescending(p => p.QuantitySold).FirstOrDefault();
                var bestSellingName = "N/A";
                if (bestSelling != null)
                {
                    var product = allProducts.FirstOrDefault(p => p.ProductID == bestSelling.ProductID);
                    bestSellingName = product?.ProductName ?? "N/A";
                }

                var totalSold = productSales.Sum(p => p.QuantitySold);
                var avgRevenue = productSales.Any() ? productSales.Average(p => p.TotalRevenue) : 0;
                var lowStock = allProducts.Count(p => p.StockQuantity < 50);

                using (var package = new ExcelPackage())
                {
                    var worksheet = package.Workbook.Worksheets.Add("Báo Cáo Sản Phẩm");

                    // TIÊU ĐỀ
                    worksheet.Cells["A1:G1"].Merge = true;
                    worksheet.Cells["A1"].Value = "BÁO CÁO SẢN PHẨM - FRESH FARM";
                    worksheet.Cells["A1"].Style.Font.Size = 18;
                    worksheet.Cells["A1"].Style.Font.Bold = true;
                    worksheet.Cells["A1"].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
                    worksheet.Cells["A1"].Style.Fill.PatternType = ExcelFillStyle.Solid;
                    worksheet.Cells["A1"].Style.Fill.BackgroundColor.SetColor(Color.FromArgb(79, 129, 189));
                    worksheet.Cells["A1"].Style.Font.Color.SetColor(Color.White);
                    worksheet.Row(1).Height = 30;

                    // THÔNG TIN BỘ LỌC
                    int currentRow = 2;
                    worksheet.Cells[$"A{currentRow}:G{currentRow}"].Merge = true;
                    worksheet.Cells[$"A{currentRow}"].Value = $"Ngày xuất: {DateTime.Now:dd/MM/yyyy HH:mm:ss}";
                    worksheet.Cells[$"A{currentRow}"].Style.Font.Italic = true;
                    currentRow++;

                    if (fromDate.HasValue || toDate.HasValue || !string.IsNullOrEmpty(productCategory))
                    {
                        worksheet.Cells[$"A{currentRow}:G{currentRow}"].Merge = true;
                        var filterInfo = "Bộ lọc: ";
                        if (fromDate.HasValue)
                            filterInfo += $"Từ {fromDate.Value:dd/MM/yyyy} ";
                        if (toDate.HasValue)
                            filterInfo += $"đến {toDate.Value:dd/MM/yyyy} ";
                        if (!string.IsNullOrEmpty(productCategory) && productCategory != "all")
                            filterInfo += $"- Danh mục: {productCategory}";
                        worksheet.Cells[$"A{currentRow}"].Value = filterInfo;
                        worksheet.Cells[$"A{currentRow}"].Style.Font.Italic = true;
                        currentRow++;
                    }

                    // THỐNG KÊ TỔNG QUAN
                    currentRow++;
                    worksheet.Cells[$"A{currentRow}:G{currentRow}"].Merge = true;
                    worksheet.Cells[$"A{currentRow}"].Value = "THỐNG KÊ TỔNG QUAN";
                    worksheet.Cells[$"A{currentRow}"].Style.Font.Bold = true;
                    worksheet.Cells[$"A{currentRow}"].Style.Font.Size = 14;
                    worksheet.Cells[$"A{currentRow}"].Style.Fill.PatternType = ExcelFillStyle.Solid;
                    worksheet.Cells[$"A{currentRow}"].Style.Fill.BackgroundColor.SetColor(Color.FromArgb(242, 242, 242));
                    currentRow++;

                    var statsStartRow = currentRow;

                    worksheet.Cells[$"A{currentRow}"].Value = "Bán chạy nhất:";
                    worksheet.Cells[$"A{currentRow}"].Style.Font.Bold = true;
                    worksheet.Cells[$"B{currentRow}"].Value = bestSellingName;
                    worksheet.Cells[$"B{currentRow}"].Style.Font.Color.SetColor(Color.FromArgb(255, 193, 7));

                    worksheet.Cells[$"D{currentRow}"].Value = "Tổng SP đã bán:";
                    worksheet.Cells[$"D{currentRow}"].Style.Font.Bold = true;
                    worksheet.Cells[$"E{currentRow}"].Value = totalSold;
                    worksheet.Cells[$"E{currentRow}"].Style.Font.Color.SetColor(Color.FromArgb(0, 112, 192));
                    currentRow++;

                    worksheet.Cells[$"A{currentRow}"].Value = "Doanh thu TB/SP:";
                    worksheet.Cells[$"A{currentRow}"].Style.Font.Bold = true;
                    worksheet.Cells[$"B{currentRow}"].Value = avgRevenue;
                    worksheet.Cells[$"B{currentRow}"].Style.Numberformat.Format = "#,##0 ₫";
                    worksheet.Cells[$"B{currentRow}"].Style.Font.Color.SetColor(Color.FromArgb(0, 176, 80));

                    worksheet.Cells[$"D{currentRow}"].Value = "SP sắp hết hàng:";
                    worksheet.Cells[$"D{currentRow}"].Style.Font.Bold = true;
                    worksheet.Cells[$"E{currentRow}"].Value = lowStock;
                    worksheet.Cells[$"E{currentRow}"].Style.Font.Color.SetColor(Color.FromArgb(220, 53, 69));
                    currentRow++;

                    using (var range = worksheet.Cells[$"A{statsStartRow}:E{currentRow - 1}"])
                    {
                        range.Style.Border.Top.Style = ExcelBorderStyle.Thin;
                        range.Style.Border.Left.Style = ExcelBorderStyle.Thin;
                        range.Style.Border.Right.Style = ExcelBorderStyle.Thin;
                        range.Style.Border.Bottom.Style = ExcelBorderStyle.Thin;
                    }

                    // BẢNG CHI TIẾT SẢN PHẨM
                    currentRow += 2;
                    worksheet.Cells[$"A{currentRow}:G{currentRow}"].Merge = true;
                    worksheet.Cells[$"A{currentRow}"].Value = "CHI TIẾT HIỆU SUẤT SẢN PHẨM";
                    worksheet.Cells[$"A{currentRow}"].Style.Font.Bold = true;
                    worksheet.Cells[$"A{currentRow}"].Style.Font.Size = 14;
                    worksheet.Cells[$"A{currentRow}"].Style.Fill.PatternType = ExcelFillStyle.Solid;
                    worksheet.Cells[$"A{currentRow}"].Style.Fill.BackgroundColor.SetColor(Color.FromArgb(242, 242, 242));
                    currentRow++;

                    int headerRow = currentRow;
                    worksheet.Cells[$"A{headerRow}"].Value = "Sản phẩm";
                    worksheet.Cells[$"B{headerRow}"].Value = "SKU";
                    worksheet.Cells[$"C{headerRow}"].Value = "Danh mục";
                    worksheet.Cells[$"D{headerRow}"].Value = "Đã bán";
                    worksheet.Cells[$"E{headerRow}"].Value = "Tồn kho";
                    worksheet.Cells[$"F{headerRow}"].Value = "Trạng thái";
                    worksheet.Cells[$"G{headerRow}"].Value = "Doanh thu";

                    using (var range = worksheet.Cells[$"A{headerRow}:G{headerRow}"])
                    {
                        range.Style.Font.Bold = true;
                        range.Style.Fill.PatternType = ExcelFillStyle.Solid;
                        range.Style.Fill.BackgroundColor.SetColor(Color.FromArgb(217, 225, 242));
                        range.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
                        range.Style.Border.Top.Style = ExcelBorderStyle.Thin;
                        range.Style.Border.Left.Style = ExcelBorderStyle.Thin;
                        range.Style.Border.Right.Style = ExcelBorderStyle.Thin;
                        range.Style.Border.Bottom.Style = ExcelBorderStyle.Thin;
                    }
                    worksheet.Row(headerRow).Height = 20;

                    currentRow++;
                    int dataStartRow = currentRow;

                    var productList = allProducts
                        .Select(p => new
                        {
                            Product = p,
                            Category = db.Categories.FirstOrDefault(c => c.CategoryID == p.CategoryId),
                            Sales = productSales.FirstOrDefault(ps => ps.ProductID == p.ProductID)
                        })
                        .OrderByDescending(x => x.Sales?.QuantitySold ?? 0)
                        .ToList();

                    foreach (var item in productList)
                    {
                        var quantitySold = item.Sales?.QuantitySold ?? 0;
                        var revenue = item.Sales?.TotalRevenue ?? 0;

                        worksheet.Cells[$"A{currentRow}"].Value = item.Product.ProductName;
                        worksheet.Cells[$"B{currentRow}"].Value = item.Product.Sku ?? "N/A";
                        worksheet.Cells[$"C{currentRow}"].Value = item.Category?.CategoryName ?? "N/A";
                        worksheet.Cells[$"D{currentRow}"].Value = quantitySold;
                        worksheet.Cells[$"D{currentRow}"].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
                        worksheet.Cells[$"E{currentRow}"].Value = item.Product.StockQuantity;
                        worksheet.Cells[$"E{currentRow}"].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;

                        string stockStatus;
                        Color statusColor;
                        if (item.Product.StockQuantity == 0)
                        {
                            stockStatus = "Hết hàng";
                            statusColor = Color.FromArgb(255, 199, 206);
                        }
                        else if (item.Product.StockQuantity < 50)
                        {
                            stockStatus = "Sắp hết";
                            statusColor = Color.FromArgb(255, 243, 205);
                        }
                        else
                        {
                            stockStatus = "Còn hàng";
                            statusColor = Color.FromArgb(198, 239, 206);
                        }

                        worksheet.Cells[$"F{currentRow}"].Value = stockStatus;
                        worksheet.Cells[$"F{currentRow}"].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
                        worksheet.Cells[$"F{currentRow}"].Style.Fill.PatternType = ExcelFillStyle.Solid;
                        worksheet.Cells[$"F{currentRow}"].Style.Fill.BackgroundColor.SetColor(statusColor);

                        worksheet.Cells[$"G{currentRow}"].Value = revenue;
                        worksheet.Cells[$"G{currentRow}"].Style.Numberformat.Format = "#,##0 ₫";
                        worksheet.Cells[$"G{currentRow}"].Style.Font.Bold = true;

                        currentRow++;
                    }

                    if (productList.Any())
                    {
                        using (var range = worksheet.Cells[$"A{dataStartRow}:G{currentRow - 1}"])
                        {
                            range.Style.Border.Top.Style = ExcelBorderStyle.Thin;
                            range.Style.Border.Left.Style = ExcelBorderStyle.Thin;
                            range.Style.Border.Right.Style = ExcelBorderStyle.Thin;
                            range.Style.Border.Bottom.Style = ExcelBorderStyle.Thin;
                        }

                        // Tổng cộng
                        currentRow++;
                        worksheet.Cells[$"A{currentRow}:C{currentRow}"].Merge = true;
                        worksheet.Cells[$"A{currentRow}"].Value = "TỔNG CỘNG";
                        worksheet.Cells[$"A{currentRow}"].Style.Font.Bold = true;
                        worksheet.Cells[$"A{currentRow}"].Style.HorizontalAlignment = ExcelHorizontalAlignment.Right;

                        worksheet.Cells[$"D{currentRow}"].Value = productList.Sum(x => x.Sales?.QuantitySold ?? 0);
                        worksheet.Cells[$"D{currentRow}"].Style.Font.Bold = true;
                        worksheet.Cells[$"D{currentRow}"].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;

                        worksheet.Cells[$"G{currentRow}"].Value = productList.Sum(x => x.Sales?.TotalRevenue ?? 0);
                        worksheet.Cells[$"G{currentRow}"].Style.Numberformat.Format = "#,##0 ₫";
                        worksheet.Cells[$"G{currentRow}"].Style.Font.Bold = true;

                        using (var range = worksheet.Cells[$"A{currentRow}:G{currentRow}"])
                        {
                            range.Style.Fill.PatternType = ExcelFillStyle.Solid;
                            range.Style.Fill.BackgroundColor.SetColor(Color.FromArgb(217, 225, 242));
                            range.Style.Border.Top.Style = ExcelBorderStyle.Double;
                            range.Style.Border.Bottom.Style = ExcelBorderStyle.Double;
                        }
                    }

                    // FOOTER
                    currentRow += 2;
                    worksheet.Cells[$"A{currentRow}:G{currentRow}"].Merge = true;
                    worksheet.Cells[$"A{currentRow}"].Value = "© Fresh Farm - Hệ thống quản lý bán hàng";
                    worksheet.Cells[$"A{currentRow}"].Style.Font.Italic = true;
                    worksheet.Cells[$"A{currentRow}"].Style.Font.Size = 9;
                    worksheet.Cells[$"A{currentRow}"].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
                    worksheet.Cells[$"A{currentRow}"].Style.Font.Color.SetColor(Color.Gray);

                    // Điều chỉnh độ rộng cột
                    worksheet.Column(1).Width = 30;
                    worksheet.Column(2).Width = 15;
                    worksheet.Column(3).Width = 20;
                    worksheet.Column(4).Width = 12;
                    worksheet.Column(5).Width = 12;
                    worksheet.Column(6).Width = 15;
                    worksheet.Column(7).Width = 18;

                    worksheet.Cells[worksheet.Dimension.Address].Style.VerticalAlignment = ExcelVerticalAlignment.Center;

                    var fileContents = package.GetAsByteArray();
                    var fileName = $"BaoCaoSanPham_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";

                    return File(fileContents,
                        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                        fileName);
                }
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = "Lỗi xuất Excel: " + ex.Message;
                return RedirectToAction("Product", new { fromDate, toDate, productCategory });
            }
        }
        // Thêm các phương thức này vào ReportController.cs

        // GET: Admin/Report/Shipping
        public ActionResult Shipping(
            DateTime? fromDate,
            DateTime? toDate,
            int? staffId,
            string status,
            string q,
            string sort = "date_desc",
            int page = 1,
            int pageSize = 20)
        {
            var fallbackModel = new ShippingReportViewModel
            {
                FromDate = fromDate,
                ToDate = toDate,
                SelectedStaffId = staffId,
                Status = string.IsNullOrWhiteSpace(status) ? "all" : status,
                Query = q,
                Sort = sort,
                CurrentPage = page,
                PageSize = pageSize
            };

            try
            {
                ViewBag.DeliveryStaffs = GetActiveDeliveryStaffSelectList();

                var orders = QueryShippingOrders(fromDate, toDate, staffId, status);

                if (!string.IsNullOrWhiteSpace(q))
                {
                    var query = q.Trim();
                    orders = orders
                        .Where(o =>
                        {
                            var idMatch = o.OrderID.ToString().Contains(query);
                            var nameMatch = (o.User?.FullName ?? o.BuyerFullName ?? string.Empty)
                                .IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
                            var phoneMatch = (o.User?.Phone ?? o.BuyerPhone ?? string.Empty)
                                .IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
                            var shipping = o.Shippings.OrderByDescending(s => s.ShippingID).FirstOrDefault();
                            var addr = BuildDeliveryAddress(shipping) ?? string.Empty;
                            var addrMatch = addr.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
                            return idMatch || nameMatch || phoneMatch || addrMatch;
                        })
                        .ToList();
                }

                var model = BuildShippingReportModel(orders, fromDate, toDate, staffId, status, q, sort, page, pageSize);

                return View(model);
            }
            catch (Exception ex)
            {
                ViewBag.ErrorMessage = "Loi: " + ex.Message;

                if (ViewBag.DeliveryStaffs == null)
                {
                    ViewBag.DeliveryStaffs = new List<SelectListItem>();
                }

                return View(fallbackModel);
            }
        }

        // GET: Admin/Report/ExportShippingExcel
        public ActionResult ExportShippingExcel(DateTime? fromDate, DateTime? toDate, int? staffId, string status, string q)
        {
            try
            {
                ExcelPackage.LicenseContext = LicenseContext.NonCommercial;

                var orders = QueryShippingOrders(fromDate, toDate, staffId, status);
                if (!string.IsNullOrWhiteSpace(q))
                {
                    var query = q.Trim();
                    orders = orders
                        .Where(o =>
                        {
                            var idMatch = o.OrderID.ToString().Contains(query);
                            var nameMatch = (o.User?.FullName ?? o.BuyerFullName ?? string.Empty)
                                .IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
                            var phoneMatch = (o.User?.Phone ?? o.BuyerPhone ?? string.Empty)
                                .IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
                            var shipping = o.Shippings.OrderByDescending(s => s.ShippingID).FirstOrDefault();
                            var addr = BuildDeliveryAddress(shipping) ?? string.Empty;
                            var addrMatch = addr.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
                            return idMatch || nameMatch || phoneMatch || addrMatch;
                        })
                        .ToList();
                }

                var activeAssignments = BuildActiveAssignments(orders);
                var model = BuildShippingReportModel(orders, fromDate, toDate, staffId, status, q, "date_desc", 1, int.MaxValue);

                using (var package = new ExcelPackage())
                {
                    var worksheet = package.Workbook.Worksheets.Add("Bao cao giao hang");

                    worksheet.Cells["A1:J1"].Merge = true;
                    worksheet.Cells["A1"].Value = "BAO CAO GIAO HANG - FRESH FARM";
                    worksheet.Cells["A1"].Style.Font.Size = 18;
                    worksheet.Cells["A1"].Style.Font.Bold = true;
                    worksheet.Cells["A1"].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
                    worksheet.Cells["A1"].Style.Fill.PatternType = ExcelFillStyle.Solid;
                    worksheet.Cells["A1"].Style.Fill.BackgroundColor.SetColor(Color.FromArgb(79, 129, 189));
                    worksheet.Cells["A1"].Style.Font.Color.SetColor(Color.White);
                    worksheet.Row(1).Height = 30;

                    var currentRow = 2;
                    worksheet.Cells[$"A{currentRow}:J{currentRow}"].Merge = true;
                    worksheet.Cells[$"A{currentRow}"].Value = $"Ngay xuat: {DateTime.Now:dd/MM/yyyy HH:mm:ss}";
                    worksheet.Cells[$"A{currentRow}"].Style.Font.Italic = true;
                    currentRow++;

                    if (fromDate.HasValue || toDate.HasValue || staffId.HasValue)
                    {
                        worksheet.Cells[$"A{currentRow}:J{currentRow}"].Merge = true;
                        var filterParts = new List<string>();
                        if (fromDate.HasValue)
                        {
                            filterParts.Add($"Tu {fromDate.Value:dd/MM/yyyy}");
                        }
                        if (toDate.HasValue)
                        {
                            filterParts.Add($"den {toDate.Value:dd/MM/yyyy}");
                        }
                        if (staffId.HasValue)
                        {
                            var staffName = model.SelectedStaffName;
                            if (string.IsNullOrWhiteSpace(staffName))
                            {
                                staffName = $"Ma NV #{staffId.Value}";
                            }
                            filterParts.Add($"Nhan vien: {staffName}");
                        }

                        worksheet.Cells[$"A{currentRow}"].Value = "Bo loc: " + string.Join(" - ", filterParts);
                        worksheet.Cells[$"A{currentRow}"].Style.Font.Italic = true;
                        currentRow++;
                    }

                    currentRow += 2;
                    worksheet.Cells[$"A{currentRow}:J{currentRow}"].Merge = true;
                    worksheet.Cells[$"A{currentRow}"].Value = "TONG QUAN";
                    worksheet.Cells[$"A{currentRow}"].Style.Font.Bold = true;
                    worksheet.Cells[$"A{currentRow}"].Style.Font.Size = 14;
                    currentRow++;

                    worksheet.Cells[$"A{currentRow}"].Value = "Don da giao";
                    worksheet.Cells[$"B{currentRow}"].Value = model.DeliveredOrders;
                    worksheet.Cells[$"C{currentRow}"].Value = "Tong phi van chuyen";
                    worksheet.Cells[$"D{currentRow}"].Value = model.TotalShippingFee;
                    worksheet.Cells[$"D{currentRow}"].Style.Numberformat.Format = "#,##0";
                    currentRow++;

                    worksheet.Cells[$"A{currentRow}"].Value = "Thoi gian giao trung binh (ngay)";
                    worksheet.Cells[$"B{currentRow}"].Value = model.AverageDeliveryTime;
                    worksheet.Cells[$"C{currentRow}"].Value = "Ty le chuyen hoan (%)";
                    worksheet.Cells[$"D{currentRow}"].Value = model.ReturnRate;
                    currentRow += 2;

                    worksheet.Cells[$"A{currentRow}"].Value = "Ma DH";
                    worksheet.Cells[$"B{currentRow}"].Value = "Khach hang";
                    worksheet.Cells[$"C{currentRow}"].Value = "SDT";
                    worksheet.Cells[$"D{currentRow}"].Value = "Nhan vien giao hang";
                    worksheet.Cells[$"E{currentRow}"].Value = "Phi van chuyen";
                    worksheet.Cells[$"F{currentRow}"].Value = "Ngay tao";
                    worksheet.Cells[$"G{currentRow}"].Value = "Ngay du kien";
                    worksheet.Cells[$"H{currentRow}"].Value = "Dia chi";
                    worksheet.Cells[$"I{currentRow}"].Value = "Trang thai";
                    worksheet.Cells[$"J{currentRow}"].Value = "Thoi gian giao";

                    using (var headerRange = worksheet.Cells[$"A{currentRow}:J{currentRow}"])
                    {
                        headerRange.Style.Font.Bold = true;
                        headerRange.Style.Fill.PatternType = ExcelFillStyle.Solid;
                        headerRange.Style.Fill.BackgroundColor.SetColor(Color.FromArgb(220, 230, 241));
                        headerRange.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
                    }

                    var rows = orders
                        .Select(order =>
                        {
                            activeAssignments.TryGetValue(order.OrderID, out var assignment);
                            var shipping = order.Shippings.OrderByDescending(s => s.ShippingID).FirstOrDefault();
                            return new { order, assignment, shipping };
                        })
                        .Where(x => x.shipping != null)
                        .OrderByDescending(x => x.order.OrderDate)
                        .ToList();

                    var dataStartRow = currentRow + 1;
                    currentRow = dataStartRow;

                    foreach (var row in rows)
                    {
                        var order = row.order;
                        var assignment = row.assignment;
                        var shipping = row.shipping;
                        var statusInfo = GetShippingStatusInfo(order.Status);

                        var staffName = assignment?.UserAdmin?.FullName;
                        if (string.IsNullOrWhiteSpace(staffName) && assignment?.AssignedToAdminID.HasValue == true)
                        {
                            staffName = $"NV #{assignment.AssignedToAdminID.Value:D4}";
                        }
                        if (string.IsNullOrWhiteSpace(staffName))
                        {
                            staffName = "Chua phan cong";
                        }

                        DateTime expectedDelivery = assignment?.ExpectedDeliveryAt ?? assignment?.DeliveredAt ?? order.OrderDate.AddDays(2);
                        int? actualDays = null;
                        if (assignment?.DeliveredAt.HasValue == true)
                        {
                            var diff = (assignment.DeliveredAt.Value - order.OrderDate).TotalDays;
                            if (diff >= 0)
                            {
                                actualDays = (int)Math.Round(diff);
                            }
                        }

                        worksheet.Cells[$"A{currentRow}"].Value = GenerateOrderCode(order.OrderID);
                        worksheet.Cells[$"B{currentRow}"].Value = order.User?.FullName ?? order.BuyerFullName ?? "N/A";
                        worksheet.Cells[$"C{currentRow}"].Value = order.User?.Phone ?? order.BuyerPhone ?? "N/A";
                        worksheet.Cells[$"D{currentRow}"].Value = staffName;
                        worksheet.Cells[$"E{currentRow}"].Value = order.ShippingFee;
                        worksheet.Cells[$"E{currentRow}"].Style.Numberformat.Format = "#,##0";
                        worksheet.Cells[$"F{currentRow}"].Value = order.OrderDate.ToString("dd/MM/yyyy");
                        worksheet.Cells[$"G{currentRow}"].Value = expectedDelivery.ToString("dd/MM/yyyy");
                        worksheet.Cells[$"H{currentRow}"].Value = BuildDeliveryAddress(shipping);
                        worksheet.Cells[$"I{currentRow}"].Value = statusInfo.Item2;
                        worksheet.Cells[$"I{currentRow}"].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
                        worksheet.Cells[$"J{currentRow}"].Value = actualDays.HasValue ? $"{actualDays.Value} ngay" : "Dang giao";
                        worksheet.Cells[$"J{currentRow}"].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;

                        var statusKey = (order.Status ?? string.Empty).ToLowerInvariant();
                        Color statusColor;
                        switch (statusKey)
                        {
                            case "completed":
                            case "delivered":
                                statusColor = Color.FromArgb(198, 239, 206);
                                break;
                            case "shipped":
                                statusColor = Color.FromArgb(217, 234, 250);
                                break;
                            case "pending":
                                statusColor = Color.FromArgb(255, 243, 205);
                                break;
                            case "cancelled":
                                statusColor = Color.FromArgb(255, 199, 206);
                                break;
                            default:
                                statusColor = Color.White;
                                break;
                        }

                        worksheet.Cells[$"I{currentRow}"].Style.Fill.PatternType = ExcelFillStyle.Solid;
                        worksheet.Cells[$"I{currentRow}"].Style.Fill.BackgroundColor.SetColor(statusColor);

                        currentRow++;
                    }

                    if (rows.Any())
                    {
                        using (var range = worksheet.Cells[$"A{dataStartRow}:J{currentRow - 1}"])
                        {
                            range.Style.Border.Top.Style = ExcelBorderStyle.Thin;
                            range.Style.Border.Left.Style = ExcelBorderStyle.Thin;
                            range.Style.Border.Right.Style = ExcelBorderStyle.Thin;
                            range.Style.Border.Bottom.Style = ExcelBorderStyle.Thin;
                        }
                    }

                    worksheet.Column(1).Width = 12;
                    worksheet.Column(2).Width = 25;
                    worksheet.Column(3).Width = 15;
                    worksheet.Column(4).Width = 25;
                    worksheet.Column(5).Width = 15;
                    worksheet.Column(6).Width = 15;
                    worksheet.Column(7).Width = 15;
                    worksheet.Column(8).Width = 40;
                    worksheet.Column(9).Width = 18;
                    worksheet.Column(10).Width = 18;
                    worksheet.Cells[worksheet.Dimension.Address].Style.VerticalAlignment = ExcelVerticalAlignment.Center;

                    currentRow += 2;
                    worksheet.Cells[$"A{currentRow}:J{currentRow}"].Merge = true;
                    worksheet.Cells[$"A{currentRow}"].Value = "© Fresh Farm - He thong quan ly ban hang";
                    worksheet.Cells[$"A{currentRow}"].Style.Font.Italic = true;
                    worksheet.Cells[$"A{currentRow}"].Style.Font.Size = 9;
                    worksheet.Cells[$"A{currentRow}"].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
                    worksheet.Cells[$"A{currentRow}"].Style.Font.Color.SetColor(Color.Gray);

                    var fileContents = package.GetAsByteArray();
                    var fileName = $"BaoCaoGiaoHang_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";

                    return File(fileContents,
                        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                        fileName);
                }
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = "Loi xuat Excel: " + ex.Message;
                return RedirectToAction("Shipping", new { fromDate, toDate, staffId });
            }
        }

        private ShippingReportViewModel BuildShippingReportModel(
            List<Order> orders,
            DateTime? fromDate,
            DateTime? toDate,
            int? staffId,
            string status,
            string query,
            string sort,
            int page,
            int pageSize)
        {
            orders = orders ?? new List<Order>();

            var model = new ShippingReportViewModel
            {
                FromDate = fromDate,
                ToDate = toDate,
                SelectedStaffId = staffId,
                Status = string.IsNullOrWhiteSpace(status) ? "all" : status,
                Query = query,
                Sort = string.IsNullOrWhiteSpace(sort) ? "date_desc" : sort,
                CurrentPage = page < 1 ? 1 : page,
                PageSize = pageSize > 0 ? pageSize : 20
            };

            var activeAssignments = BuildActiveAssignments(orders);
            var deliveredStatuses = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Completed", "Delivered" };
            var deliveredOrders = orders.Where(o => deliveredStatuses.Contains(o.Status ?? string.Empty)).ToList();
            var cancelledCount = orders.Count(o => string.Equals(o.Status, "Cancelled", StringComparison.OrdinalIgnoreCase));

            model.DeliveredOrders = deliveredOrders.Count;
            model.TotalShippingFee = orders.Sum(o => o.ShippingFee);

            var deliveryDurations = new List<double>();
            foreach (var order in deliveredOrders)
            {
                if (activeAssignments.TryGetValue(order.OrderID, out var assignment) && assignment.DeliveredAt.HasValue)
                {
                    var start = assignment.PickedUpAt ?? assignment.AcceptedAt ?? order.OrderDate;
                    var duration = (assignment.DeliveredAt.Value - start).TotalDays;
                    if (duration >= 0)
                    {
                        deliveryDurations.Add(duration);
                    }
                }
                else
                {
                    var duration = (DateTime.Now - order.OrderDate).TotalDays;
                    if (duration >= 0)
                    {
                        deliveryDurations.Add(duration);
                    }
                }
            }

            model.AverageDeliveryTime = deliveryDurations.Any()
                ? Math.Round((decimal)deliveryDurations.Average(), 2)
                : 0;

            var totalOrders = orders.Count;
            model.ReturnRate = totalOrders > 0
                ? Math.Round((decimal)cancelledCount * 100 / totalOrders, 2)
                : 0;

            var staffPerformances = new List<DeliveryStaffPerformanceViewModel>();
            var groupedByStaff = orders
                .Select(order =>
                {
                    activeAssignments.TryGetValue(order.OrderID, out var assignment);
                    return new { order, assignment };
                })
                .Where(x => x.assignment != null && x.assignment.AssignedToAdminID.HasValue)
                .GroupBy(x => x.assignment.AssignedToAdminID.Value);

            foreach (var group in groupedByStaff)
            {
                var staffOrders = group.Select(x => x.order).ToList();
                var staffAssignmentSamples = group.Select(x => x.assignment).Where(a => a != null).ToList();

                var staffName = staffAssignmentSamples
                    .Select(a => a.UserAdmin?.FullName)
                    .FirstOrDefault(name => !string.IsNullOrWhiteSpace(name));

                if (string.IsNullOrWhiteSpace(staffName))
                {
                    var staff = db.UserAdmins.FirstOrDefault(u => u.AdminID == group.Key);
                    if (staff != null)
                    {
                        staffName = string.IsNullOrWhiteSpace(staff.FullName) ? staff.UserName : staff.FullName;
                    }
                }

                if (string.IsNullOrWhiteSpace(staffName))
                {
                    staffName = $"NV #{group.Key:D4}";
                }

                var successCount = staffOrders.Count(o => deliveredStatuses.Contains(o.Status ?? string.Empty));
                var staffDurations = group
                    .Where(x => x.assignment != null && x.assignment.DeliveredAt.HasValue)
                    .Select(x =>
                    {
                        var assignment = x.assignment;
                        var start = assignment.PickedUpAt ?? assignment.AcceptedAt ?? x.order.OrderDate;
                        var duration = (assignment.DeliveredAt.Value - start).TotalDays;
                        return duration >= 0 ? (double?)duration : null;
                    })
                    .Where(d => d.HasValue)
                    .Select(d => d.Value)
                    .ToList();

                staffPerformances.Add(new DeliveryStaffPerformanceViewModel
                {
                    StaffId = group.Key,
                    StaffName = staffName,
                    TotalOrders = staffOrders.Count,
                    SuccessRate = staffOrders.Count > 0 ? Math.Round(successCount * 100m / staffOrders.Count, 2) : 0,
                    AverageDeliveryTime = staffDurations.Any() ? Math.Round((decimal)staffDurations.Average(), 2) : 0,
                    TotalRevenue = staffOrders.Sum(o => o.ShippingFee)
                });
            }

            model.StaffPerformances = staffPerformances
                .OrderByDescending(sp => sp.TotalOrders)
                .ThenBy(sp => sp.StaffName)
                .ToList();

            if (staffId.HasValue && string.IsNullOrWhiteSpace(model.SelectedStaffName))
            {
                model.SelectedStaffName = model.StaffPerformances
                    .FirstOrDefault(sp => sp.StaffId == staffId.Value)?.StaffName;

                if (string.IsNullOrWhiteSpace(model.SelectedStaffName))
                {
                    var staff = db.UserAdmins.FirstOrDefault(u => u.AdminID == staffId.Value);
                    if (staff != null)
                    {
                        model.SelectedStaffName = string.IsNullOrWhiteSpace(staff.FullName) ? staff.UserName : staff.FullName;
                    }
                }
            }

            model.DeliveryTimeDistributions.Clear();
            var distributionDefinitions = new[]
            {
                new { Label = "<= 2 ngay", Count = deliveryDurations.Count(d => d <= 2) },
                new { Label = "3-4 ngay", Count = deliveryDurations.Count(d => d > 2 && d <= 4) },
                new { Label = "5-7 ngay", Count = deliveryDurations.Count(d => d > 4 && d <= 7) },
                new { Label = "> 7 ngay", Count = deliveryDurations.Count(d => d > 7) }
            };

            var totalSamples = deliveryDurations.Count;
            foreach (var item in distributionDefinitions)
            {
                var percentage = totalSamples > 0
                    ? Math.Round((decimal)item.Count * 100 / totalSamples, 2)
                    : 0;

                model.DeliveryTimeDistributions.Add(new DeliveryTimeDistributionViewModel
                {
                    TimeRange = item.Label,
                    OrderCount = item.Count,
                    Percentage = percentage
                });
            }

            model.RecentShippings.Clear();

            // Build enriched rows
            var rows = orders
                .Select(o =>
                {
                    activeAssignments.TryGetValue(o.OrderID, out var assignment);
                    var shipping = o.Shippings.OrderByDescending(s => s.ShippingID).FirstOrDefault();
                    if (shipping == null) return null;
                    var statusInfo = GetShippingStatusInfo(o.Status);
                    var staffName = assignment?.UserAdmin?.FullName;
                    if (string.IsNullOrWhiteSpace(staffName) && assignment?.AssignedToAdminID.HasValue == true)
                    {
                        staffName = $"NV #{assignment.AssignedToAdminID.Value:D4}";
                    }
                    if (string.IsNullOrWhiteSpace(staffName))
                    {
                        staffName = "Chua phan cong";
                    }
                    DateTime expectedDelivery = assignment?.ExpectedDeliveryAt ?? assignment?.DeliveredAt ?? o.OrderDate.AddDays(2);
                    int? actualDays = null;
                    if (assignment?.DeliveredAt.HasValue == true)
                    {
                        var diff = (assignment.DeliveredAt.Value - o.OrderDate).TotalDays;
                        if (diff >= 0)
                        {
                            actualDays = (int)Math.Round(diff);
                        }
                    }
                    return new
                    {
                        order = o,
                        assignment,
                        shipping,
                        statusInfo,
                        staffName,
                        expectedDelivery,
                        actualDays
                    };
                })
                .Where(x => x != null)
                .ToList();

            // Sort
            switch ((model.Sort ?? "date_desc").ToLower())
            {
                case "code_asc":
                    rows = rows.OrderBy(x => x.order.OrderID).ToList();
                    break;
                case "code_desc":
                    rows = rows.OrderByDescending(x => x.order.OrderID).ToList();
                    break;
                case "customer_asc":
                    rows = rows.OrderBy(x => (x.order.User?.FullName ?? x.order.BuyerFullName ?? string.Empty)).ToList();
                    break;
                case "customer_desc":
                    rows = rows.OrderByDescending(x => (x.order.User?.FullName ?? x.order.BuyerFullName ?? string.Empty)).ToList();
                    break;
                case "staff_asc":
                    rows = rows.OrderBy(x => x.staffName).ToList();
                    break;
                case "staff_desc":
                    rows = rows.OrderByDescending(x => x.staffName).ToList();
                    break;
                case "expected_asc":
                    rows = rows.OrderBy(x => x.expectedDelivery).ToList();
                    break;
                case "expected_desc":
                    rows = rows.OrderByDescending(x => x.expectedDelivery).ToList();
                    break;
                case "status_asc":
                    rows = rows.OrderBy(x => x.order.Status).ToList();
                    break;
                case "status_desc":
                    rows = rows.OrderByDescending(x => x.order.Status).ToList();
                    break;
                case "date_asc":
                    rows = rows.OrderBy(x => x.order.OrderDate).ToList();
                    break;
                case "date_desc":
                default:
                    rows = rows.OrderByDescending(x => x.order.OrderDate).ToList();
                    break;
            }

            // Pagination
            model.TotalRecords = rows.Count;
            model.TotalPages = (int)Math.Ceiling(model.TotalRecords / (double)model.PageSize);
            var skip = (model.CurrentPage - 1) * model.PageSize;
            if (skip < 0) skip = 0;
            var pageRows = rows.Skip(skip).Take(model.PageSize).ToList();

            foreach (var x in pageRows)
            {
                model.RecentShippings.Add(new ShippingHistoryViewModel
                {
                    OrderID = x.order.OrderID,
                    OrderCode = GenerateOrderCode(x.order.OrderID),
                    DeliveryStaffId = x.assignment?.AssignedToAdminID,
                    DeliveryStaffName = x.staffName,
                    ShippingFee = x.order.ShippingFee,
                    ShippingDate = x.order.OrderDate,
                    ShippingDateFormatted = x.order.OrderDate.ToString("dd/MM/yyyy"),
                    ExpectedDeliveryDate = x.expectedDelivery,
                    ExpectedDeliveryDateFormatted = x.expectedDelivery.ToString("dd/MM/yyyy"),
                    Status = x.order.Status,
                    StatusBadgeClass = x.statusInfo.Item1,
                    StatusText = x.statusInfo.Item2,
                    CustomerName = x.order.User?.FullName ?? x.order.BuyerFullName ?? "N/A",
                    CustomerPhone = x.order.User?.Phone ?? x.order.BuyerPhone ?? "N/A",
                    DeliveryAddress = BuildDeliveryAddress(x.shipping),
                    ActualDeliveryDays = x.actualDays
                });
            }

            return model;
        }

        private static Dictionary<int, DeliveryAssignment> BuildActiveAssignments(IEnumerable<Order> orders)
        {
            return orders
                .Where(o => o != null)
                .SelectMany(o => o.DeliveryAssignments)
                .Where(da => da.IsActive && da.AssignedToAdminID.HasValue)
                .OrderByDescending(da => da.UpdatedAt ?? da.CreatedAt)
                .GroupBy(da => da.OrderID)
                .ToDictionary(g => g.Key, g => g.First());
        }

        private List<Order> QueryShippingOrders(DateTime? fromDate, DateTime? toDate, int? staffId, string status)
        {
            var query = db.Orders
                .Include(o => o.User)
                .Include(o => o.Shippings.Select(s => s.Commune))
                .Include(o => o.Shippings.Select(s => s.Province))
                .Include(o => o.DeliveryAssignments.Select(da => da.UserAdmin))
                .Where(o => o.Shippings.Any());

            if (fromDate.HasValue)
            {
                query = query.Where(o => o.OrderDate >= fromDate.Value);
            }

            if (toDate.HasValue)
            {
                var toExclusive = toDate.Value.AddDays(1);
                query = query.Where(o => o.OrderDate < toExclusive);
            }

            if (staffId.HasValue)
            {
                var sid = staffId.Value;
                query = query.Where(o => o.DeliveryAssignments.Any(da => da.IsActive && da.AssignedToAdminID == sid));
            }

            if (!string.IsNullOrWhiteSpace(status))
            {
                var st = status.Trim().ToLower();
                if (st != "all")
                {
                    if (st == "delivered" || st == "completed")
                    {
                        query = query.Where(o => o.Status == "Delivered" || o.Status == "Completed");
                    }
                    else if (st == "canceled" || st == "cancelled")
                    {
                        query = query.Where(o => o.Status == "Canceled" || o.Status == "Cancelled");
                    }
                    else if (st == "shipped" || st == "pending" || st == "processing" || st == "ready")
                    {
                        var statusPascal = char.ToUpper(st[0]) + st.Substring(1);
                        query = query.Where(o => o.Status == statusPascal);
                    }
                }
            }

            return query
                .OrderByDescending(o => o.OrderDate)
                .ToList();
        }

        private List<SelectListItem> GetActiveDeliveryStaffSelectList()
        {
            // Lưu ý: Không dùng IsNullOrWhiteSpace trực tiếp trong LINQ-to-Entities
            // vì EF không thể dịch được. Chuyển sang in-memory trước khi map.
            return db.UserAdmins
                .Where(u => u.IsActive)
                .OrderBy(u => u.FullName)
                .Select(u => new { u.AdminID, u.FullName, u.UserName })
                .ToList()
                .Select(u => new SelectListItem
                {
                    Value = u.AdminID.ToString(),
                    Text = string.IsNullOrWhiteSpace(u.FullName) ? u.UserName : u.FullName
                })
                .ToList();
        }

        private static string BuildDeliveryAddress(Shipping shipping)
        {
            if (shipping == null)
            {
                return "N/A";
            }

            if (shipping.IsStorePickup)
            {
                return !string.IsNullOrWhiteSpace(shipping.StoreAddress)
                    ? shipping.StoreAddress
                    : "Nhan tai cua hang";
            }

            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(shipping.AddressDetail))
            {
                parts.Add(shipping.AddressDetail);
            }
            if (shipping.Commune != null && !string.IsNullOrWhiteSpace(shipping.Commune.CommuneName))
            {
                parts.Add(shipping.Commune.CommuneName);
            }
            if (shipping.Province != null && !string.IsNullOrWhiteSpace(shipping.Province.ProvinceName))
            {
                parts.Add(shipping.Province.ProvinceName);
            }

            return parts.Count > 0 ? string.Join(", ", parts) : "N/A";
        }

        // Helper method: Lấy thông tin trạng thái vận chuyển// Helper method: Lấy thông tin trạng thái vận chuyển
        private Tuple<string, string> GetShippingStatusInfo(string status)
        {
            switch (status)
            {
                case "Completed":
                    return Tuple.Create("bg-success", "Đã giao hàng");
                case "Shipped":
                    return Tuple.Create("bg-info", "Đang vận chuyển");
                case "Pending":
                    return Tuple.Create("bg-warning text-dark", "Đang lấy hàng");
                case "Cancelled":
                    return Tuple.Create("bg-danger", "Chuyển hoàn");
                default:
                    return Tuple.Create("bg-secondary", "Không xác định");
            }
        }
        // Thêm các phương thức này vào ReportController.cs

        // GET: Admin/Report/Review
        public ActionResult Review(DateTime? fromDate, DateTime? toDate, string starRating, int page = 1, int pageSize = 10)
        {
            var model = new ReviewReportViewModel
            {
                FromDate = fromDate,
                ToDate = toDate,
                StarRating = starRating ?? "all"
            };

            try
            {
                // Lấy tất cả đánh giá
                var reviews = db.Reviews.Include("User").Include("Product").ToList();

                // Áp dụng bộ lọc ngày
                if (fromDate.HasValue)
                {
                    reviews = reviews.Where(r => r.CreatedAt >= fromDate.Value).ToList();
                }
                if (toDate.HasValue)
                {
                    reviews = reviews.Where(r => r.CreatedAt <= toDate.Value.AddDays(1)).ToList();
                }

                // Áp dụng bộ lọc xếp hạng
                if (!string.IsNullOrEmpty(starRating) && starRating != "all")
                {
                    int stars = int.Parse(starRating);
                    reviews = reviews.Where(r => r.Rating == stars).ToList();
                }

                // THỐNG KÊ TỔNG QUAN
                model.TotalReviews = reviews.Count;

                if (reviews.Any())
                {
                    // Điểm trung bình (double)
                    model.AverageRating = reviews.Any()
                        ? Math.Round(reviews.Average(r => (double)r.Rating), 1)
                        : 0.0;

                    // Đánh giá tích cực (4-5 sao)
                    model.PositiveReviews = reviews.Count(r => r.Rating >= 4);
                    model.NegativeReviews = reviews.Count(r => r.Rating <= 2);

                    // Đánh giá tiêu cực (1-2 sao)
                    model.PositivePercentage = model.TotalReviews > 0
                        ? Math.Round((double)model.PositiveReviews / model.TotalReviews * 100, 1)
                        : 0.0;
                }

                // PHÂN BỐ XẾP HẠNG SAO
                model.StarDistributions.Clear();
                for (int i = 5; i >= 1; i--)
                {
                    var count = reviews.Count(r => r.Rating == i);
                    model.StarDistributions.Add(new StarDistributionItem
                    {
                        Label = $"{i} Sao",
                        Count = count
                    });
                }

                // CHỦ ĐỀ ĐƯỢC NHẮC ĐẾN NHIỀU NHẤT
                var topics = new List<string>
        {
            "chất lượng tốt", "giao hàng nhanh", "đóng gói cẩn thận",
            "sản phẩm tươi", "giá cả hợp lý", "nhân viên nhiệt tình",
            "sẽ ủng hộ tiếp", "đúng mô tả", "hơi nhỏ", "ngon",
            "giao hàng chậm"
        };

                // Đếm số lần xuất hiện các từ khóa trong comment
                var topicCounts = new Dictionary<string, int>();
                foreach (var topic in topics)
                {
                    var count = reviews.Count(r => !string.IsNullOrEmpty(r.Comment) &&
                                                  r.Comment.ToLower().Contains(topic.ToLower()));
                    if (count > 0)
                    {
                        topicCounts[topic] = count;
                    }
                }

                // Lấy top 10 chủ đề được nhắc nhiều nhất
                model.TopMentionedTopics = topicCounts
                    .OrderByDescending(kvp => kvp.Value)
                    .Take(10)
                    .Select(kvp => kvp.Key)
                    .ToList();

                // DANH SÁCH ĐÁNH GIÁ GẦN ĐÂY
                var recentReviews = reviews
                    .OrderByDescending(r => r.CreatedAt)
                    .Skip((page - 1) * pageSize).Take(pageSize)
                    .ToList();

                // Khởi tạo danh sách một lần duy nhất
                model.RecentReviews = new List<RecentReviewRow>();

                // Duyệt danh sách review gần đây (đã Include User, Product)
                foreach (var review in recentReviews)
                {
                    // Lấy thông tin user và sản phẩm (đã Include ở query ngoài)
                    var user = review.User;
                    var product = review.Product;

                    // HTML hiển thị sao
                    var starDisplay = "";
                    for (int i = 1; i <= 5; i++)
                    {
                        if (i <= review.Rating)
                            starDisplay += "<i class='bi bi-star-fill text-warning'></i>";
                        else
                            starDisplay += "<i class='bi bi-star text-muted'></i>";
                    }

                    // Thêm vào danh sách view model
                    model.RecentReviews.Add(new RecentReviewRow
                    {
                        ReviewID = review.ReviewID,
                        ProductID = review.ProductID,
                        UserID = review.UserID,
                        CustomerName = user?.FullName ?? user?.UserName ?? "—",
                        ProductName = product?.ProductName ?? "—",
                        ProductImageFileName = product?.ImageFileName, // để view show ảnh thật
                        Rating = review.Rating,
                        Comment = review.Comment ?? "",
                        CreatedAt = review.CreatedAt,
                        StarDisplay = starDisplay
                        // View có thể dùng CreatedAtFormatted sẵn trong RecentReviewRow
                    });
                }
                }

            catch (Exception ex)
            {
                ViewBag.ErrorMessage = "Lỗi: " + ex.Message;
            }

            // paging info to view
            ViewBag.Page = page;
            ViewBag.PageSize = pageSize;
            ViewBag.TotalPages = Math.Max(1, (int)Math.Ceiling((model.TotalReviews) / (double)pageSize));
            ViewBag.Total = model.TotalReviews;
            return View(model);
        }

        // GET: Admin/Report/ExportReviewExcel
        public ActionResult ExportReviewExcel(DateTime? fromDate, DateTime? toDate, string starRating)
        {
            try
            {
                OfficeOpenXml.ExcelPackage.LicenseContext = OfficeOpenXml.LicenseContext.NonCommercial;

                // Lấy dữ liệu đánh giá
                var reviews = db.Reviews.ToList();

                // Áp dụng bộ lọc
                if (fromDate.HasValue)
                {
                    reviews = reviews.Where(r => r.CreatedAt >= fromDate.Value).ToList();
                }
                if (toDate.HasValue)
                {
                    reviews = reviews.Where(r => r.CreatedAt <= toDate.Value.AddDays(1)).ToList();
                }
                if (!string.IsNullOrEmpty(starRating) && starRating != "all")
                {
                    int stars = int.Parse(starRating);
                    reviews = reviews.Where(r => r.Rating == stars).ToList();
                }

                // Tính toán thống kê
                var totalReviews = reviews.Count;
                var avgRating = reviews.Any() ? reviews.Average(r => r.Rating) : 0;
                var positiveReviews = reviews.Count(r => r.Rating >= 4);
                var positivePercentage = totalReviews > 0 ? (decimal)positiveReviews / totalReviews * 100 : 0;
                var negativeReviews = reviews.Count(r => r.Rating <= 2);
                var negativePercentage = totalReviews > 0 ? (decimal)negativeReviews / totalReviews * 100 : 0;

                using (var package = new ExcelPackage())
                {
                    var worksheet = package.Workbook.Worksheets.Add("Báo Cáo Đánh Giá");

                    // TIÊU ĐỀ
                    worksheet.Cells["A1:H1"].Merge = true;
                    worksheet.Cells["A1"].Value = "BÁO CÁO ĐÁNH GIÁ & PHẢN HỒI - FRESH FARM";
                    worksheet.Cells["A1"].Style.Font.Size = 18;
                    worksheet.Cells["A1"].Style.Font.Bold = true;
                    worksheet.Cells["A1"].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
                    worksheet.Cells["A1"].Style.Fill.PatternType = ExcelFillStyle.Solid;
                    worksheet.Cells["A1"].Style.Fill.BackgroundColor.SetColor(Color.FromArgb(111, 66, 193));
                    worksheet.Cells["A1"].Style.Font.Color.SetColor(Color.White);
                    worksheet.Row(1).Height = 30;

                    // THÔNG TIN BỘ LỌC
                    int currentRow = 2;
                    worksheet.Cells[$"A{currentRow}:H{currentRow}"].Merge = true;
                    worksheet.Cells[$"A{currentRow}"].Value = $"Ngày xuất: {DateTime.Now:dd/MM/yyyy HH:mm:ss}";
                    worksheet.Cells[$"A{currentRow}"].Style.Font.Italic = true;
                    currentRow++;

                    if (fromDate.HasValue || toDate.HasValue || !string.IsNullOrEmpty(starRating))
                    {
                        worksheet.Cells[$"A{currentRow}:H{currentRow}"].Merge = true;
                        var filterInfo = "Bộ lọc: ";
                        if (fromDate.HasValue)
                            filterInfo += $"Từ {fromDate.Value:dd/MM/yyyy} ";
                        if (toDate.HasValue)
                            filterInfo += $"đến {toDate.Value:dd/MM/yyyy} ";
                        if (!string.IsNullOrEmpty(starRating) && starRating != "all")
                            filterInfo += $"- Xếp hạng: {starRating} sao";
                        worksheet.Cells[$"A{currentRow}"].Value = filterInfo;
                        worksheet.Cells[$"A{currentRow}"].Style.Font.Italic = true;
                        currentRow++;
                    }

                    // THỐNG KÊ TỔNG QUAN
                    currentRow++;
                    worksheet.Cells[$"A{currentRow}:H{currentRow}"].Merge = true;
                    worksheet.Cells[$"A{currentRow}"].Value = "THỐNG KÊ TỔNG QUAN";
                    worksheet.Cells[$"A{currentRow}"].Style.Font.Bold = true;
                    worksheet.Cells[$"A{currentRow}"].Style.Font.Size = 14;
                    worksheet.Cells[$"A{currentRow}"].Style.Fill.PatternType = ExcelFillStyle.Solid;
                    worksheet.Cells[$"A{currentRow}"].Style.Fill.BackgroundColor.SetColor(Color.FromArgb(242, 242, 242));
                    currentRow++;

                    var statsStartRow = currentRow;

                    worksheet.Cells[$"A{currentRow}"].Value = "Tổng đánh giá:";
                    worksheet.Cells[$"A{currentRow}"].Style.Font.Bold = true;
                    worksheet.Cells[$"B{currentRow}"].Value = totalReviews;
                    worksheet.Cells[$"B{currentRow}"].Style.Font.Bold = true;
                    worksheet.Cells[$"B{currentRow}"].Style.Font.Color.SetColor(Color.FromArgb(111, 66, 193));

                    worksheet.Cells[$"D{currentRow}"].Value = "Xếp hạng trung bình:";
                    worksheet.Cells[$"D{currentRow}"].Style.Font.Bold = true;
                    worksheet.Cells[$"E{currentRow}"].Value = $"{avgRating:F1} ⭐";
                    worksheet.Cells[$"E{currentRow}"].Style.Font.Color.SetColor(Color.FromArgb(255, 193, 7));
                    currentRow++;

                    worksheet.Cells[$"A{currentRow}"].Value = "Đánh giá tích cực:";
                    worksheet.Cells[$"A{currentRow}"].Style.Font.Bold = true;
                    worksheet.Cells[$"B{currentRow}"].Value = $"{positiveReviews} ({positivePercentage:F1}%)";
                    worksheet.Cells[$"B{currentRow}"].Style.Font.Color.SetColor(Color.FromArgb(0, 176, 80));

                    worksheet.Cells[$"D{currentRow}"].Value = "Đánh giá tiêu cực:";
                    worksheet.Cells[$"D{currentRow}"].Style.Font.Bold = true;
                    worksheet.Cells[$"E{currentRow}"].Value = $"{negativeReviews} ({negativePercentage:F1}%)";
                    worksheet.Cells[$"E{currentRow}"].Style.Font.Color.SetColor(Color.FromArgb(220, 53, 69));
                    currentRow++;

                    using (var range = worksheet.Cells[$"A{statsStartRow}:E{currentRow - 1}"])
                    {
                        range.Style.Border.Top.Style = ExcelBorderStyle.Thin;
                        range.Style.Border.Left.Style = ExcelBorderStyle.Thin;
                        range.Style.Border.Right.Style = ExcelBorderStyle.Thin;
                        range.Style.Border.Bottom.Style = ExcelBorderStyle.Thin;
                    }

                    // PHÂN BỔ XẾPH HẠNG SAO
                    currentRow += 2;
                    worksheet.Cells[$"A{currentRow}:H{currentRow}"].Merge = true;
                    worksheet.Cells[$"A{currentRow}"].Value = "PHÂN BỔ XẾPH HẠNG SAO";
                    worksheet.Cells[$"A{currentRow}"].Style.Font.Bold = true;
                    worksheet.Cells[$"A{currentRow}"].Style.Font.Size = 14;
                    worksheet.Cells[$"A{currentRow}"].Style.Fill.PatternType = ExcelFillStyle.Solid;
                    worksheet.Cells[$"A{currentRow}"].Style.Fill.BackgroundColor.SetColor(Color.FromArgb(242, 242, 242));
                    currentRow++;

                    int starHeaderRow = currentRow;
                    worksheet.Cells[$"A{starHeaderRow}"].Value = "Xếp hạng";
                    worksheet.Cells[$"B{starHeaderRow}"].Value = "Số lượng";
                    worksheet.Cells[$"C{starHeaderRow}"].Value = "Tỷ lệ";

                    using (var range = worksheet.Cells[$"A{starHeaderRow}:C{starHeaderRow}"])
                    {
                        range.Style.Font.Bold = true;
                        range.Style.Fill.PatternType = ExcelFillStyle.Solid;
                        range.Style.Fill.BackgroundColor.SetColor(Color.FromArgb(217, 225, 242));
                        range.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
                        range.Style.Border.Top.Style = ExcelBorderStyle.Thin;
                        range.Style.Border.Left.Style = ExcelBorderStyle.Thin;
                        range.Style.Border.Right.Style = ExcelBorderStyle.Thin;
                        range.Style.Border.Bottom.Style = ExcelBorderStyle.Thin;
                    }
                    currentRow++;

                    int starDataStartRow = currentRow;
                    for (int i = 5; i >= 1; i--)
                    {
                        var count = reviews.Count(r => r.Rating == i);
                        var percentage = totalReviews > 0 ? (decimal)count / totalReviews * 100 : 0;

                        worksheet.Cells[$"A{currentRow}"].Value = $"{i} Sao";
                        worksheet.Cells[$"A{currentRow}"].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;

                        worksheet.Cells[$"B{currentRow}"].Value = count;
                        worksheet.Cells[$"B{currentRow}"].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;

                        worksheet.Cells[$"C{currentRow}"].Value = $"{percentage:F1}%";
                        worksheet.Cells[$"C{currentRow}"].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;

                        // Tô màu theo số sao
                        Color starColor;
                        switch (i)
                        {
                            case 5:
                                starColor = Color.FromArgb(198, 239, 206);
                                break;
                            case 4:
                                starColor = Color.FromArgb(209, 231, 221);
                                break;
                            case 3:
                                starColor = Color.FromArgb(255, 243, 205);
                                break;
                            case 2:
                                starColor = Color.FromArgb(255, 224, 178);
                                break;
                            default:
                                starColor = Color.FromArgb(255, 199, 206);
                                break;
                        }

                        using (var range = worksheet.Cells[$"A{currentRow}:C{currentRow}"])
                        {
                            range.Style.Fill.PatternType = ExcelFillStyle.Solid;
                            range.Style.Fill.BackgroundColor.SetColor(starColor);
                        }

                        currentRow++;
                    }

                    using (var range = worksheet.Cells[$"A{starDataStartRow}:C{currentRow - 1}"])
                    {
                        range.Style.Border.Top.Style = ExcelBorderStyle.Thin;
                        range.Style.Border.Left.Style = ExcelBorderStyle.Thin;
                        range.Style.Border.Right.Style = ExcelBorderStyle.Thin;
                        range.Style.Border.Bottom.Style = ExcelBorderStyle.Thin;
                    }

                    // CHI TIẾT ĐÁNH GIÁ
                    currentRow += 2;
                    worksheet.Cells[$"A{currentRow}:H{currentRow}"].Merge = true;
                    worksheet.Cells[$"A{currentRow}"].Value = "CHI TIẾT CÁC ĐÁNH GIÁ";
                    worksheet.Cells[$"A{currentRow}"].Style.Font.Bold = true;
                    worksheet.Cells[$"A{currentRow}"].Style.Font.Size = 14;
                    worksheet.Cells[$"A{currentRow}"].Style.Fill.PatternType = ExcelFillStyle.Solid;
                    worksheet.Cells[$"A{currentRow}"].Style.Fill.BackgroundColor.SetColor(Color.FromArgb(242, 242, 242));
                    currentRow++;

                    int headerRow = currentRow;
                    worksheet.Cells[$"A{headerRow}"].Value = "Khách hàng";
                    worksheet.Cells[$"B{headerRow}"].Value = "Email";
                    worksheet.Cells[$"C{headerRow}"].Value = "SĐT";
                    worksheet.Cells[$"D{headerRow}"].Value = "Sản phẩm";
                    worksheet.Cells[$"E{headerRow}"].Value = "Xếp hạng";
                    worksheet.Cells[$"F{headerRow}"].Value = "Nội dung";
                    worksheet.Cells[$"G{headerRow}"].Value = "Ngày đánh giá";
                    worksheet.Cells[$"H{headerRow}"].Value = "Trạng thái";

                    using (var range = worksheet.Cells[$"A{headerRow}:H{headerRow}"])
                    {
                        range.Style.Font.Bold = true;
                        range.Style.Fill.PatternType = ExcelFillStyle.Solid;
                        range.Style.Fill.BackgroundColor.SetColor(Color.FromArgb(217, 225, 242));
                        range.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
                        range.Style.Border.Top.Style = ExcelBorderStyle.Thin;
                        range.Style.Border.Left.Style = ExcelBorderStyle.Thin;
                        range.Style.Border.Right.Style = ExcelBorderStyle.Thin;
                        range.Style.Border.Bottom.Style = ExcelBorderStyle.Thin;
                    }
                    worksheet.Row(headerRow).Height = 20;

                    currentRow++;
                    int dataStartRow = currentRow;

                    var orderedReviews = reviews.OrderByDescending(r => r.CreatedAt).ToList();

                    foreach (var review in orderedReviews)
                    {
                        var user = db.Users.FirstOrDefault(u => u.UserID == review.UserID);
                        var product = db.Products.FirstOrDefault(p => p.ProductID == review.ProductID);

                        worksheet.Cells[$"A{currentRow}"].Value = user?.FullName ?? "N/A";
                        worksheet.Cells[$"B{currentRow}"].Value = user?.Email ?? "N/A";
                        worksheet.Cells[$"C{currentRow}"].Value = user?.Phone ?? "N/A";
                        worksheet.Cells[$"D{currentRow}"].Value = product?.ProductName ?? "N/A";

                        worksheet.Cells[$"E{currentRow}"].Value = $"{review.Rating} ⭐";
                        worksheet.Cells[$"E{currentRow}"].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;

                        // Tô màu theo rating
                        Color ratingColor;
                        if (review.Rating >= 4)
                            ratingColor = Color.FromArgb(198, 239, 206);
                        else if (review.Rating == 3)
                            ratingColor = Color.FromArgb(255, 243, 205);
                        else
                            ratingColor = Color.FromArgb(255, 199, 206);

                        worksheet.Cells[$"E{currentRow}"].Style.Fill.PatternType = ExcelFillStyle.Solid;
                        worksheet.Cells[$"E{currentRow}"].Style.Fill.BackgroundColor.SetColor(ratingColor);

                        worksheet.Cells[$"F{currentRow}"].Value = review.Comment ?? "";
                        worksheet.Cells[$"F{currentRow}"].Style.WrapText = true;

                        worksheet.Cells[$"G{currentRow}"].Value = review.CreatedAt.ToString("dd/MM/yyyy HH:mm");
                        worksheet.Cells[$"G{currentRow}"].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;

                        worksheet.Cells[$"H{currentRow}"].Value = review.IsApproved ? "Đã duyệt" : "Chờ duyệt";
                        worksheet.Cells[$"H{currentRow}"].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;

                        worksheet.Row(currentRow).Height = 30;
                        currentRow++;
                    }

                    if (orderedReviews.Any())
                    {
                        using (var range = worksheet.Cells[$"A{dataStartRow}:H{currentRow - 1}"])
                        {
                            range.Style.Border.Top.Style = ExcelBorderStyle.Thin;
                            range.Style.Border.Left.Style = ExcelBorderStyle.Thin;
                            range.Style.Border.Right.Style = ExcelBorderStyle.Thin;
                            range.Style.Border.Bottom.Style = ExcelBorderStyle.Thin;
                        }
                    }
                    else
                    {
                        worksheet.Cells[$"A{currentRow}:H{currentRow}"].Merge = true;
                        worksheet.Cells[$"A{currentRow}"].Value = "Không có dữ liệu đánh giá";
                        worksheet.Cells[$"A{currentRow}"].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
                        worksheet.Cells[$"A{currentRow}"].Style.Font.Italic = true;
                        currentRow++;
                    }

                    // FOOTER
                    currentRow += 2;
                    worksheet.Cells[$"A{currentRow}:H{currentRow}"].Merge = true;
                    worksheet.Cells[$"A{currentRow}"].Value = "© Fresh Farm - Hệ thống quản lý bán hàng";
                    worksheet.Cells[$"A{currentRow}"].Style.Font.Italic = true;
                    worksheet.Cells[$"A{currentRow}"].Style.Font.Size = 9;
                    worksheet.Cells[$"A{currentRow}"].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
                    worksheet.Cells[$"A{currentRow}"].Style.Font.Color.SetColor(Color.Gray);

                    // Điều chỉnh độ rộng cột
                    worksheet.Column(1).Width = 25;  // Khách hàng
                    worksheet.Column(2).Width = 30;  // Email
                    worksheet.Column(3).Width = 15;  // SĐT
                    worksheet.Column(4).Width = 30;  // Sản phẩm
                    worksheet.Column(5).Width = 12;  // Xếp hạng
                    worksheet.Column(6).Width = 50;  // Nội dung
                    worksheet.Column(7).Width = 18;  // Ngày
                    worksheet.Column(8).Width = 15;  // Trạng thái

                    worksheet.Cells[worksheet.Dimension.Address].Style.VerticalAlignment = ExcelVerticalAlignment.Center;

                    var fileContents = package.GetAsByteArray();
                    var fileName = $"BaoCaoDanhGia_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";

                    return File(fileContents,
                        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                        fileName);
                }
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = "Lỗi xuất Excel: " + ex.Message;
                return RedirectToAction("Review", new { fromDate, toDate, starRating });
            }
        }
        [HttpPost]
        [ValidateAntiForgeryToken]
        public JsonResult DeleteReview(int id)
        {
            using (var transaction = db.Database.BeginTransaction())
            {
                try
                {
                    var review = db.Reviews.FirstOrDefault(r => r.ReviewID == id);
                    if (review == null)
                        return Json(new { success = false, message = "Không tìm thấy bình luận." });

                    var reports = db.ReviewReports.Where(r => r.ReviewID == id).ToList();
                    if (reports.Any())
                        db.ReviewReports.RemoveRange(reports);

                    db.Reviews.Remove(review);
                    db.SaveChanges();
                    transaction.Commit();

                    return Json(new { success = true, message = "Đã xóa bình luận thành công!" });
                }
                catch (System.Data.Entity.Infrastructure.DbUpdateException ex)
                {
                    transaction.Rollback();
                    string inner = ex.InnerException?.InnerException?.Message ?? ex.Message;
                    return Json(new { success = false, message = "Lỗi ràng buộc CSDL: " + inner });
                }
                catch (Exception ex)
                {
                    transaction.Rollback();
                    return Json(new { success = false, message = "Lỗi khác: " + ex.Message });
                }
            }
        }



        protected override void Dispose(bool disposing)
        {
            if (disposing) db.Dispose();
            base.Dispose(disposing);
        }
    }
}






