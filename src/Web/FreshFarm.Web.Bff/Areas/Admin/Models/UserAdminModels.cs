using System.ComponentModel.DataAnnotations;

namespace FreshFarm.Web.Bff.Areas.Admin.Models;

public sealed class AdminRoleViewModel
{
    public int RoleID { get; set; }

    public string RoleName { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;
}

public sealed class AdminUserViewModel
{
    public int AdminID { get; set; }

    [Required]
    [RegularExpression(@"^[a-zA-Z0-9._-]+$", ErrorMessage = "Tên đăng nhập chỉ được chứa chữ cái, số, dấu chấm, gạch dưới hoặc gạch ngang")]
    public string UserName { get; set; } = string.Empty;

    [Required]
    public string FullName { get; set; } = string.Empty;

    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;

    public string? Phone { get; set; }

    public string? Avatar { get; set; }

    public string AvatarUrl { get; set; } = "/uploads/avatar/no-avatar.jpg";

    public int RoleID { get; set; }

    public AdminRoleViewModel? Role { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;

    public DateTime? UpdatedDate { get; set; }

    public DateTime? LastLogin { get; set; }
}

public sealed class AdminUserManagementPageViewModel
{
    public string UserType { get; set; } = "all";

    public string SearchTerm { get; set; } = string.Empty;

    public bool? IsActive { get; set; }

    public DateTime? CreatedFrom { get; set; }

    public DateTime? CreatedTo { get; set; }

    public List<AdminRoleViewModel> Roles { get; set; } = new();

    public List<AdminUserViewModel> Users { get; set; } = new();

    public int Page { get; set; } = 1;

    public int PageSize { get; set; } = 15;

    public int TotalUsers { get; set; }

    public int ActiveUsers { get; set; }

    public int InactiveUsers => Math.Max(0, TotalUsers - ActiveUsers);

    public int AdminUsers { get; set; }

    public int SellerUsers { get; set; }

    public int BuyerUsers { get; set; }

    public int TotalPages => Math.Max(1, (int)Math.Ceiling(TotalUsers / (double)Math.Max(1, PageSize)));

    public int FirstItemNumber => TotalUsers == 0 ? 0 : ((Math.Max(1, Page) - 1) * Math.Max(1, PageSize)) + 1;

    public int LastItemNumber => TotalUsers == 0 ? 0 : Math.Min(TotalUsers, Math.Max(1, Page) * Math.Max(1, PageSize));

    public bool HasPreviousPage => Page > 1;

    public bool HasNextPage => Page < TotalPages;
}
