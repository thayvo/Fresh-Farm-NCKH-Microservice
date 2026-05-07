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
using System.Text.RegularExpressions;

namespace FreshFarm.Identity.Api.Controllers
{
    [Route("auth")]
    [ApiController]
    public class AuthController : ControllerBase
    {
        private const int MaxFailedLoginAttempts = 5;
        private const int BaseLockoutDurationMinutes = 5;
        private static readonly Regex VietnamPhoneRegex = new(@"^(0\d{9}|\+84\d{9})$", RegexOptions.Compiled);

        private readonly FreshFarmIdentityDBContext _db;
        private readonly IPasswordHasher<User> _passwordHasher;
        private readonly IConfiguration _config;
        private readonly IPasswordResetTokenService _passwordResetTokenService;
        private readonly IEmailVerificationTokenService _emailVerificationTokenService;
        private readonly ITotpService _totpService;
        private readonly ITwoFactorLoginTicketService _twoFactorLoginTicketService;
        private readonly IAccountEmailSender _accountEmailSender;
        private readonly IAuthAuditService _authAuditService;
        private readonly PasswordResetOptions _passwordResetOptions;
        private readonly EmailVerificationOptions _emailVerificationOptions;
        private readonly ILogger<AuthController> _logger;

        public AuthController(
            FreshFarmIdentityDBContext db,
            IPasswordHasher<User> passwordHasher,
            IConfiguration config,
            IPasswordResetTokenService passwordResetTokenService,
            IEmailVerificationTokenService emailVerificationTokenService,
            ITotpService totpService,
            ITwoFactorLoginTicketService twoFactorLoginTicketService,
            IAccountEmailSender accountEmailSender,
            IAuthAuditService authAuditService,
            IOptions<PasswordResetOptions> passwordResetOptions,
            IOptions<EmailVerificationOptions> emailVerificationOptions,
            ILogger<AuthController> logger)
        {
            _db = db;
            _passwordHasher = passwordHasher;
            _config = config;
            _passwordResetTokenService = passwordResetTokenService;
            _emailVerificationTokenService = emailVerificationTokenService;
            _totpService = totpService;
            _twoFactorLoginTicketService = twoFactorLoginTicketService;
            _accountEmailSender = accountEmailSender;
            _authAuditService = authAuditService;
            _passwordResetOptions = passwordResetOptions.Value;
            _emailVerificationOptions = emailVerificationOptions.Value;
            _logger = logger;
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginRequest request)
        {
            if (!TryValidateLoginRequest(request, out var validationProblem))
            {
                _logger.LogWarning("Tu choi login do thieu du lieu bat buoc.");
                return validationProblem!;
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
                _logger.LogWarning("Dang nhap that bai: khong tim thay tai khoan cho identifier {Identifier}.", MaskIdentifier(id));
                await WriteAuthAuditAsync(
                    request,
                    null,
                    null,
                    "login_failed",
                    success: false,
                    "account_not_found");
                return Unauthorized("Tài khoản hoặc mật khẩu không đúng.");
            }

            var roleNames = GetRoleNames(user);

            if (!user.IsActive)
            {
                _logger.LogWarning("Dang nhap that bai: userId {UserId} dang bi vo hieu hoa.", user.UserId);
                await WriteAuthAuditAsync(
                    request,
                    user,
                    roleNames,
                    "login_failed",
                    success: false,
                    "user_inactive");
                return Unauthorized("Tài khoản của bạn đã bị vô hiệu hóa.");
            }

            if (!user.EmailConfirmed)
            {
                _logger.LogInformation("Dang nhap bi chan: userId {UserId} chua xac minh email.", user.UserId);
                await WriteAuthAuditAsync(
                    request,
                    user,
                    roleNames,
                    "login_failed",
                    success: false,
                    "email_not_confirmed");
                return Unauthorized("Email của bạn chưa được xác minh. Vui lòng kiểm tra hộp thư và xác minh trước khi đăng nhập.");
            }

            var now = DateTime.UtcNow;
            if (user.UserAuth.LockedUntil.HasValue && user.UserAuth.LockedUntil.Value > now)
            {
                _logger.LogWarning(
                    "Dang nhap bi chan: userId {UserId} dang khoa tam thoi den {LockedUntil}.",
                    user.UserId,
                    user.UserAuth.LockedUntil.Value);
                await WriteAuthAuditAsync(
                    request,
                    user,
                    roleNames,
                    "login_locked",
                    success: false,
                    "account_locked",
                    user.UserAuth.FailedCount);
                return Unauthorized(BuildLockoutResponse(user.UserAuth.LockedUntil.Value, now));
            }

            if (user.UserAuth.LockedUntil.HasValue && user.UserAuth.LockedUntil.Value <= now)
            {
                user.UserAuth.LockedUntil = null;
                user.UserAuth.FailedCount = 0;
                user.UserAuth.UpdatedAt = now;
                await _db.SaveChangesAsync();
            }

            var verificationResult = _passwordHasher.VerifyHashedPassword(user, user.UserAuth.PasswordHash, request.Password);
            if (verificationResult == PasswordVerificationResult.Failed)
            {
                user.UserAuth.FailedCount += 1;
                user.UserAuth.UpdatedAt = now;

                if (user.UserAuth.FailedCount >= MaxFailedLoginAttempts)
                {
                    user.UserAuth.LockoutLevel = Math.Max(1, user.UserAuth.LockoutLevel + 1);
                    user.UserAuth.LockedUntil = now.Add(BuildLockoutDuration(user.UserAuth.LockoutLevel));
                    user.UserAuth.FailedCount = MaxFailedLoginAttempts;
                    await _db.SaveChangesAsync();

                    _logger.LogWarning(
                        "Tai khoan userId {UserId} bi khoa tam thoi den {LockedUntil} do dang nhap sai nhieu lan.",
                        user.UserId,
                        user.UserAuth.LockedUntil);
                    await WriteAuthAuditAsync(
                        request,
                        user,
                        roleNames,
                        "login_locked",
                        success: false,
                        "too_many_failed_passwords",
                        user.UserAuth.FailedCount);

                    return Unauthorized(BuildLockoutResponse(user.UserAuth.LockedUntil.Value, now));
                }

                await _db.SaveChangesAsync();
                _logger.LogWarning(
                    "Dang nhap that bai: userId {UserId} sai mat khau lan {FailedCount}/{MaxAttempts}.",
                    user.UserId,
                    user.UserAuth.FailedCount,
                    MaxFailedLoginAttempts);
                await WriteAuthAuditAsync(
                    request,
                    user,
                    roleNames,
                    "login_failed",
                    success: false,
                    "wrong_password",
                    user.UserAuth.FailedCount);
                return Unauthorized(BuildRemainingAttemptsMessage(user.UserAuth.FailedCount));
            }

            if (user.UserAuth.FailedCount > 0 || user.UserAuth.LockedUntil.HasValue || user.UserAuth.LockoutLevel > 0)
            {
                user.UserAuth.FailedCount = 0;
                user.UserAuth.LockedUntil = null;
                user.UserAuth.LockoutLevel = 0;
                user.UserAuth.UpdatedAt = now;
                await _db.SaveChangesAsync();
                _logger.LogInformation("Dang nhap thanh cong va da reset trang thai lockout cho userId {UserId}.", user.UserId);
            }
            else
            {
                _logger.LogInformation("Dang nhap thanh cong cho userId {UserId}.", user.UserId);
            }

            if (!IsRoleAllowedForLane(request.ClientLane!, roleNames))
            {
                _logger.LogWarning(
                    "Dang nhap bi tu choi: userId {UserId} khong co quyen vao lane {ClientLane}.",
                    user.UserId,
                    request.ClientLane);
                await WriteAuthAuditAsync(
                    request,
                    user,
                    roleNames,
                    "login_failed",
                    success: false,
                    "lane_access_denied",
                    user.UserAuth.FailedCount);
                return Unauthorized(BuildLaneAccessDeniedMessage(request.ClientLane!));
            }

            await WriteAuthAuditAsync(
                request,
                user,
                roleNames,
                "login_success",
                success: true,
                null,
                user.UserAuth.FailedCount);

            if (RequiresTwoFactor(roleNames))
            {
                var requiresSetup = string.IsNullOrWhiteSpace(user.UserAuth.Mfasecret);
                var setupSecret = requiresSetup ? _totpService.GenerateSecret() : null;
                var twoFactorTicket = _twoFactorLoginTicketService.CreateTicket(user.UserId, requiresSetup, setupSecret);
                var accountName = BuildTwoFactorAccountName(user);

                _logger.LogInformation(
                    "Dang nhap buoc 1 thanh cong cho userId {UserId}; yeu cau {Mode} 2FA.",
                    user.UserId,
                    requiresSetup ? "thiet lap" : "xac minh");

                return Ok(new AuthResponse
                {
                    RequiresTwoFactor = true,
                    RequiresTwoFactorSetup = requiresSetup,
                    TwoFactorTicket = twoFactorTicket,
                    ManualEntryKey = requiresSetup ? _totpService.FormatManualEntryKey(setupSecret!) : null,
                    OtpAuthUri = requiresSetup ? _totpService.BuildOtpAuthUri("FreshFarm", accountName, setupSecret!) : null,
                    AuthenticatorIssuer = "FreshFarm",
                    AuthenticatorAccountName = accountName,
                    ChallengeMessage = requiresSetup
                        ? "Vui lòng thêm mã bảo mật vào ứng dụng xác thực rồi nhập mã 6 số để hoàn tất đăng nhập."
                        : "Vui lòng nhập mã 6 số từ ứng dụng xác thực để tiếp tục đăng nhập."
                });
            }

            var token = CreateToken(user, roleNames);
            return Ok(token);
        }

