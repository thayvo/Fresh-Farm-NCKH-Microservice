using System.ComponentModel.DataAnnotations;

namespace FreshFarm.Identity.Api.Dtos;

public sealed class VerifyEmailRequest
{
    [Required(ErrorMessage = "Email là bắt buộc.")]
    [EmailAddress(ErrorMessage = "Email không đúng định dạng.")]
    [StringLength(100, ErrorMessage = "Email tối đa 100 ký tự.")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Token xác minh email là bắt buộc.")]
    public string Token { get; set; } = string.Empty;
}

public sealed class ResendEmailVerificationRequest
{
    [Required(ErrorMessage = "Email hoặc tên đăng nhập là bắt buộc.")]
    [StringLength(100, ErrorMessage = "Định danh tối đa 100 ký tự.")]
    public string Identifier { get; set; } = string.Empty;
}
