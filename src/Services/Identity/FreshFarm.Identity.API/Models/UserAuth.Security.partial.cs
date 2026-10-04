namespace FreshFarm.Identity.Api.Models;

public partial class UserAuth
{
    public int TokenVersion { get; set; }

    public string? PasswordResetNonce { get; set; }

    public string? ExternalLoginProvider { get; set; }

    public string? ExternalLoginSubject { get; set; }
}
