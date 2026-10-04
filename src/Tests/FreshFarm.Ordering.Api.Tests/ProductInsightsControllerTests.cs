using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using FreshFarm.Ordering.Api.Controllers;
using FreshFarm.Ordering.Api.Models;
using FreshFarm.Ordering.Api.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace FreshFarm.Ordering.Api.Tests;

public sealed class ProductInsightsControllerTests
{
    [Fact]
    public async Task GetStats_AggregatesDeliveredSalesAndApprovedReviews()
    {
        await using var db = CreateDbContext();

        db.Orders.AddRange(
            new Order
            {
                OrderId = 1,
                UserId = 10,
                OrderDate = DateTime.UtcNow.AddDays(-2),
                ShippingFee = 0,
                TotalAmount = 120_000m,
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
                UserId = 11,
                OrderDate = DateTime.UtcNow.AddDays(-1),
                ShippingFee = 0,
                TotalAmount = 80_000m,
                OrderNote = string.Empty,
                Status = "Pending",
                PaymentStatus = "Pending",
                BuyerFullName = "Buyer 2",
                BuyerPhone = "0900000002",
                BuyerEmail = "buyer2@example.com"
            },
            new Order
            {
                OrderId = 3,
                UserId = 12,
                OrderDate = DateTime.UtcNow.AddDays(-120),
                ShippingFee = 0,
                TotalAmount = 280_000m,
                OrderNote = string.Empty,
                Status = "Delivered",
                PaymentStatus = "Paid",
                BuyerFullName = "Buyer 3",
                BuyerPhone = "0900000003",
                BuyerEmail = "buyer3@example.com"
            });

        db.OrderDetails.AddRange(
            new OrderDetail
            {
                OrderDetailId = 1,
                OrderId = 1,
                ProductId = 104,
                Quantity = 3,
                UnitPrice = 40_000m,
                UnitSymbol = "kg"
            },
            new OrderDetail
            {
                OrderDetailId = 2,
                OrderId = 2,
                ProductId = 104,
                Quantity = 9,
                UnitPrice = 40_000m,
                UnitSymbol = "kg"
            },
            new OrderDetail
            {
                OrderDetailId = 3,
                OrderId = 1,
                ProductId = 205,
                Quantity = 2,
                UnitPrice = 30_000m,
                UnitSymbol = "kg"
            },
            new OrderDetail
            {
                OrderDetailId = 4,
                OrderId = 3,
                ProductId = 104,
                Quantity = 7,
                UnitPrice = 40_000m,
                UnitSymbol = "kg"
            });

        db.Reviews.AddRange(
            new Review
            {
                ReviewId = 1,
                ProductId = 104,
                UserId = 10,
                Rating = 5,
                Comment = "Rat ngon",
                CreatedAt = DateTime.UtcNow.AddDays(-2),
                IsApproved = true,
                IsDeleted = false
            },
            new Review
            {
                ReviewId = 2,
                ProductId = 104,
                UserId = 11,
                Rating = 4,
                Comment = "On",
                CreatedAt = DateTime.UtcNow.AddDays(-1),
                IsApproved = true,
                IsDeleted = false
            },
            new Review
            {
                ReviewId = 3,
                ProductId = 104,
                UserId = 12,
                Rating = 1,
                Comment = "Khong duoc duyet",
                CreatedAt = DateTime.UtcNow.AddHours(-12),
                IsApproved = false,
                IsDeleted = false
            },
            new Review
            {
                ReviewId = 4,
                ProductId = 205,
                UserId = 13,
                Rating = 3,
                Comment = "Da xoa",
                CreatedAt = DateTime.UtcNow.AddHours(-6),
                IsApproved = true,
                IsDeleted = true
            });

        await db.SaveChangesAsync();

        var controller = CreateController(db);

        var result = await controller.GetStats([104, 205], recentWindowDays: 90, cancellationToken: CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        using var payload = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value));
        var items = payload.RootElement.EnumerateArray().ToList();
        Assert.Equal(2, items.Count);

        var product104 = items.Single(item => item.GetProperty("ProductId").GetInt32() == 104);
        Assert.Equal(10, product104.GetProperty("SoldCount").GetInt32());
        Assert.Equal(3, product104.GetProperty("RecentSoldCount").GetInt32());
        Assert.Equal(2, product104.GetProperty("ReviewCount").GetInt32());
        Assert.Equal(4.5m, product104.GetProperty("AverageRating").GetDecimal());

