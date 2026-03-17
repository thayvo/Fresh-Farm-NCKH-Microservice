using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FreshFarm.Web.Bff.Dtos;

namespace FreshFarm.Web.Bff.Services;

public sealed class CartSessionService : ICartSessionService
{
    private const string LegacyCartSessionKey = "CART_ITEMS";
    private const string AccessTokenSessionKey = "ACCESS_TOKEN";
    private const string RequestItemsCacheKey = "FF_CART_DB_ITEMS";
    private const string LegacyMigrationFlagKey = "FF_CART_LEGACY_MIGRATED";

    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IHttpClientFactory _httpClientFactory;

    public CartSessionService(IHttpContextAccessor httpContextAccessor, IHttpClientFactory httpClientFactory)
    {
        _httpContextAccessor = httpContextAccessor;
        _httpClientFactory = httpClientFactory;
    }

    private HttpContext? HttpContext => _httpContextAccessor.HttpContext;

    private ISession? Session => HttpContext?.Session;

    public async Task<List<CartItemDto>> GetItemsAsync()
    {
        await EnsureLegacySessionMigratedAsync();

        var context = HttpContext;
        if (context?.Items[RequestItemsCacheKey] is List<CartItemDto> cachedItems)
        {
            return cachedItems;
        }

        var token = Session?.GetString(AccessTokenSessionKey);
        if (string.IsNullOrWhiteSpace(token))
        {
            var legacyItems = GetLegacySessionItems();
            var enrichedLegacyItems = await BackfillMissingSnapshotsAsync(legacyItems, token: null);
            CacheItems(enrichedLegacyItems);
            return enrichedLegacyItems;
        }

        var items = await GetItemsFromApiAsync(token);
        var enrichedItems = await BackfillMissingSnapshotsAsync(items, token);
        CacheItems(enrichedItems);
        return enrichedItems;
    }

    public async Task SetItemsAsync(List<CartItemDto> items)
    {
        var normalizedItems = NormalizeItems(items ?? new List<CartItemDto>());
        var token = Session?.GetString(AccessTokenSessionKey);
        if (string.IsNullOrWhiteSpace(token))
        {
            SetLegacySessionItems(normalizedItems);
            CacheItems(normalizedItems);
            return;
        }

        await EnsureLegacySessionMigratedAsync();
        var client = CreateAuthorizedOrderingClient(token);
        var response = await client.PutAsJsonAsync("/api/cart/me", new ReplaceCartRequestDto
        {
            Items = normalizedItems.Select(MapToUpsertRequest).ToList()
        });

        response.EnsureSuccessStatusCode();
        CacheItems(normalizedItems);
    }

    public async Task AddOrIncreaseAsync(AddToCartRequestDto request)
    {
        if (request.ProductId <= 0)
        {
            return;
        }

        var token = Session?.GetString(AccessTokenSessionKey);
        var normalized = NormalizeItem(request);
        if (normalized is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(token))
        {
            var legacyItems = GetLegacySessionItems();
            AddOrIncreaseLegacy(legacyItems, normalized);
            SetLegacySessionItems(legacyItems);
            CacheItems(legacyItems);
            return;
        }

        await EnsureLegacySessionMigratedAsync();
        var client = CreateAuthorizedOrderingClient(token);
        var response = await client.PostAsJsonAsync("/api/cart/items/add", MapToUpsertRequest(normalized));
        response.EnsureSuccessStatusCode();

        var items = await ReadCartItemsResponseAsync(response);
        CacheItems(items);
    }

    public Task UpdateQuantityAsync(int productId, int quantity)
    {
        return UpdateQuantityAsync(productId, 0, string.Empty, quantity);
    }

