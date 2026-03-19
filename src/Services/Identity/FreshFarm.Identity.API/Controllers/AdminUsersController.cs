using FreshFarm.Identity.Api.Models;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

namespace FreshFarm.Identity.Api.Controllers;

[ApiController]
[Route("auth/admin/users")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Policy = "AdminOnly")]
public sealed class AdminUsersController : ControllerBase
{
    private const string UserNamePattern = @"^[a-zA-Z0-9._-]+$";
    private readonly FreshFarmIdentityDBContext _db;
    private readonly IPasswordHasher<User> _passwordHasher;

    public AdminUsersController(FreshFarmIdentityDBContext db, IPasswordHasher<User> passwordHasher)
    {
        _db = db;
        _passwordHasher = passwordHasher;
    }

    [HttpGet]
    public async Task<IActionResult> Get(
        [FromQuery] string? search = null,
        [FromQuery] int take = 5000,
        [FromQuery] string? userType = "all",
        [FromQuery] bool? isActive = null,
        [FromQuery] DateTime? createdFrom = null,
        [FromQuery] DateTime? createdTo = null)
    {
        if (take <= 0)
        {
            take = 5000;
        }

        if (take > 10000)
        {
            take = 10000;
        }

        var normalizedType = NormalizeUserType(userType);
        var query = _db.Users.AsNoTracking().AsQueryable();

        query = normalizedType switch
        {
            "admin" => query.Where(u => u.UserRoles.Any(ur => ur.Role.RoleName == "Admin")),
            "seller" => query.Where(u => u.UserRoles.Any(ur => ur.Role.RoleName == "Seller")),
            "buyer" => query.Where(u => u.UserRoles.Any(ur => ur.Role.RoleName == "Customer")),
            _ => query
        };

        if (isActive.HasValue)
        {
            query = query.Where(u => u.IsActive == isActive.Value);
        }

        if (createdFrom.HasValue)
        {
            var createdFromUtc = createdFrom.Value.Date;
            query = query.Where(u => u.CreatedAt >= createdFromUtc);
        }

        if (createdTo.HasValue)
        {
            var createdToExclusive = createdTo.Value.Date.AddDays(1);
            query = query.Where(u => u.CreatedAt < createdToExclusive);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLowerInvariant();
            query = query.Where(u =>
                u.UserName.ToLower().Contains(term) ||
                u.FullName.ToLower().Contains(term) ||
                u.Email.ToLower().Contains(term) ||
                (u.Phone != null && u.Phone.Contains(term)));
        }

        var rows = await query
            .OrderByDescending(u => u.CreatedAt)
            .Select(u => new
            {
                userId = u.UserId,
                userName = u.UserName,
                fullName = u.FullName,
                email = u.Email,
                phone = u.Phone,
                avatar = u.Avatar,
                isActive = u.IsActive,
                created = u.CreatedAt,
                updated = u.UpdatedAt,
                lastLogin = u.UserSessions
                    .OrderByDescending(s => s.CreatedAt)
                    .Select(s => (DateTime?)s.CreatedAt)
                    .FirstOrDefault(),
                role = u.UserRoles
                    .OrderBy(ur => ur.Role.RoleName)
                    .Select(ur => new
                    {
                        roleId = ur.RoleId,
                        roleName = ur.Role.RoleName,
                        isActive = ur.Role.IsActive
                    })
                    .FirstOrDefault()
            })
            .Take(take)
            .ToListAsync();

        var result = rows.Select(row => new
        {
            row.userId,
            row.userName,
            row.fullName,
            row.email,
            row.phone,
            row.avatar,
            roleId = row.role?.roleId ?? 0,
            role = row.role,
            row.isActive,
            row.lastLogin,
            row.created,
            row.updated
        });

        return Ok(result);
    }

    [HttpGet("roles")]
    public async Task<IActionResult> GetRoles()
    {
        var roles = await _db.Roles
            .AsNoTracking()
            .Where(r => r.IsActive)
            .OrderBy(r => r.RoleName)
            .Select(r => new
            {
                roleId = r.RoleId,
                roleName = r.RoleName,
                isActive = r.IsActive
            })
            .ToListAsync();

        return Ok(roles);
    }

    private static string NormalizeUserType(string? userType)
    {
        if (string.IsNullOrWhiteSpace(userType))
        {
            return "all";
        }

        var normalized = userType.Trim().ToLowerInvariant();
        return normalized is "admin" or "seller" or "buyer" ? normalized : "all";
    }

