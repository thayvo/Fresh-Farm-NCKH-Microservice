using FreshFarm.Web.Bff.Areas.Seller.Infrastructure;
using FreshFarm.Web.Bff.Areas.Seller.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FreshFarm.Web.Bff.Areas.Seller.Controllers;

[Authorize(Roles = "Seller")]
[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
[Area("Seller")]
public class CategoryController : LegacySellerControllerBase
{
    private const string AccessTokenSessionKey = "ACCESS_TOKEN";
    private static readonly string[] AllowedExts = { ".jpg", ".jpeg", ".png", ".gif", ".webp" };
    private const int MaxFileSizeBytes = 2 * 1024 * 1024;
    private const string UploadFolderVPath = "~/Images/";
    private const string FallbackImg = "no-image.png";

    private readonly IHttpClientFactory _httpClientFactory;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public CategoryController(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public async Task<IActionResult> ManageCategories(int? page, string? search)
    {
        var pageSize = 10;
        var pageNumber = page.GetValueOrDefault(1);

        var categories = await GetCategoriesAsync();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var keyword = search.Trim().ToLower();
            categories = categories.Where(c =>
                c.CategoryName.ToLower().Contains(keyword) ||
                (c.Description ?? string.Empty).ToLower().Contains(keyword) ||
                (c.Slug ?? string.Empty).ToLower().Contains(keyword)).ToList();
            ViewBag.CurrentSearch = search.Trim();
        }

        var totalRecords = categories.Count;
        var totalPages = (int)Math.Ceiling(totalRecords / (double)pageSize);

        if (pageNumber < 1)
        {
            pageNumber = 1;
        }

        if (pageNumber > totalPages && totalPages > 0)
        {
            pageNumber = totalPages;
        }

        var result = categories
            .OrderByDescending(c => c.CreatedDate)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        ViewBag.CurrentPage = pageNumber;
        ViewBag.TotalPages = totalPages;
        ViewBag.TotalRecords = totalRecords;
        ViewBag.PageSize = pageSize;

        return View(result);
    }

    public IActionResult Create()
    {
        return View(new Category { IsActive = true });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(Category category, IFormFile? imageFile)
    {
        if (!ModelState.IsValid)
        {
            return View(category);
        }

        var categories = await GetCategoriesAsync();

        var categoryName = (category.CategoryName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(categoryName))
        {
            ModelState.AddModelError(nameof(category.CategoryName), "Tên danh mục không hợp lệ.");
            return View(category);
        }

        if (categories.Any(c => c.CategoryName.Equals(categoryName, StringComparison.OrdinalIgnoreCase)))
        {
            ModelState.AddModelError(nameof(category.CategoryName), "Tên danh mục đã tồn tại!");
            return View(category);
        }

        category.CategoryName = categoryName;
        category.Description = category.Description?.Trim();
        category.Slug = string.IsNullOrWhiteSpace(category.Slug)
            ? GenerateSlug(categoryName)
            : GenerateSlug(category.Slug);

        category.Slug = GenerateUniqueSlug(category.Slug, categories, null);

        if (imageFile is { Length: > 0 })
        {
            var savedName = SaveCategoryImage(imageFile, category.Slug);
            if (savedName is null)
            {
                return View(category);
            }

            category.ImageCategoriesName = savedName;
        }

        var client = CreateCatalogClient();
        var response = await client.PostAsJsonAsync("/api/categories", new
        {
            categoryName = category.CategoryName,
            description = category.Description,
            imageCategoriesName = category.ImageCategoriesName,
            isActive = category.IsActive,
            slug = category.Slug
        });

        if (!response.IsSuccessStatusCode)
        {
            if (!string.IsNullOrWhiteSpace(category.ImageCategoriesName))
            {
                TryDeleteCategoryImage(category.ImageCategoriesName);
            }

            TempData["ErrorMessage"] = $"❌ {await ReadApiErrorAsync(response, "Lỗi khi thêm danh mục")}";
            return View(category);
        }

        TempData["SuccessMessage"] = "✅ Thêm danh mục thành công!";
        return RedirectToAction(nameof(ManageCategories));
    }

    [Route("Seller/Category/Edit/{slug}")]
    public async Task<IActionResult> Edit(string slug)
    {
        if (string.IsNullOrWhiteSpace(slug))
        {
            TempData["ErrorMessage"] = "❌ Slug danh mục không hợp lệ!";
            return RedirectToAction(nameof(ManageCategories));
        }

        var category = await GetCategoryBySlugAsync(slug);
        if (category is null)
        {
            TempData["ErrorMessage"] = "❌ Không tìm thấy danh mục!";
            return RedirectToAction(nameof(ManageCategories));
        }

        return View(category);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Category category, IFormFile? imageFile)
    {
        if (!ModelState.IsValid)
        {
            return View(category);
        }

        var existing = await GetCategoryByIdAsync(category.CategoryID);
        if (existing is null)
        {
            TempData["ErrorMessage"] = "❌ Không tìm thấy danh mục!";
            return RedirectToAction(nameof(ManageCategories));
        }

        var allCategories = await GetCategoriesAsync();

        var newName = (category.CategoryName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(newName))
        {
            ModelState.AddModelError(nameof(category.CategoryName), "Tên danh mục không hợp lệ.");
            return View(category);
        }

        if (allCategories.Any(c => c.CategoryID != category.CategoryID && c.CategoryName.Equals(newName, StringComparison.OrdinalIgnoreCase)))
        {
            ModelState.AddModelError(nameof(category.CategoryName), "Tên danh mục đã tồn tại!");
            return View(category);
        }

        var oldImage = existing.ImageCategoriesName;

        category.CategoryName = newName;
        category.Description = category.Description?.Trim();

        var candidateSlug = GenerateSlug(newName);
        category.Slug = GenerateUniqueSlug(candidateSlug, allCategories, category.CategoryID);

        if (imageFile is { Length: > 0 })
        {
            var savedName = SaveCategoryImage(imageFile, category.Slug);
            if (savedName is null)
            {
                return View(category);
            }

            category.ImageCategoriesName = savedName;
        }
        else
        {
            category.ImageCategoriesName = existing.ImageCategoriesName;
        }

        var client = CreateCatalogClient();
        var response = await client.PutAsJsonAsync($"/api/categories/{category.CategoryID}", new
        {
            categoryName = category.CategoryName,
            description = category.Description,
            imageCategoriesName = category.ImageCategoriesName,
            isActive = category.IsActive,
            slug = category.Slug
        });

        if (!response.IsSuccessStatusCode)
        {
            if (!string.IsNullOrWhiteSpace(category.ImageCategoriesName) &&
                !string.Equals(category.ImageCategoriesName, oldImage, StringComparison.OrdinalIgnoreCase))
            {
                TryDeleteCategoryImage(category.ImageCategoriesName);
                category.ImageCategoriesName = oldImage;
            }

            TempData["ErrorMessage"] = $"❌ {await ReadApiErrorAsync(response, "Lỗi khi cập nhật danh mục")}";
            return View(category);
        }

        if (!string.IsNullOrWhiteSpace(oldImage) &&
            !string.Equals(oldImage, category.ImageCategoriesName, StringComparison.OrdinalIgnoreCase))
        {
            TryDeleteCategoryImage(oldImage);
        }

        TempData["SuccessMessage"] = "✅ Cập nhật danh mục thành công!";
        return RedirectToAction(nameof(ManageCategories));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var category = await GetCategoryByIdAsync(id);
        if (category is null)
        {
            TempData["ErrorMessage"] = "❌ Không tìm thấy danh mục!";
            return RedirectToAction(nameof(ManageCategories));
        }

        var client = CreateCatalogClient();
        var response = await client.DeleteAsync($"/api/categories/{id}");

        if (!response.IsSuccessStatusCode)
        {
            TempData["ErrorMessage"] = $"❌ {await ReadApiErrorAsync(response, "Không thể xóa danh mục")}";
            return RedirectToAction(nameof(ManageCategories));
        }

        if (!string.IsNullOrWhiteSpace(category.ImageCategoriesName))
        {
            TryDeleteCategoryImage(category.ImageCategoriesName);
        }

        TempData["SuccessMessage"] = $"✅ Đã xóa danh mục '{category.CategoryName}' thành công!";
        return RedirectToAction(nameof(ManageCategories));
    }

    private async Task<List<Category>> GetCategoriesAsync()
    {
        var client = CreateCatalogClient();
        var response = await client.GetAsync("/api/categories");

        if (!response.IsSuccessStatusCode)
        {
            return new List<Category>();
        }

        var items = await response.Content.ReadFromJsonAsync<List<ApiCategoryDto>>(JsonOptions) ?? new List<ApiCategoryDto>();

        return items.Select(MapCategory).ToList();
    }

    private async Task<Category?> GetCategoryByIdAsync(int id)
    {
        var client = CreateCatalogClient();
        var response = await client.GetAsync($"/api/categories/{id}");

        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var dto = await response.Content.ReadFromJsonAsync<ApiCategoryDto>(JsonOptions);
        return dto is null ? null : MapCategory(dto);
    }

    private async Task<Category?> GetCategoryBySlugAsync(string slug)
    {
        var client = CreateCatalogClient();
        var response = await client.GetAsync($"/api/categories/slug/{Uri.EscapeDataString(slug)}");

        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var dto = await response.Content.ReadFromJsonAsync<ApiCategoryDto>(JsonOptions);
        return dto is null ? null : MapCategory(dto);
    }

    private HttpClient CreateCatalogClient()
    {
        var client = _httpClientFactory.CreateClient("Catalog");
        var token = HttpContext.Session.GetString(AccessTokenSessionKey);

        if (!string.IsNullOrWhiteSpace(token))
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return client;
    }

    private Category MapCategory(ApiCategoryDto dto)
    {
        return new Category
        {
            CategoryID = dto.CategoryId,
            CategoryName = dto.CategoryName,
            Description = dto.Description,
            ImageCategoriesName = dto.ImageCategoriesName,
            IsActive = dto.IsActive,
            Slug = dto.Slug,
            CreatedDate = dto.CreatedDate,
            UpdatedDate = dto.UpdatedDate
        };
    }

    private string? SaveCategoryImage(IFormFile file, string? baseSlug)
    {
        if (file.Length == 0)
        {
            return null;
        }

        var ext = Path.GetExtension(file.FileName)?.ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(ext) || !AllowedExts.Contains(ext))
        {
            ModelState.AddModelError(string.Empty, "Định dạng ảnh không hợp lệ! Chỉ hỗ trợ: .jpg, .jpeg, .png, .gif, .webp");
            return null;
        }

        if (file.Length > MaxFileSizeBytes)
        {
            ModelState.AddModelError(string.Empty, "Ảnh vượt quá dung lượng 2MB!");
            return null;
        }

        var folderPath = Server.MapPath(UploadFolderVPath);
        if (!Directory.Exists(folderPath))
        {
            Directory.CreateDirectory(folderPath);
        }

        var safeSlug = string.IsNullOrWhiteSpace(baseSlug) ? "category" : baseSlug.Trim().ToLowerInvariant();
        var fileName = $"{safeSlug}-{DateTime.UtcNow.Ticks}{ext}";
        var savePath = Path.Combine(folderPath, fileName);

        using var stream = System.IO.File.Create(savePath);
        file.CopyTo(stream);

        return fileName;
    }

    private void TryDeleteCategoryImage(string? fileName)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return;
            }

            if (string.Equals(fileName, FallbackImg, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var folderPath = Server.MapPath(UploadFolderVPath);
            var fullPath = Path.Combine(folderPath, fileName);
            if (System.IO.File.Exists(fullPath))
            {
                System.IO.File.Delete(fullPath);
            }
        }
        catch
        {
            // ignore file cleanup exception
        }
    }

    private static string GenerateSlug(string categoryName)
    {
        var slug = RemoveVietnameseTone((categoryName ?? string.Empty).ToLowerInvariant().Trim());
        slug = Regex.Replace(slug, @"[^a-z0-9\s-]", string.Empty);
        slug = Regex.Replace(slug, @"\s+", "-");
        slug = Regex.Replace(slug, @"-+", "-");

        return slug.Trim('-');
    }

    private static string GenerateUniqueSlug(string baseSlug, IEnumerable<Category> categories, int? excludeCategoryId)
    {
        var slug = string.IsNullOrWhiteSpace(baseSlug) ? "category" : baseSlug;
        var current = slug;
        var idx = 1;

        while (categories.Any(c =>
                   string.Equals(c.Slug, current, StringComparison.OrdinalIgnoreCase) &&
                   (!excludeCategoryId.HasValue || c.CategoryID != excludeCategoryId.Value)))
        {
            current = $"{slug}-{idx}";
            idx++;
        }

        return current;
    }

    private static string RemoveVietnameseTone(string text)
    {
        string[] signs =
        {
            "aAeEoOuUiIdDyY",
            "áàạảãâấầậẩẫăắằặẳẵ",
            "ÁÀẠẢÃÂẤẦẬẨẪĂẮẰẶẲẴ",
            "éèẹẻẽêếềệểễ",
            "ÉÈẸẺẼÊẾỀỆỂỄ",
            "óòọỏõôốồộổỗơớờợởỡ",
            "ÓÒỌỎÕÔỐỒỘỔỖƠỚỜỢỞỠ",
            "úùụủũưứừựửữ",
            "ÚÙỤỦŨƯỨỪỰỬỮ",
            "íìịỉĩ",
            "ÍÌỊỈĨ",
            "đ",
            "Đ",
            "ýỳỵỷỹ",
            "ÝỲỴỶỸ"
        };

        for (var i = 1; i < signs.Length; i++)
        {
            for (var j = 0; j < signs[i].Length; j++)
            {
                text = text.Replace(signs[i][j], signs[0][i - 1]);
            }
        }

        return text;
    }

    private static async Task<string> ReadApiErrorAsync(HttpResponseMessage response, string fallback)
    {
        var body = await response.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(body))
        {
            return fallback;
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            if (root.TryGetProperty("message", out var messageElement) && messageElement.ValueKind == JsonValueKind.String)
            {
                return messageElement.GetString() ?? fallback;
            }

            if (root.TryGetProperty("detail", out var detailElement) && detailElement.ValueKind == JsonValueKind.String)
            {
                return detailElement.GetString() ?? fallback;
            }
        }
        catch
        {
            // ignore parse failure
        }

        return fallback;
    }

    private sealed class ApiCategoryDto
    {
        public int CategoryId { get; set; }

        public string CategoryName { get; set; } = string.Empty;

        public string? Description { get; set; }

        public string? ImageCategoriesName { get; set; }

        public bool IsActive { get; set; }

        public string? Slug { get; set; }

        public DateTime CreatedDate { get; set; }

        public DateTime? UpdatedDate { get; set; }
    }
}
