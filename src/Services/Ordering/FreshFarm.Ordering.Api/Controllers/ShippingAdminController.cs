using FreshFarm.Ordering.Api.Models;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FreshFarm.Ordering.Api.Controllers;

[ApiController]
[Route("api/orders/admin/shippings")]
[Authorize(Policy = "SellerOrAdmin")]
public sealed class ShippingAdminController : ControllerBase
{
    private static readonly string[] PaidStatuses = { "Da thanh toan", "Đã thanh toán", "Hoan tat", "Hoàn tất" };

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

    public ShippingAdminController(FreshFarmOrderingDBContext db)
    {
        _db = db;
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
                s.isStorePickup,
                s.storeAddress,
                province = province is null
                    ? null
                    : new { provinceId = province.Id, provinceName = province.Name },
                commune = commune is null
                    ? null
                    : new { communeId = commune.Id, communeName = commune.Name },
                s.order,
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
            .FirstOrDefaultAsync(o => o.OrderId == orderId, cancellationToken);

        if (order is null || !CanAccessOrderBySeller(order, sellerId, isAdmin))
        {
            return NotFound(new { success = false, message = "Khong tim thay don hang!" });
        }

        return Ok(new
        {
            success = true,
            fullName = order.BuyerFullName,
            phone = order.BuyerPhone,
            email = order.BuyerEmail,
            address = (string?)null,
            provinceId = (int?)null,
            communeId = (int?)null
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

    private sealed record ProvinceOption(int Id, string Name);

    private sealed record CommuneOption(int Id, string Name);
}