    [HttpGet("metrics")]
    public async Task<IActionResult> GetMetrics(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var todayStart = now.Date;
        var nextDay = todayStart.AddDays(1);
        var sevenDaysStart = todayStart.AddDays(-6);
        var warnings = new List<string>();

        try
        {
            var sellerRoleIdTask = _db.Roles
                .AsNoTracking()
                .Where(r => r.RoleName == "Seller")
                .Select(r => (int?)r.RoleId)
                .FirstOrDefaultAsync(cancellationToken);

            var buyerRoleIdTask = _db.Roles
                .AsNoTracking()
                .Where(r => r.RoleName == "Customer")
                .Select(r => (int?)r.RoleId)
                .FirstOrDefaultAsync(cancellationToken);

            var totalUsersTask = _db.Users
                .AsNoTracking()
                .CountAsync(cancellationToken);

            var newUsersTodayTask = _db.Users
                .AsNoTracking()
                .CountAsync(u => u.CreatedAt >= todayStart && u.CreatedAt < nextDay, cancellationToken);

            var newUsersSevenDaysTask = _db.Users
                .AsNoTracking()
                .CountAsync(u => u.CreatedAt >= sevenDaysStart && u.CreatedAt < nextDay, cancellationToken);

            await Task.WhenAll(
                sellerRoleIdTask,
                buyerRoleIdTask,
                totalUsersTask,
                newUsersTodayTask,
                newUsersSevenDaysTask);

            var sellerRoleId = sellerRoleIdTask.Result;
            var buyerRoleId = buyerRoleIdTask.Result;

            var totalSellersTask = sellerRoleId.HasValue
                ? _db.UserRoles.AsNoTracking().CountAsync(ur => ur.RoleId == sellerRoleId.Value, cancellationToken)
                : Task.FromResult(0);

            var newSellersSevenDaysTask = sellerRoleId.HasValue
                ? _db.UserRoles
                    .AsNoTracking()
                    .Where(ur => ur.RoleId == sellerRoleId.Value)
                    .Join(
                        _db.Users.AsNoTracking().Where(u => u.CreatedAt >= sevenDaysStart && u.CreatedAt < nextDay),
                        ur => ur.UserId,
                        u => u.UserId,
                        (ur, u) => ur.UserId)
                    .Distinct()
                    .CountAsync(cancellationToken)
                : Task.FromResult(0);

            var totalBuyersTask = buyerRoleId.HasValue
                ? _db.UserRoles.AsNoTracking().CountAsync(ur => ur.RoleId == buyerRoleId.Value, cancellationToken)
                : Task.FromResult(0);

            var newBuyersSevenDaysTask = buyerRoleId.HasValue
                ? _db.UserRoles
                    .AsNoTracking()
                    .Where(ur => ur.RoleId == buyerRoleId.Value)
                    .Join(
                        _db.Users.AsNoTracking().Where(u => u.CreatedAt >= sevenDaysStart && u.CreatedAt < nextDay),
                        ur => ur.UserId,
                        u => u.UserId,
                        (ur, u) => ur.UserId)
                    .Distinct()
                    .CountAsync(cancellationToken)
                : Task.FromResult(0);

            var trafficToday = 0;
            var activeSessions = 0;

            try
            {
                var trafficTodayTask = _db.UserSessions
                    .AsNoTracking()
                    .CountAsync(s => s.CreatedAt >= todayStart && s.CreatedAt < nextDay, cancellationToken);

                var activeSessionsTask = _db.UserSessions
                    .AsNoTracking()
                    .CountAsync(s => s.RevokedAt == null && s.ExpiresAt > now, cancellationToken);

                await Task.WhenAll(
                    totalSellersTask,
                    newSellersSevenDaysTask,
                    totalBuyersTask,
                    newBuyersSevenDaysTask,
                    trafficTodayTask,
                    activeSessionsTask);

                trafficToday = trafficTodayTask.Result;
                activeSessions = activeSessionsTask.Result;
            }
            catch (Exception ex)
            {
                warnings.Add($"UserSessions fallback: {ex.GetType().Name}");
                await Task.WhenAll(totalSellersTask, newSellersSevenDaysTask, totalBuyersTask, newBuyersSevenDaysTask);
            }

            return Ok(new
            {
                totalUsers = totalUsersTask.Result,
                newUsersToday = newUsersTodayTask.Result,
                newUsers7Days = newUsersSevenDaysTask.Result,
                totalSellers = totalSellersTask.Result,
                newSellers7Days = newSellersSevenDaysTask.Result,
                totalBuyers = totalBuyersTask.Result,
                newBuyers7Days = newBuyersSevenDaysTask.Result,
                trafficToday,
                activeSessions,
                warnings
            });
        }
        catch (Exception ex)
        {
            warnings.Add($"Metrics fallback: {ex.GetType().Name}");
            return Ok(new
            {
                totalUsers = 0,
                newUsersToday = 0,
                newUsers7Days = 0,
                totalSellers = 0,
                newSellers7Days = 0,
                totalBuyers = 0,
                newBuyers7Days = 0,
                trafficToday = 0,
                activeSessions = 0,
                warnings
            });
        }
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById([FromRoute] int id)
    {
        var row = await _db.Users
            .AsNoTracking()
            .Where(u => u.UserId == id)
            .Select(u => new
            {
                userId = u.UserId,
                userName = u.UserName,
                fullName = u.FullName,
                email = u.Email,
                phone = u.Phone,
                avatar = u.Avatar,
                isActive = u.IsActive,
                created = u.CreatedAt,
                updated = u.UpdatedAt,
                lastLogin = u.UserSessions
                    .OrderByDescending(s => s.CreatedAt)
                    .Select(s => (DateTime?)s.CreatedAt)
                    .FirstOrDefault(),
                role = u.UserRoles
                    .OrderBy(ur => ur.Role.RoleName)
                    .Select(ur => new
                    {
                        roleId = ur.RoleId,
                        roleName = ur.Role.RoleName,
                        isActive = ur.Role.IsActive
                    })
                    .FirstOrDefault()
            })
            .FirstOrDefaultAsync();

        if (row is null)
        {
            return NotFound(new { message = "Khong tim thay nguoi dung." });
        }

        return Ok(new
        {
            row.userId,
            row.userName,
            row.fullName,
            row.email,
            row.phone,
            row.avatar,
            roleId = row.role?.roleId ?? 0,
            role = row.role,
            row.isActive,
            row.lastLogin,
            row.created,
            row.updated
        });
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] AdminCreateUserRequest request)
    {
        if (request is null)
        {
            return BadRequest(new { message = "Du lieu khong hop le." });
        }

        var normalized = NormalizeCreateRequest(request);
        if (!normalized.isValid)
        {
            return BadRequest(new { message = normalized.error });
        }

        var role = await _db.Roles
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.RoleId == normalized.roleId && r.IsActive);
        if (role is null)
        {
            return BadRequest(new { message = "Role khong hop le hoac khong ton tai." });
        }

