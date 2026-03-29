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
                    Content = new StringContent("""
                    [
                      { "productId": 501, "averageRating": 4.4, "reviewCount": 12, "soldCount": 31 },
                      { "productId": 502, "averageRating": 4.0, "reviewCount": 6, "soldCount": 18 }
                    ]
                    """)
                };
            }

            Assert.Equal("/api/orders/product-insights/search-ranking", path);
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("""
                [
                  {
                    "productId": 502,
                    "searchClickCount": 3,
                    "searchClickSessionCount": 2,
                    "searchViewSessionCount": 2,
                    "searchRecommendationClickCount": 1,
                    "hybridSearchScore": 240
                  }
                ]
                """)
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
            pageSize: 12);

        var ok = Assert.IsType<OkObjectResult>(result);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value, WebJson));
        Assert.Equal("hybrid_search_v1", json.RootElement.GetProperty("rankingAlgorithm").GetString());
        Assert.Equal("catalog_keyword_v1", json.RootElement.GetProperty("contentSignalSource").GetString());
        var items = json.RootElement.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(502, items[0].GetProperty("productId").GetInt32());
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
        var items = json.RootElement.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(2, items.Count);
        Assert.Equal(104, items[0].GetProperty("productId").GetInt32());
        Assert.Equal(220, items[1].GetProperty("productId").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(items[0].GetProperty("recommendationReason").GetString()));
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
                response.Headers.Add("X-Recommendation-Signal-Source", "materialized");
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

        var result = await controller.GetHomeRecommendations(limit: 3);

        var ok = Assert.IsType<OkObjectResult>(result);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value, WebJson));
        Assert.Equal("hybrid_home_v1", json.RootElement.GetProperty("algorithm").GetString());
        Assert.Equal("catalog_content_v1", json.RootElement.GetProperty("contentSignalSource").GetString());
        Assert.Equal("materialized", json.RootElement.GetProperty("preferenceSignalSource").GetString());
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("collaborativeSignalSource").ValueKind);
        Assert.Equal("materialized", json.RootElement.GetProperty("signalSource").GetString());
        var items = json.RootElement.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(3, items.Count);
        Assert.Equal(105, items[0].GetProperty("productId").GetInt32());
        Assert.Contains("Bạn từng bấm từ tìm kiếm", items[0].GetProperty("recommendationReason").GetString());
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
                        "collaborativeScore": 180
                      }
                    ]
                    """)
                };
                response.Headers.Add("X-Recommendation-Signal-Source", "materialized");
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
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("preferenceSignalSource").ValueKind);
        Assert.Equal("materialized", json.RootElement.GetProperty("collaborativeSignalSource").GetString());
        Assert.Equal("materialized", json.RootElement.GetProperty("signalSource").GetString());
        var items = json.RootElement.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(220, items[0].GetProperty("productId").GetInt32());
        Assert.Contains("Hay được mua cùng", items[0].GetProperty("recommendationReason").GetString());
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
            new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("""
                [
                  { "productId": 104, "averageRating": 4.7, "reviewCount": 12, "soldCount": 35 },
                  { "productId": 105, "averageRating": 4.9, "reviewCount": 14, "soldCount": 41 },
                  { "productId": 106, "averageRating": 4.1, "reviewCount": 4, "soldCount": 9 }
                ]
                """)
            });
        var controller = CreateController(catalogHandler, identityHandler, orderingHandler);

        var result = await controller.GetSimilarProducts(104, limit: 2);

        var ok = Assert.IsType<OkObjectResult>(result);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value, WebJson));
        Assert.Equal("product_similar", json.RootElement.GetProperty("placement").GetString());
        Assert.Equal(104, json.RootElement.GetProperty("seedProductId").GetInt32());
        Assert.Equal("catalog_content_v1", json.RootElement.GetProperty("contentSignalSource").GetString());
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("collaborativeSignalSource").ValueKind);
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("signalSource").ValueKind);
        var items = json.RootElement.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(2, items.Count);
        Assert.Equal(105, items[0].GetProperty("productId").GetInt32());
        Assert.Contains("Cùng", items[0].GetProperty("recommendationReason").GetString());
        Assert.Contains("độ ngọt", items[0].GetProperty("recommendationReason").GetString());
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
            response.Headers.Add("X-Recommendation-Signal-Source", "materialized");
            return response;
        });
        var controller = CreateController(catalogHandler, identityHandler, orderingHandler);

        var result = await controller.GetSimilarProducts(104, limit: 2);

        var ok = Assert.IsType<OkObjectResult>(result);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value, WebJson));
        Assert.Equal("hybrid_similar_v1", json.RootElement.GetProperty("algorithm").GetString());
        Assert.Equal("catalog_content_v1", json.RootElement.GetProperty("contentSignalSource").GetString());
        Assert.Equal("materialized", json.RootElement.GetProperty("collaborativeSignalSource").GetString());
        Assert.Equal("materialized", json.RootElement.GetProperty("signalSource").GetString());
        var items = json.RootElement.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(106, items[0].GetProperty("productId").GetInt32());
        Assert.Contains("Hay mua cùng", items[0].GetProperty("recommendationReason").GetString());
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
