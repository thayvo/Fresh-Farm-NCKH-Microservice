using FreshFarm.Ordering.Api.Models;
using FreshFarm.Ordering.Api.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FreshFarm.Ordering.Api.Services;

public interface IFinanceCommissionService
{
    Task<IReadOnlyDictionary<int, decimal>> GetCommissionRatesAsync(IEnumerable<int> sellerIds, CancellationToken cancellationToken);

    decimal CalculateCommissionAmount(decimal grossAmount, decimal commissionRate);
}

public sealed class FinanceCommissionService : IFinanceCommissionService
{
    private readonly FreshFarmOrderingDBContext _db;
    private readonly FinanceOptions _options;

    public FinanceCommissionService(FreshFarmOrderingDBContext db, IOptions<FinanceOptions> options)
    {
        _db = db;
        _options = options.Value;
    }

    public async Task<IReadOnlyDictionary<int, decimal>> GetCommissionRatesAsync(IEnumerable<int> sellerIds, CancellationToken cancellationToken)
    {
        var normalizedSellerIds = sellerIds
            .Where(x => x > 0)
            .Distinct()
            .ToArray();

        var rates = normalizedSellerIds.ToDictionary(x => x, _ => NormalizeRate(_options.DefaultCommissionRate));
        if (normalizedSellerIds.Length == 0)
        {
            return rates;
        }

        var now = DateTime.UtcNow;
        var settings = await _db.SellerCommissionSettings
            .AsNoTracking()
            .Where(x =>
                x.IsActive &&
                x.EffectiveFrom <= now &&
                (!x.EffectiveTo.HasValue || x.EffectiveTo.Value > now) &&
                (!x.SellerId.HasValue || normalizedSellerIds.Contains(x.SellerId.Value)))
            .OrderByDescending(x => x.SellerId.HasValue)
            .ThenByDescending(x => x.EffectiveFrom)
            .ThenByDescending(x => x.SellerCommissionSettingId)
            .ToListAsync(cancellationToken);

        var defaultSetting = settings.FirstOrDefault(x => !x.SellerId.HasValue);
        if (defaultSetting is not null)
        {
            var defaultRate = NormalizeRate(defaultSetting.CommissionRate);
            foreach (var sellerId in normalizedSellerIds)
            {
                rates[sellerId] = defaultRate;
            }
        }

        foreach (var setting in settings.Where(x => x.SellerId.HasValue))
        {
            rates[setting.SellerId!.Value] = NormalizeRate(setting.CommissionRate);
        }

        return rates;
    }

    public decimal CalculateCommissionAmount(decimal grossAmount, decimal commissionRate)
    {
        if (grossAmount <= 0m)
        {
            return 0m;
        }

        var decimals = Math.Clamp(_options.CommissionRoundingDecimals, 0, 4);
        return Math.Round(grossAmount * NormalizeRate(commissionRate), decimals, MidpointRounding.AwayFromZero);
    }

    private static decimal NormalizeRate(decimal rate)
    {
        if (rate < 0m)
        {
            return 0m;
        }

        return rate > 1m ? 1m : rate;
    }
}
