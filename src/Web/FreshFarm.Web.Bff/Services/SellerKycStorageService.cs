using Microsoft.AspNetCore.Http;

namespace FreshFarm.Web.Bff.Services;

public sealed class SellerKycStorageService : ISellerKycStorageService
{
    private const int MaxFileSize = 10 * 1024 * 1024;

    private static readonly string[] AllowedImageExtensions = [".jpg", ".jpeg", ".png", ".webp"];
    private static readonly string[] AllowedDocumentExtensions = [".jpg", ".jpeg", ".png", ".webp", ".pdf"];

    private readonly string _uploadRootPath;

    public SellerKycStorageService(IWebHostEnvironment environment)
    {
        var webRootPath = environment.WebRootPath;
        if (string.IsNullOrWhiteSpace(webRootPath))
        {
            webRootPath = Path.Combine(environment.ContentRootPath, "wwwroot");
        }

        _uploadRootPath = Path.Combine(webRootPath, SellerKycPaths.UploadFolderRelativePath);
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

        return (true, string.Empty);
    }

    public string Save(IFormFile file, string prefix, bool allowPdf = false)
    {
        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        var safePrefix = string.IsNullOrWhiteSpace(prefix) ? "seller-kyc" : prefix.Trim().ToLowerInvariant();
        var uniqueFileName = $"{safePrefix}-{Guid.NewGuid():N}{extension}";
        var filePath = Path.Combine(_uploadRootPath, uniqueFileName);

        using var stream = File.Create(filePath);
        file.CopyTo(stream);

        return SellerKycPaths.BuildRequestPath(uniqueFileName);
    }

    public void Delete(string? requestPath)
    {
        var storedFileName = SellerKycPaths.NormalizeStoredFileName(requestPath);
        if (string.IsNullOrWhiteSpace(storedFileName))
        {
            return;
        }

        var filePath = Path.Combine(_uploadRootPath, storedFileName);
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
}
