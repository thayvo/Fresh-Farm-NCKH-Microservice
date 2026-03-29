using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using FreshFarm.Identity.Api.Controllers;
using FreshFarm.Identity.Api.Dtos;
using FreshFarm.Identity.Api.Models;
using FreshFarm.Identity.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FreshFarm.Identity.Api.Tests;

public sealed class SellerApplicationControllerTests
{
    [Fact]
    public async Task Upsert_CreatesSellerApplicationAndKycProfile_WhenRequestValid()
    {
        await using var db = CreateDbContext();
        await SeedRoleAsync(db, roleId: 1, roleName: "Customer");
        await SeedUserAsync(db, userId: 201, roleId: 1);

        var controller = CreateSellerApplicationController(db, userId: 201);

        var result = await controller.Upsert(CreateValidRequest(), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<SellerApplicationResponseDto>(ok.Value);
        Assert.True(response.HasApplication);
        Assert.False(response.IsSellerApproved);
        Assert.Equal("pending", response.Status);
        Assert.Equal("Fresh Farm Test Shop", response.StoreName);
        Assert.Equal("0987654321", response.StorePhone);
        Assert.Equal("0987654322", response.PickupPhone);
        Assert.True(response.Kyc.HasKycProfile);
        Assert.True(response.Kyc.HasIdentityDocuments);
        Assert.Equal("123456789012", response.Kyc.IdentityNumber);
        Assert.Equal("********9012", response.Kyc.IdentityNumberMasked);

        var sellerStore = await db.SellerStoreSettings.SingleAsync(x => x.UserId == 201);
        Assert.Equal("Fresh Farm Test Shop", sellerStore.StoreName);
        Assert.Equal("0987654321", sellerStore.StorePhone);
        Assert.Equal("0987654322", sellerStore.GhnPickupPhone);

        var kyc = await db.SellerKycProfiles.SingleAsync(x => x.UserId == 201);
        Assert.Equal("Nguyen Van Seller", kyc.LegalFullName);
        Assert.Equal("123456789012", kyc.IdentityNumber);
        Assert.Equal("/uploads/seller-kyc/front.jpg", kyc.CitizenIdFrontUrl);
        Assert.Equal("/uploads/seller-kyc/back.jpg", kyc.CitizenIdBackUrl);
    }

    [Fact]
    public async Task GetCurrent_ReturnsKycSummary_WhenApplicationExists()
    {
        await using var db = CreateDbContext();
        await SeedRoleAsync(db, roleId: 1, roleName: "Customer");
        await SeedUserAsync(db, userId: 202, roleId: 1);
        await SeedSellerApplicationAsync(db, userId: 202, includeCompleteKyc: true);
        db.SellerKycReviewEvents.Add(new SellerKycReviewEvent
        {
            UserId = 202,
            Action = "reject",
            ReviewStatus = "rejected",
            Note = "Thiếu ảnh mặt sau CCCD.",
            ReviewedAt = DateTime.UtcNow.AddHours(-2),
            ReviewedByUserId = 3,
            ReviewerUserName = "admin.a",
            ReviewerFullName = "Admin A"
        });
        db.SellerKycReviewEvents.Add(new SellerKycReviewEvent
        {
            UserId = 202,
            Action = "approve",
            ReviewStatus = "approved",
            Note = null,
            ReviewedAt = DateTime.UtcNow.AddHours(-1),
            ReviewedByUserId = 3,
            ReviewerUserName = "admin.a",
            ReviewerFullName = "Admin A"
        });
        await db.SaveChangesAsync();

        var controller = CreateSellerApplicationController(db, userId: 202);

        var result = await controller.GetCurrent(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<SellerApplicationResponseDto>(ok.Value);
        Assert.True(response.HasApplication);
        Assert.Equal("pending", response.Status);
        Assert.Equal("Đang chờ duyệt", response.ReviewStatusLabel);
        Assert.True(response.Kyc.HasKycProfile);
        Assert.True(response.Kyc.HasIdentityDocuments);
        Assert.Equal("********9012", response.Kyc.IdentityNumberMasked);
        Assert.Equal("/uploads/seller-kyc/front.jpg", response.Kyc.CitizenIdFrontUrl);
        Assert.Equal("/uploads/seller-kyc/license.pdf", response.Kyc.BusinessLicenseUrl);
        Assert.Equal(2, response.ReviewHistory.Count);
        Assert.Equal("approve", response.ReviewHistory[0].Action);
        Assert.Equal("Đã duyệt", response.ReviewHistory[0].ReviewStatusLabel);
        Assert.Equal("Admin A", response.ReviewHistory[0].ReviewerFullName);
        Assert.Equal("reject", response.ReviewHistory[1].Action);
        Assert.Equal("Thiếu ảnh mặt sau CCCD.", response.ReviewHistory[1].Note);
    }

    [Fact]
    public async Task GetCurrent_UsesLatestSellerStoreSetting_WhenDuplicateRowsExist()
    {
        await using var db = CreateDbContext();
        await SeedRoleAsync(db, roleId: 1, roleName: "Customer");
        await SeedUserAsync(db, userId: 211, roleId: 1);
        await SeedSellerApplicationAsync(db, userId: 211, includeCompleteKyc: true);

        var controller = CreateSellerApplicationController(
            db,
            userId: 211,
            sellerStoreSettingsResolver: new StubSellerStoreSettingsResolver(new SellerStoreSetting
            {
                SellerStoreSettingId = 999,
                UserId = 211,
                StoreName = "Fresh Farm Latest Shop",
                StoreAddress = "99 Nguyen Hue",
                StoreEmail = "latestshop@example.com",
                StorePhone = "0977777777",
                IsCodenabled = true,
                BankTransferInstructions = "Latest transfer",
                BankAccountInfo = "Latest bank",
                DefaultShippingFee = 45000m,
                FreeShippingThreshold = 650000m,
                IsEmailNewOrderEnabled = true,
                IsEmailDeliveredEnabled = true,
                IsEmailCancelledEnabled = true,
                AdminNotificationEmail = "latestshop@example.com",
                GhnPickupName = "Latest Pickup",
                GhnPickupPhone = "0977777778",
                GhnPickupAddress = "99 Nguyen Hue",
                CreatedAt = DateTime.UtcNow.AddMinutes(-5),
                UpdatedAt = DateTime.UtcNow.AddMinutes(5)
            }));

        var result = await controller.GetCurrent(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<SellerApplicationResponseDto>(ok.Value);
        Assert.Equal("Fresh Farm Latest Shop", response.StoreName);
        Assert.Equal("0977777777", response.StorePhone);
        Assert.Equal("0977777778", response.PickupPhone);
        Assert.Equal("latestshop@example.com", response.AdminNotificationEmail);
    }

    [Fact]
    public async Task ApproveSeller_ReturnsBadRequest_WhenKycIncomplete()
    {
        await using var db = CreateDbContext();
        await SeedRoleAsync(db, roleId: 1, roleName: "Customer");
        await SeedRoleAsync(db, roleId: 2, roleName: "Seller");
        await SeedUserAsync(db, userId: 203, roleId: 1);
        await SeedSellerApplicationAsync(db, userId: 203, includeCompleteKyc: false);

        var controller = CreateAdminMerchantsController(db);

        var result = await controller.ApproveSeller(203, CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("Hồ sơ KYC chưa đầy đủ. Cần đủ thông tin CCCD và ảnh CCCD hai mặt trước khi duyệt người bán.", badRequest.Value);
        Assert.False(await db.UserRoles.AnyAsync(x => x.UserId == 203 && x.RoleId == 2));
    }

    [Fact]
    public async Task ApproveSeller_AssignsSellerRole_WhenKycComplete()
    {
        await using var db = CreateDbContext();
        await SeedRoleAsync(db, roleId: 1, roleName: "Customer");
        await SeedRoleAsync(db, roleId: 2, roleName: "Seller");
        await SeedUserAsync(db, userId: 204, roleId: 1);
        await SeedSellerApplicationAsync(db, userId: 204, includeCompleteKyc: true);
        var emailSender = new FakeAccountEmailSender();

        var controller = CreateAdminMerchantsController(db, emailSender);

        var result = await controller.ApproveSeller(204, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        using var json = System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(ok.Value));
        Assert.Equal(204, json.RootElement.GetProperty("sellerId").GetInt32());
        Assert.Equal("Đã duyệt hồ sơ và cấp quyền người bán.", json.RootElement.GetProperty("message").GetString());
        Assert.True(await db.UserRoles.AnyAsync(x => x.UserId == 204 && x.RoleId == 2));
        var kyc = await db.SellerKycProfiles.SingleAsync(x => x.UserId == 204);
        Assert.Equal("approved", kyc.ReviewStatus);
        Assert.Null(kyc.ReviewNote);
        Assert.NotNull(kyc.ReviewedAt);
        var reviewEvent = await db.SellerKycReviewEvents.SingleAsync(x => x.UserId == 204);
        Assert.Equal("approve", reviewEvent.Action);
        Assert.Equal("approved", reviewEvent.ReviewStatus);
        Assert.Equal("sellerapp204@example.com", emailSender.LastSellerReviewEmailTo);
        Assert.True(emailSender.LastSellerReviewApproved);
        Assert.Null(emailSender.LastSellerReviewNote);
        Assert.Equal("Fresh Farm Seed Shop", emailSender.LastSellerReviewStoreName);
        Assert.Equal(204, emailSender.LastNotificationUserId);
        Assert.True(emailSender.LastNotificationApproved);
        Assert.Null(emailSender.LastNotificationReviewNote);
        Assert.Equal("Fresh Farm Seed Shop", emailSender.LastNotificationStoreName);
    }

    [Fact]
    public async Task RejectSeller_SetsRejectedReviewState_WhenApplicantNeedsMoreDocuments()
    {
        await using var db = CreateDbContext();
        await SeedRoleAsync(db, roleId: 1, roleName: "Customer");
        await SeedRoleAsync(db, roleId: 2, roleName: "Seller");
        await SeedUserAsync(db, userId: 205, roleId: 1);
        await SeedSellerApplicationAsync(db, userId: 205, includeCompleteKyc: true);
        var emailSender = new FakeAccountEmailSender();

        var controller = CreateAdminMerchantsController(db, emailSender);

        var result = await controller.RejectSeller(205, new RejectSellerApplicationRequestDto
        {
            Reason = "Thiếu ảnh giấy phép kinh doanh bản rõ nét."
        }, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        using var json = System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(ok.Value));
        Assert.Equal(205, json.RootElement.GetProperty("sellerId").GetInt32());
        Assert.Equal("Đã từ chối hồ sơ người bán và gửi lại yêu cầu bổ sung.", json.RootElement.GetProperty("message").GetString());

        var kyc = await db.SellerKycProfiles.SingleAsync(x => x.UserId == 205);
        Assert.Equal("rejected", kyc.ReviewStatus);
        Assert.Equal("Thiếu ảnh giấy phép kinh doanh bản rõ nét.", kyc.ReviewNote);
        Assert.NotNull(kyc.ReviewedAt);
        Assert.False(await db.UserRoles.AnyAsync(x => x.UserId == 205 && x.RoleId == 2));
        var reviewEvent = await db.SellerKycReviewEvents.SingleAsync(x => x.UserId == 205);
        Assert.Equal("reject", reviewEvent.Action);
        Assert.Equal("rejected", reviewEvent.ReviewStatus);
        Assert.Equal("Thiếu ảnh giấy phép kinh doanh bản rõ nét.", reviewEvent.Note);
        Assert.Equal("sellerapp205@example.com", emailSender.LastSellerReviewEmailTo);
        Assert.False(emailSender.LastSellerReviewApproved);
        Assert.Equal("Thiếu ảnh giấy phép kinh doanh bản rõ nét.", emailSender.LastSellerReviewNote);
        Assert.Equal("Fresh Farm Seed Shop", emailSender.LastSellerReviewStoreName);
        Assert.Equal(205, emailSender.LastNotificationUserId);
        Assert.False(emailSender.LastNotificationApproved);
        Assert.Equal("Thiếu ảnh giấy phép kinh doanh bản rõ nét.", emailSender.LastNotificationReviewNote);
        Assert.Equal("Fresh Farm Seed Shop", emailSender.LastNotificationStoreName);
    }

    [Fact]
    public async Task RejectSeller_NormalizesCommonAsciiVietnameseReason_BeforePersistingAndSending()
    {
        await using var db = CreateDbContext();
        await SeedRoleAsync(db, roleId: 1, roleName: "Customer");
        await SeedRoleAsync(db, roleId: 2, roleName: "Seller");
        await SeedUserAsync(db, userId: 250, roleId: 1);
        await SeedSellerApplicationAsync(db, userId: 250, includeCompleteKyc: true);
        var emailSender = new FakeAccountEmailSender();

        var controller = CreateAdminMerchantsController(db, emailSender);
        const string inputReason = "  vui long bo sung anh CCCD mat sau ro net va anh dai dien cua hang.  ";
        const string expectedReason = "vui lòng bổ sung ảnh CCCD mặt sau rõ nét va ảnh đại diện cửa hàng.";

        var result = await controller.RejectSeller(250, new RejectSellerApplicationRequestDto
        {
            Reason = inputReason
        }, CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);

        var kyc = await db.SellerKycProfiles.SingleAsync(x => x.UserId == 250);
        Assert.Equal(expectedReason, kyc.ReviewNote);
        var reviewEvent = await db.SellerKycReviewEvents.SingleAsync(x => x.UserId == 250);
        Assert.Equal(expectedReason, reviewEvent.Note);
        Assert.Equal(expectedReason, emailSender.LastSellerReviewNote);
        Assert.Equal(expectedReason, emailSender.LastNotificationReviewNote);
        Assert.Equal("Fresh Farm Seed Shop", emailSender.LastSellerReviewStoreName);
        Assert.Equal("Fresh Farm Seed Shop", emailSender.LastNotificationStoreName);
    }

    [Fact]
    public async Task Upsert_ResetsRejectedReviewStateBackToPending_WhenApplicantResubmits()
    {
        await using var db = CreateDbContext();
        await SeedRoleAsync(db, roleId: 1, roleName: "Customer");
        await SeedUserAsync(db, userId: 206, roleId: 1);
        await SeedSellerApplicationAsync(db, userId: 206, includeCompleteKyc: true, reviewStatus: "rejected", reviewNote: "Thiếu mặt sau CCCD.");

        var controller = CreateSellerApplicationController(db, userId: 206);

        var result = await controller.Upsert(CreateValidRequest(), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<SellerApplicationResponseDto>(ok.Value);
        Assert.Equal("pending", response.Status);
        Assert.Equal("Đang chờ duyệt", response.ReviewStatusLabel);
        Assert.True(string.IsNullOrWhiteSpace(response.ReviewNote));

        var kyc = await db.SellerKycProfiles.SingleAsync(x => x.UserId == 206);
        Assert.Equal("pending", kyc.ReviewStatus);
        Assert.Null(kyc.ReviewNote);
        Assert.Null(kyc.ReviewedAt);
    }

    [Fact]
    public async Task AdminMerchantList_FiltersByReviewStatus()
    {
        await using var db = CreateDbContext();
        await SeedRoleAsync(db, roleId: 1, roleName: "Customer");
        await SeedRoleAsync(db, roleId: 2, roleName: "Seller");
        await SeedUserAsync(db, userId: 207, roleId: 1);
        await SeedUserAsync(db, userId: 208, roleId: 1);
        await SeedSellerApplicationAsync(db, userId: 207, includeCompleteKyc: true, reviewStatus: "pending");
        await SeedSellerApplicationAsync(db, userId: 208, includeCompleteKyc: true, reviewStatus: "rejected", reviewNote: "Thiếu mặt sau CCCD.");

        var controller = CreateAdminMerchantsController(db);

        var result = await controller.Get(reviewStatus: "rejected", cancellationToken: CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var payload = Assert.IsType<AdminMerchantsController.MerchantListResponse>(ok.Value);
        Assert.Single(payload.Merchants);
        Assert.Equal(208, payload.Merchants[0].SellerId);
        Assert.Equal("rejected", payload.Filters.ReviewStatus);
        Assert.Contains(payload.Filters.RejectReasonTemplates, x => x.Contains("CCCD", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task AdminMerchantList_FiltersByReviewWindow()
    {
        await using var db = CreateDbContext();
        await SeedRoleAsync(db, roleId: 1, roleName: "Customer");
        await SeedRoleAsync(db, roleId: 2, roleName: "Seller");
        await SeedUserAsync(db, userId: 209, roleId: 1);
        await SeedUserAsync(db, userId: 210, roleId: 1);
        await SeedSellerApplicationAsync(db, userId: 209, includeCompleteKyc: true, reviewStatus: "rejected", reviewNote: "Thiếu giấy phép.", reviewedAt: DateTime.UtcNow.AddDays(-2));
        await SeedSellerApplicationAsync(db, userId: 210, includeCompleteKyc: true, reviewStatus: "rejected", reviewNote: "Thiếu địa chỉ.", reviewedAt: DateTime.UtcNow.AddDays(-20));

        var controller = CreateAdminMerchantsController(db);

        var result = await controller.Get(reviewStatus: "rejected", reviewWindow: "7d", cancellationToken: CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var payload = Assert.IsType<AdminMerchantsController.MerchantListResponse>(ok.Value);
        Assert.Single(payload.Merchants);
        Assert.Equal(209, payload.Merchants[0].SellerId);
        Assert.Equal("7d", payload.Filters.ReviewWindow);
    }

    private static SellerApplicationController CreateSellerApplicationController(
        FreshFarmIdentityDBContext db,
        int userId,
        ISellerStoreSettingsResolver? sellerStoreSettingsResolver = null)
    {
        return new SellerApplicationController(db, sellerStoreSettingsResolver ?? CreateSellerStoreSettingsResolver(db))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                    [
                        new Claim(JwtRegisteredClaimNames.Sub, userId.ToString())
                    ], "TestAuth"))
                }
            }
        };
    }

    private static AdminMerchantsController CreateAdminMerchantsController(
        FreshFarmIdentityDBContext db,
        IAccountEmailSender? accountEmailSender = null,
        ICustomerNotificationPublisher? customerNotificationPublisher = null)
    {
        var notificationPublisher = customerNotificationPublisher
            ?? accountEmailSender as ICustomerNotificationPublisher
            ?? new FakeAccountEmailSender();
        return new AdminMerchantsController(
            db,
            CreateSellerStoreSettingsResolver(db),
            accountEmailSender ?? new FakeAccountEmailSender(),
            notificationPublisher,
            NullLogger<AdminMerchantsController>.Instance);
    }

    private static ISellerStoreSettingsResolver CreateSellerStoreSettingsResolver(FreshFarmIdentityDBContext db)
    {
        return new SellerStoreSettingsResolver(db, NullLogger<SellerStoreSettingsResolver>.Instance);
    }

    private sealed class StubSellerStoreSettingsResolver : ISellerStoreSettingsResolver
    {
        private readonly SellerStoreSetting? _sellerStoreSetting;

        public StubSellerStoreSettingsResolver(SellerStoreSetting? sellerStoreSetting)
        {
            _sellerStoreSetting = sellerStoreSetting;
        }

        public Task<SellerStoreSetting?> GetLatestForUserAsync(int userId, bool asNoTracking, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_sellerStoreSetting);
        }
    }

    private static UpsertSellerApplicationRequestDto CreateValidRequest()
    {
        return new UpsertSellerApplicationRequestDto
        {
            StoreName = "Fresh Farm Test Shop",
            StoreAddress = "12 Nguyen Trai, Ward 1, District 1",
            StoreEmail = "shop@test.example",
            StorePhone = "+84987654321",
            BankAccountInfo = "VCB - 123456789",
            BankTransferInstructions = "Chuyen khoan dung noi dung.",
            AdminNotificationEmail = "ops@test.example",
            PickupName = "Nguoi Lay Hang",
            PickupPhone = "+84987654322",
            PickupAddress = "34 Le Loi, Ward 2, District 1",
            LegalFullName = "Nguyen Van Seller",
            IdentityNumber = "123456789012",
            IdentityIssuedDate = new DateTime(2020, 01, 15),
            IdentityIssuedPlace = "Cuc Canh sat QLHC ve TTXH",
            TaxCode = "0312345678",
            BusinessLicenseNumber = "GPKD-2026-001",
            CitizenIdFrontUrl = "/uploads/seller-kyc/front.jpg",
            CitizenIdBackUrl = "/uploads/seller-kyc/back.jpg",
            BusinessLicenseUrl = "/uploads/seller-kyc/license.pdf",
            AdditionalDocumentUrl = "/uploads/seller-kyc/additional.pdf",
            Notes = "Ho so test seller"
        };
    }

    private static FreshFarmIdentityDBContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<FreshFarmIdentityDBContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new FreshFarmIdentityDBContext(options);
    }

    private static async Task SeedRoleAsync(FreshFarmIdentityDBContext db, int roleId, string roleName)
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

    private static async Task SeedUserAsync(FreshFarmIdentityDBContext db, int userId, int roleId)
    {
        var role = await db.Roles.SingleAsync(x => x.RoleId == roleId);
        var user = new User
        {
            UserId = userId,
            UserName = $"sellerapp{userId}",
            FullName = $"Seller Applicant {userId}",
            Email = $"sellerapp{userId}@example.com",
            Phone = "0912345678",
            EmailConfirmed = true,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        var userRole = new UserRole
        {
            UserId = userId,
            RoleId = roleId,
            CreatedAt = DateTime.UtcNow,
            User = user,
            Role = role
        };

        user.UserRoles.Add(userRole);
        role.UserRoles.Add(userRole);

        db.Users.Add(user);
        db.UserRoles.Add(userRole);
        await db.SaveChangesAsync();
    }

    private static async Task SeedSellerApplicationAsync(
        FreshFarmIdentityDBContext db,
        int userId,
        bool includeCompleteKyc,
        string reviewStatus = "pending",
        string? reviewNote = null,
        DateTime? reviewedAt = null)
    {
        var now = DateTime.UtcNow;
        db.SellerStoreSettings.Add(new SellerStoreSetting
        {
            UserId = userId,
            StoreName = "Fresh Farm Seed Shop",
            StoreAddress = "1 Seed Street",
            StoreEmail = "seedshop@example.com",
            StorePhone = "0987654321",
            IsCodenabled = true,
            BankTransferInstructions = string.Empty,
            BankAccountInfo = string.Empty,
            DefaultShippingFee = 30000m,
            FreeShippingThreshold = 500000m,
            IsEmailNewOrderEnabled = true,
            IsEmailDeliveredEnabled = true,
            IsEmailCancelledEnabled = true,
            AdminNotificationEmail = "seedshop@example.com",
            GhnPickupName = "Seed Pickup",
            GhnPickupPhone = "0987654321",
            GhnPickupAddress = "1 Seed Street",
            CreatedAt = now,
            UpdatedAt = now
        });

        db.SellerKycProfiles.Add(new SellerKycProfile
        {
            UserId = userId,
            LegalFullName = "Nguyen Van Seed",
            IdentityNumber = "123456789012",
            IdentityIssuedDate = includeCompleteKyc ? new DateTime(2021, 02, 03) : default,
            IdentityIssuedPlace = includeCompleteKyc ? "Cong an TP.HCM" : string.Empty,
            CitizenIdFrontUrl = includeCompleteKyc ? "/uploads/seller-kyc/front.jpg" : null,
            CitizenIdBackUrl = includeCompleteKyc ? "/uploads/seller-kyc/back.jpg" : null,
            BusinessLicenseUrl = "/uploads/seller-kyc/license.pdf",
            ReviewStatus = reviewStatus,
            ReviewNote = reviewNote,
            ReviewedAt = reviewedAt ?? (reviewStatus == "pending" ? null : now),
            CreatedAt = now,
            UpdatedAt = now
        });

        await db.SaveChangesAsync();
    }

    private sealed class FakeAccountEmailSender : IAccountEmailSender, ICustomerNotificationPublisher
    {
        public bool IsConfigured => true;

        public bool LastNotificationApproved { get; private set; }

        public string? LastNotificationReviewNote { get; private set; }

        public string? LastNotificationStoreName { get; private set; }

        public int? LastNotificationUserId { get; private set; }

        public string? LastSellerReviewEmailTo { get; private set; }

        public bool LastSellerReviewApproved { get; private set; }

        public string? LastSellerReviewNote { get; private set; }

        public string? LastSellerReviewStoreName { get; private set; }

        public Task SendPasswordResetEmailAsync(string toEmail, string? toName, string resetUrl, int expiresInMinutes, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task SendEmailVerificationAsync(string toEmail, string? toName, string verifyUrl, int expiresInMinutes, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task SendSellerApplicationReviewAsync(string toEmail, string? toName, string? storeName, bool isApproved, string? reviewNote, CancellationToken cancellationToken = default)
        {
            LastSellerReviewEmailTo = toEmail;
            LastSellerReviewApproved = isApproved;
            LastSellerReviewNote = reviewNote;
            LastSellerReviewStoreName = storeName;
            return Task.CompletedTask;
        }

        Task ICustomerNotificationPublisher.PublishSellerReviewAsync(User user, string? storeName, bool isApproved, string? reviewNote, CancellationToken cancellationToken)
        {
            LastNotificationUserId = user.UserId;
            LastNotificationApproved = isApproved;
            LastNotificationReviewNote = reviewNote;
            LastNotificationStoreName = storeName;
            return Task.CompletedTask;
        }
    }
}
