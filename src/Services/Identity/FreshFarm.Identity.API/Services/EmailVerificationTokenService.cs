using System.Globalization;
using System.Text;
using FreshFarm.Identity.Api.Models;
using FreshFarm.Identity.Api.Options;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace FreshFarm.Identity.Api.Services;

public interface IEmailVerificationTokenService
{
    string GenerateToken(User user);

    bool TryValidateToken(string token, User user, out string? errorMessage);
}

public sealed class EmailVerificationTokenService : IEmailVerificationTokenService
{
    private readonly ITimeLimitedDataProtector _protector;
    private readonly EmailVerificationOptions _options;
    private readonly ILogger<EmailVerificationTokenService> _logger;

    public EmailVerificationTokenService(
        IDataProtectionProvider dataProtectionProvider,
        IOptions<EmailVerificationOptions> options,
        ILogger<EmailVerificationTokenService> logger)
    {
        _protector = dataProtectionProvider
            .CreateProtector("FreshFarm.Identity.EmailVerification")
            .ToTimeLimitedDataProtector();
        _options = options.Value;
        _logger = logger;
    }

    public string GenerateToken(User user)
    {
        var payload = string.Join("|",
            user.UserId.ToString(CultureInfo.InvariantCulture),
            NormalizeEmail(user.Email),
            BuildSecurityVersion(user));

        var lifetimeMinutes = _options.TokenLifetimeMinutes <= 0 ? 60 : _options.TokenLifetimeMinutes;
        var protectedPayload = _protector.Protect(payload, TimeSpan.FromMinutes(lifetimeMinutes));
        return WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(protectedPayload));
    }

    public bool TryValidateToken(string token, User user, out string? errorMessage)
    {
        errorMessage = null;

        if (string.IsNullOrWhiteSpace(token))
        {
            errorMessage = "Lien ket xac minh email khong hop le.";
            return false;
        }

        try
        {
            var payload = _protector.Unprotect(DecodeToken(token));
            var parts = payload.Split('|', StringSplitOptions.None);
            if (parts.Length != 3)
            {
                errorMessage = "Lien ket xac minh email khong hop le.";
                return false;
            }

            if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var userId) || userId != user.UserId)
            {
                errorMessage = "Lien ket xac minh email khong hop le.";
                return false;
            }

            if (!string.Equals(parts[1], NormalizeEmail(user.Email), StringComparison.Ordinal))
            {
                errorMessage = "Lien ket xac minh email khong hop le.";
                return false;
            }

            if (!string.Equals(parts[2], BuildSecurityVersion(user), StringComparison.Ordinal))
            {
                errorMessage = "Lien ket xac minh email da het hieu luc. Vui long yeu cau gui lai email moi.";
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Khong the giai ma token xac minh email.");
            errorMessage = "Lien ket xac minh email khong hop le hoac da het han.";
            return false;
        }
    }

    private static string BuildSecurityVersion(User user)
    {
        var confirmedState = user.EmailConfirmed ? "1" : "0";
        return $"{user.CreatedAt.Ticks.ToString(CultureInfo.InvariantCulture)}:{confirmedState}";
    }

    private static string NormalizeEmail(string? email)
    {
        return (email ?? string.Empty).Trim().ToUpperInvariant();
    }

    private static string DecodeToken(string token)
    {
        try
        {
            return Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(token));
        }
        catch (FormatException)
        {
            return token;
        }
        catch (ArgumentException)
        {
            return token;
        }
    }
}
