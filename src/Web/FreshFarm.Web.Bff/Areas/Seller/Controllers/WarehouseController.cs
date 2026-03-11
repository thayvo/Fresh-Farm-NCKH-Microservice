using FreshFarm.Web.Bff.Areas.Seller.Infrastructure;
using FreshFarm.Web.Bff.Areas.Seller.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace FreshFarm.Web.Bff.Areas.Seller.Controllers;

[Authorize(Roles = "Seller")]
[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
[Area("Seller")]
public class WarehouseController : LegacySellerControllerBase
{
    private const string AccessTokenSessionKey = "ACCESS_TOKEN";

    private readonly IHttpClientFactory _httpClientFactory;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public WarehouseController(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public async Task<IActionResult> GetProductInfo(int id)
    {
        var client = CreateAuthorizedClient("Catalog");
        var response = await client.GetAsync($"/api/admin/warehouse/product-info/{id}");
        if (!response.IsSuccessStatusCode)
        {
            return Json(new { success = false, message = await ReadApiErrorAsync(response, "Khong the tai thong tin san pham") });
        }

        var payload = await response.Content.ReadFromJsonAsync<ProductInfoApiResponse>(JsonOptions);
        return Json(new
        {
            success = payload?.success == true,
            nearExpiryDays = payload?.nearExpiryDays ?? 7
        });
    }

    public async Task<IActionResult> Index(string searchTerm = "", string categoryFilter = "", string stockStatusFilter = "", int page = 1)
    {
        var client = CreateAuthorizedClient("Catalog");
        var query = $"searchTerm={Uri.EscapeDataString(searchTerm ?? string.Empty)}&categoryFilter={Uri.EscapeDataString(categoryFilter ?? string.Empty)}&stockStatusFilter={Uri.EscapeDataString(stockStatusFilter ?? string.Empty)}&page={page}&pageSize=10";

        var response = await client.GetAsync($"/api/admin/warehouse/products?{query}");
        if (!response.IsSuccessStatusCode)
        {
            TempData["ErrorMessage"] = await ReadApiErrorAsync(response, "Khong the tai danh sach kho");
            return View(new WarehouseStatsViewModel());
        }

        var payload = await response.Content.ReadFromJsonAsync<WarehouseProductsApiResponse>(JsonOptions);
        var pageData = payload?.data;

        var products = pageData?.products?.Select(MapWarehouseProduct).ToList() ?? new List<WarehouseViewModel>();

        var model = new WarehouseStatsViewModel
        {
            TotalProducts = pageData?.summary?.totalProducts ?? products.Count,
            LowStockCount = pageData?.summary?.lowStockCount ?? products.Count(p => p.StockQuantity > 0 && p.StockQuantity <= 10),
            OutOfStockCount = pageData?.summary?.outOfStockCount ?? products.Count(p => p.StockQuantity == 0),
            TotalWarehouseValue = pageData?.summary?.totalWarehouseValue ?? products.Sum(p => p.StockQuantity * p.SellPrice),
            Products = products
        };

        ViewBag.SearchTerm = searchTerm;
        ViewBag.CategoryFilter = categoryFilter;
        ViewBag.StockStatusFilter = stockStatusFilter;
        ViewBag.CurrentPage = pageData?.currentPage ?? page;
        ViewBag.TotalPages = pageData?.totalPages ?? 1;
        ViewBag.PageSize = pageData?.pageSize ?? 10;
        ViewBag.TotalItems = pageData?.totalItems ?? products.Count;

        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> ImportCart()
    {
        var vm = new WarehouseImportViewModel
        {
            ImportDate = DateTime.Now.ToString("yyyy-MM-dd")
        };

        await PopulateImportProducts(vm);
        return View(vm);
    }

    [HttpGet]
    public async Task<IActionResult> ExportCart()
    {
        var client = CreateAuthorizedClient("Catalog");
        var response = await client.GetAsync("/api/admin/warehouse/products?page=1&pageSize=5000");

        if (!response.IsSuccessStatusCode)
        {
            TempData["ErrorMessage"] = await ReadApiErrorAsync(response, "Khong the tai danh sach san pham xuat kho");
            return RedirectToAction(nameof(Index));
        }

        var payload = await response.Content.ReadFromJsonAsync<WarehouseProductsApiResponse>(JsonOptions);
        var products = payload?.data?.products?.Select(MapWarehouseProduct)
            .Where(p => p.StockQuantity > 0)
            .OrderBy(p => p.ProductName)
            .ToList() ?? new List<WarehouseViewModel>();

        return View(new WarehouseStatsViewModel
        {
            Products = products
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ImportStock(
        int productId,
        int quantity,
        decimal importPrice,
        decimal? sellPrice,
        string supplierName,
        string importDate,
        string expiryDate,
        string notes,
        int? nearExpiryDaysOverride,
        bool keepDisabled = false)
    {
        var request = new List<ImportCartItem>
        {
            new()
            {
                ProductId = productId,
                Quantity = quantity,
                ImportPrice = importPrice,
                SellPrice = sellPrice,
                SupplierName = supplierName,
                ImportDate = importDate,
                ExpiryDate = expiryDate,
                Notes = notes,
                NearExpiryDays = nearExpiryDaysOverride,
                KeepDisabled = keepDisabled
            }
        };

        var result = await SendImportBatch(request);
        return Json(new { success = result.success, message = result.message });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ImportStockBatch(string cartJson)
    {
        try
        {
            var items = JsonSerializer.Deserialize<List<ImportCartItem>>(cartJson ?? string.Empty, JsonOptions);
            if (items is null || items.Count == 0)
            {
                return Json(new { success = false, message = "Gio nhap trong" });
            }

            var result = await SendImportBatch(items);
            return Json(new { success = result.success, message = result.message });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = "Loi he thong: " + ex.Message });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ExportStock(int productId, int quantity, string reason, string notes)
    {
        var request = new List<ExportCartItem>
        {
            new()
            {
                ProductId = productId,
                Quantity = quantity,
                Reason = reason,
                Notes = notes
            }
        };

        var result = await SendExportBatch(request);
        return Json(new { success = result.success, message = result.message });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ExportAllExpired()
    {
        var client = CreateAuthorizedClient("Catalog");
        var response = await client.PostAsync("/api/admin/warehouse/export-expired", content: null);

        if (!response.IsSuccessStatusCode)
        {
            return Json(new { success = false, message = await ReadApiErrorAsync(response, "Khong the xuat hang het han") });
        }

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;

        return Json(new
        {
            success = root.TryGetProperty("success", out var success) && success.GetBoolean(),
            message = root.TryGetProperty("message", out var message) ? message.GetString() : "Da xu ly"
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ExportStockBatch(string cartJson)
    {
        try
        {
            var items = JsonSerializer.Deserialize<List<ExportCartItem>>(cartJson ?? string.Empty, JsonOptions);
            if (items is null || items.Count == 0)
            {
                return Json(new { success = false, message = "Gio xuat trong" });
            }

            var result = await SendExportBatch(items);
            return Json(new { success = result.success, message = result.message });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = "Loi he thong: " + ex.Message });
        }
    }

    public async Task<IActionResult> Details(int id)
    {
        var client = CreateAuthorizedClient("Catalog");
        var response = await client.GetAsync($"/api/admin/warehouse/products/{id}");
        if (!response.IsSuccessStatusCode)
        {
            TempData["ErrorMessage"] = await ReadApiErrorAsync(response, "Khong tim thay san pham");
            return RedirectToAction(nameof(Index));
        }

        var payload = await response.Content.ReadFromJsonAsync<WarehouseDetailsApiResponse>(JsonOptions);
        if (payload?.data?.product is null)
        {
            TempData["ErrorMessage"] = "Khong co du lieu chi tiet san pham";
            return RedirectToAction(nameof(Index));
        }

        var model = MapWarehouseProduct(payload.data.product);

        ViewBag.History = payload.data.history?.Select(MapHistory).ToList() ?? new List<WarehouseHistory>();
        ViewBag.TotalLots = payload.data.totalLots;
        ViewBag.AllLots = payload.data.allLots?.Select(MapLot).ToList() ?? new List<Warehouse>();

        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> TransactionHistory(string transactionType = "", int page = 1)
    {
        var client = CreateAuthorizedClient("Catalog");
        var query = $"transactionType={Uri.EscapeDataString(transactionType ?? string.Empty)}&page={page}&pageSize=20";

        var response = await client.GetAsync($"/api/admin/warehouse/transactions?{query}");
        if (!response.IsSuccessStatusCode)
        {
            TempData["ErrorMessage"] = await ReadApiErrorAsync(response, "Khong the tai lich su giao dich");
            return View(new List<WarehouseTransactionViewModel>());
        }

        var payload = await response.Content.ReadFromJsonAsync<WarehouseTransactionsApiResponse>(JsonOptions);
        var data = payload?.data;
        var items = data?.items?.Select(MapTransaction).ToList() ?? new List<WarehouseTransactionViewModel>();

        ViewBag.TransactionType = transactionType;
        ViewBag.CurrentPage = data?.currentPage ?? page;
        ViewBag.TotalPages = data?.totalPages ?? 1;
        ViewBag.TotalItems = data?.totalItems ?? items.Count;

        return View(items);
    }

    [HttpGet]
    public async Task<IActionResult> TransactionDetails(int id)
    {
        var transaction = await LoadTransaction(id);
        if (transaction is null)
        {
            TempData["ErrorMessage"] = "Khong tim thay phieu.";
            return RedirectToAction(nameof(TransactionHistory));
        }

        return View(transaction);
    }

    [HttpGet]
    public async Task<IActionResult> GetTransactionDetails(int id)
    {
        var transaction = await LoadTransaction(id);
        if (transaction is null)
        {
            return Json(new { success = false, message = "Khong tim thay phieu" });
        }

        return Json(new { success = true, data = transaction });
    }

    private async Task<WarehouseTransactionViewModel?> LoadTransaction(int id)
    {
        var client = CreateAuthorizedClient("Catalog");
        var response = await client.GetAsync($"/api/admin/warehouse/transactions/{id}");
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var payload = await response.Content.ReadFromJsonAsync<WarehouseTransactionDetailsApiResponse>(JsonOptions);
        return payload?.data is null ? null : MapTransaction(payload.data);
    }

    private async Task PopulateImportProducts(WarehouseImportViewModel vm)
    {
        var client = CreateAuthorizedClient("Catalog");
        // Use seller-scoped warehouse products for import dropdown.
        var response = await client.GetAsync("/api/admin/warehouse/products?page=1&pageSize=5000");

        if (!response.IsSuccessStatusCode)
        {
            vm.ProductOptions = Enumerable.Empty<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem>();
            return;
        }

        var products = await response.Content.ReadFromJsonAsync<List<CatalogProductDto>>(JsonOptions) ?? new List<CatalogProductDto>();

        vm.ProductOptions = products
            .OrderBy(p => p.productName)
            .Select(p => new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem
            {
                Value = p.productId.ToString(),
                Text = $"{p.productName} ({p.sku})"
            })
            .ToList();
    }

    private async Task<(bool success, string message)> SendImportBatch(List<ImportCartItem> items)
    {
        var client = CreateAuthorizedClient("Catalog");
        var response = await client.PostAsJsonAsync("/api/admin/warehouse/import-batch", items);

        if (!response.IsSuccessStatusCode)
        {
            return (false, await ReadApiErrorAsync(response, "Khong the nhap kho"));
        }

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;

        return (
            root.TryGetProperty("success", out var success) && success.GetBoolean(),
            root.TryGetProperty("message", out var message) ? message.GetString() ?? "Da xu ly" : "Da xu ly");
    }

    private async Task<(bool success, string message)> SendExportBatch(List<ExportCartItem> items)
    {
        var client = CreateAuthorizedClient("Catalog");
        var response = await client.PostAsJsonAsync("/api/admin/warehouse/export-batch", items);

        if (!response.IsSuccessStatusCode)
        {
            return (false, await ReadApiErrorAsync(response, "Khong the xuat kho"));
        }

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;

        return (
            root.TryGetProperty("success", out var success) && success.GetBoolean(),
            root.TryGetProperty("message", out var message) ? message.GetString() ?? "Da xu ly" : "Da xu ly");
    }

    private HttpClient CreateAuthorizedClient(string clientName)
    {
        var client = _httpClientFactory.CreateClient(clientName);

        client.DefaultRequestHeaders.Remove("Authorization");
        client.DefaultRequestHeaders.Authorization = null;

        var token = GetAccessToken(AccessTokenSessionKey);
        if (!string.IsNullOrWhiteSpace(token))
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return client;
    }

    private static WarehouseViewModel MapWarehouseProduct(WarehouseProductDto p)
    {
        return new WarehouseViewModel
        {
            WarehouseID = p.warehouseID,
            ProductID = p.productID,
            ProductName = p.productName ?? string.Empty,
            SKU = p.sku ?? string.Empty,
            CategoryName = p.categoryName,
            ImageFileName = p.imageFileName ?? "no-image.png",
            StockQuantity = p.stockQuantity,
            ShelfQuantity = p.shelfQuantity,
            WarehouseQuantity = p.warehouseQuantity,
            MaxStock = p.maxStock,
            IsManuallyDisabled = p.isManuallyDisabled,
            ImportPrice = p.importPrice,
            SellPrice = p.sellPrice,
            SupplierName = p.supplierName,
            ImportDate = p.importDate,
            ExpiryDate = p.expiryDate,
            LastUpdatedDate = p.lastUpdatedDate,
            Notes = p.notes,
            NearExpiryDays = p.nearExpiryDays > 0 ? p.nearExpiryDays : 7
        };
    }

    private static WarehouseHistory MapHistory(WarehouseHistoryDto h)
    {
        return new WarehouseHistory
        {
            WarehouseID = h.warehouseID,
            TransactionType = h.transactionType ?? string.Empty,
            Quantity = h.quantity,
            Reason = h.reason,
            CreatedBy = h.createdBy,
            CreatedDate = h.createdDate,
            Notes = h.notes
        };
    }

    private static Warehouse MapLot(WarehouseLotDto lot)
    {
        return new Warehouse
        {
            WarehouseID = lot.warehouseID,
            BatchCode = lot.batchCode
        };
    }

    private static WarehouseTransactionViewModel MapTransaction(WarehouseTransactionDto t)
    {
        return new WarehouseTransactionViewModel
        {
            TransactionID = t.transactionID,
            TransactionCode = t.transactionCode ?? string.Empty,
            TransactionType = t.transactionType ?? string.Empty,
            TransactionDate = t.transactionDate,
            TotalQuantity = t.totalQuantity,
            TotalAmount = t.totalAmount,
            Notes = t.notes,
            CreatedByName = t.createdByName,
            CreatedAt = t.createdAt,
            Details = t.details?.Select(d => new WarehouseTransactionDetailViewModel
            {
                DetailID = d.detailID,
                ProductID = d.productID,
                ProductName = d.productName ?? string.Empty,
                SKU = d.sku ?? string.Empty,
                ImageFileName = d.imageFileName ?? "no-image.png",
                Quantity = d.quantity,
                UnitPrice = d.unitPrice,
                Amount = d.amount,
                BatchCode = d.batchCode,
                ExpiryDate = d.expiryDate,
                Notes = d.notes
            }).ToList() ?? new List<WarehouseTransactionDetailViewModel>()
        };
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

            if (root.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.String)
            {
                return message.GetString() ?? fallback;
            }

            if (root.TryGetProperty("detail", out var detail) && detail.ValueKind == JsonValueKind.String)
            {
                return detail.GetString() ?? fallback;
            }

            if (root.TryGetProperty("title", out var title) && title.ValueKind == JsonValueKind.String)
            {
                return title.GetString() ?? fallback;
            }
        }
        catch
        {
            // ignore parse failure
        }

        return fallback;
    }

    private sealed class CatalogProductDto
    {
        public int productId { get; set; }

        public string? productName { get; set; }

        public string? sku { get; set; }
    }

    private sealed class ProductInfoApiResponse
    {
        public bool success { get; set; }

        public int nearExpiryDays { get; set; }
    }

    private sealed class WarehouseProductsApiResponse
    {
        public bool success { get; set; }

        public WarehouseProductsDataDto? data { get; set; }
    }

    private sealed class WarehouseProductsDataDto
    {
        public List<WarehouseProductDto>? products { get; set; }

        public int totalItems { get; set; }

        public int totalPages { get; set; }

        public int currentPage { get; set; }

        public int pageSize { get; set; }

        public WarehouseSummaryDto? summary { get; set; }
    }

    private sealed class WarehouseSummaryDto
    {
        public int totalProducts { get; set; }

        public int lowStockCount { get; set; }

        public int outOfStockCount { get; set; }

        public decimal totalWarehouseValue { get; set; }
    }

    private sealed class WarehouseDetailsApiResponse
    {
        public bool success { get; set; }

        public WarehouseDetailsDataDto? data { get; set; }
    }

    private sealed class WarehouseDetailsDataDto
    {
        public WarehouseProductDto? product { get; set; }

        public List<WarehouseHistoryDto>? history { get; set; }

        public int totalLots { get; set; }

        public List<WarehouseLotDto>? allLots { get; set; }
    }

    private sealed class WarehouseProductDto
    {
        public int warehouseID { get; set; }

        public int productID { get; set; }

        public string? productName { get; set; }

        public string? sku { get; set; }

        public string? categoryName { get; set; }

        public string? imageFileName { get; set; }

        public int stockQuantity { get; set; }

        public int shelfQuantity { get; set; }

        public int warehouseQuantity { get; set; }

        public int maxStock { get; set; }

        public bool isManuallyDisabled { get; set; }

        public decimal importPrice { get; set; }

        public decimal sellPrice { get; set; }

        public string? supplierName { get; set; }

        public DateTime? importDate { get; set; }

        public DateTime? expiryDate { get; set; }

        public DateTime? lastUpdatedDate { get; set; }

        public string? notes { get; set; }

        public int nearExpiryDays { get; set; }
    }

    private sealed class WarehouseHistoryDto
    {
        public int warehouseID { get; set; }

        public string? transactionType { get; set; }

        public int quantity { get; set; }

        public string? reason { get; set; }

        public int? createdBy { get; set; }

        public DateTime createdDate { get; set; }

        public string? notes { get; set; }
    }

    private sealed class WarehouseLotDto
    {
        public int warehouseID { get; set; }

        public string? batchCode { get; set; }
    }

    private sealed class WarehouseTransactionsApiResponse
    {
        public bool success { get; set; }

        public WarehouseTransactionsDataDto? data { get; set; }
    }

    private sealed class WarehouseTransactionsDataDto
    {
        public List<WarehouseTransactionDto>? items { get; set; }

        public int totalItems { get; set; }

        public int totalPages { get; set; }

        public int currentPage { get; set; }

        public int pageSize { get; set; }
    }

    private sealed class WarehouseTransactionDetailsApiResponse
    {
        public bool success { get; set; }

        public WarehouseTransactionDto? data { get; set; }
    }

    private sealed class WarehouseTransactionDto
    {
        public int transactionID { get; set; }

        public string? transactionCode { get; set; }

        public string? transactionType { get; set; }

        public DateTime transactionDate { get; set; }

        public int totalQuantity { get; set; }

        public decimal totalAmount { get; set; }

        public string? notes { get; set; }

        public string? createdByName { get; set; }

        public DateTime createdAt { get; set; }

        public List<WarehouseTransactionDetailDto>? details { get; set; }
    }

    private sealed class WarehouseTransactionDetailDto
    {
        public int detailID { get; set; }

        public int productID { get; set; }

        public string? productName { get; set; }

        public string? sku { get; set; }

        public string? imageFileName { get; set; }

        public int quantity { get; set; }

        public decimal unitPrice { get; set; }

        public decimal amount { get; set; }

        public string? batchCode { get; set; }

        public DateTime? expiryDate { get; set; }

        public string? notes { get; set; }
    }

    private sealed class ImportCartItem
    {
        public int ProductId { get; set; }

        public string? ProductName { get; set; }

        public int Quantity { get; set; }

        public decimal ImportPrice { get; set; }

        public decimal? SellPrice { get; set; }

        public string? SupplierName { get; set; }

        public string? ImportDate { get; set; }

        public string? ExpiryDate { get; set; }

        public int? NearExpiryDays { get; set; }

        public string? Notes { get; set; }

        public bool KeepDisabled { get; set; }
    }

    private sealed class ExportCartItem
    {
        public int ProductId { get; set; }

        public string? ProductName { get; set; }

        public int Quantity { get; set; }

        public string? Reason { get; set; }

        public string? Notes { get; set; }
    }
}
