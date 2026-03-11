using FreshFarm.Catalog.Api.Dtos;
using FreshFarm.Catalog.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FreshFarm.Catalog.Api.Controllers;

[ApiController]
[Route("api/catalog/admin/readiness")]
[Authorize(Policy = "AdminOnly")]
public sealed class CatalogReadinessAdminController : ControllerBase
{
    private static readonly string[] AllowedStates = { "all", "missing", "ready" };
    private static readonly string[] AllowedInputTypes = { "text", "textarea", "number", "select" };
    private static readonly string[] SyncableKeys = { "weight", "origin", "standard", "preservation" };

    private readonly FreshFarmCatalogDBContext _db;

    public CatalogReadinessAdminController(FreshFarmCatalogDBContext db)
    {
        _db = db;
    }

    [HttpGet("center")]
    public async Task<IActionResult> GetCenter(
        [FromQuery] string? q = null,
        [FromQuery] int? categoryId = null,
        [FromQuery] string? state = null,
        CancellationToken cancellationToken = default)
    {
        var normalizedQuery = q?.Trim();
        var normalizedState = NormalizeState(state);

        var categories = await _db.Categories
            .AsNoTracking()
            .OrderBy(x => x.CategoryName)
            .Select(x => new CategoryOptionRow
            {
                CategoryId = x.CategoryId,
                CategoryName = x.CategoryName,
                IsActive = x.IsActive
            })
            .ToListAsync(cancellationToken);

        var attributeQuery = _db.CategoryAttributes
            .AsNoTracking()
            .Include(x => x.Category)
            .AsQueryable();

        if (categoryId.HasValue && categoryId.Value > 0)
        {
            attributeQuery = attributeQuery.Where(x => x.CategoryId == categoryId.Value);
        }

        if (!string.IsNullOrWhiteSpace(normalizedQuery))
        {
            attributeQuery = attributeQuery.Where(x =>
                x.AttributeKey.Contains(normalizedQuery) ||
                x.DisplayName.Contains(normalizedQuery) ||
                x.Category.CategoryName.Contains(normalizedQuery));
        }

        var attributeRows = await attributeQuery
            .OrderBy(x => x.Category.CategoryName)
            .ThenBy(x => x.SortOrder)
            .ThenBy(x => x.DisplayName)
            .Select(x => new CategoryAttributeRow
            {
                CategoryAttributeId = x.CategoryAttributeId,
                CategoryId = x.CategoryId,
                CategoryName = x.Category.CategoryName,
                AttributeKey = x.AttributeKey,
                DisplayName = x.DisplayName,
                InputType = x.InputType,
                IsRequired = x.IsRequired,
                IsFacet = x.IsFacet,
                SortOrder = x.SortOrder,
                Placeholder = x.Placeholder,
                IsActive = x.IsActive,
                CreatedAt = x.CreatedAt,
                UpdatedAt = x.UpdatedAt
            })
            .ToListAsync(cancellationToken);

        var productQuery = _db.Products
            .AsNoTracking()
            .Include(x => x.Category)
            .AsQueryable();

        if (categoryId.HasValue && categoryId.Value > 0)
        {
            productQuery = productQuery.Where(x => x.CategoryId == categoryId.Value);
        }

        if (!string.IsNullOrWhiteSpace(normalizedQuery))
        {
            productQuery = productQuery.Where(x =>
                x.ProductName.Contains(normalizedQuery) ||
                x.Sku.Contains(normalizedQuery) ||
                x.Category.CategoryName.Contains(normalizedQuery));
        }

        var productRows = await productQuery
            .OrderByDescending(x => x.CreatedDate)
            .Select(x => new ProductRow
            {
                ProductId = x.ProductId,
                ProductName = x.ProductName,
                Sku = x.Sku,
                Status = x.Status,
                CategoryId = x.CategoryId,
                CategoryName = x.Category.CategoryName,
                CreatedDate = x.CreatedDate
            })
            .ToListAsync(cancellationToken);

        var productIds = productRows.Select(x => x.ProductId).ToList();
        var attributeIds = attributeRows.Select(x => x.CategoryAttributeId).ToList();

        var valueRows = await _db.ProductAttributeValues
            .AsNoTracking()
            .Where(x => productIds.Contains(x.ProductId) && attributeIds.Contains(x.CategoryAttributeId))
            .Select(x => new ProductAttributeValueRow
            {
                ProductId = x.ProductId,
                CategoryAttributeId = x.CategoryAttributeId,
                ValueText = x.ValueText,
                Source = x.Source
            })
            .ToListAsync(cancellationToken);

        var requiredByCategory = attributeRows
            .Where(x => x.IsRequired && x.IsActive)
            .GroupBy(x => x.CategoryId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var filledValues = valueRows
            .Where(x => !string.IsNullOrWhiteSpace(x.ValueText))
            .GroupBy(x => x.ProductId)
            .ToDictionary(g => g.Key, g => g.Select(v => v.CategoryAttributeId).ToHashSet());

        var readinessRows = productRows.Select(product =>
        {
            requiredByCategory.TryGetValue(product.CategoryId, out var requiredAttrs);
            requiredAttrs ??= new List<CategoryAttributeRow>();
            filledValues.TryGetValue(product.ProductId, out var filledAttrIds);
            filledAttrIds ??= new HashSet<int>();

            var missing = requiredAttrs
                .Where(attr => !filledAttrIds.Contains(attr.CategoryAttributeId))
                .Select(attr => attr.DisplayName)
                .ToList();

            var totalRequired = requiredAttrs.Count;
            var readyCount = totalRequired - missing.Count;
            var readinessScore = totalRequired == 0 ? 100 : (int)Math.Round((readyCount * 100d) / totalRequired, MidpointRounding.AwayFromZero);

            return new ProductReadinessRow
            {
                ProductId = product.ProductId,
                ProductName = product.ProductName,
                Sku = product.Sku,
                Status = product.Status,
                CategoryId = product.CategoryId,
                CategoryName = product.CategoryName,
                CreatedDate = product.CreatedDate,
                TotalRequired = totalRequired,
                ReadyCount = readyCount,
                MissingCount = missing.Count,
                ReadinessScore = readinessScore,
                MissingAttributes = missing
            };
        }).Where(row =>
            normalizedState == "all" ||
            (normalizedState == "missing" && row.MissingCount > 0) ||
            (normalizedState == "ready" && row.MissingCount == 0))
        .ToList();

        return Ok(new
        {
            stats = new
            {
                totalCategories = categories.Count,
                totalAttributes = attributeRows.Count,
                requiredAttributes = attributeRows.Count(x => x.IsRequired && x.IsActive),
                productsReady = readinessRows.Count(x => x.MissingCount == 0),
                productsMissing = readinessRows.Count(x => x.MissingCount > 0),
                facetAttributes = attributeRows.Count(x => x.IsFacet && x.IsActive)
            },
            filters = new
            {
                q = normalizedQuery ?? string.Empty,
                categoryId,
                state = normalizedState,
                stateOptions = BuildStateOptions(),
                categories = categories
            },
            attributes = attributeRows,
            readiness = readinessRows
        });
    }

    [HttpPost("category-attributes")]
    public async Task<IActionResult> CreateCategoryAttribute([FromBody] CategoryAttributeUpsertRequest request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        var validationError = await ValidateRequestAsync(request, null, cancellationToken);
        if (!string.IsNullOrWhiteSpace(validationError))
        {
            return BadRequest(new { message = validationError });
        }

        var entity = new CategoryAttribute
        {
            CategoryId = request.CategoryId,
            AttributeKey = NormalizeKey(request.AttributeKey),
            DisplayName = request.DisplayName.Trim(),
            InputType = NormalizeInputType(request.InputType),
            IsRequired = request.IsRequired,
            IsFacet = request.IsFacet,
            SortOrder = request.SortOrder,
            Placeholder = NormalizeOptional(request.Placeholder, 255),
            IsActive = request.IsActive,
            CreatedAt = DateTime.UtcNow
        };

        _db.CategoryAttributes.Add(entity);
        await _db.SaveChangesAsync(cancellationToken);

        return Ok(new { message = "Da tao category attribute.", categoryAttributeId = entity.CategoryAttributeId });
    }

    [HttpPut("category-attributes/{id:int}")]
    public async Task<IActionResult> UpdateCategoryAttribute([FromRoute] int id, [FromBody] CategoryAttributeUpsertRequest request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        var entity = await _db.CategoryAttributes.FirstOrDefaultAsync(x => x.CategoryAttributeId == id, cancellationToken);
        if (entity is null)
        {
            return NotFound(new { message = "Khong tim thay category attribute." });
        }

        var validationError = await ValidateRequestAsync(request, id, cancellationToken);
        if (!string.IsNullOrWhiteSpace(validationError))
        {
            return BadRequest(new { message = validationError });
        }

        entity.CategoryId = request.CategoryId;
        entity.AttributeKey = NormalizeKey(request.AttributeKey);
        entity.DisplayName = request.DisplayName.Trim();
        entity.InputType = NormalizeInputType(request.InputType);
        entity.IsRequired = request.IsRequired;
        entity.IsFacet = request.IsFacet;
        entity.SortOrder = request.SortOrder;
        entity.Placeholder = NormalizeOptional(request.Placeholder, 255);
        entity.IsActive = request.IsActive;
        entity.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);

        return Ok(new { message = "Da cap nhat category attribute." });
    }

