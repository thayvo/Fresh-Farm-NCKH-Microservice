using FreshFarm.Ordering.Api.Services;
using Xunit;

namespace FreshFarm.Ordering.Api.Tests;

public sealed class SeasonalityScoringTests
{
    [Fact]
    public void IsMonthInSeason_ReturnsTrue_ForRegularSeasonWindow()
    {
        Assert.True(SeasonalityScoring.IsMonthInSeason(month: 4, startMonth: 3, endMonth: 5));
        Assert.False(SeasonalityScoring.IsMonthInSeason(month: 6, startMonth: 3, endMonth: 5));
    }

    [Fact]
    public void IsMonthInSeason_ReturnsTrue_ForCrossYearSeasonWindow()
    {
        Assert.True(SeasonalityScoring.IsMonthInSeason(month: 12, startMonth: 11, endMonth: 4));
        Assert.True(SeasonalityScoring.IsMonthInSeason(month: 3, startMonth: 11, endMonth: 4));
        Assert.False(SeasonalityScoring.IsMonthInSeason(month: 8, startMonth: 11, endMonth: 4));
    }

    [Fact]
    public void CalculateSignal_GivesLowerBaseScore_ForYearRoundProduct()
    {
        var signal = SeasonalityScoring.CalculateSignal(new CatalogProductSeasonalityItem
        {
            ProductId = 10,
            SeasonLabel = "Quanh năm",
            StartMonth = 1,
            EndMonth = 12,
            IsYearRound = true,
            SeasonScoreWeight = 80
        }, currentMonth: 7);

        Assert.Equal(50, signal.Score);
        Assert.True(signal.IsInSeason);
        Assert.Equal("Đang vào mùa", signal.BadgeLabel);
    }

    [Fact]
    public void CalculateSignal_AddsPeakBonus_ForYearRoundPeakMonth()
    {
        var signal = SeasonalityScoring.CalculateSignal(new CatalogProductSeasonalityItem
        {
            ProductId = 10,
            SeasonLabel = "Quanh năm, rộ hè",
            StartMonth = 1,
            EndMonth = 12,
            PeakMonths = "5-8",
            IsYearRound = true,
            HasPeakSeason = true,
            SeasonScoreWeight = 80
        }, currentMonth: 7);

        Assert.Equal(100, signal.Score);
        Assert.True(signal.IsPeakSeason);
        Assert.Equal("Mùa ngon nhất", signal.BadgeLabel);
    }

    [Fact]
    public void BuildBestSignals_PicksHighestMatchingSeasonalityRow()
    {
        var rows = new[]
        {
            new CatalogProductSeasonalityItem
            {
                ProductId = 20,
                SeasonLabel = "Vụ phụ",
                StartMonth = 3,
                EndMonth = 5,
                IsOffSeason = true,
                SeasonScoreWeight = 35
            },
            new CatalogProductSeasonalityItem
            {
                ProductId = 20,
                SeasonLabel = "Vụ chính",
                StartMonth = 4,
                EndMonth = 6,
                SeasonScoreWeight = 100
            }
        };

        var signals = SeasonalityScoring.BuildBestSignals([20], rows, currentMonth: 4);

        Assert.True(signals.TryGetValue(20, out var signal));
        Assert.Equal(100, signal.Score);
        Assert.Equal("Vụ chính", signal.SeasonLabel);
    }

    [Fact]
    public void BuildBestSignals_ReturnsNoSignal_WhenProductHasNoSeasonality()
    {
        var signals = SeasonalityScoring.BuildBestSignals(
            [99],
            [new CatalogProductSeasonalityItem
            {
                ProductId = 20,
                SeasonLabel = "Vụ chính",
                StartMonth = 3,
                EndMonth = 5,
                SeasonScoreWeight = 100
            }],
            currentMonth: 4);

        Assert.Empty(signals);
    }
}