    public async Task UpdateQuantityAsync(int productId, int sellerId, string? cartItemKey, int quantity)
    {
        var items = await GetItemsAsync();
        var existing = FindItem(items, productId, sellerId, cartItemKey);
        if (existing is null)
        {
            return;
        }

        var token = Session?.GetString(AccessTokenSessionKey);
        if (string.IsNullOrWhiteSpace(token))
        {
            if (quantity <= 0)
            {
                items.Remove(existing);
            }
            else
            {
                existing.Quantity = quantity;
            }

            var normalizedItems = NormalizeItems(items);
            SetLegacySessionItems(normalizedItems);
            CacheItems(normalizedItems);
            return;
        }

        var client = CreateAuthorizedOrderingClient(token);
        var response = await client.PostAsJsonAsync("/api/cart/items/update", new UpdateCartItemQuantityRequestDto
        {
            ProductId = existing.ProductId,
            SellerId = existing.SellerId,
            Quantity = quantity
        });

        response.EnsureSuccessStatusCode();
        var updatedItems = await ReadCartItemsResponseAsync(response);
        CacheItems(updatedItems);
    }

    public Task RemoveAsync(int productId)
    {
        return RemoveAsync(productId, 0, string.Empty);
    }

    public async Task RemoveAsync(int productId, int sellerId, string? cartItemKey)
    {
        var items = await GetItemsAsync();
        var existing = FindItem(items, productId, sellerId, cartItemKey);
        if (existing is null)
        {
            return;
        }

        var token = Session?.GetString(AccessTokenSessionKey);
        if (string.IsNullOrWhiteSpace(token))
        {
            items.Remove(existing);
            var normalizedItems = NormalizeItems(items);
            SetLegacySessionItems(normalizedItems);
            CacheItems(normalizedItems);
            return;
        }

        var client = CreateAuthorizedOrderingClient(token);
        var response = await client.PostAsJsonAsync("/api/cart/items/remove", new RemoveCartItemRequestDto
        {
            ProductId = existing.ProductId,
            SellerId = existing.SellerId
        });

        response.EnsureSuccessStatusCode();
        var updatedItems = await ReadCartItemsResponseAsync(response);
        CacheItems(updatedItems);
    }

    public async Task ClearAsync()
    {
        var token = Session?.GetString(AccessTokenSessionKey);
        if (string.IsNullOrWhiteSpace(token))
        {
            ClearLegacySession();
            CacheItems(new List<CartItemDto>());
            return;
        }

        var client = CreateAuthorizedOrderingClient(token);
        var response = await client.PostAsync("/api/cart/clear", content: null);
        response.EnsureSuccessStatusCode();
        ClearLegacySession();
        CacheItems(new List<CartItemDto>());
    }

    public async Task<CartSummaryDto> BuildSummaryAsync(decimal shippingFee)
    {
        var items = await GetItemsAsync();
        var subTotal = items.Sum(x => x.UnitPrice * Math.Max(1, x.Quantity));

        return new CartSummaryDto
        {
            Items = items,
            SubTotal = subTotal,
            ShippingFee = shippingFee,
            GrandTotal = subTotal + shippingFee
        };
    }

    private async Task EnsureLegacySessionMigratedAsync()
    {
        var context = HttpContext;
        if (context is null)
        {
            return;
        }

        if (context.Items.ContainsKey(LegacyMigrationFlagKey))
        {
            return;
        }

        var token = Session?.GetString(AccessTokenSessionKey);
        var legacyItems = GetLegacySessionItems();

        if (string.IsNullOrWhiteSpace(token) || legacyItems.Count == 0)
        {
            context.Items[LegacyMigrationFlagKey] = true;
            return;
        }

        var client = CreateAuthorizedOrderingClient(token);
        var currentItems = await GetItemsFromApiAsync(token);
        var mergedItems = MergeItems(currentItems, legacyItems);

        var response = await client.PutAsJsonAsync("/api/cart/me", new ReplaceCartRequestDto
        {
            Items = mergedItems.Select(MapToUpsertRequest).ToList()
        });

        response.EnsureSuccessStatusCode();
        ClearLegacySession();
        CacheItems(mergedItems);
        context.Items[LegacyMigrationFlagKey] = true;
    }

    private async Task<List<CartItemDto>> GetItemsFromApiAsync(string token)
    {
        var client = CreateAuthorizedOrderingClient(token);
        var response = await client.GetAsync("/api/cart/me");
        response.EnsureSuccessStatusCode();
        return await ReadCartItemsResponseAsync(response);
    }