    [HttpPost("sync-product-info")]
    public async Task<IActionResult> SyncProductInfo([FromQuery] int? categoryId = null, CancellationToken cancellationToken = default)
    {
        var attributes = await _db.CategoryAttributes
            .Where(x => x.IsActive && SyncableKeys.Contains(x.AttributeKey))
            .ToListAsync(cancellationToken);

        if (categoryId.HasValue && categoryId.Value > 0)
        {
            attributes = attributes.Where(x => x.CategoryId == categoryId.Value).ToList();
        }

        if (attributes.Count == 0)
        {
            return Ok(new { message = "Khong co category attribute nao phu hop de sync.", upserted = 0 });
        }

        var products = await _db.Products
            .Include(x => x.ProductInfos)
            .Where(x => !categoryId.HasValue || categoryId.Value <= 0 || x.CategoryId == categoryId.Value)
            .ToListAsync(cancellationToken);

        var attributeMap = attributes
            .GroupBy(x => x.CategoryId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var productIds = products.Select(x => x.ProductId).ToList();
        var attributeIds = attributes.Select(x => x.CategoryAttributeId).ToList();

        var existing = await _db.ProductAttributeValues
            .Where(x => productIds.Contains(x.ProductId) && attributeIds.Contains(x.CategoryAttributeId))
            .ToListAsync(cancellationToken);

        var existingLookup = existing.ToDictionary(x => $"{x.ProductId}:{x.CategoryAttributeId}");
        var affected = 0;

        foreach (var product in products)
        {
            if (!attributeMap.TryGetValue(product.CategoryId, out var categoryAttributes))
            {
                continue;
            }

            var info = product.ProductInfos.OrderByDescending(x => x.InfoId).FirstOrDefault();
            foreach (var attribute in categoryAttributes)
            {
                var rawValue = ExtractProductInfoValue(info, attribute.AttributeKey);
                if (string.IsNullOrWhiteSpace(rawValue))
                {
                    continue;
                }

                var key = $"{product.ProductId}:{attribute.CategoryAttributeId}";
                if (existingLookup.TryGetValue(key, out var current))
                {
                    if (!string.Equals(current.ValueText, rawValue, StringComparison.Ordinal))
                    {
                        current.ValueText = rawValue;
                        current.NormalizedValue = NormalizeValue(rawValue);
                        current.Source = "product_info_sync";
                        current.UpdatedAt = DateTime.UtcNow;
                        affected++;
                    }

                    continue;
                }

                current = new ProductAttributeValue
                {
                    ProductId = product.ProductId,
                    CategoryAttributeId = attribute.CategoryAttributeId,
                    ValueText = rawValue,
                    NormalizedValue = NormalizeValue(rawValue),
                    Source = "product_info_sync",
                    CreatedAt = DateTime.UtcNow
                };
                _db.ProductAttributeValues.Add(current);
                existingLookup[key] = current;
                affected++;
            }
        }

        await _db.SaveChangesAsync(cancellationToken);

        return Ok(new { message = "Da sync product info vao product attribute values.", upserted = affected });
    }

    private async Task<string?> ValidateRequestAsync(CategoryAttributeUpsertRequest request, int? excludeId, CancellationToken cancellationToken)
    {
        var categoryExists = await _db.Categories
            .AsNoTracking()
            .AnyAsync(x => x.CategoryId == request.CategoryId, cancellationToken);
        if (!categoryExists)
        {
            return "Danh muc khong ton tai.";
        }

        var key = NormalizeKey(request.AttributeKey);
        if (string.IsNullOrWhiteSpace(key))
        {
            return "Attribute key khong hop le.";
        }

        if (!AllowedInputTypes.Contains(NormalizeInputType(request.InputType)))
        {
            return "Input type khong hop le.";
        }

        var duplicate = await _db.CategoryAttributes.AsNoTracking()
            .AnyAsync(x =>
                x.CategoryId == request.CategoryId &&
                x.AttributeKey == key &&
                (!excludeId.HasValue || x.CategoryAttributeId != excludeId.Value), cancellationToken);

        return duplicate ? "Attribute key da ton tai trong danh muc nay." : null;
    }

    private static IEnumerable<object> BuildStateOptions()
        => new[]
        {
            new { value = "all", text = "Tat ca" },
            new { value = "missing", text = "Thieu attribute" },
            new { value = "ready", text = "Da san sang" }
        };

    private static string NormalizeState(string? value)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? "all" : value.Trim().ToLowerInvariant();
        return AllowedStates.Contains(normalized) ? normalized : "all";
    }

