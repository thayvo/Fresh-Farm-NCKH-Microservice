using System.Data;
using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using FreshFarm.Identity.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace FreshFarm.Identity.Api.Services;

public interface ILoginDeviceSecurityService
{
    Task<LoginDeviceSecurityResult> GetStateAsync(string? deviceId, HttpContext httpContext, CancellationToken cancellationToken = default);

    Task<LoginDeviceSecurityResult> RecordFailureAsync(string? deviceId, HttpContext httpContext, int? userId, CancellationToken cancellationToken = default);

    Task ResetAfterSuccessfulCredentialAsync(string? deviceId, HttpContext httpContext, int? userId, CancellationToken cancellationToken = default);

    Task<int> UnlockForUserAsync(int userId, CancellationToken cancellationToken = default);
}

public sealed record LoginDeviceSecurityResult(
    bool IsLocked,
    int FailedCount,
    DateTime? LockedUntilUtc,
    string DeviceKeyHash,
    int? AccountFailedCount = null,
    int? AccountLockoutLevel = null,
    DateTime? AccountLockedUntilUtc = null);

public sealed class LoginDeviceSecurityService : ILoginDeviceSecurityService
{
    public const int MaxFailedAttempts = 5;
    private const int BaseLockoutMinutes = 15;
    private const int MaxLockoutMinutes = 24 * 60;

    private readonly FreshFarmIdentityDBContext _db;

    public LoginDeviceSecurityService(FreshFarmIdentityDBContext db)
    {
        _db = db;
    }

    public async Task<LoginDeviceSecurityResult> GetStateAsync(
        string? deviceId,
        HttpContext httpContext,
        CancellationToken cancellationToken = default)
    {
        var deviceKeyHash = BuildDeviceKeyHash(deviceId, httpContext);
        var state = await _db.LoginDeviceSecurityStates
            .SingleOrDefaultAsync(x => x.DeviceKeyHash == deviceKeyHash, cancellationToken);

        if (state is null)
        {
            return new LoginDeviceSecurityResult(false, 0, null, deviceKeyHash);
        }

        var now = DateTime.UtcNow;
        if (state.LockedUntil.HasValue && state.LockedUntil.Value <= now)
        {
            // Expired rows are normalized inside the atomic failure transaction. Avoid a
            // read-modify-write here that could overwrite a concurrent failed attempt.
            return new LoginDeviceSecurityResult(false, 0, null, deviceKeyHash);
        }

        return ToResult(state, now);
    }

    public async Task<LoginDeviceSecurityResult> RecordFailureAsync(
        string? deviceId,
        HttpContext httpContext,
        int? userId,
        CancellationToken cancellationToken = default)
    {
        var deviceKeyHash = BuildDeviceKeyHash(deviceId, httpContext);
        if (_db.Database.IsSqlServer())
        {
            return await RecordFailureSqlServerWithRetryAsync(
                deviceKeyHash,
                userId,
                cancellationToken);
        }

        // The production provider is SQL Server and uses the atomic path above. This tracked
        // fallback keeps unit tests and non-relational development providers functional.
        return await RecordFailureTrackedAsync(deviceKeyHash, userId, cancellationToken);
    }