        [HttpPost("login/2fa")]
        public async Task<IActionResult> VerifyTwoFactorLogin([FromBody] VerifyTwoFactorLoginRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Ticket) || string.IsNullOrWhiteSpace(request.Code))
            {
                if (string.IsNullOrWhiteSpace(request.Ticket))
                {
                    ModelState.AddModelError(nameof(request.Ticket), "Phiên xác thực hai bước là bắt buộc.");
                }

                if (string.IsNullOrWhiteSpace(request.Code))
                {
                    ModelState.AddModelError(nameof(request.Code), "Mã xác thực 6 số là bắt buộc.");
                }

                return ValidationProblem(ModelState);
            }

            if (!_twoFactorLoginTicketService.TryReadTicket(request.Ticket, out var twoFactorTicket) || twoFactorTicket is null)
            {
                _logger.LogWarning("Xac thuc 2FA that bai: ticket khong hop le hoac da het han.");
                return Unauthorized("Phiên xác thực hai bước đã hết hạn. Vui lòng đăng nhập lại.");
            }

            var user = await _db.Users
                .Include(u => u.UserAuth)
                .Include(u => u.UserRoles)
                .ThenInclude(ur => ur.Role)
                .SingleOrDefaultAsync(u => u.UserId == twoFactorTicket.UserId);

