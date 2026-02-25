using FreshFarm.Web.Bff.Areas.Seller.Infrastructure;
using FreshFarm.Web.Bff.Areas.Seller.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace FreshFarm.Web.Bff.Areas.Seller.Controllers;

[Authorize(Roles = "Seller")]
[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
[Area("Seller")]
public class UnitController : LegacySellerControllerBase
{
    private const string AccessTokenSessionKey = "ACCESS_TOKEN";
    private readonly IHttpClientFactory _httpClientFactory;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public UnitController(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public async Task<IActionResult> Index()
    {
        var units = await GetAllUnitsAsync();
        ViewBag.Stats = GetUnitStats(units);
        return View(units);
    }

    public IActionResult Create()
    {
        var model = new UnitViewModel
        {
            IsActive = true
        };

        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(UnitViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var units = await GetAllUnitsAsync();

        var exists = units.Any(u =>
            u.UnitName.Equals(model.UnitName, StringComparison.OrdinalIgnoreCase) ||
            u.Symbol.Equals(model.Symbol, StringComparison.OrdinalIgnoreCase));

        if (exists)
        {
            ModelState.AddModelError(string.Empty, "Tên đơn vị hoặc ký hiệu đã tồn tại!");
            return View(model);
        }

        var client = CreateCatalogClient();
        var response = await client.PostAsJsonAsync("/api/units", new
        {
            unitName = model.UnitName.Trim(),
            symbol = model.Symbol.Trim(),
            description = model.Description?.Trim(),
            isActive = model.IsActive
        });

        if (!response.IsSuccessStatusCode)
        {
            ModelState.AddModelError(string.Empty, await ReadApiErrorAsync(response, "Không thể thêm đơn vị tính."));
            return View(model);
        }

        TempData["SuccessMessage"] = "Thêm đơn vị tính thành công!";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(int? id)
    {
        if (!id.HasValue)
        {
            TempData["ErrorMessage"] = "ID không hợp lệ!";
            return RedirectToAction(nameof(Index));
        }

        var unit = await GetUnitByIdAsync(id.Value);
        if (unit is null)
        {
            TempData["ErrorMessage"] = "Không tìm thấy đơn vị tính!";
            return RedirectToAction(nameof(Index));
        }

        var model = new UnitViewModel
        {
            UnitID = unit.UnitID,
            UnitName = unit.UnitName,
            Symbol = unit.Symbol,
            Description = unit.Description,
            IsActive = unit.IsActive,
            CreatedDate = unit.CreatedDate,
            UnitType = GetUnitType(unit.UnitName, unit.Symbol)
        };

        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(UnitViewModel model)
    {
        if (!ModelState.IsValid)
        {
            if (IsAjaxRequest())
            {
                var modelErrors = ModelState.Values
                    .SelectMany(v => v.Errors)
                    .Select(e => e.ErrorMessage)
                    .Where(m => !string.IsNullOrWhiteSpace(m));

                return Json(new { success = false, message = string.Join(", ", modelErrors) });
            }

            return View(model);
        }

        var existing = await GetUnitByIdAsync(model.UnitID);
        if (existing is null)
        {
            if (IsAjaxRequest())
            {
                return Json(new { success = false, message = "Không tìm thấy đơn vị tính!" });
            }

            TempData["ErrorMessage"] = "Không tìm thấy đơn vị tính!";
            return RedirectToAction(nameof(Index));
        }

        var units = await GetAllUnitsAsync();

        var exists = units.Any(u =>
            u.UnitID != model.UnitID &&
            (u.UnitName.Equals(model.UnitName, StringComparison.OrdinalIgnoreCase) ||
             u.Symbol.Equals(model.Symbol, StringComparison.OrdinalIgnoreCase)));

        if (exists)
        {
            if (IsAjaxRequest())
            {
                return Json(new { success = false, message = "Tên đơn vị hoặc ký hiệu đã tồn tại!" });
            }

            ModelState.AddModelError(string.Empty, "Tên đơn vị hoặc ký hiệu đã tồn tại!");
            return View(model);
        }

        var client = CreateCatalogClient();
        var response = await client.PutAsJsonAsync($"/api/units/{model.UnitID}", new
        {
            unitName = model.UnitName.Trim(),
            symbol = model.Symbol.Trim(),
            description = model.Description?.Trim(),
            isActive = model.IsActive
        });

        if (!response.IsSuccessStatusCode)
        {
            var apiError = await ReadApiErrorAsync(response, "Không thể cập nhật đơn vị tính.");

            if (IsAjaxRequest())
            {
                return Json(new { success = false, message = apiError });
            }

            ModelState.AddModelError(string.Empty, apiError);
            return View(model);
        }

        if (IsAjaxRequest())
        {
            return Json(new { success = true, message = "Cập nhật đơn vị tính thành công!" });
        }

        TempData["SuccessMessage"] = "Cập nhật đơn vị tính thành công!";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<JsonResult> GetById(int id)
    {
        var unit = await GetUnitByIdAsync(id);
        if (unit is null)
        {
            return Json(new { success = false, message = "Không tìm thấy đơn vị cần sửa." });
        }

        return Json(new
        {
            success = true,
            data = new
            {
                unit.UnitID,
                unit.UnitName,
                unit.Symbol,
                unit.Description,
                unit.IsActive
            }
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<JsonResult> CreateAjax(UnitViewModel model)
    {
        if (!ModelState.IsValid)
        {
            var errors = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage);
            return Json(new { success = false, message = string.Join(", ", errors) });
        }

        var units = await GetAllUnitsAsync();
        var exists = units.Any(u =>
            u.UnitName.Equals(model.UnitName, StringComparison.OrdinalIgnoreCase) ||
            u.Symbol.Equals(model.Symbol, StringComparison.OrdinalIgnoreCase));

        if (exists)
        {
            return Json(new { success = false, message = "Tên đơn vị hoặc ký hiệu đã tồn tại!" });
        }

        var client = CreateCatalogClient();
        var response = await client.PostAsJsonAsync("/api/units", new
        {
            unitName = model.UnitName.Trim(),
            symbol = model.Symbol.Trim(),
            description = model.Description?.Trim(),
            isActive = model.IsActive
        });

        if (!response.IsSuccessStatusCode)
        {
            return Json(new
            {
                success = false,
                message = await ReadApiErrorAsync(response, "Không thể thêm đơn vị tính.")
            });
        }

        return Json(new { success = true, message = "Thêm đơn vị tính thành công!" });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<JsonResult> Delete(int id)
    {
        var client = CreateCatalogClient();
        var response = await client.DeleteAsync($"/api/units/{id}");

        if (!response.IsSuccessStatusCode)
        {
            return Json(new
            {
                success = false,
                message = await ReadApiErrorAsync(response, "Không thể xóa đơn vị tính.")
            });
        }

        return Json(new { success = true, message = "Xóa đơn vị tính thành công!" });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<JsonResult> ToggleStatus(int id)
    {
        var client = CreateCatalogClient();
        var response = await client.PostAsync($"/api/units/{id}/toggle-status", content: null);

        if (!response.IsSuccessStatusCode)
        {
            return Json(new
            {
                success = false,
                message = await ReadApiErrorAsync(response, "Không thể cập nhật trạng thái đơn vị tính.")
            });
        }

        var payload = await response.Content.ReadFromJsonAsync<ApiToggleResponse>(JsonOptions);

        return Json(new
        {
            success = true,
            message = payload?.Message ?? "Cập nhật trạng thái thành công!"
        });
    }

    public async Task<JsonResult> GetUnits(string search = "", string status = "all")
    {
        var units = await GetAllUnitsAsync();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var keyword = search.ToLower();
            units = units.Where(u =>
                u.UnitName.ToLower().Contains(keyword) ||
                u.Symbol.ToLower().Contains(keyword) ||
                (u.Description ?? string.Empty).ToLower().Contains(keyword)).ToList();
        }

        if (!string.Equals(status, "all", StringComparison.OrdinalIgnoreCase))
        {
            var isActive = string.Equals(status, "active", StringComparison.OrdinalIgnoreCase);
            units = units.Where(u => u.IsActive == isActive).ToList();
        }

        return Json(new { success = true, data = units });
    }

    private async Task<List<UnitViewModel>> GetAllUnitsAsync()
    {
        var client = CreateCatalogClient();
        var response = await client.GetAsync("/api/units");

        if (!response.IsSuccessStatusCode)
        {
            return new List<UnitViewModel>();
        }

        var apiUnits = await response.Content.ReadFromJsonAsync<List<ApiUnitDto>>(JsonOptions) ?? new List<ApiUnitDto>();

        return apiUnits
            .OrderByDescending(u => u.CreatedDate)
            .Select(u => new UnitViewModel
            {
                UnitID = u.UnitId,
                UnitName = u.UnitName,
                Symbol = u.Symbol,
                Description = u.Description,
                IsActive = u.IsActive,
                CreatedDate = u.CreatedDate,
                UnitType = GetUnitType(u.UnitName, u.Symbol)
            })
            .ToList();
    }

    private async Task<Unit?> GetUnitByIdAsync(int id)
    {
        var client = CreateCatalogClient();
        var response = await client.GetAsync($"/api/units/{id}");

        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var dto = await response.Content.ReadFromJsonAsync<ApiUnitDto>(JsonOptions);
        if (dto is null)
        {
            return null;
        }

        return new Unit
        {
            UnitID = dto.UnitId,
            UnitName = dto.UnitName,
            Symbol = dto.Symbol,
            Description = dto.Description,
            IsActive = dto.IsActive,
            CreatedDate = dto.CreatedDate
        };
    }

    private static UnitStatsViewModel GetUnitStats(IEnumerable<UnitViewModel> units)
    {
        var items = units.ToList();
        var firstDayOfMonth = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1);

        return new UnitStatsViewModel
        {
            TotalUnits = items.Count,
            ActiveUnits = items.Count(u => u.IsActive),
            InactiveUnits = items.Count(u => !u.IsActive),
            NewUnitsThisMonth = items.Count(u => u.CreatedDate >= firstDayOfMonth)
        };
    }

    private static bool IsAjaxRequest(HttpRequest request)
    {
        return string.Equals(request.Headers["X-Requested-With"], "XMLHttpRequest", StringComparison.OrdinalIgnoreCase);
    }

    private bool IsAjaxRequest()
    {
        return IsAjaxRequest(Request);
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

    private static string GetUnitType(string unitName, string symbol)
    {
        var symbolLower = (symbol ?? string.Empty).ToLower();
        var nameLower = (unitName ?? string.Empty).ToLower();

        if (new[] { "kg", "g", "tấn", "tạ", "yến", "lạng" }.Contains(symbolLower) ||
            nameLower.Contains("gram") || nameLower.Contains("kilogram"))
        {
            return "Khối lượng";
        }

        if (new[] { "l", "ml" }.Contains(symbolLower) ||
            nameLower.Contains("lít") || nameLower.Contains("mililít"))
        {
            return "Thể tích";
        }

        if (new[] { "m", "cm", "mm", "km" }.Contains(symbolLower) ||
            nameLower.Contains("mét") || nameLower.Contains("centimet"))
        {
            return "Chiều dài";
        }

        if (new[] { "cái", "gói", "hộp", "chai", "túi", "bó", "quả", "con", "chiếc" }.Contains(symbolLower))
        {
            return "Đơn vị đếm";
        }

        return "Khác";
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

    private sealed class ApiToggleResponse
    {
        public string? Message { get; set; }

        public bool NewStatus { get; set; }
    }
}
