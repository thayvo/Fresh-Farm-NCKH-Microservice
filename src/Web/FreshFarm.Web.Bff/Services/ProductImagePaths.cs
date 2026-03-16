namespace FreshFarm.Web.Bff.Services;

public static class ProductImagePaths
{
    public const string UploadFolderRelativePath = "uploads/products";
    public const string UploadFolderRequestPath = "/uploads/products/";
    public const string UploadFolderContentPath = "~/uploads/products/";
    public const string FallbackImageRequestPath = "/images/legacy/no-image.png";
    public const string FallbackImageContentPath = "~/images/legacy/no-image.png";

    public static string? NormalizeStoredFileName(string? imageFileName)
    {
        if (string.IsNullOrWhiteSpace(imageFileName))
        {
            return null;
        }

        var raw = imageFileName.Trim();
        if (string.Equals(raw, "no-image.png", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (raw.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            raw.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (raw.StartsWith(UploadFolderRequestPath, StringComparison.OrdinalIgnoreCase))
        {
            raw = raw[UploadFolderRequestPath.Length..];
        }

        raw = raw.Replace('\\', '/');
        raw = Path.GetFileName(raw);

        return string.IsNullOrWhiteSpace(raw) ? null : raw;
    }

    public static string ResolveRequestPath(string? imageFileName)
    {
        if (string.IsNullOrWhiteSpace(imageFileName))
        {
            return FallbackImageRequestPath;
        }

        var raw = imageFileName.Trim();
        if (string.Equals(raw, "no-image.png", StringComparison.OrdinalIgnoreCase))
        {
            return FallbackImageRequestPath;
        }

        if (raw.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            raw.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
            raw.StartsWith("/", StringComparison.OrdinalIgnoreCase))
        {
            return raw;
        }

        var normalized = NormalizeStoredFileName(raw);
        return string.IsNullOrWhiteSpace(normalized)
            ? FallbackImageRequestPath
            : $"{UploadFolderRequestPath}{normalized}";
    }
}
