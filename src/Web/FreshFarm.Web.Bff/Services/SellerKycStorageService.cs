using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using FreshFarm.Web.Bff.Options;

namespace FreshFarm.Web.Bff.Services;

public sealed class SellerKycStorageService : ISellerKycStorageService
{
    private const int MaxFileSize = 10 * 1024 * 1024;

    private static readonly string[] AllowedImageExtensions = [".jpg", ".jpeg", ".png", ".webp"];
    private static readonly string[] AllowedDocumentExtensions = [".jpg", ".jpeg", ".png", ".webp", ".pdf"];

    private static readonly IReadOnlyDictionary<string, string> ContentTypes =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [".jpg"] = "image/jpeg",
            [".jpeg"] = "image/jpeg",
            [".png"] = "image/png",
            [".webp"] = "image/webp",
            [".pdf"] = "application/pdf"
        };

    private readonly string _uploadRootPath;

    public SellerKycStorageService(
        IWebHostEnvironment environment,
        IOptions<SellerKycStorageOptions> options)
    {
        var contentRootPath = Path.GetFullPath(environment.ContentRootPath);
        var configuredRootPath = options.Value.RootPath?.Trim();
        if (string.IsNullOrWhiteSpace(configuredRootPath))
        {
            configuredRootPath = Path.Combine("App_Data", "SellerKyc");
        }

        _uploadRootPath = Path.GetFullPath(
            Path.IsPathRooted(configuredRootPath)
                ? configuredRootPath
                : Path.Combine(contentRootPath, configuredRootPath));

        var webRootPath = Path.GetFullPath(
            string.IsNullOrWhiteSpace(environment.WebRootPath)
                ? Path.Combine(contentRootPath, "wwwroot")
                : environment.WebRootPath);
        if (IsSameOrChildPath(_uploadRootPath, webRootPath))
        {
            throw new InvalidOperationException(
                "Storage:SellerKyc:RootPath phải nằm ngoài webroot để giấy tờ KYC không bị phục vụ như file tĩnh.");
        }

        Directory.CreateDirectory(_uploadRootPath);
    }

    public (bool isValid, string errorMessage) Validate(IFormFile file, bool allowPdf = false)
    {
        if (file.Length == 0)
        {
            return (false, "File không hợp lệ.");
        }

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        var allowedExtensions = allowPdf ? AllowedDocumentExtensions : AllowedImageExtensions;
        if (!allowedExtensions.Contains(extension))
        {
            return (false, $"Chỉ chấp nhận file: {string.Join(", ", allowedExtensions)}");
        }

        if (file.Length > MaxFileSize)
        {
            return (false, $"Kích thước file không được vượt quá {MaxFileSize / (1024 * 1024)}MB.");
        }

        if (!HasExpectedFileSignature(file, extension))
        {
            return (false, "Nội dung file không khớp với định dạng đã chọn.");
        }

        return (true, string.Empty);
    }

    public string Save(IFormFile file, string prefix, bool allowPdf = false)
    {
        var validation = Validate(file, allowPdf);
        if (!validation.isValid)
        {
            throw new InvalidDataException(validation.errorMessage);
        }

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        var safePrefix = NormalizePrefix(prefix);
        var uniqueFileName = $"{safePrefix}-{Guid.NewGuid():N}{extension}";
        var filePath = ResolveFilePath(uniqueFileName);

        using var stream = new FileStream(filePath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        file.CopyTo(stream);

        return SellerKycPaths.BuildStoredReference(uniqueFileName);
    }

    public SellerKycStoredFile? OpenRead(string? storedReference)
    {
        var storedFileName = SellerKycPaths.NormalizeStoredFileName(storedReference);
        if (string.IsNullOrWhiteSpace(storedFileName))
        {
            return null;
        }

        var extension = Path.GetExtension(storedFileName);
        if (!ContentTypes.TryGetValue(extension, out var contentType))
        {
            return null;
        }

        try
        {
            var stream = new FileStream(
                ResolveFilePath(storedFileName),
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read);
            return new SellerKycStoredFile(stream, contentType);
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }
    }

    public void Delete(string? requestPath)
    {
        var storedFileName = SellerKycPaths.NormalizeStoredFileName(requestPath);
        if (string.IsNullOrWhiteSpace(storedFileName))
        {
            return;
        }

        var filePath = ResolveFilePath(storedFileName);
        if (!File.Exists(filePath))
        {
            return;
        }

        try
        {
            File.Delete(filePath);
        }
        catch
        {
            // Không chặn luồng nghiệp vụ chính nếu xóa file thất bại.
        }
    }

    private string ResolveFilePath(string storedFileName)
    {
        var filePath = Path.GetFullPath(Path.Combine(_uploadRootPath, storedFileName));
        if (!IsSameOrChildPath(filePath, _uploadRootPath) ||
            string.Equals(filePath, _uploadRootPath, PathComparison))
        {
            throw new InvalidOperationException("Đường dẫn file KYC không hợp lệ.");
        }

        return filePath;
    }

    private static string NormalizePrefix(string? prefix)
    {
        var normalized = new string((prefix ?? string.Empty)
            .Trim()
            .ToLowerInvariant()
            .Where(character => char.IsAsciiLetterOrDigit(character) || character == '-')
            .Take(40)
            .ToArray())
            .Trim('-');

        return string.IsNullOrWhiteSpace(normalized) ? "seller-kyc" : normalized;
    }

    private static bool HasExpectedFileSignature(IFormFile file, string extension)
    {
        Span<byte> header = stackalloc byte[12];
        using var stream = file.OpenReadStream();
        var bytesRead = stream.Read(header);

        return extension switch
        {
            ".jpg" or ".jpeg" =>
                bytesRead >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF,
            ".png" =>
                bytesRead >= 8 && header[..8].SequenceEqual(
                    new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }),
            ".webp" =>
                bytesRead >= 12 &&
                header[..4].SequenceEqual("RIFF"u8) &&
                header[8..12].SequenceEqual("WEBP"u8),
            ".pdf" =>
                bytesRead >= 5 && header[..5].SequenceEqual("%PDF-"u8),
            _ => false
        };
    }

    private static bool IsSameOrChildPath(string candidatePath, string rootPath)
    {
        var normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath));
        var normalizedCandidate = Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidatePath));
        if (string.Equals(normalizedCandidate, normalizedRoot, PathComparison))
        {
            return true;
        }

        return normalizedCandidate.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, PathComparison);
    }

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
}
