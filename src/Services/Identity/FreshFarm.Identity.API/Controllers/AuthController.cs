using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using FreshFarm.Identity.Api.Dtos;
using FreshFarm.Identity.Api.Models;
using FreshFarm.Identity.Api.Options;
using FreshFarm.Identity.Api.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.Security.Cryptography;

namespace FreshFarm.Identity.Api.Controllers
{
    [Route("auth")]
    [ApiController]
    public class AuthController : ControllerBase
    {
        private readonly FreshFarmIdentityDBContext _db;
        private readonly IPasswordHasher<User> _passwordHasher;
        private readonly IConfiguration _config;
        private readonly IPasswordResetTokenService _passwordResetTokenService;
        private readonly IAccountEmailSender _accountEmailSender;
        private readonly PasswordResetOptions _passwordResetOptions;
        private readonly ILogger<AuthController> _logger;

        public AuthController(
            FreshFarmIdentityDBContext db,
            IPasswordHasher<User> passwordHasher,
            IConfiguration config,
            IPasswordResetTokenService passwordResetTokenService,
            IAccountEmailSender accountEmailSender,
            IOptions<PasswordResetOptions> passwordResetOptions,
            ILogger<AuthController> logger)
        {
            _db = db;
            _passwordHasher = passwordHasher;
            _config = config;
            _passwordResetTokenService = passwordResetTokenService;
            _accountEmailSender = accountEmailSender;
            _passwordResetOptions = passwordResetOptions.Value;
            _logger = logger;
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Identifier) || string.IsNullOrWhiteSpace(request.Password))
            {
                return BadRequest("Vui lòng nhập đầy đủ Email/Username và Mật khẩu");
            }

            var id = request.Identifier.Trim();
            var isEmail = id.Contains("@");
            var user = await _db.Users
                .Include(u => u.UserAuth)
                .Include(u => u.UserRoles)
                .ThenInclude(ur => ur.Role)
                .SingleOrDefaultAsync(u => isEmail ? u.Email == id : u.UserName == id);

            if (user?.UserAuth == null)
            {
                return Unauthorized("Tài khoản hoặc mật khẩu không đúng.");
            }

            if (!user.IsActive)
            {
                return Unauthorized("Tài khoản của bạn đã bị vô hiệu hóa.");
            }

            var verificationResult = _passwordHasher.VerifyHashedPassword(user, user.UserAuth.PasswordHash, request.Password);
            if (verificationResult == PasswordVerificationResult.Failed)
            {
                return Unauthorized("Tài khoản hoặc mật khẩu không đúng.");
            }

