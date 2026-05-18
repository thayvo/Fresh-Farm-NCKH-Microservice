namespace FreshFarm.Web.Bff.Services;

public static class AvatarImagePaths
{
    public const string UploadFolderRequestPath = "/uploads/avatar/";
    public const string FallbackImageRequestPath = "/uploads/avatar/no-avatar.jpg";

    public static string? ResolveRequestPath(string? avatar)
    {
        if (string.IsNullOrWhiteSpace(avatar))
        {
            return null;
        }

        var raw = avatar.Trim();
        var normalized = raw.Replace('\\', '/');
        var fileName = Path.GetFileName(normalized);
        if (IsFallbackImageName(fileName))
        {
            return null;
        }

        if (raw.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            raw.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
            raw.StartsWith("/", StringComparison.OrdinalIgnoreCase))
        {
            return raw;
        }

        return string.IsNullOrWhiteSpace(fileName)
            ? null
            : $"{UploadFolderRequestPath}{fileName}";
    }

    private static bool IsFallbackImageName(string? fileName)
    {
        return string.Equals(fileName, "no-avatar.jpg", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(fileName, "no-image.jpg", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(fileName, "no-image.png", StringComparison.OrdinalIgnoreCase);
    }
}