            if (user?.UserAuth is null || !user.IsActive)
            {
                _logger.LogWarning("Xac thuc 2FA that bai: khong tim thay user hop le cho ticket userId {UserId}.", twoFactorTicket.UserId);
                return Unauthorized("Tài khoản không còn hợp lệ. Vui lòng đăng nhập lại.");
            }

            var roleNames = GetRoleNames(user);

            if (!user.EmailConfirmed)
            {
                _logger.LogWarning("Xac thuc 2FA bi chan: userId {UserId} chua xac minh email.", user.UserId);
                return Unauthorized("Email của bạn chưa được xác minh.");
            }

            var now = DateTime.UtcNow;
            if (user.UserAuth.LockedUntil.HasValue && user.UserAuth.LockedUntil.Value > now)
            {
                _logger.LogWarning(
                    "Xac thuc 2FA bi chan: userId {UserId} dang khoa tam thoi den {LockedUntil}.",
                    user.UserId,
                    user.UserAuth.LockedUntil.Value);
                await WriteAuthAuditAsync(
                    new LoginRequest { Identifier = user.UserName, ClientLane = ResolveClientLane(roleNames) },
                    user,
                    roleNames,
                    "login_locked",
                    success: false,
                    "account_locked",
                    user.UserAuth.FailedCount);
                return Unauthorized(BuildLockoutMessage(user.UserAuth.LockedUntil.Value, now));
            }

