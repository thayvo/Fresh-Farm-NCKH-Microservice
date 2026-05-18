using FreshFarm.Ordering.Api.Models;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using FreshFarm.Ordering.Api.Options;
using FreshFarm.Ordering.Api.Services;

namespace FreshFarm.Ordering.Api.Controllers;

[ApiController]
[Route("api/orders/admin/shippings")]
[Authorize(Policy = "SellerOrAdmin")]
public sealed class ShippingAdminController : ControllerBase
{
    private static readonly string[] PaidStatuses = { "Da thanh toan", "Đã thanh toán", "Hoan tat", "Hoàn tất" };
    private static readonly HashSet<string> TerminalGhnStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "delivered",
        "returned",
        "cancel",
        "delivery_fail",
        "lost",
        "damage"
    };
    private static readonly DateTime SqlDateTimeFloor = new(1900, 1, 1);

    private static readonly List<ProvinceOption> Provinces = new()
    {
        new ProvinceOption(1, "TP. Ho Chi Minh"),
        new ProvinceOption(2, "Ha Noi"),
        new ProvinceOption(3, "Da Nang")
    };

    private static readonly Dictionary<int, List<CommuneOption>> CommunesByProvince = new()
    {
        [1] = new List<CommuneOption>
        {
            new CommuneOption(101, "Phuong 1"),
            new CommuneOption(102, "Phuong 2")
        },
        [2] = new List<CommuneOption>
        {
            new CommuneOption(201, "Phuong A"),
            new CommuneOption(202, "Phuong B")
        },
        [3] = new List<CommuneOption>
        {
            new CommuneOption(301, "Phuong Hai Chau"),
            new CommuneOption(302, "Phuong Thanh Khe")
        }
    };

    private readonly FreshFarmOrderingDBContext _db;
    private readonly CustomerNotificationService _customerNotificationService;
    private readonly InternalServiceAuthOptions _internalServiceAuthOptions;
    private readonly ILogger<ShippingAdminController> _logger;

    public ShippingAdminController(
        FreshFarmOrderingDBContext db,
        CustomerNotificationService customerNotificationService,
        IOptions<InternalServiceAuthOptions> internalServiceAuthOptions,
        ILogger<ShippingAdminController> logger)
    {
        _db = db;
        _customerNotificationService = customerNotificationService;
        _internalServiceAuthOptions = internalServiceAuthOptions.Value;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> Get(
        [FromQuery] string status = "",
        [FromQuery] int? staffId = null,
        [FromQuery] string q = "",
        [FromQuery] string sort = "date_desc",
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        [FromQuery] string deliveredState = "",
        CancellationToken cancellationToken = default)
    {
        var isAdmin = IsAdminUser();
        var sellerId = isAdmin ? (int?)null : TryGetSellerIdFromToken();
        if (!isAdmin && !sellerId.HasValue)
        {
            return Unauthorized(new { success = false, message = "Khong xac dinh duoc seller." });
        }

        if (page < 1)
        {
            page = 1;
        }

        if (pageSize <= 0)
        {
            pageSize = 10;
        }

        var query = _db.Shippings
            .AsNoTracking()
            .Include(s => s.Order)
            .ThenInclude(o => o.Payments)
            .Where(s => isAdmin || s.Order.SellerOrders.Any(so =>
                sellerId.HasValue &&
                so.SellerId == sellerId.Value &&
                so.SellerOrderItems.Any()))
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(s => s.Order.Status == status);
        }

        if (string.Equals(status, "Delivered", StringComparison.OrdinalIgnoreCase) || !string.IsNullOrWhiteSpace(deliveredState))
        {
            if (string.Equals(deliveredState, "pending", StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(s =>
                    s.Order.Status == "Delivered" &&
                    s.Order.Payments.Any(p => p.PaymentMethod == "COD") &&
                    s.Order.Payments.Any(p => p.PaymentMethod == "COD" &&
                        (!p.PaymentDate.HasValue &&
                         (p.PaymentStatus == null || !PaidStatuses.Contains(p.PaymentStatus)))));
            }
            else if (string.Equals(deliveredState, "done", StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(s =>
                    s.Order.Status == "Delivered" &&
                    s.Order.Payments.Any(p => p.PaymentMethod == "COD" &&
                        (p.PaymentDate.HasValue ||
                         (p.PaymentStatus != null && PaidStatuses.Contains(p.PaymentStatus)))));
            }
        }

        if (!string.IsNullOrWhiteSpace(q))
        {
            var keyword = q.Trim();
            if (int.TryParse(keyword, out var code))
            {
                query = query.Where(s => s.OrderId == code || s.ShippingId == code);
            }
            else
            {
                query = query.Where(s =>
                    s.FullName.Contains(keyword) ||
                    s.Phone.Contains(keyword) ||
                    (s.Email != null && s.Email.Contains(keyword)) ||
                    (s.AddressDetail != null && s.AddressDetail.Contains(keyword)) ||
                    (s.Order.BuyerFullName != null && s.Order.BuyerFullName.Contains(keyword)) ||
                    (s.Order.BuyerPhone != null && s.Order.BuyerPhone.Contains(keyword)));
            }
        }

        query = (sort ?? "date_desc").ToLowerInvariant() switch
        {
            "code_asc" => query.OrderBy(s => s.OrderId),
            "code_desc" => query.OrderByDescending(s => s.OrderId),
            "receiver_asc" => query.OrderBy(s => s.FullName),
            "receiver_desc" => query.OrderByDescending(s => s.FullName),
            "status_asc" => query.OrderBy(s => s.Order.Status),
            "status_desc" => query.OrderByDescending(s => s.Order.Status),
            "date_asc" => query.OrderBy(s => s.Order.OrderDate),
            _ => query.OrderByDescending(s => s.ShippingId)
        };

        var totalItems = await query.CountAsync(cancellationToken);
        var totalPages = (int)Math.Ceiling(totalItems / (double)pageSize);
        if (totalPages > 0 && page > totalPages)
        {
            page = totalPages;
        }

        var rowsRaw = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(s => new
            {
                shippingID = s.ShippingId,
                orderID = s.OrderId,
                shippingType = s.ShippingType,
                fullName = s.FullName,
                phone = s.Phone,
                email = s.Email,
                addressDetail = s.AddressDetail,
                provinceId = s.ProvinceId,
                communeId = s.CommuneId,
                ghnOrderCode = s.GhnOrderCode,
                ghnClientOrderCode = s.GhnClientOrderCode,
                ghnStatus = s.GhnStatus,
                ghnStatusLabel = s.GhnStatusLabel,
                ghnTotalFee = s.GhnTotalFee,
                ghnCreatedAt = s.GhnCreatedAt,
                ghnExpectedDeliveryTime = s.GhnExpectedDeliveryTime,
                ghnLastSyncedAt = s.GhnLastSyncedAt,
                isStorePickup = s.ShippingType == "StorePickup",
                storeAddress = s.ShippingType == "StorePickup" ? s.AddressDetail : null,
                order = new
                {
                    orderID = s.Order.OrderId,
                    status = s.Order.Status,
                    payments = s.Order.Payments.Select(p => new
                    {
                        paymentID = p.PaymentId,
                        paymentMethod = p.PaymentMethod,
                        paymentStatus = p.PaymentStatus,
                        paymentDate = p.PaymentDate
                    })
                },
                deliveryAssignments = Array.Empty<object>()
            })
            .ToListAsync(cancellationToken);

        var orderIds = rowsRaw.Select(x => x.orderID).Distinct().ToList();
        var scopedSellerOrders = !isAdmin && sellerId.HasValue && orderIds.Count > 0
            ? await _db.SellerOrders
                .AsNoTracking()
                .Where(so => so.SellerId == sellerId.Value && orderIds.Contains(so.OrderId))
                .Select(so => new ShippingScopeRow(
                    so.OrderId,
                    so.ShippingFee,
                    so.SellerOrderItems.Sum(soi => (decimal?)(soi.FinalAmount ?? (soi.UnitPrice * soi.Quantity) - soi.DiscountAmount)) ?? 0m,
                    so.Shipments
                        .OrderByDescending(sh => sh.ShipmentId)
                        .Select(sh => sh.WeightKg)
                        .FirstOrDefault(),
                    so.Shipments
                        .OrderByDescending(sh => sh.ShipmentId)
                        .Select(sh => sh.LengthCm)
                        .FirstOrDefault(),
                    so.Shipments
                        .OrderByDescending(sh => sh.ShipmentId)
                        .Select(sh => sh.WidthCm)
                        .FirstOrDefault(),
                    so.Shipments
                        .OrderByDescending(sh => sh.ShipmentId)
                        .Select(sh => sh.HeightCm)
                        .FirstOrDefault()))
                .ToListAsync(cancellationToken)
            : new List<ShippingScopeRow>();

        var scopedSellerLookup = scopedSellerOrders
            .GroupBy(x => (int)x.OrderId)
            .ToDictionary(
                g => g.Key,
                g => new
                {
                    ShippingFee = g.Sum(x => (decimal)x.ShippingFee),
                    ItemsAmount = g.Sum(x => (decimal)x.ItemsAmount),
                    PackageWeight = g.Select(x => x.PackageWeightKg).FirstOrDefault(x => x.HasValue && x.Value > 0m),
                    PackageLength = g.Select(x => x.PackageLengthCm).FirstOrDefault(x => x.HasValue && x.Value > 0m),
                    PackageWidth = g.Select(x => x.PackageWidthCm).FirstOrDefault(x => x.HasValue && x.Value > 0m),
                    PackageHeight = g.Select(x => x.PackageHeightCm).FirstOrDefault(x => x.HasValue && x.Value > 0m)
                });

        var scopedSellerItemRows = !isAdmin && sellerId.HasValue && orderIds.Count > 0
            ? await _db.SellerOrderItems
                .AsNoTracking()
                .Where(soi => soi.SellerOrder.SellerId == sellerId.Value && orderIds.Contains(soi.SellerOrder.OrderId))
                .Select(soi => new ShippingItemScopeRow(
                    soi.SellerOrder.OrderId,
                    soi.Quantity,
                    soi.SnapshotName))
                .ToListAsync(cancellationToken)
            : new List<ShippingItemScopeRow>();

        var scopedSellerItemLookup = scopedSellerItemRows
            .GroupBy(x => (int)x.OrderId)
            .ToDictionary(
                g => g.Key,
                g => new
                {
                    TotalQuantity = g.Sum(x => (int)x.Quantity),
                    ItemSummary = BuildItemSummary(g.Select(x => (string?)x.SnapshotName).ToList())
                });

        var rows = rowsRaw.Select(s =>
        {
            var province = s.provinceId.HasValue
                ? Provinces.FirstOrDefault(p => p.Id == s.provinceId.Value)
                : null;

            var commune = s.communeId.HasValue &&
                          s.provinceId.HasValue &&
                          CommunesByProvince.TryGetValue(s.provinceId.Value, out var provinceCommunes)
                ? provinceCommunes.FirstOrDefault(c => c.Id == s.communeId.Value)
                : null;

            var scopedSellerData = scopedSellerLookup.TryGetValue(s.orderID, out var sellerScope)
                ? sellerScope
                : null;
            var scopedSellerItems = scopedSellerItemLookup.TryGetValue(s.orderID, out var sellerItems)
                ? sellerItems
                : null;
            var shippingFee = scopedSellerData?.ShippingFee ?? 0m;
            var itemsAmount = scopedSellerData?.ItemsAmount ?? 0m;
            var totalAmount = itemsAmount + shippingFee;

            return new
            {
                s.shippingID,
                s.orderID,
                s.shippingType,
                s.fullName,
                s.phone,
                s.email,
                s.addressDetail,
                s.provinceId,
                s.communeId,
                s.ghnOrderCode,
                s.ghnClientOrderCode,
                s.ghnStatus,
                s.ghnStatusLabel,
                s.ghnTotalFee,
                s.ghnCreatedAt,
                s.ghnExpectedDeliveryTime,
                s.ghnLastSyncedAt,
                s.isStorePickup,
                s.storeAddress,
                province = province is null
                    ? null
                    : new { provinceId = province.Id, provinceName = province.Name },
                commune = commune is null
                    ? null
                    : new { communeId = commune.Id, communeName = commune.Name },
                order = new
                {
                    s.order.orderID,
                    s.order.status,
                    s.order.payments,
                    shippingFee = isAdmin ? (decimal?)null : shippingFee,
                    itemsAmount = isAdmin ? (decimal?)null : itemsAmount,
                    totalAmount = isAdmin ? (decimal?)null : totalAmount,
                    totalQuantity = isAdmin ? (int?)null : scopedSellerItems?.TotalQuantity,
                    itemSummary = isAdmin ? null : scopedSellerItems?.ItemSummary,
                    packageWeight = isAdmin || scopedSellerData?.PackageWeight is not > 0m
                        ? null
                        : (int?)decimal.ToInt32(decimal.Round(scopedSellerData.PackageWeight.Value * 1000m, 0, MidpointRounding.AwayFromZero)),
                    packageLength = isAdmin || scopedSellerData?.PackageLength is not > 0m
                        ? null
                        : (int?)decimal.ToInt32(decimal.Round(scopedSellerData.PackageLength.Value, 0, MidpointRounding.AwayFromZero)),
                    packageWidth = isAdmin || scopedSellerData?.PackageWidth is not > 0m
                        ? null
                        : (int?)decimal.ToInt32(decimal.Round(scopedSellerData.PackageWidth.Value, 0, MidpointRounding.AwayFromZero)),
                    packageHeight = isAdmin || scopedSellerData?.PackageHeight is not > 0m
                        ? null
                        : (int?)decimal.ToInt32(decimal.Round(scopedSellerData.PackageHeight.Value, 0, MidpointRounding.AwayFromZero))
                },
                s.deliveryAssignments
            };
        }).ToList();

        var sellerOrderIds = await ApplySellerScopeToOrders(_db.Orders.AsNoTracking(), sellerId, isAdmin)
            .Select(o => o.OrderId)
            .ToListAsync(cancellationToken);

        var orderIdsWithShipping = await _db.Shippings
            .AsNoTracking()
            .Where(s => sellerOrderIds.Contains(s.OrderId))
            .Select(s => s.OrderId)
            .Distinct()
            .ToListAsync(cancellationToken);

        var ordersWithoutShipping = await ApplySellerScopeToOrders(_db.Orders.AsNoTracking(), sellerId, isAdmin)
            .Where(o => !orderIdsWithShipping.Contains(o.OrderId))
            .OrderByDescending(o => o.OrderDate)
            .Select(o => new
            {
                orderID = o.OrderId,
                displayText = "Don hang #DH" + o.OrderId.ToString().PadLeft(5, '0')
            })
            .Take(500)
            .ToListAsync(cancellationToken);

        return Ok(new
        {
            success = true,
            data = new
            {
                items = rows,
                totalItems,
                totalPages,
                currentPage = page,
                pageSize,
                currentStatus = status,
                currentStaffId = staffId,
                currentQuery = q,
                currentSort = sort,
                deliveredState,
                provinces = Provinces.Select(p => new { provinceId = p.Id, provinceName = p.Name }),
                deliveryStaffs = Array.Empty<object>(),
                orders = ordersWithoutShipping
            }
        });
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById([FromRoute] int id, CancellationToken cancellationToken)
    {
        var isAdmin = IsAdminUser();
        var sellerId = isAdmin ? (int?)null : TryGetSellerIdFromToken();
        if (!isAdmin && !sellerId.HasValue)
        {
            return Unauthorized(new { success = false, message = "Khong xac dinh duoc seller." });
        }

        var shipping = await _db.Shippings
            .AsNoTracking()
            .Include(s => s.Order)
            .ThenInclude(o => o.SellerOrders)
            .FirstOrDefaultAsync(s => s.ShippingId == id, cancellationToken);

        if (shipping is null || !CanAccessShippingBySeller(shipping, sellerId, isAdmin))
        {
            return NotFound(new { success = false, message = "Khong tim thay thong tin van chuyen!" });
        }

        return Ok(new
        {
            success = true,
            shippingID = shipping.ShippingId,
            orderID = shipping.OrderId,
            shippingType = shipping.ShippingType,
            fullName = shipping.FullName,
            phone = shipping.Phone,
            email = shipping.Email,
            addressDetail = shipping.AddressDetail,
            provinceId = shipping.ProvinceId,
            communeId = shipping.CommuneId,
            ghnOrderCode = shipping.GhnOrderCode,
            ghnClientOrderCode = shipping.GhnClientOrderCode,
            ghnStatus = shipping.GhnStatus,
            ghnStatusLabel = shipping.GhnStatusLabel,
            ghnTotalFee = shipping.GhnTotalFee,
            ghnCreatedAt = shipping.GhnCreatedAt,
            ghnExpectedDeliveryTime = shipping.GhnExpectedDeliveryTime,
            ghnLastSyncedAt = shipping.GhnLastSyncedAt,
            isStorePickup = shipping.ShippingType == "StorePickup",
            storeAddress = shipping.ShippingType == "StorePickup" ? shipping.AddressDetail : null,
            deliveryStaffId = (int?)null
        });
    }

    [HttpGet("order-info/{orderId:int}")]
    public async Task<IActionResult> GetOrderInfo([FromRoute] int orderId, CancellationToken cancellationToken)
    {
        var isAdmin = IsAdminUser();
        var sellerId = isAdmin ? (int?)null : TryGetSellerIdFromToken();
        if (!isAdmin && !sellerId.HasValue)
        {
            return Unauthorized(new { success = false, message = "Khong xac dinh duoc seller." });
        }

        var order = await _db.Orders
            .AsNoTracking()
            .Include(o => o.SellerOrders)
                .ThenInclude(so => so.SellerOrderItems)
            .Include(o => o.SellerOrders)
                .ThenInclude(so => so.Shipments)
            .Include(o => o.OrderDetails)
            .Include(o => o.Payments)
            .Include(o => o.Shippings)
            .FirstOrDefaultAsync(o => o.OrderId == orderId, cancellationToken);

        if (order is null || !CanAccessOrderBySeller(order, sellerId, isAdmin))
        {
            return NotFound(new { success = false, message = "Khong tim thay don hang!" });
        }

        var shipping = order.Shippings.OrderByDescending(x => x.ShippingId).FirstOrDefault();
        var scopedSellerOrder = !isAdmin && sellerId.HasValue
            ? order.SellerOrders.FirstOrDefault(so => so.SellerId == sellerId.Value)
            : null;
        var itemsAmount = scopedSellerOrder?.SellerOrderItems.Sum(soi => soi.FinalAmount ?? (soi.UnitPrice * soi.Quantity) - soi.DiscountAmount)
            ?? Math.Max(0m, order.TotalAmount - order.ShippingFee);
        var totalQuantity = scopedSellerOrder?.SellerOrderItems.Sum(soi => soi.Quantity)
            ?? order.OrderDetails.Sum(od => od.Quantity);
        IReadOnlyCollection<string?> itemNames = scopedSellerOrder?.SellerOrderItems
            .Select(soi => (string?)soi.SnapshotName)
            .ToList()
            ?? order.OrderDetails
                .Select(_ => (string?)null)
                .ToList();
        var itemSummary = BuildItemSummary(itemNames);
        var shippingFee = scopedSellerOrder?.ShippingFee ?? order.ShippingFee;
        var payment = order.Payments.OrderByDescending(p => p.PaymentId).FirstOrDefault();
        var scopedShipment = scopedSellerOrder?.Shipments.OrderByDescending(s => s.ShipmentId).FirstOrDefault()
            ?? order.SellerOrders
                .SelectMany(so => so.Shipments)
                .OrderByDescending(s => s.ShipmentId)
                .FirstOrDefault();
        var packageWeight = scopedShipment?.WeightKg.HasValue == true && scopedShipment.WeightKg.Value > 0m
            ? (int?)decimal.ToInt32(decimal.Round(scopedShipment.WeightKg.Value * 1000m, 0, MidpointRounding.AwayFromZero))
            : null;
        var packageLength = scopedShipment?.LengthCm.HasValue == true && scopedShipment.LengthCm.Value > 0m
            ? (int?)decimal.ToInt32(decimal.Round(scopedShipment.LengthCm.Value, 0, MidpointRounding.AwayFromZero))
            : null;
        var packageWidth = scopedShipment?.WidthCm.HasValue == true && scopedShipment.WidthCm.Value > 0m
            ? (int?)decimal.ToInt32(decimal.Round(scopedShipment.WidthCm.Value, 0, MidpointRounding.AwayFromZero))
            : null;
        var packageHeight = scopedShipment?.HeightCm.HasValue == true && scopedShipment.HeightCm.Value > 0m
            ? (int?)decimal.ToInt32(decimal.Round(scopedShipment.HeightCm.Value, 0, MidpointRounding.AwayFromZero))
            : null;

        return Ok(new
        {
            success = true,
            fullName = order.BuyerFullName,
            phone = order.BuyerPhone,
            email = order.BuyerEmail,
            address = shipping?.AddressDetail,
            provinceId = shipping?.ProvinceId,
            communeId = shipping?.CommuneId,
            shippingFee,
            itemsAmount,
            totalAmount = itemsAmount + shippingFee,
            totalQuantity,
            itemSummary,
            packageWeight,
            packageLength,
            packageWidth,
            packageHeight,
            ghnOrderCode = shipping?.GhnOrderCode,
            ghnClientOrderCode = shipping?.GhnClientOrderCode,
            ghnStatus = shipping?.GhnStatus,
            ghnStatusLabel = shipping?.GhnStatusLabel,
            ghnTotalFee = shipping?.GhnTotalFee,
            ghnCreatedAt = shipping?.GhnCreatedAt,
            ghnExpectedDeliveryTime = shipping?.GhnExpectedDeliveryTime,
            ghnLastSyncedAt = shipping?.GhnLastSyncedAt,
            paymentMethod = payment?.PaymentMethod,
            isCod = string.Equals(payment?.PaymentMethod, "COD", StringComparison.OrdinalIgnoreCase),
            codAmount = string.Equals(payment?.PaymentMethod, "COD", StringComparison.OrdinalIgnoreCase)
                ? itemsAmount + shippingFee
                : 0m
        });
    }

    [HttpGet("communes")]
    public IActionResult GetCommunes([FromQuery] int provinceId)
    {
        if (!CommunesByProvince.TryGetValue(provinceId, out var communes))
        {
            communes = new List<CommuneOption>();
        }

        return Ok(communes.Select(c => new { value = c.Id, text = c.Name }));
    }

    [HttpPost("{orderId:int}/ghn-metadata")]
    public async Task<IActionResult> UpsertGhnMetadata([FromRoute] int orderId, [FromBody] GhnMetadataUpsertRequest request, CancellationToken cancellationToken)
    {
        var isAdmin = IsAdminUser();
        var sellerId = isAdmin ? (int?)null : TryGetSellerIdFromToken();
        if (!isAdmin && !sellerId.HasValue)
        {
            return Unauthorized(new { success = false, message = "Khong xac dinh duoc seller." });
        }

        return await UpsertGhnMetadataCore(orderId, request, sellerId, isAdmin, bypassOwnershipCheck: false, cancellationToken);
    }

    [AllowAnonymous]
    [HttpGet("internal/ghn-sync-candidates")]
    public async Task<IActionResult> GetInternalGhnSyncCandidates(
        [FromQuery] int limit = 10,
        [FromQuery] int staleMinutes = 20,
        CancellationToken cancellationToken = default)
    {
        if (!IsValidInternalServiceRequest())
        {
            _logger.LogWarning("Ghn internal sync candidates bi tu choi do internal service key khong hop le.");
            return Unauthorized(new { success = false, message = "Yeu cau noi bo khong hop le." });
        }

        limit = Math.Clamp(limit, 1, 50);
        staleMinutes = Math.Clamp(staleMinutes, 1, 24 * 60);
        var cutoffUtc = DateTime.UtcNow.AddMinutes(-staleMinutes);

        var items = await _db.Shippings
            .AsNoTracking()
            .Include(s => s.Order)
            .Where(s => s.OrderId > 0 && !string.IsNullOrWhiteSpace(s.GhnOrderCode))
            .Where(s => s.GhnLastSyncedAt == null || s.GhnLastSyncedAt <= cutoffUtc)
            .Where(s => s.Order != null && s.Order.Status != "Canceled")
            .Where(s => string.IsNullOrWhiteSpace(s.GhnStatus) || !TerminalGhnStatuses.Contains(s.GhnStatus))
            .OrderBy(s => s.GhnLastSyncedAt ?? SqlDateTimeFloor)
            .ThenBy(s => s.OrderId)
            .Take(limit)
            .Select(s => new InternalGhnSyncCandidate
            {
                OrderId = s.OrderId,
                OrderCode = s.GhnOrderCode ?? string.Empty,
                ClientOrderCode = s.GhnClientOrderCode,
                GhnStatus = s.GhnStatus,
                LastSyncedAt = s.GhnLastSyncedAt
            })
            .ToListAsync(cancellationToken);

        return Ok(new
        {
            success = true,
            count = items.Count,
            staleMinutes,
            items
        });
    }

    [AllowAnonymous]
    [HttpPost("internal/{orderId:int}/ghn-metadata")]
    public async Task<IActionResult> UpsertGhnMetadataInternal(
        [FromRoute] int orderId,
        [FromBody] GhnMetadataUpsertRequest request,
        CancellationToken cancellationToken)
    {
        if (!IsValidInternalServiceRequest())
        {
            _logger.LogWarning("Ghn internal metadata upsert bi tu choi do internal service key khong hop le. OrderId={OrderId}", orderId);
            return Unauthorized(new { success = false, message = "Yeu cau noi bo khong hop le." });
        }

        return await UpsertGhnMetadataCore(orderId, request, sellerId: null, isAdmin: true, bypassOwnershipCheck: true, cancellationToken);
    }

    [AllowAnonymous]
    [HttpPost("internal/ghn-metadata/by-code")]
    public async Task<IActionResult> UpsertGhnMetadataInternalByCode(
        [FromBody] GhnMetadataByCodeUpsertRequest request,
        CancellationToken cancellationToken)
    {
        if (!IsValidInternalServiceRequest())
        {
            _logger.LogWarning(
                "Ghn internal metadata by-code upsert bi tu choi do internal service key khong hop le. OrderCode={OrderCode}, ClientOrderCode={ClientOrderCode}",
                request.OrderCode,
                request.ClientOrderCode);
            return Unauthorized(new { success = false, message = "Yeu cau noi bo khong hop le." });
        }

        var normalizedOrderCode = NormalizeNullableText(request.OrderCode, 50);
        var normalizedClientOrderCode = NormalizeNullableText(request.ClientOrderCode, 50);
        if (string.IsNullOrWhiteSpace(normalizedOrderCode) && string.IsNullOrWhiteSpace(normalizedClientOrderCode))
        {
            return BadRequest(new { success = false, message = "Thieu order code hoac client order code de luu metadata GHN." });
        }

        var shipping = await _db.Shippings
            .Include(s => s.Order)
            .ThenInclude(o => o.SellerOrders)
            .FirstOrDefaultAsync(
                s => (!string.IsNullOrWhiteSpace(normalizedOrderCode) && s.GhnOrderCode == normalizedOrderCode) ||
                     (!string.IsNullOrWhiteSpace(normalizedClientOrderCode) && s.GhnClientOrderCode == normalizedClientOrderCode),
                cancellationToken);

        if (shipping is null)
        {
            return NotFound(new { success = false, message = "Khong tim thay shipping de luu metadata GHN theo order code." });
        }

        var previousGhnStatus = shipping.GhnStatus;
        var previousGhnStatusLabel = shipping.GhnStatusLabel;
        ApplyGhnMetadata(shipping, new GhnMetadataUpsertRequest
        {
            OrderCode = normalizedOrderCode,
            ClientOrderCode = normalizedClientOrderCode,
            Status = request.Status,
            StatusLabel = request.StatusLabel,
            TotalFee = request.TotalFee,
            CreatedAt = request.CreatedAt,
            ExpectedDeliveryTime = request.ExpectedDeliveryTime,
            LastSyncedAt = request.LastSyncedAt
        });

        await _db.SaveChangesAsync(cancellationToken);
        await _customerNotificationService.PublishShippingStatusUpdateAsync(
            shipping.Order,
            shipping,
            previousGhnStatus,
            previousGhnStatusLabel,
            cancellationToken);

        return Ok(new
        {
            success = true,
            message = "Da luu metadata GHN theo order code.",
            orderId = shipping.OrderId,
            shippingId = shipping.ShippingId
        });
    }

    private async Task<IActionResult> UpsertGhnMetadataCore(
        int orderId,
        GhnMetadataUpsertRequest request,
        int? sellerId,
        bool isAdmin,
        bool bypassOwnershipCheck,
        CancellationToken cancellationToken)
    {
        if (orderId <= 0)
        {
            return BadRequest(new { success = false, message = "Thieu OrderId hop le de luu metadata GHN." });
        }

        var shipping = await _db.Shippings
            .Include(s => s.Order)
            .ThenInclude(o => o.SellerOrders)
            .FirstOrDefaultAsync(s => s.OrderId == orderId, cancellationToken);
        if (shipping is null || (!bypassOwnershipCheck && !CanAccessShippingBySeller(shipping, sellerId, isAdmin)))
        {
            return NotFound(new { success = false, message = "Khong tim thay shipping de luu metadata GHN." });
        }

        var previousGhnStatus = shipping.GhnStatus;
        var previousGhnStatusLabel = shipping.GhnStatusLabel;
        ApplyGhnMetadata(shipping, request);
        await _db.SaveChangesAsync(cancellationToken);
        await _customerNotificationService.PublishShippingStatusUpdateAsync(
            shipping.Order,
            shipping,
            previousGhnStatus,
            previousGhnStatusLabel,
            cancellationToken);

        return Ok(new
        {
            success = true,
            message = "Da luu metadata GHN cho shipping.",
            orderId,
            shippingId = shipping.ShippingId
        });
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] ShippingUpsertRequest request, CancellationToken cancellationToken)
    {
        var isAdmin = IsAdminUser();
        var sellerId = isAdmin ? (int?)null : TryGetSellerIdFromToken();
        if (!isAdmin && !sellerId.HasValue)
        {
            return Unauthorized(new { success = false, message = "Khong xac dinh duoc seller." });
        }

        if (request.OrderID <= 0)
        {
            return BadRequest(new { success = false, message = "Vui long chon don hang!" });
        }

        if (string.IsNullOrWhiteSpace(request.FullName))
        {
            return BadRequest(new { success = false, message = "Vui long nhap ten nguoi nhan!" });
        }

        if (string.IsNullOrWhiteSpace(request.Phone))
        {
            return BadRequest(new { success = false, message = "Vui long nhap so dien thoai!" });
        }

        var duplicate = await _db.Shippings.AnyAsync(s => s.OrderId == request.OrderID, cancellationToken);
        if (duplicate)
        {
            return BadRequest(new { success = false, message = "Don hang nay da co thong tin van chuyen!" });
        }

        var order = await _db.Orders
            .Include(o => o.SellerOrders)
            .FirstOrDefaultAsync(o => o.OrderId == request.OrderID, cancellationToken);
        if (order is null || !CanAccessOrderBySeller(order, sellerId, isAdmin))
        {
            return BadRequest(new { success = false, message = "Don hang khong ton tai!" });
        }

        var (addressDetail, provinceId, communeId, shippingType) = NormalizeAddress(request);

        var shipping = new Shipping
        {
            OrderId = request.OrderID,
            ShippingType = shippingType,
            FullName = request.FullName.Trim(),
            Phone = request.Phone.Trim(),
            Email = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim(),
            AddressDetail = addressDetail,
            ProvinceId = provinceId,
            CommuneId = communeId
        };

        _db.Shippings.Add(shipping);
        await _db.SaveChangesAsync(cancellationToken);

        return Ok(new { success = true, message = "Them thong tin van chuyen thanh cong!" });
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update([FromRoute] int id, [FromBody] ShippingUpsertRequest request, CancellationToken cancellationToken)
    {
        var isAdmin = IsAdminUser();
        var sellerId = isAdmin ? (int?)null : TryGetSellerIdFromToken();
        if (!isAdmin && !sellerId.HasValue)
        {
            return Unauthorized(new { success = false, message = "Khong xac dinh duoc seller." });
        }

        var shipping = await _db.Shippings
            .Include(s => s.Order)
            .ThenInclude(o => o.SellerOrders)
            .FirstOrDefaultAsync(s => s.ShippingId == id, cancellationToken);
        if (shipping is null || !CanAccessShippingBySeller(shipping, sellerId, isAdmin))
        {
            return NotFound(new { success = false, message = "Khong tim thay thong tin van chuyen!" });
        }

        if (string.IsNullOrWhiteSpace(request.FullName))
        {
            return BadRequest(new { success = false, message = "Vui long nhap ten nguoi nhan!" });
        }

        if (string.IsNullOrWhiteSpace(request.Phone))
        {
            return BadRequest(new { success = false, message = "Vui long nhap so dien thoai!" });
        }

        var (addressDetail, provinceId, communeId, shippingType) = NormalizeAddress(request);

        shipping.ShippingType = shippingType;
        shipping.FullName = request.FullName.Trim();
        shipping.Phone = request.Phone.Trim();
        shipping.Email = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim();
        shipping.AddressDetail = addressDetail;
        shipping.ProvinceId = provinceId;
        shipping.CommuneId = communeId;

        await _db.SaveChangesAsync(cancellationToken);

        return Ok(new { success = true, message = "Cap nhat thong tin van chuyen thanh cong!" });
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete([FromRoute] int id, CancellationToken cancellationToken)
    {
        var isAdmin = IsAdminUser();
        var sellerId = isAdmin ? (int?)null : TryGetSellerIdFromToken();
        if (!isAdmin && !sellerId.HasValue)
        {
            return Unauthorized(new { success = false, message = "Khong xac dinh duoc seller." });
        }

        var shipping = await _db.Shippings
            .Include(s => s.Order)
            .ThenInclude(o => o.SellerOrders)
            .FirstOrDefaultAsync(s => s.ShippingId == id, cancellationToken);
        if (shipping is null || !CanAccessShippingBySeller(shipping, sellerId, isAdmin))
        {
            return NotFound(new { success = false, message = "Khong tim thay thong tin van chuyen!" });
        }

        _db.Shippings.Remove(shipping);
        await _db.SaveChangesAsync(cancellationToken);

        return Ok(new { success = true, message = "Xoa thong tin van chuyen thanh cong!" });
    }

    [HttpPost("reconcile-cod")]
    public async Task<IActionResult> ReconcileCod([FromBody] ReconcileCodRequest request, CancellationToken cancellationToken)
    {
        var isAdmin = IsAdminUser();
        var sellerId = isAdmin ? (int?)null : TryGetSellerIdFromToken();
        if (!isAdmin && !sellerId.HasValue)
        {
            return Unauthorized(new { success = false, message = "Khong xac dinh duoc seller." });
        }

        var order = await _db.Orders
            .Include(o => o.Payments)
            .Include(o => o.SellerOrders)
            .FirstOrDefaultAsync(o => o.OrderId == request.OrderId, cancellationToken);

        if (order is null || !CanAccessOrderBySeller(order, sellerId, isAdmin))
        {
            return NotFound(new { success = false, message = "Khong tim thay don hang!" });
        }

        var cod = order.Payments.FirstOrDefault(p => p.PaymentMethod == "COD");
        if (cod is null)
        {
            return BadRequest(new { success = false, message = "Don nay chua co Payment COD de doi soat!" });
        }

        if (!cod.PaymentDate.HasValue)
        {
            cod.PaymentDate = DateTime.UtcNow;
        }

        if (string.IsNullOrWhiteSpace(cod.PaymentStatus) || !PaidStatuses.Contains(cod.PaymentStatus))
        {
            cod.PaymentStatus = "Da thanh toan";
        }

        await _db.SaveChangesAsync(cancellationToken);

        return Ok(new { success = true, message = "Da danh dau DA DOI SOAT (COD)." });
    }

    [HttpPost("unreconcile-cod")]
    public async Task<IActionResult> UnreconcileCod([FromBody] ReconcileCodRequest request, CancellationToken cancellationToken)
    {
        var isAdmin = IsAdminUser();
        var sellerId = isAdmin ? (int?)null : TryGetSellerIdFromToken();
        if (!isAdmin && !sellerId.HasValue)
        {
            return Unauthorized(new { success = false, message = "Khong xac dinh duoc seller." });
        }

        var order = await _db.Orders
            .Include(o => o.Payments)
            .Include(o => o.SellerOrders)
            .FirstOrDefaultAsync(o => o.OrderId == request.OrderId, cancellationToken);

        if (order is null || !CanAccessOrderBySeller(order, sellerId, isAdmin))
        {
            return NotFound(new { success = false, message = "Khong tim thay don hang!" });
        }

        var cod = order.Payments.FirstOrDefault(p => p.PaymentMethod == "COD");
        if (cod is null)
        {
            return BadRequest(new { success = false, message = "Don nay chua co Payment COD!" });
        }

        cod.PaymentDate = null;
        cod.PaymentStatus = "Chua thanh toan";

        await _db.SaveChangesAsync(cancellationToken);

        return Ok(new { success = true, message = "Da BO doi soat (COD)." });
    }

    private int? TryGetSellerIdFromToken()
    {
        var sub = User.FindFirstValue(JwtRegisteredClaimNames.Sub)
                  ?? User.FindFirstValue(ClaimTypes.NameIdentifier)
                  ?? User.FindFirstValue("sub");

        return int.TryParse(sub, out var sellerId) ? sellerId : null;
    }

    private bool IsAdminUser()
    {
        return User.IsInRole("Admin");
    }

    private IQueryable<Order> ApplySellerScopeToOrders(IQueryable<Order> query, int? sellerId = null, bool isAdmin = false)
    {
        if (isAdmin)
        {
            return query;
        }

        var resolvedSellerId = sellerId ?? TryGetSellerIdFromToken();
        if (!resolvedSellerId.HasValue)
        {
            return query.Where(_ => false);
        }

        return query.Where(o => o.SellerOrders.Any(so =>
            so.SellerId == resolvedSellerId.Value &&
            so.SellerOrderItems.Any()));
    }

    private bool CanAccessOrderBySeller(Order order, int? sellerId, bool isAdmin = false)
    {
        if (isAdmin)
        {
            return true;
        }

        if (!sellerId.HasValue)
        {
            return false;
        }

        return order.SellerOrders.Any(so =>
            so.SellerId == sellerId.Value &&
            so.SellerOrderItems.Any());
    }

    private bool CanAccessShippingBySeller(Shipping shipping, int? sellerId, bool isAdmin = false)
    {
        return shipping.Order is not null && CanAccessOrderBySeller(shipping.Order, sellerId, isAdmin);
    }

    private bool IsValidInternalServiceRequest()
    {
        var configuredKey = _internalServiceAuthOptions.InternalServiceKey?.Trim();
        var incomingKey = Request.Headers["X-Internal-Service-Key"].ToString().Trim();

        if (string.IsNullOrWhiteSpace(configuredKey) || string.IsNullOrWhiteSpace(incomingKey))
        {
            return false;
        }

        var configuredBytes = Encoding.UTF8.GetBytes(configuredKey);
        var incomingBytes = Encoding.UTF8.GetBytes(incomingKey);
        return CryptographicOperations.FixedTimeEquals(configuredBytes, incomingBytes);
    }

    private static string BuildItemSummary(IReadOnlyCollection<string?> names)
    {
        var cleaned = names
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(3)
            .ToList();

        if (cleaned.Count == 0)
        {
            return "Goi hang tong hop";
        }

        return cleaned.Count == 1
            ? cleaned[0]
            : $"{cleaned[0]} va {Math.Max(1, names.Count - 1)} san pham khac";
    }

    private static (string addressDetail, int? provinceId, int? communeId, string shippingType) NormalizeAddress(ShippingUpsertRequest request)
    {
        if (request.IsStorePickup)
        {
            var storeAddress = string.IsNullOrWhiteSpace(request.StoreAddress)
                ? "Nhan tai cua hang"
                : request.StoreAddress.Trim();

            return (storeAddress, null, null, "StorePickup");
        }

        var addressDetail = string.IsNullOrWhiteSpace(request.AddressDetail)
            ? ""
            : request.AddressDetail.Trim();

        var shippingType = string.IsNullOrWhiteSpace(request.ShippingType)
            ? "Giao noi bo"
            : request.ShippingType.Trim();

        return (addressDetail, request.ProvinceId, request.CommuneId, shippingType);
    }

    private static void ApplyGhnMetadata(Shipping shipping, GhnMetadataUpsertRequest request)
    {
        shipping.GhnOrderCode = NormalizeNullableText(request.OrderCode, 50);
        shipping.GhnClientOrderCode = NormalizeNullableText(request.ClientOrderCode, 50);
        shipping.GhnStatus = NormalizeNullableText(request.Status, 50);
        shipping.GhnStatusLabel = NormalizeNullableText(request.StatusLabel, 100);
        shipping.GhnTotalFee = request.TotalFee;
        shipping.GhnCreatedAt = request.CreatedAt;
        shipping.GhnExpectedDeliveryTime = request.ExpectedDeliveryTime;
        shipping.GhnLastSyncedAt = request.LastSyncedAt ?? DateTime.UtcNow;
    }

    private static string? NormalizeNullableText(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength
            ? trimmed
            : trimmed[..maxLength];
    }

    public sealed class ShippingUpsertRequest
    {
        public int OrderID { get; set; }

        public string? ShippingType { get; set; }

        public string FullName { get; set; } = string.Empty;

        public string Phone { get; set; } = string.Empty;

        public string? Email { get; set; }

        public string? AddressDetail { get; set; }

        public int? ProvinceId { get; set; }

        public int? CommuneId { get; set; }

        public bool IsStorePickup { get; set; }

        public string? StoreAddress { get; set; }

        public int? DeliveryStaffId { get; set; }
    }

    public sealed class ReconcileCodRequest
    {
        public int OrderId { get; set; }
    }

    public sealed class GhnMetadataUpsertRequest
    {
        public string? OrderCode { get; set; }

        public string? ClientOrderCode { get; set; }

        public string? Status { get; set; }

        public string? StatusLabel { get; set; }

        public decimal? TotalFee { get; set; }

        public DateTime? CreatedAt { get; set; }

        public DateTime? ExpectedDeliveryTime { get; set; }

        public DateTime? LastSyncedAt { get; set; }
    }

    private sealed record ShippingScopeRow(
        int OrderId,
        decimal ShippingFee,
        decimal ItemsAmount,
        decimal? PackageWeightKg,
        decimal? PackageLengthCm,
        decimal? PackageWidthCm,
        decimal? PackageHeightCm);

    private sealed record ShippingItemScopeRow(int OrderId, int Quantity, string? SnapshotName);

    private sealed record ProvinceOption(int Id, string Name);

    private sealed record CommuneOption(int Id, string Name);

    public sealed class InternalGhnSyncCandidate
    {
        public int OrderId { get; set; }

        public string OrderCode { get; set; } = string.Empty;

        public string? ClientOrderCode { get; set; }

        public string? GhnStatus { get; set; }

        public DateTime? LastSyncedAt { get; set; }
    }

    public sealed class GhnMetadataByCodeUpsertRequest
    {
        public string? OrderCode { get; set; }

        public string? ClientOrderCode { get; set; }

        public string? Status { get; set; }

        public string? StatusLabel { get; set; }

        public decimal? TotalFee { get; set; }

        public DateTime? CreatedAt { get; set; }

        public DateTime? ExpectedDeliveryTime { get; set; }

        public DateTime? LastSyncedAt { get; set; }
    }
}
