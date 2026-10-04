using Microsoft.Extensions.Options;

namespace FreshFarm.Web.Bff.Options;

public sealed class SellerKycStorageOptionsValidator : IValidateOptions<SellerKycStorageOptions>
{
    private readonly IWebHostEnvironment _environment;

    public SellerKycStorageOptionsValidator(IWebHostEnvironment environment)
    {
        _environment = environment;
    }

    public ValidateOptionsResult Validate(string? name, SellerKycStorageOptions options)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(options.RootPath))
            {
                return ValidateOptionsResult.Fail(
                    "Storage:SellerKyc:RootPath không được để trống.");
            }

            var contentRootPath = Path.GetFullPath(_environment.ContentRootPath);
            var storageRootPath = Path.GetFullPath(
                Path.IsPathRooted(options.RootPath)
                    ? options.RootPath
                    : Path.Combine(contentRootPath, options.RootPath));
            var webRootPath = Path.GetFullPath(
                string.IsNullOrWhiteSpace(_environment.WebRootPath)
                    ? Path.Combine(contentRootPath, "wwwroot")
                    : _environment.WebRootPath);

            if (IsSameOrChildPath(storageRootPath, webRootPath))
            {
                return ValidateOptionsResult.Fail(
                    "Storage:SellerKyc:RootPath phải nằm ngoài webroot.");
            }

            return ValidateOptionsResult.Success;
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return ValidateOptionsResult.Fail(
                $"Storage:SellerKyc:RootPath không hợp lệ: {exception.Message}");
        }
    }

    private static bool IsSameOrChildPath(string candidatePath, string rootPath)
    {
        var normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath));
        var normalizedCandidate = Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidatePath));
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        return string.Equals(normalizedCandidate, normalizedRoot, comparison) ||
               normalizedCandidate.StartsWith(
                   normalizedRoot + Path.DirectorySeparatorChar,
                   comparison);
    }
}