    private static string NormalizeKey(string value)
        => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToLowerInvariant().Replace(' ', '_');

    private static string NormalizeInputType(string? value)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? "text" : value.Trim().ToLowerInvariant();
        return AllowedInputTypes.Contains(normalized) ? normalized : "text";
    }

    private static string? NormalizeOptional(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }

    private static string NormalizeValue(string value)
        => value.Trim().ToLowerInvariant();

    private static string? ExtractProductInfoValue(ProductInfo? info, string key)
        => key switch
        {
            "weight" => info?.Weight?.Trim(),
            "origin" => info?.Origin?.Trim(),
            "standard" => info?.Standard?.Trim(),
            "preservation" => info?.Preservation?.Trim(),
            _ => null
        };

    private sealed class CategoryOptionRow
    {
        public int CategoryId { get; set; }

        public string CategoryName { get; set; } = string.Empty;

        public bool IsActive { get; set; }
    }

    private sealed class CategoryAttributeRow
    {
        public int CategoryAttributeId { get; set; }

        public int CategoryId { get; set; }

        public string CategoryName { get; set; } = string.Empty;

        public string AttributeKey { get; set; } = string.Empty;

        public string DisplayName { get; set; } = string.Empty;

        public string InputType { get; set; } = string.Empty;

        public bool IsRequired { get; set; }

        public bool IsFacet { get; set; }

        public int SortOrder { get; set; }

        public string? Placeholder { get; set; }

        public bool IsActive { get; set; }

        public DateTime CreatedAt { get; set; }

        public DateTime? UpdatedAt { get; set; }
    }

    private sealed class ProductRow
    {
        public int ProductId { get; set; }

        public string ProductName { get; set; } = string.Empty;

        public string? Sku { get; set; }

        public bool Status { get; set; }

        public int CategoryId { get; set; }

        public string CategoryName { get; set; } = string.Empty;

        public DateTime CreatedDate { get; set; }
    }

    private sealed class ProductAttributeValueRow
    {
        public int ProductId { get; set; }

        public int CategoryAttributeId { get; set; }

        public string ValueText { get; set; } = string.Empty;

        public string? Source { get; set; }
    }

    private sealed class ProductReadinessRow
    {
        public int ProductId { get; set; }

        public string ProductName { get; set; } = string.Empty;

        public string? Sku { get; set; }

        public bool Status { get; set; }

        public int CategoryId { get; set; }

        public string CategoryName { get; set; } = string.Empty;

        public DateTime CreatedDate { get; set; }

        public int TotalRequired { get; set; }

        public int ReadyCount { get; set; }

        public int MissingCount { get; set; }

        public int ReadinessScore { get; set; }

        public List<string> MissingAttributes { get; set; } = new();
    }
}
