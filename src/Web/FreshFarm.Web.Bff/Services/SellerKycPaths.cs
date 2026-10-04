namespace FreshFarm.Web.Bff.Services;

public static class SellerKycPaths
{
    public const string LegacyUploadFolderRelativePath = "uploads/seller-kyc";
    public const string StoredReferencePrefix = "seller-kyc:";

    private static readonly System.Text.RegularExpressions.Regex SafeFileNamePattern = new(
        @"^[a-z0-9][a-z0-9-]{0,120}\.(?:jpe?g|png|webp|pdf)$",
        System.Text.RegularExpressions.RegexOptions.Compiled |
        System.Text.RegularExpressions.RegexOptions.CultureInvariant |
        System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    public static string? NormalizeStoredFileName(string? requestPath)
    {
        if (string.IsNullOrWhiteSpace(requestPath))
        {
            return null;
        }

        var normalized = requestPath.Trim();
        string candidate;

        if (normalized.StartsWith(StoredReferencePrefix, StringComparison.OrdinalIgnoreCase))
        {
            candidate = normalized[StoredReferencePrefix.Length..];
        }
        else
        {
            normalized = normalized.Replace('\\', '/').TrimStart('/');
            if (!normalized.StartsWith(
                    LegacyUploadFolderRelativePath + "/",
                    StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            candidate = normalized[(LegacyUploadFolderRelativePath.Length + 1)..];
        }

        if (candidate.Length == 0 ||
            candidate != Path.GetFileName(candidate) ||
            candidate.Contains('%', StringComparison.Ordinal) ||
            !SafeFileNamePattern.IsMatch(candidate))
        {
            return null;
        }

        return candidate;
    }

    public static string BuildStoredReference(string storedFileName)
    {
        if (!SafeFileNamePattern.IsMatch(storedFileName) || storedFileName != Path.GetFileName(storedFileName))
        {
            throw new ArgumentException("Tên file KYC không hợp lệ.", nameof(storedFileName));
        }

        return StoredReferencePrefix + storedFileName;
    }

    public static bool AreEquivalentStoredReferences(string? firstReference, string? secondReference)
    {
        var firstFileName = NormalizeStoredFileName(firstReference);
        var secondFileName = NormalizeStoredFileName(secondReference);

        return !string.IsNullOrWhiteSpace(firstFileName) &&
               !string.IsNullOrWhiteSpace(secondFileName) &&
               string.Equals(firstFileName, secondFileName, StringComparison.OrdinalIgnoreCase);
    }
}
