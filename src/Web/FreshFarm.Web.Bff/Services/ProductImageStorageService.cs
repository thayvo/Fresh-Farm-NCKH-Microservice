using Microsoft.AspNetCore.Http;

namespace FreshFarm.Web.Bff.Services;

public sealed class ProductImageStorageService : IProductImageStorageService
{
    private const int MaxFileSize = 5 * 1024 * 1024;

    private static readonly string[] AllowedImageExtensions = [".jpg", ".jpeg", ".png", ".gif", ".webp"];

    private readonly string _uploadRootPath;

    public ProductImageStorageService(IWebHostEnvironment environment)
    {
        var webRootPath = environment.WebRootPath;
        if (string.IsNullOrWhiteSpace(webRootPath))
        {
            webRootPath = Path.Combine(environment.ContentRootPath, "wwwroot");
        }

        _uploadRootPath = Path.Combine(webRootPath, ProductImagePaths.UploadFolderRelativePath);
        Directory.CreateDirectory(_uploadRootPath);
    }

    public (bool isValid, string errorMessage) Validate(IFormFile file)
    {
        if (file.Length == 0)
        {
            return (false, "File không hợp lệ");
        }

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!AllowedImageExtensions.Contains(extension))
        {
            return (false, $"Chỉ chấp nhận file ảnh: {string.Join(", ", AllowedImageExtensions)}");
        }

        if (file.Length > MaxFileSize)
        {
            return (false, $"Kích thước file không được vượt quá {MaxFileSize / (1024 * 1024)}MB");
        }

        return (true, string.Empty);
    }

    public string Save(IFormFile file)
    {
        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        var uniqueFileName = $"{Guid.NewGuid()}{extension}";
        var filePath = Path.Combine(_uploadRootPath, uniqueFileName);

        using var stream = File.Create(filePath);
        file.CopyTo(stream);

        return uniqueFileName;
    }

    public void Delete(string? imageFileName)
    {
        var normalized = ProductImagePaths.NormalizeStoredFileName(imageFileName);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return;
        }

        var filePath = Path.Combine(_uploadRootPath, normalized);
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
