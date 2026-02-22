namespace FreshFarm.Web.Bff.Dtos;

public sealed class ProfileUpdateRequestDto
{
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string? ReturnUrl { get; set; }
}

