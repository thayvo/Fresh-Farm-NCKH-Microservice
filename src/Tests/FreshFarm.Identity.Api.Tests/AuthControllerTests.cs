using FreshFarm.Identity.Api.Controllers;
using FreshFarm.Identity.Api.Dtos;
using FreshFarm.Identity.Api.Models;
using FreshFarm.Identity.Api.Options;
using FreshFarm.Identity.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.JsonWebTokens;
using Xunit;

namespace FreshFarm.Identity.Api.Tests;

public sealed class AuthControllerTests
{
    [Fact]
    public async Task Login_ReturnsValidationProblem_WhenRequiredFieldsMissing()
    {
        await using var db = CreateDbContext();
        var controller = CreateController(db);

        var result = await controller.Login(new LoginRequest
        {
            Identifier = "",
            Password = "",
            ClientLane = null
        });

        Assert.IsType<ObjectResult>(result);
        Assert.False(controller.ModelState.IsValid);
        Assert.True(controller.ModelState.ContainsKey(nameof(LoginRequest.Identifier)));
        Assert.True(controller.ModelState.ContainsKey(nameof(LoginRequest.Password)));
        Assert.True(controller.ModelState.ContainsKey(nameof(LoginRequest.ClientLane)));
    }

    [Fact]
    public async Task Login_ReturnsUnauthorized_WhenEmailNotConfirmed()
    {
        await using var db = CreateDbContext();
        await SeedUserAsync(db, userId: 1, roleName: "Customer", password: "Secret123!", emailConfirmed: false);
        var controller = CreateController(db);

        var result = await controller.Login(new LoginRequest
        {
            Identifier = "customer1",
            Password = "Secret123!",
            ClientLane = "Customer"
        });

        var unauthorized = Assert.IsType<UnauthorizedObjectResult>(result);
        Assert.Equal("Email của bạn chưa được xác minh. Vui lòng kiểm tra hộp thư và xác minh trước khi đăng nhập.", unauthorized.Value);
    }

    [Fact]
    public async Task Login_ReturnsUnauthorized_WhenLaneAccessDenied()
    {
        await using var db = CreateDbContext();
        await SeedUserAsync(db, userId: 2, roleName: "Customer", password: "Secret123!", emailConfirmed: true);
        var controller = CreateController(db);

        var result = await controller.Login(new LoginRequest
        {
            Identifier = "customer2",
            Password = "Secret123!",
            ClientLane = "Seller"
        });

        var unauthorized = Assert.IsType<UnauthorizedObjectResult>(result);
        Assert.Equal("Bạn không có quyền truy cập khu vực nhà bán hàng.", unauthorized.Value);
    }

    [Fact]
    public async Task Login_ReturnsUnauthorized_WithRemainingAttempts_WhenPasswordIncorrect()
    {
        await using var db = CreateDbContext();
        await SeedUserAsync(db, userId: 3, roleName: "Customer", password: "Secret123!", emailConfirmed: true);
        var controller = CreateController(db);

        var result = await controller.Login(new LoginRequest
        {
            Identifier = "customer3",
            Password = "WrongPassword!",
            ClientLane = "Customer"
        });

        var unauthorized = Assert.IsType<UnauthorizedObjectResult>(result);
        Assert.Equal("Tài khoản hoặc mật khẩu không đúng. Bạn còn 4 lần thử trước khi tài khoản bị khóa tạm thời.", unauthorized.Value);

        var userAuth = await db.UserAuths.SingleAsync(x => x.UserId == 3);
        Assert.Equal(1, userAuth.FailedCount);
        Assert.Null(userAuth.LockedUntil);
    }

    [Fact]
    public async Task Login_ReturnsLockoutPayload_WhenFailedAttemptsReachThreshold()
    {
        await using var db = CreateDbContext();
        await SeedUserAsync(
            db,
            userId: 4,
            roleName: "Customer",
            password: "Secret123!",
            emailConfirmed: true,
            failedCount: 4);
        var controller = CreateController(db);

        var result = await controller.Login(new LoginRequest
        {
            Identifier = "customer4",
            Password = "WrongPassword!",
            ClientLane = "Customer"
        });

        var unauthorized = Assert.IsType<UnauthorizedObjectResult>(result);
        using var json = System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(unauthorized.Value));
        var message = json.RootElement.GetProperty("message").GetString();
        var lockedUntilUtc = json.RootElement.GetProperty("lockedUntilUtc").GetDateTime();

        Assert.Contains("Tài khoản đang bị khóa tạm thời.", message);
        Assert.True(lockedUntilUtc > DateTime.UtcNow);

