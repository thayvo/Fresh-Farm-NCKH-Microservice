namespace FreshFarm.Web.Bff.Services;

public static class SellerKycPaths
{
    public const string UploadFolderRelativePath = "uploads/seller-kyc";

    public static string? NormalizeStoredFileName(string? requestPath)
    {
        if (string.IsNullOrWhiteSpace(requestPath))
        {
            return null;
        }

        var normalized = requestPath.Trim().Replace('\\', '/');
        if (normalized.StartsWith('/'))
        {
            normalized = normalized[1..];
        }

        if (!normalized.StartsWith(UploadFolderRelativePath, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return normalized[UploadFolderRelativePath.Length..].TrimStart('/');
    }

    public static string BuildRequestPath(string storedFileName)
    {
        return "/" + $"{UploadFolderRelativePath}/{storedFileName}".Replace('\\', '/');
    }
}