    private async Task<List<CartItemDto>> BackfillMissingSnapshotsAsync(List<CartItemDto> items, string? token)
    {
        var normalizedItems = NormalizeItems(items);
        var productIdsToRefresh = normalizedItems
            .Where(NeedsSnapshotRefresh)
            .Select(item => item.ProductId)
            .Distinct()
            .ToList();

        if (productIdsToRefresh.Count == 0)
        {
            return normalizedItems;
        }

        var productSnapshots = await LoadCatalogSnapshotsAsync(productIdsToRefresh);
        if (productSnapshots.Count == 0)
        {
            return normalizedItems;
        }

        var changed = false;
        foreach (var item in normalizedItems)
        {
            if (!productSnapshots.TryGetValue(item.ProductId, out var snapshot))
            {
                continue;
            }

            changed |= ApplySnapshot(item, snapshot);
        }

        if (!changed)
        {
            return normalizedItems;
        }

        normalizedItems = NormalizeItems(normalizedItems);

        if (string.IsNullOrWhiteSpace(token))
        {
            SetLegacySessionItems(normalizedItems);
            return normalizedItems;
        }

        var client = CreateAuthorizedOrderingClient(token);
        var response = await client.PutAsJsonAsync("/api/cart/me", new ReplaceCartRequestDto
        {
            Items = normalizedItems.Select(MapToUpsertRequest).ToList()
        });

        response.EnsureSuccessStatusCode();
        return await ReadCartItemsResponseAsync(response);
    }

    private async Task<List<CartItemDto>> ReadCartItemsResponseAsync(HttpResponseMessage response)
    {
        var payload = await response.Content.ReadFromJsonAsync<CartResponseDto>();
        return NormalizeItems(payload?.Items ?? new List<CartItemDto>());
    }

    private HttpClient CreateAuthorizedOrderingClient(string token)
    {
        var client = _httpClientFactory.CreateClient("Ordering");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private async Task<Dictionary<int, CatalogProductSnapshotDto>> LoadCatalogSnapshotsAsync(IEnumerable<int> productIds)
    {
        var client = _httpClientFactory.CreateClient("Catalog");
        var snapshots = new Dictionary<int, CatalogProductSnapshotDto>();

        foreach (var productId in productIds.Distinct())
        {
            if (productId <= 0)
            {
                continue;
            }

            try
            {
                var response = await client.GetAsync($"/api/products/{productId}");
                if (!response.IsSuccessStatusCode)
                {
                    continue;
                }

                var snapshot = await response.Content.ReadFromJsonAsync<CatalogProductSnapshotDto>();
                if (snapshot is null || snapshot.ProductId <= 0)
                {
                    continue;
                }

                snapshots[snapshot.ProductId] = snapshot;
            }
            catch
            {
                // Best-effort enrichment only; cart must still render even if Catalog is unavailable.
            }
        }

        return snapshots;
    }

    private void CacheItems(List<CartItemDto> items)
    {
        var context = HttpContext;
        if (context is null)
        {
            return;
        }

        context.Items[RequestItemsCacheKey] = NormalizeItems(items);
    }

    private List<CartItemDto> GetLegacySessionItems()
    {
        var json = Session?.GetString(LegacyCartSessionKey);
        if (string.IsNullOrWhiteSpace(json))
        {
            return new List<CartItemDto>();
        }

        return JsonSerializer.Deserialize<List<CartItemDto>>(json) ?? new List<CartItemDto>();
    }

    private void SetLegacySessionItems(List<CartItemDto> items)
    {
        if (Session is null)
        {
            return;
        }

        Session.SetString(LegacyCartSessionKey, JsonSerializer.Serialize(items));
    }

    private void ClearLegacySession()
    {
        Session?.Remove(LegacyCartSessionKey);
    }

    private static List<CartItemDto> MergeItems(IEnumerable<CartItemDto> currentItems, IEnumerable<CartItemDto> legacyItems)
    {
        return currentItems
            .Concat(legacyItems)
            .Select(NormalizeItem)
            .Where(item => item is not null)
            .GroupBy(item => item!.CartItemKey, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var merged = group.First()!;
                merged.Quantity = group.Sum(x => Math.Max(1, x!.Quantity));
                merged.UnitPrice = group.LastOrDefault(x => x!.UnitPrice > 0m)?.UnitPrice ?? merged.UnitPrice;
                merged.SellerName = group.LastOrDefault(x => !string.IsNullOrWhiteSpace(x!.SellerName))?.SellerName ?? merged.SellerName;
                merged.ProductName = group.LastOrDefault(x => !string.IsNullOrWhiteSpace(x!.ProductName))?.ProductName ?? merged.ProductName;
                merged.ImageFileName = group.LastOrDefault(x => !string.IsNullOrWhiteSpace(x!.ImageFileName))?.ImageFileName ?? merged.ImageFileName;
                merged.UnitSymbol = group.LastOrDefault(x => !string.IsNullOrWhiteSpace(x!.UnitSymbol))?.UnitSymbol ?? merged.UnitSymbol;
                return merged;
            })
            .ToList();
    }

