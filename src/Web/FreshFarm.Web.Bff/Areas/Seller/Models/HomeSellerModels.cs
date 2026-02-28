using System.ComponentModel.DataAnnotations;

namespace FreshFarm.Web.Bff.Areas.Seller.Models;

public sealed class DashboardViewModel
{
    public decimal MonthlyRevenue { get; set; }

    public int NewOrdersCount { get; set; }

    public int TotalCustomers { get; set; }

    public int LowStockProducts { get; set; }

    public List<string> ChartLabels { get; set; } = new();

    public List<decimal> ChartData { get; set; } = new();

    public List<string> CategoryLabels { get; set; } = new();

    public List<decimal> CategoryData { get; set; } = new();

    public List<RecentOrderViewModel> RecentOrders { get; set; } = new();

    public int OpenSupportConversations { get; set; }

    public int UnreadSupportMessages { get; set; }
}

public sealed class RecentOrderViewModel
{
    public string OrderCode { get; set; } = string.Empty;

    public string CustomerName { get; set; } = string.Empty;

    public DateTime OrderDate { get; set; }

    public decimal TotalAmount { get; set; }

    public string Status { get; set; } = string.Empty;

    public string StatusBadgeClass { get; set; } = "bg-secondary";
}

public sealed class ProfileViewModel
{
    public int UserID { get; set; }

    public string UserName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Ho va ten khong duoc de trong.")]
    [StringLength(100, ErrorMessage = "Ho va ten toi da 100 ky tu.")]
    public string FullName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Email khong duoc de trong.")]
    [EmailAddress(ErrorMessage = "Email khong dung dinh dang.")]
    [StringLength(100, ErrorMessage = "Email toi da 100 ky tu.")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "So dien thoai khong duoc de trong.")]
    [RegularExpression(@"^0\d{9}$", ErrorMessage = "So dien thoai phai gom 10 chu so va bat dau bang 0.")]
    public string Phone { get; set; } = string.Empty;

    public DateTime CreatedDate { get; set; }

    public string LastActivity { get; set; } = string.Empty;
}

public sealed class ChangePasswordViewModel
{
    [Required(ErrorMessage = "Vui long nhap mat khau hien tai.")]
    public string CurrentPassword { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui long nhap mat khau moi.")]
    [StringLength(100, MinimumLength = 6, ErrorMessage = "Mat khau moi phai tu 6 den 100 ky tu.")]
    public string NewPassword { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui long xac nhan mat khau moi.")]
    [Compare(nameof(NewPassword), ErrorMessage = "Mat khau xac nhan khong khop.")]
    public string ConfirmPassword { get; set; } = string.Empty;
}
