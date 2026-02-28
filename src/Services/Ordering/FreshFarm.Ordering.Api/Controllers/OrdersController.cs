using FreshFarm.Ordering.Api.Dtos;
using FreshFarm.Ordering.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace FreshFarm.Ordering.Api.Controllers;

[ApiController]
[Route("api/orders")]
[Authorize]
public sealed class OrdersController : ControllerBase
{
    private static readonly Dictionary<string, string> CanonicalStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Pending"] = "Pending",
        ["Processing"] = "Processing",
        ["Ready"] = "Ready",
        ["Shipped"] = "Shipped",
        ["Delivered"] = "Delivered",
        ["Canceled"] = "Canceled"
    };

    private readonly FreshFarmOrderingDBContext _db;

    public OrdersController(FreshFarmOrderingDBContext db)
    {
        _db = db;
    }

    [HttpGet("my")]
    public async Task<IActionResult> GetMyOrders(CancellationToken cancellationToken)
    {
        var userId = TryGetUserIdFromToken();
        if (userId is null)
        {
            return Unauthorized("Token khong co claim user id hop le.");
        }

        var data = await _db.Orders
            .AsNoTracking()
            .Where(order => order.UserId == userId.Value)
            .OrderByDescending(order => order.OrderDate)
            .Select(order => new OrderListItemResponse
            {
                OrderId = order.OrderId,
                OrderDate = order.OrderDate,
                TotalAmount = order.TotalAmount,
                Status = order.Status,
                PaymentStatus = order.PaymentStatus
            })
            .ToListAsync(cancellationToken);

        return Ok(data);
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id, CancellationToken cancellationToken)
    {
        var userId = TryGetUserIdFromToken();
        if (userId is null)
        {
            return Unauthorized("Token khong co claim user id hop le.");
        }

        var result = await _db.Orders
            .AsNoTracking()
            .Where(o => o.OrderId == id)
            .Select(o => new
            {
                o.UserId,
                Data = new OrderDetailResponse
                {
                    OrderId = o.OrderId,
                    OrderDate = o.OrderDate,
                    ShippingFee = o.ShippingFee,
                    CouponId = o.CouponId,
                    TotalAmount = o.TotalAmount,
                    OrderNote = o.OrderNote,
                    Status = o.Status ?? string.Empty,
                    PaymentStatus = o.PaymentStatus,
                    PaidAt = o.PaidAt,
                    BuyerFullName = o.BuyerFullName,
                    BuyerPhone = o.BuyerPhone,
                    BuyerEmail = o.BuyerEmail,
                    PointsEarned = o.PointsEarned,
                    PointsRedeemed = o.PointsRedeemed,
                    Items = o.OrderDetails
                        .OrderBy(x => x.OrderDetailId)
                        .Select(x => new OrderDetailItemResponse
                        {
                            OrderDetailId = x.OrderDetailId,
                            ProductId = x.ProductId,
                            Quantity = x.Quantity,
                            UnitPrice = x.UnitPrice,
                            UnitSymbol = x.UnitSymbol
                        })
                        .ToList(),
                    Shippings = o.Shippings
                        .OrderBy(x => x.ShippingId)
                        .Select(x => new OrderDetailShippingResponse
                        {
                            ShippingId = x.ShippingId,
                            ShippingType = x.ShippingType,
                            FullName = x.FullName,
                            Phone = x.Phone,
                            Email = x.Email,
                            AddressDetail = x.AddressDetail,
                            ProvinceId = x.ProvinceId,
                            CommuneId = x.CommuneId
                        })
                        .ToList(),
                    Payments = o.Payments
                        .OrderBy(x => x.PaymentId)
                        .Select(x => new OrderDetailPaymentResponse
                        {
                            PaymentId = x.PaymentId,
                            PaymentMethod = x.PaymentMethod,
                            BankName = x.BankName,
                            AccountName = x.AccountName,
                            AccountNumber = x.AccountNumber,
                            TransactionCode = x.TransactionCode,
                            PaymentStatus = x.PaymentStatus,
                            PaymentDate = x.PaymentDate
                        })
                        .ToList()
                }
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (result is null)
        {
            return NotFound($"Khong tim thay order id = {id}.");
        }

        if (result.UserId != userId.Value)
        {
            return Forbid();
        }

        return Ok(result.Data);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateOrderRequest request, CancellationToken cancellationToken)
    {
        var userId = TryGetUserIdFromToken();
        if (userId is null)
        {
            return Unauthorized("Token khong co claim user id hop le.");
        }

        if (request.Items.Count == 0)
        {
            return BadRequest("Order phai co it nhat 1 item.");
        }

        var invalidItem = request.Items.Any(item =>
            item.Quantity <= 0 ||
            item.Quantity > 10_000 ||
            item.UnitPrice < 0 ||
            item.ProductId <= 0);
        if (invalidItem)
        {
            return BadRequest("Co item khong hop le (ProductId/Quantity/UnitPrice). Quantity phai trong khoang 1..10000.");
        }

        var itemsAmount = request.Items.Sum(item => item.UnitPrice * item.Quantity);
        var shippingFee = request.ShippingFee ?? 0m;
        var totalAmount = itemsAmount + shippingFee;

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var order = new Order
            {
                UserId = userId.Value,
                OrderDate = DateTime.UtcNow,
                ShippingFee = shippingFee,
                CouponId = null,
                TotalAmount = totalAmount,
                OrderNote = request.OrderNote,
                Status = "Pending",
                PaymentStatus = "Pending",
                PaidAt = null,
                StatusId = null,
                BuyerFullName = request.Shipping.FullName,
                BuyerPhone = request.Shipping.Phone,
                BuyerEmail = request.Shipping.Email,
                PointsEarned = 0,
                PointsRedeemed = 0
            };

            _db.Orders.Add(order);
            await _db.SaveChangesAsync(cancellationToken);

            var details = request.Items.Select(item => new OrderDetail
            {
                OrderId = order.OrderId,
                ProductId = item.ProductId,
                Quantity = item.Quantity,
                UnitPrice = item.UnitPrice,
                UnitSymbol = item.UnitSymbol
            }).ToList();

            _db.OrderDetails.AddRange(details);

            var shipping = new Shipping
            {
                OrderId = order.OrderId,
                ShippingType = request.Shipping.ShippingType,
                FullName = request.Shipping.FullName,
                Phone = request.Shipping.Phone,
                Email = request.Shipping.Email,
                AddressDetail = request.Shipping.AddressDetail,
                ProvinceId = request.Shipping.ProvinceId,
                CommuneId = request.Shipping.CommuneId
            };

            _db.Shippings.Add(shipping);

            if (request.Payment is not null)
            {
                var payment = new Payment
                {
                    OrderId = order.OrderId,
                    PaymentMethod = request.Payment.PaymentMethod,
                    PaymentStatus = "Pending",
                    PaymentDate = null,
                    UserId = userId.Value
                };

                _db.Payments.Add(payment);
            }

            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return CreatedAtAction(
                nameof(GetById),
                new { id = order.OrderId },
                new { orderId = order.OrderId, totalAmount });
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(cancellationToken);
            return StatusCode(500, new { message = "Tao order that bai.", detail = ex.Message });
        }
    }

    [Authorize(Policy = "SellerOnly")]
    [HttpGet("admin/paged")]
    public async Task<IActionResult> GetAdminOrdersPaged(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        [FromQuery] string? searchTerm = null,
        [FromQuery] string? statusFilter = null,
        [FromQuery] string? dateFilter = null,
        CancellationToken cancellationToken = default)
    {
        if (page < 1)
        {
            page = 1;
        }

        if (pageSize <= 0)
        {
            pageSize = 10;
        }

        var query = _db.Orders.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            var term = searchTerm.Trim().ToLower();
            query = query.Where(o =>
                o.OrderId.ToString().Contains(term) ||
                (o.BuyerFullName ?? string.Empty).ToLower().Contains(term) ||
                (o.BuyerEmail ?? string.Empty).ToLower().Contains(term) ||
                (o.BuyerPhone ?? string.Empty).Contains(term));
        }

        if (!string.IsNullOrWhiteSpace(statusFilter) &&
            !string.Equals(statusFilter, "All", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(o => o.Status == statusFilter);
        }

        if (!string.IsNullOrWhiteSpace(dateFilter) && DateTime.TryParse(dateFilter, out var filterDate))
        {
            var date = filterDate.Date;
            query = query.Where(o => o.OrderDate.Date == date);
        }

        var total = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(o => o.OrderDate)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(o => new
            {
                OrderID = o.OrderId,
                OrderCode = $"#{o.OrderId:D6}",
                CustomerName = string.IsNullOrWhiteSpace(o.BuyerFullName) ? $"U{o.UserId}" : o.BuyerFullName,
                OrderDate = o.OrderDate,
                TotalAmount = o.TotalAmount,
                Status = o.Status,
                StatusBadgeClass = GetStatusBadgeClass(o.Status),
                StatusText = GetStatusText(o.Status)
            })
            .ToListAsync(cancellationToken);

        return Ok(new
        {
            success = true,
            data = items,
            page,
            pageSize,
            total
        });
    }

    [Authorize(Policy = "SellerOnly")]
    [HttpGet("admin/{orderId:int}/detail")]
    public async Task<IActionResult> GetAdminOrderDetail([FromRoute] int orderId, CancellationToken cancellationToken)
    {
        var order = await _db.Orders
            .AsNoTracking()
            .Include(o => o.OrderDetails)
            .Include(o => o.Payments)
            .Include(o => o.Shippings)
            .FirstOrDefaultAsync(o => o.OrderId == orderId, cancellationToken);

        if (order is null)
        {
            return NotFound(new { message = "Không tìm thấy đơn hàng." });
        }

        var payment = order.Payments.OrderByDescending(p => p.PaymentId).FirstOrDefault();
        var shipping = order.Shippings.OrderByDescending(s => s.ShippingId).FirstOrDefault();

        var items = order.OrderDetails
            .OrderBy(od => od.OrderDetailId)
            .Select(od => new
            {
                productId = od.ProductId,
                productName = $"#P{od.ProductId}",
                quantity = od.Quantity,
                unitPrice = od.UnitPrice,
                unitSymbol = od.UnitSymbol ?? string.Empty,
                totalPrice = od.Quantity * od.UnitPrice
            })
            .ToList();

        return Ok(new
        {
            success = true,
            orderId = order.OrderId,
            orderCode = $"#{order.OrderId:D6}",
            userId = order.UserId,
            customerName = string.IsNullOrWhiteSpace(order.BuyerFullName) ? $"U{order.UserId}" : order.BuyerFullName,
            customerEmail = order.BuyerEmail,
            buyerFullName = order.BuyerFullName,
            buyerPhone = order.BuyerPhone,
            buyerEmail = order.BuyerEmail,
            phone = shipping?.Phone ?? order.BuyerPhone,
            address = shipping?.AddressDetail,
            orderDate = order.OrderDate,
            status = GetStatusText(order.Status),
            statusCode = order.Status,
            paymentMethod = payment?.PaymentMethod ?? "COD",
            paymentStatus = payment?.PaymentStatus ?? order.PaymentStatus,
            bankName = payment?.BankName,
            transactionCode = payment?.TransactionCode,
            subtotal = order.TotalAmount - order.ShippingFee,
            shippingFee = order.ShippingFee,
            total = order.TotalAmount,
            orderNote = order.OrderNote,
            items,
            canCancel = order.Status == "Pending" || order.Status == "Processing",
            canEdit = order.Status != "Delivered" && order.Status != "Canceled"
        });
    }

    [Authorize(Policy = "SellerOnly")]
    [HttpPost("admin/{orderId:int}/status")]
    public async Task<IActionResult> UpdateAdminOrderStatus([FromRoute] int orderId, [FromBody] UpdateAdminOrderStatusRequest request)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.NewStatus))
        {
            return BadRequest(new { message = "Thiếu trạng thái cần cập nhật." });
        }

        if (!TryNormalizeStatus(request.NewStatus, out var newStatus))
        {
            return BadRequest(new { message = "Trạng thái không hợp lệ." });
        }

        var order = await _db.Orders.FirstOrDefaultAsync(o => o.OrderId == orderId);
        if (order is null)
        {
            return NotFound(new { message = "Không tìm thấy đơn hàng." });
        }

        if (!CanChangeStatus(order.Status, newStatus))
        {
            return BadRequest(new
            {
                message = $"Không thể chuyển từ '{GetStatusText(order.Status)}' sang '{GetStatusText(newStatus)}'."
            });
        }

        var oldStatus = order.Status;
        order.Status = newStatus;

        try
        {
            var stRow = await _db.Statuses.FirstOrDefaultAsync(s => s.StatusName == newStatus);
            if (stRow is not null)
            {
                order.StatusId = stRow.StatusId;
            }
        }
        catch
        {
            // No-op for status lookup compatibility.
        }

        if (newStatus == "Delivered")
        {
            var payment = await _db.Payments.FirstOrDefaultAsync(p => p.OrderId == orderId);
            if (payment is not null)
            {
                var isCod = string.Equals(payment.PaymentMethod, "COD", StringComparison.OrdinalIgnoreCase);
                if (!isCod)
                {
                    payment.PaymentStatus = "Paid";
                    payment.PaymentDate = DateTime.UtcNow;
                    order.PaymentStatus = "Paid";
                    order.PaidAt = DateTime.UtcNow;
                }
            }
        }

        await _db.SaveChangesAsync();

        return Ok(new
        {
            success = true,
            message = $"Cập nhật trạng thái thành công: {GetStatusText(newStatus)}",
            status = newStatus,
            statusText = GetStatusText(newStatus),
            statusClass = GetStatusBadgeClass(newStatus),
            oldStatus
        });
    }

    [Authorize(Policy = "SellerOnly")]
    [HttpDelete("admin/{orderId:int}")]
    public async Task<IActionResult> DeleteAdminOrder([FromRoute] int orderId)
    {
        var order = await _db.Orders
            .Include(o => o.OrderDetails)
            .Include(o => o.Payments)
            .Include(o => o.Shippings)
            .Include(o => o.CouponUsageHistories)
            .Include(o => o.InventoryReservations)
            .Include(o => o.PaymentTransactions)
            .Include(o => o.ReconciliationLogs)
            .Include(o => o.SellerOrders)
            .FirstOrDefaultAsync(o => o.OrderId == orderId);

        if (order is null)
        {
            return NotFound(new { message = "Không tìm thấy đơn hàng." });
        }

        if (string.Equals(order.Status, "Delivered", StringComparison.OrdinalIgnoreCase))
        {
            return Conflict(new { message = "Không thể xóa đơn hàng đã giao. Vui lòng hủy đơn trước." });
        }

        await using var tran = await _db.Database.BeginTransactionAsync();
        try
        {
            var sellerOrderIds = order.SellerOrders.Select(so => so.SellerOrderId).ToList();
            if (sellerOrderIds.Count > 0)
            {
                var sellerOrderItems = await _db.SellerOrderItems
                    .Where(i => sellerOrderIds.Contains(i.SellerOrderId))
                    .ToListAsync();

                var sellerOrderItemIds = sellerOrderItems.Select(i => i.SellerOrderItemId).ToList();
                if (sellerOrderItemIds.Count > 0)
                {
                    var returns = await _db.ReturnRequests
                        .Where(r => sellerOrderItemIds.Contains(r.SellerOrderItemId))
                        .ToListAsync();
                    if (returns.Count > 0)
                    {
                        _db.ReturnRequests.RemoveRange(returns);
                    }

                    var inventoryReservations = await _db.InventoryReservations
                        .Where(r => r.SellerOrderItemId.HasValue && sellerOrderItemIds.Contains(r.SellerOrderItemId.Value))
                        .ToListAsync();
                    if (inventoryReservations.Count > 0)
                    {
                        _db.InventoryReservations.RemoveRange(inventoryReservations);
                    }
                }

                var payouts = await _db.PayoutItems
                    .Where(pi => sellerOrderIds.Contains(pi.SellerOrderId))
                    .ToListAsync();
                if (payouts.Count > 0)
                {
                    _db.PayoutItems.RemoveRange(payouts);
                }

                var shipments = await _db.Shipments
                    .Where(s => sellerOrderIds.Contains(s.SellerOrderId))
                    .ToListAsync();
                if (shipments.Count > 0)
                {
                    var shipmentIds = shipments.Select(s => s.ShipmentId).ToList();
                    var shipmentEvents = await _db.ShipmentEvents
                        .Where(e => shipmentIds.Contains(e.ShipmentId))
                        .ToListAsync();
                    if (shipmentEvents.Count > 0)
                    {
                        _db.ShipmentEvents.RemoveRange(shipmentEvents);
                    }

                    _db.Shipments.RemoveRange(shipments);
                }

                if (sellerOrderItems.Count > 0)
                {
                    _db.SellerOrderItems.RemoveRange(sellerOrderItems);
                }

                _db.SellerOrders.RemoveRange(order.SellerOrders);
            }

            if (order.OrderDetails.Count > 0)
            {
                _db.OrderDetails.RemoveRange(order.OrderDetails);
            }

            if (order.Payments.Count > 0)
            {
                _db.Payments.RemoveRange(order.Payments);
            }

            if (order.Shippings.Count > 0)
            {
                _db.Shippings.RemoveRange(order.Shippings);
            }

            if (order.CouponUsageHistories.Count > 0)
            {
                _db.CouponUsageHistories.RemoveRange(order.CouponUsageHistories);
            }

            if (order.InventoryReservations.Count > 0)
            {
                _db.InventoryReservations.RemoveRange(order.InventoryReservations);
            }

            if (order.PaymentTransactions.Count > 0)
            {
                var paymentTxnIds = order.PaymentTransactions.Select(t => t.PaymentTxnId).ToList();
                if (paymentTxnIds.Count > 0)
                {
                    var refunds = await _db.RefundTransactions.Where(r => paymentTxnIds.Contains(r.PaymentTxnId)).ToListAsync();
                    if (refunds.Count > 0)
                    {
                        _db.RefundTransactions.RemoveRange(refunds);
                    }

                    var payouts = await _db.Payouts.Where(p => p.PaymentTxnId.HasValue && paymentTxnIds.Contains(p.PaymentTxnId.Value)).ToListAsync();
                    if (payouts.Count > 0)
                    {
                        var payoutIds = payouts.Select(p => p.PayoutId).ToList();
                        var payoutItems = await _db.PayoutItems.Where(pi => payoutIds.Contains(pi.PayoutId)).ToListAsync();
                        if (payoutItems.Count > 0)
                        {
                            _db.PayoutItems.RemoveRange(payoutItems);
                        }

                        _db.Payouts.RemoveRange(payouts);
                    }
                }

                _db.PaymentTransactions.RemoveRange(order.PaymentTransactions);
            }

            if (order.ReconciliationLogs.Count > 0)
            {
                _db.ReconciliationLogs.RemoveRange(order.ReconciliationLogs);
            }

            _db.Orders.Remove(order);

            await _db.SaveChangesAsync();
            await tran.CommitAsync();

            return Ok(new { success = true, message = "Xóa đơn hàng thành công." });
        }
        catch (Exception ex)
        {
            await tran.RollbackAsync();
            return BadRequest(new { success = false, message = "Lỗi khi xóa đơn hàng.", detail = ex.Message });
        }
    }

    [Authorize(Policy = "SellerOnly")]
    [HttpGet("admin/statistics")]
    public async Task<IActionResult> GetAdminStatistics(CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var today = now.Date;
        var firstDayOfMonth = new DateTime(today.Year, today.Month, 1);
        var diff = (7 + (int)today.DayOfWeek - (int)DayOfWeek.Monday) % 7;
        var startOfWeek = today.AddDays(-diff);

        var query = _db.Orders.AsNoTracking();

        var totalOrders = await query.CountAsync(cancellationToken);
        var pendingOrders = await query.CountAsync(o => o.Status == "Pending", cancellationToken);
        var processingOrders = await query.CountAsync(o => o.Status == "Processing", cancellationToken);
        var shippedOrders = await query.CountAsync(o => o.Status == "Shipped", cancellationToken);
        var deliveredOrders = await query.CountAsync(o => o.Status == "Delivered", cancellationToken);
        var canceledOrders = await query.CountAsync(o => o.Status == "Canceled", cancellationToken);

        var totalRevenue = await query.Where(o => o.Status == "Delivered").SumAsync(o => (decimal?)o.TotalAmount, cancellationToken) ?? 0;
        var todayRevenue = await query.Where(o => o.Status == "Delivered" && o.OrderDate.Date == today).SumAsync(o => (decimal?)o.TotalAmount, cancellationToken) ?? 0;
        var monthRevenue = await query.Where(o => o.Status == "Delivered" && o.OrderDate >= firstDayOfMonth).SumAsync(o => (decimal?)o.TotalAmount, cancellationToken) ?? 0;

        var todayOrders = await query.CountAsync(o => o.OrderDate.Date == today, cancellationToken);
        var thisWeekOrders = await query.CountAsync(o => o.OrderDate >= startOfWeek, cancellationToken);
        var thisMonthOrders = await query.CountAsync(o => o.OrderDate >= firstDayOfMonth, cancellationToken);

        var avgOrderValue = deliveredOrders > 0 ? totalRevenue / deliveredOrders : 0m;
        var deliveryRate = totalOrders > 0 ? (decimal)deliveredOrders / totalOrders * 100m : 0m;
        var cancelRate = totalOrders > 0 ? (decimal)canceledOrders / totalOrders * 100m : 0m;

        return Ok(new
        {
            success = true,
            data = new
            {
                totalOrders,
                pendingOrders,
                processingOrders,
                shippedOrders,
                deliveredOrders,
                canceledOrders,
                totalRevenue,
                todayRevenue,
                monthRevenue,
                todayOrders,
                thisWeekOrders,
                thisMonthOrders,
                averageOrderValue = avgOrderValue,
                deliveryRate,
                cancelRate
            }
        });
    }

    private static bool TryNormalizeStatus(string? status, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(status))
        {
            return false;
        }

        return CanonicalStatuses.TryGetValue(status.Trim(), out normalized);
    }

    private static bool CanChangeStatus(string currentStatus, string newStatus)
    {
        if (string.Equals(currentStatus, "Delivered", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(currentStatus, "Canceled", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var validTransitions = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
        {
            { "Pending", new List<string> { "Processing", "Canceled" } },
            { "Processing", new List<string> { "Ready", "Shipped", "Canceled" } },
            { "Ready", new List<string> { "Shipped", "Canceled" } },
            { "Shipped", new List<string> { "Delivered", "Canceled" } }
        };

        return validTransitions.TryGetValue(currentStatus, out var next)
            && next.Contains(newStatus, StringComparer.OrdinalIgnoreCase);
    }

    private static string GetStatusText(string? status)
    {
        return status switch
        {
            "Pending" => "Chờ xử lý",
            "Processing" => "Đang xử lý",
            "Ready" => "Đã xử lý / Sẵn sàng giao",
            "Shipped" => "Đang giao hàng",
            "Delivered" => "Đã giao hàng",
            "Canceled" => "Đã hủy",
            _ => status ?? string.Empty
        };
    }

    private static string GetStatusBadgeClass(string? status)
    {
        return status switch
        {
            "Pending" => "bg-secondary",
            "Processing" => "bg-info",
            "Ready" => "badge-ready",
            "Shipped" => "bg-warning",
            "Delivered" => "bg-success",
            "Canceled" => "bg-danger",
            _ => "bg-secondary"
        };
    }

    private int? TryGetUserIdFromToken()
    {
        var sub = User.FindFirstValue(JwtRegisteredClaimNames.Sub)
                  ?? User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (!int.TryParse(sub, out var userId))
        {
            return null;
        }

        return userId;
    }

    public sealed class UpdateAdminOrderStatusRequest
    {
        public string NewStatus { get; set; } = string.Empty;
    }
}