    private static void AddOrIncreaseLegacy(List<CartItemDto> items, CartItemDto request)
    {
        var existing = items.FirstOrDefault(x => string.Equals(x.CartItemKey, request.CartItemKey, StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            items.Add(request);
            return;
        }

        existing.Quantity += Math.Max(1, request.Quantity);
        existing.UnitPrice = request.UnitPrice;
        existing.SellerName = string.IsNullOrWhiteSpace(request.SellerName) ? existing.SellerName : request.SellerName;
        existing.ProductName = string.IsNullOrWhiteSpace(request.ProductName) ? existing.ProductName : request.ProductName;
        existing.ImageFileName = string.IsNullOrWhiteSpace(request.ImageFileName) ? existing.ImageFileName : request.ImageFileName;
        existing.UnitSymbol = string.IsNullOrWhiteSpace(request.UnitSymbol) ? existing.UnitSymbol : request.UnitSymbol;
    }

    private static CartItemDto? FindItem(IEnumerable<CartItemDto> items, int productId, int sellerId, string? cartItemKey)
    {
        var normalizedKey = (cartItemKey ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(normalizedKey))
        {
            return items.FirstOrDefault(x => string.Equals(x.CartItemKey, normalizedKey, StringComparison.OrdinalIgnoreCase));
        }

        var normalizedSellerId = NormalizeSellerId(sellerId);
        return items.FirstOrDefault(x => x.ProductId == productId && NormalizeSellerId(x.SellerId) == normalizedSellerId);
    }

    private static List<CartItemDto> NormalizeItems(IEnumerable<CartItemDto> items)
    {
        return items
            .Select(NormalizeItem)
            .Where(item => item is not null)
            .GroupBy(item => item!.CartItemKey, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var first = group.First()!;
                first.Quantity = Math.Max(1, group.Sum(x => Math.Max(1, x!.Quantity)));
                return first;
            })
            .ToList();
    }

