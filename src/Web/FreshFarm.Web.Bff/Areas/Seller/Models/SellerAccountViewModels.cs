using System.ComponentModel.DataAnnotations;

namespace FreshFarm.Web.Bff.Areas.Seller.Models;

public sealed class SellerLoginViewModel
{
    [Required(ErrorMessage = "Vui lòng nhập tên đăng nhập hoặc email.")]
    [Display(Name = "Tên đăng nhập hoặc email")]
    public string UserName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng nhập mật khẩu.")]
    [DataType(DataType.Password)]
    [Display(Name = "Mật khẩu")]
    public string Password { get; set; } = string.Empty;

    [Display(Name = "Ghi nhớ tôi")]
    public bool RememberMe { get; set; }
}

public sealed class SellerTwoFactorViewModel
{
    [Required(ErrorMessage = "Vui lòng nhập mã xác thực 6 số.")]
    [Display(Name = "Mã xác thực 6 số")]
    public string Code { get; set; } = string.Empty;

    public bool RequiresSetup { get; set; }
    public bool RememberMe { get; set; }
    public string? ManualEntryKey { get; set; }
    public string? OtpAuthUri { get; set; }
    public string? QrCodeImageDataUri { get; set; }
    public string? AuthenticatorIssuer { get; set; }
    public string? AuthenticatorAccountName { get; set; }
    public string? ChallengeMessage { get; set; }
}
