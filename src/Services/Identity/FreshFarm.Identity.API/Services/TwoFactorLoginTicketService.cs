using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.WebUtilities;

namespace FreshFarm.Identity.Api.Services;

public sealed class TwoFactorLoginTicket
{
    public int UserId { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public bool RequiresSetup { get; set; }
    public string? SetupSecret { get; set; }
}

public interface ITwoFactorLoginTicketService
{
    string CreateTicket(int userId, bool requiresSetup, string? setupSecret);
    bool TryReadTicket(string protectedTicket, out TwoFactorLoginTicket? ticket);
}

public sealed class TwoFactorLoginTicketService : ITwoFactorLoginTicketService
{
    private static readonly TimeSpan TicketLifetime = TimeSpan.FromMinutes(10);

    private readonly IDataProtector _protector;

    public TwoFactorLoginTicketService(IDataProtectionProvider dataProtectionProvider)
    {
        _protector = dataProtectionProvider.CreateProtector("FreshFarm.Identity.TwoFactorLoginTicket.v1");
    }

    public string CreateTicket(int userId, bool requiresSetup, string? setupSecret)
    {
        var payload = new TwoFactorLoginTicket
        {
            UserId = userId,
            ExpiresAtUtc = DateTime.UtcNow.Add(TicketLifetime),
            RequiresSetup = requiresSetup,
            SetupSecret = requiresSetup ? setupSecret : null
        };

        var json = JsonSerializer.Serialize(payload);
        var protectedBytes = _protector.Protect(System.Text.Encoding.UTF8.GetBytes(json));
        return WebEncoders.Base64UrlEncode(protectedBytes);
    }

    public bool TryReadTicket(string protectedTicket, out TwoFactorLoginTicket? ticket)
    {
        ticket = null;
        if (string.IsNullOrWhiteSpace(protectedTicket))
        {
            return false;
        }

        try
        {
            var protectedBytes = WebEncoders.Base64UrlDecode(protectedTicket);
            var json = System.Text.Encoding.UTF8.GetString(_protector.Unprotect(protectedBytes));
            var payload = JsonSerializer.Deserialize<TwoFactorLoginTicket>(json);
            if (payload is null || payload.ExpiresAtUtc <= DateTime.UtcNow)
            {
                return false;
            }

            if (payload.RequiresSetup && string.IsNullOrWhiteSpace(payload.SetupSecret))
            {
                return false;
            }

            ticket = payload;
            return true;
        }
        catch
        {
            return false;
        }
    }
}
