using System.Security.Cryptography;
using System.Text;

namespace FreshFarm.Web.Bff.Services;

public sealed class SignUpCaptchaService : ISignUpCaptchaService
{
    private const string AllowedChars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    public string GenerateCode(int length = 5)
    {
        length = Math.Clamp(length, 4, 8);
        Span<byte> randomBytes = stackalloc byte[length];
        RandomNumberGenerator.Fill(randomBytes);

        var chars = new char[length];
        for (var i = 0; i < length; i++)
        {
            chars[i] = AllowedChars[randomBytes[i] % AllowedChars.Length];
        }

        return new string(chars);
    }

    public string BuildSvg(string captchaCode)
    {
        var safeCode = string.IsNullOrWhiteSpace(captchaCode) ? GenerateCode() : captchaCode.Trim().ToUpperInvariant();
        var random = RandomNumberGenerator.GetInt32(int.MaxValue);
        var builder = new StringBuilder();

        builder.AppendLine("""<svg xmlns="http://www.w3.org/2000/svg" width="180" height="64" viewBox="0 0 180 64" role="img" aria-label="Mã xác nhận">""");
        builder.AppendLine("""<rect width="180" height="64" rx="12" fill="#f5fbf7" />""");
        builder.AppendLine("""<rect x="1" y="1" width="178" height="62" rx="11" fill="none" stroke="#cfe4d7" />""");

        for (var i = 0; i < 8; i++)
        {
            var x1 = RandomNumberGenerator.GetInt32(0, 180);
            var y1 = RandomNumberGenerator.GetInt32(0, 64);
            var x2 = RandomNumberGenerator.GetInt32(0, 180);
            var y2 = RandomNumberGenerator.GetInt32(0, 64);
            var strokeWidth = RandomNumberGenerator.GetInt32(1, 3);
            builder.AppendLine($"""<line x1="{x1}" y1="{y1}" x2="{x2}" y2="{y2}" stroke="#b7d7c3" stroke-width="{strokeWidth}" opacity="0.8" />""");
        }

        for (var i = 0; i < safeCode.Length; i++)
        {
            var x = 20 + (i * 28);
            var y = RandomNumberGenerator.GetInt32(38, 50);
            var rotation = RandomNumberGenerator.GetInt32(-20, 21);
            var fontSize = RandomNumberGenerator.GetInt32(26, 33);
            var fill = i % 2 == 0 ? "#0a7a48" : "#14532d";
            builder.AppendLine(
                $"""<text x="{x}" y="{y}" fill="{fill}" font-family="Verdana, Arial, sans-serif" font-size="{fontSize}" font-weight="700" transform="rotate({rotation} {x} {y})">{safeCode[i]}</text>""");
        }

        for (var i = 0; i < 18; i++)
        {
            var cx = RandomNumberGenerator.GetInt32(0, 180);
            var cy = RandomNumberGenerator.GetInt32(0, 64);
            var radius = RandomNumberGenerator.GetInt32(1, 3);
            builder.AppendLine($"""<circle cx="{cx}" cy="{cy}" r="{radius}" fill="#dbeee2" />""");
        }

        builder.AppendLine($"""<desc>FreshFarm sign up captcha seed {random}</desc>""");
        builder.AppendLine("</svg>");
        return builder.ToString();
    }

    public bool Matches(string? expectedCode, string? submittedCode)
    {
        if (string.IsNullOrWhiteSpace(expectedCode) || string.IsNullOrWhiteSpace(submittedCode))
        {
            return false;
        }

        return string.Equals(
            expectedCode.Trim(),
            submittedCode.Trim(),
            StringComparison.OrdinalIgnoreCase);
    }
}
