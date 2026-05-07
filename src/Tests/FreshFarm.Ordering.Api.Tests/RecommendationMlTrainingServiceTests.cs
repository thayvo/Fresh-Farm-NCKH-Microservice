using FreshFarm.Ordering.Api.Models;
using FreshFarm.Ordering.Api.Options;
using FreshFarm.Ordering.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace FreshFarm.Ordering.Api.Tests;

public sealed class RecommendationMlTrainingServiceTests
{
    [Fact]
    public async Task RebuildUserProductScoresAsync_Skips_WhenDisabledAndNotForced()
    {
        await using var db = CreateDbContext();
        var service = CreateService(db, new RecommendationMlOptions
        {
            Enabled = false,
            PersistModelArtifact = false
        });

        var result = await service.RebuildUserProductScoresAsync(
            force: false,
            materializeUserProductScoresOverride: null,
            CancellationToken.None);

        Assert.True(result.Skipped);
        Assert.Equal("recommendation_ml_disabled", result.SkipReason);
        Assert.Empty(db.RecommendationUserProductScores);
    }

    [Fact]
    public async Task RebuildUserProductScoresAsync_TrainsModelAndMaterializesPredictions_WhenForced()
    {
        await using var db = CreateDbContext();
        SeedInteractions(db);
        await db.SaveChangesAsync();

        var service = CreateService(db, new RecommendationMlOptions
        {
            Enabled = false,
            PersistModelArtifact = false,
            MinInteractionRows = 3,
            MinDistinctUsers = 2,
            MinDistinctProducts = 2,
            NumberOfIterations = 3,
            ApproximationRank = 8,
            TopNPerUser = 2,
            MaxUsersPerRefresh = 2,
            MaxCandidateProducts = 3,
            MaxTrainingPairs = 20
        });

        var result = await service.RebuildUserProductScoresAsync(
            force: true,
            materializeUserProductScoresOverride: null,
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.False(result.Skipped);
        Assert.Equal("mlnet_matrix_factorization_v1", result.Algorithm);
        Assert.True(result.TrainingRowCount >= 3);
        Assert.True(result.MaterializedUserProductScoreRowCount > 0);

        var rows = await db.RecommendationUserProductScores
            .AsNoTracking()
            .OrderBy(item => item.UserId)
            .ThenByDescending(item => item.UserProductScore)
            .ToListAsync();
        Assert.NotEmpty(rows);
        Assert.All(rows, row => Assert.InRange(row.UserProductScore, 20d, 100d));
        Assert.Contains(rows, row => row.UserId == 1);
        Assert.Contains(rows, row => row.UserId == 2);
    }

    [Fact]
    public async Task RebuildUserProductScoresAsync_AddsWeakNegativeSamples_WhenRepeatedImpressionsHaveNoInteraction()
    {
        await using var db = CreateDbContext();
        SeedRepeatedImpressionsWithoutClicks(db);
        await db.SaveChangesAsync();

        var service = CreateService(db, new RecommendationMlOptions
        {
            Enabled = false,
            PersistModelArtifact = false,
            MinInteractionRows = 4,
            MinDistinctUsers = 2,
            MinDistinctProducts = 2,
            NumberOfIterations = 2,
            ApproximationRank = 4,
            TopNPerUser = 2,
            MaxUsersPerRefresh = 2,
            MaxCandidateProducts = 2,
            MaxTrainingPairs = 10
        });

        var result = await service.RebuildUserProductScoresAsync(
            force: true,
            materializeUserProductScoresOverride: null,
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(4, result.InteractionRowCount);
        Assert.Equal(4, result.TrainingRowCount);
        Assert.Equal(2, result.DistinctUserCount);
        Assert.Equal(2, result.DistinctProductCount);
        Assert.NotEmpty(db.RecommendationUserProductScores);
    }

    private static void SeedInteractions(FreshFarmOrderingDBContext db)
    {
        var now = DateTime.UtcNow;
        db.ProductViewEvents.AddRange(
            new ProductViewEvent { ProductViewEventId = 1, UserId = 1, SessionId = "u1-a", ProductId = 101, SourcePage = "home", SourceModule = "card", CreatedAt = now.AddDays(-5) },
            new ProductViewEvent { ProductViewEventId = 2, UserId = 1, SessionId = "u1-b", ProductId = 102, SourcePage = "home", SourceModule = "card", CreatedAt = now.AddDays(-4) },
            new ProductViewEvent { ProductViewEventId = 3, UserId = 2, SessionId = "u2-a", ProductId = 102, SourcePage = "home", SourceModule = "card", CreatedAt = now.AddDays(-3) },
            new ProductViewEvent { ProductViewEventId = 4, UserId = 2, SessionId = "u2-b", ProductId = 103, SourcePage = "home", SourceModule = "card", CreatedAt = now.AddDays(-2) });

        db.SearchClickEvents.AddRange(
            new SearchClickEvent { SearchClickEventId = 1, UserId = 1, SessionId = "u1-search", ProductId = 101, Rank = 1, CreatedAt = now.AddDays(-2) },
            new SearchClickEvent { SearchClickEventId = 2, UserId = 2, SessionId = "u2-search", ProductId = 103, Rank = 1, CreatedAt = now.AddDays(-1) });

        db.RecommendationClickEvents.Add(
            new RecommendationClickEvent { RecommendationClickEventId = 1, UserId = 1, SessionId = "u1-rec", ProductId = 102, Placement = "home_today", Algorithm = "heuristic", CreatedAt = now.AddHours(-18) });

        db.Orders.AddRange(
            new Order
            {
                OrderId = 1,
                UserId = 1,
                OrderDate = now.AddDays(-1),
                ShippingFee = 0,
                TotalAmount = 100_000m,
                OrderNote = string.Empty,
                Status = "Delivered",
                PaymentStatus = "Paid",
                BuyerFullName = "Buyer 1",
                BuyerPhone = "0900000001",
                BuyerEmail = "buyer1@example.com"
            },
            new Order
            {
                OrderId = 2,
                UserId = 2,
                OrderDate = now.AddHours(-12),
                ShippingFee = 0,
                TotalAmount = 120_000m,
                OrderNote = string.Empty,
                Status = "Completed",
                PaymentStatus = "Paid",
                BuyerFullName = "Buyer 2",
                BuyerPhone = "0900000002",
                BuyerEmail = "buyer2@example.com"
            });

        db.OrderDetails.AddRange(
            new OrderDetail { OrderDetailId = 1, OrderId = 1, ProductId = 101, Quantity = 2, UnitPrice = 50_000m, UnitSymbol = "kg" },
            new OrderDetail { OrderDetailId = 2, OrderId = 2, ProductId = 103, Quantity = 2, UnitPrice = 60_000m, UnitSymbol = "kg" });
    }

    private static void SeedRepeatedImpressionsWithoutClicks(FreshFarmOrderingDBContext db)
    {
        var now = DateTime.UtcNow;
        var id = 1;
        foreach (var userId in new[] { 1, 2 })
        {
            foreach (var productId in new[] { 201, 202 })
            {
                db.RecommendationImpressions.AddRange(
                    new RecommendationImpression { Id = id++, UserId = userId, ProductId = productId, Position = 1, RecommendationSource = "ML", ExperimentGroup = "A", Timestamp = now.AddHours(-3) },
                    new RecommendationImpression { Id = id++, UserId = userId, ProductId = productId, Position = 4, RecommendationSource = "ML", ExperimentGroup = "A", Timestamp = now.AddHours(-2) },
                    new RecommendationImpression { Id = id++, UserId = userId, ProductId = productId, Position = 7, RecommendationSource = "ML", ExperimentGroup = "A", Timestamp = now.AddHours(-1) });
            }
        }
    }

    private static RecommendationMlTrainingService CreateService(
        FreshFarmOrderingDBContext db,
        RecommendationMlOptions options)
        => new(
            db,
            new TestOptionsMonitor<RecommendationMlOptions>(options),
            new TestHostEnvironment(),
            NullLogger<RecommendationMlTrainingService>.Instance);

    private static FreshFarmOrderingDBContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<FreshFarmOrderingDBContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new FreshFarmOrderingDBContext(options);
    }

    private sealed class TestOptionsMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue => value;

        public T Get(string? name) => value;

        public IDisposable OnChange(Action<T, string?> listener) => NullDisposable.Instance;
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;

        public string ApplicationName { get; set; } = "FreshFarm.Ordering.Api.Tests";

        public string ContentRootPath { get; set; } = Path.GetTempPath();

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class NullDisposable : IDisposable
    {
        public static readonly NullDisposable Instance = new();

        public void Dispose()
        {
        }
    }
}
