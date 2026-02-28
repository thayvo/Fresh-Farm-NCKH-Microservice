using System.ComponentModel.DataAnnotations;

namespace FreshFarm.Web.Bff.Areas.Seller.Models;

public sealed class AdminLoginVM
{
    [Required(ErrorMessage = "Vui long nhap ten dang nhap")]
    [Display(Name = "Ten dang nhap")]
    public string UserName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui long nhap mat khau")]
    [DataType(DataType.Password)]
    [Display(Name = "Mat khau")]
    public string Password { get; set; } = string.Empty;

    [Display(Name = "Ghi nho toi")]
    public bool RememberMe { get; set; }
}
