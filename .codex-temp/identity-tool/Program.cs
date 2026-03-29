using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Tokens;

if (args.Length == 0)
{
    Console.Error.WriteLine("Usage:");
    Console.Error.WriteLine("  hash <password>");
    Console.Error.WriteLine("  jwt <userId> <userName> <email> <role>");
    Console.Error.WriteLine("  totp <base32Secret>");
    return 1;
}

switch (args[0].ToLowerInvariant())
{
    case "hash":
        if (args.Length < 2)
        {
            Console.Error.WriteLine("Missing password.");
            return 1;
        }

        var hasher = new PasswordHasher<object>();
        Console.Write(hasher.HashPassword(new object(), args[1]));
        return 0;

    case "jwt":
        if (args.Length < 5)
        {
            Console.Error.WriteLine("Missing jwt args.");
            return 1;
        }

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, args[1]),
            new("username", args[2]),
            new(JwtRegisteredClaimNames.Email, args[3]),
            new(ClaimTypes.Role, args[4])
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes("THIEN_LI_OI_EM_CO_THE_O_LAI_DAY_KHONG_123456"));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: "FreshFarm.Identity",
            audience: "FreshFarm",
            claims: claims,
            expires: DateTime.UtcNow.AddHours(2),
            signingCredentials: creds);

        Console.Write(new JwtSecurityTokenHandler().WriteToken(token));
        return 0;

    case "totp":
        if (args.Length < 2)
        {
            Console.Error.WriteLine("Missing TOTP secret.");
            return 1;
        }

        Console.Write(ComputeTotp(args[1], DateTime.UtcNow));
        return 0;

    default:
        Console.Error.WriteLine("Unknown command.");
        return 1;
}

static string ComputeTotp(string secret, DateTime utcNow)
{
    const int digits = 6;
    const int stepSeconds = 30;
    var key = Base32Decode(secret);
    var unixTime = new DateTimeOffset(utcNow).ToUnixTimeSeconds();
    var counter = unixTime / stepSeconds;
    Span<byte> counterBytes = stackalloc byte[8];
    for (var i = 7; i >= 0; i--)
    {
        counterBytes[i] = (byte)(counter & 0xFF);
        counter >>= 8;
    }

    using var hmac = new System.Security.Cryptography.HMACSHA1(key);
    var hash = hmac.ComputeHash(counterBytes.ToArray());
    var offset = hash[^1] & 0x0F;
    var binary =
        ((hash[offset] & 0x7F) << 24) |
        ((hash[offset + 1] & 0xFF) << 16) |
        ((hash[offset + 2] & 0xFF) << 8) |
        (hash[offset + 3] & 0xFF);
    var otp = binary % (int)Math.Pow(10, digits);
    return otp.ToString(new string('0', digits));
}

static byte[] Base32Decode(string input)
{
    const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
    var normalized = new string((input ?? string.Empty)
        .Trim()
        .ToUpperInvariant()
        .Where(ch => !char.IsWhiteSpace(ch) && ch != '=')
        .ToArray());

    var output = new List<byte>();
    var bits = 0;
    var value = 0;

    foreach (var ch in normalized)
    {
        var index = alphabet.IndexOf(ch);
        if (index < 0)
        {
            throw new InvalidOperationException("Secret 2FA khong dung dinh dang Base32.");
        }

        value = (value << 5) | index;
        bits += 5;

        while (bits >= 8)
        {
            output.Add((byte)((value >> (bits - 8)) & 0xFF));
            bits -= 8;
        }
    }

    return output.ToArray();
}
