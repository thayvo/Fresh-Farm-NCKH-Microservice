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
    public string UserName { get; set; } = string.Empty;

    [Required]
    public string FullName { get; set; } = string.Empty;

    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;

    public string? Phone { get; set; }

    public string? Avatar { get; set; }

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
}