        var duplicateUserName = await _db.Users.AnyAsync(u => u.UserName == normalized.userName);
        if (duplicateUserName)
        {
            return Conflict(new { message = "Ten dang nhap da ton tai." });
        }

        var duplicateEmail = await _db.Users.AnyAsync(u => u.Email == normalized.email);
        if (duplicateEmail)
        {
            return Conflict(new { message = "Email da duoc su dung." });
        }

        if (!string.IsNullOrWhiteSpace(normalized.phone))
        {
            var duplicatePhone = await _db.Users.AnyAsync(u => u.Phone == normalized.phone);
            if (duplicatePhone)
            {
                return Conflict(new { message = "So dien thoai da duoc su dung." });
            }
        }

        var now = DateTime.UtcNow;
        var user = new User
        {
            UserName = normalized.userName,
            FullName = normalized.fullName,
            Email = normalized.email,
            Phone = normalized.phone,
            Avatar = normalized.avatar,
            IsActive = normalized.isActive,
            CreatedAt = now,
            UpdatedAt = now
        };

        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        _db.UserRoles.Add(new UserRole
        {
            UserId = user.UserId,
            RoleId = normalized.roleId,
            CreatedAt = now
        });

        _db.UserAuths.Add(new UserAuth
        {
            UserId = user.UserId,
            PasswordHash = _passwordHasher.HashPassword(user, normalized.password),
            FailedCount = 0,
            LockedUntil = null,
            Mfasecret = string.Empty,
            UpdatedAt = now
        });

        await _db.SaveChangesAsync();

