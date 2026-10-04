using System.ComponentModel.DataAnnotations;

namespace FreshFarm.Identity.Api.Dtos;

public sealed class ForgotPasswordRequest
{
    [Required(ErrorMessage = "Email là bắt buộc.")]
    [EmailAddress(ErrorMessage = "Email không đúng định dạng.")]
    [StringLength(100, ErrorMessage = "Email tối đa 100 ký tự.")]
    public string Email { get; set; } = string.Empty;
}

public sealed class ResetPasswordRequest
{
    [Required(ErrorMessage = "Email là bắt buộc.")]
    [EmailAddress(ErrorMessage = "Email không đúng định dạng.")]
    [StringLength(100, ErrorMessage = "Email tối đa 100 ký tự.")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Mã đặt lại mật khẩu là bắt buộc.")]
    public string Token { get; set; } = string.Empty;

    [Required(ErrorMessage = "Mật khẩu mới là bắt buộc.")]
    [StringLength(100, MinimumLength = 8, ErrorMessage = "Mật khẩu phải từ 8 đến 100 ký tự.")]
    [RegularExpression(@"^(?=.*[A-Za-z])(?=.*\d).+$", ErrorMessage = "Mật khẩu phải có ít nhất một chữ cái và một chữ số.")]
    public string NewPassword { get; set; } = string.Empty;

    [Required(ErrorMessage = "Xác nhận mật khẩu là bắt buộc.")]
    [Compare(nameof(NewPassword), ErrorMessage = "Xác nhận mật khẩu không khớp.")]
    public string ConfirmPassword { get; set; } = string.Empty;
}
