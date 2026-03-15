using System.Globalization;
using FreshFarm.Identity.Api.Models;
using FreshFarm.Identity.Api.Options;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;

namespace FreshFarm.Identity.Api.Services;

public interface IPasswordResetTokenService
{
    string GenerateToken(User user, UserAuth? userAuth);

    bool TryValidateToken(string token, User user, UserAuth? userAuth, out string? errorMessage);
}

public sealed class PasswordResetTokenService : IPasswordResetTokenService
{
    private readonly ITimeLimitedDataProtector _protector;
    private readonly PasswordResetOptions _options;

    public PasswordResetTokenService(
        IDataProtectionProvider dataProtectionProvider,
        IOptions<PasswordResetOptions> options)
    {
        _protector = dataProtectionProvider
            .CreateProtector("FreshFarm.Identity.PasswordReset")
            .ToTimeLimitedDataProtector();
        _options = options.Value;
    }

    public string GenerateToken(User user, UserAuth? userAuth)
    {
        var payload = string.Join("|",
            user.UserId.ToString(CultureInfo.InvariantCulture),
            NormalizeEmail(user.Email),
            BuildSecurityVersion(user, userAuth));

        var lifetimeMinutes = _options.TokenLifetimeMinutes <= 0 ? 30 : _options.TokenLifetimeMinutes;
        return _protector.Protect(payload, TimeSpan.FromMinutes(lifetimeMinutes));
    }

    public bool TryValidateToken(string token, User user, UserAuth? userAuth, out string? errorMessage)
    {
        errorMessage = null;

        if (string.IsNullOrWhiteSpace(token))
        {
            errorMessage = "Lien ket dat lai mat khau khong hop le.";
            return false;
        }

        try
        {
            var payload = _protector.Unprotect(token);
            var parts = payload.Split('|', StringSplitOptions.None);
            if (parts.Length != 3)
            {
                errorMessage = "Lien ket dat lai mat khau khong hop le.";
                return false;
            }

            if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var userId) || userId != user.UserId)
            {
                errorMessage = "Lien ket dat lai mat khau khong hop le.";
                return false;
            }

            if (!string.Equals(parts[1], NormalizeEmail(user.Email), StringComparison.Ordinal))
            {
                errorMessage = "Lien ket dat lai mat khau khong hop le.";
                return false;
            }

            if (!string.Equals(parts[2], BuildSecurityVersion(user, userAuth), StringComparison.Ordinal))
            {
                errorMessage = "Lien ket dat lai mat khau da het hieu luc. Vui long yeu cau lai email moi.";
                return false;
            }

            return true;
        }
        catch
        {
            errorMessage = "Lien ket dat lai mat khau khong hop le hoac da het han.";
            return false;
        }
    }

    private static string BuildSecurityVersion(User user, UserAuth? userAuth)
    {
        var versionMoment = userAuth?.UpdatedAt
            ?? user.UpdatedAt
            ?? user.CreatedAt;

        return versionMoment.ToUniversalTime().Ticks.ToString(CultureInfo.InvariantCulture);
    }

    private static string NormalizeEmail(string? email)
    {
        return (email ?? string.Empty).Trim().ToUpperInvariant();
    }
}
