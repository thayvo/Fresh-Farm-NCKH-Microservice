using FreshFarm.Web.Bff.Areas.Seller.Infrastructure;
using FreshFarm.Web.Bff.Areas.Seller.Models;
using FreshFarm.Web.Bff.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace FreshFarm.Web.Bff.Areas.Admin.Controllers;

[Authorize(Policy = "AdminOnly")]
[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
[Area("Admin")]
public sealed class ProductController : LegacySellerControllerBase
{
    private const string AccessTokenSessionKey = "ACCESS_TOKEN";
    private const int PageSize = 6;

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IProductImageStorageService _productImageStorageService;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public ProductController(
        IHttpClientFactory httpClientFactory,
        IProductImageStorageService productImageStorageService)
    {
        _httpClientFactory = httpClientFactory;
        _productImageStorageService = productImageStorageService;
    }

    public async Task<IActionResult> ManageProducts(
        string? searchTerm,
        int? categoryId,
        int? unitId,
        bool? status,
        string? sortBy,
        int page = 1,
        bool? expired = null,
        bool? expiringSoon = null)
    {
        var products = await GetProductsAsync();
        var categories = await GetCategoriesAsync();
        var units = await GetUnitsAsync();

        var totalProductsInDb = products.Count;
        var activeProductsInDb = products.Count(p => p.Status);
        var inactiveProductsInDb = products.Count(p => !p.Status);
        var lowStockProductsInDb = products.Count(p => p.StockQuantity < 10);

        var query = products.AsQueryable();

        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            var keyword = searchTerm.Trim().ToLowerInvariant();
            query = query.Where(p =>
                p.ProductName.ToLower().Contains(keyword) ||
                (p.Sku ?? string.Empty).ToLower().Contains(keyword));
        }

        if (categoryId.HasValue && categoryId.Value > 0)
        {
            query = query.Where(p => p.CategoryId == categoryId.Value);
        }

        if (unitId.HasValue && unitId.Value > 0)
        {
            query = query.Where(p => p.UnitID == unitId.Value);
        }

        if (status.HasValue)
        {
            query = query.Where(p => p.Status == status.Value);
        }

        query = (sortBy ?? string.Empty).ToLowerInvariant() switch
        {
            "oldest" => query.OrderBy(p => p.CreatedDate),
            "name_asc" => query.OrderBy(p => p.ProductName),
            "name_desc" => query.OrderByDescending(p => p.ProductName),
            "price_asc" => query.OrderBy(p => p.Price),
            "price_desc" => query.OrderByDescending(p => p.Price),
            "stock_asc" => query.OrderBy(p => p.StockQuantity),
            "stock_desc" => query.OrderByDescending(p => p.StockQuantity),
            _ => query.OrderByDescending(p => p.CreatedDate)
        };

        var filtered = query.ToList();
        var totalCount = filtered.Count;
        var totalPages = (int)Math.Ceiling(totalCount / (double)PageSize);

        if (page < 1)
        {
            page = 1;
        }

        if (page > totalPages && totalPages > 0)
        {
            page = totalPages;
        }

        var pageItems = filtered
            .Skip((page - 1) * PageSize)
            .Take(PageSize)
            .ToList();

        ViewBag.Categories = categories
            .Where(c => c.IsActive)
            .OrderBy(c => c.CategoryName)
            .ToList();
        ViewBag.Units = units
            .OrderBy(u => u.UnitName)
            .ToList();
        ViewBag.TotalProductsInDB = totalProductsInDb;
        ViewBag.ActiveProductsInDB = activeProductsInDb;
        ViewBag.InactiveProductsInDB = inactiveProductsInDb;
        ViewBag.LowStockProductsInDB = lowStockProductsInDb;
        ViewBag.ExpiredProductsInDB = 0;
        ViewBag.ExpiringSoonProductsInDB = 0;
        ViewBag.ProductExpiryMap = new Dictionary<int, DateTime?>();
        ViewBag.TotalCount = totalCount;
        ViewBag.CurrentPage = page;
        ViewBag.PageSize = PageSize;
        ViewBag.TotalPages = totalPages;
        ViewBag.SearchTerm = searchTerm;
        ViewBag.SelectedCategory = categoryId;
        ViewBag.SelectedUnit = unitId;
        ViewBag.SelectedStatus = status;
        ViewBag.CurrentSort = sortBy;

        return RenderProductView("ManageProducts", pageItems);
    }

    public async Task<IActionResult> Create()
    {
        var categories = await GetCategoriesAsync();
        var units = await GetUnitsAsync();

        LoadDropdownData(categories, units, null, null);

        var model = new Product
        {
            Status = true,
            UnitID = units.FirstOrDefault(u => u.IsActive)?.UnitID ?? 0
        };

        return RenderProductView("Create", model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(Product product, IFormFile? imageFile)
    {
        var categories = await GetCategoriesAsync();
        var units = await GetUnitsAsync();

        if (!ModelState.IsValid)
        {
            LoadDropdownData(categories, units, product.CategoryId, product.UnitID);
            return RenderProductView("Create", product);
        }

        var products = await GetProductsAsync();

        if (products.Any(p => p.ProductName.Equals(product.ProductName.Trim(), StringComparison.OrdinalIgnoreCase)))
        {
            ModelState.AddModelError(nameof(product.ProductName), "Tên sản phẩm đã tồn tại");
            TempData["ErrorMessage"] = "❌ Tên sản phẩm đã tồn tại.";
            LoadDropdownData(categories, units, product.CategoryId, product.UnitID);
            return RenderProductView("Create", product);
        }

        if (!string.IsNullOrWhiteSpace(product.Sku) &&
            products.Any(p => string.Equals(p.Sku, product.Sku.Trim(), StringComparison.OrdinalIgnoreCase)))
        {
            ModelState.AddModelError(nameof(product.Sku), "Mã SKU đã tồn tại");
            TempData["ErrorMessage"] = "❌ Mã SKU đã tồn tại.";
            LoadDropdownData(categories, units, product.CategoryId, product.UnitID);
            return RenderProductView("Create", product);
        }

        if (!categories.Any(c => c.CategoryID == product.CategoryId && c.IsActive))
        {
            ModelState.AddModelError(nameof(product.CategoryId), "Danh mục không hợp lệ");
            TempData["ErrorMessage"] = "❌ Danh mục không hợp lệ hoặc đã bị ẩn.";
            LoadDropdownData(categories, units, product.CategoryId, product.UnitID);
            return RenderProductView("Create", product);
        }

        if (!units.Any(u => u.UnitID == product.UnitID && u.IsActive))
        {
            ModelState.AddModelError(nameof(product.UnitID), "Đơn vị tính không hợp lệ");
            TempData["ErrorMessage"] = "❌ Đơn vị tính không hợp lệ hoặc đã bị ẩn.";
            LoadDropdownData(categories, units, product.CategoryId, product.UnitID);
            return RenderProductView("Create", product);
        }

        product.ProductName = product.ProductName.Trim();
        product.Sku = string.IsNullOrWhiteSpace(product.Sku)
            ? GenerateSku(product.CategoryId, products)
            : product.Sku.Trim();
        product.ShortDescription = product.ShortDescription?.Trim();
        product.LongDescription = product.LongDescription?.Trim();
        product.StockQuantity = 0;
        product.CreatedDate = DateTime.Now;

        if (imageFile is { Length: > 0 })
        {
            var validation = _productImageStorageService.Validate(imageFile);
            if (!validation.isValid)
            {
                ModelState.AddModelError("ImageFile", validation.errorMessage);
                TempData["ErrorMessage"] = $"❌ {validation.errorMessage}";
                LoadDropdownData(categories, units, product.CategoryId, product.UnitID);
                return RenderProductView("Create", product);
            }

            product.ImageFileName = _productImageStorageService.Save(imageFile);
        }

        var client = CreateCatalogClient();
        var response = await client.PostAsJsonAsync("/api/products", new
        {
            categoryId = product.CategoryId,
            unitId = product.UnitID,
            productName = product.ProductName,
            price = product.Price,
            sku = product.Sku,
            shortDescription = product.ShortDescription,
            longDescription = product.LongDescription,
            imageFileName = product.ImageFileName,
            status = product.Status
        });

        if (!response.IsSuccessStatusCode)
        {
            if (!string.IsNullOrWhiteSpace(product.ImageFileName))
            {
                _productImageStorageService.Delete(product.ImageFileName);
            }

            TempData["ErrorMessage"] = $"❌ {await ReadApiErrorAsync(response, "Không thể thêm sản phẩm")}";
            LoadDropdownData(categories, units, product.CategoryId, product.UnitID);
            return RenderProductView("Create", product);
        }

        TempData["SuccessMessage"] = "✅ Thêm sản phẩm thành công!";
        return RedirectToAction(nameof(ManageProducts));
    }

    public async Task<IActionResult> Edit(int? id)
    {
        if (!id.HasValue)
        {
            TempData["ErrorMessage"] = "❌ ID sản phẩm không hợp lệ.";
            return RedirectToAction(nameof(ManageProducts));
        }

        var product = await GetProductByIdAsync(id.Value);
        if (product is null)
        {
            TempData["ErrorMessage"] = "❌ Không tìm thấy sản phẩm.";
            return RedirectToAction(nameof(ManageProducts));
        }

        var categories = await GetCategoriesAsync();
        var units = await GetUnitsAsync();
        LoadDropdownData(categories, units, product.CategoryId, product.UnitID);

        return RenderProductView("Edit", product);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Product product, IFormFile? imageFile)
    {
        var categories = await GetCategoriesAsync();
        var units = await GetUnitsAsync();

        if (!ModelState.IsValid)
        {
            LoadDropdownData(categories, units, product.CategoryId, product.UnitID);
            return RenderProductView("Edit", product);
        }

        var existingProduct = await GetProductByIdAsync(product.ProductID);
        if (existingProduct is null)
        {
            TempData["ErrorMessage"] = "❌ Không tìm thấy sản phẩm.";
            return RedirectToAction(nameof(ManageProducts));
        }

        var products = await GetProductsAsync();

        if (products.Any(p => p.ProductID != product.ProductID &&
                              p.ProductName.Equals(product.ProductName.Trim(), StringComparison.OrdinalIgnoreCase)))
        {
            ModelState.AddModelError(nameof(product.ProductName), "Tên sản phẩm đã tồn tại");
            TempData["ErrorMessage"] = "❌ Tên sản phẩm đã tồn tại.";
            LoadDropdownData(categories, units, product.CategoryId, product.UnitID);
            return RenderProductView("Edit", product);
        }

        if (!string.IsNullOrWhiteSpace(product.Sku) &&
            products.Any(p => p.ProductID != product.ProductID &&
                              string.Equals(p.Sku, product.Sku.Trim(), StringComparison.OrdinalIgnoreCase)))
        {
            ModelState.AddModelError(nameof(product.Sku), "Mã SKU đã tồn tại");
            TempData["ErrorMessage"] = "❌ Mã SKU đã tồn tại.";
            LoadDropdownData(categories, units, product.CategoryId, product.UnitID);
            return RenderProductView("Edit", product);
        }

        if (!categories.Any(c => c.CategoryID == product.CategoryId && c.IsActive))
        {
            ModelState.AddModelError(nameof(product.CategoryId), "Danh mục không hợp lệ");
            TempData["ErrorMessage"] = "❌ Danh mục không hợp lệ hoặc đã bị ẩn.";
            LoadDropdownData(categories, units, product.CategoryId, product.UnitID);
            return RenderProductView("Edit", product);
        }

        if (!units.Any(u => u.UnitID == product.UnitID && u.IsActive))
        {
            ModelState.AddModelError(nameof(product.UnitID), "Đơn vị tính không hợp lệ");
            TempData["ErrorMessage"] = "❌ Đơn vị tính không hợp lệ hoặc đã bị ẩn.";
            LoadDropdownData(categories, units, product.CategoryId, product.UnitID);
            return RenderProductView("Edit", product);
        }

        var oldImageFileName = existingProduct.ImageFileName;

        if (imageFile is { Length: > 0 })
        {
            var validation = _productImageStorageService.Validate(imageFile);
            if (!validation.isValid)
            {
                ModelState.AddModelError("ImageFile", validation.errorMessage);
                TempData["ErrorMessage"] = $"❌ {validation.errorMessage}";
                LoadDropdownData(categories, units, product.CategoryId, product.UnitID);
                return RenderProductView("Edit", product);
            }

            product.ImageFileName = _productImageStorageService.Save(imageFile);
        }
        else
        {
            product.ImageFileName = existingProduct.ImageFileName;
        }

        product.ProductName = product.ProductName.Trim();
        product.Sku = string.IsNullOrWhiteSpace(product.Sku) ? existingProduct.Sku : product.Sku.Trim();
        product.ShortDescription = product.ShortDescription?.Trim();
        product.LongDescription = product.LongDescription?.Trim();
        product.StockQuantity = existingProduct.StockQuantity;
        product.CreatedDate = existingProduct.CreatedDate;

        var client = CreateCatalogClient();
        var response = await client.PutAsJsonAsync($"/api/products/{product.ProductID}", new
        {
            categoryId = product.CategoryId,
            unitId = product.UnitID,
            productName = product.ProductName,
            price = product.Price,
            sku = product.Sku,
            shortDescription = product.ShortDescription,
            longDescription = product.LongDescription,
            imageFileName = product.ImageFileName,
            status = product.Status
        });

        if (!response.IsSuccessStatusCode)
        {
            if (!string.IsNullOrWhiteSpace(product.ImageFileName) &&
                !string.Equals(product.ImageFileName, oldImageFileName, StringComparison.OrdinalIgnoreCase))
            {
                _productImageStorageService.Delete(product.ImageFileName);
                product.ImageFileName = oldImageFileName;
            }

            TempData["ErrorMessage"] = $"❌ {await ReadApiErrorAsync(response, "Không thể cập nhật sản phẩm")}";
            LoadDropdownData(categories, units, product.CategoryId, product.UnitID);
            return RenderProductView("Edit", product);
        }

        if (!string.IsNullOrWhiteSpace(oldImageFileName) &&
            !string.Equals(oldImageFileName, product.ImageFileName, StringComparison.OrdinalIgnoreCase))
        {
            _productImageStorageService.Delete(oldImageFileName);
        }

        TempData["SuccessMessage"] = $"✅ Cập nhật sản phẩm <strong>{product.ProductName}</strong> thành công!";
        return RedirectToAction(nameof(ManageProducts));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var existing = await GetProductByIdAsync(id);
        if (existing is null)
        {
            TempData["ErrorMessage"] = "❌ Không tìm thấy sản phẩm!";
            return RedirectToAction(nameof(ManageProducts));
        }

        var client = CreateCatalogClient();
        var response = await client.DeleteAsync($"/api/products/{id}");

        if (!response.IsSuccessStatusCode)
        {
            TempData["ErrorMessage"] = $"❌ {await ReadApiErrorAsync(response, "Không thể xóa sản phẩm")}";
            return RedirectToAction(nameof(ManageProducts));
        }

        if (!string.IsNullOrWhiteSpace(existing.ImageFileName))
        {
            _productImageStorageService.Delete(existing.ImageFileName);
        }

        TempData["SuccessMessage"] = "✅ Xóa sản phẩm thành công!";
        return RedirectToAction(nameof(ManageProducts));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<JsonResult> ToggleStatus(int id)
    {
        var client = CreateCatalogClient();
        var response = await client.PostAsync($"/api/products/{id}/toggle-status", content: null);

        if (!response.IsSuccessStatusCode)
        {
            return Json(new
            {
                success = false,
                message = await ReadApiErrorAsync(response, "Không thể cập nhật trạng thái sản phẩm")
            });
        }

        var payload = await response.Content.ReadFromJsonAsync<ApiToggleResponse>(JsonOptions);
        return Json(new
        {
            success = true,
            message = payload?.Message ?? "Cập nhật trạng thái thành công",
            newStatus = payload?.NewStatus ?? false
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<JsonResult> GetProductDetails(int id)
    {
        var product = await GetProductByIdAsync(id);
        if (product is null)
        {
            return Json(new { success = false, message = "Không tìm thấy sản phẩm" });
        }

        var productInfo = product.ProductInfoes.FirstOrDefault();

        return Json(new
        {
            success = true,
            product = new
            {
                product.ProductID,
                product.ProductName,
                product.Sku,
                CategoryName = product.Category.CategoryName,
                UnitName = product.Unit.UnitName,
                product.Price,
                product.StockQuantity,
                product.Status,
                product.ShortDescription,
                product.LongDescription,
                product.ImageFileName,
                CreatedDate = product.CreatedDate.ToString("dd/MM/yyyy HH:mm"),
                Weight = productInfo?.Weight,
                Origin = productInfo?.Origin,
                Standard = productInfo?.Standard,
                Preservation = productInfo?.Preservation
            }
        });
    }

    [HttpGet]
    public IActionResult CheckSession()
    {
        return User?.Identity?.IsAuthenticated == true ? StatusCode(200) : StatusCode(401);
    }

    private IActionResult RenderProductView(string viewName, object model)
    {
        ViewData["AreaName"] = "Admin";
        ViewData["LayoutPath"] = "~/Areas/Admin/Views/Shared/_LayoutAdmin.cshtml";
        ViewData["ProductScopeLabel"] = "Toàn sàn";
        return View($"~/Areas/Seller/Views/Product/{viewName}.cshtml", model);
    }

    private async Task<List<Product>> GetProductsAsync()
    {
        var client = CreateCatalogClient();
        var response = await client.GetAsync("/api/products");

        if (!response.IsSuccessStatusCode)
        {
            return new List<Product>();
        }

        var apiProducts = DeduplicateProducts(
            await response.Content.ReadFromJsonAsync<List<ApiProductDto>>(JsonOptions) ?? new List<ApiProductDto>());
        return apiProducts.Select(MapProduct).ToList();
    }

    private async Task<Product?> GetProductByIdAsync(int id)
    {
        var client = CreateCatalogClient();
        var response = await client.GetAsync($"/api/products/{id}");

        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var dto = await response.Content.ReadFromJsonAsync<ApiProductDto>(JsonOptions);
        return dto is null ? null : MapProduct(dto);
    }

    private async Task<List<Category>> GetCategoriesAsync()
    {
        var client = CreateCatalogClient();
        var response = await client.GetAsync("/api/categories");

        if (!response.IsSuccessStatusCode)
        {
            return new List<Category>();
        }

        var apiCategories = DeduplicateCategories(
            await response.Content.ReadFromJsonAsync<List<ApiCategoryDto>>(JsonOptions) ?? new List<ApiCategoryDto>());
        return apiCategories.Select(c => new Category
        {
            CategoryID = c.CategoryId,
            CategoryName = c.CategoryName,
            Description = c.Description,
            ImageCategoriesName = c.ImageCategoriesName,
            IsActive = c.IsActive,
            Slug = c.Slug,
            CreatedDate = c.CreatedDate,
            UpdatedDate = c.UpdatedDate
        }).ToList();
    }

    private async Task<List<Unit>> GetUnitsAsync()
    {
        var client = CreateCatalogClient();
        var response = await client.GetAsync("/api/units");

        if (!response.IsSuccessStatusCode)
        {
            return new List<Unit>();
        }

        var apiUnits = DeduplicateUnits(
            await response.Content.ReadFromJsonAsync<List<ApiUnitDto>>(JsonOptions) ?? new List<ApiUnitDto>());
        return apiUnits.Select(u => new Unit
        {
            UnitID = u.UnitId,
            UnitName = u.UnitName,
            Symbol = u.Symbol,
            Description = u.Description,
            IsActive = u.IsActive,
            CreatedDate = u.CreatedDate
        }).ToList();
    }

    private Product MapProduct(ApiProductDto dto)
    {
        return new Product
        {
            ProductID = dto.ProductId,
            ProductName = dto.ProductName,
            Sku = dto.Sku,
            Price = dto.Price,
            Status = dto.Status,
            StockQuantity = dto.StockQuantity,
            ImageFileName = dto.ImageFileName,
            CreatedDate = dto.CreatedDate,
            ShortDescription = dto.ShortDescription,
            LongDescription = dto.LongDescription,
            IsManuallyDisabled = dto.IsManuallyDisabled,
            CategoryId = dto.CategoryId,
            UnitID = dto.UnitId,
            Category = new Category
            {
                CategoryID = dto.CategoryId,
                CategoryName = dto.CategoryName ?? string.Empty
            },
            Unit = new Unit
            {
                UnitID = dto.UnitId,
                UnitName = dto.UnitName ?? string.Empty,
                Symbol = dto.UnitSymbol ?? string.Empty
            },
            ProductInfoes = dto.ProductInfos?.Select(info => new ProductInfo
            {
                Weight = info.Weight,
                Origin = info.Origin,
                Standard = info.Standard,
                Preservation = info.Preservation
            }).ToList() ?? new List<ProductInfo>()
        };
    }

    private static List<ApiProductDto> DeduplicateProducts(IEnumerable<ApiProductDto> products)
    {
        return products
            .Where(product => product.ProductId > 0)
            .GroupBy(product => product.ProductId)
            .Select(group => SelectPreferredProduct(group))
            .ToList();
    }

    private static ApiProductDto SelectPreferredProduct(IEnumerable<ApiProductDto> products)
    {
        return products
            .OrderByDescending(CalculateProductScore)
            .ThenByDescending(CalculateProductSignalLength)
            .ThenByDescending(product => product.UpdatedDate ?? product.CreatedDate)
            .First();
    }

    private static int CalculateProductScore(ApiProductDto product)
    {
        var score = 0;

        score += HasMeaningfulValue(product.ProductName) ? 3 : 0;
        score += HasMeaningfulValue(product.Sku) ? 2 : 0;
        score += HasMeaningfulValue(product.CategoryName) ? 2 : 0;
        score += HasMeaningfulValue(product.UnitName) ? 1 : 0;
        score += HasMeaningfulValue(product.ShortDescription) ? 1 : 0;
        score += HasMeaningfulValue(product.LongDescription) ? 1 : 0;
        score += HasMeaningfulValue(product.ImageFileName) ? 1 : 0;
        score += product.Price > 0 ? 1 : 0;
        score += product.StockQuantity > 0 ? 1 : 0;
        score += product.ProductInfos?.Count > 0 ? 1 : 0;

        return score;
    }

    private static int CalculateProductSignalLength(ApiProductDto product)
    {
        var values = new[]
        {
            product.ProductName,
            product.Sku,
            product.CategoryName,
            product.UnitName,
            product.ShortDescription,
            product.LongDescription,
            product.ImageFileName
        };

        return values.Sum(value => value?.Length ?? 0);
    }

    private static List<ApiCategoryDto> DeduplicateCategories(IEnumerable<ApiCategoryDto> categories)
    {
        return categories
            .Where(category => category.CategoryId > 0)
            .GroupBy(category => category.CategoryId)
            .Select(group => group
                .OrderByDescending(category => HasMeaningfulValue(category.CategoryName))
                .ThenByDescending(category => HasMeaningfulValue(category.Description))
                .ThenByDescending(category => category.IsActive)
                .ThenByDescending(category => category.UpdatedDate ?? category.CreatedDate)
                .First())
            .ToList();
    }

    private static List<ApiUnitDto> DeduplicateUnits(IEnumerable<ApiUnitDto> units)
    {
        return units
            .Where(unit => unit.UnitId > 0)
            .GroupBy(unit => unit.UnitId)
            .Select(group => group
                .OrderByDescending(unit => HasMeaningfulValue(unit.UnitName))
                .ThenByDescending(unit => HasMeaningfulValue(unit.Symbol))
                .ThenByDescending(unit => HasMeaningfulValue(unit.Description))
                .ThenByDescending(unit => unit.IsActive)
                .ThenByDescending(unit => unit.CreatedDate)
                .First())
            .ToList();
    }

    private static bool HasMeaningfulValue(string? value) => !string.IsNullOrWhiteSpace(value);

    private void LoadDropdownData(IEnumerable<Category> categories, IEnumerable<Unit> units, int? selectedCategory, int? selectedUnit)
    {
        ViewBag.Categories = new SelectList(
            categories.Where(c => c.IsActive).OrderBy(c => c.CategoryName),
            nameof(Category.CategoryID),
            nameof(Category.CategoryName),
            selectedCategory);

        ViewBag.Units = new SelectList(
            units.Where(u => u.IsActive).OrderBy(u => u.UnitName),
            nameof(Unit.UnitID),
            nameof(Unit.UnitName),
            selectedUnit);
    }

    private HttpClient CreateCatalogClient()
    {
        var client = _httpClientFactory.CreateClient("Catalog");
        var token = GetAccessToken(AccessTokenSessionKey);

        if (!string.IsNullOrWhiteSpace(token))
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return client;
    }

    private static string GenerateSku(int categoryId, IEnumerable<Product> products)
    {
        var count = products.Count(p => p.CategoryId == categoryId) + 1;
        return $"PRD-{categoryId:D3}-{count:D4}";
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

            if (root.TryGetProperty("title", out var titleElement) && titleElement.ValueKind == JsonValueKind.String)
            {
                return titleElement.GetString() ?? fallback;
            }
        }
        catch
        {
        }

        return fallback;
    }

    private sealed class ApiToggleResponse
    {
        public string? Message { get; set; }
        public bool NewStatus { get; set; }
    }

    private sealed class ApiProductDto
    {
        public int ProductId { get; set; }
        public string ProductName { get; set; } = string.Empty;
        public string? Sku { get; set; }
        public decimal Price { get; set; }
        public bool Status { get; set; }
        public int StockQuantity { get; set; }
        public string? ImageFileName { get; set; }
        public DateTime CreatedDate { get; set; }
        public DateTime? UpdatedDate { get; set; }
        public string? ShortDescription { get; set; }
        public string? LongDescription { get; set; }
        public bool IsManuallyDisabled { get; set; }
        public int CategoryId { get; set; }
        public string? CategoryName { get; set; }
        public int UnitId { get; set; }
        public string? UnitName { get; set; }
        public string? UnitSymbol { get; set; }
        public List<ApiProductInfoDto>? ProductInfos { get; set; }
    }

    private sealed class ApiProductInfoDto
    {
        public string? Weight { get; set; }
        public string? Origin { get; set; }
        public string? Standard { get; set; }
        public string? Preservation { get; set; }
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

    private sealed class ApiUnitDto
    {
        public int UnitId { get; set; }
        public string UnitName { get; set; } = string.Empty;
        public string Symbol { get; set; } = string.Empty;
        public string? Description { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedDate { get; set; }
    }
}
