using System.Linq;
using System.Text.Json;
using FreshFarm.Web.Bff.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace FreshFarm.Web.Bff.Tests;

public sealed class BffCatalogControllerTests
{
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);
    private static string RecentInteractionUtc => DateTime.UtcNow.AddDays(-3).ToString("yyyy-MM-ddTHH:mm:ssZ");
    private static string RecentPurchaseUtc => DateTime.UtcNow.AddDays(-7).ToString("yyyy-MM-ddTHH:mm:ssZ");

    [Fact]
    public async Task GetProducts_ReturnsServiceUnavailablePayload_WhenCatalogThrowsHttpRequestException()
    {
        var catalogHandler = new RecordingHttpMessageHandler(_ => throw new HttpRequestException("Connection refused."));
        var identityHandler = new RecordingHttpMessageHandler(_ =>
            new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("[]")
            });
        var controller = CreateController(catalogHandler, identityHandler);

        var result = await controller.GetProducts(
            name: null,
            sellerId: 4,
            categoryIds: null,
            origins: null,
            standards: null,
            units: null);

        var content = Assert.IsType<ContentResult>(result);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, content.StatusCode);
        using var json = JsonDocument.Parse(content.Content!);
        Assert.Equal("upstream_unavailable", json.RootElement.GetProperty("error").GetString());
        Assert.Equal("Catalog", json.RootElement.GetProperty("upstreamService").GetString());
        Assert.Equal("Chua ket noi duoc dich vu san pham. Vui long thu lai sau.", json.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task GetProductOffers_DedupesDuplicateMerchantPayload_FromIdentity()
    {
        var catalogHandler = new RecordingHttpMessageHandler(_ =>
            new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("""
                [
                  {
                    "sellerId": 15,
                    "productId": 901,
                    "activeSinceUtc": "2026-03-24T01:02:03Z"
                  }
                ]
                """)
            });
        var identityHandler = new RecordingHttpMessageHandler(_ =>
            new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("""
                [
                  {
                    "sellerId": 15,
                    "shopName": "",
                    "userName": "seller15",
                    "addressSummary": "",
                    "joinedAt": "2025-01-01T00:00:00Z"
                  },
                  {
                    "sellerId": 15,
                    "shopName": "Fresh Farm Dup Safe",
                    "userName": "seller15",
                    "avatar": "/images/seller15.png",
                    "addressSummary": "12 Nguyen Trai, Quan 1",
                    "joinedAt": "2026-02-01T00:00:00Z"
                  }
                ]
                """)
            });
        var controller = CreateController(catalogHandler, identityHandler);

        var result = await controller.GetProductOffers(901);

        var ok = Assert.IsType<OkObjectResult>(result);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value, WebJson));
        var items = json.RootElement;
        Assert.Equal(JsonValueKind.Array, items.ValueKind);
        var offer = items.EnumerateArray().Single();
        Assert.Equal(15, offer.GetProperty("sellerId").GetInt32());
        Assert.Equal("Fresh Farm Dup Safe", offer.GetProperty("shopName").GetString());
        Assert.Equal("12 Nguyen Trai, Quan 1", offer.GetProperty("addressSummary").GetString());
    }

    [Fact]
    public async Task SearchProducts_BuildsAvailableShops_WhenIdentityReturnsDuplicateMerchants()
    {
        var catalogHandler = new RecordingHttpMessageHandler(_ =>
            new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("""
                [
                  {
                    "productId": 501,
                    "productName": "Rau Muong",
                    "categoryName": "Rau Cu",
                    "categoryId": 3,
                    "unitName": "kg",
                    "price": 25000,
                    "averageRating": 4.6,
                    "soldCount": 20,
                    "stockQuantity": 50,
                    "availableStock": 50,
                    "onHandStock": 50,
                    "reservedStock": 0,
                    "origin": "Da Lat",
                    "standard": "VietGAP",
                    "primarySellerId": 22
                  }
                ]
                """)
            });
        var identityHandler = new RecordingHttpMessageHandler(_ =>
            new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("""
                {
                  "merchants": [
                    {
                      "sellerId": 22,
                      "shopName": "",
                      "userName": "seller22",
                      "addressSummary": "",
                      "joinedAt": "2025-01-01T00:00:00Z"
                    },
                    {
                      "sellerId": 22,
                      "shopName": "Vuon Rau Quan 1",
                      "userName": "seller22",
                      "avatar": "/images/seller22.png",
                      "addressSummary": "45 Le Loi, Phuong Ben Nghe, Quan 1",
                      "joinedAt": "2026-03-01T00:00:00Z"
                    }
                  ]
                }
                """)
            });
        var controller = CreateController(catalogHandler, identityHandler);

        var result = await controller.SearchProducts(
            name: null,
            categoryIds: null,
            sellerIds: null,
            availability: null,
            deliveryScopes: null,
            origins: null,
            standards: null,
            units: null,
            minPrice: null,
            maxPrice: null,
            minRating: null,
            preset: null,
            sort: null,
            page: 1,
            pageSize: 12);

        var ok = Assert.IsType<OkObjectResult>(result);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value, WebJson));
        var availableShops = json.RootElement.GetProperty("availableShops");
        var shop = availableShops.EnumerateArray().Single();
        Assert.Equal(22, shop.GetProperty("sellerId").GetInt32());
        Assert.Equal("Vuon Rau Quan 1", shop.GetProperty("shopName").GetString());
        Assert.Equal("45 Le Loi, Phuong Ben Nghe, Quan 1", shop.GetProperty("addressSummary").GetString());
    }

    [Fact]
    public async Task GetProducts_EnrichesAverageRatingAndSoldCount_FromOrderingProductInsights()
    {
        var catalogHandler = new RecordingHttpMessageHandler(_ =>
            new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("""
                [
                  {
                    "productId": 104,
                    "productName": "Ca chua huu co",
                    "categoryName": "Rau Cu",
                    "categoryId": 3,
                    "unitId": 1,
                    "unitName": "kg",
                    "unitSymbol": "kg",
                    "price": 32000,
                    "stockQuantity": 20,
                    "availableStock": 20,
                    "onHandStock": 20,
                    "reservedStock": 0,
                    "primarySellerId": 4
                  }
                ]
                """)
            });
        var identityHandler = new RecordingHttpMessageHandler(_ =>
            new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("[]")
            });
        var orderingHandler = new RecordingHttpMessageHandler(_ =>
            new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("""
                [
                  {
                    "productId": 104,
                    "averageRating": 4.7,
                    "reviewCount": 9,
                    "soldCount": 31
                  }
                ]
                """)
            });
        var controller = CreateController(catalogHandler, identityHandler, orderingHandler);

        var result = await controller.GetProducts(
            name: null,
            sellerId: 4,
            categoryIds: null,
            origins: null,
            standards: null,
            units: null);

        var content = Assert.IsType<ContentResult>(result);
        using var json = JsonDocument.Parse(content.Content!);
        var item = json.RootElement.EnumerateArray().Single();
        Assert.Equal(4.7m, item.GetProperty("averageRating").GetDecimal());
        Assert.Equal(9, item.GetProperty("reviewCount").GetInt32());
        Assert.Equal(31, item.GetProperty("soldCount").GetInt32());
    }

    [Fact]
    public async Task SearchProducts_AppliesMinRating_AfterEnrichingStatsFromOrdering()
    {
        var catalogHandler = new RecordingHttpMessageHandler(_ =>
            new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("""
                [
                  {
                    "productId": 501,
                    "productName": "Rau Muong",
                    "categoryName": "Rau Cu",
                    "categoryId": 3,
                    "unitName": "kg",
                    "price": 25000,
                    "averageRating": 0,
                    "soldCount": 0,
                    "stockQuantity": 50,
                    "availableStock": 50,
                    "onHandStock": 50,
                    "reservedStock": 0,
                    "origin": "Da Lat",
                    "standard": "VietGAP",
                    "primarySellerId": 22
                  }
                ]
                """)
            });
        var identityHandler = new RecordingHttpMessageHandler(_ =>
            new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("""
                {
                  "merchants": [
                    {
                      "sellerId": 22,
                      "shopName": "Vuon Rau Quan 1",
                      "userName": "seller22",
                      "addressSummary": "45 Le Loi, Phuong Ben Nghe, Quan 1",
                      "joinedAt": "2026-03-01T00:00:00Z"
                    }
                  ]
                }
                """)
            });
        var orderingHandler = new RecordingHttpMessageHandler(_ =>
            new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("""
                [
                  {
                    "productId": 501,
                    "averageRating": 4.8,
                    "reviewCount": 12,
                    "soldCount": 40
                  }
                ]
                """)
            });
        var controller = CreateController(catalogHandler, identityHandler, orderingHandler);

        var result = await controller.SearchProducts(
            name: null,
            categoryIds: null,
            sellerIds: null,
            availability: null,
            deliveryScopes: null,
            origins: null,
            standards: null,
            units: null,
            minPrice: null,
            maxPrice: null,
            minRating: 4.5m,
            preset: null,
            sort: null,
            page: 1,
            pageSize: 12);

        var ok = Assert.IsType<OkObjectResult>(result);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value, WebJson));
        var item = json.RootElement.GetProperty("items").EnumerateArray().Single();
        Assert.Equal(4.8m, item.GetProperty("averageRating").GetDecimal());
        Assert.Equal(40, item.GetProperty("soldCount").GetInt32());
    }

    [Fact]
    public async Task SearchProducts_PrefersHybridKeywordSignal_WhenOrderingProvidesSearchRanking()
    {
        var catalogHandler = new RecordingHttpMessageHandler(_ =>
            new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("""
                [
                  {
                    "productId": 501,
                    "productName": "Rau muong huu co",
                    "categoryName": "Rau Cu",
                    "categoryId": 3,
                    "unitName": "kg",
                    "price": 25000,
                    "averageRating": 0,
                    "soldCount": 0,
                    "stockQuantity": 50,
                    "availableStock": 50,
                    "onHandStock": 50,
                    "reservedStock": 0,
                    "origin": "Da Lat",
                    "standard": "VietGAP",
                    "primarySellerId": 22
                  },
                  {
                    "productId": 502,
                    "productName": "Bong cai xanh",
                    "categoryName": "Rau Cu",
                    "categoryId": 3,
                    "unitName": "kg",
                    "price": 32000,
                    "averageRating": 0,
                    "soldCount": 0,
                    "stockQuantity": 45,
                    "availableStock": 45,
                    "onHandStock": 45,
                    "reservedStock": 0,
                    "origin": "Da Lat",
                    "standard": "VietGAP",
                    "primarySellerId": 22
                  }
                ]
                """)
            });
        var identityHandler = new RecordingHttpMessageHandler(_ =>
            new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("""
                {
                  "merchants": [
                    {
                      "sellerId": 22,
                      "shopName": "Vuon Rau Quan 1",
                      "userName": "seller22",
                      "addressSummary": "45 Le Loi, Phuong Ben Nghe, Quan 1",
                      "joinedAt": "2026-03-01T00:00:00Z"
                    }
                  ]
                }
                """)
            });
        var orderingHandler = new RecordingHttpMessageHandler(request =>
        {
            var path = request.RequestUri?.AbsolutePath;
            if (string.Equals(path, "/api/orders/product-insights/stats", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent($$"""
                    [
                      { "productId": 501, "averageRating": 4.4, "reviewCount": 12, "soldCount": 31 },
                      { "productId": 502, "averageRating": 4.0, "reviewCount": 6, "soldCount": 18 }
                    ]
                    """)
                };
            }

            Assert.Equal("/api/orders/product-insights/search-ranking", path);
            var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("""
                [
                  {
                    "productId": 502,
                    "searchClickCount": 3,
                    "searchClickSessionCount": 2,
                    "searchViewSessionCount": 2,
                    "searchRecommendationClickCount": 1,
                    "hybridSearchScore": 240,
                    "seasonalityScore": 72,
                    "seasonalityLabel": "Vụ chính",
                    "seasonalityBadgeLabel": "Mùa ngon nhất"
                  }
                ]
                """)
            };
            response.Headers.Add("X-Recommendation-Signal-Source", "materialized_cf_v1");
            return response;
        });
        var controller = CreateController(catalogHandler, identityHandler, orderingHandler);

        var result = await controller.SearchProducts(
            name: "rau",
            categoryIds: null,
            sellerIds: null,
            availability: null,
            deliveryScopes: null,
            origins: null,
            standards: null,
            units: null,
            minPrice: null,
            maxPrice: null,
            minRating: null,
            preset: null,
            sort: null,
            page: 1,
            pageSize: 12);

        var ok = Assert.IsType<OkObjectResult>(result);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value, WebJson));
        Assert.Equal("hybrid_search_v1", json.RootElement.GetProperty("rankingAlgorithm").GetString());
        Assert.Equal("materialized_cf_v1", json.RootElement.GetProperty("rankingSignalSource").GetString());
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("rankingFallbackReason").ValueKind);
        Assert.Equal("catalog_keyword_v1", json.RootElement.GetProperty("contentSignalSource").GetString());
        var searchSignalBreakdown = json.RootElement.GetProperty("signalBreakdown");
        Assert.Equal("catalog_keyword_v1", searchSignalBreakdown.GetProperty("content").GetString());
        Assert.Equal("materialized_cf_v1", searchSignalBreakdown.GetProperty("overall").GetString());
        Assert.Equal(JsonValueKind.Null, searchSignalBreakdown.GetProperty("overallReason").ValueKind);
        var items = json.RootElement.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(502, items[0].GetProperty("productId").GetInt32());
        Assert.Equal(72d, items[0].GetProperty("seasonalityScore").GetDouble());
        Assert.Equal("Vụ chính", items[0].GetProperty("seasonalityLabel").GetString());
        Assert.Equal("Mùa ngon nhất", items[0].GetProperty("seasonalityBadgeLabel").GetString());
    }

    [Fact]
    public async Task SearchProducts_UsesMaterializedUserCategoryScore_WhenBehaviorSignalsAreMissing()
    {
        var catalogHandler = new RecordingHttpMessageHandler(_ =>
            new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("""
                [
                  {
                    "productId": 701,
                    "productName": "Rau lang huu co",
                    "categoryName": "Rau la",
                    "categoryId": 1,
                    "unitName": "kg",
                    "price": 22000,
                    "averageRating": 0,
                    "soldCount": 0,
                    "stockQuantity": 20,
                    "availableStock": 20,
                    "onHandStock": 20,
                    "reservedStock": 0,
                    "origin": "Tien Giang",
                    "standard": "VietGAP",
                    "primarySellerId": 21
                  },
                  {
                    "productId": 702,
                    "productName": "Nam huong huu co",
                    "categoryName": "Nam",
                    "categoryId": 5,
                    "unitName": "kg",
                    "price": 86000,
                    "averageRating": 0,
                    "soldCount": 0,
                    "stockQuantity": 18,
                    "availableStock": 18,
                    "onHandStock": 18,
                    "reservedStock": 0,
                    "origin": "Da Lat",
                    "standard": "VietGAP",
                    "primarySellerId": 33
                  }
                ]
                """)
            });
        var identityHandler = new RecordingHttpMessageHandler(_ =>
            new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("""
                {
                  "merchants": [
                    {
                      "sellerId": 21,
                      "shopName": "Vuon rau Tien Giang",
                      "userName": "seller21",
                      "addressSummary": "My Tho, Tien Giang",
                      "joinedAt": "2026-03-01T00:00:00Z"
                    },
                    {
                      "sellerId": 33,
                      "shopName": "Nong trai Da Lat",
                      "userName": "seller33",
                      "addressSummary": "Da Lat, Lam Dong",
                      "joinedAt": "2026-03-01T00:00:00Z"
                    }
                  ]
                }
                """)
            });
        var orderingHandler = new RecordingHttpMessageHandler(request =>
        {
            var path = request.RequestUri?.AbsolutePath;
            if (string.Equals(path, "/api/orders/product-insights/stats", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent($$"""
                    [
                      { "productId": 701, "averageRating": 4.7, "reviewCount": 11, "soldCount": 18 },
                      { "productId": 702, "averageRating": 4.8, "reviewCount": 22, "soldCount": 45 }
                    ]
                    """)
                };
            }

            if (string.Equals(path, "/api/orders/product-insights/search-ranking", StringComparison.Ordinal))
            {
                var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("[]")
                };
                response.Headers.Add("X-Recommendation-Fallback-Reason", "no_materialized_keyword_affinity");
                return response;
            }

            if (string.Equals(path, "/api/orders/product-insights/user-category", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent($$"""
                    [
                      {
                        "userId": 8,
                        "categoryId": 1,
                        "categoryName": "Rau la",
                        "viewCount": 7,
                        "searchClickCount": 4,
                        "recommendationClickCount": 2,
                        "purchaseCount": 3,
                        "userCategoryScore": 900,
                        "lastInteractedAtUtc": "2026-04-01T10:00:00Z"
                      }
                    ]
                    """)
                };
            }

            throw new Xunit.Sdk.XunitException($"Unexpected Ordering path: {path}");
        });
        var controller = CreateController(catalogHandler, identityHandler, orderingHandler);
        controller.Request.Headers.Authorization = "Bearer test-token";

        var result = await controller.SearchProducts(
            name: "rau",
            categoryIds: null,
            sellerIds: null,
            availability: null,
            deliveryScopes: null,
            origins: null,
            standards: null,
            units: null,
            minPrice: null,
            maxPrice: null,
            minRating: null,
            preset: null,
            sort: null,
            page: 1,
            pageSize: 12);

        var ok = Assert.IsType<OkObjectResult>(result);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value, WebJson));
        Assert.Equal("hybrid_search_v1", json.RootElement.GetProperty("rankingAlgorithm").GetString());
        Assert.Equal("materialized_category_pref_v1", json.RootElement.GetProperty("rankingSignalSource").GetString());
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("rankingFallbackReason").ValueKind);

        var searchSignalBreakdown = json.RootElement.GetProperty("signalBreakdown");
        Assert.Equal("materialized_category_pref_v1", searchSignalBreakdown.GetProperty("preference").GetString());
        Assert.Equal(JsonValueKind.Null, searchSignalBreakdown.GetProperty("preferenceReason").ValueKind);

        var items = json.RootElement.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(701, items[0].GetProperty("productId").GetInt32());
        Assert.Contains("Rau la", items[0].GetProperty("recommendationReason").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SearchProducts_UsesMaterializedUserSellerScore_WhenBehaviorSignalsAreMissing()
    {
        var catalogHandler = new RecordingHttpMessageHandler(_ =>
            new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("""
                [
                  {
                    "productId": 711,
                    "productName": "Rau cai cua shop quen",
                    "categoryName": "Rau cu",
                    "categoryId": 3,
                    "unitName": "kg",
                    "price": 28000,
                    "averageRating": 0,
                    "soldCount": 0,
                    "stockQuantity": 20,
                    "availableStock": 20,
                    "onHandStock": 20,
                    "reservedStock": 0,
                    "origin": "Da Lat",
                    "standard": "VietGAP",
                    "primarySellerId": 77
                  },
                  {
                    "productId": 712,
                    "productName": "Rau cai cua shop la",
                    "categoryName": "Rau cu",
                    "categoryId": 3,
                    "unitName": "kg",
                    "price": 28000,
                    "averageRating": 0,
                    "soldCount": 0,
                    "stockQuantity": 20,
                    "availableStock": 20,
                    "onHandStock": 20,
                    "reservedStock": 0,
                    "origin": "Da Lat",
                    "standard": "VietGAP",
                    "primarySellerId": 88
                  }
                ]
                """)
            });
        var identityHandler = new RecordingHttpMessageHandler(_ =>
            new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("""
                {
                  "merchants": [
                    {
                      "sellerId": 77,
                      "shopName": "Shop quen Da Lat",
                      "userName": "seller77",
                      "addressSummary": "Da Lat, Lam Dong",
                      "joinedAt": "2026-03-01T00:00:00Z"
                    },
                    {
                      "sellerId": 88,
                      "shopName": "Shop la Da Lat",
                      "userName": "seller88",
                      "addressSummary": "Da Lat, Lam Dong",
                      "joinedAt": "2026-03-01T00:00:00Z"
                    }
                  ]
                }
                """)
            });
        var orderingHandler = new RecordingHttpMessageHandler(request =>
        {
            var path = request.RequestUri?.AbsolutePath;
            if (string.Equals(path, "/api/orders/product-insights/stats", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent($$"""
                    [
                      { "productId": 711, "averageRating": 4.6, "reviewCount": 12, "soldCount": 18 },
                      { "productId": 712, "averageRating": 4.6, "reviewCount": 12, "soldCount": 18 }
                    ]
                    """)
                };
            }

            if (string.Equals(path, "/api/orders/product-insights/search-ranking", StringComparison.Ordinal))
            {
                var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("[]")
                };
                response.Headers.Add("X-Recommendation-Fallback-Reason", "no_materialized_keyword_affinity");
                return response;
            }

            if (string.Equals(path, "/api/orders/product-insights/user-category", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("[]")
                };
            }

            if (string.Equals(path, "/api/orders/product-insights/user-seller", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent($$"""
                    [
                      {
                        "userId": 8,
                        "sellerId": 77,
                        "viewCount": 6,
                        "searchClickCount": 2,
                        "purchaseCount": 4,
                        "userSellerScore": 880,
                        "lastInteractedAtUtc": "2026-04-01T08:00:00Z"
                      }
                    ]
                    """)
                };
            }

            throw new Xunit.Sdk.XunitException($"Unexpected Ordering path: {path}");
        });
        var controller = CreateController(catalogHandler, identityHandler, orderingHandler);
        controller.Request.Headers.Authorization = "Bearer test-token";

        var result = await controller.SearchProducts(
            name: "rau",
            categoryIds: null,
            sellerIds: null,
            availability: null,
            deliveryScopes: null,
            origins: null,
            standards: null,
            units: null,
            minPrice: null,
            maxPrice: null,
            minRating: null,
            preset: null,
            sort: null,
            page: 1,
            pageSize: 12);

        var ok = Assert.IsType<OkObjectResult>(result);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value, WebJson));
        Assert.Equal("hybrid_search_v1", json.RootElement.GetProperty("rankingAlgorithm").GetString());
        Assert.Equal("materialized_seller_pref_v1", json.RootElement.GetProperty("rankingSignalSource").GetString());
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("rankingFallbackReason").ValueKind);

        var searchSignalBreakdown = json.RootElement.GetProperty("signalBreakdown");
        Assert.Equal("materialized_seller_pref_v1", searchSignalBreakdown.GetProperty("preference").GetString());
        Assert.Equal(JsonValueKind.Null, searchSignalBreakdown.GetProperty("preferenceReason").ValueKind);

        var items = json.RootElement.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(711, items[0].GetProperty("productId").GetInt32());
        var reason = items[0].GetProperty("recommendationReason").GetString();
        Assert.True(
            reason?.Contains("shop bạn hay quay lại", StringComparison.OrdinalIgnoreCase) == true
            || reason?.Contains("shop bạn vừa quay lại", StringComparison.OrdinalIgnoreCase) == true);
    }

    [Fact]
    public async Task SearchProducts_PrefersRecentlyReturnedSeller_WhenSellerScoresAreClose()
    {
        var catalogHandler = new RecordingHttpMessageHandler(_ =>
            new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("""
                [
                  {
                    "productId": 713,
                    "productName": "Rau cai shop moi quay lai",
                    "categoryName": "Rau cu",
                    "categoryId": 3,
                    "unitName": "kg",
                    "price": 28000,
                    "averageRating": 0,
                    "soldCount": 0,
                    "stockQuantity": 20,
                    "availableStock": 20,
                    "onHandStock": 20,
                    "reservedStock": 0,
                    "origin": "Da Lat",
                    "standard": "VietGAP",
                    "primarySellerId": 77
                  },
                  {
                    "productId": 714,
                    "productName": "Rau cai shop cu",
                    "categoryName": "Rau cu",
                    "categoryId": 3,
                    "unitName": "kg",
                    "price": 28000,
                    "averageRating": 0,
                    "soldCount": 0,
                    "stockQuantity": 20,
                    "availableStock": 20,
                    "onHandStock": 20,
                    "reservedStock": 0,
                    "origin": "Da Lat",
                    "standard": "VietGAP",
                    "primarySellerId": 88
                  }
                ]
                """)
            });
        var identityHandler = new RecordingHttpMessageHandler(_ =>
            new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("""
                {
                  "merchants": [
                    {
                      "sellerId": 77,
                      "shopName": "Shop quen Da Lat",
                      "userName": "seller77",
                      "addressSummary": "Da Lat, Lam Dong",
                      "joinedAt": "2026-03-01T00:00:00Z"
                    },
                    {
                      "sellerId": 88,
                      "shopName": "Shop cu Da Lat",
                      "userName": "seller88",
                      "addressSummary": "Da Lat, Lam Dong",
                      "joinedAt": "2026-03-01T00:00:00Z"
                    }
                  ]
                }
                """)
            });
        var orderingHandler = new RecordingHttpMessageHandler(request =>
        {
            var path = request.RequestUri?.AbsolutePath;
            if (string.Equals(path, "/api/orders/product-insights/stats", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("[]")
                };
            }

            if (string.Equals(path, "/api/orders/product-insights/search-ranking", StringComparison.Ordinal))
            {
                var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("[]")
                };
                response.Headers.Add("X-Recommendation-Fallback-Reason", "no_materialized_keyword_affinity");
                return response;
            }

            if (string.Equals(path, "/api/orders/product-insights/user-category", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("[]")
                };
            }

            if (string.Equals(path, "/api/orders/product-insights/user-seller", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent($$"""
                    [
                      {
                        "userId": 8,
                        "sellerId": 77,
                        "viewCount": 3,
                        "searchClickCount": 1,
                        "purchaseCount": 1,
                        "userSellerScore": 180,
                        "lastInteractedAtUtc": "{{RecentInteractionUtc}}"
                      },
                      {
                        "userId": 8,
                        "sellerId": 88,
                        "viewCount": 5,
                        "searchClickCount": 2,
                        "purchaseCount": 2,
                        "userSellerScore": 188,
                        "lastInteractedAtUtc": "2025-12-01T09:00:00Z"
                      }
                    ]
                    """)
                };
            }

            throw new Xunit.Sdk.XunitException($"Unexpected Ordering path: {path}");
        });
        var controller = CreateController(catalogHandler, identityHandler, orderingHandler);
        controller.Request.Headers.Authorization = "Bearer test-token";

        var result = await controller.SearchProducts(
            name: "rau",
            categoryIds: null,
            sellerIds: null,
            availability: null,
            deliveryScopes: null,
            origins: null,
            standards: null,
            units: null,
            minPrice: null,
            maxPrice: null,
            minRating: null,
            preset: null,
            sort: null,
            page: 1,
            pageSize: 12);

        var ok = Assert.IsType<OkObjectResult>(result);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value, WebJson));
        var items = json.RootElement.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(713, items[0].GetProperty("productId").GetInt32());
        Assert.Contains("Từ shop bạn vừa quay lại", items[0].GetProperty("recommendationReason").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SearchProducts_KeepsRecentSellerDiscoveryItem_OnFirstPageWhenCategoryScoresWouldOtherwiseFillIt()
    {
        var catalogHandler = new RecordingHttpMessageHandler(_ =>
            new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("""
                [
                  {
                    "productId": 801,
                    "productName": "Rau muong uu tien",
                    "categoryName": "Rau La",
                    "categoryId": 5,
                    "unitName": "kg",
                    "price": 26000,
                    "averageRating": 4.8,
                    "soldCount": 12,
                    "stockQuantity": 25,
                    "availableStock": 25,
                    "onHandStock": 25,
                    "reservedStock": 0,
                    "origin": "Long An",
                    "standard": "VietGAP",
                    "primarySellerId": 31
                  },
                  {
                    "productId": 802,
                    "productName": "Rau cai uu tien",
                    "categoryName": "Rau La",
                    "categoryId": 5,
                    "unitName": "kg",
                    "price": 26500,
                    "averageRating": 4.7,
                    "soldCount": 11,
                    "stockQuantity": 25,
                    "availableStock": 25,
                    "onHandStock": 25,
                    "reservedStock": 0,
                    "origin": "Long An",
                    "standard": "VietGAP",
                    "primarySellerId": 32
                  },
                  {
                    "productId": 803,
                    "productName": "Rau lang uu tien",
                    "categoryName": "Rau La",
                    "categoryId": 5,
                    "unitName": "kg",
                    "price": 27000,
                    "averageRating": 4.6,
                    "soldCount": 10,
                    "stockQuantity": 25,
                    "availableStock": 25,
                    "onHandStock": 25,
                    "reservedStock": 0,
                    "origin": "Tien Giang",
                    "standard": "VietGAP",
                    "primarySellerId": 33
                  },
                  {
                    "productId": 804,
                    "productName": "Rau den uu tien",
                    "categoryName": "Rau La",
                    "categoryId": 5,
                    "unitName": "kg",
                    "price": 27500,
                    "averageRating": 4.5,
                    "soldCount": 9,
                    "stockQuantity": 25,
                    "availableStock": 25,
                    "onHandStock": 25,
                    "reservedStock": 0,
                    "origin": "Ben Tre",
                    "standard": "VietGAP",
                    "primarySellerId": 34
                  },
                  {
                    "productId": 805,
                    "productName": "Rau tu shop vua quay lai",
                    "categoryName": "Rau Cu",
                    "categoryId": 3,
                    "unitName": "kg",
                    "price": 28000,
                    "averageRating": 4.2,
                    "soldCount": 4,
                    "stockQuantity": 25,
                    "availableStock": 25,
                    "onHandStock": 25,
                    "reservedStock": 0,
                    "origin": "Da Lat",
                    "standard": "VietGAP",
                    "primarySellerId": 77
                  }
                ]
                """)
            });

        var identityHandler = new RecordingHttpMessageHandler(_ =>
            new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("""
                {
                  "merchants": [
                    {
                      "sellerId": 77,
                      "shopName": "Shop moi ghe",
                      "userName": "seller77",
                      "addressSummary": "Da Lat, Lam Dong",
                      "joinedAt": "2026-03-01T00:00:00Z"
                    }
                  ]
                }
                """)
            });

        var orderingHandler = new RecordingHttpMessageHandler(request =>
        {
            var path = request.RequestUri?.AbsolutePath;
            if (string.Equals(path, "/api/orders/product-insights/stats", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("[]")
                };
            }

            if (string.Equals(path, "/api/orders/product-insights/search-ranking", StringComparison.Ordinal))
            {
                var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("[]")
                };
                response.Headers.Add("X-Recommendation-Fallback-Reason", "no_materialized_keyword_affinity");
                return response;
            }

            if (string.Equals(path, "/api/orders/product-insights/user-category", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent($$"""
                    [
                      {
                        "userId": 8,
                        "categoryId": 5,
                        "categoryName": "Rau La",
                        "viewCount": 6,
                        "searchClickCount": 2,
                        "recommendationClickCount": 0,
                        "purchaseCount": 2,
                        "userCategoryScore": 240,
                        "lastInteractedAtUtc": "2026-04-03T08:00:00Z"
                      }
                    ]
                    """)
                };
            }

            if (string.Equals(path, "/api/orders/product-insights/user-seller", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent($$"""
                    [
                      {
                        "userId": 8,
                        "sellerId": 77,
                        "viewCount": 2,
                        "searchClickCount": 1,
                        "purchaseCount": 1,
                        "userSellerScore": 210,
                        "lastInteractedAtUtc": "{{RecentInteractionUtc}}"
                      }
                    ]
                    """)
                };
            }

            throw new Xunit.Sdk.XunitException($"Unexpected Ordering path: {path}");
        });

        var controller = CreateController(catalogHandler, identityHandler, orderingHandler);
        controller.Request.Headers.Authorization = "Bearer test-token";

        var result = await controller.SearchProducts(
            name: "rau",
            categoryIds: null,
            sellerIds: null,
            availability: null,
            deliveryScopes: null,
            origins: null,
            standards: null,
            units: null,
            minPrice: null,
            maxPrice: null,
            minRating: null,
            preset: null,
            sort: null,
            page: 1,
            pageSize: 4);

        var ok = Assert.IsType<OkObjectResult>(result);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value, WebJson));
        var items = json.RootElement.GetProperty("items").EnumerateArray().ToList();

        Assert.Contains(items, item => item.GetProperty("productId").GetInt32() == 805);
        var recentSellerItem = items.Single(item => item.GetProperty("productId").GetInt32() == 805);
        Assert.Contains("shop bạn vừa quay lại", recentSellerItem.GetProperty("recommendationReason").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SearchProducts_KeepsFavoriteSellerDiscoveryItem_OnFirstPageWhenCategoryScoresWouldOtherwiseFillIt()
    {
        var catalogHandler = new RecordingHttpMessageHandler(_ =>
            new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("""
                [
                  {
                    "productId": 811,
                    "productName": "Rau muong uu tien",
                    "categoryName": "Rau La",
                    "categoryId": 5,
                    "unitName": "kg",
                    "price": 26000,
                    "averageRating": 4.8,
                    "soldCount": 12,
                    "stockQuantity": 25,
                    "availableStock": 25,
                    "onHandStock": 25,
                    "reservedStock": 0,
                    "origin": "Long An",
                    "standard": "VietGAP",
                    "primarySellerId": 31
                  },
                  {
                    "productId": 812,
                    "productName": "Rau cai uu tien",
                    "categoryName": "Rau La",
                    "categoryId": 5,
                    "unitName": "kg",
                    "price": 26500,
                    "averageRating": 4.7,
                    "soldCount": 11,
                    "stockQuantity": 25,
                    "availableStock": 25,
                    "onHandStock": 25,
                    "reservedStock": 0,
                    "origin": "Long An",
                    "standard": "VietGAP",
                    "primarySellerId": 32
                  },
                  {
                    "productId": 813,
                    "productName": "Rau lang uu tien",
                    "categoryName": "Rau La",
                    "categoryId": 5,
                    "unitName": "kg",
                    "price": 27000,
                    "averageRating": 4.6,
                    "soldCount": 10,
                    "stockQuantity": 25,
                    "availableStock": 25,
                    "onHandStock": 25,
                    "reservedStock": 0,
                    "origin": "Tien Giang",
                    "standard": "VietGAP",
                    "primarySellerId": 33
                  },
                  {
                    "productId": 814,
                    "productName": "Rau den uu tien",
                    "categoryName": "Rau La",
                    "categoryId": 5,
                    "unitName": "kg",
                    "price": 27500,
                    "averageRating": 4.5,
                    "soldCount": 9,
                    "stockQuantity": 25,
                    "availableStock": 25,
                    "onHandStock": 25,
                    "reservedStock": 0,
                    "origin": "Ben Tre",
                    "standard": "VietGAP",
                    "primarySellerId": 34
                  },
                  {
                    "productId": 815,
                    "productName": "Nam tu shop hay mua",
                    "categoryName": "Nam",
                    "categoryId": 7,
                    "unitName": "hop",
                    "price": 68000,
                    "averageRating": 4.2,
                    "soldCount": 4,
                    "stockQuantity": 25,
                    "availableStock": 25,
                    "onHandStock": 25,
                    "reservedStock": 0,
                    "origin": "Da Nang",
                    "standard": "GlobalGAP",
                    "primarySellerId": 78
                  }
                ]
                """)
            });

        var identityHandler = new RecordingHttpMessageHandler(_ =>
            new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("""
                {
                  "merchants": [
                    {
                      "sellerId": 78,
                      "shopName": "Shop quen 78",
                      "userName": "seller78",
                      "addressSummary": "Da Nang",
                      "joinedAt": "2025-12-01T00:00:00Z"
                    }
                  ]
                }
                """)
            });

        var orderingHandler = new RecordingHttpMessageHandler(request =>
        {
            var path = request.RequestUri?.AbsolutePath;
            if (string.Equals(path, "/api/orders/product-insights/stats", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("[]")
                };
            }

            if (string.Equals(path, "/api/orders/product-insights/search-ranking", StringComparison.Ordinal))
            {
                var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("[]")
                };
                response.Headers.Add("X-Recommendation-Fallback-Reason", "no_materialized_keyword_affinity");
                return response;
            }

            if (string.Equals(path, "/api/orders/product-insights/user-category", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent($$"""
                    [
                      {
                        "userId": 8,
                        "categoryId": 5,
                        "categoryName": "Rau La",
                        "viewCount": 6,
                        "searchClickCount": 2,
                        "recommendationClickCount": 0,
                        "purchaseCount": 2,
                        "userCategoryScore": 240,
                        "lastInteractedAtUtc": "2026-04-03T08:00:00Z"
                      }
                    ]
                    """)
                };
            }

            if (string.Equals(path, "/api/orders/product-insights/user-seller", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent($$"""
                    [
                      {
                        "userId": 8,
                        "sellerId": 78,
                        "viewCount": 2,
                        "searchClickCount": 1,
                        "purchaseCount": 2,
                        "userSellerScore": 210,
                        "lastInteractedAtUtc": "2026-02-03T09:00:00Z"
                      }
                    ]
                    """)
                };
            }

            throw new Xunit.Sdk.XunitException($"Unexpected Ordering path: {path}");
        });

        var controller = CreateController(catalogHandler, identityHandler, orderingHandler);
        controller.Request.Headers.Authorization = "Bearer test-token";

        var result = await controller.SearchProducts(
            name: "rau",
            categoryIds: null,
            sellerIds: null,
            availability: null,
            deliveryScopes: null,
            origins: null,
            standards: null,
            units: null,
            minPrice: null,
            maxPrice: null,
            minRating: null,
            preset: null,
            sort: null,
            page: 1,
            pageSize: 4);

        var ok = Assert.IsType<OkObjectResult>(result);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value, WebJson));
        var items = json.RootElement.GetProperty("items").EnumerateArray().ToList();

        Assert.Contains(items, item => item.GetProperty("productId").GetInt32() == 815);
        var favoriteSellerItem = items.Single(item => item.GetProperty("productId").GetInt32() == 815);
        var reason = favoriteSellerItem.GetProperty("recommendationReason").GetString();
        Assert.Contains("Từ shop bạn", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SearchProducts_PrioritizesFavoriteSellerSignal_WithinVisibleTopEight()
    {
        var catalogHandler = new RecordingHttpMessageHandler(_ =>
            new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("""
                [
                  { "productId": 901, "productName": "Rau uu tien 1", "categoryName": "Rau La", "categoryId": 5, "unitName": "kg", "price": 25000, "averageRating": 4.8, "soldCount": 22, "stockQuantity": 25, "availableStock": 25, "onHandStock": 25, "reservedStock": 0, "origin": "Long An", "standard": "VietGAP", "primarySellerId": 31 },
                  { "productId": 902, "productName": "Rau uu tien 2", "categoryName": "Rau La", "categoryId": 5, "unitName": "kg", "price": 25200, "averageRating": 4.7, "soldCount": 21, "stockQuantity": 25, "availableStock": 25, "onHandStock": 25, "reservedStock": 0, "origin": "Long An", "standard": "VietGAP", "primarySellerId": 32 },
                  { "productId": 903, "productName": "Rau uu tien 3", "categoryName": "Rau La", "categoryId": 5, "unitName": "kg", "price": 25400, "averageRating": 4.6, "soldCount": 20, "stockQuantity": 25, "availableStock": 25, "onHandStock": 25, "reservedStock": 0, "origin": "Tien Giang", "standard": "VietGAP", "primarySellerId": 33 },
                  { "productId": 904, "productName": "Rau uu tien 4", "categoryName": "Rau La", "categoryId": 5, "unitName": "kg", "price": 25600, "averageRating": 4.5, "soldCount": 19, "stockQuantity": 25, "availableStock": 25, "onHandStock": 25, "reservedStock": 0, "origin": "Ben Tre", "standard": "VietGAP", "primarySellerId": 34 },
                  { "productId": 905, "productName": "Rau uu tien 5", "categoryName": "Rau La", "categoryId": 5, "unitName": "kg", "price": 25800, "averageRating": 4.4, "soldCount": 18, "stockQuantity": 25, "availableStock": 25, "onHandStock": 25, "reservedStock": 0, "origin": "Dong Thap", "standard": "VietGAP", "primarySellerId": 35 },
                  { "productId": 906, "productName": "Rau uu tien 6", "categoryName": "Rau La", "categoryId": 5, "unitName": "kg", "price": 26000, "averageRating": 4.3, "soldCount": 17, "stockQuantity": 25, "availableStock": 25, "onHandStock": 25, "reservedStock": 0, "origin": "Lam Dong", "standard": "VietGAP", "primarySellerId": 36 },
                  { "productId": 907, "productName": "Rau uu tien 7", "categoryName": "Rau La", "categoryId": 5, "unitName": "kg", "price": 26200, "averageRating": 4.2, "soldCount": 16, "stockQuantity": 25, "availableStock": 25, "onHandStock": 25, "reservedStock": 0, "origin": "Can Tho", "standard": "VietGAP", "primarySellerId": 37 },
                  { "productId": 908, "productName": "Rau uu tien 8", "categoryName": "Rau La", "categoryId": 5, "unitName": "kg", "price": 26400, "averageRating": 4.1, "soldCount": 15, "stockQuantity": 25, "availableStock": 25, "onHandStock": 25, "reservedStock": 0, "origin": "Da Lat", "standard": "VietGAP", "primarySellerId": 38 },
                  { "productId": 909, "productName": "Rau huu co tu shop hay mua", "categoryName": "Rau La", "categoryId": 5, "unitName": "hop", "price": 68000, "averageRating": 4.2, "soldCount": 4, "stockQuantity": 25, "availableStock": 25, "onHandStock": 25, "reservedStock": 0, "origin": "Da Nang", "standard": "GlobalGAP", "primarySellerId": 78 }
                ]
                """)
            });

        var identityHandler = new RecordingHttpMessageHandler(_ =>
            new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("""
                {
                  "merchants": [
                    {
                      "sellerId": 78,
                      "shopName": "Shop quen 78",
                      "userName": "seller78",
                      "addressSummary": "Da Nang",
                      "joinedAt": "2025-12-01T00:00:00Z"
                    }
                  ]
                }
                """)
            });

        var orderingHandler = new RecordingHttpMessageHandler(request =>
        {
            var path = request.RequestUri?.AbsolutePath;
            if (string.Equals(path, "/api/orders/product-insights/stats", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("[]")
                };
            }

            if (string.Equals(path, "/api/orders/product-insights/search-ranking", StringComparison.Ordinal))
            {
                var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("[]")
                };
                response.Headers.Add("X-Recommendation-Fallback-Reason", "no_materialized_keyword_affinity");
                return response;
            }

            if (string.Equals(path, "/api/orders/product-insights/user-category", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent($$"""
                    [
                      {
                        "userId": 8,
                        "categoryId": 5,
                        "categoryName": "Rau La",
                        "viewCount": 8,
                        "searchClickCount": 2,
                        "recommendationClickCount": 0,
                        "purchaseCount": 2,
                        "userCategoryScore": 260,
                        "lastInteractedAtUtc": "2026-04-03T08:00:00Z"
                      }
                    ]
                    """)
                };
            }

            if (string.Equals(path, "/api/orders/product-insights/user-seller", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("""
                    [
                      {
                        "userId": 8,
                        "sellerId": 78,
                        "viewCount": 2,
                        "searchClickCount": 1,
                        "purchaseCount": 3,
                        "userSellerScore": 360,
                        "lastInteractedAtUtc": "2026-03-20T09:00:00Z"
                      }
                    ]
                    """)
                };
            }

            throw new Xunit.Sdk.XunitException($"Unexpected Ordering path: {path}");
        });

        var controller = CreateController(catalogHandler, identityHandler, orderingHandler);
        controller.Request.Headers.Authorization = "Bearer test-token";

        var result = await controller.SearchProducts(
            name: "rau",
            categoryIds: null,
            sellerIds: null,
            availability: null,
            deliveryScopes: null,
            origins: null,
            standards: null,
            units: null,
            minPrice: null,
            maxPrice: null,
            minRating: null,
            preset: null,
            sort: null,
            page: 1,
            pageSize: 12);

        var ok = Assert.IsType<OkObjectResult>(result);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value, WebJson));
        var items = json.RootElement.GetProperty("items").EnumerateArray().Take(8).ToList();

        Assert.Contains(items, item => item.GetProperty("productId").GetInt32() == 909);
        var favoriteSellerItem = items.Single(item => item.GetProperty("productId").GetInt32() == 909);
        var reason = favoriteSellerItem.GetProperty("recommendationReason").GetString();
        Assert.Contains("Từ shop bạn", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SearchProducts_IncludesLocationAndSeasonalReason_WhenKeywordMatchesRegion()
    {
        var catalogHandler = new RecordingHttpMessageHandler(request =>
        {
            var query = request.RequestUri?.Query ?? string.Empty;
            var payload = query.Contains("name=mang%20tay", StringComparison.OrdinalIgnoreCase)
                ? """
                  [
                    {
                      "productId": 601,
                      "productName": "Mang tay xanh huu co",
                      "categoryName": "Rau Cu",
                      "categoryId": 3,
                      "unitName": "kg",
                      "price": 65000,
                      "averageRating": 0,
                      "soldCount": 0,
                      "stockQuantity": 40,
                      "availableStock": 40,
                      "onHandStock": 40,
                      "reservedStock": 0,
                      "origin": "Da Lat, Lam Dong",
                      "standard": "VietGAP",
                      "primarySellerId": 22
                    },
                    {
                      "productId": 602,
                      "productName": "Mang tay xanh huu co",
                      "categoryName": "Rau Cu",
                      "categoryId": 3,
                      "unitName": "kg",
                      "price": 64000,
                      "averageRating": 0,
                      "soldCount": 0,
                      "stockQuantity": 40,
                      "availableStock": 40,
                      "onHandStock": 40,
                      "reservedStock": 0,
                      "origin": "Can Tho",
                      "standard": "VietGAP",
                      "primarySellerId": 33
                    }
                  ]
                  """
                : "[]";

            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(payload)
            };
        });
        var identityHandler = new RecordingHttpMessageHandler(_ =>
            new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("""
                {
                  "merchants": [
                    {
                      "sellerId": 22,
                      "shopName": "Nong trai Da Lat",
                      "userName": "seller22",
                      "addressSummary": "12 Tran Hung Dao, Phuong 3, Da Lat, Lam Dong",
                      "joinedAt": "2026-03-01T00:00:00Z"
                    },
                    {
                      "sellerId": 33,
                      "shopName": "Vuon rau Can Tho",
                      "userName": "seller33",
                      "addressSummary": "88 Nguyen Trai, Ninh Kieu, Can Tho",
                      "joinedAt": "2026-03-01T00:00:00Z"
                    }
                  ]
                }
                """)
            });
        var orderingHandler = new RecordingHttpMessageHandler(request =>
        {
            var path = request.RequestUri?.AbsolutePath ?? string.Empty;
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(path.Contains("/api/orders/product-insights/stats", StringComparison.OrdinalIgnoreCase)
                    ? """
                      [
                        { "productId": 601, "averageRating": 4.6, "reviewCount": 9, "soldCount": 20 },
                        { "productId": 602, "averageRating": 4.6, "reviewCount": 9, "soldCount": 20 }
                      ]
                      """
                    : "[]")
            };
        });
        var controller = CreateController(catalogHandler, identityHandler, orderingHandler);

        var result = await controller.SearchProducts(
            name: "mang tay da lat",
            categoryIds: null,
            sellerIds: null,
            availability: null,
            deliveryScopes: null,
            origins: null,
            standards: null,
            units: null,
            minPrice: null,
            maxPrice: null,
            minRating: null,
            preset: null,
            sort: null,
            page: 1,
            pageSize: 12);

        var ok = Assert.IsType<OkObjectResult>(result);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value, WebJson));
        var items = json.RootElement.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(601, items[0].GetProperty("productId").GetInt32());
        Assert.Contains("Đúng nông sản vùng bạn tìm", items[0].GetProperty("recommendationReason").GetString());
        Assert.Contains("Đang đúng mùa", items[0].GetProperty("recommendationReason").GetString());
        Assert.Equal("Nong trai Da Lat", items[0].GetProperty("sellerShopName").GetString());
    }

    [Fact]
    public async Task SearchProducts_DoesNotTreatShopNameAsLocationMatch_WhenKeywordContainsProvinceName()
    {
        var catalogHandler = new RecordingHttpMessageHandler(_ =>
            new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("""
                [
                  {
                    "productId": 701,
                    "productName": "Rau huu co",
                    "categoryName": "Rau Cu",
                    "categoryId": 3,
                    "unitName": "kg",
                    "price": 45000,
                    "averageRating": 0,
                    "soldCount": 0,
                    "stockQuantity": 40,
                    "availableStock": 40,
                    "onHandStock": 40,
                    "reservedStock": 0,
                    "origin": "Da Lat, Lam Dong",
                    "standard": "VietGAP",
                    "primarySellerId": 4
                  }
                ]
                """)
            });
        var identityHandler = new RecordingHttpMessageHandler(_ =>
            new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("""
                {
                  "merchants": [
                    {
                      "sellerId": 4,
                      "shopName": "Cua hang tho",
                      "userName": "tho",
                      "addressSummary": "12 Tran Hung Dao, Da Lat, Lam Dong",
                      "joinedAt": "2026-03-01T00:00:00Z"
                    }
                  ]
                }
                """)
            });
        var orderingHandler = new RecordingHttpMessageHandler(request =>
            new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(request.RequestUri?.AbsolutePath?.Contains("/stats", StringComparison.OrdinalIgnoreCase) == true
                    ? """[{ "productId": 701, "averageRating": 4.4, "reviewCount": 8, "soldCount": 18 }]"""
                    : "[]")
            });
        var controller = CreateController(catalogHandler, identityHandler, orderingHandler);

        var result = await controller.SearchProducts(
            name: "rau can tho",
            categoryIds: null,
            sellerIds: null,
            availability: null,
            deliveryScopes: null,
            origins: null,
            standards: null,
            units: null,
            minPrice: null,
            maxPrice: null,
            minRating: null,
            preset: null,
            sort: null,
            page: 1,
            pageSize: 12);

        var ok = Assert.IsType<OkObjectResult>(result);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value, WebJson));
        var item = json.RootElement.GetProperty("items").EnumerateArray().Single();
        Assert.DoesNotContain("Đúng nông sản vùng bạn tìm", item.GetProperty("recommendationReason").GetString());
    }

    [Fact]
    public async Task SearchProducts_UsesShopLocalityReason_WhenKeywordMatchesSellerAddressOnly()
    {
        var catalogHandler = new RecordingHttpMessageHandler(request =>
            new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(request.RequestUri?.Query?.Contains("name=rau", StringComparison.OrdinalIgnoreCase) == true
                    ? """
                      [
                        {
                          "productId": 702,
                          "productName": "Rau huu co",
                          "categoryName": "Rau Cu",
                          "categoryId": 3,
                          "unitName": "kg",
                          "price": 42000,
                          "averageRating": 0,
                          "soldCount": 0,
                          "stockQuantity": 40,
                          "availableStock": 40,
                          "onHandStock": 40,
                          "reservedStock": 0,
                          "origin": "Da Lat, Lam Dong",
                          "standard": "VietGAP",
                          "primarySellerId": 33
                        }
                      ]
                      """
                    : "[]")
            });
        var identityHandler = new RecordingHttpMessageHandler(_ =>
            new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("""
                {
                  "merchants": [
                    {
                      "sellerId": 33,
                      "shopName": "Vuon rau Can Tho",
                      "userName": "seller33",
                      "addressSummary": "88 Nguyen Trai, Ninh Kieu, Can Tho",
                      "joinedAt": "2026-03-01T00:00:00Z"
                    }
                  ]
                }
                """)
            });
        var orderingHandler = new RecordingHttpMessageHandler(request =>
            new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(request.RequestUri?.AbsolutePath?.Contains("/stats", StringComparison.OrdinalIgnoreCase) == true
                    ? """[{ "productId": 702, "averageRating": 4.2, "reviewCount": 5, "soldCount": 12 }]"""
                    : "[]")
            });
        var controller = CreateController(catalogHandler, identityHandler, orderingHandler);

        var result = await controller.SearchProducts(
            name: "rau can tho",
            categoryIds: null,
            sellerIds: null,
            availability: null,
            deliveryScopes: null,
            origins: null,
            standards: null,
            units: null,
            minPrice: null,
            maxPrice: null,
            minRating: null,
            preset: null,
            sort: null,
            page: 1,
            pageSize: 12);

        var ok = Assert.IsType<OkObjectResult>(result);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value, WebJson));
        var item = json.RootElement.GetProperty("items").EnumerateArray().Single();
        Assert.Contains("Shop ở đúng địa phương bạn tìm", item.GetProperty("recommendationReason").GetString());
    }

    [Fact]
    public async Task SearchProducts_PrefersOriginLocalityOverShopLocality_WhenKeywordHasStrongRegionIntent()
    {
        var catalogHandler = new RecordingHttpMessageHandler(request =>
            new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(request.RequestUri?.Query?.Contains("name=rau", StringComparison.OrdinalIgnoreCase) == true
                    ? """
                      [
                        {
                          "productId": 703,
                          "productName": "Rau huu co",
                          "categoryName": "Rau Cu",
                          "categoryId": 3,
                          "unitName": "kg",
                          "price": 43000,
                          "averageRating": 0,
                          "soldCount": 0,
                          "stockQuantity": 40,
                          "availableStock": 40,
                          "onHandStock": 40,
                          "reservedStock": 0,
                          "origin": "Can Tho",
                          "standard": "VietGAP",
                          "primarySellerId": 34
                        },
                        {
                          "productId": 704,
                          "productName": "Rau huu co",
                          "categoryName": "Rau Cu",
                          "categoryId": 3,
                          "unitName": "kg",
                          "price": 43000,
                          "averageRating": 0,
                          "soldCount": 0,
                          "stockQuantity": 40,
                          "availableStock": 40,
                          "onHandStock": 40,
                          "reservedStock": 0,
                          "origin": "Da Lat, Lam Dong",
                          "standard": "VietGAP",
                          "primarySellerId": 33
                        }
                      ]
                      """
                    : "[]")
            });
        var identityHandler = new RecordingHttpMessageHandler(_ =>
            new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("""
                {
                  "merchants": [
                    {
                      "sellerId": 33,
                      "shopName": "Vuon rau Can Tho",
                      "userName": "seller33",
                      "addressSummary": "88 Nguyen Trai, Ninh Kieu, Can Tho",
                      "joinedAt": "2026-03-01T00:00:00Z"
                    },
                    {
                      "sellerId": 34,
                      "shopName": "Nong trai Mien Tay",
                      "userName": "seller34",
                      "addressSummary": "12 Le Loi, Ninh Kieu, Can Tho",
                      "joinedAt": "2026-03-01T00:00:00Z"
                    }
                  ]
                }
                """)
            });
        var orderingHandler = new RecordingHttpMessageHandler(request =>
            new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(request.RequestUri?.AbsolutePath?.Contains("/stats", StringComparison.OrdinalIgnoreCase) == true
                    ? """
                      [
                        { "productId": 703, "averageRating": 4.1, "reviewCount": 5, "soldCount": 11 },
                        { "productId": 704, "averageRating": 4.9, "reviewCount": 14, "soldCount": 42 }
                      ]
                      """
                    : "[]")
            });
        var controller = CreateController(catalogHandler, identityHandler, orderingHandler);

        var result = await controller.SearchProducts(
            name: "rau can tho",
            categoryIds: null,
            sellerIds: null,
            availability: null,
            deliveryScopes: null,
            origins: null,
            standards: null,
            units: null,
            minPrice: null,
            maxPrice: null,
            minRating: null,
            preset: null,
            sort: null,
            page: 1,
            pageSize: 12);

        var ok = Assert.IsType<OkObjectResult>(result);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value, WebJson));
        var items = json.RootElement.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(703, items[0].GetProperty("productId").GetInt32());
        Assert.Contains("Đúng nông sản vùng bạn tìm", items[0].GetProperty("recommendationReason").GetString());
        Assert.Contains("Shop ở đúng địa phương bạn tìm", items[1].GetProperty("recommendationReason").GetString());
    }

    [Fact]
    public async Task SearchProducts_FirstPassDiversifiesAcrossSellers_WhenHybridSortHasEnoughCandidates()
    {
        var catalogHandler = new RecordingHttpMessageHandler(_ =>
            new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("""
                [
                  { "productId": 501, "productName": "Rau muong huu co", "categoryName": "Rau Cu", "categoryId": 3, "unitName": "kg", "price": 25000, "averageRating": 0, "soldCount": 0, "stockQuantity": 50, "availableStock": 50, "onHandStock": 50, "reservedStock": 0, "origin": "Da Lat", "standard": "VietGAP", "primarySellerId": 22 },
                  { "productId": 502, "productName": "Rau lang huu co", "categoryName": "Rau Cu", "categoryId": 3, "unitName": "kg", "price": 26000, "averageRating": 0, "soldCount": 0, "stockQuantity": 50, "availableStock": 50, "onHandStock": 50, "reservedStock": 0, "origin": "Da Lat", "standard": "VietGAP", "primarySellerId": 22 },
                  { "productId": 503, "productName": "Rau ram huu co", "categoryName": "Rau Thom", "categoryId": 4, "unitName": "kg", "price": 27000, "averageRating": 0, "soldCount": 0, "stockQuantity": 50, "availableStock": 50, "onHandStock": 50, "reservedStock": 0, "origin": "Da Lat", "standard": "VietGAP", "primarySellerId": 22 },
                  { "productId": 504, "productName": "Rau diep huu co", "categoryName": "Rau Thom", "categoryId": 4, "unitName": "kg", "price": 28000, "averageRating": 0, "soldCount": 0, "stockQuantity": 50, "availableStock": 50, "onHandStock": 50, "reservedStock": 0, "origin": "Can Tho", "standard": "VietGAP", "primarySellerId": 33 },
                  { "productId": 505, "productName": "Rau ma huu co", "categoryName": "Rau La", "categoryId": 5, "unitName": "kg", "price": 29000, "averageRating": 0, "soldCount": 0, "stockQuantity": 50, "availableStock": 50, "onHandStock": 50, "reservedStock": 0, "origin": "Can Tho", "standard": "VietGAP", "primarySellerId": 33 },
                  { "productId": 506, "productName": "Rau cai huu co", "categoryName": "Rau La", "categoryId": 5, "unitName": "kg", "price": 30000, "averageRating": 0, "soldCount": 0, "stockQuantity": 50, "availableStock": 50, "onHandStock": 50, "reservedStock": 0, "origin": "Can Tho", "standard": "VietGAP", "primarySellerId": 33 },
                  { "productId": 507, "productName": "Rau ngo huu co", "categoryName": "Rau La", "categoryId": 5, "unitName": "kg", "price": 31000, "averageRating": 0, "soldCount": 0, "stockQuantity": 50, "availableStock": 50, "onHandStock": 50, "reservedStock": 0, "origin": "Long An", "standard": "VietGAP", "primarySellerId": 44 }
                ]
                """)
            });
        var identityHandler = new RecordingHttpMessageHandler(_ =>
            new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("""{"merchants": []}""")
            });
        var orderingHandler = new RecordingHttpMessageHandler(request =>
        {
            var path = request.RequestUri?.AbsolutePath ?? string.Empty;
            if (path.Contains("/api/orders/product-insights/search-ranking", StringComparison.OrdinalIgnoreCase))
            {
                Assert.Contains("keyword=rau", request.RequestUri?.Query);
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Headers =
                    {
                        { "X-Recommendation-Signal-Source", "materialized_cf_v1" }
                    },
                    Content = new StringContent("""
                    [
                      { "productId": 501, "hybridSearchScore": 200, "searchClickCount": 10, "searchRecommendationClickCount": 0, "searchViewSessionCount": 0 },
                      { "productId": 502, "hybridSearchScore": 190, "searchClickCount": 9, "searchRecommendationClickCount": 0, "searchViewSessionCount": 0 },
                      { "productId": 503, "hybridSearchScore": 180, "searchClickCount": 8, "searchRecommendationClickCount": 0, "searchViewSessionCount": 0 },
                      { "productId": 504, "hybridSearchScore": 170, "searchClickCount": 7, "searchRecommendationClickCount": 0, "searchViewSessionCount": 0 },
                      { "productId": 505, "hybridSearchScore": 160, "searchClickCount": 6, "searchRecommendationClickCount": 0, "searchViewSessionCount": 0 },
                      { "productId": 506, "hybridSearchScore": 150, "searchClickCount": 5, "searchRecommendationClickCount": 0, "searchViewSessionCount": 0 },
                      { "productId": 507, "hybridSearchScore": 140, "searchClickCount": 4, "searchRecommendationClickCount": 0, "searchViewSessionCount": 0 }
                    ]
                    """)
                };
            }

            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("[]")
            };
        });

        var controller = CreateController(catalogHandler, identityHandler, orderingHandler);

        var result = await controller.SearchProducts(
            name: "rau",
            categoryIds: null,
            sellerIds: null,
            availability: null,
            deliveryScopes: null,
            origins: null,
            standards: null,
            units: null,
            minPrice: null,
            maxPrice: null,
            minRating: null,
            preset: null,
            sort: null,
            page: 1,
            pageSize: 6);

        var ok = Assert.IsType<OkObjectResult>(result);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value, WebJson));
        var items = json.RootElement.GetProperty("items").EnumerateArray().ToList();
        var topSellerIds = items.Take(6).Select(item => item.GetProperty("primarySellerId").GetInt32()).ToList();

        Assert.Equal(new[] { 22, 22, 33, 33, 44, 22 }, topSellerIds);
    }

    [Fact]
    public async Task SearchProducts_IncludesRankingFallbackReason_WhenOrderingFallsBackToAdHocSignals()
    {
        var catalogHandler = new RecordingHttpMessageHandler(_ =>
            new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("""
                [
                  {
                    "productId": 501,
                    "productName": "Rau muong huu co",
                    "categoryName": "Rau Cu",
                    "categoryId": 3,
                    "unitName": "kg",
                    "price": 25000,
                    "averageRating": 0,
                    "soldCount": 0,
                    "stockQuantity": 50,
                    "availableStock": 50,
                    "onHandStock": 50,
                    "reservedStock": 0,
                    "origin": "Da Lat",
                    "standard": "VietGAP",
                    "primarySellerId": 22
                  }
                ]
                """)
            });
        var identityHandler = new RecordingHttpMessageHandler(_ =>
            new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("""
                {
                  "merchants": [
                    {
                      "sellerId": 22,
                      "shopName": "Vuon Rau Quan 1",
                      "userName": "seller22",
                      "addressSummary": "45 Le Loi, Phuong Ben Nghe, Quan 1",
                      "joinedAt": "2026-03-01T00:00:00Z"
                    }
                  ]
                }
                """)
            });
        var orderingHandler = new RecordingHttpMessageHandler(request =>
        {
            var path = request.RequestUri?.AbsolutePath;
            if (string.Equals(path, "/api/orders/product-insights/stats", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("""
                    [
                      { "productId": 501, "averageRating": 4.4, "reviewCount": 12, "soldCount": 31 }
                    ]
                    """)
                };
            }

            var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("""
                [
                  {
                    "productId": 501,
                    "searchClickCount": 1,
                    "searchClickSessionCount": 1,
                    "searchViewSessionCount": 1,
                    "searchRecommendationClickCount": 0,
                    "hybridSearchScore": 26
                  }
                ]
                """)
            };
            response.Headers.Add("X-Recommendation-Signal-Source", "ad_hoc_cf_v1");
            response.Headers.Add("X-Recommendation-Fallback-Reason", "no_materialized_keyword_affinity");
            return response;
        });
        var controller = CreateController(catalogHandler, identityHandler, orderingHandler);

        var result = await controller.SearchProducts(
            name: "rau",
            categoryIds: null,
            sellerIds: null,
            availability: null,
            deliveryScopes: null,
            origins: null,
            standards: null,
            units: null,
            minPrice: null,
            maxPrice: null,
            minRating: null,
            preset: null,
            sort: null,
            page: 1,
            pageSize: 12);

        var ok = Assert.IsType<OkObjectResult>(result);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value, WebJson));
        Assert.Equal("hybrid_search_v1", json.RootElement.GetProperty("rankingAlgorithm").GetString());
        Assert.Equal("ad_hoc_cf_v1", json.RootElement.GetProperty("rankingSignalSource").GetString());
        Assert.Equal("no_materialized_keyword_affinity", json.RootElement.GetProperty("rankingFallbackReason").GetString());
        var searchSignalBreakdown = json.RootElement.GetProperty("signalBreakdown");
        Assert.Equal("no_materialized_keyword_affinity", searchSignalBreakdown.GetProperty("overallReason").GetString());
    }

    [Fact]
    public async Task SearchProducts_IncludesRankingFallbackReason_WhenOrderingHasNoTrackedKeywordSessions()
    {
        var catalogHandler = new RecordingHttpMessageHandler(_ =>
            new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("""
                [
                  {
                    "productId": 105,
                    "productName": "Bông cải trắng mini",
                    "categoryName": "Rau ăn hoa / thân / mầm",
                    "categoryId": 2,
                    "unitName": "Kilogram",
                    "price": 28000,
                    "status": true,
                    "availableStock": 12
                  },
                  {
                    "productId": 118,
                    "productName": "Dưa hấu không hạt mini",
                    "categoryName": "Trái cây",
                    "categoryId": 7,
                    "unitName": "Kilogram",
                    "price": 42000,
                    "status": true,
                    "availableStock": 8
                  }
                ]
                """)
            });
        var identityHandler = new RecordingHttpMessageHandler(_ =>
            new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("[]")
            });
        var orderingHandler = new RecordingHttpMessageHandler(request =>
        {
            var path = request.RequestUri?.AbsolutePath;
            if (string.Equals(path, "/api/orders/product-insights/stats", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("""
                    [
                      { "productId": 105, "averageRating": 5.0, "reviewCount": 1, "soldCount": 0 },
                      { "productId": 118, "averageRating": 0, "reviewCount": 0, "soldCount": 1 }
                    ]
                    """)
                };
            }

            var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("[]")
            };
            response.Headers.Add("X-Recommendation-Fallback-Reason", "no_tracked_keyword_sessions");
            return response;
        });

        var controller = CreateController(catalogHandler, identityHandler, orderingHandler);
        var result = await controller.SearchProducts(name: "mini", page: 1, pageSize: 5);

        var ok = Assert.IsType<OkObjectResult>(result);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value, WebJson));
        Assert.Equal("keyword_relevance_v1", json.RootElement.GetProperty("rankingAlgorithm").GetString());
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("rankingSignalSource").ValueKind);
        Assert.Equal("no_tracked_keyword_sessions", json.RootElement.GetProperty("rankingFallbackReason").GetString());
        var searchSignalBreakdown = json.RootElement.GetProperty("signalBreakdown");
        Assert.Equal(JsonValueKind.Null, searchSignalBreakdown.GetProperty("overall").ValueKind);
        Assert.Equal("no_tracked_keyword_sessions", searchSignalBreakdown.GetProperty("overallReason").GetString());
    }

    [Fact]
    public async Task GetHomeRecommendations_ReturnsDiversifiedContentBasedItems()
    {
        var catalogHandler = new RecordingHttpMessageHandler(request =>
        {
            Assert.Equal("/api/products", request.RequestUri?.AbsolutePath);
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("""
                [
                  {
                    "productId": 104,
                    "productName": "Ca chua huu co",
                    "categoryName": "Rau Cu",
                    "categoryId": 3,
                    "unitName": "kg",
                    "price": 32000,
                    "status": true,
                    "availableStock": 24,
                    "origin": "Da Lat",
                    "standard": "VietGAP",
                    "preservation": "giu mat",
                    "weight": "500g"
                  },
                  {
                    "productId": 105,
                    "productName": "Ca rot huu co",
                    "categoryName": "Rau Cu",
                    "categoryId": 3,
                    "unitName": "kg",
                    "price": 28000,
                    "status": true,
                    "availableStock": 18,
                    "origin": "Da Lat",
                    "standard": "VietGAP",
                    "preservation": "giu mat",
                    "weight": "500g"
                  },
                  {
                    "productId": 220,
                    "productName": "Cam san vuon",
                    "categoryName": "Trai Cay",
                    "categoryId": 7,
                    "unitName": "kg",
                    "price": 45000,
                    "status": true,
                    "availableStock": 40,
                    "origin": "Vinh Long",
                    "standard": "VietGAP",
                    "preservation": "thoang mat",
                    "weight": "1kg"
                  }
                ]
                """)
            };
        });
        var identityHandler = new RecordingHttpMessageHandler(_ =>
            new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("[]")
            });
        var orderingHandler = new RecordingHttpMessageHandler(request =>
        {
            var path = request.RequestUri?.AbsolutePath;
            if (string.Equals(path, "/api/orders/product-insights/stats", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("""
                    [
                      { "productId": 104, "averageRating": 4.9, "reviewCount": 21, "soldCount": 66 },
                      { "productId": 105, "averageRating": 4.2, "reviewCount": 9, "soldCount": 18 },
                      { "productId": 220, "averageRating": 4.8, "reviewCount": 17, "soldCount": 40 }
                    ]
                    """)
                };
            }

            if (string.Equals(path, "/api/orders/product-insights/home-profile", StringComparison.Ordinal) ||
                string.Equals(path, "/api/orders/product-insights/home-collaborative", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("[]")
                };
            }

            throw new Xunit.Sdk.XunitException($"Unexpected Ordering path: {path}");
        });
        var controller = CreateController(catalogHandler, identityHandler, orderingHandler);

        var result = await controller.GetHomeRecommendations(limit: 2);

        var ok = Assert.IsType<OkObjectResult>(result);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value, WebJson));
        Assert.Equal("home_today", json.RootElement.GetProperty("placement").GetString());
        Assert.Equal("content_based_home_v1", json.RootElement.GetProperty("algorithm").GetString());
        Assert.Equal("catalog_content_v1", json.RootElement.GetProperty("contentSignalSource").GetString());
        var signalBreakdown = json.RootElement.GetProperty("signalBreakdown");
        Assert.Equal("catalog_content_v1", signalBreakdown.GetProperty("content").GetString());
        Assert.Equal(JsonValueKind.Null, signalBreakdown.GetProperty("overall").ValueKind);
        Assert.Equal(JsonValueKind.Null, signalBreakdown.GetProperty("preference").ValueKind);
        Assert.Equal(JsonValueKind.Null, signalBreakdown.GetProperty("collaborative").ValueKind);
        var items = json.RootElement.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(2, items.Count);
        Assert.Equal(104, items[0].GetProperty("productId").GetInt32());
        Assert.Equal(220, items[1].GetProperty("productId").GetInt32());
        var topReason = items[0].GetProperty("recommendationReason").GetString();
        Assert.False(string.IsNullOrWhiteSpace(topReason));
        Assert.True(
            topReason!.Contains("Được chọn nhiều", StringComparison.Ordinal)
            || topReason.Contains("Đánh giá tốt", StringComparison.Ordinal)
            || topReason.Contains("Nổi bật trong", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GetHomeRecommendations_IncludesSeasonalTag_WhenColdStartItemMatchesCurrentSeason()
    {
        var catalogHandler = new RecordingHttpMessageHandler(request =>
        {
            Assert.Equal("/api/products", request.RequestUri?.AbsolutePath);
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("""
                [
                  {
                    "productId": 808,
                    "productName": "Mang tay xanh huu co",
                    "categoryName": "Rau ăn hoa / thân / mầm",
                    "categoryId": 8,
                    "unitName": "kg",
                    "price": 52000,
                    "status": true,
                    "availableStock": 18,
                    "origin": "Ninh Thuận, Việt Nam",
                    "standard": "VietGAP",
                    "preservation": "giu mat",
                    "weight": "500g"
                  }
                ]
                """)
            };
        });
        var identityHandler = new RecordingHttpMessageHandler(_ =>
            new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("[]")
            });
        var orderingHandler = new RecordingHttpMessageHandler(request =>
        {
            var path = request.RequestUri?.AbsolutePath;
            if (string.Equals(path, "/api/orders/product-insights/stats", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("""
                    [
                      { "productId": 808, "averageRating": 4.8, "reviewCount": 15, "soldCount": 33 }
                    ]
                    """)
                };
            }

            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("[]")
            };
        });
        var controller = CreateController(catalogHandler, identityHandler, orderingHandler);

        var result = await controller.GetHomeRecommendations(limit: 1);

        var ok = Assert.IsType<OkObjectResult>(result);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value, WebJson));
        var item = json.RootElement.GetProperty("items")[0];
        Assert.Contains("Đang đúng mùa", item.GetProperty("recommendationReason").GetString());
    }

    [Fact]
    public async Task GetHomeRecommendations_PrefersSeasonalOriginAlignedItem_WhenColdStartScoresAreClose()
    {
        var catalogHandler = new RecordingHttpMessageHandler(request =>
        {
            Assert.Equal("/api/products", request.RequestUri?.AbsolutePath);
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("""
                [
                  {
                    "productId": 881,
                    "productName": "Mang tay Da Nang",
                    "categoryName": "Rau ăn hoa / thân / mầm",
                    "categoryId": 8,
                    "unitName": "kg",
                    "price": 52000,
                    "status": true,
                    "availableStock": 18,
                    "origin": "Da Nang, Viet Nam",
                    "standard": "VietGAP",
                    "preservation": "giu mat",
                    "weight": "500g",
                    "primarySellerId": 81
                  },
                  {
                    "productId": 882,
                    "productName": "Rau huu co tong hop",
                    "categoryName": "Rau ăn hoa / thân / mầm",
                    "categoryId": 8,
                    "unitName": "kg",
                    "price": 51000,
                    "status": true,
                    "availableStock": 22,
                    "origin": "Da Lat, Viet Nam",
                    "standard": "VietGAP",
                    "preservation": "giu mat",
                    "weight": "500g",
                    "primarySellerId": 82
                  }
                ]
                """)
            };
        });
        var identityHandler = new RecordingHttpMessageHandler(_ =>
            new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("""
                {
                  "merchants": [
                    {
                      "sellerId": 81,
                      "shopName": "Vuon Mien Trung",
                      "userName": "seller81",
                      "addressSummary": "18 Tran Phu, Da Nang",
                      "joinedAt": "2025-01-01T00:00:00Z"
                    },
                    {
                      "sellerId": 82,
                      "shopName": "Vuon Tay Nguyen",
                      "userName": "seller82",
                      "addressSummary": "25 Yersin, Da Nang",
                      "joinedAt": "2025-01-01T00:00:00Z"
                    }
                  ]
                }
                """)
            });
        var orderingHandler = new RecordingHttpMessageHandler(request =>
        {
            var path = request.RequestUri?.AbsolutePath;
            if (string.Equals(path, "/api/orders/product-insights/stats", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("""
                    [
                      { "productId": 881, "averageRating": 4.7, "reviewCount": 16, "soldCount": 35 },
                      { "productId": 882, "averageRating": 4.8, "reviewCount": 18, "soldCount": 39 }
                    ]
                    """)
                };
            }

            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("[]")
            };
        });
        var controller = CreateController(catalogHandler, identityHandler, orderingHandler);

        var result = await controller.GetHomeRecommendations(limit: 1);

        var ok = Assert.IsType<OkObjectResult>(result);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value, WebJson));
        var item = json.RootElement.GetProperty("items")[0];
        Assert.Equal(881, item.GetProperty("productId").GetInt32());
        var reason = item.GetProperty("recommendationReason").GetString();
        Assert.StartsWith("Đúng mùa ở vùng trồng này", reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetHomeRecommendations_IncludesSellerLocalityReason_WhenMerchantAddressesShareRegion()
    {
        var catalogHandler = new RecordingHttpMessageHandler(request =>
        {
            Assert.Equal("/api/products", request.RequestUri?.AbsolutePath);
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("""
                [
                  {
                    "productId": 901,
                    "productName": "Dua leo huu co",
                    "categoryName": "Rau Cu",
                    "categoryId": 8,
                    "unitName": "kg",
                    "price": 32000,
                    "status": true,
                    "availableStock": 20,
                    "origin": "Can Tho, Viet Nam",
                    "standard": "VietGAP",
                    "preservation": "giu mat",
                    "weight": "500g",
                    "primarySellerId": 31
                  },
                  {
                    "productId": 902,
                    "productName": "Bi ngo xanh",
                    "categoryName": "Rau Cu",
                    "categoryId": 8,
                    "unitName": "kg",
                    "price": 34000,
                    "status": true,
                    "availableStock": 18,
                    "origin": "Da Nang, Viet Nam",
                    "standard": "VietGAP",
                    "preservation": "giu mat",
                    "weight": "600g",
                    "primarySellerId": 32
                  }
                ]
                """)
            };
        });
        var identityHandler = new RecordingHttpMessageHandler(request =>
        {
            Assert.Equal("/auth/public/merchants", request.RequestUri?.AbsolutePath);
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("""
                {
                  "merchants": [
                    {
                      "sellerId": 31,
                      "shopName": "Vuon Da Lat",
                      "userName": "seller31",
                      "addressSummary": "12 Hoang Van Thu, Da Lat, Lam Dong",
                      "joinedAt": "2025-01-01T00:00:00Z"
                    },
                    {
                      "sellerId": 32,
                      "shopName": "Vuon Bao Loc",
                      "userName": "seller32",
                      "addressSummary": "9 Tran Phu, Bao Loc, Lam Dong",
                      "joinedAt": "2025-01-01T00:00:00Z"
                    }
                  ]
                }
                """)
            };
        });
        var orderingHandler = new RecordingHttpMessageHandler(request =>
        {
            var path = request.RequestUri?.AbsolutePath;
            if (string.Equals(path, "/api/orders/product-insights/stats", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("""
                    [
                      { "productId": 901, "averageRating": 4.5, "reviewCount": 15, "soldCount": 21 },
                      { "productId": 902, "averageRating": 4.7, "reviewCount": 17, "soldCount": 28 }
                    ]
                    """)
                };
            }

            if (string.Equals(path, "/api/orders/product-insights/home-profile", StringComparison.Ordinal))
            {
                var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("""
                    [
                      {
                        "productId": 901,
                        "viewCount": 2,
                        "searchClickCount": 1,
                        "recommendationClickCount": 0,
                        "purchaseCount": 0,
                        "preferenceScore": 48
                      }
                    ]
                    """)
                };
                response.Headers.Add("X-Recommendation-Signal-Source", "materialized_profile_v1");
                return response;
            }

            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("[]")
            };
        });
        var controller = CreateController(catalogHandler, identityHandler, orderingHandler);

        var result = await controller.GetHomeRecommendations(limit: 2);

        var ok = Assert.IsType<OkObjectResult>(result);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value, WebJson));
        var items = json.RootElement.GetProperty("items").EnumerateArray().ToList();
        var candidate = items.Single(item => item.GetProperty("productId").GetInt32() == 902);
        Assert.Contains("Từ shop cùng vùng bạn vừa xem", candidate.GetProperty("recommendationReason").GetString());
    }

    [Fact]
    public async Task GetHomeRecommendations_PrefersOriginLocalityOverSellerLocality_WhenSignalsConflict()
    {
        var catalogHandler = new RecordingHttpMessageHandler(request =>
        {
            Assert.Equal("/api/products", request.RequestUri?.AbsolutePath);
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("""
                [
                  {
                    "productId": 931,
                    "productName": "Dua leo seed",
                    "categoryName": "Rau Cu",
                    "categoryId": 8,
                    "unitName": "kg",
                    "price": 32000,
                    "status": true,
                    "availableStock": 20,
                    "origin": "Can Tho, Viet Nam",
                    "standard": "VietGAP",
                    "preservation": "giu mat",
                    "weight": "500g",
                    "primarySellerId": 41
                  },
                  {
                    "productId": 932,
                    "productName": "Dua leo Can Tho",
                    "categoryName": "Rau Cu",
                    "categoryId": 8,
                    "unitName": "kg",
                    "price": 33000,
                    "status": true,
                    "availableStock": 18,
                    "origin": "Can Tho, Viet Nam",
                    "standard": "VietGAP",
                    "preservation": "giu mat",
                    "weight": "520g",
                    "primarySellerId": 42
                  },
                  {
                    "productId": 933,
                    "productName": "Dua leo Da Lat",
                    "categoryName": "Rau Cu",
                    "categoryId": 8,
                    "unitName": "kg",
                    "price": 33000,
                    "status": true,
                    "availableStock": 18,
                    "origin": "Da Lat, Viet Nam",
                    "standard": "VietGAP",
                    "preservation": "giu mat",
                    "weight": "520g",
                    "primarySellerId": 43
                  }
                ]
                """)
            };
        });
        var identityHandler = new RecordingHttpMessageHandler(_ =>
            new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("""
                {
                  "merchants": [
                    {
                      "sellerId": 41,
                      "shopName": "Seed Shop",
                      "userName": "seller41",
                      "addressSummary": "18 Yersin, Da Lat, Lam Dong",
                      "joinedAt": "2025-01-01T00:00:00Z"
                    },
                    {
                      "sellerId": 42,
                      "shopName": "Can Tho Farm",
                      "userName": "seller42",
                      "addressSummary": "10 Le Loi, Hai Phong",
                      "joinedAt": "2025-01-01T00:00:00Z"
                    },
                    {
                      "sellerId": 43,
                      "shopName": "Bao Loc Shop",
                      "userName": "seller43",
                      "addressSummary": "27 Le Hong Phong, Bao Loc, Lam Dong",
                      "joinedAt": "2025-01-01T00:00:00Z"
                    }
                  ]
                }
                """)
            });
        var orderingHandler = new RecordingHttpMessageHandler(request =>
        {
            var path = request.RequestUri?.AbsolutePath;
            if (string.Equals(path, "/api/orders/product-insights/stats", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("""
                    [
                      { "productId": 931, "averageRating": 4.2, "reviewCount": 9, "soldCount": 15 },
                      { "productId": 932, "averageRating": 4.2, "reviewCount": 9, "soldCount": 15 },
                      { "productId": 933, "averageRating": 4.2, "reviewCount": 9, "soldCount": 15 }
                    ]
                    """)
                };
            }

            if (string.Equals(path, "/api/orders/product-insights/home-profile", StringComparison.Ordinal))
            {
                var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("""
                    [
                      {
                        "productId": 931,
                        "viewCount": 2,
                        "searchClickCount": 1,
                        "recommendationClickCount": 0,
                        "purchaseCount": 0,
                        "preferenceScore": 48
                      }
                    ]
                    """)
                };
                response.Headers.Add("X-Recommendation-Signal-Source", "materialized_profile_v1");
                return response;
            }

            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("[]")
            };
        });
        var controller = CreateController(catalogHandler, identityHandler, orderingHandler);

        var result = await controller.GetHomeRecommendations(limit: 3);

        var ok = Assert.IsType<OkObjectResult>(result);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value, WebJson));
        var items = json.RootElement.GetProperty("items").EnumerateArray().ToList();
        Assert.Contains(items, item => item.GetProperty("productId").GetInt32() == 932);
        var exactOriginCandidate = items.Single(item => item.GetProperty("productId").GetInt32() == 932);
        Assert.Contains("Cùng xuất xứ bạn vừa xem", exactOriginCandidate.GetProperty("recommendationReason").GetString());
    }

    [Fact]
    public async Task GetHomeRecommendations_FirstPassDiversifiesAcrossSellers_WhenEnoughCandidatesExist()
    {
        var catalogHandler = new RecordingHttpMessageHandler(request =>
        {
            Assert.Equal("/api/products", request.RequestUri?.AbsolutePath);
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("""
                [
                  {
                    "productId": 901,
                    "productName": "Rau huu co A",
                    "categoryName": "Rau la A",
                    "categoryId": 31,
                    "unitName": "kg",
                    "price": 28000,
                    "status": true,
                    "availableStock": 30,
                    "origin": "Da Lat",
                    "standard": "VietGAP",
                    "preservation": "giu mat",
                    "weight": "500g",
                    "primarySellerId": 4
                  },
                  {
                    "productId": 902,
                    "productName": "Rau huu co B",
                    "categoryName": "Rau la B",
                    "categoryId": 32,
                    "unitName": "kg",
                    "price": 28500,
                    "status": true,
                    "availableStock": 28,
                    "origin": "Da Lat",
                    "standard": "VietGAP",
                    "preservation": "giu mat",
                    "weight": "500g",
                    "primarySellerId": 4
                  },
                  {
                    "productId": 903,
                    "productName": "Rau huu co C",
                    "categoryName": "Rau la C",
                    "categoryId": 33,
                    "unitName": "kg",
                    "price": 29000,
                    "status": true,
                    "availableStock": 26,
                    "origin": "Da Lat",
                    "standard": "VietGAP",
                    "preservation": "giu mat",
                    "weight": "500g",
                    "primarySellerId": 4
                  },
                  {
                    "productId": 904,
                    "productName": "Rau huu co D",
                    "categoryName": "Rau la D",
                    "categoryId": 34,
                    "unitName": "kg",
                    "price": 29500,
                    "status": true,
                    "availableStock": 25,
                    "origin": "Da Lat",
                    "standard": "VietGAP",
                    "preservation": "giu mat",
                    "weight": "500g",
                    "primarySellerId": 4
                  },
                  {
                    "productId": 905,
                    "productName": "Rau huu co E",
                    "categoryName": "Rau la E",
                    "categoryId": 35,
                    "unitName": "kg",
                    "price": 30000,
                    "status": true,
                    "availableStock": 24,
                    "origin": "Can Tho",
                    "standard": "GlobalGAP",
                    "preservation": "giu mat",
                    "weight": "500g",
                    "primarySellerId": 9
                  }
                ]
                """)
            };
        });
        var identityHandler = new RecordingHttpMessageHandler(_ =>
            new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("[]")
            });
        var orderingHandler = new RecordingHttpMessageHandler(request =>
        {
            var path = request.RequestUri?.AbsolutePath;
            if (string.Equals(path, "/api/orders/product-insights/stats", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("""
                    [
                      { "productId": 901, "averageRating": 4.9, "reviewCount": 25, "soldCount": 70 },
                      { "productId": 902, "averageRating": 4.8, "reviewCount": 21, "soldCount": 61 },
                      { "productId": 903, "averageRating": 4.7, "reviewCount": 18, "soldCount": 55 },
                      { "productId": 904, "averageRating": 4.6, "reviewCount": 15, "soldCount": 49 },
                      { "productId": 905, "averageRating": 4.5, "reviewCount": 14, "soldCount": 45 }
                    ]
                    """)
                };
            }

            if (string.Equals(path, "/api/orders/product-insights/home-profile", StringComparison.Ordinal) ||
                string.Equals(path, "/api/orders/product-insights/home-collaborative", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("[]")
                };
            }

            throw new Xunit.Sdk.XunitException($"Unexpected Ordering path: {path}");
        });
        var controller = CreateController(catalogHandler, identityHandler, orderingHandler);

        var result = await controller.GetHomeRecommendations(limit: 4);

        var ok = Assert.IsType<OkObjectResult>(result);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value, WebJson));
        var items = json.RootElement.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(4, items.Count);
        Assert.Contains(items, item => item.GetProperty("productId").GetInt32() == 905);
        Assert.Equal(3, items.Count(item => item.GetProperty("primarySellerId").GetInt32() == 4));
        Assert.Equal(1, items.Count(item => item.GetProperty("primarySellerId").GetInt32() == 9));
    }

    [Fact]
    public async Task GetHomeRecommendations_UsesTighterSellerAndCategoryCaps_ForCompactLimit()
    {
        var catalogHandler = new RecordingHttpMessageHandler(request =>
        {
            Assert.Equal("/api/products", request.RequestUri?.AbsolutePath);
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("""
                [
                  { "productId": 911, "productName": "Rau huu co A", "categoryName": "Rau la A", "categoryId": 41, "unitName": "kg", "price": 28000, "status": true, "availableStock": 30, "origin": "Da Lat", "standard": "VietGAP", "preservation": "giu mat", "weight": "500g", "primarySellerId": 4 },
                  { "productId": 912, "productName": "Rau huu co B", "categoryName": "Rau la B", "categoryId": 42, "unitName": "kg", "price": 27500, "status": true, "availableStock": 28, "origin": "Da Lat", "standard": "VietGAP", "preservation": "giu mat", "weight": "500g", "primarySellerId": 4 },
                  { "productId": 913, "productName": "Qua huu co C", "categoryName": "Trai cay C", "categoryId": 43, "unitName": "kg", "price": 32000, "status": true, "availableStock": 27, "origin": "Ben Tre", "standard": "GlobalGAP", "preservation": "thoang mat", "weight": "500g", "primarySellerId": 9 },
                  { "productId": 914, "productName": "Cu huu co D", "categoryName": "Cu qua D", "categoryId": 44, "unitName": "kg", "price": 33000, "status": true, "availableStock": 26, "origin": "Can Tho", "standard": "VietGAP", "preservation": "giu mat", "weight": "500g", "primarySellerId": 11 },
                  { "productId": 915, "productName": "Trai huu co E", "categoryName": "Trai cay E", "categoryId": 45, "unitName": "kg", "price": 34000, "status": true, "availableStock": 25, "origin": "Long An", "standard": "VietGAP", "preservation": "thoang mat", "weight": "500g", "primarySellerId": 12 }
                ]
                """)
            };
        });
        var identityHandler = new RecordingHttpMessageHandler(_ =>
            new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("[]")
            });
        var orderingHandler = new RecordingHttpMessageHandler(request =>
        {
            var path = request.RequestUri?.AbsolutePath;
            if (string.Equals(path, "/api/orders/product-insights/stats", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("""
                    [
                      { "productId": 911, "averageRating": 4.9, "reviewCount": 25, "soldCount": 70 },
                      { "productId": 912, "averageRating": 4.8, "reviewCount": 21, "soldCount": 61 },
                      { "productId": 913, "averageRating": 4.7, "reviewCount": 18, "soldCount": 55 },
                      { "productId": 914, "averageRating": 4.6, "reviewCount": 15, "soldCount": 49 },
                      { "productId": 915, "averageRating": 4.5, "reviewCount": 14, "soldCount": 45 }
                    ]
                    """)
                };
            }

            if (string.Equals(path, "/api/orders/product-insights/home-profile", StringComparison.Ordinal) ||
                string.Equals(path, "/api/orders/product-insights/home-collaborative", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("[]")
                };
            }

            throw new Xunit.Sdk.XunitException($"Unexpected Ordering path: {path}");
        });
        var controller = CreateController(catalogHandler, identityHandler, orderingHandler);

        var result = await controller.GetHomeRecommendations(limit: 4);

        var ok = Assert.IsType<OkObjectResult>(result);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value, WebJson));
        var items = json.RootElement.GetProperty("items").EnumerateArray().ToList();
        var topSellerIds = items.Select(item => item.GetProperty("primarySellerId").GetInt32()).ToList();
        var topCategoryNames = items.Select(item => item.GetProperty("categoryName").GetString()).ToList();

        Assert.Equal(4, items.Count);
        Assert.True(topSellerIds.Distinct().Count() >= 3);
        Assert.True(topSellerIds.Count(id => id == 4) <= 2);
        Assert.Contains(9, topSellerIds);
        Assert.Contains(11, topSellerIds);
        Assert.Equal(4, topCategoryNames.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public async Task GetHomeRecommendations_SecondPassPrefersNewSellerAndRegion_WhenFillingCompactList()
    {
        var catalogHandler = new RecordingHttpMessageHandler(request =>
        {
            Assert.Equal("/api/products", request.RequestUri?.AbsolutePath);
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("""
                [
                  { "productId": 941, "productName": "Rau huu co A", "categoryName": "Rau la A", "categoryId": 61, "unitName": "kg", "price": 28000, "status": true, "availableStock": 30, "origin": "Da Lat, Viet Nam", "standard": "VietGAP", "preservation": "giu mat", "weight": "500g", "primarySellerId": 4 },
                  { "productId": 942, "productName": "Rau huu co B", "categoryName": "Rau la A", "categoryId": 61, "unitName": "kg", "price": 27900, "status": true, "availableStock": 29, "origin": "Da Lat, Viet Nam", "standard": "VietGAP", "preservation": "giu mat", "weight": "500g", "primarySellerId": 4 },
                  { "productId": 943, "productName": "Qua huu co C", "categoryName": "Trai cay C", "categoryId": 62, "unitName": "kg", "price": 32000, "status": true, "availableStock": 27, "origin": "Ben Tre, Viet Nam", "standard": "GlobalGAP", "preservation": "thoang mat", "weight": "500g", "primarySellerId": 9 },
                  { "productId": 944, "productName": "Cu huu co D", "categoryName": "Cu qua D", "categoryId": 63, "unitName": "kg", "price": 33000, "status": true, "availableStock": 26, "origin": "Can Tho, Viet Nam", "standard": "VietGAP", "preservation": "giu mat", "weight": "500g", "primarySellerId": 11 },
                  { "productId": 945, "productName": "Trai huu co E", "categoryName": "Trai cay E", "categoryId": 64, "unitName": "kg", "price": 34000, "status": true, "availableStock": 25, "origin": "Long An, Viet Nam", "standard": "VietGAP", "preservation": "thoang mat", "weight": "500g", "primarySellerId": 12 }
                ]
                """)
            };
        });
        var identityHandler = new RecordingHttpMessageHandler(_ =>
            new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("[]")
            });
        var orderingHandler = new RecordingHttpMessageHandler(request =>
        {
            var path = request.RequestUri?.AbsolutePath;
            if (string.Equals(path, "/api/orders/product-insights/stats", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("""
                    [
                      { "productId": 941, "averageRating": 4.9, "reviewCount": 25, "soldCount": 70 },
                      { "productId": 942, "averageRating": 4.8, "reviewCount": 21, "soldCount": 61 },
                      { "productId": 943, "averageRating": 4.7, "reviewCount": 18, "soldCount": 55 },
                      { "productId": 944, "averageRating": 4.1, "reviewCount": 8, "soldCount": 16 },
                      { "productId": 945, "averageRating": 4.0, "reviewCount": 7, "soldCount": 14 }
                    ]
                    """)
                };
            }

            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("[]")
            };
        });
        var controller = CreateController(catalogHandler, identityHandler, orderingHandler);

        var result = await controller.GetHomeRecommendations(limit: 4);

        var ok = Assert.IsType<OkObjectResult>(result);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value, WebJson));
        var items = json.RootElement.GetProperty("items").EnumerateArray().ToList();
        var topSellerIds = items.Select(item => item.GetProperty("primarySellerId").GetInt32()).ToList();

        Assert.Equal(4, items.Count);
        Assert.Contains(12, topSellerIds);
        Assert.Equal(4, topSellerIds.Distinct().Count());
    }

    [Fact]
    public async Task GetHomeRecommendations_UsesTighterRegionCap_ForCompactLimit()
    {
        var catalogHandler = new RecordingHttpMessageHandler(request =>
        {
            Assert.Equal("/api/products", request.RequestUri?.AbsolutePath);
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("""
                [
                  { "productId": 921, "productName": "Rau huu co A", "categoryName": "Rau la A", "categoryId": 51, "unitName": "kg", "price": 28000, "status": true, "availableStock": 30, "origin": "Da Lat, Viet Nam", "standard": "VietGAP", "preservation": "giu mat", "weight": "500g", "primarySellerId": 4 },
                  { "productId": 922, "productName": "Rau huu co B", "categoryName": "Rau la B", "categoryId": 52, "unitName": "kg", "price": 27500, "status": true, "availableStock": 28, "origin": "Lam Dong, Viet Nam", "standard": "VietGAP", "preservation": "giu mat", "weight": "500g", "primarySellerId": 9 },
                  { "productId": 923, "productName": "Qua huu co C", "categoryName": "Trai cay C", "categoryId": 53, "unitName": "kg", "price": 32000, "status": true, "availableStock": 27, "origin": "Can Tho, Viet Nam", "standard": "GlobalGAP", "preservation": "thoang mat", "weight": "500g", "primarySellerId": 11 },
                  { "productId": 924, "productName": "Cu huu co D", "categoryName": "Cu qua D", "categoryId": 54, "unitName": "kg", "price": 33000, "status": true, "availableStock": 26, "origin": "Hai Phong, Viet Nam", "standard": "VietGAP", "preservation": "giu mat", "weight": "500g", "primarySellerId": 12 },
                  { "productId": 925, "productName": "Trai huu co E", "categoryName": "Trai cay E", "categoryId": 55, "unitName": "kg", "price": 34000, "status": true, "availableStock": 25, "origin": "Bao Loc, Lam Dong", "standard": "VietGAP", "preservation": "thoang mat", "weight": "500g", "primarySellerId": 13 }
                ]
                """)
            };
        });
        var identityHandler = new RecordingHttpMessageHandler(_ =>
            new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("[]")
            });
        var orderingHandler = new RecordingHttpMessageHandler(request =>
        {
            var path = request.RequestUri?.AbsolutePath;
            if (string.Equals(path, "/api/orders/product-insights/stats", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("""
                    [
                      { "productId": 921, "averageRating": 4.9, "reviewCount": 25, "soldCount": 70 },
                      { "productId": 922, "averageRating": 4.8, "reviewCount": 21, "soldCount": 61 },
                      { "productId": 923, "averageRating": 4.7, "reviewCount": 18, "soldCount": 55 },
                      { "productId": 924, "averageRating": 4.6, "reviewCount": 15, "soldCount": 49 },
                      { "productId": 925, "averageRating": 4.5, "reviewCount": 14, "soldCount": 45 }
                    ]
                    """)
                };
            }

            if (string.Equals(path, "/api/orders/product-insights/home-profile", StringComparison.Ordinal) ||
                string.Equals(path, "/api/orders/product-insights/home-collaborative", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("[]")
                };
            }

            throw new Xunit.Sdk.XunitException($"Unexpected Ordering path: {path}");
        });
        var controller = CreateController(catalogHandler, identityHandler, orderingHandler);

        var result = await controller.GetHomeRecommendations(limit: 4);

        var ok = Assert.IsType<OkObjectResult>(result);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value, WebJson));
        var items = json.RootElement.GetProperty("items").EnumerateArray().ToList();
        var topItemIds = items.Select(item => item.GetProperty("productId").GetInt32()).ToList();

        Assert.DoesNotContain(925, topItemIds);
        Assert.Contains(921, topItemIds);
        Assert.Contains(923, topItemIds);
        Assert.Contains(924, topItemIds);
    }

    [Fact]
    public async Task GetHomeRecommendations_PrefersHybridHomeSignal_WhenOrderingProvidesPreferenceSeeds()
    {
        var catalogHandler = new RecordingHttpMessageHandler(request =>
        {
            Assert.Equal("/api/products", request.RequestUri?.AbsolutePath);
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("""
                [
                  {
                    "productId": 104,
                    "productName": "Ca chua huu co",
                    "categoryName": "Rau Cu",
                    "categoryId": 3,
                    "unitName": "kg",
                    "price": 32000,
                    "status": true,
                    "availableStock": 24,
                    "origin": "Da Lat",
                    "standard": "VietGAP",
                    "preservation": "giu mat",
                    "weight": "1kg"
                  },
                  {
                    "productId": 105,
                    "productName": "Xa lach huu co",
                    "categoryName": "Rau Cu",
                    "categoryId": 3,
                    "unitName": "kg",
                    "price": 29000,
                    "status": true,
                    "availableStock": 22,
                    "origin": "Da Lat",
                    "standard": "VietGAP",
                    "preservation": "giu mat",
                    "weight": "500g"
                  },
                  {
                    "productId": 220,
                    "productName": "Cam san vang",
                    "categoryName": "Trai Cay",
                    "categoryId": 5,
                    "unitName": "kg",
                    "price": 45000,
                    "status": true,
                    "availableStock": 30,
                    "origin": "Ben Tre",
                    "standard": "GlobalGAP",
                    "preservation": "thoang mat",
                    "weight": "1kg"
                  }
                ]
                """)
            };
        });
        var identityHandler = new RecordingHttpMessageHandler(_ =>
            new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("[]")
            });
        var orderingHandler = new RecordingHttpMessageHandler(request =>
        {
            var path = request.RequestUri?.AbsolutePath;
            if (string.Equals(path, "/api/orders/product-insights/stats", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("""
                    [
                      { "productId": 104, "averageRating": 4.6, "reviewCount": 10, "soldCount": 24 },
                      { "productId": 105, "averageRating": 4.3, "reviewCount": 4, "soldCount": 9 },
                      { "productId": 220, "averageRating": 4.9, "reviewCount": 17, "soldCount": 54 }
                    ]
                    """)
                };
            }

            if (string.Equals(path, "/api/orders/product-insights/home-profile", StringComparison.Ordinal))
            {
                var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("""
                    [
                      {
                        "productId": 105,
                        "viewCount": 2,
                        "searchClickCount": 2,
                        "recommendationClickCount": 0,
                        "purchaseCount": 0,
                        "preferenceScore": 76,
                        "lastInteractedAtUtc": "2026-03-29T01:02:03Z"
                      }
                    ]
                    """)
                };
                response.Headers.Add("X-Recommendation-Signal-Source", "materialized_profile_v1");
                return response;
            }

            if (string.Equals(path, "/api/orders/product-insights/home-collaborative", StringComparison.Ordinal))
            {
                var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("[]")
                };
                response.Headers.Add("X-Recommendation-Signal-Source", "ad_hoc_cf_v1");
                response.Headers.Add("X-Recommendation-Fallback-Reason", "no_materialized_collaborative_candidates");
                return response;
            }

            throw new Xunit.Sdk.XunitException($"Unexpected Ordering path: {path}");
        });
        var controller = CreateController(catalogHandler, identityHandler, orderingHandler);

        var result = await controller.GetHomeRecommendations(limit: 3);

        var ok = Assert.IsType<OkObjectResult>(result);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value, WebJson));
        Assert.Equal("hybrid_home_v1", json.RootElement.GetProperty("algorithm").GetString());
        Assert.Equal("catalog_content_v1", json.RootElement.GetProperty("contentSignalSource").GetString());
        Assert.Equal("collaborative:no_materialized_collaborative_candidates", json.RootElement.GetProperty("fallbackReason").GetString());
        Assert.Equal("materialized_profile_v1", json.RootElement.GetProperty("preferenceSignalSource").GetString());
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("preferenceFallbackReason").ValueKind);
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("collaborativeSignalSource").ValueKind);
        Assert.Equal("no_materialized_collaborative_candidates", json.RootElement.GetProperty("collaborativeFallbackReason").GetString());
        Assert.Equal("materialized_profile_v1", json.RootElement.GetProperty("signalSource").GetString());
        var signalBreakdown = json.RootElement.GetProperty("signalBreakdown");
        Assert.Equal("catalog_content_v1", signalBreakdown.GetProperty("content").GetString());
        Assert.Equal("materialized_profile_v1", signalBreakdown.GetProperty("overall").GetString());
        Assert.Equal("collaborative:no_materialized_collaborative_candidates", signalBreakdown.GetProperty("overallReason").GetString());
        Assert.Equal("materialized_profile_v1", signalBreakdown.GetProperty("preference").GetString());
        Assert.Equal(JsonValueKind.Null, signalBreakdown.GetProperty("preferenceReason").ValueKind);
        Assert.Equal(JsonValueKind.Null, signalBreakdown.GetProperty("collaborative").ValueKind);
        Assert.Equal("no_materialized_collaborative_candidates", signalBreakdown.GetProperty("collaborativeReason").GetString());
        var items = json.RootElement.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(3, items.Count);
        Assert.Equal(105, items[0].GetProperty("productId").GetInt32());
        Assert.Contains("Bạn từng bấm từ tìm kiếm", items[0].GetProperty("recommendationReason").GetString());
        var sections = json.RootElement.GetProperty("sections").EnumerateArray().ToList();
        Assert.Contains(sections, section => section.GetProperty("id").GetString() == "for_you");
    }

    [Fact]
    public async Task GetHomeRecommendations_CreatesBuyAgainSection_WhenOrderingSeedsContainPurchases()
    {
        var catalogHandler = new RecordingHttpMessageHandler(_ =>
            new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("""
                [
                  {
                    "productId": 104,
                    "productName": "Ca chua huu co",
                    "categoryName": "Rau Cu",
                    "categoryId": 3,
                    "unitName": "kg",
                    "price": 32000,
                    "status": true,
                    "availableStock": 24,
                    "origin": "Da Lat",
                    "standard": "VietGAP",
                    "preservation": "giu mat",
                    "weight": "500g"
                  },
                  {
                    "productId": 105,
                    "productName": "Xa lach huu co",
                    "categoryName": "Rau La",
                    "categoryId": 4,
                    "unitName": "kg",
                    "price": 29000,
                    "status": true,
                    "availableStock": 20,
                    "origin": "Long An",
                    "standard": "VietGAP",
                    "preservation": "giu mat",
                    "weight": "300g"
                  },
                  {
                    "productId": 220,
                    "productName": "Cam san vuon",
                    "categoryName": "Trai Cay",
                    "categoryId": 5,
                    "unitName": "kg",
                    "price": 45000,
                    "status": true,
                    "availableStock": 40,
                    "origin": "Ben Tre",
                    "standard": "GlobalGAP",
                    "preservation": "thoang mat",
                    "weight": "1kg"
                  }
                ]
                """)
            });
        var identityHandler = new RecordingHttpMessageHandler(_ =>
            new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("[]")
            });
        var orderingHandler = new RecordingHttpMessageHandler(request =>
        {
            var path = request.RequestUri?.AbsolutePath;
            if (string.Equals(path, "/api/orders/product-insights/stats", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("""
                    [
                      { "productId": 104, "averageRating": 4.6, "reviewCount": 10, "soldCount": 24 },
                      { "productId": 105, "averageRating": 4.4, "reviewCount": 4, "soldCount": 12 },
                      { "productId": 220, "averageRating": 4.8, "reviewCount": 17, "soldCount": 54 }
                    ]
                    """)
                };
            }

            if (string.Equals(path, "/api/orders/product-insights/home-profile", StringComparison.Ordinal))
            {
                var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent($$"""
                    [
                      {
                        "productId": 104,
                        "viewCount": 1,
                        "searchClickCount": 0,
                        "recommendationClickCount": 0,
                        "purchaseCount": 3,
                        "preferenceScore": 88,
                        "lastInteractedAtUtc": "{{RecentPurchaseUtc}}"
                      }
                    ]
                    """)
                };
                response.Headers.Add("X-Recommendation-Signal-Source", "materialized_profile_v1");
                return response;
            }

            if (string.Equals(path, "/api/orders/product-insights/home-collaborative", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("[]")
                };
            }

            throw new Xunit.Sdk.XunitException($"Unexpected Ordering path: {path}");
        });
        var controller = CreateController(catalogHandler, identityHandler, orderingHandler);

        var result = await controller.GetHomeRecommendations(limit: 6);

        var ok = Assert.IsType<OkObjectResult>(result);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value, WebJson));
        var sections = json.RootElement.GetProperty("sections").EnumerateArray().ToList();
        var buyAgain = sections.Single(section => section.GetProperty("id").GetString() == "buy_again");
        Assert.Equal("Mua lại từ lịch sử", buyAgain.GetProperty("title").GetString());
        Assert.Equal("Mua nhanh lần nữa", buyAgain.GetProperty("pillLabel").GetString());
        Assert.Contains("mua gần đây", buyAgain.GetProperty("subtitle").GetString(), StringComparison.OrdinalIgnoreCase);
        var buyAgainItems = buyAgain.GetProperty("items").EnumerateArray().ToList();
        Assert.Single(buyAgainItems);
        Assert.Equal(104, buyAgainItems[0].GetProperty("productId").GetInt32());
        Assert.Contains("Bạn đã mua 3 lần", buyAgainItems[0].GetProperty("recommendationReason").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Mới mua gần đây", buyAgainItems[0].GetProperty("recommendationReason").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Sẵn để đặt lại ngay", buyAgainItems[0].GetProperty("recommendationReason").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            buyAgainItems[0].GetProperty("recommendationTags").EnumerateArray().Select(static item => item.GetString()).OfType<string>(),
            tag => string.Equals(tag, "Bạn đã mua 3 lần", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            buyAgainItems[0].GetProperty("recommendationTags").EnumerateArray().Select(static item => item.GetString()).OfType<string>(),
            tag => string.Equals(tag, "Mới mua gần đây", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            buyAgainItems[0].GetProperty("recommendationTags").EnumerateArray().Select(static item => item.GetString()).OfType<string>(),
            tag => string.Equals(tag, "Sẵn để đặt lại ngay", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task GetHomeRecommendations_BuildsBuyAgainFromPurchaseSeeds_EvenWhenPrimaryLimitIsSmaller()
    {
        var catalogHandler = new RecordingHttpMessageHandler(request =>
        {
            Assert.Equal("/api/products", request.RequestUri?.AbsolutePath);
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("""
                [
                  { "productId": 11, "productName": "Rau A", "categoryName": "Rau la", "categoryId": 1, "unitName": "kg", "price": 22000, "status": true, "availableStock": 60, "origin": "Long An", "standard": "VietGAP", "preservation": "mat", "weight": "500g" },
                  { "productId": 12, "productName": "Rau B", "categoryName": "Rau cu", "categoryId": 2, "unitName": "kg", "price": 28000, "status": true, "availableStock": 55, "origin": "Lam Dong", "standard": "VietGAP", "preservation": "mat", "weight": "500g" },
                  { "productId": 13, "productName": "Rau C", "categoryName": "Rau qua", "categoryId": 3, "unitName": "kg", "price": 26000, "status": true, "availableStock": 54, "origin": "Tien Giang", "standard": "VietGAP", "preservation": "mat", "weight": "500g" },
                  { "productId": 14, "productName": "Rau D", "categoryName": "Nam", "categoryId": 4, "unitName": "kg", "price": 31000, "status": true, "availableStock": 53, "origin": "Da Lat", "standard": "VietGAP", "preservation": "mat", "weight": "500g" },
                  { "productId": 15, "productName": "Rau E", "categoryName": "Trai cay", "categoryId": 5, "unitName": "kg", "price": 42000, "status": true, "availableStock": 52, "origin": "Ben Tre", "standard": "VietGAP", "preservation": "mat", "weight": "500g" },
                  { "productId": 16, "productName": "Rau F", "categoryName": "Dac san", "categoryId": 6, "unitName": "kg", "price": 39000, "status": true, "availableStock": 51, "origin": "Can Tho", "standard": "VietGAP", "preservation": "mat", "weight": "500g" },
                  { "productId": 105, "productName": "Rau mua lai 2", "categoryName": "Rau cu", "categoryId": 2, "unitName": "kg", "price": 21000, "status": true, "availableStock": 6, "origin": "Lam Dong", "standard": "VietGAP", "preservation": "mat", "weight": "500g" },
                  { "productId": 106, "productName": "Rau mua lai 3", "categoryName": "Rau qua", "categoryId": 3, "unitName": "kg", "price": 23000, "status": true, "availableStock": 5, "origin": "Tien Giang", "standard": "VietGAP", "preservation": "mat", "weight": "500g" },
                  { "productId": 104, "productName": "Rau mua lai", "categoryName": "Rau la", "categoryId": 1, "unitName": "kg", "price": 18000, "status": true, "availableStock": 1, "origin": "Long An", "standard": "VietGAP", "preservation": "mat", "weight": "500g" }
                ]
                """)
            };
        });
        var identityHandler = new RecordingHttpMessageHandler(_ => new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent("[]")
        });
        var orderingHandler = new RecordingHttpMessageHandler(request =>
        {
            var path = request.RequestUri?.AbsolutePath;
            if (string.Equals(path, "/api/orders/product-insights/home-profile", StringComparison.Ordinal))
            {
                var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("""
                    [
                      {
                        "productId": 104,
                        "viewCount": 0,
                        "searchClickCount": 0,
                        "recommendationClickCount": 0,
                        "purchaseCount": 2,
                        "preferenceScore": 5,
                        "lastInteractedAtUtc": "2026-03-31T10:00:00Z"
                      },
                      {
                        "productId": 105,
                        "viewCount": 0,
                        "searchClickCount": 0,
                        "recommendationClickCount": 0,
                        "purchaseCount": 1,
                        "preferenceScore": 4,
                        "lastInteractedAtUtc": "2026-03-31T09:00:00Z"
                      },
                      {
                        "productId": 106,
                        "viewCount": 0,
                        "searchClickCount": 0,
                        "recommendationClickCount": 0,
                        "purchaseCount": 1,
                        "preferenceScore": 3,
                        "lastInteractedAtUtc": "2026-03-31T08:00:00Z"
                      }
                    ]
                    """)
                };
                response.Headers.Add("X-Recommendation-Signal-Source", "materialized_profile_v1");
                return response;
            }

            if (string.Equals(path, "/api/orders/product-insights/home-collaborative", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("[]")
                };
            }

            if (string.Equals(path, "/api/orders/product-insights/stats", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("[]")
                };
            }

            throw new Xunit.Sdk.XunitException($"Unexpected Ordering path: {path}");
        });
        var controller = CreateController(catalogHandler, identityHandler, orderingHandler);

        var result = await controller.GetHomeRecommendations(limit: 2);

        var ok = Assert.IsType<OkObjectResult>(result);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value, WebJson));
        var sections = json.RootElement.GetProperty("sections").EnumerateArray().ToList();
        var forYou = sections.Single(section => section.GetProperty("id").GetString() == "for_you");
        Assert.Equal(2, forYou.GetProperty("items").GetArrayLength());

        var buyAgain = sections.Single(section => section.GetProperty("id").GetString() == "buy_again");
        var buyAgainItems = buyAgain.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(3, buyAgainItems.Count);
        Assert.Equal(new[] { 104, 105, 106 }, buyAgainItems.Select(item => item.GetProperty("productId").GetInt32()).ToArray());
        Assert.Contains("Bạn đã mua 2 lần", buyAgainItems[0].GetProperty("recommendationReason").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Sẵn để đặt lại ngay", buyAgainItems[0].GetProperty("recommendationReason").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetHomeRecommendations_BuyAgainPrefersRecentRepurchaseCandidate_WhenCountsAreClose()
    {
        var catalogHandler = new RecordingHttpMessageHandler(request =>
        {
            Assert.Equal("/api/products", request.RequestUri?.AbsolutePath);
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("""
                [
                  { "productId": 201, "productName": "Mon A", "categoryName": "Rau la", "categoryId": 1, "unitName": "kg", "price": 22000, "status": true, "availableStock": 50, "origin": "Long An", "standard": "VietGAP", "preservation": "mat", "weight": "500g" },
                  { "productId": 202, "productName": "Mon B", "categoryName": "Rau la", "categoryId": 1, "unitName": "kg", "price": 22000, "status": true, "availableStock": 50, "origin": "Long An", "standard": "VietGAP", "preservation": "mat", "weight": "500g" }
                ]
                """)
            };
        });
        var identityHandler = new RecordingHttpMessageHandler(_ => new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent("[]")
        });
        var orderingHandler = new RecordingHttpMessageHandler(request =>
        {
            var path = request.RequestUri?.AbsolutePath;
            if (string.Equals(path, "/api/orders/product-insights/home-profile", StringComparison.Ordinal))
            {
                var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent($$"""
                    [
                      {
                        "productId": 201,
                        "viewCount": 0,
                        "searchClickCount": 0,
                        "recommendationClickCount": 0,
                        "purchaseCount": 2,
                        "preferenceScore": 40,
                        "lastInteractedAtUtc": "{{DateTime.UtcNow.AddDays(-3):O}}"
                      },
                      {
                        "productId": 202,
                        "viewCount": 0,
                        "searchClickCount": 0,
                        "recommendationClickCount": 0,
                        "purchaseCount": 2,
                        "preferenceScore": 42,
                        "lastInteractedAtUtc": "{{DateTime.UtcNow.AddDays(-120):O}}"
                      }
                    ]
                    """)
                };
                response.Headers.Add("X-Recommendation-Signal-Source", "materialized_profile_v1");
                return response;
            }

            if (string.Equals(path, "/api/orders/product-insights/home-collaborative", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("[]")
                };
            }

            if (string.Equals(path, "/api/orders/product-insights/stats", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("[]")
                };
            }

            throw new Xunit.Sdk.XunitException($"Unexpected Ordering path: {path}");
        });

        var controller = CreateController(catalogHandler, identityHandler, orderingHandler);

        var result = await controller.GetHomeRecommendations(limit: 2);

        var ok = Assert.IsType<OkObjectResult>(result);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value, WebJson));
        var buyAgain = json.RootElement.GetProperty("sections").EnumerateArray()
            .Single(section => section.GetProperty("id").GetString() == "buy_again");
        var buyAgainItems = buyAgain.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(new[] { 201, 202 }, buyAgainItems.Select(item => item.GetProperty("productId").GetInt32()).ToArray());
        Assert.Contains("mua gần đây", buyAgain.GetProperty("subtitle").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetHomeRecommendations_BuyAgainIncludesShopAndFastReorderHints_WhenProductStillFitsQuickReorder()
    {
        var catalogHandler = new RecordingHttpMessageHandler(request =>
        {
            Assert.Equal("/api/products", request.RequestUri?.AbsolutePath);
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("""
                [
                  {
                    "productId": 301,
                    "productName": "Rau dat lai nhanh",
                    "categoryName": "Rau la",
                    "categoryId": 1,
                    "unitName": "kg",
                    "price": 18000,
                    "status": true,
                    "availableStock": 8,
                    "origin": "Long An, Viet Nam",
                    "sellerShopName": "Shop quen",
                    "sellerAddressSummary": "Phuong 1, Tan An, Long An",
                    "standard": "VietGAP",
                    "preservation": "mat",
                    "weight": "500g"
                  }
                ]
                """)
            };
        });
        var identityHandler = new RecordingHttpMessageHandler(_ => new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent("[]")
        });
        var orderingHandler = new RecordingHttpMessageHandler(request =>
        {
            var path = request.RequestUri?.AbsolutePath;
            if (string.Equals(path, "/api/orders/product-insights/home-profile", StringComparison.Ordinal))
            {
                var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent($$"""
                    [
                      {
                        "productId": 301,
                        "viewCount": 0,
                        "searchClickCount": 0,
                        "recommendationClickCount": 0,
                        "purchaseCount": 2,
                        "preferenceScore": 60,
                        "lastInteractedAtUtc": "{{DateTime.UtcNow.AddDays(-5):O}}"
                      }
                    ]
                    """)
                };
                response.Headers.Add("X-Recommendation-Signal-Source", "materialized_profile_v1");
                return response;
            }

            if (string.Equals(path, "/api/orders/product-insights/home-collaborative", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("[]")
                };
            }

            if (string.Equals(path, "/api/orders/product-insights/stats", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("[]")
                };
            }

            throw new Xunit.Sdk.XunitException($"Unexpected Ordering path: {path}");
        });

        var controller = CreateController(catalogHandler, identityHandler, orderingHandler);

        var result = await controller.GetHomeRecommendations(limit: 3);

        var ok = Assert.IsType<OkObjectResult>(result);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value, WebJson));
        var buyAgain = json.RootElement.GetProperty("sections").EnumerateArray()
            .Single(section => section.GetProperty("id").GetString() == "buy_again");
        var item = buyAgain.GetProperty("items").EnumerateArray().Single();
        var tags = item.GetProperty("recommendationTags").EnumerateArray().Select(static value => value.GetString()).OfType<string>().ToArray();
        Assert.Contains(tags, tag => string.Equals(tag, "Cùng vùng trồng & giao", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(tags, tag => string.Equals(tag, "Từ shop bạn từng đặt", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task GetHomeRecommendations_CreatesBuyWithHistoryAndReplenishSections_WhenOrderingProvidesSignals()
    {
        var catalogHandler = new RecordingHttpMessageHandler(request =>
        {
            Assert.Equal("/api/products", request.RequestUri?.AbsolutePath);
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("""
                [
                  {
                    "productId": 104,
                    "productName": "Rau muong huu co",
                    "categoryName": "Rau la",
                    "categoryId": 1,
                    "unitName": "kg",
                    "price": 24000,
                    "status": true,
                    "availableStock": 30,
                    "origin": "Long An, Viet Nam",
                    "standard": "VietGAP",
                    "preservation": "giu mat",
                    "weight": "500g",
                    "primarySellerId": 4
                  },
                  {
                    "productId": 410,
                    "productName": "Ca chua goi y mua kem",
                    "categoryName": "Rau cu",
                    "categoryId": 3,
                    "unitName": "kg",
                    "price": 18000,
                    "status": true,
                    "availableStock": 16,
                    "origin": "Da Lat, Viet Nam",
                    "standard": "Huu co",
                    "preservation": "giu mat",
                    "weight": "500g",
                    "primarySellerId": 47
                  },
                  {
                    "productId": 330,
                    "productName": "Dua leo sap can mua lai",
                    "categoryName": "Rau cu",
                    "categoryId": 3,
                    "unitName": "kg",
                    "price": 26000,
                    "status": true,
                    "availableStock": 22,
                    "origin": "Can Tho, Viet Nam",
                    "standard": "VietGAP",
                    "preservation": "giu mat",
                    "weight": "500g",
                    "primarySellerId": 51
                  }
                ]
                """)
            };
        });
        var identityHandler = new RecordingHttpMessageHandler(_ =>
            new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("[]")
            });
        var orderingHandler = new RecordingHttpMessageHandler(request =>
        {
            var path = request.RequestUri?.AbsolutePath;
            if (string.Equals(path, "/api/orders/product-insights/stats", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("""
                    [
                      { "productId": 104, "averageRating": 4.8, "reviewCount": 12, "soldCount": 40 },
                      { "productId": 410, "averageRating": 4.1, "reviewCount": 3, "soldCount": 4 },
                      { "productId": 330, "averageRating": 4.6, "reviewCount": 8, "soldCount": 24 }
                    ]
                    """)
                };
            }

            if (string.Equals(path, "/api/orders/product-insights/home-profile", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("""
                    [
                      {
                        "productId": 104,
                        "purchaseCount": 3,
                        "viewCount": 4,
                        "searchClickCount": 1,
                        "recommendationClickCount": 0,
                        "preferenceScore": 92,
                        "lastInteractedAtUtc": "2026-04-01T09:00:00Z"
                      }
                    ]
                    """)
                };
            }

            if (string.Equals(path, "/api/orders/product-insights/home-collaborative", StringComparison.Ordinal) ||
                string.Equals(path, "/api/orders/product-insights/user-product", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("[]")
                };
            }

            if (string.Equals(path, "/api/orders/product-insights/basket-affinity", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("""
                    [
                      {
                        "productId": 104,
                        "candidateProductId": 410,
                        "coPurchaseOrderCount": 4,
                        "basketScore": 145
                      }
                    ]
                    """)
                };
            }

            if (string.Equals(path, "/api/orders/product-insights/replenishment-profile", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("""
                    [
                      {
                        "productId": 330,
                        "purchaseCount": 2,
                        "averageRepurchaseDays": 7,
                        "expectedReorderAtUtc": "2026-04-03T09:00:00Z",
                        "replenishmentScore": 133,
                        "lastPurchasedAtUtc": "2026-03-24T09:00:00Z"
                      }
                    ]
                    """)
                };
            }

            throw new Xunit.Sdk.XunitException($"Unexpected Ordering path: {path}");
        });
        var controller = CreateController(catalogHandler, identityHandler, orderingHandler);
        controller.Request.Headers.Authorization = "Bearer test-token";

        var result = await controller.GetHomeRecommendations(limit: 1);

        var ok = Assert.IsType<OkObjectResult>(result);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value, WebJson));
        var sections = json.RootElement.GetProperty("sections").EnumerateArray().ToList();

        var buyWithHistory = sections.Single(section => section.GetProperty("id").GetString() == "buy_with_history");
        Assert.Equal("Mua kèm từ lịch sử", buyWithHistory.GetProperty("title").GetString());
        Assert.Equal("Hay mua cùng", buyWithHistory.GetProperty("pillLabel").GetString());
        var buyWithHistoryItem = buyWithHistory.GetProperty("items").EnumerateArray().Single();
        Assert.Equal(410, buyWithHistoryItem.GetProperty("productId").GetInt32());
        Assert.Contains("Hay mua cùng", buyWithHistoryItem.GetProperty("recommendationReason").GetString(), StringComparison.OrdinalIgnoreCase);

        var replenishSoon = sections.Single(section => section.GetProperty("id").GetString() == "replenish_soon");
        Assert.Equal("Đến kỳ mua lại", replenishSoon.GetProperty("title").GetString());
        Assert.Equal("Sắp cần mua thêm", replenishSoon.GetProperty("pillLabel").GetString());
        var replenishItem = replenishSoon.GetProperty("items").EnumerateArray().Single();
        Assert.Equal(330, replenishItem.GetProperty("productId").GetInt32());
        Assert.Contains("mua", replenishItem.GetProperty("recommendationReason").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetHomeRecommendations_CreatesFavoriteShopSection_FromPurchaseHistorySellerAffinity()
    {
        var catalogHandler = new RecordingHttpMessageHandler(request =>
        {
            Assert.Equal("/api/products", request.RequestUri?.AbsolutePath);
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("""
                [
                  {
                    "productId": 330,
                    "productName": "Rau muong huu co",
                    "categoryName": "Rau la",
                    "categoryId": 1,
                    "unitName": "kg",
                    "price": 25000,
                    "status": true,
                    "availableStock": 9,
                    "origin": "Long An",
                    "standard": "VietGAP",
                    "preservation": "giu mat",
                    "averageRating": 4.6,
                    "reviewCount": 8,
                    "soldCount": 21,
                    "primarySellerId": 17
                  },
                  {
                    "productId": 331,
                    "productName": "Cai ngot huu co",
                    "categoryName": "Rau la",
                    "categoryId": 1,
                    "unitName": "kg",
                    "price": 29000,
                    "status": true,
                    "availableStock": 14,
                    "origin": "Long An",
                    "standard": "VietGAP",
                    "preservation": "giu mat",
                    "averageRating": 4.8,
                    "reviewCount": 11,
                    "soldCount": 36,
                    "primarySellerId": 17
                  }
                ]
                """)
            };
        });

        var identityHandler = new RecordingHttpMessageHandler(_ => new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent("[]")
        });

        var orderingHandler = new RecordingHttpMessageHandler(request =>
        {
            var path = request.RequestUri?.AbsolutePath ?? string.Empty;

            if (string.Equals(path, "/api/orders/product-insights/home-profile", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("[]")
                };
            }

            if (string.Equals(path, "/api/orders/product-insights/home-collaborative", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("[]")
                };
            }

            if (string.Equals(path, "/api/orders/product-insights/user-product", StringComparison.Ordinal))
            {
                var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("""
                    [
                      {
                        "userId": 8,
                        "productId": 330,
                        "viewCount": 4,
                        "searchClickCount": 1,
                        "recommendationClickCount": 0,
                        "purchaseCount": 3,
                        "userProductScore": 144,
                        "lastInteractedAtUtc": "2026-04-01T09:00:00Z"
                      }
                    ]
                    """)
                };
                return response;
            }

            if (string.Equals(path, "/api/orders/product-insights/basket-affinity", StringComparison.Ordinal)
                || string.Equals(path, "/api/orders/product-insights/replenishment-profile", StringComparison.Ordinal)
                || string.Equals(path, "/api/orders/product-insights/stats", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("[]")
                };
            }

            throw new Xunit.Sdk.XunitException($"Unexpected Ordering path: {path}");
        });

        var controller = CreateController(catalogHandler, identityHandler, orderingHandler);
        controller.Request.Headers.Authorization = "Bearer test-token";

        var result = await controller.GetHomeRecommendations(limit: 4);

        var ok = Assert.IsType<OkObjectResult>(result);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value, WebJson));
        var sections = json.RootElement.GetProperty("sections").EnumerateArray().ToList();

        var favoriteShop = sections.Single(section => section.GetProperty("id").GetString() == "favorite_shop");
        Assert.Equal("Từ shop bạn hay mua", favoriteShop.GetProperty("title").GetString());
        Assert.Equal("Shop quen thuộc", favoriteShop.GetProperty("pillLabel").GetString());
        var item = favoriteShop.GetProperty("items").EnumerateArray().First();
        Assert.Equal(331, item.GetProperty("productId").GetInt32());
        var reason = item.GetProperty("recommendationReason").GetString();
        Assert.True(
            reason?.Contains("shop bạn hay mua", StringComparison.OrdinalIgnoreCase) == true
            || reason?.Contains("shop bạn vừa quay lại", StringComparison.OrdinalIgnoreCase) == true);
    }

    [Fact]
    public async Task GetHomeRecommendations_CreatesRecentShopSection_WhenSellerWasRecentlyRevisited()
    {
        var catalogHandler = new RecordingHttpMessageHandler(request =>
        {
            Assert.Equal("/api/products", request.RequestUri?.AbsolutePath);
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("""
                [
                  {
                    "productId": 430,
                    "productName": "Rau muong shop moi ghe",
                    "categoryName": "Rau la",
                    "categoryId": 1,
                    "unitName": "kg",
                    "price": 25000,
                    "status": true,
                    "availableStock": 9,
                    "origin": "Long An",
                    "standard": "VietGAP",
                    "preservation": "giu mat",
                    "averageRating": 4.6,
                    "reviewCount": 8,
                    "soldCount": 21,
                    "primarySellerId": 17,
                    "sellerShopName": "Shop quen 17"
                  },
                  {
                    "productId": 431,
                    "productName": "Cai ngot shop moi ghe",
                    "categoryName": "Rau la",
                    "categoryId": 1,
                    "unitName": "kg",
                    "price": 29000,
                    "status": true,
                    "availableStock": 14,
                    "origin": "Long An",
                    "standard": "VietGAP",
                    "preservation": "giu mat",
                    "averageRating": 4.8,
                    "reviewCount": 11,
                    "soldCount": 36,
                    "primarySellerId": 17,
                    "sellerShopName": "Shop quen 17"
                  },
                  {
                    "productId": 432,
                    "productName": "Ca chua shop khac",
                    "categoryName": "Rau cu",
                    "categoryId": 3,
                    "unitName": "kg",
                    "price": 30000,
                    "status": true,
                    "availableStock": 12,
                    "origin": "Da Lat",
                    "standard": "VietGAP",
                    "preservation": "giu mat",
                    "averageRating": 4.5,
                    "reviewCount": 6,
                    "soldCount": 18,
                    "primarySellerId": 29,
                    "sellerShopName": "Shop 29"
                  }
                ]
                """)
            };
        });

        var identityHandler = new RecordingHttpMessageHandler(_ => new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent("[]")
        });

        var orderingHandler = new RecordingHttpMessageHandler(request =>
        {
            var path = request.RequestUri?.AbsolutePath ?? string.Empty;

            if (string.Equals(path, "/api/orders/product-insights/home-profile", StringComparison.Ordinal) ||
                string.Equals(path, "/api/orders/product-insights/home-collaborative", StringComparison.Ordinal) ||
                string.Equals(path, "/api/orders/product-insights/user-product", StringComparison.Ordinal) ||
                string.Equals(path, "/api/orders/product-insights/basket-affinity", StringComparison.Ordinal) ||
                string.Equals(path, "/api/orders/product-insights/replenishment-profile", StringComparison.Ordinal) ||
                string.Equals(path, "/api/orders/product-insights/stats", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("[]")
                };
            }

            if (string.Equals(path, "/api/orders/product-insights/user-seller", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent($$"""
                    [
                      {
                        "userId": 8,
                        "sellerId": 17,
                        "viewCount": 6,
                        "searchClickCount": 2,
                        "purchaseCount": 1,
                        "userSellerScore": 188,
                        "lastInteractedAtUtc": "{{RecentInteractionUtc}}"
                      }
                    ]
                    """)
                };
            }

            throw new Xunit.Sdk.XunitException($"Unexpected Ordering path: {path}");
        });

        var controller = CreateController(catalogHandler, identityHandler, orderingHandler);
        controller.Request.Headers.Authorization = "Bearer test-token";

        var result = await controller.GetHomeRecommendations(limit: 4);

        var ok = Assert.IsType<OkObjectResult>(result);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value, WebJson));
        var sections = json.RootElement.GetProperty("sections").EnumerateArray().ToList();

        var recentShop = sections.Single(section => section.GetProperty("id").GetString() == "recent_shop");
        Assert.Equal("Từ shop bạn vừa quay lại", recentShop.GetProperty("title").GetString());
        Assert.Equal("Shop vừa ghé", recentShop.GetProperty("pillLabel").GetString());
        var item = recentShop.GetProperty("items").EnumerateArray().First();
        Assert.Equal(431, item.GetProperty("productId").GetInt32());
        Assert.Contains("shop bạn vừa quay lại", item.GetProperty("recommendationReason").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetHomeRecommendations_FavoriteShopAvoidsRepeatingRecentShopSeller_WhenAnotherFavoriteSellerExists()
    {
        var catalogHandler = new RecordingHttpMessageHandler(request =>
        {
            Assert.Equal("/api/products", request.RequestUri?.AbsolutePath);
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("""
                [
                  {
                    "productId": 430,
                    "productName": "Rau muong shop moi ghe",
                    "categoryName": "Rau la",
                    "categoryId": 1,
                    "unitName": "kg",
                    "price": 25000,
                    "status": true,
                    "availableStock": 9,
                    "origin": "Long An",
                    "standard": "VietGAP",
                    "preservation": "giu mat",
                    "averageRating": 4.6,
                    "reviewCount": 8,
                    "soldCount": 21,
                    "primarySellerId": 17,
                    "sellerShopName": "Shop quen 17"
                  },
                  {
                    "productId": 431,
                    "productName": "Cai ngot shop moi ghe",
                    "categoryName": "Rau la",
                    "categoryId": 1,
                    "unitName": "kg",
                    "price": 29000,
                    "status": true,
                    "availableStock": 14,
                    "origin": "Long An",
                    "standard": "VietGAP",
                    "preservation": "giu mat",
                    "averageRating": 4.8,
                    "reviewCount": 11,
                    "soldCount": 36,
                    "primarySellerId": 17,
                    "sellerShopName": "Shop quen 17"
                  },
                  {
                    "productId": 450,
                    "productName": "Dua leo shop quen khac",
                    "categoryName": "Rau cu",
                    "categoryId": 3,
                    "unitName": "kg",
                    "price": 32000,
                    "status": true,
                    "availableStock": 16,
                    "origin": "Da Lat",
                    "standard": "VietGAP",
                    "preservation": "giu mat",
                    "averageRating": 4.7,
                    "reviewCount": 9,
                    "soldCount": 24,
                    "primarySellerId": 29,
                    "sellerShopName": "Shop quen 29"
                  }
                ]
                """)
            };
        });

        var identityHandler = new RecordingHttpMessageHandler(_ => new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent("[]")
        });

        var orderingHandler = new RecordingHttpMessageHandler(request =>
        {
            var path = request.RequestUri?.AbsolutePath ?? string.Empty;

            if (string.Equals(path, "/api/orders/product-insights/home-profile", StringComparison.Ordinal) ||
                string.Equals(path, "/api/orders/product-insights/home-collaborative", StringComparison.Ordinal) ||
                string.Equals(path, "/api/orders/product-insights/user-product", StringComparison.Ordinal) ||
                string.Equals(path, "/api/orders/product-insights/basket-affinity", StringComparison.Ordinal) ||
                string.Equals(path, "/api/orders/product-insights/replenishment-profile", StringComparison.Ordinal) ||
                string.Equals(path, "/api/orders/product-insights/stats", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("[]")
                };
            }

            if (string.Equals(path, "/api/orders/product-insights/user-seller", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent($$"""
                    [
                      {
                        "userId": 8,
                        "sellerId": 17,
                        "viewCount": 6,
                        "searchClickCount": 2,
                        "purchaseCount": 1,
                        "userSellerScore": 188,
                        "lastInteractedAtUtc": "{{RecentInteractionUtc}}"
                      },
                      {
                        "userId": 8,
                        "sellerId": 29,
                        "viewCount": 4,
                        "searchClickCount": 1,
                        "purchaseCount": 2,
                        "userSellerScore": 176,
                        "lastInteractedAtUtc": "2026-03-01T09:00:00Z"
                      }
                    ]
                    """)
                };
            }

            throw new Xunit.Sdk.XunitException($"Unexpected Ordering path: {path}");
        });

        var controller = CreateController(catalogHandler, identityHandler, orderingHandler);
        controller.Request.Headers.Authorization = "Bearer test-token";

        var result = await controller.GetHomeRecommendations(limit: 4);

        var ok = Assert.IsType<OkObjectResult>(result);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value, WebJson));
        var sections = json.RootElement.GetProperty("sections").EnumerateArray().ToList();

        var recentShop = sections.Single(section => section.GetProperty("id").GetString() == "recent_shop");
        var favoriteShop = sections.Single(section => section.GetProperty("id").GetString() == "favorite_shop");

        Assert.All(
            recentShop.GetProperty("items").EnumerateArray(),
            item => Assert.Equal(17, item.GetProperty("primarySellerId").GetInt32()));
        Assert.All(
            favoriteShop.GetProperty("items").EnumerateArray(),
            item => Assert.Equal(29, item.GetProperty("primarySellerId").GetInt32()));
    }

    [Fact]
    public async Task GetHomeRecommendations_FavoriteShopDiversifiesAcrossSellers_WhenEnoughCandidatesExist()
    {
        var catalogHandler = new RecordingHttpMessageHandler(request =>
        {
            Assert.Equal("/api/products", request.RequestUri?.AbsolutePath);
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("""
                [
                  {
                    "productId": 440,
                    "productName": "Rau muong shop 17",
                    "categoryName": "Rau la",
                    "categoryId": 1,
                    "unitName": "kg",
                    "price": 22000,
                    "status": true,
                    "availableStock": 12,
                    "origin": "Long An",
                    "standard": "VietGAP",
                    "preservation": "giu mat",
                    "averageRating": 4.6,
                    "reviewCount": 7,
                    "soldCount": 18,
                    "primarySellerId": 17,
                    "sellerShopName": "Shop 17"
                  },
                  {
                    "productId": 441,
                    "productName": "Cai ngot shop 17",
                    "categoryName": "Rau la",
                    "categoryId": 1,
                    "unitName": "kg",
                    "price": 21000,
                    "status": true,
                    "availableStock": 11,
                    "origin": "Long An",
                    "standard": "VietGAP",
                    "preservation": "giu mat",
                    "averageRating": 4.5,
                    "reviewCount": 6,
                    "soldCount": 15,
                    "primarySellerId": 17,
                    "sellerShopName": "Shop 17"
                  },
                  {
                    "productId": 442,
                    "productName": "Cai xanh shop 17",
                    "categoryName": "Rau la",
                    "categoryId": 1,
                    "unitName": "kg",
                    "price": 23000,
                    "status": true,
                    "availableStock": 9,
                    "origin": "Long An",
                    "standard": "VietGAP",
                    "preservation": "giu mat",
                    "averageRating": 4.7,
                    "reviewCount": 8,
                    "soldCount": 20,
                    "primarySellerId": 17,
                    "sellerShopName": "Shop 17"
                  },
                  {
                    "productId": 450,
                    "productName": "Dua leo shop 29",
                    "categoryName": "Rau cu",
                    "categoryId": 3,
                    "unitName": "kg",
                    "price": 32000,
                    "status": true,
                    "availableStock": 16,
                    "origin": "Da Lat",
                    "standard": "VietGAP",
                    "preservation": "giu mat",
                    "averageRating": 4.7,
                    "reviewCount": 9,
                    "soldCount": 24,
                    "primarySellerId": 29,
                    "sellerShopName": "Shop 29"
                  }
                ]
                """)
            };
        });

        var identityHandler = new RecordingHttpMessageHandler(_ => new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent("[]")
        });

        var orderingHandler = new RecordingHttpMessageHandler(request =>
        {
            var path = request.RequestUri?.AbsolutePath ?? string.Empty;

            if (string.Equals(path, "/api/orders/product-insights/home-profile", StringComparison.Ordinal) ||
                string.Equals(path, "/api/orders/product-insights/home-collaborative", StringComparison.Ordinal) ||
                string.Equals(path, "/api/orders/product-insights/user-product", StringComparison.Ordinal) ||
                string.Equals(path, "/api/orders/product-insights/basket-affinity", StringComparison.Ordinal) ||
                string.Equals(path, "/api/orders/product-insights/replenishment-profile", StringComparison.Ordinal) ||
                string.Equals(path, "/api/orders/product-insights/stats", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("[]")
                };
            }

            if (string.Equals(path, "/api/orders/product-insights/user-seller", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent($$"""
                    [
                      {
                        "userId": 8,
                        "sellerId": 17,
                        "viewCount": 6,
                        "searchClickCount": 2,
                        "purchaseCount": 2,
                        "userSellerScore": 188,
                        "lastInteractedAtUtc": "2026-03-01T09:00:00Z"
                      },
                      {
                        "userId": 8,
                        "sellerId": 29,
                        "viewCount": 4,
                        "searchClickCount": 1,
                        "purchaseCount": 1,
                        "userSellerScore": 172,
                        "lastInteractedAtUtc": "2026-02-20T09:00:00Z"
                      }
                    ]
                    """)
                };
            }

            throw new Xunit.Sdk.XunitException($"Unexpected Ordering path: {path}");
        });

        var controller = CreateController(catalogHandler, identityHandler, orderingHandler);
        controller.Request.Headers.Authorization = "Bearer test-token";

        var result = await controller.GetHomeRecommendations(limit: 4);

        var ok = Assert.IsType<OkObjectResult>(result);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value, WebJson));
        var favoriteShop = json.RootElement.GetProperty("sections").EnumerateArray()
            .Single(section => section.GetProperty("id").GetString() == "favorite_shop");

        var sellerIds = favoriteShop.GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("primarySellerId").GetInt32())
            .ToList();

        Assert.Contains(17, sellerIds);
        Assert.Contains(29, sellerIds);
    }

    [Fact]
    public async Task GetHomeRecommendations_UsesUserSellerScores_ToCreateFavoriteShopSection_WhenPurchaseHistoryIsSparse()
    {
        var catalogHandler = new RecordingHttpMessageHandler(request =>
        {
            Assert.Equal("/api/products", request.RequestUri?.AbsolutePath);
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("""
                [
                  {
                    "productId": 410,
                    "productName": "Rau den do",
                    "categoryName": "Rau la",
                    "categoryId": 1,
                    "unitName": "kg",
                    "price": 22000,
                    "status": true,
                    "availableStock": 10,
                    "origin": "Long An",
                    "standard": "VietGAP",
                    "preservation": "giu mat",
                    "averageRating": 4.5,
                    "reviewCount": 6,
                    "soldCount": 10,
                    "primarySellerId": 17,
                    "sellerShopName": "Shop quen 17"
                  },
                  {
                    "productId": 411,
                    "productName": "Cai xanh shop quen",
                    "categoryName": "Rau la",
                    "categoryId": 1,
                    "unitName": "kg",
                    "price": 26000,
                    "status": true,
                    "availableStock": 18,
                    "origin": "Da Lat",
                    "standard": "VietGAP",
                    "preservation": "giu mat",
                    "averageRating": 4.8,
                    "reviewCount": 10,
                    "soldCount": 30,
                    "primarySellerId": 17,
                    "sellerShopName": "Shop quen 17"
                  },
                  {
                    "productId": 420,
                    "productName": "Ca chua doi thu",
                    "categoryName": "Rau cu",
                    "categoryId": 3,
                    "unitName": "kg",
                    "price": 29000,
                    "status": true,
                    "availableStock": 15,
                    "origin": "Da Lat",
                    "standard": "VietGAP",
                    "preservation": "giu mat",
                    "averageRating": 4.9,
                    "reviewCount": 12,
                    "soldCount": 38,
                    "primarySellerId": 29,
                    "sellerShopName": "Shop 29"
                  }
                ]
                """)
            };
        });

        var identityHandler = new RecordingHttpMessageHandler(_ => new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent("[]")
        });

        var orderingHandler = new RecordingHttpMessageHandler(request =>
        {
            var path = request.RequestUri?.AbsolutePath ?? string.Empty;

            if (string.Equals(path, "/api/orders/product-insights/home-profile", StringComparison.Ordinal) ||
                string.Equals(path, "/api/orders/product-insights/home-collaborative", StringComparison.Ordinal) ||
                string.Equals(path, "/api/orders/product-insights/user-product", StringComparison.Ordinal) ||
                string.Equals(path, "/api/orders/product-insights/basket-affinity", StringComparison.Ordinal) ||
                string.Equals(path, "/api/orders/product-insights/replenishment-profile", StringComparison.Ordinal) ||
                string.Equals(path, "/api/orders/product-insights/stats", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("[]")
                };
            }

            if (string.Equals(path, "/api/orders/product-insights/user-seller", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent($$"""
                    [
                      {
                        "userId": 8,
                        "sellerId": 17,
                        "viewCount": 6,
                        "searchClickCount": 2,
                        "purchaseCount": 0,
                        "userSellerScore": 188,
                        "lastInteractedAtUtc": "2026-04-02T09:00:00Z"
                      }
                    ]
                    """)
                };
            }

            throw new Xunit.Sdk.XunitException($"Unexpected Ordering path: {path}");
        });

        var controller = CreateController(catalogHandler, identityHandler, orderingHandler);
        controller.Request.Headers.Authorization = "Bearer test-token";

        var result = await controller.GetHomeRecommendations(limit: 4);

        var ok = Assert.IsType<OkObjectResult>(result);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value, WebJson));
        var sections = json.RootElement.GetProperty("sections").EnumerateArray().ToList();
        var favoriteShop = sections.Single(section => section.GetProperty("id").GetString() == "favorite_shop");
        var item = favoriteShop.GetProperty("items").EnumerateArray().First();

        Assert.Equal(411, item.GetProperty("productId").GetInt32());
        var reason = item.GetProperty("recommendationReason").GetString();
        Assert.True(
            reason?.Contains("shop bạn hay mua", StringComparison.OrdinalIgnoreCase) == true
            || reason?.Contains("shop bạn vừa quay lại", StringComparison.OrdinalIgnoreCase) == true);
    }

    [Fact]
    public async Task GetHomeRecommendations_FavoriteShopPrefersRecentlyReturnedSeller_WhenScoresAreClose()
    {
        var catalogHandler = new RecordingHttpMessageHandler(request =>
        {
            Assert.Equal("/api/products", request.RequestUri?.AbsolutePath);
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("""
                [
                  {
                    "productId": 430,
                    "productName": "Rau den shop moi quay lai",
                    "categoryName": "Rau la",
                    "categoryId": 1,
                    "unitName": "kg",
                    "price": 22000,
                    "status": true,
                    "availableStock": 12,
                    "origin": "Long An",
                    "standard": "VietGAP",
                    "preservation": "giu mat",
                    "averageRating": 4.6,
                    "reviewCount": 7,
                    "soldCount": 18,
                    "primarySellerId": 17,
                    "sellerShopName": "Shop 17"
                  },
                  {
                    "productId": 431,
                    "productName": "Rau den shop cu",
                    "categoryName": "Rau la",
                    "categoryId": 1,
                    "unitName": "kg",
                    "price": 21000,
                    "status": true,
                    "availableStock": 12,
                    "origin": "Long An",
                    "standard": "VietGAP",
                    "preservation": "giu mat",
                    "averageRating": 4.6,
                    "reviewCount": 7,
                    "soldCount": 18,
                    "primarySellerId": 18,
                    "sellerShopName": "Shop 18"
                  }
                ]
                """)
            };
        });

        var identityHandler = new RecordingHttpMessageHandler(_ => new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent("[]")
        });

        var orderingHandler = new RecordingHttpMessageHandler(request =>
        {
            var path = request.RequestUri?.AbsolutePath ?? string.Empty;

            if (string.Equals(path, "/api/orders/product-insights/home-profile", StringComparison.Ordinal) ||
                string.Equals(path, "/api/orders/product-insights/home-collaborative", StringComparison.Ordinal) ||
                string.Equals(path, "/api/orders/product-insights/user-product", StringComparison.Ordinal) ||
                string.Equals(path, "/api/orders/product-insights/basket-affinity", StringComparison.Ordinal) ||
                string.Equals(path, "/api/orders/product-insights/replenishment-profile", StringComparison.Ordinal) ||
                string.Equals(path, "/api/orders/product-insights/stats", StringComparison.Ordinal) ||
                string.Equals(path, "/api/orders/product-insights/user-category", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("[]")
                };
            }

            if (string.Equals(path, "/api/orders/product-insights/user-seller", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent($$"""
                    [
                      {
                        "userId": 8,
                        "sellerId": 17,
                        "viewCount": 4,
                        "searchClickCount": 1,
                        "purchaseCount": 1,
                        "userSellerScore": 165,
                        "lastInteractedAtUtc": "{{RecentInteractionUtc}}"
                      },
                      {
                        "userId": 8,
                        "sellerId": 18,
                        "viewCount": 5,
                        "searchClickCount": 2,
                        "purchaseCount": 2,
                        "userSellerScore": 172,
                        "lastInteractedAtUtc": "2025-12-20T09:00:00Z"
                      }
                    ]
                    """)
                };
            }

            throw new Xunit.Sdk.XunitException($"Unexpected Ordering path: {path}");
        });

        var controller = CreateController(catalogHandler, identityHandler, orderingHandler);
        controller.Request.Headers.Authorization = "Bearer test-token";

        var result = await controller.GetHomeRecommendations(limit: 4);

        var ok = Assert.IsType<OkObjectResult>(result);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value, WebJson));
        var recentShop = json.RootElement.GetProperty("sections").EnumerateArray()
            .Single(section => section.GetProperty("id").GetString() == "recent_shop");
        Assert.Equal("Từ shop bạn vừa quay lại", recentShop.GetProperty("title").GetString());
        var item = recentShop.GetProperty("items").EnumerateArray().First();
        Assert.Contains(item.GetProperty("productId").GetInt32(), new[] { 430, 431 });
        Assert.Contains("vừa quay lại", item.GetProperty("recommendationReason").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetHomeRecommendations_UsesUserProductScores_ToLiftForYouItem_WhenAvailable()
    {
        var catalogHandler = new RecordingHttpMessageHandler(request =>
        {
            Assert.Equal("/api/products", request.RequestUri?.AbsolutePath);
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("""
                [
                  {
                    "productId": 104,
                    "productName": "Rau muong huu co",
                    "categoryName": "Rau la",
                    "categoryId": 1,
                    "unitName": "kg",
                    "price": 24000,
                    "status": true,
                    "availableStock": 30,
                    "origin": "Long An, Viet Nam",
                    "standard": "VietGAP",
                    "preservation": "giu mat",
                    "weight": "500g",
                    "primarySellerId": 4
                  },
                  {
                    "productId": 220,
                    "productName": "Ca chua duoc uu tien",
                    "categoryName": "Rau cu",
                    "categoryId": 3,
                    "unitName": "kg",
                    "price": 32000,
                    "status": true,
                    "availableStock": 28,
                    "origin": "Da Lat, Viet Nam",
                    "standard": "VietGAP",
                    "preservation": "giu mat",
                    "weight": "500g",
                    "primarySellerId": 47
                  }
                ]
                """)
            };
        });
        var identityHandler = new RecordingHttpMessageHandler(_ =>
            new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("[]")
            });
        var orderingHandler = new RecordingHttpMessageHandler(request =>
        {
            var path = request.RequestUri?.AbsolutePath;
            if (string.Equals(path, "/api/orders/product-insights/stats", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent($$"""
                    [
                      { "productId": 104, "averageRating": 4.9, "reviewCount": 15, "soldCount": 66 },
                      { "productId": 220, "averageRating": 4.4, "reviewCount": 8, "soldCount": 18 }
                    ]
                    """)
                };
            }

            if (string.Equals(path, "/api/orders/product-insights/home-profile", StringComparison.Ordinal) ||
                string.Equals(path, "/api/orders/product-insights/home-collaborative", StringComparison.Ordinal) ||
                string.Equals(path, "/api/orders/product-insights/basket-affinity", StringComparison.Ordinal) ||
                string.Equals(path, "/api/orders/product-insights/replenishment-profile", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("[]")
                };
            }

            if (string.Equals(path, "/api/orders/product-insights/user-product", StringComparison.Ordinal))
            {
                var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("""
                    [
                      {
                        "productId": 220,
                        "purchaseCount": 2,
                        "viewCount": 5,
                        "searchClickCount": 2,
                        "recommendationClickCount": 1,
                        "userProductScore": 520,
                        "lastInteractedAtUtc": "2026-04-01T10:00:00Z",
                        "computedAtUtc": "2026-04-28T04:43:20Z"
                      }
                    ]
                    """)
                };
                response.Headers.Add("X-Recommendation-Signal-Source", "mlnet_user_product_v1");
                return response;
            }

            throw new Xunit.Sdk.XunitException($"Unexpected Ordering path: {path}");
        });
        var controller = CreateController(catalogHandler, identityHandler, orderingHandler);
        controller.Request.Headers.Authorization = "Bearer test-token";

        var result = await controller.GetHomeRecommendations(limit: 2);

        var ok = Assert.IsType<OkObjectResult>(result);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value, WebJson));
        Assert.Equal("hybrid_home_v1", json.RootElement.GetProperty("algorithm").GetString());
        Assert.Equal("mlnet_user_product_v1", json.RootElement.GetProperty("signalSource").GetString());
        Assert.Equal("mlnet_user_product_v1", json.RootElement.GetProperty("mlSignalSource").GetString());
        Assert.Equal(1, json.RootElement.GetProperty("mlScoreHitCount").GetInt32());
        Assert.Equal(JsonValueKind.String, json.RootElement.GetProperty("mlModelComputedAtUtc").ValueKind);
        Assert.Equal("mlnet_user_product_v1", json.RootElement.GetProperty("signalBreakdown").GetProperty("ml").GetString());
        var topRankedItem = json.RootElement.GetProperty("items")[0];
        Assert.Equal(220, topRankedItem.GetProperty("productId").GetInt32());
        Assert.Contains("Bạn", topRankedItem.GetProperty("recommendationReason").GetString(), StringComparison.OrdinalIgnoreCase);
        var buyAgain = json.RootElement.GetProperty("sections").EnumerateArray()
            .Single(section => section.GetProperty("id").GetString() == "buy_again");
        var rebuyTopItem = buyAgain.GetProperty("items")[0];
        Assert.Equal(220, rebuyTopItem.GetProperty("productId").GetInt32());
    }

    [Fact]
    public async Task GetHomeRecommendations_ForYouUsesLongTermCategoryAndOriginPreference_WhenDirectItemScoreIsMissing()
    {
        var catalogHandler = new RecordingHttpMessageHandler(request =>
        {
            Assert.Equal("/api/products", request.RequestUri?.AbsolutePath);
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("""
                [
                  {
                    "productId": 104,
                    "productName": "Rau muong huu co",
                    "categoryName": "Rau la",
                    "categoryId": 1,
                    "unitName": "kg",
                    "price": 24000,
                    "status": true,
                    "availableStock": 30,
                    "origin": "Long An, Viet Nam",
                    "standard": "VietGAP",
                    "preservation": "giu mat",
                    "weight": "500g",
                    "primarySellerId": 4
                  },
                  {
                    "productId": 105,
                    "productName": "Rau day uu tien",
                    "categoryName": "Rau la",
                    "categoryId": 1,
                    "unitName": "kg",
                    "price": 26000,
                    "status": true,
                    "availableStock": 26,
                    "origin": "Long An, Viet Nam",
                    "standard": "VietGAP",
                    "preservation": "giu mat",
                    "weight": "500g",
                    "primarySellerId": 2
                  },
                  {
                    "productId": 220,
                    "productName": "Ca chua doi thu",
                    "categoryName": "Rau cu",
                    "categoryId": 3,
                    "unitName": "kg",
                    "price": 32000,
                    "status": true,
                    "availableStock": 28,
                    "origin": "Da Lat, Viet Nam",
                    "standard": "VietGAP",
                    "preservation": "giu mat",
                    "weight": "500g",
                    "primarySellerId": 47
                  }
                ]
                """)
            };
        });

        var identityHandler = new RecordingHttpMessageHandler(_ =>
            new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("[]")
            });

        var orderingHandler = new RecordingHttpMessageHandler(request =>
        {
            var path = request.RequestUri?.AbsolutePath;
            if (string.Equals(path, "/api/orders/product-insights/stats", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("""
                    [
                      { "productId": 104, "averageRating": 4.8, "reviewCount": 10, "soldCount": 32 },
                      { "productId": 105, "averageRating": 4.7, "reviewCount": 9, "soldCount": 20 },
                      { "productId": 220, "averageRating": 4.9, "reviewCount": 12, "soldCount": 36 }
                    ]
                    """)
                };
            }

            if (string.Equals(path, "/api/orders/product-insights/home-profile", StringComparison.Ordinal) ||
                string.Equals(path, "/api/orders/product-insights/home-collaborative", StringComparison.Ordinal) ||
                string.Equals(path, "/api/orders/product-insights/basket-affinity", StringComparison.Ordinal) ||
                string.Equals(path, "/api/orders/product-insights/replenishment-profile", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("[]")
                };
            }

            if (string.Equals(path, "/api/orders/product-insights/user-product", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("""
                    [
                      {
                        "productId": 104,
                        "purchaseCount": 3,
                        "viewCount": 4,
                        "searchClickCount": 1,
                        "recommendationClickCount": 0,
                        "userProductScore": 420,
                        "lastInteractedAtUtc": "2026-04-01T10:00:00Z"
                      }
                    ]
                    """)
                };
            }

            throw new Xunit.Sdk.XunitException($"Unexpected Ordering path: {path}");
        });

        var controller = CreateController(catalogHandler, identityHandler, orderingHandler);
        controller.Request.Headers.Authorization = "Bearer test-token";

        var result = await controller.GetHomeRecommendations(limit: 3);

        var ok = Assert.IsType<OkObjectResult>(result);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value, WebJson));
        var forYou = json.RootElement.GetProperty("sections").EnumerateArray()
            .Single(section => section.GetProperty("id").GetString() == "for_you");
        var candidate = forYou.GetProperty("items").EnumerateArray()
            .Single(item => item.GetProperty("productId").GetInt32() == 105);
        var reason = candidate.GetProperty("recommendationReason").GetString();

        Assert.Contains("Hợp nhóm", reason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Long An", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetHomeRecommendations_ForYouUsesMaterializedUserCategoryScore_WhenDirectItemScoreIsMissing()
    {
        var catalogHandler = new RecordingHttpMessageHandler(request =>
        {
            Assert.Equal("/api/products", request.RequestUri?.AbsolutePath);
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("""
                [
                  {
                    "productId": 105,
                    "productName": "Rau day uu tien",
                    "categoryName": "Rau la",
                    "categoryId": 1,
                    "unitName": "kg",
                    "price": 26000,
                    "status": true,
                    "availableStock": 26,
                    "origin": "Long An, Viet Nam",
                    "standard": "VietGAP",
                    "preservation": "giu mat",
                    "weight": "500g",
                    "primarySellerId": 2
                  },
                  {
                    "productId": 220,
                    "productName": "Ca chua doi thu",
                    "categoryName": "Rau cu",
                    "categoryId": 3,
                    "unitName": "kg",
                    "price": 32000,
                    "status": true,
                    "availableStock": 28,
                    "origin": "Da Lat, Viet Nam",
                    "standard": "VietGAP",
                    "preservation": "giu mat",
                    "weight": "500g",
                    "primarySellerId": 47
                  }
                ]
                """)
            };
        });

        var identityHandler = new RecordingHttpMessageHandler(_ =>
            new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("[]")
            });

        var orderingHandler = new RecordingHttpMessageHandler(request =>
        {
            var path = request.RequestUri?.AbsolutePath;
            if (string.Equals(path, "/api/orders/product-insights/stats", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("""
                    [
                      { "productId": 105, "averageRating": 4.7, "reviewCount": 9, "soldCount": 20 },
                      { "productId": 220, "averageRating": 4.9, "reviewCount": 12, "soldCount": 36 }
                    ]
                    """)
                };
            }

            if (string.Equals(path, "/api/orders/product-insights/home-profile", StringComparison.Ordinal) ||
                string.Equals(path, "/api/orders/product-insights/home-collaborative", StringComparison.Ordinal) ||
                string.Equals(path, "/api/orders/product-insights/basket-affinity", StringComparison.Ordinal) ||
                string.Equals(path, "/api/orders/product-insights/replenishment-profile", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("[]")
                };
            }

            if (string.Equals(path, "/api/orders/product-insights/user-product", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("[]")
                };
            }

            if (string.Equals(path, "/api/orders/product-insights/user-category", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("""
                    [
                      {
                        "categoryId": 1,
                        "categoryName": "Rau la",
                        "purchaseCount": 4,
                        "viewCount": 6,
                        "searchClickCount": 3,
                        "recommendationClickCount": 1,
                        "userCategoryScore": 480,
                        "lastInteractedAtUtc": "2026-04-02T08:00:00Z"
                      }
                    ]
                    """)
                };
            }

            if (string.Equals(path, "/api/orders/product-insights/user-seller", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("[]")
                };
            }

            throw new Xunit.Sdk.XunitException($"Unexpected Ordering path: {path}");
        });

        var controller = CreateController(catalogHandler, identityHandler, orderingHandler);
        controller.Request.Headers.Authorization = "Bearer test-token";

        var result = await controller.GetHomeRecommendations(limit: 2);

        var ok = Assert.IsType<OkObjectResult>(result);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value, WebJson));
        var forYou = json.RootElement.GetProperty("sections").EnumerateArray()
            .Single(section => section.GetProperty("id").GetString() == "for_you");
        var candidate = forYou.GetProperty("items").EnumerateArray()
            .Single(item => item.GetProperty("productId").GetInt32() == 105);
        var reason = candidate.GetProperty("recommendationReason").GetString();

        Assert.Contains("Hợp nhóm Rau la", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetHomeRecommendations_ForYouUsesRecentSellerReason_WhenSellerWasRecentlyRevisited()
    {
        var catalogHandler = new RecordingHttpMessageHandler(request =>
        {
            Assert.Equal("/api/products", request.RequestUri?.AbsolutePath);
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("""
                [
                  {
                    "productId": 520,
                    "productName": "Rau den tu shop quen",
                    "categoryName": "Rau la",
                    "categoryId": 1,
                    "unitName": "kg",
                    "price": 23000,
                    "status": true,
                    "availableStock": 18,
                    "origin": "Long An",
                    "standard": "VietGAP",
                    "preservation": "giu mat",
                    "averageRating": 4.7,
                    "reviewCount": 9,
                    "soldCount": 24,
                    "primarySellerId": 17
                  },
                  {
                    "productId": 521,
                    "productName": "Rau den doi thu",
                    "categoryName": "Rau la",
                    "categoryId": 1,
                    "unitName": "kg",
                    "price": 23000,
                    "status": true,
                    "availableStock": 18,
                    "origin": "Long An",
                    "standard": "VietGAP",
                    "preservation": "giu mat",
                    "averageRating": 4.7,
                    "reviewCount": 9,
                    "soldCount": 24,
                    "primarySellerId": 18
                  }
                ]
                """)
            };
        });

        var identityHandler = new RecordingHttpMessageHandler(_ => new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent("[]")
        });

        var orderingHandler = new RecordingHttpMessageHandler(request =>
        {
            var path = request.RequestUri?.AbsolutePath ?? string.Empty;

            if (string.Equals(path, "/api/orders/product-insights/home-profile", StringComparison.Ordinal) ||
                string.Equals(path, "/api/orders/product-insights/home-collaborative", StringComparison.Ordinal) ||
                string.Equals(path, "/api/orders/product-insights/user-product", StringComparison.Ordinal) ||
                string.Equals(path, "/api/orders/product-insights/basket-affinity", StringComparison.Ordinal) ||
                string.Equals(path, "/api/orders/product-insights/replenishment-profile", StringComparison.Ordinal) ||
                string.Equals(path, "/api/orders/product-insights/stats", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("[]")
                };
            }

            if (string.Equals(path, "/api/orders/product-insights/user-category", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("""
                    [
                      {
                        "categoryId": 1,
                        "categoryName": "Rau la",
                        "purchaseCount": 1,
                        "viewCount": 2,
                        "searchClickCount": 1,
                        "recommendationClickCount": 0,
                        "userCategoryScore": 120,
                        "lastInteractedAtUtc": "2026-04-02T08:00:00Z"
                      }
                    ]
                    """)
                };
            }

            if (string.Equals(path, "/api/orders/product-insights/user-seller", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent($$"""
                    [
                      {
                        "userId": 8,
                        "sellerId": 17,
                        "viewCount": 4,
                        "searchClickCount": 1,
                        "purchaseCount": 1,
                        "userSellerScore": 180,
                        "lastInteractedAtUtc": "{{RecentInteractionUtc}}"
                      }
                    ]
                    """)
                };
            }

            throw new Xunit.Sdk.XunitException($"Unexpected Ordering path: {path}");
        });

        var controller = CreateController(catalogHandler, identityHandler, orderingHandler);
        controller.Request.Headers.Authorization = "Bearer test-token";

        var result = await controller.GetHomeRecommendations(limit: 4);

        var ok = Assert.IsType<OkObjectResult>(result);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value, WebJson));
        var forYou = json.RootElement.GetProperty("sections").EnumerateArray()
            .Single(section => section.GetProperty("id").GetString() == "for_you");
        var item = forYou.GetProperty("items").EnumerateArray().First();
        Assert.Equal(520, item.GetProperty("productId").GetInt32());
        Assert.Contains("Từ shop bạn vừa quay lại", item.GetProperty("recommendationReason").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetHomeRecommendations_ForYouKeepsLongTermDiscoveryItem_WhenDirectScoresWouldOtherwiseFillCompactLimit()
    {
        var catalogHandler = new RecordingHttpMessageHandler(request =>
        {
            Assert.Equal("/api/products", request.RequestUri?.AbsolutePath);
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("""
                [
                  {
                    "productId": 104,
                    "productName": "Rau muong huu co",
                    "categoryName": "Rau la",
                    "categoryId": 1,
                    "unitName": "kg",
                    "price": 24000,
                    "status": true,
                    "availableStock": 30,
                    "origin": "Long An, Viet Nam",
                    "standard": "VietGAP",
                    "preservation": "giu mat",
                    "weight": "500g",
                    "primarySellerId": 4
                  },
                  {
                    "productId": 106,
                    "productName": "Cai xanh da mua",
                    "categoryName": "Rau la",
                    "categoryId": 1,
                    "unitName": "kg",
                    "price": 25000,
                    "status": true,
                    "availableStock": 24,
                    "origin": "Long An, Viet Nam",
                    "standard": "VietGAP",
                    "preservation": "giu mat",
                    "weight": "500g",
                    "primarySellerId": 47
                  },
                  {
                    "productId": 220,
                    "productName": "Ca chua da mua",
                    "categoryName": "Rau cu",
                    "categoryId": 3,
                    "unitName": "kg",
                    "price": 32000,
                    "status": true,
                    "availableStock": 28,
                    "origin": "Da Lat, Viet Nam",
                    "standard": "VietGAP",
                    "preservation": "giu mat",
                    "weight": "500g",
                    "primarySellerId": 51
                  },
                  {
                    "productId": 221,
                    "productName": "Bi do da mua",
                    "categoryName": "Rau cu",
                    "categoryId": 3,
                    "unitName": "kg",
                    "price": 30000,
                    "status": true,
                    "availableStock": 22,
                    "origin": "Can Tho, Viet Nam",
                    "standard": "VietGAP",
                    "preservation": "giu mat",
                    "weight": "500g",
                    "primarySellerId": 2
                  },
                  {
                    "productId": 105,
                    "productName": "Rau day kham pha",
                    "categoryName": "Rau la",
                    "categoryId": 1,
                    "unitName": "kg",
                    "price": 26000,
                    "status": true,
                    "availableStock": 26,
                    "origin": "Long An, Viet Nam",
                    "standard": "VietGAP",
                    "preservation": "giu mat",
                    "weight": "500g",
                    "primarySellerId": 8
                  }
                ]
                """)
            };
        });

        var identityHandler = new RecordingHttpMessageHandler(_ =>
            new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("[]")
            });

        var orderingHandler = new RecordingHttpMessageHandler(request =>
        {
            var path = request.RequestUri?.AbsolutePath;
            if (string.Equals(path, "/api/orders/product-insights/stats", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent($$"""
                    [
                      { "productId": 104, "averageRating": 4.8, "reviewCount": 10, "soldCount": 32 },
                      { "productId": 106, "averageRating": 4.7, "reviewCount": 9, "soldCount": 28 },
                      { "productId": 220, "averageRating": 4.9, "reviewCount": 12, "soldCount": 36 },
                      { "productId": 221, "averageRating": 4.6, "reviewCount": 8, "soldCount": 26 },
                      { "productId": 105, "averageRating": 4.5, "reviewCount": 7, "soldCount": 18 }
                    ]
                    """)
                };
            }

            if (string.Equals(path, "/api/orders/product-insights/home-profile", StringComparison.Ordinal) ||
                string.Equals(path, "/api/orders/product-insights/home-collaborative", StringComparison.Ordinal) ||
                string.Equals(path, "/api/orders/product-insights/basket-affinity", StringComparison.Ordinal) ||
                string.Equals(path, "/api/orders/product-insights/replenishment-profile", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("[]")
                };
            }

            if (string.Equals(path, "/api/orders/product-insights/user-product", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("""
                    [
                      {
                        "productId": 104,
                        "purchaseCount": 3,
                        "viewCount": 4,
                        "searchClickCount": 1,
                        "recommendationClickCount": 0,
                        "userProductScore": 420,
                        "lastInteractedAtUtc": "2026-04-01T10:00:00Z"
                      },
                      {
                        "productId": 220,
                        "purchaseCount": 2,
                        "viewCount": 3,
                        "searchClickCount": 1,
                        "recommendationClickCount": 0,
                        "userProductScore": 360,
                        "lastInteractedAtUtc": "2026-03-31T10:00:00Z"
                      },
                      {
                        "productId": 221,
                        "purchaseCount": 2,
                        "viewCount": 2,
                        "searchClickCount": 1,
                        "recommendationClickCount": 0,
                        "userProductScore": 330,
                        "lastInteractedAtUtc": "2026-03-30T10:00:00Z"
                      },
                      {
                        "productId": 106,
                        "purchaseCount": 1,
                        "viewCount": 3,
                        "searchClickCount": 1,
                        "recommendationClickCount": 0,
                        "userProductScore": 310,
                        "lastInteractedAtUtc": "2026-03-29T10:00:00Z"
                      }
                    ]
                    """)
                };
            }

            throw new Xunit.Sdk.XunitException($"Unexpected Ordering path: {path}");
        });

        var controller = CreateController(catalogHandler, identityHandler, orderingHandler);
        controller.Request.Headers.Authorization = "Bearer test-token";

        var result = await controller.GetHomeRecommendations(limit: 4);

        var ok = Assert.IsType<OkObjectResult>(result);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value, WebJson));
        var forYou = json.RootElement.GetProperty("sections").EnumerateArray()
            .Single(section => section.GetProperty("id").GetString() == "for_you");
        var items = forYou.GetProperty("items").EnumerateArray().ToList();

        Assert.Contains(items, item => item.GetProperty("productId").GetInt32() == 105);

        var discoveryItem = items.Single(item => item.GetProperty("productId").GetInt32() == 105);
        var discoveryReason = discoveryItem.GetProperty("recommendationReason").GetString();
        Assert.Contains("Hợp nhóm", discoveryReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetHomeRecommendations_ForYouKeepsRecentSellerDiscoveryItem_WhenDirectScoresWouldOtherwiseFillCompactLimit()
    {
        var catalogHandler = new RecordingHttpMessageHandler(request =>
        {
            Assert.Equal("/api/products", request.RequestUri?.AbsolutePath);
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("""
                [
                  {
                    "productId": 104,
                    "productName": "Rau muong huu co",
                    "categoryName": "Rau la",
                    "categoryId": 1,
                    "unitName": "kg",
                    "price": 24000,
                    "status": true,
                    "availableStock": 30,
                    "origin": "Long An, Viet Nam",
                    "standard": "VietGAP",
                    "preservation": "giu mat",
                    "weight": "500g",
                    "primarySellerId": 4
                  },
                  {
                    "productId": 106,
                    "productName": "Cai xanh da mua",
                    "categoryName": "Rau la",
                    "categoryId": 1,
                    "unitName": "kg",
                    "price": 25000,
                    "status": true,
                    "availableStock": 24,
                    "origin": "Long An, Viet Nam",
                    "standard": "VietGAP",
                    "preservation": "giu mat",
                    "weight": "500g",
                    "primarySellerId": 47
                  },
                  {
                    "productId": 220,
                    "productName": "Ca chua da mua",
                    "categoryName": "Rau cu",
                    "categoryId": 3,
                    "unitName": "kg",
                    "price": 32000,
                    "status": true,
                    "availableStock": 28,
                    "origin": "Da Lat, Viet Nam",
                    "standard": "VietGAP",
                    "preservation": "giu mat",
                    "weight": "500g",
                    "primarySellerId": 51
                  },
                  {
                    "productId": 221,
                    "productName": "Bi do da mua",
                    "categoryName": "Rau cu",
                    "categoryId": 3,
                    "unitName": "kg",
                    "price": 30000,
                    "status": true,
                    "availableStock": 22,
                    "origin": "Can Tho, Viet Nam",
                    "standard": "VietGAP",
                    "preservation": "giu mat",
                    "weight": "500g",
                    "primarySellerId": 2
                  },
                  {
                    "productId": 520,
                    "productName": "Dau tay shop quen",
                    "categoryName": "Trai cay",
                    "categoryId": 9,
                    "unitName": "hop",
                    "price": 68000,
                    "status": true,
                    "availableStock": 15,
                    "origin": "Son La, Viet Nam",
                    "standard": "GlobalGAP",
                    "preservation": "giu lanh",
                    "weight": "300g",
                    "primarySellerId": 8
                  }
                ]
                """)
            };
        });

        var identityHandler = new RecordingHttpMessageHandler(_ =>
            new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("[]")
            });

        var orderingHandler = new RecordingHttpMessageHandler(request =>
        {
            var path = request.RequestUri?.AbsolutePath;
            if (string.Equals(path, "/api/orders/product-insights/stats", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("""
                    [
                      { "productId": 104, "averageRating": 4.8, "reviewCount": 10, "soldCount": 32 },
                      { "productId": 106, "averageRating": 4.7, "reviewCount": 9, "soldCount": 28 },
                      { "productId": 220, "averageRating": 4.9, "reviewCount": 12, "soldCount": 36 },
                      { "productId": 221, "averageRating": 4.6, "reviewCount": 8, "soldCount": 26 },
                      { "productId": 520, "averageRating": 4.5, "reviewCount": 7, "soldCount": 18 }
                    ]
                    """)
                };
            }

            if (string.Equals(path, "/api/orders/product-insights/home-profile", StringComparison.Ordinal) ||
                string.Equals(path, "/api/orders/product-insights/home-collaborative", StringComparison.Ordinal) ||
                string.Equals(path, "/api/orders/product-insights/basket-affinity", StringComparison.Ordinal) ||
                string.Equals(path, "/api/orders/product-insights/replenishment-profile", StringComparison.Ordinal) ||
                string.Equals(path, "/api/orders/product-insights/user-category", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("[]")
                };
            }

            if (string.Equals(path, "/api/orders/product-insights/user-product", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("""
                    [
                      {
                        "productId": 104,
                        "purchaseCount": 3,
                        "viewCount": 4,
                        "searchClickCount": 1,
                        "recommendationClickCount": 0,
                        "userProductScore": 420,
                        "lastInteractedAtUtc": "2026-04-01T10:00:00Z"
                      },
                      {
                        "productId": 220,
                        "purchaseCount": 2,
                        "viewCount": 3,
                        "searchClickCount": 1,
                        "recommendationClickCount": 0,
                        "userProductScore": 360,
                        "lastInteractedAtUtc": "2026-03-31T10:00:00Z"
                      },
                      {
                        "productId": 221,
                        "purchaseCount": 2,
                        "viewCount": 2,
                        "searchClickCount": 1,
                        "recommendationClickCount": 0,
                        "userProductScore": 330,
                        "lastInteractedAtUtc": "2026-03-30T10:00:00Z"
                      },
                      {
                        "productId": 106,
                        "purchaseCount": 1,
                        "viewCount": 3,
                        "searchClickCount": 1,
                        "recommendationClickCount": 0,
                        "userProductScore": 310,
                        "lastInteractedAtUtc": "2026-03-29T10:00:00Z"
                      }
                    ]
                    """)
                };
            }

            if (string.Equals(path, "/api/orders/product-insights/user-seller", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent($$"""
                    [
                      {
                        "sellerId": 8,
                        "purchaseCount": 1,
                        "viewCount": 2,
                        "searchClickCount": 1,
                        "distinctPurchasedProductCount": 1,
                        "userSellerScore": 180,
                        "lastInteractedAtUtc": "{{RecentInteractionUtc}}"
                      }
                    ]
                    """)
                };
            }

            throw new Xunit.Sdk.XunitException($"Unexpected Ordering path: {path}");
        });

        var controller = CreateController(catalogHandler, identityHandler, orderingHandler);
        controller.Request.Headers.Authorization = "Bearer test-token";

        var result = await controller.GetHomeRecommendations(limit: 4);

        var ok = Assert.IsType<OkObjectResult>(result);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value, WebJson));
        var forYou = json.RootElement.GetProperty("sections").EnumerateArray()
            .Single(section => section.GetProperty("id").GetString() == "for_you");
        var items = forYou.GetProperty("items").EnumerateArray().ToList();

        Assert.Contains(items, item => item.GetProperty("productId").GetInt32() == 520);

        var discoveryItem = items.Single(item => item.GetProperty("productId").GetInt32() == 520);
        var discoveryReason = discoveryItem.GetProperty("recommendationReason").GetString();
        Assert.Contains("Từ shop bạn vừa quay lại", discoveryReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetHomeRecommendations_ForYouKeepsFavoriteSellerDiscoveryItem_WhenDirectScoresWouldOtherwiseFillCompactLimit()
    {
        var catalogHandler = new RecordingHttpMessageHandler(request =>
        {
            Assert.Equal("/api/products", request.RequestUri?.AbsolutePath);
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("""
                [
                  {
                    "productId": 104,
                    "productName": "Rau muong huu co",
                    "categoryName": "Rau la",
                    "categoryId": 1,
                    "unitName": "kg",
                    "price": 24000,
                    "status": true,
                    "availableStock": 30,
                    "origin": "Long An, Viet Nam",
                    "standard": "VietGAP",
                    "preservation": "giu mat",
                    "weight": "500g",
                    "primarySellerId": 4
                  },
                  {
                    "productId": 106,
                    "productName": "Cai xanh da mua",
                    "categoryName": "Rau la",
                    "categoryId": 1,
                    "unitName": "kg",
                    "price": 25000,
                    "status": true,
                    "availableStock": 24,
                    "origin": "Long An, Viet Nam",
                    "standard": "VietGAP",
                    "preservation": "giu mat",
                    "weight": "500g",
                    "primarySellerId": 47
                  },
                  {
                    "productId": 220,
                    "productName": "Ca chua da mua",
                    "categoryName": "Rau cu",
                    "categoryId": 3,
                    "unitName": "kg",
                    "price": 32000,
                    "status": true,
                    "availableStock": 28,
                    "origin": "Da Lat, Viet Nam",
                    "standard": "VietGAP",
                    "preservation": "giu mat",
                    "weight": "500g",
                    "primarySellerId": 51
                  },
                  {
                    "productId": 221,
                    "productName": "Bi do da mua",
                    "categoryName": "Rau cu",
                    "categoryId": 3,
                    "unitName": "kg",
                    "price": 30000,
                    "status": true,
                    "availableStock": 22,
                    "origin": "Can Tho, Viet Nam",
                    "standard": "VietGAP",
                    "preservation": "giu mat",
                    "weight": "500g",
                    "primarySellerId": 2
                  },
                  {
                    "productId": 530,
                    "productName": "Nam huong shop quen",
                    "categoryName": "Nam",
                    "categoryId": 7,
                    "unitName": "hop",
                    "price": 72000,
                    "status": true,
                    "availableStock": 10,
                    "origin": "Da Nang, Viet Nam",
                    "standard": "GlobalGAP",
                    "preservation": "giu lanh",
                    "weight": "250g",
                    "primarySellerId": 18
                  }
                ]
                """)
            };
        });

        var identityHandler = new RecordingHttpMessageHandler(_ =>
            new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("[]")
            });

        var orderingHandler = new RecordingHttpMessageHandler(request =>
        {
            var path = request.RequestUri?.AbsolutePath;
            if (string.Equals(path, "/api/orders/product-insights/stats", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("""
                    [
                      { "productId": 104, "averageRating": 4.8, "reviewCount": 10, "soldCount": 32 },
                      { "productId": 106, "averageRating": 4.7, "reviewCount": 9, "soldCount": 28 },
                      { "productId": 220, "averageRating": 4.9, "reviewCount": 12, "soldCount": 36 },
                      { "productId": 221, "averageRating": 4.6, "reviewCount": 8, "soldCount": 26 },
                      { "productId": 530, "averageRating": 4.5, "reviewCount": 7, "soldCount": 18 }
                    ]
                    """)
                };
            }

            if (string.Equals(path, "/api/orders/product-insights/home-profile", StringComparison.Ordinal) ||
                string.Equals(path, "/api/orders/product-insights/home-collaborative", StringComparison.Ordinal) ||
                string.Equals(path, "/api/orders/product-insights/basket-affinity", StringComparison.Ordinal) ||
                string.Equals(path, "/api/orders/product-insights/replenishment-profile", StringComparison.Ordinal) ||
                string.Equals(path, "/api/orders/product-insights/user-category", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("[]")
                };
            }

            if (string.Equals(path, "/api/orders/product-insights/user-product", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("""
                    [
                      {
                        "productId": 104,
                        "purchaseCount": 3,
                        "viewCount": 4,
                        "searchClickCount": 1,
                        "recommendationClickCount": 0,
                        "userProductScore": 420,
                        "lastInteractedAtUtc": "2026-04-01T10:00:00Z"
                      },
                      {
                        "productId": 220,
                        "purchaseCount": 2,
                        "viewCount": 3,
                        "searchClickCount": 1,
                        "recommendationClickCount": 0,
                        "userProductScore": 360,
                        "lastInteractedAtUtc": "2026-03-31T10:00:00Z"
                      },
                      {
                        "productId": 221,
                        "purchaseCount": 2,
                        "viewCount": 2,
                        "searchClickCount": 1,
                        "recommendationClickCount": 0,
                        "userProductScore": 330,
                        "lastInteractedAtUtc": "2026-03-30T10:00:00Z"
                      },
                      {
                        "productId": 106,
                        "purchaseCount": 1,
                        "viewCount": 3,
                        "searchClickCount": 1,
                        "recommendationClickCount": 0,
                        "userProductScore": 310,
                        "lastInteractedAtUtc": "2026-03-29T10:00:00Z"
                      }
                    ]
                    """)
                };
            }

            if (string.Equals(path, "/api/orders/product-insights/user-seller", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("""
                    [
                      {
                        "sellerId": 18,
                        "purchaseCount": 2,
                        "viewCount": 2,
                        "searchClickCount": 1,
                        "distinctPurchasedProductCount": 2,
                        "userSellerScore": 190,
                        "lastInteractedAtUtc": "2026-02-10T09:00:00Z"
                      }
                    ]
                    """)
                };
            }

            throw new Xunit.Sdk.XunitException($"Unexpected Ordering path: {path}");
        });

        var controller = CreateController(catalogHandler, identityHandler, orderingHandler);
        controller.Request.Headers.Authorization = "Bearer test-token";

        var result = await controller.GetHomeRecommendations(limit: 4);

        var ok = Assert.IsType<OkObjectResult>(result);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value, WebJson));
        var forYou = json.RootElement.GetProperty("sections").EnumerateArray()
            .Single(section => section.GetProperty("id").GetString() == "for_you");
        var items = forYou.GetProperty("items").EnumerateArray().ToList();

        Assert.Contains(items, item => item.GetProperty("productId").GetInt32() == 530);

        var discoveryItem = items.Single(item => item.GetProperty("productId").GetInt32() == 530);
        var discoveryReason = discoveryItem.GetProperty("recommendationReason").GetString();
        Assert.True(
            discoveryReason?.Contains("shop bạn hay mua", StringComparison.OrdinalIgnoreCase) == true
            || discoveryReason?.Contains("shop bạn hay quan tâm", StringComparison.OrdinalIgnoreCase) == true);
    }

    [Fact]
    public async Task GetHomeRecommendations_CreatesBuyWithHistorySection_FromUserProductPurchaseSeeds_WhenRecentPreferenceSeedsAreEmpty()
    {
        var catalogHandler = new RecordingHttpMessageHandler(request =>
        {
            Assert.Equal("/api/products", request.RequestUri?.AbsolutePath);
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("""
                [
                  {
                    "productId": 104,
                    "productName": "Rau muong huu co",
                    "categoryName": "Rau la",
                    "categoryId": 1,
                    "unitName": "kg",
                    "price": 24000,
                    "status": true,
                    "availableStock": 30,
                    "origin": "Long An, Viet Nam",
                    "standard": "VietGAP",
                    "preservation": "giu mat",
                    "weight": "500g",
                    "primarySellerId": 4
                  },
                  {
                    "productId": 410,
                    "productName": "Ca chua goi y mua kem",
                    "categoryName": "Rau cu",
                    "categoryId": 3,
                    "unitName": "kg",
                    "price": 18000,
                    "status": true,
                    "availableStock": 16,
                    "origin": "Da Lat, Viet Nam",
                    "standard": "Huu co",
                    "preservation": "giu mat",
                    "weight": "500g",
                    "primarySellerId": 47
                  }
                ]
                """)
            };
        });
        var identityHandler = new RecordingHttpMessageHandler(_ =>
            new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("[]")
            });
        var orderingHandler = new RecordingHttpMessageHandler(request =>
        {
            var path = request.RequestUri?.AbsolutePath;
            if (string.Equals(path, "/api/orders/product-insights/stats", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("""
                    [
                      { "productId": 104, "averageRating": 4.8, "reviewCount": 12, "soldCount": 40 },
                      { "productId": 410, "averageRating": 4.1, "reviewCount": 3, "soldCount": 4 }
                    ]
                    """)
                };
            }

            if (string.Equals(path, "/api/orders/product-insights/home-profile", StringComparison.Ordinal) ||
                string.Equals(path, "/api/orders/product-insights/home-collaborative", StringComparison.Ordinal) ||
                string.Equals(path, "/api/orders/product-insights/replenishment-profile", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("[]")
                };
            }

            if (string.Equals(path, "/api/orders/product-insights/user-product", StringComparison.Ordinal))
            {
                var query = request.RequestUri?.Query ?? string.Empty;
                if (query.Contains("productIds=", StringComparison.Ordinal))
                {
                    return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                    {
                        Content = new StringContent("""
                        [
                          {
                            "productId": 104,
                            "purchaseCount": 3,
                            "viewCount": 1,
                            "searchClickCount": 0,
                            "recommendationClickCount": 0,
                            "userProductScore": 120,
                            "lastInteractedAtUtc": "2026-04-01T09:00:00Z"
                          }
                        ]
                        """)
                    };
                }

                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("""
                    [
                      {
                        "productId": 104,
                        "purchaseCount": 3,
                        "viewCount": 1,
                        "searchClickCount": 0,
                        "recommendationClickCount": 0,
                        "userProductScore": 120,
                        "lastInteractedAtUtc": "2026-04-01T09:00:00Z"
                      }
                    ]
                    """)
                };
            }

            if (string.Equals(path, "/api/orders/product-insights/basket-affinity", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("""
                    [
                      {
                        "productId": 104,
                        "candidateProductId": 410,
                        "coPurchaseOrderCount": 4,
                        "basketScore": 145
                      }
                    ]
                    """)
                };
            }

            throw new Xunit.Sdk.XunitException($"Unexpected Ordering path: {path}");
        });

        var controller = CreateController(catalogHandler, identityHandler, orderingHandler);
        controller.Request.Headers.Authorization = "Bearer test-token";

        var result = await controller.GetHomeRecommendations(limit: 1);

        var ok = Assert.IsType<OkObjectResult>(result);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value, WebJson));
        var buyWithHistory = json.RootElement.GetProperty("sections").EnumerateArray()
            .Single(section => section.GetProperty("id").GetString() == "buy_with_history");
        var item = buyWithHistory.GetProperty("items").EnumerateArray().Single();
        Assert.Equal(410, item.GetProperty("productId").GetInt32());
        Assert.Contains("Hay mua cùng", item.GetProperty("recommendationReason").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetHomeRecommendations_ReplenishSoonFallsBackToOverlappingPurchaseItems_WhenAllReorderCandidatesAlreadyAppearInBuyAgain()
    {
        var catalogHandler = new RecordingHttpMessageHandler(request =>
        {
            Assert.Equal("/api/products", request.RequestUri?.AbsolutePath);
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("""
                [
                  {
                    "productId": 104,
                    "productName": "Rau muong huu co",
                    "categoryName": "Rau la",
                    "categoryId": 1,
                    "unitName": "kg",
                    "price": 24000,
                    "status": true,
                    "availableStock": 30,
                    "origin": "Long An, Viet Nam",
                    "standard": "VietGAP",
                    "preservation": "giu mat",
                    "weight": "500g",
                    "primarySellerId": 4
                  }
                ]
                """)
            };
        });
        var identityHandler = new RecordingHttpMessageHandler(_ =>
            new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("[]")
            });
        var orderingHandler = new RecordingHttpMessageHandler(request =>
        {
            var path = request.RequestUri?.AbsolutePath;
            if (string.Equals(path, "/api/orders/product-insights/stats", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("""
                    [
                      { "productId": 104, "averageRating": 4.8, "reviewCount": 12, "soldCount": 40 }
                    ]
                    """)
                };
            }

            if (string.Equals(path, "/api/orders/product-insights/home-profile", StringComparison.Ordinal) ||
                string.Equals(path, "/api/orders/product-insights/home-collaborative", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("""
                    [
                      {
                        "productId": 104,
                        "purchaseCount": 3,
                        "viewCount": 4,
                        "searchClickCount": 1,
                        "recommendationClickCount": 0,
                        "preferenceScore": 92,
                        "lastInteractedAtUtc": "2026-04-01T09:00:00Z"
                      }
                    ]
                    """)
                };
            }

            if (string.Equals(path, "/api/orders/product-insights/user-product", StringComparison.Ordinal) ||
                string.Equals(path, "/api/orders/product-insights/basket-affinity", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("[]")
                };
            }

            if (string.Equals(path, "/api/orders/product-insights/replenishment-profile", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("""
                    [
                      {
                        "productId": 104,
                        "purchaseCount": 3,
                        "averageRepurchaseDays": 7,
                        "expectedReorderAtUtc": "2026-04-03T09:00:00Z",
                        "replenishmentScore": 133,
                        "lastPurchasedAtUtc": "2026-03-24T09:00:00Z"
                      }
                    ]
                    """)
                };
            }

            throw new Xunit.Sdk.XunitException($"Unexpected Ordering path: {path}");
        });

        var controller = CreateController(catalogHandler, identityHandler, orderingHandler);
        controller.Request.Headers.Authorization = "Bearer test-token";

        var result = await controller.GetHomeRecommendations(limit: 1);

        var ok = Assert.IsType<OkObjectResult>(result);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value, WebJson));
        var sections = json.RootElement.GetProperty("sections").EnumerateArray().ToList();
        var buyAgain = sections.Single(section => section.GetProperty("id").GetString() == "buy_again");
        Assert.Equal(104, buyAgain.GetProperty("items")[0].GetProperty("productId").GetInt32());

        var replenishSoon = sections.Single(section => section.GetProperty("id").GetString() == "replenish_soon");
        var replenishItem = replenishSoon.GetProperty("items")[0];
        Assert.Equal(104, replenishItem.GetProperty("productId").GetInt32());
        Assert.Contains("mua", replenishItem.GetProperty("recommendationReason").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetHomeRecommendations_ReplenishSoonUsesFriendlyCadenceCopy_WhenAverageRepurchaseDaysRoundsToZero()
    {
        var catalogHandler = new RecordingHttpMessageHandler(request =>
        {
            Assert.Equal("/api/products", request.RequestUri?.AbsolutePath);
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("""
                [
                  {
                    "productId": 330,
                    "productName": "Rau muong huu co",
                    "categoryName": "Rau la",
                    "categoryId": 1,
                    "unitName": "kg",
                    "price": 25000,
                    "status": true,
                    "availableStock": 18,
                    "origin": "Long An",
                    "standard": "VietGAP",
                    "preservation": "giu mat",
                    "shortDescription": "Rau xanh sach",
                    "averageRating": 4.7,
                    "reviewCount": 12,
                    "soldCount": 33,
                    "primarySellerId": 4
                  }
                ]
                """)
            };
        });

        var identityHandler = new RecordingHttpMessageHandler(_ => new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent("[]")
        });

        var orderingHandler = new RecordingHttpMessageHandler(request =>
        {
            var path = request.RequestUri?.AbsolutePath ?? string.Empty;

            if (string.Equals(path, "/api/orders/product-insights/home-profile", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("[]")
                };
            }

            if (string.Equals(path, "/api/orders/product-insights/home-collaborative", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("[]")
                };
            }

            if (string.Equals(path, "/api/orders/product-insights/user-product", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("[]")
                };
            }

            if (string.Equals(path, "/api/orders/product-insights/basket-affinity", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("[]")
                };
            }

            if (string.Equals(path, "/api/orders/product-insights/replenishment-profile", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent($$"""
                    [
                      {
                        "productId": 330,
                        "purchaseCount": 4,
                        "averageRepurchaseDays": 0.4,
                        "expectedReorderAtUtc": null,
                        "replenishmentScore": 188,
                        "lastPurchasedAtUtc": "{{RecentPurchaseUtc}}"
                      }
                    ]
                    """)
                };
            }

            if (string.Equals(path, "/api/orders/product-insights/stats", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("""
                    [
                      {
                        "productId": 330,
                        "averageRating": 4.7,
                        "reviewCount": 12,
                        "soldCount": 33
                      }
                    ]
                    """)
                };
            }

            throw new Xunit.Sdk.XunitException($"Unexpected Ordering path: {path}");
        });

        var controller = CreateController(catalogHandler, identityHandler, orderingHandler);
        controller.Request.Headers.Authorization = "Bearer test-token";

        var result = await controller.GetHomeRecommendations(limit: 4);

        var ok = Assert.IsType<OkObjectResult>(result);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value, WebJson));
        var sections = json.RootElement.GetProperty("sections").EnumerateArray().ToList();
        var replenishSoon = sections.Single(section => section.GetProperty("id").GetString() == "replenish_soon");
        var replenishItem = replenishSoon.GetProperty("items")[0];
        var recommendationReason = replenishItem.GetProperty("recommendationReason").GetString();

        Assert.DoesNotContain("0 ngày", recommendationReason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Mới mua gần đây", recommendationReason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("khá thường xuyên", recommendationReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetHomeRecommendations_OnlySurfacesFallbackReasons_WhenOrderingReturnsNoUsableSignals()
    {
        var catalogHandler = new RecordingHttpMessageHandler(request =>
        {
            Assert.Equal("/api/products", request.RequestUri?.AbsolutePath);
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("""
                [
                  {
                    "productId": 104,
                    "productName": "Ca chua huu co",
                    "categoryName": "Rau Cu",
                    "categoryId": 3,
                    "unitName": "kg",
                    "price": 32000,
                    "status": true,
                    "availableStock": 24,
                    "origin": "Da Lat",
                    "standard": "VietGAP",
                    "preservation": "giu mat",
                    "weight": "500g"
                  },
                  {
                    "productId": 220,
                    "productName": "Cam san vuon",
                    "categoryName": "Trai Cay",
                    "categoryId": 7,
                    "unitName": "kg",
                    "price": 45000,
                    "status": true,
                    "availableStock": 40,
                    "origin": "Vinh Long",
                    "standard": "VietGAP",
                    "preservation": "thoang mat",
                    "weight": "1kg"
                  }
                ]
                """)
            };
        });
        var identityHandler = new RecordingHttpMessageHandler(_ =>
            new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("[]")
            });
        var orderingHandler = new RecordingHttpMessageHandler(request =>
        {
            var path = request.RequestUri?.AbsolutePath;
            if (string.Equals(path, "/api/orders/product-insights/stats", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("""
                    [
                      { "productId": 104, "averageRating": 4.9, "reviewCount": 21, "soldCount": 66 },
                      { "productId": 220, "averageRating": 4.8, "reviewCount": 17, "soldCount": 40 }
                    ]
                    """)
                };
            }

            if (string.Equals(path, "/api/orders/product-insights/home-profile", StringComparison.Ordinal))
            {
                var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("[]")
                };
                response.Headers.Add("X-Recommendation-Fallback-Reason", "no_tracked_preference_interactions");
                return response;
            }

            if (string.Equals(path, "/api/orders/product-insights/home-collaborative", StringComparison.Ordinal))
            {
                var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("[]")
                };
                response.Headers.Add("X-Recommendation-Fallback-Reason", "no_preference_seed_for_collaborative");
                return response;
            }

            throw new Xunit.Sdk.XunitException($"Unexpected Ordering path: {path}");
        });

        var controller = CreateController(catalogHandler, identityHandler, orderingHandler);
        var result = await controller.GetHomeRecommendations(limit: 2);

        var ok = Assert.IsType<OkObjectResult>(result);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value, WebJson));
        Assert.Equal("content_based_home_v1", json.RootElement.GetProperty("algorithm").GetString());
        Assert.Equal("catalog_content_v1", json.RootElement.GetProperty("contentSignalSource").GetString());
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("signalSource").ValueKind);
        Assert.Equal("preference:no_tracked_preference_interactions+collaborative:no_preference_seed_for_collaborative", json.RootElement.GetProperty("fallbackReason").GetString());
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("preferenceSignalSource").ValueKind);
        Assert.Equal("no_tracked_preference_interactions", json.RootElement.GetProperty("preferenceFallbackReason").GetString());
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("collaborativeSignalSource").ValueKind);
        Assert.Equal("no_preference_seed_for_collaborative", json.RootElement.GetProperty("collaborativeFallbackReason").GetString());
        var signalBreakdown = json.RootElement.GetProperty("signalBreakdown");
        Assert.Equal("catalog_content_v1", signalBreakdown.GetProperty("content").GetString());
        Assert.Equal(JsonValueKind.Null, signalBreakdown.GetProperty("overall").ValueKind);
        Assert.Equal("preference:no_tracked_preference_interactions+collaborative:no_preference_seed_for_collaborative", signalBreakdown.GetProperty("overallReason").GetString());
        Assert.Equal(JsonValueKind.Null, signalBreakdown.GetProperty("preference").ValueKind);
        Assert.Equal("no_tracked_preference_interactions", signalBreakdown.GetProperty("preferenceReason").GetString());
        Assert.Equal(JsonValueKind.Null, signalBreakdown.GetProperty("collaborative").ValueKind);
        Assert.Equal("no_preference_seed_for_collaborative", signalBreakdown.GetProperty("collaborativeReason").GetString());
    }

    [Fact]
    public async Task GetHomeRecommendations_BlendsCollaborativeHomeCandidates_WhenOrderingProvidesThem()
    {
        var catalogHandler = new RecordingHttpMessageHandler(request => new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent("""
            [
              {
                "productId": 104,
                "productName": "Ca chua huu co",
                "categoryName": "Rau Cu",
                "categoryId": 3,
                "unitName": "kg",
                "price": 32000,
                "status": true,
                "availableStock": 24,
                "origin": "Da Lat",
                "standard": "VietGAP",
                "preservation": "giu mat",
                "weight": "1kg"
              },
              {
                "productId": 220,
                "productName": "Cam san vang",
                "categoryName": "Trai Cay",
                "categoryId": 5,
                "unitName": "kg",
                "price": 45000,
                "status": true,
                "availableStock": 30,
                "origin": "Ben Tre",
                "standard": "GlobalGAP",
                "preservation": "thoang mat",
                "weight": "1kg"
              }
            ]
            """)
        });
        var identityHandler = new RecordingHttpMessageHandler(_ => new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent("[]")
        });
        var orderingHandler = new RecordingHttpMessageHandler(request =>
        {
            var path = request.RequestUri?.AbsolutePath;
            if (string.Equals(path, "/api/orders/product-insights/stats", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("""
                    [
                      { "productId": 104, "averageRating": 4.0, "reviewCount": 3, "soldCount": 6 },
                      { "productId": 220, "averageRating": 4.0, "reviewCount": 3, "soldCount": 6 }
                    ]
                    """)
                };
            }

            if (string.Equals(path, "/api/orders/product-insights/home-profile", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("[]")
                };
            }

            if (string.Equals(path, "/api/orders/product-insights/home-collaborative", StringComparison.Ordinal))
            {
                var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("""
                    [
                      {
                        "productId": 220,
                        "coPurchaseOrderCount": 2,
                        "coViewSessionCount": 0,
                        "coClickSessionCount": 1,
                        "collaborativeScore": 180,
                        "seasonalityScore": 84,
                        "seasonalityLabel": "Vụ chính",
                        "seasonalityBadgeLabel": "Đang vào mùa"
                      }
                    ]
                    """)
                };
                response.Headers.Add("X-Recommendation-Signal-Source", "materialized_cf_v1");
                return response;
            }

            throw new Xunit.Sdk.XunitException($"Unexpected Ordering path: {path}");
        });

        var controller = CreateController(catalogHandler, identityHandler, orderingHandler);
        var result = await controller.GetHomeRecommendations(limit: 2);

        var ok = Assert.IsType<OkObjectResult>(result);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value, WebJson));
        Assert.Equal("hybrid_home_v1", json.RootElement.GetProperty("algorithm").GetString());
        Assert.Equal("catalog_content_v1", json.RootElement.GetProperty("contentSignalSource").GetString());
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("fallbackReason").ValueKind);
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("preferenceSignalSource").ValueKind);
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("preferenceFallbackReason").ValueKind);
        Assert.Equal("materialized_cf_v1", json.RootElement.GetProperty("collaborativeSignalSource").GetString());
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("collaborativeFallbackReason").ValueKind);
        Assert.Equal("materialized_cf_v1", json.RootElement.GetProperty("signalSource").GetString());
        var signalBreakdown = json.RootElement.GetProperty("signalBreakdown");
        Assert.Equal("catalog_content_v1", signalBreakdown.GetProperty("content").GetString());
        Assert.Equal("materialized_cf_v1", signalBreakdown.GetProperty("overall").GetString());
        Assert.Equal(JsonValueKind.Null, signalBreakdown.GetProperty("overallReason").ValueKind);
        Assert.Equal(JsonValueKind.Null, signalBreakdown.GetProperty("preference").ValueKind);
        Assert.Equal(JsonValueKind.Null, signalBreakdown.GetProperty("preferenceReason").ValueKind);
        Assert.Equal("materialized_cf_v1", signalBreakdown.GetProperty("collaborative").GetString());
        Assert.Equal(JsonValueKind.Null, signalBreakdown.GetProperty("collaborativeReason").ValueKind);
        var items = json.RootElement.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(220, items[0].GetProperty("productId").GetInt32());
        Assert.Contains("Hay được mua cùng", items[0].GetProperty("recommendationReason").GetString());
        Assert.Equal(84d, items[0].GetProperty("seasonalityScore").GetDouble());
        Assert.Equal("Vụ chính", items[0].GetProperty("seasonalityLabel").GetString());
        Assert.Equal("Đang vào mùa", items[0].GetProperty("seasonalityBadgeLabel").GetString());
    }

    [Fact]
    public async Task GetSimilarProducts_RanksClosestContentMatch_First()
    {
        var catalogHandler = new RecordingHttpMessageHandler(request =>
        {
            var path = request.RequestUri?.AbsolutePath;
            if (string.Equals(path, "/api/products/104", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("""
                    {
                      "productId": 104,
                      "productName": "Ca chua huu co",
                      "categoryName": "Rau Cu",
                      "categoryId": 3,
                      "unitName": "kg",
                      "price": 32000,
                      "status": true,
                      "availableStock": 20,
                      "origin": "Da Lat",
                      "standard": "VietGAP",
                      "preservation": "giu mat",
                      "weight": "500g",
                      "productAttributes": [
                        {
                          "categoryAttributeId": 9,
                          "attributeKey": "sweetness",
                          "displayName": "độ ngọt",
                          "valueText": "vừa",
                          "normalizedValue": "vua"
                        }
                      ]
                    }
                    """)
                };
            }

            Assert.Equal("/api/products", path);
            Assert.Contains("categoryIds=3", request.RequestUri?.Query);
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("""
                [
                  {
                    "productId": 105,
                    "productName": "Ca chua cherry huu co",
                    "categoryName": "Rau Cu",
                    "categoryId": 3,
                    "unitName": "kg",
                    "price": 34000,
                    "status": true,
                    "availableStock": 15,
                    "origin": "Da Lat",
                    "standard": "VietGAP",
                    "preservation": "giu mat",
                    "weight": "550g",
                    "productAttributes": [
                      {
                        "categoryAttributeId": 9,
                        "attributeKey": "sweetness",
                        "displayName": "độ ngọt",
                        "valueText": "vừa",
                        "normalizedValue": "vua"
                      }
                    ]
                  },
                  {
                    "productId": 106,
                    "productName": "Cu den tuoi",
                    "categoryName": "Rau Cu",
                    "categoryId": 3,
                    "unitName": "kg",
                    "price": 60000,
                    "status": true,
                    "availableStock": 15,
                    "origin": "Gia Lai",
                    "standard": "GlobalGAP",
                    "preservation": "kho",
                    "weight": "1kg"
                  }
                ]
                """)
            };
        });
        var identityHandler = new RecordingHttpMessageHandler(_ =>
            new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("[]")
            });
        var orderingHandler = new RecordingHttpMessageHandler(request =>
        {
            if (string.Equals(request.RequestUri?.AbsolutePath, "/api/orders/product-insights/stats", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("""
                    [
                      { "productId": 104, "averageRating": 4.7, "reviewCount": 12, "soldCount": 35 },
                      { "productId": 105, "averageRating": 4.9, "reviewCount": 14, "soldCount": 41 },
                      { "productId": 106, "averageRating": 4.1, "reviewCount": 4, "soldCount": 9 }
                    ]
                    """)
                };
            }

            var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("[]")
            };
            response.Headers.Add("X-Recommendation-Signal-Source", "ad_hoc_cf_v1");
            response.Headers.Add("X-Recommendation-Fallback-Reason", "no_materialized_collaborative_candidates");
            return response;
        });
        var controller = CreateController(catalogHandler, identityHandler, orderingHandler);

        var result = await controller.GetSimilarProducts(104, limit: 2);

        var ok = Assert.IsType<OkObjectResult>(result);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value, WebJson));
        Assert.Equal("product_similar", json.RootElement.GetProperty("placement").GetString());
        Assert.Equal(104, json.RootElement.GetProperty("seedProductId").GetInt32());
        Assert.Equal("catalog_content_v1", json.RootElement.GetProperty("contentSignalSource").GetString());
        Assert.Equal("no_materialized_collaborative_candidates", json.RootElement.GetProperty("fallbackReason").GetString());
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("collaborativeSignalSource").ValueKind);
        Assert.Equal("no_materialized_collaborative_candidates", json.RootElement.GetProperty("collaborativeFallbackReason").GetString());
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("signalSource").ValueKind);
        var signalBreakdown = json.RootElement.GetProperty("signalBreakdown");
        Assert.Equal("catalog_content_v1", signalBreakdown.GetProperty("content").GetString());
        Assert.Equal(JsonValueKind.Null, signalBreakdown.GetProperty("overall").ValueKind);
        Assert.Equal("no_materialized_collaborative_candidates", signalBreakdown.GetProperty("overallReason").GetString());
        Assert.Equal(JsonValueKind.Null, signalBreakdown.GetProperty("preference").ValueKind);
        Assert.Equal(JsonValueKind.Null, signalBreakdown.GetProperty("preferenceReason").ValueKind);
        Assert.Equal(JsonValueKind.Null, signalBreakdown.GetProperty("collaborative").ValueKind);
        Assert.Equal("no_materialized_collaborative_candidates", signalBreakdown.GetProperty("collaborativeReason").GetString());
        var items = json.RootElement.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(2, items.Count);
        Assert.Equal(105, items[0].GetProperty("productId").GetInt32());
        Assert.Contains("Cùng", items[0].GetProperty("recommendationReason").GetString());
        Assert.Contains("độ ngọt", items[0].GetProperty("recommendationReason").GetString());
    }

    [Fact]
    public async Task GetSimilarProducts_IncludesRegionalOriginReason_WhenSameRegionButNotExactOrigin()
    {
        var catalogHandler = new RecordingHttpMessageHandler(request =>
        {
            var path = request.RequestUri?.AbsolutePath;
            if (string.Equals(path, "/api/products/204", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("""
                    {
                      "productId": 204,
                      "productName": "Xa lach romaine",
                      "categoryName": "Rau lá",
                      "categoryId": 4,
                      "unitName": "Bó",
                      "price": 28000,
                      "status": true,
                      "availableStock": 12,
                      "origin": "Da Lat, Viet Nam",
                      "standard": "VietGAP",
                      "preservation": "giu mat",
                      "weight": "400g"
                    }
                    """)
                };
            }

            Assert.Equal("/api/products", path);
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("""
                [
                  {
                    "productId": 205,
                    "productName": "Xa lach lo xo",
                    "categoryName": "Rau lá",
                    "categoryId": 4,
                    "unitName": "Bó",
                    "price": 29000,
                    "status": true,
                    "availableStock": 10,
                    "origin": "Lam Dong, Viet Nam",
                    "standard": "VietGAP",
                    "preservation": "giu mat",
                    "weight": "400g"
                  },
                  {
                    "productId": 206,
                    "productName": "Rau cai xanh",
                    "categoryName": "Rau lá",
                    "categoryId": 4,
                    "unitName": "Bó",
                    "price": 25000,
                    "status": true,
                    "availableStock": 15,
                    "origin": "Can Tho, Viet Nam",
                    "standard": "VietGAP",
                    "preservation": "giu mat",
                    "weight": "400g"
                  }
                ]
                """)
            };
        });
        var identityHandler = new RecordingHttpMessageHandler(_ =>
            new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("[]")
            });
        var orderingHandler = new RecordingHttpMessageHandler(request =>
        {
            if (string.Equals(request.RequestUri?.AbsolutePath, "/api/orders/product-insights/stats", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("""
                    [
                      { "productId": 204, "averageRating": 4.5, "reviewCount": 12, "soldCount": 25 },
                      { "productId": 205, "averageRating": 4.6, "reviewCount": 10, "soldCount": 20 },
                      { "productId": 206, "averageRating": 4.6, "reviewCount": 10, "soldCount": 20 }
                    ]
                    """)
                };
            }

            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("[]")
            };
        });
        var controller = CreateController(catalogHandler, identityHandler, orderingHandler);

        var result = await controller.GetSimilarProducts(204, limit: 2);

        var ok = Assert.IsType<OkObjectResult>(result);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value, WebJson));
        var items = json.RootElement.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(205, items[0].GetProperty("productId").GetInt32());
        Assert.Contains("Cùng vùng trồng", items[0].GetProperty("recommendationReason").GetString());
    }

    [Fact]
    public async Task GetSimilarProducts_IncludesSellerLocalityReason_WhenMerchantAddressesShareRegion()
    {
        var catalogHandler = new RecordingHttpMessageHandler(request =>
        {
            var path = request.RequestUri?.AbsolutePath;
            if (string.Equals(path, "/api/products/304", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("""
                    {
                      "productId": 304,
                      "productName": "Ot chuong xanh",
                      "categoryName": "Rau quả",
                      "categoryId": 17,
                      "unitName": "kg",
                      "price": 42000,
                      "status": true,
                      "availableStock": 16,
                      "origin": "Can Tho, Viet Nam",
                      "standard": "VietGAP",
                      "preservation": "giu mat",
                      "weight": "500g",
                      "primarySellerId": 41
                    }
                    """)
                };
            }

            Assert.Equal("/api/products", path);
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("""
                [
                  {
                    "productId": 305,
                    "productName": "Ot chuong vang",
                    "categoryName": "Rau quả",
                    "categoryId": 17,
                    "unitName": "kg",
                    "price": 43000,
                    "status": true,
                    "availableStock": 14,
                    "origin": "Da Nang, Viet Nam",
                    "standard": "VietGAP",
                    "preservation": "giu mat",
                    "weight": "500g",
                    "primarySellerId": 42
                  },
                  {
                    "productId": 306,
                    "productName": "Ca tim huu co",
                    "categoryName": "Rau quả",
                    "categoryId": 17,
                    "unitName": "kg",
                    "price": 39000,
                    "status": true,
                    "availableStock": 18,
                    "origin": "Hai Phong, Viet Nam",
                    "standard": "VietGAP",
                    "preservation": "giu mat",
                    "weight": "500g",
                    "primarySellerId": 43
                  }
                ]
                """)
            };
        });
        var identityHandler = new RecordingHttpMessageHandler(request =>
            new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("""
                [
                  {
                    "sellerId": 41,
                    "shopName": "Nong trai Da Lat",
                    "userName": "seller41",
                    "addressSummary": "18 Yersin, Da Lat, Lam Dong",
                    "joinedAt": "2025-01-01T00:00:00Z"
                  },
                  {
                    "sellerId": 42,
                    "shopName": "Vuon Bao Loc",
                    "userName": "seller42",
                    "addressSummary": "27 Le Hong Phong, Bao Loc, Lam Dong",
                    "joinedAt": "2025-01-01T00:00:00Z"
                  },
                  {
                    "sellerId": 43,
                    "shopName": "Vuon Hai Phong",
                    "userName": "seller43",
                    "addressSummary": "10 Le Loi, Hai Phong",
                    "joinedAt": "2025-01-01T00:00:00Z"
                  }
                ]
                """)
            });
        var orderingHandler = new RecordingHttpMessageHandler(request =>
        {
            if (string.Equals(request.RequestUri?.AbsolutePath, "/api/orders/product-insights/stats", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("""
                    [
                      { "productId": 304, "averageRating": 4.4, "reviewCount": 12, "soldCount": 20 },
                      { "productId": 305, "averageRating": 4.6, "reviewCount": 13, "soldCount": 24 },
                      { "productId": 306, "averageRating": 4.5, "reviewCount": 11, "soldCount": 18 }
                    ]
                    """)
                };
            }

            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("[]")
            };
        });
        var controller = CreateController(catalogHandler, identityHandler, orderingHandler);

        var result = await controller.GetSimilarProducts(304, limit: 2);

        var ok = Assert.IsType<OkObjectResult>(result);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value, WebJson));
        var items = json.RootElement.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(305, items[0].GetProperty("productId").GetInt32());
        Assert.Contains("Shop cùng vùng", items[0].GetProperty("recommendationReason").GetString());
        Assert.Contains("Từ Da Nang", items[0].GetProperty("recommendationReason").GetString());
    }

    [Fact]
    public async Task GetSimilarProducts_PrioritizesSeasonalOriginReason_BeforeSellerLocality()
    {
        var catalogHandler = new RecordingHttpMessageHandler(request =>
        {
            var path = request.RequestUri?.AbsolutePath;
            if (string.Equals(path, "/api/products/344", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("""
                    {
                      "productId": 344,
                      "productName": "Dua leo seed",
                      "categoryName": "Rau quả",
                      "categoryId": 17,
                      "unitName": "kg",
                      "price": 38000,
                      "status": true,
                      "availableStock": 20,
                      "origin": "Da Nang, Viet Nam",
                      "standard": "VietGAP",
                      "preservation": "giu mat",
                      "weight": "500g",
                      "primarySellerId": 61
                    }
                    """)
                };
            }

            Assert.Equal("/api/products", path);
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("""
                [
                  {
                    "productId": 345,
                    "productName": "Dua leo huu co",
                    "categoryName": "Rau quả",
                    "categoryId": 17,
                    "unitName": "kg",
                    "price": 39000,
                    "status": true,
                    "availableStock": 18,
                    "origin": "Da Nang, Viet Nam",
                    "standard": "VietGAP",
                    "preservation": "giu mat",
                    "weight": "500g",
                    "primarySellerId": 62
                  }
                ]
                """)
            };
        });
        var identityHandler = new RecordingHttpMessageHandler(_ =>
            new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("""
                [
                  {
                    "sellerId": 61,
                    "shopName": "Vuon Da Nang",
                    "userName": "seller61",
                    "addressSummary": "18 Tran Phu, Da Nang",
                    "joinedAt": "2025-01-01T00:00:00Z"
                  },
                  {
                    "sellerId": 62,
                    "shopName": "Vuon Son Tra",
                    "userName": "seller62",
                    "addressSummary": "25 Le Duan, Da Nang",
                    "joinedAt": "2025-01-01T00:00:00Z"
                  }
                ]
                """)
            });
        var orderingHandler = new RecordingHttpMessageHandler(request =>
        {
            if (string.Equals(request.RequestUri?.AbsolutePath, "/api/orders/product-insights/stats", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("""
                    [
                      { "productId": 344, "averageRating": 4.5, "reviewCount": 9, "soldCount": 16 },
                      { "productId": 345, "averageRating": 4.7, "reviewCount": 12, "soldCount": 22 }
                    ]
                    """)
                };
            }

            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("[]")
            };
        });
        var controller = CreateController(catalogHandler, identityHandler, orderingHandler);

        var result = await controller.GetSimilarProducts(344, limit: 1);

        var ok = Assert.IsType<OkObjectResult>(result);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value, WebJson));
        var reason = json.RootElement.GetProperty("items")[0].GetProperty("recommendationReason").GetString();
        Assert.StartsWith("Cùng xuất xứ", reason, StringComparison.Ordinal);
        Assert.Contains("Đúng mùa ở vùng trồng này", reason);
    }

    [Fact]
    public async Task GetSimilarProducts_PrefersOriginLocalityOverSellerLocality_WhenSignalsConflict()
    {
        var catalogHandler = new RecordingHttpMessageHandler(request =>
        {
            var path = request.RequestUri?.AbsolutePath;
            if (string.Equals(path, "/api/products/314", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("""
                    {
                      "productId": 314,
                      "productName": "Ot chuong seed",
                      "categoryName": "Rau quả",
                      "categoryId": 17,
                      "unitName": "kg",
                      "price": 42000,
                      "status": true,
                      "availableStock": 16,
                      "origin": "Can Tho, Viet Nam",
                      "standard": "VietGAP",
                      "preservation": "giu mat",
                      "weight": "500g",
                      "primarySellerId": 41
                    }
                    """)
                };
            }

            Assert.Equal("/api/products", path);
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("""
                [
                  {
                    "productId": 315,
                    "productName": "Ot chuong vang",
                    "categoryName": "Rau quả",
                    "categoryId": 17,
                    "unitName": "kg",
                    "price": 43000,
                    "status": true,
                    "availableStock": 14,
                    "origin": "Can Tho, Viet Nam",
                    "standard": "VietGAP",
                    "preservation": "giu mat",
                    "weight": "500g",
                    "primarySellerId": 42
                  },
                  {
                    "productId": 316,
                    "productName": "Ot chuong xanh",
                    "categoryName": "Rau quả",
                    "categoryId": 17,
                    "unitName": "kg",
                    "price": 43000,
                    "status": true,
                    "availableStock": 14,
                    "origin": "Da Lat, Viet Nam",
                    "standard": "VietGAP",
                    "preservation": "giu mat",
                    "weight": "500g",
                    "primarySellerId": 43
                  }
                ]
                """)
            };
        });
        var identityHandler = new RecordingHttpMessageHandler(_ =>
            new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("""
                [
                  {
                    "sellerId": 41,
                    "shopName": "Seed Shop",
                    "userName": "seller41",
                    "addressSummary": "18 Yersin, Da Lat, Lam Dong",
                    "joinedAt": "2025-01-01T00:00:00Z"
                  },
                  {
                    "sellerId": 42,
                    "shopName": "Can Tho Farm",
                    "userName": "seller42",
                    "addressSummary": "10 Le Loi, Hai Phong",
                    "joinedAt": "2025-01-01T00:00:00Z"
                  },
                  {
                    "sellerId": 43,
                    "shopName": "Bao Loc Shop",
                    "userName": "seller43",
                    "addressSummary": "27 Le Hong Phong, Bao Loc, Lam Dong",
                    "joinedAt": "2025-01-01T00:00:00Z"
                  }
                ]
                """)
            });
        var orderingHandler = new RecordingHttpMessageHandler(request =>
        {
            if (string.Equals(request.RequestUri?.AbsolutePath, "/api/orders/product-insights/stats", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("""
                    [
                      { "productId": 314, "averageRating": 4.4, "reviewCount": 12, "soldCount": 20 },
                      { "productId": 315, "averageRating": 4.4, "reviewCount": 12, "soldCount": 20 },
                      { "productId": 316, "averageRating": 4.4, "reviewCount": 12, "soldCount": 20 }
                    ]
                    """)
                };
            }

            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("[]")
            };
        });
        var controller = CreateController(catalogHandler, identityHandler, orderingHandler);

        var result = await controller.GetSimilarProducts(314, limit: 2);

        var ok = Assert.IsType<OkObjectResult>(result);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value, WebJson));
        var items = json.RootElement.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(315, items[0].GetProperty("productId").GetInt32());
        Assert.Contains("Cùng xuất xứ", items[0].GetProperty("recommendationReason").GetString());
    }

    [Fact]
    public async Task GetSimilarProducts_FirstPassDiversifiesAcrossSellers_WhenEnoughCandidatesExist()
    {
        var catalogHandler = new RecordingHttpMessageHandler(request =>
        {
            var path = request.RequestUri?.AbsolutePath;
            if (string.Equals(path, "/api/products/204", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("""
                    {
                      "productId": 204,
                      "productName": "Rau la seed",
                      "categoryName": "Rau la",
                      "categoryId": 12,
                      "unitName": "kg",
                      "price": 32000,
                      "status": true,
                      "availableStock": 20,
                      "origin": "Da Lat",
                      "standard": "VietGAP",
                      "preservation": "giu mat",
                      "weight": "500g",
                      "primarySellerId": 4
                    }
                    """)
                };
            }

            Assert.Equal("/api/products", path);
            Assert.Contains("categoryIds=12", request.RequestUri?.Query);
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("""
                [
                  {
                    "productId": 211,
                    "productName": "Rau la A",
                    "categoryName": "Rau la",
                    "categoryId": 12,
                    "unitName": "kg",
                    "price": 32500,
                    "status": true,
                    "availableStock": 18,
                    "origin": "Da Lat",
                    "standard": "VietGAP",
                    "preservation": "giu mat",
                    "weight": "500g",
                    "primarySellerId": 4
                  },
                  {
                    "productId": 212,
                    "productName": "Rau la B",
                    "categoryName": "Rau la",
                    "categoryId": 12,
                    "unitName": "kg",
                    "price": 33000,
                    "status": true,
                    "availableStock": 18,
                    "origin": "Da Lat",
                    "standard": "VietGAP",
                    "preservation": "giu mat",
                    "weight": "500g",
                    "primarySellerId": 4
                  },
                  {
                    "productId": 213,
                    "productName": "Rau la C",
                    "categoryName": "Rau la",
                    "categoryId": 12,
                    "unitName": "kg",
                    "price": 33500,
                    "status": true,
                    "availableStock": 18,
                    "origin": "Da Lat",
                    "standard": "VietGAP",
                    "preservation": "giu mat",
                    "weight": "500g",
                    "primarySellerId": 4
                  },
                  {
                    "productId": 214,
                    "productName": "Rau la D",
                    "categoryName": "Rau la",
                    "categoryId": 12,
                    "unitName": "kg",
                    "price": 34000,
                    "status": true,
                    "availableStock": 18,
                    "origin": "Can Tho",
                    "standard": "GlobalGAP",
                    "preservation": "giu mat",
                    "weight": "500g",
                    "primarySellerId": 9
                  }
                ]
                """)
            };
        });
        var identityHandler = new RecordingHttpMessageHandler(_ =>
            new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("[]")
            });
        var orderingHandler = new RecordingHttpMessageHandler(request =>
        {
            if (string.Equals(request.RequestUri?.AbsolutePath, "/api/orders/product-insights/stats", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("""
                    [
                      { "productId": 204, "averageRating": 4.7, "reviewCount": 12, "soldCount": 35 },
                      { "productId": 211, "averageRating": 4.9, "reviewCount": 14, "soldCount": 48 },
                      { "productId": 212, "averageRating": 4.8, "reviewCount": 13, "soldCount": 43 },
                      { "productId": 213, "averageRating": 4.7, "reviewCount": 11, "soldCount": 40 },
                      { "productId": 214, "averageRating": 4.5, "reviewCount": 10, "soldCount": 36 }
                    ]
                    """)
                };
            }

            var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("[]")
            };
            response.Headers.Add("X-Recommendation-Signal-Source", "ad_hoc_cf_v1");
            response.Headers.Add("X-Recommendation-Fallback-Reason", "no_materialized_collaborative_candidates");
            return response;
        });
        var controller = CreateController(catalogHandler, identityHandler, orderingHandler);

        var result = await controller.GetSimilarProducts(204, limit: 3);

        var ok = Assert.IsType<OkObjectResult>(result);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value, WebJson));
        var items = json.RootElement.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(3, items.Count);
        Assert.Contains(items, item => item.GetProperty("productId").GetInt32() == 214);
        Assert.Equal(2, items.Count(item => item.GetProperty("primarySellerId").GetInt32() == 4));
        Assert.Equal(1, items.Count(item => item.GetProperty("primarySellerId").GetInt32() == 9));
    }

    [Fact]
    public async Task GetSimilarProducts_UsesTighterSellerCap_ForCompactLimit()
    {
        var catalogHandler = new RecordingHttpMessageHandler(request =>
        {
            var path = request.RequestUri?.AbsolutePath;
            if (string.Equals(path, "/api/products/301", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("""
                    {
                      "productId": 301,
                      "productName": "Seed compact",
                      "categoryName": "Rau la",
                      "categoryId": 21,
                      "unitName": "kg",
                      "price": 32000,
                      "status": true,
                      "availableStock": 20,
                      "origin": "Da Lat",
                      "standard": "VietGAP",
                      "preservation": "giu mat",
                      "weight": "500g",
                      "primarySellerId": 4
                    }
                    """)
                };
            }

            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("""
                [
                  { "productId": 302, "productName": "A", "categoryName": "Rau la", "categoryId": 21, "unitName": "kg", "price": 33000, "status": true, "availableStock": 20, "origin": "Da Lat", "standard": "VietGAP", "preservation": "giu mat", "weight": "500g", "primarySellerId": 4 },
                  { "productId": 303, "productName": "B", "categoryName": "Rau la", "categoryId": 21, "unitName": "kg", "price": 34000, "status": true, "availableStock": 20, "origin": "Da Lat", "standard": "VietGAP", "preservation": "giu mat", "weight": "500g", "primarySellerId": 4 },
                  { "productId": 304, "productName": "C", "categoryName": "Rau la", "categoryId": 21, "unitName": "kg", "price": 35000, "status": true, "availableStock": 20, "origin": "Can Tho", "standard": "GlobalGAP", "preservation": "giu mat", "weight": "500g", "primarySellerId": 9 },
                  { "productId": 305, "productName": "D", "categoryName": "Rau la", "categoryId": 21, "unitName": "kg", "price": 36000, "status": true, "availableStock": 20, "origin": "Long An", "standard": "GlobalGAP", "preservation": "giu mat", "weight": "500g", "primarySellerId": 11 }
                ]
                """)
            };
        });
        var identityHandler = new RecordingHttpMessageHandler(_ =>
            new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("[]")
            });
        var orderingHandler = new RecordingHttpMessageHandler(request =>
        {
            if (string.Equals(request.RequestUri?.AbsolutePath, "/api/orders/product-insights/stats", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("""
                    [
                      { "productId": 301, "averageRating": 4.7, "reviewCount": 12, "soldCount": 35 },
                      { "productId": 302, "averageRating": 4.9, "reviewCount": 14, "soldCount": 48 },
                      { "productId": 303, "averageRating": 4.8, "reviewCount": 13, "soldCount": 43 },
                      { "productId": 304, "averageRating": 4.5, "reviewCount": 10, "soldCount": 36 },
                      { "productId": 305, "averageRating": 4.4, "reviewCount": 9, "soldCount": 33 }
                    ]
                    """)
                };
            }

            var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("""
                [
                  { "productId": 302, "collaborativeScore": 160, "coPurchaseOrderCount": 9, "coClickSessionCount": 0, "coViewSessionCount": 0 },
                  { "productId": 303, "collaborativeScore": 150, "coPurchaseOrderCount": 8, "coClickSessionCount": 0, "coViewSessionCount": 0 },
                  { "productId": 304, "collaborativeScore": 140, "coPurchaseOrderCount": 7, "coClickSessionCount": 0, "coViewSessionCount": 0 },
                  { "productId": 305, "collaborativeScore": 130, "coPurchaseOrderCount": 6, "coClickSessionCount": 0, "coViewSessionCount": 0 }
                ]
                """)
            };
            response.Headers.Add("X-Recommendation-Signal-Source", "materialized_cf_v1");
            return response;
        });

        var controller = CreateController(catalogHandler, identityHandler, orderingHandler);

        var result = await controller.GetSimilarProducts(301, limit: 4);

        var ok = Assert.IsType<OkObjectResult>(result);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value, WebJson));
        var items = json.RootElement.GetProperty("items").EnumerateArray().ToList();
        var sellerIds = items.Select(item => item.GetProperty("primarySellerId").GetInt32()).ToList();

        Assert.Equal(new[] { 4, 9, 11, 4 }, sellerIds);
    }

    [Fact]
    public async Task GetSimilarProducts_UsesTighterOriginCap_ForCompactLimit()
    {
        var catalogHandler = new RecordingHttpMessageHandler(request =>
        {
            var path = request.RequestUri?.AbsolutePath;
            if (string.Equals(path, "/api/products/401", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("""
                    {
                      "productId": 401,
                      "productName": "Rau la seed",
                      "categoryName": "Rau lá",
                      "categoryId": 4,
                      "unitName": "Bó",
                      "price": 18000,
                      "status": true,
                      "availableStock": 20,
                      "origin": "Da Lat, Viet Nam",
                      "standard": "VietGAP",
                      "preservation": "giu mat",
                      "weight": "400g",
                      "primarySellerId": 70
                    }
                    """)
                };
            }

            Assert.Equal("/api/products", path);
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("""
                [
                  { "productId": 402, "productName": "Rau la A", "categoryName": "Rau lá", "categoryId": 4, "unitName": "Bó", "price": 17500, "status": true, "availableStock": 18, "origin": "Da Lat, Viet Nam", "standard": "VietGAP", "preservation": "giu mat", "weight": "400g", "primarySellerId": 4 },
                  { "productId": 403, "productName": "Rau la B", "categoryName": "Rau lá", "categoryId": 4, "unitName": "Bó", "price": 17800, "status": true, "availableStock": 18, "origin": "Da Lat, Viet Nam", "standard": "VietGAP", "preservation": "giu mat", "weight": "400g", "primarySellerId": 9 },
                  { "productId": 404, "productName": "Rau la C", "categoryName": "Rau lá", "categoryId": 4, "unitName": "Bó", "price": 17900, "status": true, "availableStock": 18, "origin": "Long An, Viet Nam", "standard": "VietGAP", "preservation": "giu mat", "weight": "400g", "primarySellerId": 11 },
                  { "productId": 405, "productName": "Rau la D", "categoryName": "Rau lá", "categoryId": 4, "unitName": "Bó", "price": 18100, "status": true, "availableStock": 18, "origin": "Can Tho, Viet Nam", "standard": "VietGAP", "preservation": "giu mat", "weight": "400g", "primarySellerId": 12 },
                  { "productId": 406, "productName": "Rau la E", "categoryName": "Rau lá", "categoryId": 4, "unitName": "Bó", "price": 18200, "status": true, "availableStock": 18, "origin": "Da Lat, Viet Nam", "standard": "VietGAP", "preservation": "giu mat", "weight": "400g", "primarySellerId": 13 }
                ]
                """)
            };
        });
        var identityHandler = new RecordingHttpMessageHandler(_ =>
            new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("[]")
            });
        var orderingHandler = new RecordingHttpMessageHandler(request =>
        {
            if (string.Equals(request.RequestUri?.AbsolutePath, "/api/orders/product-insights/stats", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("""
                    [
                      { "productId": 401, "averageRating": 4.7, "reviewCount": 12, "soldCount": 35 },
                      { "productId": 402, "averageRating": 4.9, "reviewCount": 15, "soldCount": 42 },
                      { "productId": 403, "averageRating": 4.8, "reviewCount": 13, "soldCount": 37 },
                      { "productId": 404, "averageRating": 4.6, "reviewCount": 11, "soldCount": 31 },
                      { "productId": 405, "averageRating": 4.5, "reviewCount": 10, "soldCount": 28 },
                      { "productId": 406, "averageRating": 4.4, "reviewCount": 9, "soldCount": 24 }
                    ]
                    """)
                };
            }

            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("[]")
            };
        });
        var controller = CreateController(catalogHandler, identityHandler, orderingHandler);

        var result = await controller.GetSimilarProducts(401, limit: 4);

        var ok = Assert.IsType<OkObjectResult>(result);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value, WebJson));
        var items = json.RootElement.GetProperty("items").EnumerateArray().ToList();
        var topOrigins = items
            .Select(item => item.GetProperty("origin").GetString())
            .ToList();

        Assert.Equal(4, items.Count);
        Assert.Equal(3, topOrigins.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(2, topOrigins.Count(origin => string.Equals(origin, "Da Lat, Viet Nam", StringComparison.Ordinal)));
        Assert.Contains("Long An, Viet Nam", topOrigins);
        Assert.Contains("Can Tho, Viet Nam", topOrigins);
    }

    [Fact]
    public async Task GetSimilarProducts_SecondPassPrefersNewSellerOrOrigin_WhenCompactFirstPassNeedsFill()
    {
        var catalogHandler = new RecordingHttpMessageHandler(request =>
        {
            var path = request.RequestUri?.AbsolutePath;
            if (string.Equals(path, "/api/products/501", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("""
                    {
                      "productId": 501,
                      "productName": "Seed compact similar",
                      "categoryName": "Rau lá",
                      "categoryId": 4,
                      "unitName": "Bó",
                      "price": 18000,
                      "status": true,
                      "availableStock": 20,
                      "origin": "Da Lat, Viet Nam",
                      "standard": "VietGAP",
                      "preservation": "giu mat",
                      "weight": "400g",
                      "primarySellerId": 70
                    }
                    """)
                };
            }

            Assert.Equal("/api/products", path);
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("""
                [
                  { "productId": 502, "productName": "Candidate A", "categoryName": "Rau lá", "categoryId": 4, "unitName": "Bó", "price": 17500, "status": true, "availableStock": 18, "origin": "Da Lat, Viet Nam", "standard": "VietGAP", "preservation": "giu mat", "weight": "400g", "primarySellerId": 4 },
                  { "productId": 503, "productName": "Candidate B", "categoryName": "Rau lá", "categoryId": 4, "unitName": "Bó", "price": 17600, "status": true, "availableStock": 18, "origin": "Long An, Viet Nam", "standard": "VietGAP", "preservation": "giu mat", "weight": "400g", "primarySellerId": 9 },
                  { "productId": 504, "productName": "Candidate C", "categoryName": "Rau lá", "categoryId": 4, "unitName": "Bó", "price": 17700, "status": true, "availableStock": 18, "origin": "Can Tho, Viet Nam", "standard": "VietGAP", "preservation": "giu mat", "weight": "400g", "primarySellerId": 4 },
                  { "productId": 505, "productName": "Candidate D", "categoryName": "Rau lá", "categoryId": 4, "unitName": "Bó", "price": 18200, "status": true, "availableStock": 18, "origin": "Da Lat, Viet Nam", "standard": "VietGAP", "preservation": "giu mat", "weight": "400g", "primarySellerId": 12 },
                  { "productId": 506, "productName": "Candidate E", "categoryName": "Rau lá", "categoryId": 4, "unitName": "Bó", "price": 18300, "status": true, "availableStock": 18, "origin": "Da Lat, Viet Nam", "standard": "VietGAP", "preservation": "giu mat", "weight": "400g", "primarySellerId": 4 }
                ]
                """)
            };
        });
        var identityHandler = new RecordingHttpMessageHandler(_ =>
            new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("[]")
            });
        var orderingHandler = new RecordingHttpMessageHandler(request =>
        {
            if (string.Equals(request.RequestUri?.AbsolutePath, "/api/orders/product-insights/stats", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("""
                    [
                      { "productId": 501, "averageRating": 4.7, "reviewCount": 12, "soldCount": 35 },
                      { "productId": 502, "averageRating": 4.9, "reviewCount": 18, "soldCount": 52 },
                      { "productId": 503, "averageRating": 4.7, "reviewCount": 15, "soldCount": 43 },
                      { "productId": 504, "averageRating": 4.8, "reviewCount": 17, "soldCount": 51 },
                      { "productId": 505, "averageRating": 4.4, "reviewCount": 9, "soldCount": 22 },
                      { "productId": 506, "averageRating": 4.0, "reviewCount": 6, "soldCount": 12 }
                    ]
                    """)
                };
            }

            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("[]")
            };
        });
        var controller = CreateController(catalogHandler, identityHandler, orderingHandler);

        var result = await controller.GetSimilarProducts(501, limit: 4);

        var ok = Assert.IsType<OkObjectResult>(result);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value, WebJson));
        var items = json.RootElement.GetProperty("items").EnumerateArray().ToList();
        var productIds = items
            .Select(item => item.GetProperty("productId").GetInt32())
            .ToList();
        var sellerIds = items
            .Select(item => item.GetProperty("primarySellerId").GetInt32())
            .ToList();
        var origins = items
            .Select(item => item.GetProperty("origin").GetString())
            .ToList();

        Assert.Equal(4, items.Count);
        Assert.Equal(new[] { 502, 503 }, productIds.Take(2).ToArray());
        Assert.Contains(504, productIds);
        Assert.Contains(505, productIds);
        Assert.DoesNotContain(506, productIds);
        Assert.Equal(3, sellerIds.Distinct().Count());
        Assert.Equal(3, origins.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public async Task GetSimilarProducts_PrefersHybridSignal_WhenOrderingProvidesCollaborativeMatch()
    {
        var catalogHandler = new RecordingHttpMessageHandler(request =>
        {
            var path = request.RequestUri?.AbsolutePath;
            if (string.Equals(path, "/api/products/104", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("""
                    {
                      "productId": 104,
                      "productName": "Ca chua huu co",
                      "categoryName": "Rau Cu",
                      "categoryId": 3,
                      "unitName": "kg",
                      "price": 32000,
                      "status": true,
                      "availableStock": 20,
                      "origin": "Da Lat",
                      "standard": "VietGAP",
                      "preservation": "giu mat",
                      "weight": "500g"
                    }
                    """)
                };
            }

            Assert.Equal("/api/products", path);
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("""
                [
                  {
                    "productId": 105,
                    "productName": "Ca chua cherry huu co",
                    "categoryName": "Rau Cu",
                    "categoryId": 3,
                    "unitName": "kg",
                    "price": 34000,
                    "status": true,
                    "availableStock": 15,
                    "origin": "Da Lat",
                    "standard": "VietGAP",
                    "preservation": "giu mat",
                    "weight": "550g"
                  },
                  {
                    "productId": 106,
                    "productName": "Cu den tuoi",
                    "categoryName": "Rau Cu",
                    "categoryId": 3,
                    "unitName": "kg",
                    "price": 60000,
                    "status": true,
                    "availableStock": 15,
                    "origin": "Gia Lai",
                    "standard": "GlobalGAP",
                    "preservation": "kho",
                    "weight": "1kg"
                  }
                ]
                """)
            };
        });
        var identityHandler = new RecordingHttpMessageHandler(_ =>
            new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("[]")
            });
        var orderingHandler = new RecordingHttpMessageHandler(request =>
        {
            var path = request.RequestUri?.AbsolutePath;
            if (string.Equals(path, "/api/orders/product-insights/stats", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("""
                    [
                      { "productId": 104, "averageRating": 4.7, "reviewCount": 12, "soldCount": 35 },
                      { "productId": 105, "averageRating": 4.9, "reviewCount": 14, "soldCount": 41 },
                      { "productId": 106, "averageRating": 4.1, "reviewCount": 4, "soldCount": 9 }
                    ]
                    """)
                };
            }

            Assert.Equal("/api/orders/product-insights/similar", path);
            var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("""
                [
                  {
                    "productId": 106,
                    "coPurchaseOrderCount": 2,
                    "coViewSessionCount": 1,
                    "coClickSessionCount": 1,
                    "collaborativeScore": 160
                  }
                ]
                """)
            };
            response.Headers.Add("X-Recommendation-Signal-Source", "materialized_cf_v1");
            return response;
        });
        var controller = CreateController(catalogHandler, identityHandler, orderingHandler);

        var result = await controller.GetSimilarProducts(104, limit: 2);

        var ok = Assert.IsType<OkObjectResult>(result);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value, WebJson));
        Assert.Equal("hybrid_similar_v1", json.RootElement.GetProperty("algorithm").GetString());
        Assert.Equal("catalog_content_v1", json.RootElement.GetProperty("contentSignalSource").GetString());
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("fallbackReason").ValueKind);
        Assert.Equal("materialized_cf_v1", json.RootElement.GetProperty("collaborativeSignalSource").GetString());
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("collaborativeFallbackReason").ValueKind);
        Assert.Equal("materialized_cf_v1", json.RootElement.GetProperty("signalSource").GetString());
        var signalBreakdown = json.RootElement.GetProperty("signalBreakdown");
        Assert.Equal("catalog_content_v1", signalBreakdown.GetProperty("content").GetString());
        Assert.Equal("materialized_cf_v1", signalBreakdown.GetProperty("overall").GetString());
        Assert.Equal(JsonValueKind.Null, signalBreakdown.GetProperty("overallReason").ValueKind);
        Assert.Equal(JsonValueKind.Null, signalBreakdown.GetProperty("preference").ValueKind);
        Assert.Equal(JsonValueKind.Null, signalBreakdown.GetProperty("preferenceReason").ValueKind);
        Assert.Equal("materialized_cf_v1", signalBreakdown.GetProperty("collaborative").GetString());
        Assert.Equal(JsonValueKind.Null, signalBreakdown.GetProperty("collaborativeReason").ValueKind);
        var items = json.RootElement.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(106, items[0].GetProperty("productId").GetInt32());
        Assert.Contains("Hay mua cùng", items[0].GetProperty("recommendationReason").GetString());
    }

    [Fact]
    public async Task GetSimilarProducts_UsesUserSellerAndCategoryScores_WhenCollaborativeSignalsAreMissing()
    {
        var catalogHandler = new RecordingHttpMessageHandler(request =>
        {
            var path = request.RequestUri?.AbsolutePath;
            if (string.Equals(path, "/api/products/204", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("""
                    {
                      "productId": 204,
                      "productName": "Rau cai huu co",
                      "categoryName": "Rau la",
                      "categoryId": 1,
                      "unitName": "kg",
                      "price": 26000,
                      "status": true,
                      "availableStock": 24,
                      "origin": "Da Lat",
                      "standard": "VietGAP",
                      "preservation": "giu mat",
                      "weight": "500g",
                      "primarySellerId": 2
                    }
                    """)
                };
            }

            Assert.Equal("/api/products", path);
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("""
                [
                  {
                    "productId": 205,
                    "productName": "Rau cai xanh huu co",
                    "categoryName": "Rau la",
                    "categoryId": 1,
                    "unitName": "kg",
                    "price": 27500,
                    "status": true,
                    "availableStock": 20,
                    "origin": "Da Lat",
                    "standard": "VietGAP",
                    "preservation": "giu mat",
                    "weight": "520g",
                    "primarySellerId": 2
                  },
                  {
                    "productId": 206,
                    "productName": "Rau lang huu co",
                    "categoryName": "Rau la",
                    "categoryId": 1,
                    "unitName": "kg",
                    "price": 25500,
                    "status": true,
                    "availableStock": 18,
                    "origin": "Da Lat",
                    "standard": "VietGAP",
                    "preservation": "giu mat",
                    "weight": "510g",
                    "primarySellerId": 4
                  }
                ]
                """)
            };
        });
        var identityHandler = new RecordingHttpMessageHandler(_ =>
            new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("""
                [
                  {
                    "sellerId": 2,
                    "shopName": "Nong trai quen",
                    "addressSummary": "Da Lat, Lam Dong"
                  },
                  {
                    "sellerId": 4,
                    "shopName": "Shop moi",
                    "addressSummary": "Da Lat, Lam Dong"
                  }
                ]
                """)
            });
        var orderingHandler = new RecordingHttpMessageHandler(request =>
        {
            var path = request.RequestUri?.AbsolutePath;
            if (string.Equals(path, "/api/orders/product-insights/stats", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("""
                    [
                      { "productId": 204, "averageRating": 4.5, "reviewCount": 10, "soldCount": 33 },
                      { "productId": 205, "averageRating": 4.6, "reviewCount": 11, "soldCount": 30 },
                      { "productId": 206, "averageRating": 4.7, "reviewCount": 12, "soldCount": 35 }
                    ]
                    """)
                };
            }

            if (string.Equals(path, "/api/orders/product-insights/similar", StringComparison.Ordinal))
            {
                var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("[]")
                };
                response.Headers.Add("X-Recommendation-Fallback-Reason", "no_materialized_collaborative_candidates");
                return response;
            }

            if (string.Equals(path, "/api/orders/product-insights/user-category", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("""
                    [
                      {
                        "categoryId": 1,
                        "categoryName": "Rau la",
                        "viewCount": 3,
                        "searchClickCount": 2,
                        "recommendationClickCount": 1,
                        "purchaseCount": 4,
                        "userCategoryScore": 260,
                        "lastInteractedAtUtc": "2026-04-02T09:00:00Z"
                      }
                    ]
                    """)
                };
            }

            if (string.Equals(path, "/api/orders/product-insights/user-seller", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("""
                    [
                      {
                        "sellerId": 2,
                        "viewCount": 4,
                        "searchClickCount": 2,
                        "purchaseCount": 3,
                        "userSellerScore": 380,
                        "lastInteractedAtUtc": "2026-04-02T10:00:00Z"
                      }
                    ]
                    """)
                };
            }

            throw new Xunit.Sdk.XunitException($"Unexpected Ordering path: {path}");
        });
        var controller = CreateController(catalogHandler, identityHandler, orderingHandler);
        controller.Request.Headers.Authorization = "Bearer test-token";

        var result = await controller.GetSimilarProducts(204, limit: 2);

        var ok = Assert.IsType<OkObjectResult>(result);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value, WebJson));
        Assert.Equal("hybrid_similar_v1", json.RootElement.GetProperty("algorithm").GetString());
        Assert.Equal("materialized_category_pref_v1+materialized_seller_pref_v1", json.RootElement.GetProperty("preferenceSignalSource").GetString());
        Assert.Equal("materialized_category_pref_v1+materialized_seller_pref_v1", json.RootElement.GetProperty("signalSource").GetString());
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("fallbackReason").ValueKind);
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("collaborativeSignalSource").ValueKind);
        Assert.Equal("no_materialized_collaborative_candidates", json.RootElement.GetProperty("collaborativeFallbackReason").GetString());
        var signalBreakdown = json.RootElement.GetProperty("signalBreakdown");
        Assert.Equal("materialized_category_pref_v1+materialized_seller_pref_v1", signalBreakdown.GetProperty("preference").GetString());
        Assert.Equal(JsonValueKind.Null, signalBreakdown.GetProperty("preferenceReason").ValueKind);
        Assert.Equal(JsonValueKind.Null, signalBreakdown.GetProperty("collaborative").ValueKind);
        var items = json.RootElement.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(205, items[0].GetProperty("productId").GetInt32());
        var reason = items[0].GetProperty("recommendationReason").GetString();
        Assert.Contains("Từ shop bạn hay quay lại", reason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Bạn hay mua nhóm", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetTrendingProducts_UsesNonOverlapItemsFirst_WhenNoOverlapNeeded()
    {
        var controller = CreateTrendingController(productIds: [1, 2, 3, 4, 5, 6]);

        var result = await controller.GetTrendingProducts(
            limit: 4,
            personalizedProductIds: [100, 101],
            excludeProductIds: null);

        var root = ReadOkJson(result);
        var items = root.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(4, items.Count);
        Assert.Equal(0, root.GetProperty("overlapCount").GetInt32());
        Assert.Equal(0d, root.GetProperty("overlapRatio").GetDouble());
        Assert.DoesNotContain(items, item => item.GetProperty("productId").GetInt32() is 100 or 101);
    }

    [Fact]
    public async Task GetTrendingProducts_AllowsPartialOverlap_WhenNonOverlapItemsAreInsufficient()
    {
        var controller = CreateTrendingController(productIds: [1, 2, 3, 4, 5]);

        var result = await controller.GetTrendingProducts(
            limit: 5,
            personalizedProductIds: [1],
            excludeProductIds: null);

        var root = ReadOkJson(result);
        var items = root.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(5, items.Count);
        Assert.Equal(1, root.GetProperty("overlapCount").GetInt32());
        Assert.Equal(0.2d, root.GetProperty("overlapRatio").GetDouble());
        Assert.Equal(1, items.Count(item => item.GetProperty("productId").GetInt32() == 1));
    }

    [Fact]
    public async Task GetTrendingProducts_ForcesOverlap_WhenNeededToFillList()
    {
        var controller = CreateTrendingController(productIds: [1, 2, 3, 4, 5]);

        var result = await controller.GetTrendingProducts(
            limit: 5,
            personalizedProductIds: [1, 2, 3],
            excludeProductIds: null);

        var root = ReadOkJson(result);
        var items = root.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(5, items.Count);
        Assert.Equal(3, root.GetProperty("overlapCount").GetInt32());
        Assert.Equal(0.6d, root.GetProperty("overlapRatio").GetDouble());
        Assert.Equal(3, items.Count(item => new[] { 1, 2, 3 }.Contains(item.GetProperty("productId").GetInt32())));
    }

    [Fact]
    public async Task GetNewArrivalsProducts_ReturnsLatestActiveInStockItems_WithCreatedAt()
    {
        var catalogHandler = new RecordingHttpMessageHandler(_ =>
            new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("""
                [
                  { "productId": 10, "productName": "Newest", "sku": "N10", "price": 10000, "status": true, "stockQuantity": 20, "reservedStock": 0, "availableStock": 20, "onHandStock": 20, "imageFileName": "no-image.png", "createdDate": "2026-04-03T00:00:00Z", "isManuallyDisabled": false, "categoryId": 1, "categoryName": "Rau", "unitId": 1, "unitName": "kg", "unitSymbol": "kg" },
                  { "productId": 11, "productName": "Excluded", "sku": "N11", "price": 10000, "status": true, "stockQuantity": 20, "reservedStock": 0, "availableStock": 20, "onHandStock": 20, "imageFileName": "no-image.png", "createdDate": "2026-04-02T00:00:00Z", "isManuallyDisabled": false, "categoryId": 1, "categoryName": "Rau", "unitId": 1, "unitName": "kg", "unitSymbol": "kg" },
                  { "productId": 12, "productName": "Older", "sku": "N12", "price": 10000, "status": true, "stockQuantity": 20, "reservedStock": 0, "availableStock": 20, "onHandStock": 20, "imageFileName": "no-image.png", "createdDate": "2026-04-01T00:00:00Z", "isManuallyDisabled": false, "categoryId": 1, "categoryName": "Rau", "unitId": 1, "unitName": "kg", "unitSymbol": "kg" },
                  { "productId": 13, "productName": "Out", "sku": "N13", "price": 10000, "status": true, "stockQuantity": 0, "reservedStock": 0, "availableStock": 0, "onHandStock": 0, "imageFileName": "no-image.png", "createdDate": "2026-04-04T00:00:00Z", "isManuallyDisabled": false, "categoryId": 1, "categoryName": "Rau", "unitId": 1, "unitName": "kg", "unitSymbol": "kg" },
                  { "productId": 14, "productName": "Inactive", "sku": "N14", "price": 10000, "status": false, "stockQuantity": 20, "reservedStock": 0, "availableStock": 20, "onHandStock": 20, "imageFileName": "no-image.png", "createdDate": "2026-04-05T00:00:00Z", "isManuallyDisabled": false, "categoryId": 1, "categoryName": "Rau", "unitId": 1, "unitName": "kg", "unitSymbol": "kg" }
                ]
                """)
            });
        var identityHandler = new RecordingHttpMessageHandler(_ =>
            new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("[]")
            });
        var orderingHandler = new RecordingHttpMessageHandler(_ =>
            new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("[]")
            });
        var controller = CreateController(catalogHandler, identityHandler, orderingHandler);

        var result = await controller.GetNewArrivalsProducts(limit: 12, excludeProductIds: [11]);

        var root = ReadOkJson(result);
        Assert.Equal("home_new_arrivals", root.GetProperty("placement").GetString());
        var items = root.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal([10, 12], items.Select(item => item.GetProperty("productId").GetInt32()).ToArray());
        Assert.Equal("2026-04-03T00:00:00Z", items[0].GetProperty("createdAt").GetDateTime().ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ"));
    }

    [Fact]
    public async Task GetBestSellersProducts_ReturnsActiveInStockItems_OrderedByRecentWeightedBestSellerScore()
    {
        var catalogHandler = new RecordingHttpMessageHandler(_ =>
            new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("""
                [
                  { "productId": 20, "productName": "Sold 20", "sku": "B20", "price": 10000, "status": true, "stockQuantity": 20, "reservedStock": 0, "availableStock": 20, "onHandStock": 20, "imageFileName": "no-image.png", "createdDate": "2026-04-03T00:00:00Z", "isManuallyDisabled": false, "categoryId": 1, "categoryName": "Rau", "unitId": 1, "unitName": "kg", "unitSymbol": "kg" },
                  { "productId": 21, "productName": "Excluded", "sku": "B21", "price": 10000, "status": true, "stockQuantity": 20, "reservedStock": 0, "availableStock": 20, "onHandStock": 20, "imageFileName": "no-image.png", "createdDate": "2026-04-02T00:00:00Z", "isManuallyDisabled": false, "categoryId": 1, "categoryName": "Rau", "unitId": 1, "unitName": "kg", "unitSymbol": "kg" },
                  { "productId": 22, "productName": "Sold 22", "sku": "B22", "price": 10000, "status": true, "stockQuantity": 20, "reservedStock": 0, "availableStock": 20, "onHandStock": 20, "imageFileName": "no-image.png", "createdDate": "2026-04-01T00:00:00Z", "isManuallyDisabled": false, "categoryId": 1, "categoryName": "Rau", "unitId": 1, "unitName": "kg", "unitSymbol": "kg" },
                  { "productId": 23, "productName": "Out", "sku": "B23", "price": 10000, "status": true, "stockQuantity": 0, "reservedStock": 0, "availableStock": 0, "onHandStock": 0, "imageFileName": "no-image.png", "createdDate": "2026-04-04T00:00:00Z", "isManuallyDisabled": false, "categoryId": 1, "categoryName": "Rau", "unitId": 1, "unitName": "kg", "unitSymbol": "kg" },
                  { "productId": 24, "productName": "Inactive", "sku": "B24", "price": 10000, "status": false, "stockQuantity": 20, "reservedStock": 0, "availableStock": 20, "onHandStock": 20, "imageFileName": "no-image.png", "createdDate": "2026-04-05T00:00:00Z", "isManuallyDisabled": false, "categoryId": 1, "categoryName": "Rau", "unitId": 1, "unitName": "kg", "unitSymbol": "kg" },
                  { "productId": 25, "productName": "Zero", "sku": "B25", "price": 10000, "status": true, "stockQuantity": 20, "reservedStock": 0, "availableStock": 20, "onHandStock": 20, "imageFileName": "no-image.png", "createdDate": "2026-03-31T00:00:00Z", "isManuallyDisabled": false, "categoryId": 1, "categoryName": "Rau", "unitId": 1, "unitName": "kg", "unitSymbol": "kg" }
                ]
                """)
            });
        var identityHandler = new RecordingHttpMessageHandler(_ =>
            new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("[]")
            });
        var orderingHandler = new RecordingHttpMessageHandler(_ =>
            new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("""
                [
                  { "productId": 20, "averageRating": 4.2, "reviewCount": 8, "recentSoldCount": 0, "soldCount": 100 },
                  { "productId": 21, "averageRating": 4.9, "reviewCount": 20, "recentSoldCount": 90, "soldCount": 90 },
                  { "productId": 22, "averageRating": 4.5, "reviewCount": 12, "recentSoldCount": 40, "soldCount": 80 },
                  { "productId": 23, "averageRating": 5.0, "reviewCount": 30, "recentSoldCount": 100, "soldCount": 100 },
                  { "productId": 24, "averageRating": 4.8, "reviewCount": 11, "recentSoldCount": 45, "soldCount": 70 },
                  { "productId": 25, "averageRating": 4.0, "reviewCount": 3, "recentSoldCount": 0, "soldCount": 0 }
                ]
                """)
            });
        var controller = CreateController(catalogHandler, identityHandler, orderingHandler);

        var result = await controller.GetBestSellersProducts(limit: 12, excludeProductIds: [21]);

        var root = ReadOkJson(result);
        Assert.Equal("home_best_sellers", root.GetProperty("placement").GetString());
        var items = root.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal([22, 20], items.Select(item => item.GetProperty("productId").GetInt32()).ToArray());
        Assert.Contains("40 lượt bán gần đây", items[0].GetProperty("recommendationReason").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetBestSellersProducts_RelaxesExcludedIds_WhenAllBestSellersWereAlreadyShownElsewhere()
    {
        var catalogHandler = new RecordingHttpMessageHandler(_ =>
            new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("""
                [
                  { "productId": 20, "productName": "Sold 20", "sku": "B20", "price": 10000, "status": true, "stockQuantity": 20, "reservedStock": 0, "availableStock": 20, "onHandStock": 20, "imageFileName": "no-image.png", "createdDate": "2026-04-03T00:00:00Z", "isManuallyDisabled": false, "categoryId": 1, "categoryName": "Rau", "unitId": 1, "unitName": "kg", "unitSymbol": "kg" },
                  { "productId": 21, "productName": "Sold 21", "sku": "B21", "price": 10000, "status": true, "stockQuantity": 20, "reservedStock": 0, "availableStock": 20, "onHandStock": 20, "imageFileName": "no-image.png", "createdDate": "2026-04-02T00:00:00Z", "isManuallyDisabled": false, "categoryId": 1, "categoryName": "Rau", "unitId": 1, "unitName": "kg", "unitSymbol": "kg" }
                ]
                """)
            });
        var identityHandler = new RecordingHttpMessageHandler(_ =>
            new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("[]")
            });
        var orderingHandler = new RecordingHttpMessageHandler(_ =>
            new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("""
                [
                  { "productId": 20, "averageRating": 4.2, "reviewCount": 8, "recentSoldCount": 20, "soldCount": 100 },
                  { "productId": 21, "averageRating": 4.9, "reviewCount": 20, "recentSoldCount": 90, "soldCount": 90 }
                ]
                """)
            });
        var controller = CreateController(catalogHandler, identityHandler, orderingHandler);

        var result = await controller.GetBestSellersProducts(limit: 12, excludeProductIds: [20, 21]);

        var root = ReadOkJson(result);
        var items = root.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal([21, 20], items.Select(item => item.GetProperty("productId").GetInt32()).ToArray());
    }

    private static JsonElement ReadOkJson(IActionResult result)
    {
        var ok = Assert.IsType<OkObjectResult>(result);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value, WebJson));
        return json.RootElement.Clone();
    }

    private static BffCatalogController CreateTrendingController(int[] productIds)
    {
        var catalogHandler = new RecordingHttpMessageHandler(_ =>
            new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(BuildTrendingCatalogPayload(productIds))
            });
        var identityHandler = new RecordingHttpMessageHandler(_ =>
            new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("[]")
            });
        var orderingHandler = new RecordingHttpMessageHandler(request =>
        {
            var path = request.RequestUri?.AbsolutePath;
            if (string.Equals(path, "/api/orders/product-insights/stats", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent(BuildTrendingStatsPayload(productIds))
                };
            }

            throw new Xunit.Sdk.XunitException($"Unexpected Ordering path: {path}");
        });

        return CreateController(catalogHandler, identityHandler, orderingHandler);
    }

    private static string BuildTrendingCatalogPayload(IReadOnlyList<int> productIds)
    {
        var items = productIds.Select((productId, index) => new
        {
            productId,
            productName = $"Trending {productId}",
            sku = $"TR-{productId}",
            price = 10000 + productId,
            status = true,
            stockQuantity = 40,
            reservedStock = 0,
            availableStock = 40,
            onHandStock = 40,
            imageFileName = "no-image.png",
            createdDate = DateTime.UtcNow.AddDays(-index).ToString("yyyy-MM-ddTHH:mm:ssZ"),
            isManuallyDisabled = false,
            categoryId = 1,
            categoryName = "Rau cu",
            unitId = 1,
            unitName = "kg",
            unitSymbol = "kg"
        });

        return JsonSerializer.Serialize(items, WebJson);
    }

    private static string BuildTrendingStatsPayload(IReadOnlyList<int> productIds)
    {
        var items = productIds.Select((productId, index) => new
        {
            productId,
            averageRating = Math.Max(1m, 5m - (index * 0.1m)),
            reviewCount = 100 - index,
            soldCount = 1000 - index
        });

        return JsonSerializer.Serialize(items, WebJson);
    }

    private static BffCatalogController CreateController(
        RecordingHttpMessageHandler catalogHandler,
        RecordingHttpMessageHandler identityHandler,
        RecordingHttpMessageHandler? orderingHandler = null)
    {
        var catalogClient = new HttpClient(catalogHandler)
        {
            BaseAddress = new Uri("https://catalog.test")
        };
        var identityClient = new HttpClient(identityHandler)
        {
            BaseAddress = new Uri("https://identity.test")
        };
        var orderingClient = new HttpClient(orderingHandler ?? new RecordingHttpMessageHandler(_ =>
            new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("[]")
            }))
        {
            BaseAddress = new Uri("https://ordering.test")
        };

        var httpContext = new DefaultHttpContext();
        httpContext.Features.Set<ISessionFeature>(new SessionFeature
        {
            Session = new TestSession()
        });

        return new BffCatalogController(new NamedHttpClientFactory(new Dictionary<string, HttpClient>(StringComparer.OrdinalIgnoreCase)
        {
            ["Catalog"] = catalogClient,
            ["Identity"] = identityClient,
            ["Ordering"] = orderingClient
        }))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = httpContext
            }
        };
    }

    private sealed class NamedHttpClientFactory(IReadOnlyDictionary<string, HttpClient> clients) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => clients[name];
    }

    private sealed class RecordingHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(responder(request));
    }

    private sealed class SessionFeature : ISessionFeature
    {
        public ISession Session { get; set; } = null!;
    }

    private sealed class TestSession : ISession
    {
        private readonly Dictionary<string, byte[]> _store = new(StringComparer.Ordinal);

        public IEnumerable<string> Keys => _store.Keys;

        public string Id { get; } = Guid.NewGuid().ToString("N");

        public bool IsAvailable => true;

        public void Clear() => _store.Clear();

        public Task CommitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public void Remove(string key) => _store.Remove(key);

        public void Set(string key, byte[] value) => _store[key] = value;

        public bool TryGetValue(string key, out byte[] value) => _store.TryGetValue(key, out value!);
    }
}
