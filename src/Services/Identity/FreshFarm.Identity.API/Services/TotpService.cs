using System.Security.Cryptography;
using System.Text;

namespace FreshFarm.Identity.Api.Services;

public interface ITotpService
{
    string GenerateSecret();
    string FormatManualEntryKey(string secret);
    string BuildOtpAuthUri(string issuer, string accountName, string secret);
    bool VerifyCode(string secret, string code, DateTime utcNow);
}

public sealed class TotpService : ITotpService
{
    private const int SecretBytesLength = 20;
    private const int TimeStepSeconds = 30;
    private const int Digits = 6;
    private const int AllowedDriftWindows = 1;
    private const string Base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    public string GenerateSecret()
    {
        var bytes = RandomNumberGenerator.GetBytes(SecretBytesLength);
        return Base32Encode(bytes);
    }

    public string FormatManualEntryKey(string secret)
    {
        var normalized = NormalizeSecret(secret);
        if (string.IsNullOrEmpty(normalized))
        {
            return string.Empty;
        }

        var groups = new List<string>();
        for (var index = 0; index < normalized.Length; index += 4)
        {
            groups.Add(normalized.Substring(index, Math.Min(4, normalized.Length - index)));
        }

        return string.Join(" ", groups);
    }

    public string BuildOtpAuthUri(string issuer, string accountName, string secret)
    {
        var normalizedIssuer = string.IsNullOrWhiteSpace(issuer) ? "FreshFarm" : issuer.Trim();
        var normalizedAccount = string.IsNullOrWhiteSpace(accountName) ? "user" : accountName.Trim();
        var normalizedSecret = NormalizeSecret(secret);

        var label = Uri.EscapeDataString($"{normalizedIssuer}:{normalizedAccount}");
        var issuerParam = Uri.EscapeDataString(normalizedIssuer);
        var secretParam = Uri.EscapeDataString(normalizedSecret);

        return $"otpauth://totp/{label}?secret={secretParam}&issuer={issuerParam}&algorithm=SHA1&digits=6&period=30";
    }

    public bool VerifyCode(string secret, string code, DateTime utcNow)
    {
        var normalizedSecret = NormalizeSecret(secret);
        var normalizedCode = NormalizeCode(code);
        if (normalizedSecret.Length == 0 || normalizedCode.Length != Digits)
        {
            return false;
        }

        var secretBytes = Base32Decode(normalizedSecret);
        var counter = (long)Math.Floor((utcNow - DateTime.UnixEpoch).TotalSeconds / TimeStepSeconds);
        for (var drift = -AllowedDriftWindows; drift <= AllowedDriftWindows; drift++)
        {
            if (ComputeTotp(secretBytes, counter + drift) == normalizedCode)
            {
                return true;
            }
        }

        return false;
    }

    private static string NormalizeSecret(string secret)
    {
        return (secret ?? string.Empty)
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Trim()
            .ToUpperInvariant();
    }

    private static string NormalizeCode(string code)
    {
        var builder = new StringBuilder();
        foreach (var ch in code ?? string.Empty)
        {
            if (char.IsDigit(ch))
            {
                builder.Append(ch);
            }
        }

        return builder.ToString();
    }

    private static string ComputeTotp(byte[] secretBytes, long counter)
    {
        Span<byte> counterBytes = stackalloc byte[8];
        for (var index = 7; index >= 0; index--)
        {
            counterBytes[index] = (byte)(counter & 0xff);
            counter >>= 8;
        }

        using var hmac = new HMACSHA1(secretBytes);
        var hash = hmac.ComputeHash(counterBytes.ToArray());
        var offset = hash[^1] & 0x0F;
        var binaryCode =
            ((hash[offset] & 0x7f) << 24) |
            ((hash[offset + 1] & 0xff) << 16) |
            ((hash[offset + 2] & 0xff) << 8) |
            (hash[offset + 3] & 0xff);

        var otp = binaryCode % (int)Math.Pow(10, Digits);
        return otp.ToString($"D{Digits}");
    }

    private static string Base32Encode(IReadOnlyList<byte> data)
    {
        if (data.Count == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder((data.Count * 8 + 4) / 5);
        var buffer = 0;
        var bitsLeft = 0;

        foreach (var value in data)
        {
            buffer = (buffer << 8) | value;
            bitsLeft += 8;

            while (bitsLeft >= 5)
            {
                builder.Append(Base32Alphabet[(buffer >> (bitsLeft - 5)) & 0x1f]);
                bitsLeft -= 5;
            }
        }

        if (bitsLeft > 0)
        {
            builder.Append(Base32Alphabet[(buffer << (5 - bitsLeft)) & 0x1f]);
        }

        return builder.ToString();
    }

    private static byte[] Base32Decode(string input)
    {
        var normalized = NormalizeSecret(input);
        if (normalized.Length == 0)
        {
            return Array.Empty<byte>();
        }

        var bytes = new List<byte>(normalized.Length * 5 / 8);
        var buffer = 0;
        var bitsLeft = 0;

        foreach (var ch in normalized)
        {
            var index = Base32Alphabet.IndexOf(ch);
            if (index < 0)
            {
                throw new InvalidOperationException("Secret 2FA khong dung dinh dang Base32.");
            }

            buffer = (buffer << 5) | index;
            bitsLeft += 5;

            if (bitsLeft >= 8)
            {
                bytes.Add((byte)((buffer >> (bitsLeft - 8)) & 0xff));
                bitsLeft -= 8;
            }
        }

        return bytes.ToArray();
    }
}