        return Ok(new
        {
            success = true,
            message = "Tao nguoi dung thanh cong.",
            userId = user.UserId
        });
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update([FromRoute] int id, [FromBody] AdminUpdateUserRequest request)
    {
        if (request is null)
        {
            return BadRequest(new { message = "Du lieu khong hop le." });
        }

        var user = await _db.Users
            .Include(u => u.UserAuth)
            .Include(u => u.UserRoles)
            .ThenInclude(ur => ur.Role)
            .FirstOrDefaultAsync(u => u.UserId == id);

        if (user is null)
        {
            return NotFound(new { message = "Khong tim thay nguoi dung." });
        }

        var normalized = NormalizeUpdateRequest(request);
        if (!normalized.isValid)
        {
            return BadRequest(new { message = normalized.error });
        }

        var duplicateUserName = await _db.Users.AnyAsync(u => u.UserId != id && u.UserName == normalized.userName);
        if (duplicateUserName)
        {
            return Conflict(new { message = "Ten dang nhap da ton tai." });
        }

        var duplicateEmail = await _db.Users.AnyAsync(u => u.UserId != id && u.Email == normalized.email);
        if (duplicateEmail)
        {
            return Conflict(new { message = "Email da duoc su dung." });
        }

        if (!string.IsNullOrWhiteSpace(normalized.phone))
        {
            var duplicatePhone = await _db.Users.AnyAsync(u => u.UserId != id && u.Phone == normalized.phone);
            if (duplicatePhone)
            {
                return Conflict(new { message = "So dien thoai da duoc su dung." });
            }
        }

        var role = await _db.Roles
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.RoleId == normalized.roleId && r.IsActive);
        if (role is null)
        {
            return BadRequest(new { message = "Role khong hop le hoac khong ton tai." });
        }

        var now = DateTime.UtcNow;

        user.UserName = normalized.userName;
        user.FullName = normalized.fullName;
        user.Email = normalized.email;
        user.Phone = normalized.phone;
        user.Avatar = normalized.avatar;
        user.IsActive = normalized.isActive;
        user.UpdatedAt = now;

        var currentAssignedRoles = user.UserRoles
            .ToList();

        var hasTargetRole = currentAssignedRoles.Any(ur => ur.RoleId == normalized.roleId);
        if (!hasTargetRole)
        {
            _db.UserRoles.RemoveRange(currentAssignedRoles);
            user.UserRoles.Add(new UserRole
            {
                UserId = user.UserId,
                RoleId = normalized.roleId,
                CreatedAt = now
            });
        }

        if (!string.IsNullOrWhiteSpace(normalized.newPassword))
        {
            if (user.UserAuth is null)
            {
                user.UserAuth = new UserAuth
                {
                    UserId = user.UserId,
                    FailedCount = 0,
                    UpdatedAt = now
                };
            }

            user.UserAuth.PasswordHash = _passwordHasher.HashPassword(user, normalized.newPassword);
            user.UserAuth.UpdatedAt = now;
        }

        await _db.SaveChangesAsync();

        return Ok(new { success = true, message = "Cap nhat ho so thanh cong." });
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete([FromRoute] int id)
    {
        if (!TryGetCurrentUserId(out var currentUserId))
        {
            return Unauthorized(new { message = "Khong xac dinh duoc nguoi dung dang dang nhap." });
        }

        if (id == currentUserId)
        {
            return BadRequest(new { message = "Khong the xoa chinh tai khoan dang dang nhap." });
        }

        var user = await _db.Users
            .Include(u => u.UserRoles)
            .ThenInclude(ur => ur.Role)
            .FirstOrDefaultAsync(u => u.UserId == id);

        if (user is null)
        {
            return NotFound(new { message = "Khong tim thay nguoi dung." });
        }

        user.IsActive = false;
        user.UpdatedAt = DateTime.UtcNow;

        var sessions = await _db.UserSessions.Where(s => s.UserId == id).ToListAsync();
        if (sessions.Count > 0)
        {
            _db.UserSessions.RemoveRange(sessions);
        }

        await _db.SaveChangesAsync();

        return Ok(new
        {
            success = true,
            message = "Da vo hieu hoa tai khoan nguoi dung."
        });
    }

