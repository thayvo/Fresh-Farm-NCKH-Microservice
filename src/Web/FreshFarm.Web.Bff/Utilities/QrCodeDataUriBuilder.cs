using System.Text;
using QRCoder;

namespace FreshFarm.Web.Bff.Utilities;

internal static class QrCodeDataUriBuilder
{
    public static string? BuildSvgDataUri(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(content.Trim(), QRCodeGenerator.ECCLevel.Q);
        var svg = new SvgQRCode(data).GetGraphic(8);
        return $"data:image/svg+xml;base64,{Convert.ToBase64String(Encoding.UTF8.GetBytes(svg))}";
    }
}