    public async Task ResetAfterSuccessfulCredentialAsync(
        string? deviceId,
        HttpContext httpContext,
        int? userId,
        CancellationToken cancellationToken = default)
    {
        var deviceKeyHash = BuildDeviceKeyHash(deviceId, httpContext);
        var state = await _db.LoginDeviceSecurityStates
            .SingleOrDefaultAsync(x => x.DeviceKeyHash == deviceKeyHash, cancellationToken);

        if (state is null)
        {
            return;
        }

        state.LastUserId = userId;
        state.FailedCount = 0;
        state.LockoutLevel = 0;
        state.LockedUntil = null;
        state.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<int> UnlockForUserAsync(int userId, CancellationToken cancellationToken = default)
    {
        var states = await _db.LoginDeviceSecurityStates
            .Where(x => x.LastUserId == userId && (x.FailedCount > 0 || x.LockedUntil != null || x.LockoutLevel > 0))
            .ToListAsync(cancellationToken);

        foreach (var state in states)
        {
            state.FailedCount = 0;
            state.LockoutLevel = 0;
            state.LockedUntil = null;
            state.UpdatedAt = DateTime.UtcNow;
        }

        if (states.Count > 0)
        {
            await _db.SaveChangesAsync(cancellationToken);
        }

        return states.Count;
    }

    private static LoginDeviceSecurityResult ToResult(LoginDeviceSecurityState state, DateTime now)
        => new(
            state.LockedUntil.HasValue && state.LockedUntil.Value > now,
            state.FailedCount,
            state.LockedUntil,
            state.DeviceKeyHash);

    private async Task<LoginDeviceSecurityResult> RecordFailureTrackedAsync(
        string deviceKeyHash,
        int? userId,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        UserAuth? accountState = null;
        if (userId.HasValue)
        {
            accountState = await _db.UserAuths.SingleOrDefaultAsync(
                x => x.UserId == userId.Value,
                cancellationToken);
            if (accountState is not null
                && (!accountState.LockedUntil.HasValue || accountState.LockedUntil.Value <= now))
            {
                if (accountState.LockedUntil.HasValue)
                {
                    accountState.FailedCount = 0;
                    accountState.LockedUntil = null;
                }

                accountState.FailedCount = Math.Min(MaxFailedAttempts, accountState.FailedCount + 1);
                if (accountState.FailedCount >= MaxFailedAttempts)
                {
                    accountState.LockoutLevel = Math.Clamp(accountState.LockoutLevel + 1, 1, 16);
                    accountState.LockedUntil = now.Add(BuildLockoutDuration(accountState.LockoutLevel));
                }

                accountState.UpdatedAt = now;
            }
        }

        var state = await _db.LoginDeviceSecurityStates
            .SingleOrDefaultAsync(x => x.DeviceKeyHash == deviceKeyHash, cancellationToken);
        if (state is null)
        {
            state = new LoginDeviceSecurityState
            {
                DeviceKeyHash = deviceKeyHash,
                FailedCount = 0,
                LockoutLevel = 0,
                CreatedAt = now,
                UpdatedAt = now
            };
            _db.LoginDeviceSecurityStates.Add(state);
        }

        if (!state.LockedUntil.HasValue || state.LockedUntil.Value <= now)
        {
            if (state.LockedUntil.HasValue)
            {
                state.FailedCount = 0;
                state.LockedUntil = null;
            }

            state.LastUserId = userId;
            state.LastFailedAt = now;
            state.UpdatedAt = now;
            state.FailedCount = Math.Min(MaxFailedAttempts, state.FailedCount + 1);
            if (state.FailedCount >= MaxFailedAttempts)
            {
                state.LockoutLevel = Math.Clamp(state.LockoutLevel + 1, 1, 16);
                state.LockedUntil = now.Add(BuildLockoutDuration(state.LockoutLevel));
            }
        }

        await _db.SaveChangesAsync(cancellationToken);
        return new LoginDeviceSecurityResult(
            state.LockedUntil.HasValue && state.LockedUntil.Value > now,
            state.FailedCount,
            state.LockedUntil,
            state.DeviceKeyHash,
            accountState?.FailedCount,
            accountState?.LockoutLevel,
            accountState?.LockedUntil);
    }

    private async Task<LoginDeviceSecurityResult> RecordFailureSqlServerWithRetryAsync(
        string deviceKeyHash,
        int? userId,
        CancellationToken cancellationToken)
    {
        const int maxAttempts = 3;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await RecordFailureSqlServerOnceAsync(deviceKeyHash, userId, cancellationToken);
            }
            catch (DbException exception) when (attempt < maxAttempts && IsRetryableSqlServerError(exception))
            {
                await Task.Delay(TimeSpan.FromMilliseconds(20 * attempt), cancellationToken);
            }
        }
    }

    private async Task<LoginDeviceSecurityResult> RecordFailureSqlServerOnceAsync(
        string deviceKeyHash,
        int? userId,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        await using var transaction = await _db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        var connection = _db.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction.GetDbTransaction();
        command.CommandText = AtomicFailureSql;
        AddParameter(command, "@UserId", DbType.Int32, userId);
        AddParameter(command, "@DeviceKeyHash", DbType.AnsiString, deviceKeyHash, 64);
        AddParameter(command, "@Now", DbType.DateTime2, now);
        AddParameter(command, "@MaxFailedAttempts", DbType.Int32, MaxFailedAttempts);
        AddParameter(command, "@BaseLockoutMinutes", DbType.Int32, BaseLockoutMinutes);
        AddParameter(command, "@MaxLockoutMinutes", DbType.Int32, MaxLockoutMinutes);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("Atomic login failure update did not return a state row.");
        }

        var accountFailedCount = ReadNullableInt32(reader, 0);
        var accountLockoutLevel = ReadNullableInt32(reader, 1);
        var accountLockedUntil = ReadNullableDateTime(reader, 2);
        var deviceFailedCount = reader.GetInt32(3);
        var deviceLockedUntil = ReadNullableDateTime(reader, 5);
        await reader.DisposeAsync();
        await transaction.CommitAsync(cancellationToken);

        return new LoginDeviceSecurityResult(
            deviceLockedUntil.HasValue && deviceLockedUntil.Value > now,
            deviceFailedCount,
            deviceLockedUntil,
            deviceKeyHash,
            accountFailedCount,
            accountLockoutLevel,
            accountLockedUntil);
    }

    private static readonly string AtomicFailureSql = """
        SET NOCOUNT ON;

        DECLARE @AccountFailedCount int = NULL;
        DECLARE @AccountLockoutLevel int = NULL;
        DECLARE @AccountLockedUntil datetime2(0) = NULL;
        DECLARE @DurationMinutes int;

        -- Always lock account before device to keep the lock order stable across instances.
        IF @UserId IS NOT NULL
        BEGIN
            SELECT
                @AccountFailedCount = [FailedCount],
                @AccountLockoutLevel = [LockoutLevel],
                @AccountLockedUntil = [LockedUntil]
            FROM [UserAuth] WITH (UPDLOCK, HOLDLOCK)
            WHERE [UserId] = @UserId;

            IF @AccountFailedCount IS NOT NULL
               AND (@AccountLockedUntil IS NULL OR @AccountLockedUntil <= @Now)
            BEGIN
                IF @AccountLockedUntil IS NOT NULL
                BEGIN
                    SET @AccountFailedCount = 0;
                    SET @AccountLockedUntil = NULL;
                END;

                SET @AccountFailedCount =
                    CASE WHEN @AccountFailedCount < @MaxFailedAttempts
                         THEN @AccountFailedCount + 1 ELSE @MaxFailedAttempts END;

                IF @AccountFailedCount >= @MaxFailedAttempts
                BEGIN
                    SET @AccountLockoutLevel =
                        CASE WHEN @AccountLockoutLevel < 1 THEN 1
                             WHEN @AccountLockoutLevel < 16 THEN @AccountLockoutLevel + 1
                             ELSE 16 END;
                    SET @DurationMinutes =
                        CASE WHEN @AccountLockoutLevel >= 8 THEN @MaxLockoutMinutes
                             ELSE @BaseLockoutMinutes * CONVERT(int, POWER(CONVERT(float, 2), @AccountLockoutLevel - 1)) END;
                    SET @AccountLockedUntil = DATEADD(MINUTE, @DurationMinutes, @Now);
                END;

                UPDATE [UserAuth]
                SET [FailedCount] = @AccountFailedCount,
                    [LockoutLevel] = @AccountLockoutLevel,
                    [LockedUntil] = @AccountLockedUntil,
                    [UpdatedAt] = @Now
                WHERE [UserId] = @UserId;
            END;
        END;

        DECLARE @DeviceStateId bigint = NULL;
        DECLARE @DeviceFailedCount int = NULL;
        DECLARE @DeviceLockoutLevel int = NULL;
        DECLARE @DeviceLockedUntil datetime2(0) = NULL;

        -- HOLDLOCK at SERIALIZABLE protects the unique-key range when the device row is absent.
        SELECT
            @DeviceStateId = [LoginDeviceSecurityStateId],
            @DeviceFailedCount = [FailedCount],
            @DeviceLockoutLevel = [LockoutLevel],
            @DeviceLockedUntil = [LockedUntil]
        FROM [LoginDeviceSecurityState] WITH (UPDLOCK, HOLDLOCK, INDEX([UX_LoginDeviceSecurityState_DeviceKeyHash]))
        WHERE [DeviceKeyHash] = @DeviceKeyHash;

        IF @DeviceStateId IS NULL
        BEGIN
            SET @DeviceFailedCount = 1;
            SET @DeviceLockoutLevel = 0;
            INSERT INTO [LoginDeviceSecurityState]
                ([DeviceKeyHash], [LastUserId], [FailedCount], [LockoutLevel], [LockedUntil], [LastFailedAt], [CreatedAt], [UpdatedAt])
            VALUES
                (@DeviceKeyHash, @UserId, @DeviceFailedCount, @DeviceLockoutLevel, NULL, @Now, @Now, @Now);
        END
        ELSE IF @DeviceLockedUntil IS NULL OR @DeviceLockedUntil <= @Now
        BEGIN
            IF @DeviceLockedUntil IS NOT NULL
            BEGIN
                SET @DeviceFailedCount = 0;
                SET @DeviceLockedUntil = NULL;
            END;

            SET @DeviceFailedCount =
                CASE WHEN @DeviceFailedCount < @MaxFailedAttempts
                     THEN @DeviceFailedCount + 1 ELSE @MaxFailedAttempts END;

            IF @DeviceFailedCount >= @MaxFailedAttempts
            BEGIN
                SET @DeviceLockoutLevel =
                    CASE WHEN @DeviceLockoutLevel < 1 THEN 1
                         WHEN @DeviceLockoutLevel < 16 THEN @DeviceLockoutLevel + 1
                         ELSE 16 END;
                SET @DurationMinutes =
                    CASE WHEN @DeviceLockoutLevel >= 8 THEN @MaxLockoutMinutes
                         ELSE @BaseLockoutMinutes * CONVERT(int, POWER(CONVERT(float, 2), @DeviceLockoutLevel - 1)) END;
                SET @DeviceLockedUntil = DATEADD(MINUTE, @DurationMinutes, @Now);
            END;

            UPDATE [LoginDeviceSecurityState]
            SET [LastUserId] = @UserId,
                [FailedCount] = @DeviceFailedCount,
                [LockoutLevel] = @DeviceLockoutLevel,
                [LockedUntil] = @DeviceLockedUntil,
                [LastFailedAt] = @Now,
                [UpdatedAt] = @Now
            WHERE [LoginDeviceSecurityStateId] = @DeviceStateId;
        END;

        SELECT
            @AccountFailedCount AS [AccountFailedCount],
            @AccountLockoutLevel AS [AccountLockoutLevel],
            @AccountLockedUntil AS [AccountLockedUntil],
            @DeviceFailedCount AS [DeviceFailedCount],
            @DeviceLockoutLevel AS [DeviceLockoutLevel],
            @DeviceLockedUntil AS [DeviceLockedUntil];
        """;

    private static void AddParameter(
        DbCommand command,
        string name,
        DbType type,
        object? value,
        int? size = null)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.DbType = type;
        parameter.Value = value ?? DBNull.Value;
        if (size.HasValue)
        {
            parameter.Size = size.Value;
        }

        command.Parameters.Add(parameter);
    }

    private static int? ReadNullableInt32(DbDataReader reader, int ordinal)
        => reader.IsDBNull(ordinal) ? null : reader.GetInt32(ordinal);

    private static DateTime? ReadNullableDateTime(DbDataReader reader, int ordinal)
        => reader.IsDBNull(ordinal) ? null : reader.GetDateTime(ordinal);

    private static bool IsRetryableSqlServerError(DbException exception)
    {
        var numberProperty = exception.GetType().GetProperty("Number");
        return numberProperty?.GetValue(exception) is int number
               && number is 1205 or 2601 or 2627;
    }

    private static TimeSpan BuildLockoutDuration(int lockoutLevel)
    {
        var normalizedLevel = Math.Clamp(lockoutLevel, 1, 16);
        var minutes = BaseLockoutMinutes * Math.Pow(2, normalizedLevel - 1);
        return TimeSpan.FromMinutes(Math.Min(MaxLockoutMinutes, minutes));
    }

    private static string BuildDeviceKeyHash(string? deviceId, HttpContext httpContext)
    {
        var normalizedDeviceId = deviceId?.Trim();
        var material = normalizedDeviceId?.Length == 64 && normalizedDeviceId.All(Uri.IsHexDigit)
            ? $"device:{normalizedDeviceId}"
            : $"fallback:{ResolveEffectiveIp(httpContext)}|{httpContext.Request.Headers.UserAgent}";

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material)));
    }

    private static string ResolveEffectiveIp(HttpContext httpContext)
    {
        var forwardedFor = httpContext.Request.Headers["X-Forwarded-For"].ToString();
        if (!string.IsNullOrWhiteSpace(forwardedFor))
        {
            return forwardedFor.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault()
                   ?? "unknown";
        }

        return httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }
}