    private static (bool isValid, string? error, string userName, string fullName, string email, string password, string? phone, string? avatar, bool isActive, int roleId)
        NormalizeCreateRequest(AdminCreateUserRequest request)
    {
        var userName = request.UserName?.Trim() ?? string.Empty;
        var fullName = request.FullName?.Trim() ?? string.Empty;
        var email = request.Email?.Trim() ?? string.Empty;
        var password = request.Password?.Trim() ?? string.Empty;
        var phone = string.IsNullOrWhiteSpace(request.Phone) ? null : request.Phone.Trim();
        var avatar = string.IsNullOrWhiteSpace(request.Avatar) ? null : request.Avatar.Trim();

        if (string.IsNullOrWhiteSpace(userName))
        {
            return (false, "Ten dang nhap khong duoc de trong.", string.Empty, string.Empty, string.Empty, string.Empty, null, null, true, 0);
        }

        if (!System.Text.RegularExpressions.Regex.IsMatch(userName, UserNamePattern))
        {
            return (false, "Ten dang nhap chi duoc chua chu cai, so, dau cham, gach duoi hoac gach ngang.", string.Empty, string.Empty, string.Empty, string.Empty, null, null, true, 0);
        }

        if (string.IsNullOrWhiteSpace(fullName))
        {
            return (false, "Ho ten khong duoc de trong.", string.Empty, string.Empty, string.Empty, string.Empty, null, null, true, 0);
        }

        if (string.IsNullOrWhiteSpace(email))
        {
            return (false, "Email khong duoc de trong.", string.Empty, string.Empty, string.Empty, string.Empty, null, null, true, 0);
        }

        if (string.IsNullOrWhiteSpace(password))
        {
            return (false, "Mat khau khong duoc de trong.", string.Empty, string.Empty, string.Empty, string.Empty, null, null, true, 0);
        }

        if (!request.RoleId.HasValue || request.RoleId.Value <= 0)
        {
            return (false, "Vui long chon role cho nguoi dung.", string.Empty, string.Empty, string.Empty, string.Empty, null, null, true, 0);
        }

        return (true, null, userName, fullName, email, password, phone, avatar, request.IsActive ?? true, request.RoleId ?? 0);
    }

    private static (bool isValid, string? error, string userName, string fullName, string email, string? newPassword, string? phone, string? avatar, bool isActive, int roleId)
        NormalizeUpdateRequest(AdminUpdateUserRequest request)
    {
        var userName = request.UserName?.Trim() ?? string.Empty;
        var fullName = request.FullName?.Trim() ?? string.Empty;
        var email = request.Email?.Trim() ?? string.Empty;
        var phone = string.IsNullOrWhiteSpace(request.Phone) ? null : request.Phone.Trim();
        var avatar = string.IsNullOrWhiteSpace(request.Avatar) ? null : request.Avatar.Trim();
        var newPassword = string.IsNullOrWhiteSpace(request.NewPassword) ? null : request.NewPassword.Trim();

        if (string.IsNullOrWhiteSpace(userName))
        {
            return (false, "Ten dang nhap khong duoc de trong.", string.Empty, string.Empty, string.Empty, null, null, null, true, 0);
        }

        if (!System.Text.RegularExpressions.Regex.IsMatch(userName, UserNamePattern))
        {
            return (false, "Ten dang nhap chi duoc chua chu cai, so, dau cham, gach duoi hoac gach ngang.", string.Empty, string.Empty, string.Empty, null, null, null, true, 0);
        }

        if (string.IsNullOrWhiteSpace(fullName))
        {
            return (false, "Ho ten khong duoc de trong.", string.Empty, string.Empty, string.Empty, null, null, null, true, 0);
        }

        if (string.IsNullOrWhiteSpace(email))
        {
            return (false, "Email khong duoc de trong.", string.Empty, string.Empty, string.Empty, null, null, null, true, 0);
        }

        if (!request.RoleId.HasValue || request.RoleId.Value <= 0)
        {
            return (false, "Vui long chon role cho nguoi dung.", string.Empty, string.Empty, string.Empty, null, null, null, true, 0);
        }

        return (true, null, userName, fullName, email, newPassword, phone, avatar, request.IsActive ?? true, request.RoleId ?? 0);
    }

    private bool TryGetCurrentUserId(out int userId)
    {
        userId = 0;

        var claim = User.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)?.Value
                    ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

        return int.TryParse(claim, out userId);
    }

    public sealed class AdminCreateUserRequest
    {
        [Required]
        [RegularExpression(UserNamePattern, ErrorMessage = "Ten dang nhap chi duoc chua chu cai, so, dau cham, gach duoi hoac gach ngang.")]
        public string UserName { get; set; } = string.Empty;

        [Required]
        public string FullName { get; set; } = string.Empty;

        [Required]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Required]
        public string Password { get; set; } = string.Empty;

        public string? Phone { get; set; }

        public string? Avatar { get; set; }

        public bool? IsActive { get; set; }

        public int? RoleId { get; set; }
    }

    public sealed class AdminUpdateUserRequest
    {
        [Required]
        [RegularExpression(UserNamePattern, ErrorMessage = "Ten dang nhap chi duoc chua chu cai, so, dau cham, gach duoi hoac gach ngang.")]
        public string UserName { get; set; } = string.Empty;

        [Required]
        public string FullName { get; set; } = string.Empty;

        [Required]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;

        public string? NewPassword { get; set; }

        public string? Phone { get; set; }

        public string? Avatar { get; set; }

        public bool? IsActive { get; set; }

        public int? RoleId { get; set; }
    }
}