    private static bool NeedsSnapshotRefresh(CartItemDto item)
    {
        return string.IsNullOrWhiteSpace(item.ProductName) ||
               ProductImagePaths.NormalizeStoredFileName(item.ImageFileName) is null ||
               item.SellerId <= 0 ||
               string.IsNullOrWhiteSpace(item.SellerName) ||
               string.IsNullOrWhiteSpace(item.UnitSymbol) ||
               string.Equals(item.UnitSymbol, "đơn vị", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ApplySnapshot(CartItemDto item, CatalogProductSnapshotDto snapshot)
    {
        var changed = false;

        var normalizedImageFileName = ProductImagePaths.NormalizeStoredFileName(snapshot.ImageFileName) ?? string.Empty;
        if (string.IsNullOrWhiteSpace(item.ProductName) && !string.IsNullOrWhiteSpace(snapshot.ProductName))
        {
            item.ProductName = snapshot.ProductName.Trim();
            changed = true;
        }

        if (ProductImagePaths.NormalizeStoredFileName(item.ImageFileName) is null &&
            !string.IsNullOrWhiteSpace(normalizedImageFileName))
        {
            item.ImageFileName = normalizedImageFileName;
            changed = true;
        }

        if ((string.IsNullOrWhiteSpace(item.UnitSymbol) ||
             string.Equals(item.UnitSymbol, "đơn vị", StringComparison.OrdinalIgnoreCase)) &&
            !string.IsNullOrWhiteSpace(snapshot.UnitSymbol))
        {
            item.UnitSymbol = snapshot.UnitSymbol.Trim();
            changed = true;
        }

        if (snapshot.PrimarySellerId > 0 &&
            (item.SellerId <= 0 || string.IsNullOrWhiteSpace(item.SellerName)) &&
            item.SellerId != snapshot.PrimarySellerId)
        {
            item.SellerId = snapshot.PrimarySellerId.Value;
            changed = true;
        }

        if (snapshot.PrimarySellerId > 0 && string.IsNullOrWhiteSpace(item.SellerName))
        {
            item.SellerName = $"FreshFarm Seller {snapshot.PrimarySellerId}";
            changed = true;
        }

        return changed;
    }

    private static CartItemDto? NormalizeItem(CartItemDto? item)
    {
        if (item is null || item.ProductId <= 0)
        {
            return null;
        }

        var sellerId = NormalizeSellerId(item.SellerId);
        return new CartItemDto
        {
            ProductId = item.ProductId,
            SellerId = sellerId,
            SellerName = item.SellerName?.Trim() ?? string.Empty,
            ProductName = item.ProductName?.Trim() ?? string.Empty,
            ImageFileName = item.ImageFileName?.Trim() ?? string.Empty,
            UnitPrice = item.UnitPrice < 0m ? 0m : item.UnitPrice,
            UnitSymbol = string.IsNullOrWhiteSpace(item.UnitSymbol) ? "đơn vị" : item.UnitSymbol.Trim(),
            Quantity = Math.Max(1, item.Quantity)
        };
    }

    private static CartItemDto? NormalizeItem(AddToCartRequestDto? item)
    {
        if (item is null || item.ProductId <= 0)
        {
            return null;
        }

        var sellerId = NormalizeSellerId(item.SellerId);
        return new CartItemDto
        {
            ProductId = item.ProductId,
            SellerId = sellerId,
            SellerName = item.SellerName?.Trim() ?? string.Empty,
            ProductName = item.ProductName?.Trim() ?? string.Empty,
            ImageFileName = item.ImageFileName?.Trim() ?? string.Empty,
            UnitPrice = item.UnitPrice < 0m ? 0m : item.UnitPrice,
            UnitSymbol = string.IsNullOrWhiteSpace(item.UnitSymbol) ? "đơn vị" : item.UnitSymbol.Trim(),
            Quantity = Math.Max(1, item.Quantity)
        };
    }

    private static int NormalizeSellerId(int sellerId)
    {
        return sellerId > 0 ? sellerId : 0;
    }

    private static UpsertCartItemRequestDto MapToUpsertRequest(CartItemDto item)
    {
        return new UpsertCartItemRequestDto
        {
            ProductId = item.ProductId,
            SellerId = item.SellerId,
            SellerName = item.SellerName,
            ProductName = item.ProductName,
            ImageFileName = item.ImageFileName,
            UnitPrice = item.UnitPrice,
            UnitSymbol = item.UnitSymbol,
            Quantity = Math.Max(1, item.Quantity)
        };
    }

    private sealed class CartResponseDto
    {
        public List<CartItemDto> Items { get; set; } = new();
    }

    private sealed class ReplaceCartRequestDto
    {
        public List<UpsertCartItemRequestDto> Items { get; set; } = new();
    }

    private sealed class UpsertCartItemRequestDto
    {
        public int ProductId { get; set; }
        public int SellerId { get; set; }
        public string SellerName { get; set; } = string.Empty;
        public string ProductName { get; set; } = string.Empty;
        public string ImageFileName { get; set; } = string.Empty;
        public decimal UnitPrice { get; set; }
        public string UnitSymbol { get; set; } = "đơn vị";
        public int Quantity { get; set; } = 1;
    }

    private sealed class UpdateCartItemQuantityRequestDto
    {
        public int ProductId { get; set; }
        public int SellerId { get; set; }
        public int Quantity { get; set; }
    }

    private sealed class RemoveCartItemRequestDto
    {
        public int ProductId { get; set; }
        public int SellerId { get; set; }
    }

    private sealed class CatalogProductSnapshotDto
    {
        public int ProductId { get; set; }
        public string ProductName { get; set; } = string.Empty;
        public string ImageFileName { get; set; } = string.Empty;
        public string UnitSymbol { get; set; } = string.Empty;
        public int? PrimarySellerId { get; set; }
    }
}