            if (!RequiresTwoFactor(roleNames))
            {
                _logger.LogInformation("Xac thuc 2FA bo qua vi userId {UserId} khong thuoc lane Seller/Admin.", user.UserId);
                return Ok(CreateToken(user, roleNames));
            }

            var secret = twoFactorTicket.RequiresSetup
                ? (string.IsNullOrWhiteSpace(user.UserAuth.Mfasecret) ? twoFactorTicket.SetupSecret : user.UserAuth.Mfasecret)
                : user.UserAuth.Mfasecret;

            if (string.IsNullOrWhiteSpace(secret) || !_totpService.VerifyCode(secret, request.Code, now))
            {
                _logger.LogWarning("Xac thuc 2FA that bai cho userId {UserId}: ma OTP khong hop le.", user.UserId);
                await WriteAuthAuditAsync(
                    new LoginRequest { Identifier = user.UserName, ClientLane = ResolveClientLane(roleNames) },
                    user,
                    roleNames,
                    "two_factor_failed",
                    success: false,
                    "invalid_otp");
                return Unauthorized("Mã xác thực hai bước không đúng hoặc đã hết hạn.");
            }

            if (twoFactorTicket.RequiresSetup && string.IsNullOrWhiteSpace(user.UserAuth.Mfasecret))
            {
                user.UserAuth.Mfasecret = secret;
                user.UserAuth.UpdatedAt = now;
                user.UpdatedAt = now;
                await _db.SaveChangesAsync();

                _logger.LogInformation("Da kich hoat 2FA cho userId {UserId}.", user.UserId);
            }