        var product205 = items.Single(item => item.GetProperty("ProductId").GetInt32() == 205);
        Assert.Equal(2, product205.GetProperty("SoldCount").GetInt32());
        Assert.Equal(2, product205.GetProperty("RecentSoldCount").GetInt32());
        Assert.Equal(0, product205.GetProperty("ReviewCount").GetInt32());
        Assert.Equal(0m, product205.GetProperty("AverageRating").GetDecimal());
    }

    [Fact]
    public async Task GetStats_ReturnsEmptyArray_WhenProductIdsAreMissing()
    {
        await using var db = CreateDbContext();
        var controller = CreateController(db);

        var result = await controller.GetStats([0, -3], cancellationToken: CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        using var payload = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value));
        Assert.Empty(payload.RootElement.EnumerateArray());
    }

    [Fact]
    public async Task GetUserProductScores_ReturnsMaterializedLongTermScores_ForResolvedUser()
    {
        await using var db = CreateDbContext();

        db.RecommendationUserProductScores.AddRange(
            new RecommendationUserProductScore
            {
                RecommendationUserProductScoreId = 1,
                UserId = 42,
                ProductId = 901,
                ViewCount = 3,
                SearchClickCount = 2,
                RecommendationClickCount = 1,
                PurchaseCount = 4,
                UserProductScore = 250,
                LastInteractedAtUtc = DateTime.UtcNow.AddHours(-2),
                ComputedAt = DateTime.UtcNow
            },
            new RecommendationUserProductScore
            {
                RecommendationUserProductScoreId = 2,
                UserId = 42,
                ProductId = 902,
                ViewCount = 1,
                SearchClickCount = 0,
                RecommendationClickCount = 0,
                PurchaseCount = 1,
                UserProductScore = 60,
                LastInteractedAtUtc = DateTime.UtcNow.AddDays(-2),
                ComputedAt = DateTime.UtcNow
            },
            new RecommendationUserProductScore
            {
                RecommendationUserProductScoreId = 3,
                UserId = 9,
                ProductId = 903,
                ViewCount = 9,
                SearchClickCount = 9,
                RecommendationClickCount = 9,
                PurchaseCount = 9,
                UserProductScore = 999,
                LastInteractedAtUtc = DateTime.UtcNow,
                ComputedAt = DateTime.UtcNow
            });

        await db.SaveChangesAsync();

        var controller = CreateController(db, userId: 42);
        var result = await controller.GetUserProductScores(null, [901, 902, 903], limit: 5, cancellationToken: CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        using var payload = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value));
        var items = payload.RootElement.EnumerateArray().ToList();

        Assert.Equal(2, items.Count);
        Assert.Equal(901, items[0].GetProperty("ProductId").GetInt32());
        Assert.Equal(4, items[0].GetProperty("PurchaseCount").GetInt32());
        Assert.Equal(250d, items[0].GetProperty("UserProductScore").GetDouble());
        Assert.Equal("mlnet_user_product_v1", controller.Response.Headers["X-Recommendation-Signal-Source"].ToString());
        Assert.Equal(JsonValueKind.String, items[0].GetProperty("ComputedAtUtc").ValueKind);
        Assert.DoesNotContain(items, item => item.GetProperty("ProductId").GetInt32() == 903);
    }

    [Fact]
    public async Task GetUserSellerScores_ReturnsMaterializedLongTermScores_ForResolvedUser()
    {
        await using var db = CreateDbContext();

        db.RecommendationUserSellerScores.AddRange(
            new RecommendationUserSellerScore
            {
                RecommendationUserSellerScoreId = 1,
                UserId = 42,
                SellerId = 4,
                ViewCount = 3,
                SearchClickCount = 2,
                PurchaseCount = 4,
                UserSellerScore = 280,
                LastInteractedAtUtc = DateTime.UtcNow.AddHours(-2),
                ComputedAt = DateTime.UtcNow
            },
            new RecommendationUserSellerScore
            {
                RecommendationUserSellerScoreId = 2,
                UserId = 42,
                SellerId = 7,
                ViewCount = 1,
                SearchClickCount = 0,
                PurchaseCount = 1,
                UserSellerScore = 60,
                LastInteractedAtUtc = DateTime.UtcNow.AddDays(-2),
                ComputedAt = DateTime.UtcNow
            },
            new RecommendationUserSellerScore
            {
                RecommendationUserSellerScoreId = 3,
                UserId = 9,
                SellerId = 99,
                ViewCount = 9,
                SearchClickCount = 9,
                PurchaseCount = 9,
                UserSellerScore = 999,
                LastInteractedAtUtc = DateTime.UtcNow,
                ComputedAt = DateTime.UtcNow
            });

        await db.SaveChangesAsync();

        var controller = CreateController(db, authenticatedUserIdFromScheme: 42);
        var result = await controller.GetUserSellerScores(null, [4, 7, 99], limit: 5, cancellationToken: CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        using var payload = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value));
        var items = payload.RootElement.EnumerateArray().ToList();

        Assert.Equal(2, items.Count);
        Assert.Equal(4, items[0].GetProperty("SellerId").GetInt32());
        Assert.Equal(4, items[0].GetProperty("PurchaseCount").GetInt32());
        Assert.Equal(280d, items[0].GetProperty("UserSellerScore").GetDouble());
        Assert.DoesNotContain(items, item => item.GetProperty("SellerId").GetInt32() == 99);
    }

    [Fact]
    public async Task GetBasketAffinity_ReturnsMaterializedCoPurchaseCandidates()
    {
        await using var db = CreateDbContext();

        db.RecommendationBasketAffinities.AddRange(
            new RecommendationBasketAffinity
            {
                RecommendationBasketAffinityId = 1,
                ProductId = 501,
                CandidateProductId = 601,
                CoPurchaseOrderCount = 5,
                BasketScore = 180,
                ComputedAt = DateTime.UtcNow
            },
            new RecommendationBasketAffinity
            {
                RecommendationBasketAffinityId = 2,
                ProductId = 501,
                CandidateProductId = 602,
                CoPurchaseOrderCount = 2,
                BasketScore = 66,
                ComputedAt = DateTime.UtcNow
            });

        await db.SaveChangesAsync();

        var controller = CreateController(db);
        var result = await controller.GetBasketAffinity(501, [601, 602], limit: 5, cancellationToken: CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        using var payload = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value));
        var items = payload.RootElement.EnumerateArray().ToList();

        Assert.Equal(2, items.Count);
        Assert.Equal(601, items[0].GetProperty("CandidateProductId").GetInt32());
        Assert.Equal(5, items[0].GetProperty("CoPurchaseOrderCount").GetInt32());
        Assert.Equal(180d, items[0].GetProperty("BasketScore").GetDouble());
    }

    [Fact]
    public async Task GetReplenishmentProfile_ReturnsMaterializedReorderCandidates_ForResolvedUser()
    {
        await using var db = CreateDbContext();

        db.RecommendationReplenishmentProfiles.AddRange(
            new RecommendationReplenishmentProfile
            {
                RecommendationReplenishmentProfileId = 1,
                UserId = 42,
                ProductId = 701,
                PurchaseCount = 3,
                LastPurchasedAtUtc = DateTime.UtcNow.AddDays(-8),
                AverageRepurchaseDays = 10,
                ExpectedReorderAtUtc = DateTime.UtcNow.AddDays(2),
                ReplenishmentScore = 72,
                ComputedAt = DateTime.UtcNow
            },
            new RecommendationReplenishmentProfile
            {
                RecommendationReplenishmentProfileId = 2,
                UserId = 42,
                ProductId = 702,
                PurchaseCount = 1,
                LastPurchasedAtUtc = DateTime.UtcNow.AddDays(-30),
                AverageRepurchaseDays = 0,
                ExpectedReorderAtUtc = null,
                ReplenishmentScore = 18,
                ComputedAt = DateTime.UtcNow
            });

        await db.SaveChangesAsync();

        var controller = CreateController(db, authenticatedUserIdFromScheme: 42);
        var result = await controller.GetReplenishmentProfile(null, [701, 702], limit: 5, cancellationToken: CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        using var payload = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value));
        var items = payload.RootElement.EnumerateArray().ToList();

        Assert.Equal(2, items.Count);
        Assert.Equal(701, items[0].GetProperty("ProductId").GetInt32());
        Assert.Equal(3, items[0].GetProperty("PurchaseCount").GetInt32());
        Assert.Equal(72d, items[0].GetProperty("ReplenishmentScore").GetDouble());
    }

    [Fact]
    public async Task GetSimilar_ReturnsCollaborativeSignals_FromOrdersAndSessions()
    {
        await using var db = CreateDbContext();

        db.Orders.AddRange(
            new Order
            {
                OrderId = 1,
                UserId = 10,
                OrderDate = DateTime.UtcNow.AddDays(-4),
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
                UserId = 11,
                OrderDate = DateTime.UtcNow.AddDays(-3),
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
            new OrderDetail { OrderDetailId = 1, OrderId = 1, ProductId = 104, Quantity = 2, UnitPrice = 30_000m, UnitSymbol = "kg" },
            new OrderDetail { OrderDetailId = 2, OrderId = 1, ProductId = 105, Quantity = 1, UnitPrice = 35_000m, UnitSymbol = "kg" },
            new OrderDetail { OrderDetailId = 3, OrderId = 2, ProductId = 104, Quantity = 2, UnitPrice = 30_000m, UnitSymbol = "kg" },
            new OrderDetail { OrderDetailId = 4, OrderId = 2, ProductId = 106, Quantity = 1, UnitPrice = 28_000m, UnitSymbol = "kg" });

        db.ProductViewEvents.AddRange(
            new ProductViewEvent { ProductViewEventId = 1, SessionId = "view-a", ProductId = 104, CreatedAt = DateTime.UtcNow.AddDays(-2) },
            new ProductViewEvent { ProductViewEventId = 2, SessionId = "view-a", ProductId = 105, CreatedAt = DateTime.UtcNow.AddDays(-2) },
            new ProductViewEvent { ProductViewEventId = 3, SessionId = "view-b", ProductId = 104, CreatedAt = DateTime.UtcNow.AddDays(-1) },
            new ProductViewEvent { ProductViewEventId = 4, SessionId = "view-b", ProductId = 105, CreatedAt = DateTime.UtcNow.AddDays(-1) },
            new ProductViewEvent { ProductViewEventId = 5, SessionId = "view-c", ProductId = 104, CreatedAt = DateTime.UtcNow.AddHours(-20) },
            new ProductViewEvent { ProductViewEventId = 6, SessionId = "view-c", ProductId = 106, CreatedAt = DateTime.UtcNow.AddHours(-20) });

        db.SearchClickEvents.AddRange(
            new SearchClickEvent { SearchClickEventId = 1, SessionId = "click-a", ProductId = 104, Rank = 1, CreatedAt = DateTime.UtcNow.AddHours(-10) },
            new SearchClickEvent { SearchClickEventId = 2, SessionId = "click-a", ProductId = 105, Rank = 2, CreatedAt = DateTime.UtcNow.AddHours(-10) });

        db.RecommendationClickEvents.AddRange(
            new RecommendationClickEvent { RecommendationClickEventId = 1, SessionId = "click-b", ProductId = 104, Placement = "home_today", Algorithm = "content", CreatedAt = DateTime.UtcNow.AddHours(-6) },
            new RecommendationClickEvent { RecommendationClickEventId = 2, SessionId = "click-b", ProductId = 106, Placement = "product_similar", Algorithm = "content", CreatedAt = DateTime.UtcNow.AddHours(-6) });

        await db.SaveChangesAsync();

        var controller = CreateController(db);

        var result = await controller.GetSimilar(104, [105, 106], limit: 4, cancellationToken: CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        using var payload = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value));
        var items = payload.RootElement.EnumerateArray().ToList();
        Assert.Equal(2, items.Count);

        var product105 = items.Single(item => item.GetProperty("ProductId").GetInt32() == 105);
        Assert.Equal(1, product105.GetProperty("CoPurchaseOrderCount").GetInt32());
        Assert.Equal(2, product105.GetProperty("CoViewSessionCount").GetInt32());
        Assert.Equal(1, product105.GetProperty("CoClickSessionCount").GetInt32());

        var product106 = items.Single(item => item.GetProperty("ProductId").GetInt32() == 106);
        Assert.Equal(1, product106.GetProperty("CoPurchaseOrderCount").GetInt32());
        Assert.Equal(1, product106.GetProperty("CoViewSessionCount").GetInt32());
        Assert.Equal(1, product106.GetProperty("CoClickSessionCount").GetInt32());

        Assert.Equal(105, items[0].GetProperty("ProductId").GetInt32());
        Assert.True(items[0].GetProperty("CollaborativeScore").GetDouble() > items[1].GetProperty("CollaborativeScore").GetDouble());
    }

    [Fact]
    public async Task GetSimilar_PrefersMaterializedAffinity_WhenAvailable()
    {
        await using var db = CreateDbContext();

        db.RecommendationProductAffinities.AddRange(
            new RecommendationProductAffinity
            {
                RecommendationProductAffinityId = 1,
                SeedProductId = 104,
                CandidateProductId = 106,
                CoPurchaseOrderCount = 3,
                CoViewSessionCount = 1,
                CoClickSessionCount = 1,
                AffinityScore = 116,
                ComputedAt = DateTime.UtcNow
            },
            new RecommendationProductAffinity
            {
                RecommendationProductAffinityId = 2,
                SeedProductId = 104,
                CandidateProductId = 105,
                CoPurchaseOrderCount = 1,
                CoViewSessionCount = 1,
                CoClickSessionCount = 0,
                AffinityScore = 38,
                ComputedAt = DateTime.UtcNow
            });

        await db.SaveChangesAsync();

        var controller = CreateController(db);
        var result = await controller.GetSimilar(104, [105, 106], limit: 4, cancellationToken: CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        using var payload = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value));
        var items = payload.RootElement.EnumerateArray().ToList();
        Assert.Equal(2, items.Count);
        Assert.Equal(106, items[0].GetProperty("ProductId").GetInt32());
        Assert.Equal(116d, items[0].GetProperty("CollaborativeScore").GetDouble());
        Assert.Equal(3, items[0].GetProperty("CoPurchaseOrderCount").GetInt32());
    }

    [Fact]
    public async Task GetSearchRanking_ReturnsKeywordSignals_FromMatchingSearchSessions()
    {
        await using var db = CreateDbContext();

        db.SearchEvents.AddRange(
            new SearchEvent
            {
                SearchEventId = 1,
                SessionId = "search-a",
                Keyword = "rau",
                ResultCount = 12,
                CreatedAt = DateTime.UtcNow.AddDays(-2)
            },
            new SearchEvent
            {
                SearchEventId = 2,
                SessionId = "search-b",
                Keyword = "rau",
                ResultCount = 8,
                CreatedAt = DateTime.UtcNow.AddDays(-1)
            },
            new SearchEvent
            {
                SearchEventId = 3,
                SessionId = "search-other",
                Keyword = "trai cay",
                ResultCount = 6,
                CreatedAt = DateTime.UtcNow.AddDays(-1)
            });

        db.SearchClickEvents.AddRange(
            new SearchClickEvent { SearchClickEventId = 1, SearchEventId = 1, SessionId = "search-a", ProductId = 501, Rank = 1, CreatedAt = DateTime.UtcNow.AddDays(-2) },
            new SearchClickEvent { SearchClickEventId = 2, SearchEventId = 1, SessionId = "search-a", ProductId = 502, Rank = 2, CreatedAt = DateTime.UtcNow.AddDays(-2) },
            new SearchClickEvent { SearchClickEventId = 3, SearchEventId = 2, SessionId = "search-b", ProductId = 502, Rank = 1, CreatedAt = DateTime.UtcNow.AddDays(-1) },
            new SearchClickEvent { SearchClickEventId = 4, SearchEventId = 3, SessionId = "search-other", ProductId = 503, Rank = 1, CreatedAt = DateTime.UtcNow.AddDays(-1) });

        db.ProductViewEvents.AddRange(
            new ProductViewEvent { ProductViewEventId = 1, SessionId = "search-a", ProductId = 501, CreatedAt = DateTime.UtcNow.AddDays(-2) },
            new ProductViewEvent { ProductViewEventId = 2, SessionId = "search-a", ProductId = 502, CreatedAt = DateTime.UtcNow.AddDays(-2) },
            new ProductViewEvent { ProductViewEventId = 3, SessionId = "search-b", ProductId = 502, CreatedAt = DateTime.UtcNow.AddDays(-1) });

        db.RecommendationClickEvents.Add(
            new RecommendationClickEvent
            {
                RecommendationClickEventId = 1,
                SessionId = "search-b",
                ProductId = 502,
                Placement = "search_related",
                Algorithm = "search_related_heuristic",
                CreatedAt = DateTime.UtcNow.AddDays(-1)
            });

        await db.SaveChangesAsync();

        var controller = CreateController(db);

        var result = await controller.GetSearchRanking("rau", [501, 502, 503], limit: 5, cancellationToken: CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        using var payload = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value));
        var items = payload.RootElement.EnumerateArray().ToList();
        Assert.Equal(2, items.Count);

        var product502 = items.Single(item => item.GetProperty("ProductId").GetInt32() == 502);
        Assert.Equal(2, product502.GetProperty("SearchClickCount").GetInt32());
        Assert.Equal(2, product502.GetProperty("SearchViewSessionCount").GetInt32());
        Assert.Equal(1, product502.GetProperty("SearchRecommendationClickCount").GetInt32());

        var product501 = items.Single(item => item.GetProperty("ProductId").GetInt32() == 501);
        Assert.Equal(1, product501.GetProperty("SearchClickCount").GetInt32());
        Assert.Equal(1, product501.GetProperty("SearchViewSessionCount").GetInt32());

        Assert.Equal(502, items[0].GetProperty("ProductId").GetInt32());
        Assert.True(items[0].GetProperty("HybridSearchScore").GetDouble() > items[1].GetProperty("HybridSearchScore").GetDouble());
        Assert.Equal("ad_hoc_cf_v1", controller.Response.Headers["X-Recommendation-Signal-Source"].ToString());
        Assert.Equal("no_materialized_keyword_affinity", controller.Response.Headers["X-Recommendation-Fallback-Reason"].ToString());
    }

    [Fact]
    public async Task GetSearchRanking_RequestsRefreshSignal_WhenFallingBackToAdHocSignals()
    {
        await using var db = CreateDbContext();

        db.SearchEvents.Add(
            new SearchEvent
            {
                SearchEventId = 1,
                SessionId = "search-refresh",
                Keyword = "rau",
                ResultCount = 5,
                CreatedAt = DateTime.UtcNow.AddDays(-1)
            });

        db.SearchClickEvents.Add(
            new SearchClickEvent
            {
                SearchClickEventId = 1,
                SearchEventId = 1,
                SessionId = "search-refresh",
                ProductId = 501,
                Rank = 1,
                CreatedAt = DateTime.UtcNow.AddHours(-10)
            });

        await db.SaveChangesAsync();

        var refreshSignal = new RecommendationAffinityRefreshSignal();
        var controller = CreateController(db, refreshSignal: refreshSignal);

        var result = await controller.GetSearchRanking("rau", [501], limit: 5, cancellationToken: CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal(1, refreshSignal.RequestCount);
        Assert.Equal("ad_hoc_cf_v1", controller.Response.Headers["X-Recommendation-Signal-Source"].ToString());
    }

    [Fact]
    public async Task GetSearchRanking_PrefersMaterializedKeywordAffinity_WhenAvailable()
    {
        await using var db = CreateDbContext();

        db.RecommendationSearchKeywordAffinities.AddRange(
            new RecommendationSearchKeywordAffinity
            {
                RecommendationSearchKeywordAffinityId = 1,
                Keyword = "rau",
                ProductId = 502,
                SearchClickCount = 4,
                SearchClickSessionCount = 3,
                SearchViewSessionCount = 2,
                SearchRecommendationClickCount = 1,
                HybridSearchScore = 108,
                ComputedAt = DateTime.UtcNow
            },
            new RecommendationSearchKeywordAffinity
            {
                RecommendationSearchKeywordAffinityId = 2,
                Keyword = "rau",
                ProductId = 501,
                SearchClickCount = 1,
                SearchClickSessionCount = 1,
                SearchViewSessionCount = 1,
                SearchRecommendationClickCount = 0,
                HybridSearchScore = 26,
                ComputedAt = DateTime.UtcNow
            });

        await db.SaveChangesAsync();

        var controller = CreateController(db);
        var result = await controller.GetSearchRanking("rau", [501, 502], limit: 5, cancellationToken: CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        using var payload = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value));
        var items = payload.RootElement.EnumerateArray().ToList();
        Assert.Equal(2, items.Count);
        Assert.Equal(502, items[0].GetProperty("ProductId").GetInt32());
        Assert.Equal(108d, items[0].GetProperty("HybridSearchScore").GetDouble());
        Assert.Equal(4, items[0].GetProperty("SearchClickCount").GetInt32());
        Assert.Equal("materialized_cf_v1", controller.Response.Headers["X-Recommendation-Signal-Source"].ToString());
        Assert.False(controller.Response.Headers.ContainsKey("X-Recommendation-Fallback-Reason"));
    }

    [Fact]
    public async Task GetSearchRanking_ReturnsFallbackReason_WhenKeywordHasNoTrackedSessions()
    {
        await using var db = CreateDbContext();

        var controller = CreateController(db);
        var result = await controller.GetSearchRanking("mini", [105, 118, 11], limit: 5, cancellationToken: CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        using var payload = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value));
        Assert.Empty(payload.RootElement.EnumerateArray());
        Assert.False(controller.Response.Headers.ContainsKey("X-Recommendation-Signal-Source"));
        Assert.Equal("no_tracked_keyword_sessions", controller.Response.Headers["X-Recommendation-Fallback-Reason"].ToString());
    }

    [Fact]
    public async Task GetSearchRanking_ReturnsBehaviorFallbackReason_WhenSessionsExistButNoSignalsMatchCandidates()
    {
        await using var db = CreateDbContext();

        db.SearchEvents.Add(
            new SearchEvent
            {
                SearchEventId = 1,
                SessionId = "search-mini",
                Keyword = "mini",
                ResultCount = 3,
                CreatedAt = DateTime.UtcNow.AddDays(-1)
            });

        await db.SaveChangesAsync();

        var controller = CreateController(db);
        var result = await controller.GetSearchRanking("mini", [105, 118, 11], limit: 5, cancellationToken: CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        using var payload = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value));
        Assert.Empty(payload.RootElement.EnumerateArray());
        Assert.False(controller.Response.Headers.ContainsKey("X-Recommendation-Signal-Source"));
        Assert.Equal("no_behavioral_signal_for_keyword", controller.Response.Headers["X-Recommendation-Fallback-Reason"].ToString());
    }

    [Fact]
    public async Task GetUserCategoryScores_ReturnsMaterializedLongTermScores_ForResolvedUser()
    {
        await using var db = CreateDbContext();

        db.RecommendationUserCategoryScores.AddRange(
            new RecommendationUserCategoryScore
            {
                RecommendationUserCategoryScoreId = 1,
                UserId = 10,
                CategoryId = 3,
                CategoryName = "Rau la",
                ViewCount = 4,
                SearchClickCount = 2,
                RecommendationClickCount = 1,
                PurchaseCount = 3,
                UserCategoryScore = 288,
                LastInteractedAtUtc = DateTime.UtcNow.AddHours(-8),
                ComputedAt = DateTime.UtcNow
            },
            new RecommendationUserCategoryScore
            {
                RecommendationUserCategoryScoreId = 2,
                UserId = 10,
                CategoryId = 7,
                CategoryName = "Nam",
                ViewCount = 1,
                SearchClickCount = 1,
                RecommendationClickCount = 0,
                PurchaseCount = 1,
                UserCategoryScore = 92,
                LastInteractedAtUtc = DateTime.UtcNow.AddDays(-2),
                ComputedAt = DateTime.UtcNow
            });

        await db.SaveChangesAsync();

        var controller = CreateController(db);
        var result = await controller.GetUserCategoryScores(userId: 10, categoryIds: [3, 7], limit: 5, cancellationToken: CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        using var payload = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value));
        var items = payload.RootElement.EnumerateArray().ToList();

        Assert.Equal(2, items.Count);
        Assert.Equal(3, items[0].GetProperty("CategoryId").GetInt32());
        Assert.Equal("Rau la", items[0].GetProperty("CategoryName").GetString());
        Assert.Equal(288d, items[0].GetProperty("UserCategoryScore").GetDouble());
        Assert.Equal(7, items[1].GetProperty("CategoryId").GetInt32());
    }

    [Fact]
    public async Task GetHomeProfile_ReturnsPreferenceSeeds_FromSessionAndPurchases()
    {
        await using var db = CreateDbContext();

        db.ProductViewEvents.AddRange(
            new ProductViewEvent { ProductViewEventId = 1, SessionId = "home-session", ProductId = 501, CreatedAt = DateTime.UtcNow.AddDays(-2) },
            new ProductViewEvent { ProductViewEventId = 2, SessionId = "home-session", ProductId = 501, CreatedAt = DateTime.UtcNow.AddDays(-1) },
            new ProductViewEvent { ProductViewEventId = 3, SessionId = "other-session", ProductId = 777, CreatedAt = DateTime.UtcNow.AddDays(-1) });

        db.SearchClickEvents.Add(
            new SearchClickEvent
            {
                SearchClickEventId = 1,
                SessionId = "home-session",
                ProductId = 501,
                Rank = 1,
                CreatedAt = DateTime.UtcNow.AddDays(-1)
            });

        db.RecommendationClickEvents.Add(
            new RecommendationClickEvent
            {
                RecommendationClickEventId = 1,
                SessionId = "home-session",
                ProductId = 502,
                Placement = "home_today",
                Algorithm = "content_based_home_v1",
                CreatedAt = DateTime.UtcNow.AddHours(-12)
            });

        await db.SaveChangesAsync();

        var controller = new ProductInsightsController(db);

        var result = await controller.GetHomeProfile("home-session", limit: 5, cancellationToken: CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        using var payload = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value));
        var items = payload.RootElement.EnumerateArray().ToList();
        Assert.Equal(2, items.Count);

        var product501 = items.Single(item => item.GetProperty("ProductId").GetInt32() == 501);
        Assert.Equal(2, product501.GetProperty("ViewCount").GetInt32());
        Assert.Equal(1, product501.GetProperty("SearchClickCount").GetInt32());
        Assert.Equal(0, product501.GetProperty("RecommendationClickCount").GetInt32());

        var product502 = items.Single(item => item.GetProperty("ProductId").GetInt32() == 502);
        Assert.Equal(1, product502.GetProperty("RecommendationClickCount").GetInt32());
        Assert.Equal(0, product502.GetProperty("SearchClickCount").GetInt32());

        Assert.Equal(501, items[0].GetProperty("ProductId").GetInt32());
        Assert.True(items[0].GetProperty("PreferenceScore").GetDouble() > items[1].GetProperty("PreferenceScore").GetDouble());
    }

    [Fact]
    public async Task GetHomeProfile_PrefersMaterializedSeeds_WhenAvailable()
    {
        await using var db = CreateDbContext();

        db.RecommendationHomePreferenceSeeds.AddRange(
            new RecommendationHomePreferenceSeed
            {
                RecommendationHomePreferenceSeedId = 1,
                ScopeType = "session",
                ScopeKey = "home-materialized",
                ProductId = 901,
                ViewCount = 2,
                SearchClickCount = 1,
                RecommendationClickCount = 0,
                PurchaseCount = 0,
                PreferenceScore = 48,
                LastInteractedAtUtc = DateTime.UtcNow.AddHours(-3),
                ComputedAt = DateTime.UtcNow
            },
            new RecommendationHomePreferenceSeed
            {
                RecommendationHomePreferenceSeedId = 2,
                ScopeType = "user",
                ScopeKey = "42",
                UserId = 42,
                ProductId = 901,
                ViewCount = 1,
                SearchClickCount = 0,
                RecommendationClickCount = 1,
                PurchaseCount = 0,
                PreferenceScore = 32,
                LastInteractedAtUtc = DateTime.UtcNow.AddHours(-2),
                ComputedAt = DateTime.UtcNow
            },
            new RecommendationHomePreferenceSeed
            {
                RecommendationHomePreferenceSeedId = 3,
                ScopeType = "user",
                ScopeKey = "42",
                UserId = 42,
                ProductId = 902,
                ViewCount = 0,
                SearchClickCount = 0,
                RecommendationClickCount = 0,
                PurchaseCount = 2,
                PreferenceScore = 70,
                LastInteractedAtUtc = DateTime.UtcNow.AddHours(-1),
                ComputedAt = DateTime.UtcNow
            });

        await db.SaveChangesAsync();

        var controller = CreateController(db, userId: 42);
        var result = await controller.GetHomeProfile("home-materialized", limit: 5, cancellationToken: CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        using var payload = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value));
        var items = payload.RootElement.EnumerateArray().ToList();

        Assert.Equal(2, items.Count);
        Assert.Equal(901, items[0].GetProperty("ProductId").GetInt32());

        var product901 = items.Single(item => item.GetProperty("ProductId").GetInt32() == 901);
        Assert.Equal(3, product901.GetProperty("ViewCount").GetInt32());
        Assert.Equal(1, product901.GetProperty("SearchClickCount").GetInt32());
        Assert.Equal(1, product901.GetProperty("RecommendationClickCount").GetInt32());
        Assert.Equal(80, product901.GetProperty("PreferenceScore").GetDouble());

        var product902 = items.Single(item => item.GetProperty("ProductId").GetInt32() == 902);
        Assert.Equal(2, product902.GetProperty("PurchaseCount").GetInt32());
        Assert.Equal("materialized_profile_v1", controller.Response.Headers["X-Recommendation-Signal-Source"].ToString());
        Assert.False(controller.Response.Headers.ContainsKey("X-Recommendation-Fallback-Reason"));
    }

    [Fact]
    public async Task GetHomeProfile_UsesJwtBearerAuthenticateResult_WhenHttpContextUserIsEmpty()
    {
        await using var db = CreateDbContext();

        db.Orders.Add(new Order
        {
            OrderId = 1,
            UserId = 42,
            OrderDate = DateTime.UtcNow.AddDays(-2),
            ShippingFee = 0,
            TotalAmount = 240_000m,
            OrderNote = string.Empty,
            Status = "Delivered",
            PaymentStatus = "Paid",
            BuyerFullName = "Buyer Jwt",
            BuyerPhone = "0900000042",
            BuyerEmail = "buyer42@example.com"
        });

        db.OrderDetails.Add(
            new OrderDetail
            {
                OrderDetailId = 1,
                OrderId = 1,
                ProductId = 901,
                Quantity = 3,
                UnitPrice = 80_000m,
                UnitSymbol = "kg"
            });

        await db.SaveChangesAsync();

        var controller = CreateController(db, authenticatedUserIdFromScheme: 42);
        var result = await controller.GetHomeProfile(null, limit: 5, cancellationToken: CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        using var payload = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value));
        var items = payload.RootElement.EnumerateArray().ToList();

        Assert.Single(items);
        Assert.Equal(901, items[0].GetProperty("ProductId").GetInt32());
        Assert.Equal(3, items[0].GetProperty("PurchaseCount").GetInt32());
        Assert.Equal("ad_hoc_profile_v1", controller.Response.Headers["X-Recommendation-Signal-Source"].ToString());
        Assert.Equal("no_materialized_profile_seed", controller.Response.Headers["X-Recommendation-Fallback-Reason"].ToString());
    }

    [Fact]
    public async Task GetHomeProfile_RequestsRefreshSignal_WhenFallingBackToAdHocProfile()
    {
        await using var db = CreateDbContext();

        db.ProductViewEvents.Add(
            new ProductViewEvent
            {
                ProductViewEventId = 1,
                SessionId = "home-refresh",
                ProductId = 901,
                CreatedAt = DateTime.UtcNow.AddHours(-3)
            });

        await db.SaveChangesAsync();

        var refreshSignal = new RecommendationAffinityRefreshSignal();
        var controller = CreateController(db, refreshSignal: refreshSignal);
        var result = await controller.GetHomeProfile("home-refresh", limit: 5, cancellationToken: CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal(1, refreshSignal.RequestCount);
        Assert.Equal("ad_hoc_profile_v1", controller.Response.Headers["X-Recommendation-Signal-Source"].ToString());
        Assert.Equal("no_materialized_profile_seed", controller.Response.Headers["X-Recommendation-Fallback-Reason"].ToString());
    }

    [Fact]
    public async Task GetHomeProfile_ReturnsInteractionFallbackReason_WhenNoPreferenceInteractionsExist()
    {
        await using var db = CreateDbContext();

        var controller = CreateController(db);
        var result = await controller.GetHomeProfile("cold-home-session", limit: 5, cancellationToken: CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        using var payload = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value));
        Assert.Empty(payload.RootElement.EnumerateArray());
        Assert.False(controller.Response.Headers.ContainsKey("X-Recommendation-Signal-Source"));
        Assert.Equal("no_tracked_preference_interactions", controller.Response.Headers["X-Recommendation-Fallback-Reason"].ToString());
    }

    [Fact]
    public async Task GetHomeCollaborative_ReturnsCandidates_FromSeedInteractions()
    {
        await using var db = CreateDbContext();

        db.ProductViewEvents.AddRange(
            new ProductViewEvent { ProductViewEventId = 1, SessionId = "home-collab", ProductId = 501, CreatedAt = DateTime.UtcNow.AddDays(-2) },
            new ProductViewEvent { ProductViewEventId = 2, SessionId = "seed-view-a", ProductId = 501, CreatedAt = DateTime.UtcNow.AddDays(-3) },
            new ProductViewEvent { ProductViewEventId = 3, SessionId = "seed-view-a", ProductId = 601, CreatedAt = DateTime.UtcNow.AddDays(-3) },
            new ProductViewEvent { ProductViewEventId = 4, SessionId = "seed-view-b", ProductId = 501, CreatedAt = DateTime.UtcNow.AddDays(-2) },
            new ProductViewEvent { ProductViewEventId = 5, SessionId = "seed-view-b", ProductId = 601, CreatedAt = DateTime.UtcNow.AddDays(-2) });

        db.SearchClickEvents.AddRange(
            new SearchClickEvent { SearchClickEventId = 1, SessionId = "home-collab", ProductId = 501, Rank = 1, CreatedAt = DateTime.UtcNow.AddDays(-1) },
            new SearchClickEvent { SearchClickEventId = 2, SessionId = "seed-click-a", ProductId = 501, Rank = 1, CreatedAt = DateTime.UtcNow.AddDays(-2) },
            new SearchClickEvent { SearchClickEventId = 3, SessionId = "seed-click-a", ProductId = 602, Rank = 2, CreatedAt = DateTime.UtcNow.AddDays(-2) });

        db.Orders.Add(new Order
        {
            OrderId = 1,
            UserId = 10,
            OrderDate = DateTime.UtcNow.AddDays(-3),
            ShippingFee = 0,
            TotalAmount = 120_000m,
            OrderNote = string.Empty,
            Status = "Delivered",
            PaymentStatus = "Paid",
            BuyerFullName = "Buyer 1",
            BuyerPhone = "0900000001",
            BuyerEmail = "buyer1@example.com"
        });
        db.OrderDetails.AddRange(
            new OrderDetail { OrderDetailId = 1, OrderId = 1, ProductId = 501, Quantity = 1, UnitPrice = 40_000m, UnitSymbol = "kg" },
            new OrderDetail { OrderDetailId = 2, OrderId = 1, ProductId = 603, Quantity = 2, UnitPrice = 35_000m, UnitSymbol = "kg" });

        await db.SaveChangesAsync();

        var controller = new ProductInsightsController(db);
        var result = await controller.GetHomeCollaborative("home-collab", limit: 5, cancellationToken: CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        using var payload = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value));
        var items = payload.RootElement.EnumerateArray().ToList();

        Assert.Equal(3, items.Count);
        Assert.Equal(603, items[0].GetProperty("ProductId").GetInt32());
        Assert.True(items[0].GetProperty("CoPurchaseOrderCount").GetInt32() > 0);
        Assert.Contains(items, item => item.GetProperty("ProductId").GetInt32() == 601);
        Assert.Contains(items, item => item.GetProperty("ProductId").GetInt32() == 602);
    }

    [Fact]
    public async Task GetHomeCollaborative_PrefersMaterializedAffinity_WhenAvailable()
    {
        await using var db = CreateDbContext();

        db.RecommendationHomePreferenceSeeds.Add(
            new RecommendationHomePreferenceSeed
            {
                RecommendationHomePreferenceSeedId = 1,
                ScopeType = "session",
                ScopeKey = "home-collab-materialized",
                ProductId = 501,
                ViewCount = 1,
                SearchClickCount = 1,
                RecommendationClickCount = 0,
                PurchaseCount = 0,
                PreferenceScore = 38,
                LastInteractedAtUtc = DateTime.UtcNow.AddHours(-12),
                ComputedAt = DateTime.UtcNow
            });

        db.RecommendationHomeCollaborativeCandidates.AddRange(
            new RecommendationHomeCollaborativeCandidate
            {
                RecommendationHomeCollaborativeCandidateId = 1,
                ScopeType = "session",
                ScopeKey = "home-collab-materialized",
                ProductId = 601,
                CoPurchaseOrderCount = 2,
                CoViewSessionCount = 1,
                CoClickSessionCount = 1,
                CollaborativeScore = 84,
                ComputedAt = DateTime.UtcNow
            },
            new RecommendationHomeCollaborativeCandidate
            {
                RecommendationHomeCollaborativeCandidateId = 2,
                ScopeType = "session",
                ScopeKey = "home-collab-materialized",
                ProductId = 602,
                CoPurchaseOrderCount = 1,
                CoViewSessionCount = 0,
                CoClickSessionCount = 1,
                CollaborativeScore = 46,
                ComputedAt = DateTime.UtcNow
            });

        await db.SaveChangesAsync();

        var controller = new ProductInsightsController(db);
        var result = await controller.GetHomeCollaborative("home-collab-materialized", limit: 5, cancellationToken: CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        using var payload = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value));
        var items = payload.RootElement.EnumerateArray().ToList();
        Assert.Equal(2, items.Count);
        Assert.Equal(601, items[0].GetProperty("ProductId").GetInt32());
        Assert.True(items[0].GetProperty("CollaborativeScore").GetDouble() > items[1].GetProperty("CollaborativeScore").GetDouble());
        Assert.Equal(2, items[0].GetProperty("CoPurchaseOrderCount").GetInt32());
    }

    [Fact]
    public async Task GetHomeCollaborative_ReturnsCollaborativeSeedFallbackReason_WhenNoPreferenceSeedsExist()
    {
        await using var db = CreateDbContext();

        var controller = CreateController(db);
        var result = await controller.GetHomeCollaborative("cold-start-session", limit: 5, cancellationToken: CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        using var payload = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value));
        Assert.Empty(payload.RootElement.EnumerateArray());
        Assert.False(controller.Response.Headers.ContainsKey("X-Recommendation-Signal-Source"));
        Assert.Equal("no_preference_seed_for_collaborative", controller.Response.Headers["X-Recommendation-Fallback-Reason"].ToString());
    }

    [Fact]
    public async Task GetSimilar_ReturnsInteractionFallbackReason_WhenNoCollaborativeSignalsExist()
    {
        await using var db = CreateDbContext();

        var controller = CreateController(db);
        var result = await controller.GetSimilar(104, [105, 106], limit: 4, cancellationToken: CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        using var payload = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value));
        Assert.Empty(payload.RootElement.EnumerateArray());
        Assert.False(controller.Response.Headers.ContainsKey("X-Recommendation-Signal-Source"));
        Assert.Equal("no_collaborative_interactions_for_seed_product", controller.Response.Headers["X-Recommendation-Fallback-Reason"].ToString());
    }

    [Fact]
    public async Task GetSimilar_RequestsRefreshSignal_WhenFallingBackToAdHocCollaborativeSignals()
    {
        await using var db = CreateDbContext();

        db.ProductViewEvents.AddRange(
            new ProductViewEvent { ProductViewEventId = 1, SessionId = "view-refresh", ProductId = 104, CreatedAt = DateTime.UtcNow.AddDays(-1) },
            new ProductViewEvent { ProductViewEventId = 2, SessionId = "view-refresh", ProductId = 105, CreatedAt = DateTime.UtcNow.AddDays(-1) });

        await db.SaveChangesAsync();

        var refreshSignal = new RecommendationAffinityRefreshSignal();
        var controller = CreateController(db, refreshSignal: refreshSignal);
        var result = await controller.GetSimilar(104, [105], limit: 4, cancellationToken: CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal(1, refreshSignal.RequestCount);
        Assert.Equal("ad_hoc_cf_v1", controller.Response.Headers["X-Recommendation-Signal-Source"].ToString());
        Assert.Equal("no_materialized_collaborative_candidates", controller.Response.Headers["X-Recommendation-Fallback-Reason"].ToString());
    }

    private static FreshFarmOrderingDBContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<FreshFarmOrderingDBContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new FreshFarmOrderingDBContext(options);
    }

    private static ProductInsightsController CreateController(
        FreshFarmOrderingDBContext db,
        int? userId = null,
        int? authenticatedUserIdFromScheme = null,
        RecommendationAffinityRefreshSignal? refreshSignal = null)
    {
        var httpContext = new DefaultHttpContext();
        if (authenticatedUserIdFromScheme.HasValue)
        {
            var services = new ServiceCollection();
            services.AddSingleton<IAuthenticationService>(new FakeAuthenticationService(authenticatedUserIdFromScheme.Value));
            httpContext.RequestServices = services.BuildServiceProvider();
        }

        if (userId.HasValue)
        {
            httpContext.User = new ClaimsPrincipal(
                new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, userId.Value.ToString())],
                    authenticationType: "Test"));
        }

        var controller = refreshSignal is null
            ? new ProductInsightsController(db)
            : new ProductInsightsController(db, refreshSignal);

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContext
        };
        return controller;
    }

    private sealed class FakeAuthenticationService(int userId) : IAuthenticationService
    {
        public Task<AuthenticateResult> AuthenticateAsync(HttpContext context, string? scheme)
        {
            if (!string.Equals(scheme, JwtBearerDefaults.AuthenticationScheme, StringComparison.Ordinal))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var principal = new ClaimsPrincipal(
                new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, userId.ToString())],
                    authenticationType: JwtBearerDefaults.AuthenticationScheme));

            var ticket = new AuthenticationTicket(principal, JwtBearerDefaults.AuthenticationScheme);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }

        public Task ChallengeAsync(HttpContext context, string? scheme, AuthenticationProperties? properties)
            => Task.CompletedTask;

        public Task ForbidAsync(HttpContext context, string? scheme, AuthenticationProperties? properties)
            => Task.CompletedTask;

        public Task SignInAsync(HttpContext context, string? scheme, ClaimsPrincipal principal, AuthenticationProperties? properties)
            => Task.CompletedTask;

        public Task SignOutAsync(HttpContext context, string? scheme, AuthenticationProperties? properties)
            => Task.CompletedTask;
    }
}
