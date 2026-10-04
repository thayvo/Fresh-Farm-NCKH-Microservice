using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using FreshFarm.Identity.Api.Controllers;
using FreshFarm.Identity.Api.Models;
using FreshFarm.Identity.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FreshFarm.Identity.Api.Tests;

public sealed class AdminUsersControllerApprovalTests
{
    [Fact]
    public void NewUser_DefaultsApprovalStatusToPending()
    {
        Assert.Equal(AccountApprovalStatus.Pending, new User().ApprovalStatus);
    }

    [Fact]
    public async Task ChangeApproval_ReturnsConflictWithoutSideEffects_WhenExpectedVersionIsStale()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        var root = new InMemoryDatabaseRoot();
        await using var db = CreateDbContext(databaseName, root);
        await SeedUsersAsync(db, approvalVersion: 4);
        var emailSender = new RecordingAccountEmailSender();
        var controller = CreateController(db, emailSender);

        var result = await controller.ChangeApproval(
            901,
            new AdminUsersController.AdminChangeApprovalRequest
            {
                Status = AccountApprovalStatus.Approved,
                ExpectedApprovalVersion = 3
            });

        var conflict = Assert.IsType<ConflictObjectResult>(result);
        Assert.Equal(StatusCodes.Status409Conflict, conflict.StatusCode);
        Assert.Equal(AccountApprovalStatus.Pending, (await db.Users.SingleAsync(x => x.UserId == 901)).ApprovalStatus);
        Assert.Equal(4, (await db.Users.SingleAsync(x => x.UserId == 901)).ApprovalVersion);
        Assert.Empty(await db.AccountApprovalEvents.ToListAsync());
        Assert.Equal(0, emailSender.ApprovalResultCallCount);
    }

    [Fact]
    public async Task ChangeApproval_AllowsExactlyOneWinner_WhenTwoAdminsSubmitSameVersion()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        var root = new InMemoryDatabaseRoot();
        await using (var seedDb = CreateDbContext(databaseName, root))
        {
            await SeedUsersAsync(seedDb, approvalVersion: 0);
        }

        var barrier = new ConcurrentSaveBarrier(expectedArrivals: 2);
        await using var firstDb = CreateDbContext(databaseName, root, barrier);
        await using var secondDb = CreateDbContext(databaseName, root, barrier);
        var emailSender = new RecordingAccountEmailSender();
        var firstController = CreateController(firstDb, emailSender);
        var secondController = CreateController(secondDb, emailSender);

        var results = await Task.WhenAll(
            firstController.ChangeApproval(
                901,
                new AdminUsersController.AdminChangeApprovalRequest
                {
                    Status = AccountApprovalStatus.Approved,
                    ExpectedApprovalVersion = 0
                }),
            secondController.ChangeApproval(
                901,
                new AdminUsersController.AdminChangeApprovalRequest
                {
                    Status = AccountApprovalStatus.Rejected,
                    Note = "Thông tin tài khoản chưa hợp lệ.",
                    ExpectedApprovalVersion = 0
                }));

        Assert.Single(results.OfType<OkObjectResult>());
        Assert.Single(results.OfType<ConflictObjectResult>());

        await using var verificationDb = CreateDbContext(databaseName, root);
        var user = await verificationDb.Users.SingleAsync(x => x.UserId == 901);
        var auditEvent = Assert.Single(await verificationDb.AccountApprovalEvents.ToListAsync());
        Assert.Equal(1, user.ApprovalVersion);
        Assert.Equal(user.ApprovalStatus, auditEvent.ToStatus);
        Assert.Equal(1, emailSender.ApprovalResultCallCount);
    }

    private static FreshFarmIdentityDBContext CreateDbContext(
        string databaseName,
        InMemoryDatabaseRoot root,
        IInterceptor? interceptor = null)
    {
        var optionsBuilder = new DbContextOptionsBuilder<FreshFarmIdentityDBContext>()
            .UseInMemoryDatabase(databaseName, root);
        if (interceptor is not null)
        {
            optionsBuilder.AddInterceptors(interceptor);
        }

        return new FreshFarmIdentityDBContext(optionsBuilder.Options);
    }

    private static async Task SeedUsersAsync(FreshFarmIdentityDBContext db, long approvalVersion)
    {
        var now = DateTime.UtcNow;
        db.Users.AddRange(
            new User
            {
                UserId = 900,
                UserName = "admin900",
                FullName = "Admin 900",
                Email = "admin900@example.com",
                Phone = "0900000900",
                EmailConfirmed = true,
                IsActive = true,
                ApprovalStatus = AccountApprovalStatus.Approved,
                CreatedAt = now
            },
            new User
            {
                UserId = 901,
                UserName = "customer901",
                FullName = "Customer 901",
                Email = "customer901@example.com",
                Phone = "0900000901",
                EmailConfirmed = true,
                IsActive = true,
                ApprovalStatus = AccountApprovalStatus.Pending,
                ApprovalVersion = approvalVersion,
                CreatedAt = now
            });
        await db.SaveChangesAsync();
    }

    private static AdminUsersController CreateController(
        FreshFarmIdentityDBContext db,
        RecordingAccountEmailSender emailSender)
    {
        var controller = new AdminUsersController(
            db,
            new PasswordHasher<User>(),
            new StubLoginDeviceSecurityService(),
            emailSender,
            NullLogger<AdminUsersController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                    [
                        new Claim(JwtRegisteredClaimNames.Sub, "900")
                    ], "TestAuth"))
                }
            }
        };

        return controller;
    }

    private sealed class ConcurrentSaveBarrier(int expectedArrivals) : SaveChangesInterceptor
    {
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _arrivals;

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _arrivals) == expectedArrivals)
            {
                _release.TrySetResult();
            }

            await _release.Task.WaitAsync(cancellationToken);
            return result;
        }
    }

    private sealed class StubLoginDeviceSecurityService : ILoginDeviceSecurityService
    {
        public Task<LoginDeviceSecurityResult> GetStateAsync(string? deviceId, HttpContext httpContext, CancellationToken cancellationToken = default) =>
            Task.FromResult(new LoginDeviceSecurityResult(false, 0, null, "test-device"));

        public Task<LoginDeviceSecurityResult> RecordFailureAsync(string? deviceId, HttpContext httpContext, int? userId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new LoginDeviceSecurityResult(false, 1, null, "test-device"));

        public Task ResetAfterSuccessfulCredentialAsync(string? deviceId, HttpContext httpContext, int? userId, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<int> UnlockForUserAsync(int userId, CancellationToken cancellationToken = default) =>
            Task.FromResult(0);
    }

    private sealed class RecordingAccountEmailSender : IAccountEmailSender
    {
        private int _approvalResultCallCount;

        public bool IsConfigured => true;

        public int ApprovalResultCallCount => Volatile.Read(ref _approvalResultCallCount);

        public Task SendAccountApprovalResultAsync(string toEmail, string? toName, bool isApproved, string? reviewNote, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _approvalResultCallCount);
            return Task.CompletedTask;
        }

        public Task SendPasswordResetEmailAsync(string toEmail, string? toName, string resetUrl, int expiresInMinutes, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task SendEmailVerificationAsync(string toEmail, string? toName, string verifyUrl, int expiresInMinutes, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task SendSellerApplicationReviewAsync(string toEmail, string? toName, string? storeName, bool isApproved, string? reviewNote, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