        var userAuth = await db.UserAuths.SingleAsync(x => x.UserId == 4);
        Assert.Equal(5, userAuth.FailedCount);
        Assert.Equal(1, userAuth.LockoutLevel);
        Assert.True(userAuth.LockedUntil > DateTime.UtcNow);
    }

    [Theory]
    [InlineData("Seller")]
    [InlineData("Admin")]
    public async Task Login_ReturnsToken_WhenTwoFactorDisabledForPrivilegedLane(string roleName)
    {
        await using var db = CreateDbContext();
        await SeedUserAsync(db, userId: 5, roleName: roleName, password: "Secret123!", emailConfirmed: true);
        var twoFactorTickets = new FakeTwoFactorLoginTicketService();
        var totpService = new FakeTotpService();
        var controller = CreateController(db, totpService: totpService, twoFactorLoginTicketService: twoFactorTickets);

        var result = await controller.Login(new LoginRequest
        {
            Identifier = "customer5",
            Password = "Secret123!",
            ClientLane = roleName
        });

        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<AuthResponse>(ok.Value);

        Assert.False(response.RequiresTwoFactor);
        Assert.False(response.RequiresTwoFactorSetup);
        Assert.Null(response.TwoFactorTicket);
        Assert.Null(response.ManualEntryKey);
        Assert.Null(response.OtpAuthUri);
        Assert.False(string.IsNullOrWhiteSpace(response.AccessToken));
    }

    [Fact]
    public async Task VerifyTwoFactorLogin_ReturnsUnauthorized_WhenTicketInvalid()
    {
        await using var db = CreateDbContext();
        await SeedUserAsync(db, userId: 6, roleName: "Seller", password: "Secret123!", emailConfirmed: true);
        var controller = CreateController(db, twoFactorLoginTicketService: new FakeTwoFactorLoginTicketService
        {
            TicketToRead = null
        });

        var result = await controller.VerifyTwoFactorLogin(new VerifyTwoFactorLoginRequest
        {
            Ticket = "expired-ticket",
            Code = "123456"
        });

        var unauthorized = Assert.IsType<UnauthorizedObjectResult>(result);
        Assert.Equal("Phiên xác thực hai bước đã hết hạn. Vui lòng đăng nhập lại.", unauthorized.Value);
    }

    [Fact]
    public async Task VerifyTwoFactorLogin_ReturnsValidationProblem_WhenTicketAndCodeMissing()
    {
        await using var db = CreateDbContext();
        var controller = CreateController(db);

        var result = await controller.VerifyTwoFactorLogin(new VerifyTwoFactorLoginRequest
        {
            Ticket = "",
            Code = ""
        });

        var objectResult = Assert.IsType<ObjectResult>(result);
        var problem = Assert.IsType<ValidationProblemDetails>(objectResult.Value);
        Assert.Contains(nameof(VerifyTwoFactorLoginRequest.Ticket), problem.Errors.Keys);
        Assert.Contains(nameof(VerifyTwoFactorLoginRequest.Code), problem.Errors.Keys);
        Assert.False(controller.ModelState.IsValid);
        Assert.True(controller.ModelState.ContainsKey(nameof(VerifyTwoFactorLoginRequest.Ticket)));
        Assert.True(controller.ModelState.ContainsKey(nameof(VerifyTwoFactorLoginRequest.Code)));
    }

    [Fact]
    public async Task VerifyTwoFactorLogin_ReturnsToken_WhenTwoFactorDisabledForSeller()
    {
        await using var db = CreateDbContext();
        await SeedUserAsync(db, userId: 7, roleName: "Seller", password: "Secret123!", emailConfirmed: true, mfaSecret: "SELLER-SECRET");
        var controller = CreateController(
            db,
            totpService: new FakeTotpService { VerifyCodeResult = false },
            twoFactorLoginTicketService: new FakeTwoFactorLoginTicketService
            {
                TicketToRead = new TwoFactorLoginTicket
                {
                    UserId = 7,
                    RequiresSetup = false,
                    ExpiresAtUtc = DateTime.UtcNow.AddMinutes(5)
                }
            });

        var result = await controller.VerifyTwoFactorLogin(new VerifyTwoFactorLoginRequest
        {
            Ticket = "valid-ticket",
            Code = "000000"
        });

        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<AuthResponse>(ok.Value);
        Assert.False(response.RequiresTwoFactor);
        Assert.False(string.IsNullOrWhiteSpace(response.AccessToken));
    }

    [Fact]
    public async Task VerifyTwoFactorLogin_ReturnsToken_WhenRoleDoesNotRequireTwoFactor()
    {
        await using var db = CreateDbContext();
        await SeedUserAsync(db, userId: 8, roleName: "Customer", password: "Secret123!", emailConfirmed: true);
        var controller = CreateController(
            db,
            twoFactorLoginTicketService: new FakeTwoFactorLoginTicketService
            {
                TicketToRead = new TwoFactorLoginTicket
                {
                    UserId = 8,
                    RequiresSetup = false,
                    ExpiresAtUtc = DateTime.UtcNow.AddMinutes(5)
                }
            });

        var result = await controller.VerifyTwoFactorLogin(new VerifyTwoFactorLoginRequest
        {
            Ticket = "customer-ticket",
            Code = "123456"
        });

        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<AuthResponse>(ok.Value);
        Assert.False(response.RequiresTwoFactor);
        Assert.False(string.IsNullOrWhiteSpace(response.AccessToken));
    }

    [Fact]
    public async Task VerifyEmail_ReturnsBadRequest_WhenTokenInvalid()
    {
        await using var db = CreateDbContext();
        await SeedUserAsync(db, userId: 9, roleName: "Customer", password: "Secret123!", emailConfirmed: false);
        var controller = CreateController(
            db,
            emailVerificationTokenService: new FakeEmailVerificationTokenService
            {
                TryValidateResult = false,
                ErrorMessage = "Token xác minh đã hết hạn."
            });

        var result = await controller.VerifyEmail(new VerifyEmailRequest
        {
            Email = "customer9@example.com",
            Token = "expired-token"
        }, CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("Token xác minh đã hết hạn.", badRequest.Value);

        var user = await db.Users.SingleAsync(x => x.UserId == 9);
        Assert.False(user.EmailConfirmed);
        Assert.Null(user.EmailConfirmedAt);
    }

    [Fact]
    public async Task VerifyEmail_ReturnsAlreadyConfirmedPayload_WhenEmailAlreadyConfirmed()
    {
        await using var db = CreateDbContext();
        await SeedUserAsync(db, userId: 10, roleName: "Customer", password: "Secret123!", emailConfirmed: true);
        var controller = CreateController(db);

        var result = await controller.VerifyEmail(new VerifyEmailRequest
        {
            Email = "customer10@example.com",
            Token = "any-token"
        }, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        using var json = System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(ok.Value));
        Assert.True(json.RootElement.GetProperty("success").GetBoolean());
        Assert.True(json.RootElement.GetProperty("alreadyConfirmed").GetBoolean());
        Assert.Equal("Email của bạn đã được xác minh trước đó.", json.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task ForgotPassword_ReturnsServiceUnavailable_WhenEmailSenderNotConfigured()
    {
        await using var db = CreateDbContext();
        var controller = CreateController(
            db,
            accountEmailSender: new FakeAccountEmailSender
            {
                IsConfiguredValue = false
            });

        var result = await controller.ForgotPassword(new ForgotPasswordRequest
        {
            Email = "customer11@example.com"
        }, CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, objectResult.StatusCode);
        Assert.Equal("Chưa cấu hình kênh email đặt lại mật khẩu. Cần cập nhật SMTP hoặc Gmail sender.", objectResult.Value);
    }

    [Fact]
    public async Task ForgotPassword_ReturnsValidationProblem_WhenModelStateInvalid()
    {
        await using var db = CreateDbContext();
        var controller = CreateController(db);
        controller.ModelState.AddModelError(nameof(ForgotPasswordRequest.Email), "Email không được để trống.");

        var result = await controller.ForgotPassword(new ForgotPasswordRequest
        {
            Email = string.Empty
        }, CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        var problem = Assert.IsType<ValidationProblemDetails>(objectResult.Value);
        Assert.Contains(nameof(ForgotPasswordRequest.Email), problem.Errors.Keys);
        Assert.True(controller.ModelState.ContainsKey(nameof(ForgotPasswordRequest.Email)));
    }

    [Fact]
    public async Task ForgotPassword_ReturnsServiceUnavailable_WhenResetUrlBaseMissing()
    {
        await using var db = CreateDbContext();
        var controller = CreateController(
            db,
            passwordResetUrlBase: "");

        var result = await controller.ForgotPassword(new ForgotPasswordRequest
        {
            Email = "customer11@example.com"
        }, CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, objectResult.StatusCode);
        Assert.Equal("Chưa cấu hình đường dẫn ResetUrlBase cho email đặt lại mật khẩu.", objectResult.Value);
    }

    [Fact]
    public async Task ResetPassword_ReturnsBadRequest_WhenTokenInvalid()
    {
        await using var db = CreateDbContext();
        await SeedUserAsync(
            db,
            userId: 12,
            roleName: "Customer",
            password: "Secret123!",
            emailConfirmed: true,
            failedCount: 3,
            lockoutLevel: 1,
            lockedUntil: DateTime.UtcNow.AddMinutes(10));
        var controller = CreateController(
            db,
            passwordResetTokenService: new FakePasswordResetTokenService
            {
                TryValidateResult = false,
                ErrorMessage = "Token đặt lại mật khẩu không hợp lệ."
            });

        var result = await controller.ResetPassword(new ResetPasswordRequest
        {
            Email = "customer12@example.com",
            Token = "invalid-token",
            NewPassword = "NewSecret123!",
            ConfirmPassword = "NewSecret123!"
        }, CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("Token đặt lại mật khẩu không hợp lệ.", badRequest.Value);

        var userAuth = await db.UserAuths.SingleAsync(x => x.UserId == 12);
        Assert.Equal(3, userAuth.FailedCount);
        Assert.Equal(1, userAuth.LockoutLevel);
        Assert.True(userAuth.LockedUntil > DateTime.UtcNow);
    }

    [Fact]
    public async Task VerifyEmail_ConfirmsEmail_WhenTokenValid()
    {
        await using var db = CreateDbContext();
        await SeedUserAsync(db, userId: 13, roleName: "Customer", password: "Secret123!", emailConfirmed: false);
        var controller = CreateController(db);

        var result = await controller.VerifyEmail(new VerifyEmailRequest
        {
            Email = "customer13@example.com",
            Token = "valid-token"
        }, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        using var json = System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(ok.Value));
        Assert.True(json.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("Xác minh email thành công. Bạn có thể đăng nhập.", json.RootElement.GetProperty("message").GetString());

        var user = await db.Users.SingleAsync(x => x.UserId == 13);
        Assert.True(user.EmailConfirmed);
        Assert.NotNull(user.EmailConfirmedAt);
        Assert.NotNull(user.UpdatedAt);
    }

    [Fact]
    public async Task ResetPassword_ResetsPasswordAndClearsLockout_WhenTokenValid()
    {
        await using var db = CreateDbContext();
        await SeedUserAsync(
            db,
            userId: 14,
            roleName: "Customer",
            password: "Secret123!",
            emailConfirmed: true,
            failedCount: 4,
            lockoutLevel: 2,
            lockedUntil: DateTime.UtcNow.AddMinutes(20));
        var controller = CreateController(db);

        var result = await controller.ResetPassword(new ResetPasswordRequest
        {
            Email = "customer14@example.com",
            Token = "valid-reset-token",
            NewPassword = "NewSecret123!",
            ConfirmPassword = "NewSecret123!"
        }, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        using var json = System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(ok.Value));
        Assert.True(json.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("Đặt lại mật khẩu thành công.", json.RootElement.GetProperty("message").GetString());

        var user = await db.Users.Include(x => x.UserAuth).SingleAsync(x => x.UserId == 14);
        var userAuth = Assert.IsType<UserAuth>(user.UserAuth);
        var hasher = new PasswordHasher<User>();
        Assert.Equal(PasswordVerificationResult.Success, hasher.VerifyHashedPassword(user, userAuth.PasswordHash, "NewSecret123!"));
        Assert.Equal(0, userAuth.FailedCount);
        Assert.Null(userAuth.LockedUntil);
        Assert.True(userAuth.UpdatedAt > DateTime.MinValue);
        Assert.True(user.UpdatedAt > DateTime.MinValue);
    }

    [Fact]
    public async Task ResendEmailVerification_SendsVerificationEmail_WhenUserExistsAndUnconfirmed()
    {
        await using var db = CreateDbContext();
        await SeedUserAsync(db, userId: 15, roleName: "Customer", password: "Secret123!", emailConfirmed: false);
        var emailSender = new FakeAccountEmailSender();
        var emailVerificationTokenService = new FakeEmailVerificationTokenService
        {
            GeneratedToken = "verify-token-15"
        };
        var controller = CreateController(
            db,
            emailVerificationTokenService: emailVerificationTokenService,
            accountEmailSender: emailSender);

        var result = await controller.ResendEmailVerification(new ResendEmailVerificationRequest
        {
            Identifier = "customer15"
        }, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        using var json = System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(ok.Value));
        Assert.True(json.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("Nếu tài khoản tồn tại và chưa xác minh, chúng tôi đã gửi lại email xác minh.", json.RootElement.GetProperty("message").GetString());

        Assert.Equal("customer15@example.com", emailSender.LastVerificationEmailTo);
        Assert.Equal("Customer 15", emailSender.LastVerificationEmailName);
        Assert.Equal(60, emailSender.LastVerificationExpiresInMinutes);
        Assert.Equal("https://freshfarm.test/verify-email?email=customer15@example.com&token=verify-token-15", emailSender.LastVerificationUrl);
    }

    [Fact]
    public async Task ForgotPassword_SendsResetEmail_WhenUserExists()
    {
        await using var db = CreateDbContext();
        await SeedUserAsync(db, userId: 17, roleName: "Customer", password: "Secret123!", emailConfirmed: true);
        var emailSender = new FakeAccountEmailSender();
        var passwordResetTokenService = new FakePasswordResetTokenService
        {
            GeneratedToken = "reset-token-17"
        };
        var controller = CreateController(
            db,
            passwordResetTokenService: passwordResetTokenService,
            accountEmailSender: emailSender);

        var result = await controller.ForgotPassword(new ForgotPasswordRequest
        {
            Email = "customer17@example.com"
        }, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        using var json = System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(ok.Value));
        Assert.True(json.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("Nếu email tồn tại trong hệ thống, chúng tôi đã gửi hướng dẫn đặt lại mật khẩu.", json.RootElement.GetProperty("message").GetString());

        Assert.Equal("customer17@example.com", emailSender.LastResetEmailTo);
        Assert.Equal("Customer 17", emailSender.LastResetEmailName);
        Assert.Equal(30, emailSender.LastResetExpiresInMinutes);
        Assert.Equal("https://freshfarm.test/reset-password?email=customer17@example.com&token=reset-token-17", emailSender.LastResetUrl);
    }

    [Fact]
    public async Task ForgotPassword_ReturnsGenericSuccessWithoutSendingEmail_WhenUserDoesNotExist()
    {
        await using var db = CreateDbContext();
        var emailSender = new FakeAccountEmailSender();
        var controller = CreateController(db, accountEmailSender: emailSender);

        var result = await controller.ForgotPassword(new ForgotPasswordRequest
        {
            Email = "missing-user@example.com"
        }, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        using var json = System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(ok.Value));
        Assert.True(json.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("Nếu email tồn tại trong hệ thống, chúng tôi đã gửi hướng dẫn đặt lại mật khẩu.", json.RootElement.GetProperty("message").GetString());
        Assert.Null(emailSender.LastResetEmailTo);
        Assert.Null(emailSender.LastResetUrl);
    }

    [Fact]
    public async Task ResendEmailVerification_DoesNotSendEmail_WhenUserAlreadyConfirmed()
    {
        await using var db = CreateDbContext();
        await SeedUserAsync(db, userId: 18, roleName: "Customer", password: "Secret123!", emailConfirmed: true);
        var emailSender = new FakeAccountEmailSender();
        var controller = CreateController(db, accountEmailSender: emailSender);

        var result = await controller.ResendEmailVerification(new ResendEmailVerificationRequest
        {
            Identifier = "customer18@example.com"
        }, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        using var json = System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(ok.Value));
        Assert.True(json.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("Nếu tài khoản tồn tại và chưa xác minh, chúng tôi đã gửi lại email xác minh.", json.RootElement.GetProperty("message").GetString());
        Assert.Null(emailSender.LastVerificationEmailTo);
        Assert.Null(emailSender.LastVerificationUrl);
    }

    [Fact]
    public async Task ResendEmailVerification_ReturnsGenericSuccessWithoutSendingEmail_WhenUserDoesNotExist()
    {
        await using var db = CreateDbContext();
        var emailSender = new FakeAccountEmailSender();
        var controller = CreateController(db, accountEmailSender: emailSender);

        var result = await controller.ResendEmailVerification(new ResendEmailVerificationRequest
        {
            Identifier = "missing-user@example.com"
        }, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        using var json = System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(ok.Value));
        Assert.True(json.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("Nếu tài khoản tồn tại và chưa xác minh, chúng tôi đã gửi lại email xác minh.", json.RootElement.GetProperty("message").GetString());
        Assert.Null(emailSender.LastVerificationEmailTo);
        Assert.Null(emailSender.LastVerificationUrl);
    }

    [Fact]
    public async Task ResendEmailVerification_ReturnsValidationProblem_WhenModelStateInvalid()
    {
        await using var db = CreateDbContext();
        var controller = CreateController(db);
        controller.ModelState.AddModelError(nameof(ResendEmailVerificationRequest.Identifier), "Identifier là bắt buộc.");

        var result = await controller.ResendEmailVerification(new ResendEmailVerificationRequest
        {
            Identifier = string.Empty
        }, CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        var problem = Assert.IsType<ValidationProblemDetails>(objectResult.Value);
        Assert.Contains(nameof(ResendEmailVerificationRequest.Identifier), problem.Errors.Keys);
        Assert.True(controller.ModelState.ContainsKey(nameof(ResendEmailVerificationRequest.Identifier)));
    }

    [Fact]
    public async Task ResendEmailVerification_ReturnsServerError_WhenEmailSenderThrows()
    {
        await using var db = CreateDbContext();
        await SeedUserAsync(db, userId: 19, roleName: "Customer", password: "Secret123!", emailConfirmed: false);
        var controller = CreateController(
            db,
            accountEmailSender: new FakeAccountEmailSender
            {
                ThrowOnVerificationSend = true
            });

        var result = await controller.ResendEmailVerification(new ResendEmailVerificationRequest
        {
            Identifier = "customer19"
        }, CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status500InternalServerError, objectResult.StatusCode);
        Assert.Equal("Không thể gửi lại email xác minh lúc này. Vui lòng thử lại sau.", objectResult.Value);
    }

    [Fact]
    public async Task ResendEmailVerification_ReturnsServiceUnavailable_WhenVerifyUrlBaseMissing()
    {
        await using var db = CreateDbContext();
        await SeedUserAsync(db, userId: 21, roleName: "Customer", password: "Secret123!", emailConfirmed: false);
        var controller = CreateController(
            db,
            emailVerificationUrlBase: "");

        var result = await controller.ResendEmailVerification(new ResendEmailVerificationRequest
        {
            Identifier = "customer21@example.com"
        }, CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, objectResult.StatusCode);
        Assert.Equal("Chưa cấu hình email xác minh tài khoản.", objectResult.Value);
    }

    [Fact]
    public async Task Register_CreatesUserAndSendsVerificationEmail_WhenRequestValid()
    {
        await using var db = CreateDbContext();
        await SeedRoleAsync(db, roleId: 100, roleName: "Customer");
        var emailSender = new FakeAccountEmailSender();
        var emailVerificationTokenService = new FakeEmailVerificationTokenService
        {
            GeneratedToken = "register-verify-token"
        };
        var controller = CreateController(
            db,
            emailVerificationTokenService: emailVerificationTokenService,
            accountEmailSender: emailSender);

        var result = await controller.Register(new RegisterRequest
        {
            UserName = "newcustomer",
            FullName = "New Customer",
            Email = "newcustomer@example.com",
            Phone = "0912345678",
            Password = "Secret123!",
            ConfirmPassword = "Secret123!",
            RoleName = "Customer"
        });

        var ok = Assert.IsType<OkObjectResult>(result);
        using var json = System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(ok.Value));
        Assert.True(json.RootElement.GetProperty("EmailVerificationRequired").GetBoolean());
        Assert.True(json.RootElement.GetProperty("VerificationEmailSent").GetBoolean());
        Assert.Equal("Tài khoản đã được tạo. Vui lòng kiểm tra email để xác minh trước khi đăng nhập.", json.RootElement.GetProperty("Message").GetString());
        Assert.Equal("newcustomer@example.com", json.RootElement.GetProperty("Email").GetString());
        Assert.Equal("newcustomer", json.RootElement.GetProperty("UserName").GetString());
        Assert.Equal("Customer", json.RootElement.GetProperty("RoleName").GetString());

        var user = await db.Users.Include(x => x.UserAuth).Include(x => x.UserRoles).SingleAsync(x => x.Email == "newcustomer@example.com");
        Assert.Equal("newcustomer", user.UserName);
        Assert.Equal("New Customer", user.FullName);
        Assert.Equal("0912345678", user.Phone);
        Assert.False(user.EmailConfirmed);
        Assert.True(user.IsActive);
        Assert.Single(user.UserRoles);
        Assert.Equal(100, user.UserRoles.Single().RoleId);
        Assert.NotNull(user.UserAuth);

        Assert.Equal("newcustomer@example.com", emailSender.LastVerificationEmailTo);
        Assert.Equal("New Customer", emailSender.LastVerificationEmailName);
        Assert.Equal(60, emailSender.LastVerificationExpiresInMinutes);
        Assert.Equal("https://freshfarm.test/verify-email?email=newcustomer@example.com&token=register-verify-token", emailSender.LastVerificationUrl);
    }

    [Fact]
    public async Task Register_ReturnsFallbackMessage_WhenVerificationEmailFails()
    {
        await using var db = CreateDbContext();
        await SeedRoleAsync(db, roleId: 101, roleName: "Customer");
        var controller = CreateController(
            db,
            accountEmailSender: new FakeAccountEmailSender
            {
                ThrowOnVerificationSend = true
            });

        var result = await controller.Register(new RegisterRequest
        {
            UserName = "emailfailuser",
            FullName = "Email Fail User",
            Email = "emailfail@example.com",
            Phone = "0987654321",
            Password = "Secret123!",
            ConfirmPassword = "Secret123!",
            RoleName = "Customer"
        });

        var ok = Assert.IsType<OkObjectResult>(result);
        using var json = System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(ok.Value));
        Assert.True(json.RootElement.GetProperty("EmailVerificationRequired").GetBoolean());
        Assert.False(json.RootElement.GetProperty("VerificationEmailSent").GetBoolean());
        Assert.Equal("Tài khoản đã được tạo nhưng chưa gửi được email xác minh. Vui lòng dùng chức năng gửi lại email xác minh.", json.RootElement.GetProperty("Message").GetString());

        var user = await db.Users.Include(x => x.UserAuth).Include(x => x.UserRoles).SingleAsync(x => x.Email == "emailfail@example.com");
        Assert.Equal("emailfailuser", user.UserName);
        Assert.False(user.EmailConfirmed);
        Assert.NotNull(user.UserAuth);
        Assert.Single(user.UserRoles);
        Assert.Equal(101, user.UserRoles.Single().RoleId);
    }

    [Fact]
    public async Task Register_ReturnsValidationProblem_WhenModelStateInvalid()
    {
        await using var db = CreateDbContext();
        var controller = CreateController(db);
        controller.ModelState.AddModelError(nameof(RegisterRequest.Email), "Email không đúng định dạng.");

        var result = await controller.Register(new RegisterRequest
        {
            UserName = "badregister",
            FullName = "Bad Register",
            Email = "invalid-email",
            Phone = "0912345678",
            Password = "Secret123!",
            ConfirmPassword = "Secret123!",
            RoleName = "Customer"
        });

        var objectResult = Assert.IsType<ObjectResult>(result);
        var problem = Assert.IsType<ValidationProblemDetails>(objectResult.Value);
        Assert.Contains(nameof(RegisterRequest.Email), problem.Errors.Keys);
        Assert.True(controller.ModelState.ContainsKey(nameof(RegisterRequest.Email)));
    }

    [Fact]
    public async Task ExternalLogin_ReturnsValidationProblem_WhenModelStateInvalid()
    {
        await using var db = CreateDbContext();
        var controller = CreateController(db);
        controller.ModelState.AddModelError(nameof(ExternalLoginRequest.Provider), "Provider là bắt buộc.");

        var result = await controller.ExternalLogin(new ExternalLoginRequest
        {
            Provider = string.Empty,
            Email = "user@example.com",
            FullName = "External User"
        }, CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        var problem = Assert.IsType<ValidationProblemDetails>(objectResult.Value);
        Assert.Contains(nameof(ExternalLoginRequest.Provider), problem.Errors.Keys);
    }

    [Fact]
    public async Task ExternalLogin_ReturnsBadRequest_WhenProviderUnsupported()
    {
        await using var db = CreateDbContext();
        var controller = CreateController(db);

        var result = await controller.ExternalLogin(new ExternalLoginRequest
        {
            Provider = "Facebook",
            Email = "user@example.com",
            FullName = "External User"
        }, CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("Hiện tại hệ thống chỉ hỗ trợ đăng nhập Google.", badRequest.Value);
    }

    [Fact]
    public async Task ExternalLogin_ReturnsServerError_WhenCustomerRoleMissingForNewUser()
    {
        await using var db = CreateDbContext();
        var controller = CreateController(db);

        var result = await controller.ExternalLogin(new ExternalLoginRequest
        {
            Provider = "Google",
            Email = "google-new@example.com",
            FullName = "Google New User",
            AvatarUrl = "https://avatar.test/google-new.png"
        }, CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status500InternalServerError, objectResult.StatusCode);
        Assert.Equal("Không tìm thấy vai trò Customer để tạo tài khoản Google.", objectResult.Value);
    }

    [Fact]
    public async Task ExternalLogin_CreatesCustomerUserAndReturnsToken_WhenGoogleUserDoesNotExist()
    {
        await using var db = CreateDbContext();
        await SeedRoleAsync(db, roleId: 105, roleName: "Customer");
        var controller = CreateController(db);

        var result = await controller.ExternalLogin(new ExternalLoginRequest
        {
            Provider = "Google",
            Email = "google-create@example.com",
            FullName = "Google Create User",
            AvatarUrl = "https://avatar.test/google-create.png"
        }, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<AuthResponse>(ok.Value);
        Assert.False(response.RequiresTwoFactor);
        Assert.False(string.IsNullOrWhiteSpace(response.AccessToken));

        var user = await db.Users.Include(x => x.UserAuth).Include(x => x.UserRoles).SingleAsync(x => x.Email == "google-create@example.com");
        Assert.Equal("Google Create User", user.FullName);
        Assert.Equal("https://avatar.test/google-create.png", user.Avatar);
        Assert.True(user.EmailConfirmed);
        Assert.NotNull(user.EmailConfirmedAt);
        Assert.True(user.IsActive);
        Assert.NotNull(user.UserAuth);
        Assert.Single(user.UserRoles);
        Assert.Equal(105, user.UserRoles.Single().RoleId);
        Assert.Matches(@"^0\d{9}$", user.Phone);
        Assert.False(string.IsNullOrWhiteSpace(user.UserName));
    }

    [Fact]
    public async Task ExternalLogin_ReturnsUnauthorized_WhenExistingGoogleUserIsInactive()
    {
        await using var db = CreateDbContext();
        await SeedUserAsync(
            db,
            userId: 22,
            roleName: "Customer",
            password: "Secret123!",
            emailConfirmed: true,
            isActive: false);
        var controller = CreateController(db);

        var result = await controller.ExternalLogin(new ExternalLoginRequest
        {
            Provider = "Google",
            Email = "customer22@example.com",
            FullName = "Inactive Google User"
        }, CancellationToken.None);

        var unauthorized = Assert.IsType<UnauthorizedObjectResult>(result);
        Assert.Equal("Tài khoản của bạn đã bị vô hiệu hóa.", unauthorized.Value);
    }

    [Fact]
    public async Task ExternalLogin_ReturnsUnauthorized_WhenExistingGoogleUserLocked()
    {
        await using var db = CreateDbContext();
        await SeedUserAsync(
            db,
            userId: 23,
            roleName: "Customer",
            password: "Secret123!",
            emailConfirmed: true,
            lockedUntil: DateTime.UtcNow.AddMinutes(15));
        var controller = CreateController(db);

        var result = await controller.ExternalLogin(new ExternalLoginRequest
        {
            Provider = "Google",
            Email = "customer23@example.com",
            FullName = "Locked Google User"
        }, CancellationToken.None);

        var unauthorized = Assert.IsType<UnauthorizedObjectResult>(result);
        var message = Assert.IsType<string>(unauthorized.Value);
        Assert.Contains("Tài khoản đang bị khóa tạm thời.", message);
    }

    [Fact]
    public async Task ExternalLogin_UpdatesExistingUserProfileAndConfirmsEmail_WhenGoogleUserExists()
    {
        await using var db = CreateDbContext();
        await SeedUserAsync(
            db,
            userId: 24,
            roleName: "Customer",
            password: "Secret123!",
            emailConfirmed: false,
            fullName: "Old Name",
            avatar: null);
        var controller = CreateController(db);

        var result = await controller.ExternalLogin(new ExternalLoginRequest
        {
            Provider = "Google",
            Email = "customer24@example.com",
            FullName = "Google Updated Name",
            AvatarUrl = "https://avatar.test/customer24.png"
        }, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<AuthResponse>(ok.Value);
        Assert.False(string.IsNullOrWhiteSpace(response.AccessToken));

        var user = await db.Users.Include(x => x.UserRoles).SingleAsync(x => x.UserId == 24);
        Assert.Equal("Google Updated Name", user.FullName);
        Assert.Equal("https://avatar.test/customer24.png", user.Avatar);
        Assert.True(user.EmailConfirmed);
        Assert.NotNull(user.EmailConfirmedAt);
        Assert.NotNull(user.UpdatedAt);
        Assert.Single(user.UserRoles);
    }

    [Fact]
    public async Task Register_ReturnsServiceUnavailable_WhenVerifyUrlBaseMissing()
    {
        await using var db = CreateDbContext();
        await SeedRoleAsync(db, roleId: 104, roleName: "Customer");
        var controller = CreateController(
            db,
            emailVerificationUrlBase: "");

        var result = await controller.Register(new RegisterRequest
        {
            UserName = "verifyurlmissing",
            FullName = "Verify Url Missing",
            Email = "verifyurlmissing@example.com",
            Phone = "0912345679",
            Password = "Secret123!",
            ConfirmPassword = "Secret123!",
            RoleName = "Customer"
        });

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, objectResult.StatusCode);
        Assert.Equal("Chưa cấu hình đường dẫn VerifyUrlBase cho email xác minh tài khoản.", objectResult.Value);
        Assert.False(await db.Users.AnyAsync(x => x.Email == "verifyurlmissing@example.com"));
    }

    [Fact]
    public async Task Register_ReturnsConflict_WhenEmailUserNameOrPhoneAlreadyExists()
    {
        await using var db = CreateDbContext();
        await SeedRoleAsync(db, roleId: 102, roleName: "Customer");
        await SeedUserAsync(db, userId: 20, roleName: "Customer", password: "Secret123!", emailConfirmed: true);
        var controller = CreateController(db);

        var result = await controller.Register(new RegisterRequest
        {
            UserName = "customer20",
            FullName = "Conflict User",
            Email = "customer20@example.com",
            Phone = "0123456720",
            Password = "Secret123!",
            ConfirmPassword = "Secret123!",
            RoleName = "Customer"
        });

        var conflict = Assert.IsType<ConflictObjectResult>(result);
        Assert.Equal("Email, số điện thoại hoặc tên đăng nhập đã tồn tại.", conflict.Value);
        Assert.Equal(1, await db.Users.CountAsync(x => x.Email == "customer20@example.com"));
    }

    [Fact]
    public async Task Register_ReturnsBadRequest_WhenPhoneInvalid()
    {
        await using var db = CreateDbContext();
        await SeedRoleAsync(db, roleId: 103, roleName: "Customer");
        var controller = CreateController(db);

        var result = await controller.Register(new RegisterRequest
        {
            UserName = "invalidphoneuser",
            FullName = "Invalid Phone User",
            Email = "invalidphone@example.com",
            Phone = "12345",
            Password = "Secret123!",
            ConfirmPassword = "Secret123!",
            RoleName = "Customer"
        });

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("Số điện thoại phải là số di động Việt Nam hợp lệ gồm 10 số, hoặc bắt đầu bằng +84 và đủ 9 số phía sau.", badRequest.Value);
        Assert.False(await db.Users.AnyAsync(x => x.Email == "invalidphone@example.com"));
    }

    [Fact]
    public async Task Register_ReturnsBadRequest_WhenRoleNotFound()
    {
        await using var db = CreateDbContext();
        var controller = CreateController(db);

        var result = await controller.Register(new RegisterRequest
        {
            UserName = "missingroleuser",
            FullName = "Missing Role User",
            Email = "missingrole@example.com",
            Phone = "0911222333",
            Password = "Secret123!",
            ConfirmPassword = "Secret123!",
            RoleName = "Seller"
        });

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("Vai trò Seller không tìm thấy.", badRequest.Value);
        Assert.False(await db.Users.AnyAsync(x => x.Email == "missingrole@example.com"));
    }

    [Fact]
    public async Task Login_ReturnsJwtContainingExpectedClaims_WhenCustomerLoginSucceeds()
    {
        await using var db = CreateDbContext();
        await SeedUserAsync(db, userId: 16, roleName: "Customer", password: "Secret123!", emailConfirmed: true);
        var controller = CreateController(db);

        var result = await controller.Login(new LoginRequest
        {
            Identifier = "customer16",
            Password = "Secret123!",
            ClientLane = "Customer"
        });

        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<AuthResponse>(ok.Value);
        Assert.False(response.RequiresTwoFactor);
        Assert.False(string.IsNullOrWhiteSpace(response.AccessToken));
        Assert.True(response.ExpiredAtUtc > DateTime.UtcNow);

        var jwt = new JsonWebToken(response.AccessToken);
        Assert.Equal("16", jwt.Subject);
        Assert.Equal("customer16@example.com", jwt.Claims.Single(x => x.Type == JwtRegisteredClaimNames.Email).Value);
        Assert.Equal("customer16", jwt.Claims.Single(x => x.Type == "username").Value);
        Assert.Contains("Customer", jwt.Claims.Where(x => x.Type.EndsWith("/role", StringComparison.OrdinalIgnoreCase)).Select(x => x.Value));
    }

    private static AuthController CreateController(
        FreshFarmIdentityDBContext db,
        IPasswordResetTokenService? passwordResetTokenService = null,
        IEmailVerificationTokenService? emailVerificationTokenService = null,
        ITotpService? totpService = null,
        ITwoFactorLoginTicketService? twoFactorLoginTicketService = null,
        IAccountEmailSender? accountEmailSender = null,
        string? passwordResetUrlBase = "https://freshfarm.test/reset-password",
        string? emailVerificationUrlBase = "https://freshfarm.test/verify-email")
    {
        return new AuthController(
            db,
            new PasswordHasher<User>(),
            BuildConfiguration(),
            passwordResetTokenService ?? new FakePasswordResetTokenService(),
            emailVerificationTokenService ?? new FakeEmailVerificationTokenService(),
            totpService ?? new FakeTotpService(),
            twoFactorLoginTicketService ?? new FakeTwoFactorLoginTicketService(),
            accountEmailSender ?? new FakeAccountEmailSender(),
            new FakeAuthAuditService(),
            Microsoft.Extensions.Options.Options.Create(new PasswordResetOptions
            {
                ResetUrlBase = passwordResetUrlBase ?? string.Empty,
                TokenLifetimeMinutes = 30
            }),
            Microsoft.Extensions.Options.Options.Create(new EmailVerificationOptions
            {
                VerifyUrlBase = emailVerificationUrlBase ?? string.Empty,
                TokenLifetimeMinutes = 60
            }),
            NullLogger<AuthController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
    }

    private static IConfiguration BuildConfiguration()
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Key"] = "this-is-a-test-jwt-key-with-at-least-32-bytes",
                ["Jwt:Issuer"] = "FreshFarm.Tests",
                ["Jwt:Audience"] = "FreshFarm.Tests"
            })
            .Build();
    }

    private static FreshFarmIdentityDBContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<FreshFarmIdentityDBContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new FreshFarmIdentityDBContext(options);
    }

    private static async Task SeedUserAsync(
        FreshFarmIdentityDBContext db,
        int userId,
        string roleName,
        string password,
        bool emailConfirmed,
        int failedCount = 0,
        int lockoutLevel = 0,
        DateTime? lockedUntil = null,
        string? mfaSecret = null,
        bool isActive = true,
        string? fullName = null,
        string? avatar = null)
    {
        var role = new Role
        {
            RoleId = userId,
            RoleName = roleName,
            Description = roleName,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        var user = new User
        {
            UserId = userId,
            UserName = $"customer{userId}",
            FullName = fullName ?? $"Customer {userId}",
            Email = $"customer{userId}@example.com",
            Phone = $"01234567{userId:00}",
            EmailConfirmed = emailConfirmed,
            IsActive = isActive,
            Avatar = avatar,
            CreatedAt = DateTime.UtcNow
        };

        var hasher = new PasswordHasher<User>();
        var userAuth = new UserAuth
        {
            UserId = userId,
            PasswordHash = hasher.HashPassword(user, password),
            FailedCount = failedCount,
            LockoutLevel = lockoutLevel,
            LockedUntil = lockedUntil,
            Mfasecret = mfaSecret,
            UpdatedAt = DateTime.UtcNow,
            User = user
        };

        var userRole = new UserRole
        {
            UserId = userId,
            RoleId = role.RoleId,
            CreatedAt = DateTime.UtcNow,
            User = user,
            Role = role
        };

        user.UserAuth = userAuth;
        user.UserRoles.Add(userRole);
        role.UserRoles.Add(userRole);

        db.Roles.Add(role);
        db.Users.Add(user);
        db.UserAuths.Add(userAuth);
        db.UserRoles.Add(userRole);
        await db.SaveChangesAsync();
    }

    private static async Task SeedRoleAsync(
        FreshFarmIdentityDBContext db,
        int roleId,
        string roleName)
    {
        db.Roles.Add(new Role
        {
            RoleId = roleId,
            RoleName = roleName,
            Description = roleName,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        });

        await db.SaveChangesAsync();
    }

    private sealed class FakePasswordResetTokenService : IPasswordResetTokenService
    {
        public bool TryValidateResult { get; set; } = true;

        public string? ErrorMessage { get; set; }

        public string GeneratedToken { get; set; } = "reset-token";

        public string GenerateToken(User user, UserAuth? userAuth) => GeneratedToken;

        public bool TryValidateToken(string token, User user, UserAuth? userAuth, out string? errorMessage)
        {
            errorMessage = ErrorMessage;
            return TryValidateResult;
        }
    }

    private sealed class FakeEmailVerificationTokenService : IEmailVerificationTokenService
    {
        public bool TryValidateResult { get; set; } = true;

        public string? ErrorMessage { get; set; }

        public string GeneratedToken { get; set; } = "verify-token";

        public string GenerateToken(User user) => GeneratedToken;

        public bool TryValidateToken(string token, User user, out string? errorMessage)
        {
            errorMessage = ErrorMessage;
            return TryValidateResult;
        }
    }

    private sealed class FakeTotpService : ITotpService
    {
        public bool VerifyCodeResult { get; set; } = true;

        public string GenerateSecret() => "SECRET";

        public string FormatManualEntryKey(string secret) => $"MANUAL-{secret}";

        public string BuildOtpAuthUri(string issuer, string accountName, string secret) => $"otpauth://{issuer}/{accountName}";

        public bool VerifyCode(string secret, string code, DateTime utcNow) => VerifyCodeResult;
    }

    private sealed class FakeTwoFactorLoginTicketService : ITwoFactorLoginTicketService
    {
        public TwoFactorLoginTicket? TicketToRead { get; set; }

        public string CreateTicket(int userId, bool requiresSetup, string? setupSecret) => "ticket";

        public bool TryReadTicket(string protectedTicket, out TwoFactorLoginTicket? ticket)
        {
            ticket = TicketToRead;
            return ticket is not null;
        }
    }

    private sealed class FakeAccountEmailSender : IAccountEmailSender
    {
        public bool IsConfiguredValue { get; set; } = true;

        public bool IsConfigured => IsConfiguredValue;

        public string? LastResetEmailTo { get; private set; }

        public string? LastResetEmailName { get; private set; }

        public string? LastResetUrl { get; private set; }

        public int? LastResetExpiresInMinutes { get; private set; }

        public string? LastVerificationEmailTo { get; private set; }

        public string? LastVerificationEmailName { get; private set; }

        public string? LastVerificationUrl { get; private set; }

        public int? LastVerificationExpiresInMinutes { get; private set; }

        public bool ThrowOnResetSend { get; set; }

        public bool ThrowOnVerificationSend { get; set; }

        public bool ThrowOnSellerReviewSend { get; set; }

        public Task SendPasswordResetEmailAsync(string toEmail, string? toName, string resetUrl, int expiresInMinutes, CancellationToken cancellationToken = default)
        {
            if (ThrowOnResetSend)
            {
                throw new InvalidOperationException("Simulated reset email failure.");
            }

            LastResetEmailTo = toEmail;
            LastResetEmailName = toName;
            LastResetUrl = resetUrl;
            LastResetExpiresInMinutes = expiresInMinutes;
            return Task.CompletedTask;
        }

        public Task SendEmailVerificationAsync(string toEmail, string? toName, string verifyUrl, int expiresInMinutes, CancellationToken cancellationToken = default)
        {
            if (ThrowOnVerificationSend)
            {
                throw new InvalidOperationException("Simulated verification email failure.");
            }

            LastVerificationEmailTo = toEmail;
            LastVerificationEmailName = toName;
            LastVerificationUrl = verifyUrl;
            LastVerificationExpiresInMinutes = expiresInMinutes;
            return Task.CompletedTask;
        }

        public Task SendSellerApplicationReviewAsync(string toEmail, string? toName, string? storeName, bool isApproved, string? reviewNote, CancellationToken cancellationToken = default)
        {
            if (ThrowOnSellerReviewSend)
            {
                throw new InvalidOperationException("Simulated seller review email failure.");
            }

            return Task.CompletedTask;
        }
    }

    private sealed class FakeAuthAuditService : IAuthAuditService
    {
        public Task WriteAsync(AuthAuditWriteRequest request, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