            var roleNames = user.UserRoles.Select(ur => ur.Role.RoleName).ToList();
            var token = CreateToken(user, roleNames);
            return Ok(token);
        }

        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] RegisterRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Email) ||
                string.IsNullOrWhiteSpace(request.UserName) ||
                string.IsNullOrWhiteSpace(request.FullName) ||
                string.IsNullOrWhiteSpace(request.Password) ||
                string.IsNullOrWhiteSpace(request.Phone) ||
                string.IsNullOrWhiteSpace(request.ConfirmPassword))
            {
                return BadRequest("Toàn bộ thông tin là bắt buộc nhập.");
            }

            var existingUser = await _db.Users.AnyAsync(u =>
                u.Email == request.Email.Trim() ||
                u.UserName == request.UserName.Trim() ||
                u.Phone == request.Phone.Trim());

            if (existingUser)
            {
                return Conflict("Email, số điện thoại hoặc tên đăng nhập đã tồn tại.");
            }

            if (request.Password != request.ConfirmPassword)
            {
                return BadRequest("Mật khẩu xác nhận không khớp.");
            }

            var roleName = string.IsNullOrEmpty(request.RoleName) ? "Customer" : request.RoleName.Trim();
            var role = await _db.Roles.SingleOrDefaultAsync(r => r.RoleName == roleName);
            if (role == null)
            {
                return BadRequest($"Vai trò {roleName} không tìm thấy.");
            }

            await using var transaction = await _db.Database.BeginTransactionAsync();
            try
            {
                var user = new User
                {
                    Email = request.Email.Trim(),
                    UserName = request.UserName.Trim(),
                    FullName = request.FullName.Trim(),
                    Phone = request.Phone.Trim(),
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow
                };

                _db.Users.Add(user);
                await _db.SaveChangesAsync();

                var hashedPassword = _passwordHasher.HashPassword(user, request.Password);
                _db.UserAuths.Add(new UserAuth
                {
                    UserId = user.UserId,
                    PasswordHash = hashedPassword,
                    FailedCount = 0,
                    UpdatedAt = DateTime.UtcNow
                });

                _db.UserRoles.Add(new UserRole
                {
                    RoleId = role.RoleId,
                    UserId = user.UserId,
                    CreatedAt = DateTime.UtcNow
                });

                await _db.SaveChangesAsync();
                await transaction.CommitAsync();
                return Ok(new { user.UserId, user.UserName, user.Email, role.RoleName });
            }
            catch (Exception)
            {
                await transaction.RollbackAsync();
                return StatusCode(500, "Có lỗi xảy ra khi đăng kí, vui lòng thử lại");
            }
        }

        [HttpPost("external-login")]
        public async Task<IActionResult> ExternalLogin([FromBody] ExternalLoginRequest request, CancellationToken cancellationToken)
        {
            if (!ModelState.IsValid)
            {
                return ValidationProblem(ModelState);
            }

            var provider = request.Provider.Trim();
            if (!provider.Equals("Google", StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest("Hiện tại hệ thống chỉ hỗ trợ đăng nhập Google.");
            }

            var normalizedEmail = request.Email.Trim();
            var normalizedFullName = request.FullName.Trim();
            var normalizedAvatar = string.IsNullOrWhiteSpace(request.AvatarUrl)
                ? null
                : request.AvatarUrl.Trim();

            var user = await _db.Users
                .Include(u => u.UserAuth)
                .Include(u => u.UserRoles)
                .ThenInclude(ur => ur.Role)
                .SingleOrDefaultAsync(u => u.Email == normalizedEmail, cancellationToken);

            if (user is not null && !user.IsActive)
            {
                return Unauthorized("Tài khoản của bạn đã bị vô hiệu hóa.");
            }

            if (user is null)
            {
                var customerRole = await _db.Roles.SingleOrDefaultAsync(r => r.RoleName == "Customer", cancellationToken);
                if (customerRole is null)
                {
                    return StatusCode(StatusCodes.Status500InternalServerError, "Không tìm thấy vai trò Customer để tạo tài khoản Google.");
                }

                await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
                try
                {
                    var now = DateTime.UtcNow;
                    user = new User
                    {
                        Email = normalizedEmail,
                        FullName = string.IsNullOrWhiteSpace(normalizedFullName) ? normalizedEmail : normalizedFullName,
                        UserName = await GenerateUniqueUserNameAsync(normalizedEmail, cancellationToken),
                        Phone = await GeneratePlaceholderPhoneAsync(cancellationToken),
                        Avatar = normalizedAvatar,
                        IsActive = true,
                        CreatedAt = now
                    };

                    _db.Users.Add(user);
                    await _db.SaveChangesAsync(cancellationToken);

                    var randomPassword = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
                    _db.UserAuths.Add(new UserAuth
                    {
                        UserId = user.UserId,
                        PasswordHash = _passwordHasher.HashPassword(user, randomPassword),
                        FailedCount = 0,
                        UpdatedAt = now
                    });

                    _db.UserRoles.Add(new UserRole
                    {
                        UserId = user.UserId,
                        RoleId = customerRole.RoleId,
                        CreatedAt = now
                    });

                    await _db.SaveChangesAsync(cancellationToken);
                    await transaction.CommitAsync(cancellationToken);

                    user = await _db.Users
                        .Include(u => u.UserAuth)
                        .Include(u => u.UserRoles)
                        .ThenInclude(ur => ur.Role)
                        .SingleAsync(u => u.UserId == user.UserId, cancellationToken);
                }
                catch
                {
                    await transaction.RollbackAsync(cancellationToken);
                    throw;
                }
            }
            else
            {
                var shouldSave = false;
                if (!string.IsNullOrWhiteSpace(normalizedFullName) && !string.Equals(user.FullName, normalizedFullName, StringComparison.Ordinal))
                {
                    user.FullName = normalizedFullName;
                    shouldSave = true;
                }

                if (!string.IsNullOrWhiteSpace(normalizedAvatar) && !string.Equals(user.Avatar, normalizedAvatar, StringComparison.Ordinal))
                {
                    user.Avatar = normalizedAvatar;
                    shouldSave = true;
                }

                if (shouldSave)
                {
                    user.UpdatedAt = DateTime.UtcNow;
                    await _db.SaveChangesAsync(cancellationToken);
                }
            }

            var roleNames = user.UserRoles.Select(ur => ur.Role.RoleName).ToList();
            if (roleNames.Count == 0)
            {
                roleNames.Add("Customer");
            }

            return Ok(CreateToken(user, roleNames));
        }

        [HttpPost("forgot-password")]
        public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest request, CancellationToken cancellationToken)
        {
            if (!ModelState.IsValid)
            {
                return ValidationProblem(ModelState);
            }

            if (!_accountEmailSender.IsConfigured)
            {
                return StatusCode(StatusCodes.Status503ServiceUnavailable, "Chưa cấu hình kênh email đặt lại mật khẩu. Cần cập nhật SMTP hoặc Gmail sender.");
            }

            if (!TryBuildResetUrlBase(out var resetUrlBase))
            {
                return StatusCode(StatusCodes.Status503ServiceUnavailable, "Chưa cấu hình đường dẫn ResetUrlBase cho email đặt lại mật khẩu.");
            }

            var normalizedEmail = request.Email.Trim();
            var user = await _db.Users
                .Include(x => x.UserAuth)
                .SingleOrDefaultAsync(x => x.Email == normalizedEmail, cancellationToken);

            if (user is not null && user.IsActive)
            {
                var token = _passwordResetTokenService.GenerateToken(user, user.UserAuth);
                var resetUrl = QueryHelpers.AddQueryString(resetUrlBase, new Dictionary<string, string?>
                {
                    ["email"] = user.Email,
                    ["token"] = token
                });

                await _accountEmailSender.SendPasswordResetEmailAsync(
                    user.Email,
                    user.FullName,
                    resetUrl,
                    ResolveTokenLifetimeMinutes(),
                    cancellationToken);

                _logger.LogInformation("Đã gửi email đặt lại mật khẩu cho userId {UserId}.", user.UserId);
            }

            return Ok(new
            {
                success = true,
                message = "Nếu email tồn tại trong hệ thống, chúng tôi đã gửi hướng dẫn đặt lại mật khẩu."
            });
        }

        [HttpPost("reset-password")]
        public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest request, CancellationToken cancellationToken)
        {
            if (!ModelState.IsValid)
            {
                return ValidationProblem(ModelState);
            }

            var normalizedEmail = request.Email.Trim();
            var user = await _db.Users
                .Include(x => x.UserAuth)
                .SingleOrDefaultAsync(x => x.Email == normalizedEmail, cancellationToken);

            if (user is null || !user.IsActive)
            {
                return BadRequest("Liên kết đặt lại mật khẩu không hợp lệ.");
            }

            if (!_passwordResetTokenService.TryValidateToken(request.Token, user, user.UserAuth, out var tokenError))
            {
                return BadRequest(tokenError ?? "Liên kết đặt lại mật khẩu không hợp lệ.");
            }

            var now = DateTime.UtcNow;
            var userAuth = user.UserAuth;
            if (userAuth is null)
            {
                userAuth = new UserAuth
                {
                    UserId = user.UserId,
                    FailedCount = 0,
                    LockedUntil = null,
                    Mfasecret = string.Empty,
                    UpdatedAt = now
                };
                _db.UserAuths.Add(userAuth);
            }

            userAuth.PasswordHash = _passwordHasher.HashPassword(user, request.NewPassword);
            userAuth.FailedCount = 0;
            userAuth.LockedUntil = null;
            userAuth.UpdatedAt = now;
            user.UpdatedAt = now;

            await _db.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Đã đặt lại mật khẩu cho userId {UserId}.", user.UserId);
            return Ok(new
            {
                success = true,
                message = "Đặt lại mật khẩu thành công."
            });
        }

        private AuthResponse CreateToken(User user, IEnumerable<string> roles)
        {
            var jwtKey = _config["Jwt:Key"];
            if (string.IsNullOrWhiteSpace(jwtKey))
            {
                throw new InvalidOperationException("JWT Key chưa được cấu hình.");
            }

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var claims = new List<Claim>
            {
                new Claim(JwtRegisteredClaimNames.Sub, user.UserId.ToString()),
                new Claim(JwtRegisteredClaimNames.Email, user.Email ?? string.Empty),
                new Claim("username", user.UserName ?? string.Empty)
            };

            claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));
            var expires = DateTime.UtcNow.AddHours(1);
            var jwt = new JwtSecurityToken(
                issuer: _config["Jwt:Issuer"],
                audience: _config["Jwt:Audience"],
                claims: claims,
                expires: expires,
                signingCredentials: creds);

            return new AuthResponse
            {
                AccessToken = new JwtSecurityTokenHandler().WriteToken(jwt),
                ExpiredAtUtc = expires
            };
        }

        [HttpGet("profile")]
        [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
        public async Task<IActionResult> GetProfile()
        {
            if (!TryGetCurrentUserId(out var userId))
            {
                return Unauthorized("Token không chứa user id hợp lệ.");
            }

            var user = await _db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.UserId == userId);
            if (user is null)
            {
                return NotFound("Không tìm thấy tài khoản.");
            }

            var dto = new ProfileResponseDto
            {
                UserId = user.UserId,
                UserName = user.UserName,
                FullName = user.FullName,
                Email = user.Email,
                Phone = user.Phone
            };

            return Ok(dto);
        }

        [HttpPut("profile")]
        [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
        public async Task<IActionResult> UpdateProfile([FromBody] UpdateProfileRequestDto request)
        {
            if (!ModelState.IsValid)
            {
                return ValidationProblem(ModelState);
            }

            if (!TryGetCurrentUserId(out var userId))
            {
                return Unauthorized("Token không chứa user id hợp lệ.");
            }

            var user = await _db.Users.SingleOrDefaultAsync(u => u.UserId == userId);
            if (user is null)
            {
                return NotFound("Không tìm thấy tài khoản.");
            }

            var normalizedEmail = request.Email.Trim();
            var normalizedPhone = request.Phone.Trim();
            var normalizedFullName = request.FullName.Trim();

            var emailExists = await _db.Users.AnyAsync(u => u.UserId != userId && u.Email == normalizedEmail);
            if (emailExists)
            {
                return Conflict("Email đã được sử dụng bởi tài khoản khác.");
            }

            var phoneExists = await _db.Users.AnyAsync(u => u.UserId != userId && u.Phone == normalizedPhone);
            if (phoneExists)
            {
                return Conflict("Số điện thoại đã được sử dụng bởi tài khoản khác.");
            }

            user.FullName = normalizedFullName;
            user.Email = normalizedEmail;
            user.Phone = normalizedPhone;
            user.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();

            return Ok(new ProfileResponseDto
            {
                UserId = user.UserId,
                UserName = user.UserName,
                FullName = user.FullName,
                Email = user.Email,
                Phone = user.Phone
            });
        }

        [HttpGet("addresses")]
        [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
        public async Task<IActionResult> GetAddresses()
        {
            if (!TryGetCurrentUserId(out var userId))
            {
                return Unauthorized("Token không chứa user id hợp lệ.");
            }

            var addresses = await _db.AddressBooks
                .AsNoTracking()
                .Where(a => a.UserId == userId && a.IsActive)
                .OrderByDescending(a => a.IsDefault)
                .ThenByDescending(a => a.UpdatedAt ?? a.CreatedAt)
                .Select(a => new AddressResponseDto
                {
                    AddressId = a.AddressId,
                    RecipientName = a.RecipientName,
                    Phone = a.Phone,
                    AddressDetail = a.AddressDetail,
                    Province = a.Province,
                    District = a.District,
                    Ward = a.Ward,
                    IsDefault = a.IsDefault
                })
                .ToListAsync();

            return Ok(addresses);
        }

        [HttpPost("addresses")]
        [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
        public async Task<IActionResult> CreateAddress([FromBody] UpsertAddressRequestDto request)
        {
            if (!ModelState.IsValid)
            {
                return ValidationProblem(ModelState);
            }

            if (!TryGetCurrentUserId(out var userId))
            {
                return Unauthorized("Token không chứa user id hợp lệ.");
            }

            var now = DateTime.UtcNow;
            var normalizedRecipient = request.RecipientName.Trim();
            var normalizedPhone = request.Phone.Trim();
            var normalizedAddressDetail = request.AddressDetail.Trim();
            var normalizedProvince = string.IsNullOrWhiteSpace(request.Province) ? null : request.Province.Trim();
            var normalizedDistrict = string.IsNullOrWhiteSpace(request.District) ? null : request.District.Trim();
            var normalizedWard = string.IsNullOrWhiteSpace(request.Ward) ? null : request.Ward.Trim();

            var duplicated = await _db.AddressBooks
                .AsNoTracking()
                .Where(a => a.UserId == userId && a.IsActive)
                .AnyAsync(a =>
                    a.AddressDetail == normalizedAddressDetail &&
                    a.Province == normalizedProvince &&
                    a.District == normalizedDistrict &&
                    a.Ward == normalizedWard);

            if (duplicated)
            {
                return Conflict("Địa chỉ này đã có trong sổ địa chỉ. Vui lòng chọn địa chỉ đã lưu hoặc nhập địa chỉ khác.");
            }

            if (request.IsDefault)
            {
                var oldDefaults = await _db.AddressBooks
                    .Where(a => a.UserId == userId && a.IsActive && a.IsDefault)
                    .ToListAsync();

                foreach (var item in oldDefaults)
                {
                    item.IsDefault = false;
                    item.UpdatedAt = now;
                }
            }

            var entity = new AddressBook
            {
                UserId = userId,
                RecipientName = normalizedRecipient,
                Phone = normalizedPhone,
                AddressDetail = normalizedAddressDetail,
                Province = normalizedProvince,
                District = normalizedDistrict,
                Ward = normalizedWard,
                IsDefault = request.IsDefault,
                IsActive = true,
                CreatedAt = now,
                UpdatedAt = now
            };

            _db.AddressBooks.Add(entity);
            await _db.SaveChangesAsync();

            return Ok(new AddressResponseDto
            {
                AddressId = entity.AddressId,
                RecipientName = entity.RecipientName,
                Phone = entity.Phone,
                AddressDetail = entity.AddressDetail,
                Province = entity.Province,
                District = entity.District,
                Ward = entity.Ward,
                IsDefault = entity.IsDefault
            });
        }

        [HttpPut("addresses/{addressId:int}")]
        [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
        public async Task<IActionResult> UpdateAddress(int addressId, [FromBody] UpsertAddressRequestDto request)
        {
            if (!ModelState.IsValid)
            {
                return ValidationProblem(ModelState);
            }

            if (!TryGetCurrentUserId(out var userId))
            {
                return Unauthorized("Token không chứa user id hợp lệ.");
            }

            var entity = await _db.AddressBooks
                .SingleOrDefaultAsync(a => a.AddressId == addressId && a.UserId == userId && a.IsActive);
            if (entity is null)
            {
                return NotFound("Không tìm thấy địa chỉ cần cập nhật.");
            }

            var now = DateTime.UtcNow;
            var normalizedRecipient = request.RecipientName.Trim();
            var normalizedPhone = request.Phone.Trim();
            var normalizedAddressDetail = request.AddressDetail.Trim();
            var normalizedProvince = string.IsNullOrWhiteSpace(request.Province) ? null : request.Province.Trim();
            var normalizedDistrict = string.IsNullOrWhiteSpace(request.District) ? null : request.District.Trim();
            var normalizedWard = string.IsNullOrWhiteSpace(request.Ward) ? null : request.Ward.Trim();
            var duplicated = await _db.AddressBooks
                .AsNoTracking()
                .Where(a => a.UserId == userId && a.IsActive && a.AddressId != addressId)
                .AnyAsync(a =>
                    a.AddressDetail == normalizedAddressDetail &&
                    a.Province == normalizedProvince &&
                    a.District == normalizedDistrict &&
                    a.Ward == normalizedWard);

            if (duplicated)
            {
                return Conflict("Địa chỉ này đã có trong sổ địa chỉ. Vui lòng chọn địa chỉ đã lưu hoặc nhập địa chỉ khác.");
            }

            if (request.IsDefault)
            {
                var oldDefaults = await _db.AddressBooks
                    .Where(a => a.UserId == userId && a.IsActive && a.IsDefault && a.AddressId != addressId)
                    .ToListAsync();

                foreach (var item in oldDefaults)
                {
                    item.IsDefault = false;
                    item.UpdatedAt = now;
                }
            }

            entity.RecipientName = normalizedRecipient;
            entity.Phone = normalizedPhone;
            entity.AddressDetail = normalizedAddressDetail;
            entity.Province = normalizedProvince;
            entity.District = normalizedDistrict;
            entity.Ward = normalizedWard;
            entity.IsDefault = request.IsDefault;
            entity.UpdatedAt = now;

            await _db.SaveChangesAsync();

            return Ok(new AddressResponseDto
            {
                AddressId = entity.AddressId,
                RecipientName = entity.RecipientName,
                Phone = entity.Phone,
                AddressDetail = entity.AddressDetail,
                Province = entity.Province,
                District = entity.District,
                Ward = entity.Ward,
                IsDefault = entity.IsDefault
            });
        }

        [HttpPost("addresses/{addressId:int}/set-default")]
        [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
        public async Task<IActionResult> SetDefaultAddress(int addressId)
        {
            if (!TryGetCurrentUserId(out var userId))
            {
                return Unauthorized("Token không chứa user id hợp lệ.");
            }

            var entity = await _db.AddressBooks
                .SingleOrDefaultAsync(a => a.AddressId == addressId && a.UserId == userId && a.IsActive);
            if (entity is null)
            {
                return NotFound("Không tìm thấy địa chỉ cần đặt mặc định.");
            }

            var now = DateTime.UtcNow;
            var oldDefaults = await _db.AddressBooks
                .Where(a => a.UserId == userId && a.IsActive && a.IsDefault && a.AddressId != addressId)
                .ToListAsync();

            foreach (var item in oldDefaults)
            {
                item.IsDefault = false;
                item.UpdatedAt = now;
            }

            entity.IsDefault = true;
            entity.UpdatedAt = now;

            await _db.SaveChangesAsync();
            return NoContent();
        }

        [HttpDelete("addresses/{addressId:int}")]
        [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
        public async Task<IActionResult> DeleteAddress(int addressId)
        {
            if (!TryGetCurrentUserId(out var userId))
            {
                return Unauthorized("Token không chứa user id hợp lệ.");
            }

            var entity = await _db.AddressBooks
                .SingleOrDefaultAsync(a => a.AddressId == addressId && a.UserId == userId && a.IsActive);
            if (entity is null)
            {
                return NotFound("Không tìm thấy địa chỉ cần xóa.");
            }

            entity.IsActive = false;
            entity.IsDefault = false;
            entity.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();
            return NoContent();
        }

        private bool TryGetCurrentUserId(out int userId)
        {
            userId = 0;

            var rawUserId = User.FindFirstValue(JwtRegisteredClaimNames.Sub)
                ?? User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? User.FindFirstValue("sub");

            return int.TryParse(rawUserId, out userId);
        }

        private bool TryBuildResetUrlBase(out string resetUrlBase)
        {
            resetUrlBase = _passwordResetOptions.ResetUrlBase?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(resetUrlBase))
            {
                return false;
            }

            return Uri.TryCreate(resetUrlBase, UriKind.Absolute, out _);
        }

        private int ResolveTokenLifetimeMinutes()
        {
            return _passwordResetOptions.TokenLifetimeMinutes <= 0
                ? 30
                : _passwordResetOptions.TokenLifetimeMinutes;
        }

        private async Task<string> GenerateUniqueUserNameAsync(string email, CancellationToken cancellationToken)
        {
            var localPart = email.Split('@')[0];
            var sanitized = new string(localPart
                .Where(ch => char.IsLetterOrDigit(ch) || ch == '.' || ch == '_' || ch == '-')
                .ToArray());

            if (string.IsNullOrWhiteSpace(sanitized))
            {
                sanitized = "googleuser";
            }

            var candidate = sanitized;
            var suffix = 1;
            while (await _db.Users.AnyAsync(u => u.UserName == candidate, cancellationToken))
            {
                candidate = $"{sanitized}{suffix}";
                suffix++;
            }

            return candidate;
        }

        private async Task<string> GeneratePlaceholderPhoneAsync(CancellationToken cancellationToken)
        {
            for (var attempt = 0; attempt < 20; attempt++)
            {
                var digits = RandomNumberGenerator.GetInt32(100000000, 999999999);
                var candidate = $"0{digits}";
                var exists = await _db.Users.AnyAsync(u => u.Phone == candidate, cancellationToken);
                if (!exists)
                {
                    return candidate;
                }
            }

            throw new InvalidOperationException("Không thể tạo số điện thoại tạm duy nhất cho tài khoản Google.");
        }
    }
}
