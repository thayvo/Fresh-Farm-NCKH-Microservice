using FreshFarm.Identity.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace FreshFarm.Identity.Api.Services;

public interface ISellerStoreSettingsResolver
{
    Task<SellerStoreSetting?> GetLatestForUserAsync(int userId, bool asNoTracking, CancellationToken cancellationToken = default);
}

public sealed class SellerStoreSettingsResolver : ISellerStoreSettingsResolver
{
    private readonly FreshFarmIdentityDBContext _db;
    private readonly ILogger<SellerStoreSettingsResolver> _logger;

    public SellerStoreSettingsResolver(
        FreshFarmIdentityDBContext db,
        ILogger<SellerStoreSettingsResolver> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<SellerStoreSetting?> GetLatestForUserAsync(
        int userId,
        bool asNoTracking,
        CancellationToken cancellationToken = default)
    {
        IQueryable<SellerStoreSetting> query = _db.SellerStoreSettings
            .Where(x => x.UserId == userId);

        if (asNoTracking)
        {
            query = query.AsNoTracking();
        }

        var matches = await query
            .OrderByDescending(x => x.UpdatedAt)
            .ThenByDescending(x => x.SellerStoreSettingId)
            .Take(2)
            .ToListAsync(cancellationToken);

        if (matches.Count > 1)
        {
            _logger.LogWarning(
                "SellerStoreSettings has duplicate rows for user {UserId}. Keeping SellerStoreSettingId={KeptId} and ignoring at least one older row.",
                userId,
                matches[0].SellerStoreSettingId);
        }

        return matches.FirstOrDefault();
    }
}
