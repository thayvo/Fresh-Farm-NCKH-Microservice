namespace FreshFarm.Ordering.Api.Services;

public static class SeasonalityScoring
{
    private const int MinimumMonth = 1;
    private const int MaximumMonth = 12;
    private const int MaximumSeasonalityScore = 100;
    private const int PeakSeasonBonus = 20;

    public static int GetVietnamCurrentMonth()
    {
        try
        {
            var vietnamTimeZone = TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time");
            return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, vietnamTimeZone).Month;
        }
        catch (TimeZoneNotFoundException)
        {
            return DateTime.UtcNow.AddHours(7).Month;
        }
        catch (InvalidTimeZoneException)
        {
            return DateTime.UtcNow.AddHours(7).Month;
        }
    }

    public static bool IsMonthInSeason(int month, int startMonth, int endMonth)
    {
        if (!IsValidMonth(month) || !IsValidMonth(startMonth) || !IsValidMonth(endMonth))
        {
            return false;
        }

        return startMonth <= endMonth
            ? month >= startMonth && month <= endMonth
            : month >= startMonth || month <= endMonth;
    }

    public static bool IsPeakMonth(int month, string? peakMonths)
    {
        if (!IsValidMonth(month) || string.IsNullOrWhiteSpace(peakMonths))
        {
            return false;
        }

        var tokens = peakMonths
            .Split([';', ',', '|'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var token in tokens)
        {
            var parts = token.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 1 && int.TryParse(parts[0], out var singleMonth) && month == singleMonth)
            {
                return true;
            }

            if (parts.Length == 2
                && int.TryParse(parts[0], out var startMonth)
                && int.TryParse(parts[1], out var endMonth)
                && IsMonthInSeason(month, startMonth, endMonth))
            {
                return true;
            }
        }

        return false;
    }

    public static IReadOnlyDictionary<int, ProductSeasonalitySignal> BuildBestSignals(
        IEnumerable<int> productIds,
        IEnumerable<CatalogProductSeasonalityItem> seasonalityRows,
        int currentMonth)
    {
        var requestedProductIds = productIds
            .Where(id => id > 0)
            .Distinct()
            .ToHashSet();

        if (requestedProductIds.Count == 0 || !IsValidMonth(currentMonth))
        {
            return new Dictionary<int, ProductSeasonalitySignal>();
        }

        return seasonalityRows
            .Where(row => requestedProductIds.Contains(row.ProductId))
            .Select(row => CalculateSignal(row, currentMonth))
            .Where(signal => signal.Score > 0)
            .GroupBy(signal => signal.ProductId)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderByDescending(signal => signal.Score)
                    .ThenByDescending(signal => signal.IsPeakSeason)
                    .ThenByDescending(signal => signal.IsInSeason)
                    .First());
    }

    public static ProductSeasonalitySignal CalculateSignal(CatalogProductSeasonalityItem row, int currentMonth)
    {
        if (!IsValidMonth(currentMonth) || row.ProductId <= 0)
        {
            return ProductSeasonalitySignal.Empty(row.ProductId);
        }

        var isInWindow = row.IsYearRound || IsMonthInSeason(currentMonth, row.StartMonth, row.EndMonth);
        var isPeakMonth = row.HasPeakSeason && IsPeakMonth(currentMonth, row.PeakMonths);

        if (!isInWindow && !isPeakMonth)
        {
            return ProductSeasonalitySignal.Empty(row.ProductId);
        }

        var score = Math.Clamp(row.SeasonScoreWeight, 0, MaximumSeasonalityScore);
        if (row.IsYearRound && !isPeakMonth)
        {
            score = Math.Min(score, 50);
        }

        if (row.IsOffSeason)
        {
            score = Math.Min(score, 35);
        }

        if (row.IsPostHarvestAvailability)
        {
            score = Math.Min(score, 30);
        }

        if (isPeakMonth)
        {
            score = Math.Clamp(Math.Max(score, row.SeasonScoreWeight) + PeakSeasonBonus, 0, MaximumSeasonalityScore);
        }

        var badgeLabel = isPeakMonth
            ? "Mùa ngon nhất"
            : isInWindow
                ? "Đang vào mùa"
                : "Gợi ý hôm nay";

        return new ProductSeasonalitySignal(
            row.ProductId,
            score,
            isInWindow,
            isPeakMonth,
            row.SeasonLabel,
            badgeLabel);
    }

    private static bool IsValidMonth(int month)
        => month is >= MinimumMonth and <= MaximumMonth;
}

public sealed record ProductSeasonalitySignal(
    int ProductId,
    int Score,
    bool IsInSeason,
    bool IsPeakSeason,
    string? SeasonLabel,
    string? BadgeLabel)
{
    public static ProductSeasonalitySignal Empty(int productId)
        => new(productId, 0, false, false, null, null);
}