            _logger.LogInformation("Xac thuc 2FA thanh cong cho userId {UserId}.", user.UserId);
            await WriteAuthAuditAsync(
                new LoginRequest { Identifier = user.UserName, ClientLane = ResolveClientLane(roleNames) },
                user,
                roleNames,
                "two_factor_success",
                success: true,
                null);
            return Ok(CreateToken(user, roleNames));
        }

        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] RegisterRequest request)
        {
            if (!ModelState.IsValid)
            {
                return ValidationProblem(ModelState);
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

            if (!TryNormalizeVietnamPhone(request.Phone, out var normalizedPhone, out var phoneValidationError))
            {
                return BadRequest(phoneValidationError);
            }

            if (!_accountEmailSender.IsConfigured)
            {
                return StatusCode(StatusCodes.Status503ServiceUnavailable, "Chưa cấu hình email xác minh tài khoản. Vui lòng thử lại sau.");
            }

            if (!TryBuildVerifyUrlBase(out var verifyUrlBase))
            {
                return StatusCode(StatusCodes.Status503ServiceUnavailable, "Chưa cấu hình đường dẫn VerifyUrlBase cho email xác minh tài khoản.");
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
                    Phone = normalizedPhone,
                    EmailConfirmed = false,
                    EmailConfirmedAt = null,
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

                var persistedUserForVerification = await _db.Users
                    .AsNoTracking()
                    .SingleAsync(x => x.UserId == user.UserId);

                var verificationEmailSent = false;
                var verificationMessage = "Tài khoản đã được tạo. Vui lòng kiểm tra email để xác minh trước khi đăng nhập.";

                try
                {
                    var token = _emailVerificationTokenService.GenerateToken(persistedUserForVerification);
                    var verifyUrl = QueryHelpers.AddQueryString(verifyUrlBase, new Dictionary<string, string?>
                    {
                        ["email"] = persistedUserForVerification.Email,
                        ["token"] = token
                    });

                    await _accountEmailSender.SendEmailVerificationAsync(
                        persistedUserForVerification.Email,
                        persistedUserForVerification.FullName,
                        verifyUrl,
                        ResolveEmailVerificationTokenLifetimeMinutes());

                    verificationEmailSent = true;
                    _logger.LogInformation("Da tao tai khoan userId {UserId} va gui email xac minh.", user.UserId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Da tao tai khoan userId {UserId} nhung gui email xac minh that bai.", user.UserId);
                    verificationMessage = "Tài khoản đã được tạo nhưng chưa gửi được email xác minh. Vui lòng dùng chức năng gửi lại email xác minh.";
                }

                return Ok(new
                {
                    user.UserId,
                    user.UserName,
                    user.Email,
                    role.RoleName,
                    EmailVerificationRequired = true,
                    VerificationEmailSent = verificationEmailSent,
                    Message = verificationMessage
                });
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(
                    ex,
                    "Dang ky that bai cho email {Email}, userName {UserName}, phone {Phone}.",
                    MaskIdentifier(request.Email),
                    request.UserName?.Trim(),
                    request.Phone?.Trim());
                return StatusCode(500, "Có lỗi xảy ra khi đăng kí, vui lòng thử lại");
            }
        }

        [HttpPost("external-login")]
        public async Task<IActionResult> ExternalLogin([FromBody] ExternalLoginRequest request, CancellationToken cancellationToken)
        {
            if (!ModelState.IsValid)
            {
                _logger.LogWarning("External login that bai do request model khong hop le.");
                await WriteExternalAuthAuditAsync(
                    request,
                    null,
                    null,
                    "external_login_failed",
                    success: false,
                    failureReason: "invalid_request",
                    cancellationToken: cancellationToken);
                return ValidationProblem(ModelState);
            }

            var provider = request.Provider.Trim();
            if (!provider.Equals("Google", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("External login that bai: provider {Provider} khong duoc ho tro.", provider);
                await WriteExternalAuthAuditAsync(
                    request,
                    null,
                    null,
                    "external_login_failed",
                    success: false,
                    failureReason: "unsupported_provider",
                    cancellationToken: cancellationToken);
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
                _logger.LogWarning("Google external login that bai: userId {UserId} dang bi vo hieu hoa.", user.UserId);
                await WriteExternalAuthAuditAsync(
                    request,
                    user,
                    user.UserRoles.Select(x => x.Role?.RoleName).Where(x => !string.IsNullOrWhiteSpace(x)).Cast<string>().ToArray(),
                    "external_login_failed",
                    success: false,
                    failureReason: "inactive_account",
                    cancellationToken: cancellationToken);
                return Unauthorized("Tài khoản của bạn đã bị vô hiệu hóa.");
            }

            if (user?.UserAuth?.LockedUntil.HasValue == true && user.UserAuth.LockedUntil.Value > DateTime.UtcNow)
            {
                _logger.LogWarning(
                    "Google external login bi chan: userId {UserId} dang khoa tam thoi den {LockedUntil}.",
                    user.UserId,
                    user.UserAuth.LockedUntil.Value);
                await WriteExternalAuthAuditAsync(
                    request,
                    user,
                    user.UserRoles.Select(x => x.Role?.RoleName).Where(x => !string.IsNullOrWhiteSpace(x)).Cast<string>().ToArray(),
                    "login_locked",
                    success: false,
                    failureReason: "account_locked",
                    cancellationToken: cancellationToken);
                return Unauthorized(BuildLockoutMessage(user.UserAuth.LockedUntil.Value, DateTime.UtcNow));
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
                        EmailConfirmed = true,
                        EmailConfirmedAt = now,
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

                if (!user.EmailConfirmed)
                {
                    user.EmailConfirmed = true;
                    user.EmailConfirmedAt = DateTime.UtcNow;
                    shouldSave = true;
                    _logger.LogInformation("Da tu dong xac minh email cho userId {UserId} qua Google external login.", user.UserId);
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

            await WriteExternalAuthAuditAsync(
                request,
                user,
                roleNames,
                "external_login_success",
                success: true,
                failureReason: null,
                cancellationToken: cancellationToken);

            return Ok(CreateToken(user, roleNames));
        }

        [HttpPost("resend-email-verification")]
        public async Task<IActionResult> ResendEmailVerification([FromBody] ResendEmailVerificationRequest request, CancellationToken cancellationToken)
        {
            if (!ModelState.IsValid)
            {
                return ValidationProblem(ModelState);
            }

            if (!_accountEmailSender.IsConfigured || !TryBuildVerifyUrlBase(out var verifyUrlBase))
            {
                return StatusCode(StatusCodes.Status503ServiceUnavailable, "Chưa cấu hình email xác minh tài khoản.");
            }

            var identifier = request.Identifier.Trim();
            var isEmail = identifier.Contains("@");
            var user = await _db.Users
                .Include(x => x.UserAuth)
                .SingleOrDefaultAsync(x => isEmail ? x.Email == identifier : x.UserName == identifier, cancellationToken);

            if (user is not null && user.IsActive && !user.EmailConfirmed)
            {
                try
                {
                    var token = _emailVerificationTokenService.GenerateToken(user);
                    var verifyUrl = QueryHelpers.AddQueryString(verifyUrlBase, new Dictionary<string, string?>
                    {
                        ["email"] = user.Email,
                        ["token"] = token
                    });

                    await _accountEmailSender.SendEmailVerificationAsync(
                        user.Email,
                        user.FullName,
                        verifyUrl,
                        ResolveEmailVerificationTokenLifetimeMinutes(),
                        cancellationToken);

                    _logger.LogInformation("Da gui lai email xac minh cho userId {UserId}.", user.UserId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Gui lai email xac minh that bai cho identifier {Identifier}.", MaskIdentifier(identifier));
                    return StatusCode(StatusCodes.Status500InternalServerError, "Không thể gửi lại email xác minh lúc này. Vui lòng thử lại sau.");
                }
            }
            else
            {
                _logger.LogInformation(
                    "Nhan yeu cau resend-email-verification cho identifier {Identifier} nhung khong can gui lai.",
                    MaskIdentifier(identifier));
            }

            return Ok(new
            {
                success = true,
                message = "Nếu tài khoản tồn tại và chưa xác minh, chúng tôi đã gửi lại email xác minh."
            });
        }

        [HttpPost("verify-email")]
        public async Task<IActionResult> VerifyEmail([FromBody] VerifyEmailRequest request, CancellationToken cancellationToken)
        {
            if (!ModelState.IsValid)
            {
                return ValidationProblem(ModelState);
            }

            var normalizedEmail = request.Email.Trim();
            var user = await _db.Users
                .SingleOrDefaultAsync(x => x.Email == normalizedEmail, cancellationToken);

            if (user is null || !user.IsActive)
            {
                _logger.LogWarning("Xac minh email that bai: khong tim thay tai khoan active cho email {Email}.", MaskIdentifier(normalizedEmail));
                return BadRequest("Liên kết xác minh email không hợp lệ.");
            }

            if (user.EmailConfirmed)
            {
                return Ok(new
                {
                    success = true,
                    alreadyConfirmed = true,
                    message = "Email của bạn đã được xác minh trước đó."
                });
            }

            if (!_emailVerificationTokenService.TryValidateToken(request.Token, user, out var tokenError))
            {
                _logger.LogWarning(
                    "Xac minh email that bai cho userId {UserId}. Ly do: {TokenError}",
                    user.UserId,
                    tokenError ?? "unknown");
                return BadRequest(tokenError ?? "Liên kết xác minh email không hợp lệ.");
            }

            user.EmailConfirmed = true;
            user.EmailConfirmedAt = DateTime.UtcNow;
            user.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Da xac minh email cho userId {UserId}.", user.UserId);
            return Ok(new
            {
                success = true,
                message = "Xác minh email thành công. Bạn có thể đăng nhập."
            });
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
            else
            {
                _logger.LogInformation(
                    "Nhan yeu cau forgot-password cho email {Email} nhung khong tim thay tai khoan active phu hop.",
                    MaskIdentifier(normalizedEmail));
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
                _logger.LogWarning(
                    "Reset password that bai: email {Email} khong ton tai hoac tai khoan khong active.",
                    MaskIdentifier(normalizedEmail));
                return BadRequest("Liên kết đặt lại mật khẩu không hợp lệ.");
            }

            if (!_passwordResetTokenService.TryValidateToken(request.Token, user, user.UserAuth, out var tokenError))
            {
                _logger.LogWarning("Reset password that bai: token khong hop le cho userId {UserId}.", user.UserId);
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

        private static bool RequiresTwoFactor(IEnumerable<string> roles)
        {
            // 2FA is currently disabled for all roles by product decision.
            return false;
        }

        private static string BuildTwoFactorAccountName(User user)
        {
            if (!string.IsNullOrWhiteSpace(user.Email))
            {
                return user.Email.Trim();
            }

            return string.IsNullOrWhiteSpace(user.UserName)
                ? $"user-{user.UserId}"
                : user.UserName.Trim();
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
            if (!TryNormalizeVietnamPhone(request.Phone, out var normalizedPhone, out var phoneValidationError))
            {
                return BadRequest(phoneValidationError);
            }

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

        private List<string> GetRoleNames(User user)
        {
            return user.UserRoles
                .Select(ur => ur.Role?.RoleName)
                .Where(roleName => !string.IsNullOrWhiteSpace(roleName))
                .Select(roleName => roleName!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static string ResolveClientLane(IEnumerable<string> roleNames)
        {
            if (roleNames.Any(x => string.Equals(x, "Admin", StringComparison.OrdinalIgnoreCase)))
            {
                return "Admin";
            }

            if (roleNames.Any(x => string.Equals(x, "Seller", StringComparison.OrdinalIgnoreCase)))
            {
                return "Seller";
            }

            if (roleNames.Any(x => string.Equals(x, "Customer", StringComparison.OrdinalIgnoreCase)))
            {
                return "Customer";
            }

            return "Unknown";
        }

        private static bool IsRoleAllowedForLane(string clientLane, IEnumerable<string> roleNames)
        {
            if (string.Equals(clientLane, "Admin", StringComparison.OrdinalIgnoreCase))
            {
                return roleNames.Any(x => string.Equals(x, "Admin", StringComparison.OrdinalIgnoreCase));
            }

            if (string.Equals(clientLane, "Seller", StringComparison.OrdinalIgnoreCase))
            {
                return roleNames.Any(x =>
                    string.Equals(x, "Seller", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(x, "Admin", StringComparison.OrdinalIgnoreCase));
            }

            return true;
        }

        private static string BuildLaneAccessDeniedMessage(string clientLane)
        {
            if (string.Equals(clientLane, "Admin", StringComparison.OrdinalIgnoreCase))
            {
                return "Bạn không có quyền truy cập khu vực quản trị.";
            }

            if (string.Equals(clientLane, "Seller", StringComparison.OrdinalIgnoreCase))
            {
                return "Bạn không có quyền truy cập khu vực nhà bán hàng.";
            }

            return "Bạn không có quyền truy cập khu vực này.";
        }

        private bool TryValidateLoginRequest(LoginRequest request, out IActionResult? validationProblem)
        {
            validationProblem = null;

            if (string.IsNullOrWhiteSpace(request.Identifier))
            {
                ModelState.AddModelError(nameof(request.Identifier), "Email hoặc tên đăng nhập là bắt buộc.");
            }

            if (string.IsNullOrWhiteSpace(request.Password))
            {
                ModelState.AddModelError(nameof(request.Password), "Mật khẩu là bắt buộc.");
            }

            if (string.IsNullOrWhiteSpace(request.ClientLane))
            {
                ModelState.AddModelError(nameof(request.ClientLane), "Loại luồng đăng nhập là bắt buộc.");
            }

            if (ModelState.IsValid)
            {
                return true;
            }

            validationProblem = ValidationProblem(ModelState);
            return false;
        }

        private Task WriteAuthAuditAsync(
            LoginRequest request,
            User? user,
            IReadOnlyCollection<string>? roleNames,
            string eventType,
            bool success,
            string? failureReason,
            int? failedAttemptCount = null,
            CancellationToken cancellationToken = default)
        {
            return _authAuditService.WriteAsync(
                new AuthAuditWriteRequest
                {
                    HttpContext = HttpContext,
                    ClientLane = request.ClientLane,
                    User = user,
                    RoleNames = roleNames,
                    Identifier = request.Identifier?.Trim() ?? string.Empty,
                    EventType = eventType,
                    Success = success,
                    FailureReason = failureReason,
                    FailedAttemptCount = failedAttemptCount
                },
                cancellationToken);
        }

        private Task WriteExternalAuthAuditAsync(
            ExternalLoginRequest request,
            User? user,
            IReadOnlyCollection<string>? roleNames,
            string eventType,
            bool success,
            string? failureReason,
            CancellationToken cancellationToken = default)
        {
            return _authAuditService.WriteAsync(
                new AuthAuditWriteRequest
                {
                    HttpContext = HttpContext,
                    ClientLane = ResolveClientLane(roleNames ?? user?.UserRoles?
                        .Select(x => x.Role?.RoleName)
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .Cast<string>()
                        .ToArray() ?? []),
                    User = user,
                    RoleNames = roleNames,
                    Identifier = request.Email?.Trim() ?? string.Empty,
                    EventType = eventType,
                    Success = success,
                    FailureReason = failureReason
                },
                cancellationToken);
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

        private bool TryBuildVerifyUrlBase(out string verifyUrlBase)
        {
            verifyUrlBase = _emailVerificationOptions.VerifyUrlBase?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(verifyUrlBase))
            {
                return false;
            }

            return Uri.TryCreate(verifyUrlBase, UriKind.Absolute, out _);
        }

        private int ResolveTokenLifetimeMinutes()
        {
            return _passwordResetOptions.TokenLifetimeMinutes <= 0
                ? 30
                : _passwordResetOptions.TokenLifetimeMinutes;
        }

        private int ResolveEmailVerificationTokenLifetimeMinutes()
        {
            return _emailVerificationOptions.TokenLifetimeMinutes <= 0
                ? 60
                : _emailVerificationOptions.TokenLifetimeMinutes;
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

        private static object BuildLockoutResponse(DateTime lockedUntilUtc, DateTime nowUtc)
            => new
            {
                message = BuildLockoutMessage(lockedUntilUtc, nowUtc),
                lockedUntilUtc
            };

        private static TimeSpan BuildLockoutDuration(int lockoutLevel)
        {
            var normalizedLevel = Math.Max(1, lockoutLevel);
            var multiplier = Math.Pow(2, normalizedLevel - 1);
            return TimeSpan.FromMinutes(BaseLockoutDurationMinutes * multiplier);
        }

        private static string BuildLockoutMessage(DateTime lockedUntilUtc, DateTime nowUtc)
        {
            var remaining = lockedUntilUtc - nowUtc;
            if (remaining <= TimeSpan.Zero)
            {
                return "Tài khoản đang bị khóa tạm thời. Vui lòng thử lại sau.";
            }

            var roundedMinutes = Math.Max(1, (int)Math.Ceiling(remaining.TotalMinutes));
            return $"Tài khoản đang bị khóa tạm thời. Vui lòng thử lại sau {roundedMinutes} phút.";
        }

        private static string BuildRemainingAttemptsMessage(int failedCount)
        {
            var remainingAttempts = Math.Max(0, MaxFailedLoginAttempts - failedCount);
            if (remainingAttempts <= 0)
            {
                return "Tài khoản đã bị khóa tạm thời do nhập sai mật khẩu quá nhiều lần.";
            }

            return $"Tài khoản hoặc mật khẩu không đúng. Bạn còn {remainingAttempts} lần thử trước khi tài khoản bị khóa tạm thời.";
        }

        private static string MaskIdentifier(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return "unknown";
            }

            if (raw.Contains('@'))
            {
                var parts = raw.Split('@', 2);
                var local = parts[0];
                var domain = parts.Length > 1 ? parts[1] : string.Empty;
                var visible = local.Length <= 2 ? local : local[..2];
                return $"{visible}***@{domain}";
            }

            if (raw.Length <= 2)
            {
                return raw;
            }

            return $"{raw[..2]}***";
        }

        private static bool TryNormalizeVietnamPhone(string? rawPhone, out string normalizedPhone, out string errorMessage)
        {
            normalizedPhone = string.Empty;
            errorMessage = "Số điện thoại phải đúng định dạng Việt Nam.";

            var trimmed = rawPhone?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                errorMessage = "Số điện thoại không được để trống.";
                return false;
            }

            if (!VietnamPhoneRegex.IsMatch(trimmed))
            {
                errorMessage = "Số điện thoại phải là số di động Việt Nam hợp lệ gồm 10 số, hoặc bắt đầu bằng +84 và đủ 9 số phía sau.";
                return false;
            }

            normalizedPhone = trimmed.StartsWith("+84", StringComparison.Ordinal)
                ? $"0{trimmed[3..]}"
                : trimmed;

            return true;
        }
    }
}
