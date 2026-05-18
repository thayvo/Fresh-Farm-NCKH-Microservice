        (() => {
            const configNode = document.getElementById("searchPageConfig");
            const initialKeyword = configNode?.dataset.initialKeyword || "";
            const isAuthenticated = (configNode?.dataset.isAuthenticated || "false") === "true";
            const isBuyerEligible = (configNode?.dataset.isBuyerEligible || "false") === "true";
            const endpointBase = configNode?.dataset.endpointBase || "/bff/product-search";
            const categoryEndpoint = configNode?.dataset.categoryEndpoint || "/bff/categories";
            const fallbackImage = configNode?.dataset.fallbackImage || "/uploads/products/no-image.png";
            const uploadRoot = configNode?.dataset.uploadRoot || "/uploads/products/";
            const urlParams = new URLSearchParams(window.location.search);
            const initialCategoryIds = urlParams
                .getAll("category")
                .map((value) => Number(value))
                .filter((value) => Number.isFinite(value) && value > 0);
            const initialSellerIds = [...urlParams.getAll("seller"), ...urlParams.getAll("shop")]
                .map((value) => Number(value))
                .filter((value) => Number.isFinite(value) && value > 0);
            const initialAvailability = urlParams.getAll("availability").map((value) => value.trim()).filter((value) => value.length > 0);
            const initialDeliveryScopes = urlParams.getAll("deliveryScope").map((value) => value.trim()).filter((value) => value.length > 0);
            const initialOrigins = urlParams.getAll("origin").map((value) => value.trim()).filter((value) => value.length > 0);
            const initialStandards = urlParams.getAll("standard").map((value) => value.trim()).filter((value) => value.length > 0);
            const initialUnits = urlParams.getAll("unit").map((value) => value.trim()).filter((value) => value.length > 0);
            const initialMinPrice = urlParams.get("minPrice");
            const initialMaxPrice = urlParams.get("maxPrice");
            const initialMinRating = urlParams.get("minRating");
            const initialSort = (urlParams.get("sort") || "related").trim();
            const initialPage = Number(urlParams.get("page") || "1");
            const initialPreset = (urlParams.get("preset") || "").trim();
            const marketplaceShortcutKeywords = new Set([
                "rau sach",
                "trai cay",
                "nong san huu co",
                "thuc pham tuoi",
                "giao nhanh"
            ]);
            const state = {
                keyword: (initialKeyword || "").trim(),
                availableCategories: [],
                availableShops: [],
                availableAvailability: [],
                availableDeliveryScopes: [],
                availableLocations: [],
                availableUnits: [],
                availableStandards: [],
                allProducts: [],
                relatedProducts: [],
                totalCount: 0,
                totalPages: 1,
                chatSummaries: new Map(),
                chatSummaryRefreshTimer: null,
                minPrice: initialMinPrice && Number(initialMinPrice) >= 0 ? Number(initialMinPrice) : null,
                maxPrice: initialMaxPrice && Number(initialMaxPrice) >= 0 ? Number(initialMaxPrice) : null,
                minRating: initialMinRating && Number(initialMinRating) > 0 ? Number(initialMinRating) : 0,
                selectedCategoryIds: new Set(initialCategoryIds),
                selectedSellerIds: new Set(initialSellerIds),
                selectedAvailability: new Set(initialAvailability),
                selectedDeliveryScopes: new Set(initialDeliveryScopes),
                selectedLocations: new Set(initialOrigins),
                selectedUnits: new Set(initialUnits),
                selectedCertifications: new Set(initialStandards),
                presetAvailabilityKeys: new Set(),
                presetDeliveryScopeKeys: new Set(),
                presetAppliedSort: "",
                activePreset: initialPreset,
                sortBy: initialSort || "related",
                page: Number.isFinite(initialPage) && initialPage > 0 ? Math.floor(initialPage) : 1,
                pageSize: 12,
                latestSearchEventId: null
            };
            const relatedRecommendationPlacement = "search_related";
            const relatedRecommendationAlgorithm = "search_related_heuristic";
            let relatedRecommendationRunId = "";
            const relatedImpressionIds = new Map();

            const grid = document.getElementById("resultGrid");
            const emptyNode = document.getElementById("resultEmpty");
            const categoryWrap = document.getElementById("categoryFilterWrap");
            const shopWrap = document.getElementById("shopFilterWrap");
            const availabilityWrap = document.getElementById("availabilityFilterWrap");
            const deliveryScopeWrap = document.getElementById("deliveryScopeFilterWrap");
            const locationWrap = document.getElementById("locationFilterWrap");
            const unitWrap = document.getElementById("unitFilterWrap");
            const certWrap = document.getElementById("certFilterWrap");
            const minPriceInput = document.getElementById("minPriceInput");
            const maxPriceInput = document.getElementById("maxPriceInput");
            const applyPriceBtn = document.getElementById("applyPriceBtn");
            const clearFilterBtn = document.getElementById("clearFilterBtn");
            const resultCountNode = document.getElementById("searchResultCount");
            const sortSummaryNode = document.getElementById("sortSummary");
            const paginationNode = document.getElementById("searchPagination");
            const paginationMetaNode = document.getElementById("searchPaginationMeta");
            const paginationActionsNode = document.getElementById("searchPaginationActions");
            const relatedWrap = document.getElementById("shopRelatedWrap");
            const keywordLabel = document.getElementById("searchKeywordLabel");
            const relatedKeywordLabel = document.getElementById("relatedKeywordLabel");
            const activeFilterSummary = document.getElementById("activeFilterSummary");
            const categoryPillWrap = document.getElementById("searchCategoryPills");
            const presetPillWrap = document.getElementById("searchPresetPills");
            const antiForgeryToken = document.querySelector("#searchAntiForgeryForm input[name='__RequestVerificationToken']")?.value ?? "";
            const appHelpers = window.FreshFarmApp;

            const searchForm = document.getElementById("headerSearchForm");
            const searchInput = document.getElementById("headerSearchInput");

            if (!state.keyword) {
                const keywordFromUrl = urlParams.get("q");
                state.keyword = (keywordFromUrl || "").trim();
            }

            const toNumber = (value, fallback = 0) => {
                const n = Number(value);
                return Number.isFinite(n) ? n : fallback;
            };

            const vnd = (value) => toNumber(value, 0).toLocaleString("vi-VN") + " ₫";

            const normalizeText = (value) => (value || "")
                .toString()
                .trim()
                .toLowerCase()
                .normalize("NFD")
                .replace(/[\u0300-\u036f]/g, "");

            const escapeHtml = (value) => {
                const text = value ?? "";
                return text
                    .replaceAll("&", "&amp;")
                    .replaceAll("<", "&lt;")
                    .replaceAll(">", "&gt;")
                    .replaceAll('"', "&quot;")
                    .replaceAll("'", "&#39;");
            };

            const resolveImageUrl = (raw) => {
                const imageFileName = (raw.imageFileName ?? raw.ImageFileName ?? "").toString().trim();
                if (!imageFileName || imageFileName.toLowerCase() === "no-image.png") {
                    return fallbackImage;
                }

                if (/^(https?:)?\/\//i.test(imageFileName) || imageFileName.startsWith("/")) {
                    return imageFileName;
                }

                return `${uploadRoot}${imageFileName}`;
            };

            const normalizeProduct = (raw) => {
                const productId = toNumber(raw.productID ?? raw.productId ?? raw.ProductID ?? raw.ProductId, 0);
                const productName = (raw.productName ?? raw.ProductName ?? "Nông sản chưa đặt tên").toString();
                const categoryId = toNumber(raw.categoryId ?? raw.CategoryId, 0);
                const categoryName = (raw.categoryName ?? raw.CategoryName ?? "Chưa phân loại").toString();
                const unitName = (raw.unitName ?? raw.UnitName ?? "đơn vị").toString();
                const price = toNumber(raw.price ?? raw.Price, 0);
                const rating = toNumber(raw.averageRating ?? raw.AverageRating, 0);
                const sold = toNumber(raw.soldCount ?? raw.SoldCount, 0);
                const origin = (raw.origin ?? raw.Origin ?? "").toString().trim();
                const standard = (raw.standard ?? raw.Standard ?? "").toString().trim();
                const preservation = (raw.preservation ?? raw.Preservation ?? "").toString().trim();
                const weight = (raw.weight ?? raw.Weight ?? "").toString().trim();
                const image = resolveImageUrl(raw);
                const certifications = [standard].filter((value) => !!value);
                const recommendationReason = (raw.recommendationReason ?? raw.RecommendationReason ?? "").toString().trim();
                const recommendationTags = Array.isArray(raw.recommendationTags ?? raw.RecommendationTags)
                    ? (raw.recommendationTags ?? raw.RecommendationTags)
                        .map((tag) => (tag ?? "").toString().trim())
                        .filter((tag) => tag.length > 0)
                    : [];
                const rawSeasonalityScore = raw.seasonalityScore ?? raw.SeasonalityScore;
                const seasonalityScore = rawSeasonalityScore === null || rawSeasonalityScore === undefined
                    ? null
                    : toNumber(rawSeasonalityScore, 0);
                const seasonalityLabel = (raw.seasonalityLabel ?? raw.SeasonalityLabel ?? "").toString().trim();
                const seasonalityBadgeLabel = (raw.seasonalityBadgeLabel ?? raw.SeasonalityBadgeLabel ?? "").toString().trim();

                return {
                    productId,
                    productName,
                    categoryId,
                    categoryName,
                    unitName,
                    primarySellerId: toNumber(raw.primarySellerId ?? raw.PrimarySellerId, 0),
                stockQuantity: toNumber(raw.availableStock ?? raw.AvailableStock ?? raw.stockQuantity ?? raw.StockQuantity, 0),
                    price,
                    rating,
                    sold,
                    origin,
                    standard,
                    preservation,
                    weight,
                availabilityKey: resolveAvailability(raw.availableStock ?? raw.AvailableStock ?? raw.stockQuantity ?? raw.StockQuantity),
                    location: origin,
                    image,
                    certifications,
                    recommendationReason,
                    recommendationTags,
                    seasonalityScore,
                    seasonalityLabel,
                    seasonalityBadgeLabel
                };
            };

            const shouldShowSeasonalityBadge = (product) => {
                const label = (product?.seasonalityBadgeLabel ?? "").toString().trim();
                return label.length > 0 && (product?.seasonalityScore === null || product?.seasonalityScore > 0);
            };

            const renderSeasonalityBadge = (product) => shouldShowSeasonalityBadge(product)
                ? `<span class="search-seasonality-badge">${escapeHtml(product.seasonalityBadgeLabel)}</span>`
                : "";

            const renderRecommendationHints = (product, maxTags = 2) => {
                const tags = Array.isArray(product?.recommendationTags)
                    ? product.recommendationTags.filter((tag) => (tag || "").toString().trim().length > 0).slice(0, maxTags)
                    : [];
                const reason = (product?.recommendationReason ?? "").toString().trim();
                if (!reason && tags.length === 0) {
                    return "";
                }

                return `
                    <div class="search-recommendation-meta">
                        ${tags.length > 0 ? `
                            <div class="d-flex flex-wrap gap-1 mb-1">
                                ${tags.map((tag) => `
                                    <span class="badge">${escapeHtml(tag)}</span>
                                `).join("")}
                            </div>
                        ` : ""}
                        ${reason ? `<div class="small">${escapeHtml(reason)}</div>` : ""}
                    </div>
                `;
            };

            const buildCheckoutUrl = (product, qty = 1) => {
                const sellerId = toNumber(product.primarySellerId, 0);
                const sellerName = (getShopSummary(sellerId)?.shopName || "").toString();
                const query = new URLSearchParams({
                    productId: String(product.productId),
                    sellerId: String(sellerId),
                    sellerName,
                    productName: product.productName,
                    unitPrice: String(product.price),
                    unitSymbol: product.unitName,
                    quantity: String(Math.max(1, qty))
                });
                return "/checkout?" + query.toString();
            };

            const buildProductUrl = (product) => {
                const base = `/products/${product.productId}`;
                if (!state.keyword) {
                    return base;
                }

                return `${base}?q=${encodeURIComponent(state.keyword)}`;
            };

            const buildShopUrl = (product) => {
                if (toNumber(product.primarySellerId, 0) <= 0) {
                    return "";
                }

                return `/shop/${product.primarySellerId}`;
            };

            const buildShopChatUrl = (product) => {
                const shopUrl = buildShopUrl(product);
                return shopUrl ? `${shopUrl}#shop-chat` : "";
            };

            const buildRelatedImpressionKey = (productId, rank) =>
                `${toNumber(productId, 0)}:${Math.max(0, toNumber(rank, 0))}`;

            const buildSearchFilterPayload = () => ({
                categoryIds: [...state.selectedCategoryIds].sort((a, b) => a - b),
                sellerIds: [...state.selectedSellerIds].sort((a, b) => a - b),
                availability: [...state.selectedAvailability].sort(),
                deliveryScopes: [...state.selectedDeliveryScopes].sort(),
                locations: [...state.selectedLocations].sort((a, b) => a.localeCompare(b, "vi-VN")),
                units: [...state.selectedUnits].sort((a, b) => a.localeCompare(b, "vi-VN")),
                certifications: [...state.selectedCertifications].sort((a, b) => a.localeCompare(b, "vi-VN")),
                minPrice: state.minPrice,
                maxPrice: state.maxPrice,
                minRating: state.minRating,
                sort: state.sortBy,
                page: state.page,
                pageSize: state.pageSize,
                preset: state.activePreset || null
            });

            const registerRelatedImpressions = (relatedItems) => {
                if (!appHelpers?.trackRecommendationImpression || !Array.isArray(relatedItems) || relatedItems.length === 0) {
                    relatedImpressionIds.clear();
                    return;
                }

                relatedRecommendationRunId = appHelpers.createRecommendationRunId?.(relatedRecommendationPlacement) ?? "";
                relatedImpressionIds.clear();

                relatedItems.forEach((product, index) => {
                    const rank = index + 1;
                    const impressionKey = buildRelatedImpressionKey(product.productId, rank);

                    void appHelpers.trackRecommendationImpression({
                        placement: relatedRecommendationPlacement,
                        recommendationRunId: relatedRecommendationRunId,
                        productId: product.productId,
                        rank,
                        algorithm: relatedRecommendationAlgorithm
                    }).then((eventId) => {
                        if (eventId) {
                            relatedImpressionIds.set(impressionKey, eventId);
                        }
                    });
                });
            };

            const getSelectedCategories = () => state.availableCategories.filter((category) => state.selectedCategoryIds.has(category.categoryId));
            const getSelectedShops = () => state.availableShops.filter((shop) => state.selectedSellerIds.has(shop.sellerId));
            const getSelectedAvailability = () => state.availableAvailability.filter((entry) => state.selectedAvailability.has(entry.key));
            const getSelectedDeliveryScopes = () => state.availableDeliveryScopes.filter((entry) => state.selectedDeliveryScopes.has(entry.key));
            const isBrowsingKeywordContext = () => {
                const normalizedKeyword = normalizeText(state.keyword);
                if (!normalizedKeyword) {
                    return false;
                }

                if (marketplaceShortcutKeywords.has(normalizedKeyword)) {
                    return true;
                }

                return state.availableCategories.some((category) => {
                    const normalizedName = normalizeText(category.categoryName);
                    const normalizedSlug = normalizeText(category.slug);
                    return normalizedKeyword === normalizedName || normalizedKeyword === normalizedSlug;
                });
            };

            const shouldOmitKeywordFromProductRequest = () => {
                return !!state.keyword
                    && state.selectedCategoryIds.size > 0
                    && isBrowsingKeywordContext();
            };
            const searchPresets = [
                { key: "ready-today", label: "Ưu tiên còn hàng", icon: "bi-box2-heart" },
                { key: "fresh-pick", label: "Hàng tươi dễ chọn", icon: "bi-flower1" },
                { key: "trusted-shop", label: "Shop đáng tin", icon: "bi-shield-check" },
                { key: "local-delivery", label: "Ưu tiên nội tỉnh", icon: "bi-truck" },
                { key: "same-day-urban", label: "Nội thành trong ngày", icon: "bi-lightning-charge" },
                ...(isBuyerEligible ? [{ key: "recent-reply", label: "Shop vừa phản hồi", icon: "bi-chat-dots" }] : []),
                { key: "seasonal-pick", label: "Hàng theo mùa", icon: "bi-calendar-event" },
                { key: "newest", label: "Sản phẩm mới", icon: "bi-stars" },
                { key: "bestseller", label: "Bán chạy", icon: "bi-fire" },
                { key: "budget", label: "Giá dễ mua", icon: "bi-cash-coin" }
            ];
            const resolveAvailability = (stockQuantity) => {
                if (toNumber(stockQuantity, 0) <= 0) return "out-of-stock";
                if (toNumber(stockQuantity, 0) <= 10) return "low-stock";
                return "in-stock";
            };
            const getAvailabilityMeta = (product) => {
                const key = (product?.availabilityKey || resolveAvailability(product?.stockQuantity)).toString();
                if (key === "out-of-stock") {
                    return {
                        key,
                        icon: "bi-x-circle",
                        label: "Hết hàng",
                        detail: "Tạm thời chưa thể mua ngay"
                    };
                }

                if (key === "low-stock") {
                    return {
                        key,
                        icon: "bi-exclamation-circle",
                        label: "Sắp hết hàng",
                        detail: `Còn khoảng ${Math.max(0, toNumber(product?.stockQuantity, 0)).toLocaleString("vi-VN")} ${product?.unitName || "đơn vị"}`
                    };
                }

                return {
                    key: "in-stock",
                    icon: "bi-check-circle",
                    label: "Còn hàng",
                    detail: `Sẵn ${Math.max(0, toNumber(product?.stockQuantity, 0)).toLocaleString("vi-VN")} ${product?.unitName || "đơn vị"}`
                };
            };

            const parseDate = (value) => {
                if (!value) {
                    return null;
                }

                const parsed = new Date(value);
                return Number.isNaN(parsed.getTime()) ? null : parsed;
            };

            const getShopSummary = (sellerId) => {
                const normalizedSellerId = toNumber(sellerId, 0);
                if (normalizedSellerId <= 0) {
                    return null;
                }

                return state.availableShops.find((shop) => shop.sellerId === normalizedSellerId) ?? null;
            };

            const getShopTrustSignals = (shop) => {
                if (!shop) {
                    return [];
                }

                const signals = [];
                const joinedAt = parseDate(shop.joinedAt);
                const now = new Date();
                const daysActive = joinedAt ? Math.floor((now.getTime() - joinedAt.getTime()) / 86400000) : null;
                const addressText = (shop.addressSummary ?? "").toString().trim();
                const hasAddress = !!addressText && !/^chưa cập nhật/i.test(addressText);

                if (typeof daysActive === "number" && daysActive >= 180) {
                    signals.push({ key: "stable", icon: "bi-patch-check", label: "Hoạt động lâu" });
                } else if (typeof daysActive === "number" && daysActive <= 90) {
                    signals.push({ key: "new", icon: "bi-stars", label: "Mới tham gia" });
                }

                if (toNumber(shop.productCount, 0) >= 4) {
                    signals.push({ key: "catalog", icon: "bi-grid", label: "Nhiều mặt hàng" });
                }

                if (hasAddress) {
                    signals.push({ key: "address", icon: "bi-geo-alt", label: "Có địa chỉ shop" });
                }

                return signals.slice(0, 2);
            };

            const renderShopTrustBadges = (shop) => {
                const signals = getShopTrustSignals(shop);
                if (signals.length === 0) {
                    return "";
                }

                return `
                    <div class="search-shop-trust">
                        ${signals.map((signal) => `
                            <span class="search-shop-trust-badge ${signal.key}">
                                <i class="bi ${signal.icon}"></i>
                                <span>${escapeHtml(signal.label)}</span>
                            </span>
                        `).join("")}
                    </div>
                `;
            };

            const getFreshnessSignals = (product) => {
                const signals = [];
                const preservation = (product?.preservation ?? "").toString().trim();
                const origin = (product?.origin ?? "").toString().trim();
                const weight = (product?.weight ?? "").toString().trim();
                const normalizedPreservation = normalizeText(preservation);

                if (origin) {
                    signals.push({ key: "origin", icon: "bi-geo-alt", label: "Xuất xứ rõ ràng" });
                }

                if (normalizedPreservation.includes("lanh")) {
                    signals.push({ key: "fresh", icon: "bi-snow", label: "Ưu tiên chuỗi lạnh" });
                } else if (normalizedPreservation.includes("mat")) {
                    signals.push({ key: "fresh", icon: "bi-thermometer-snow", label: "Giữ mát khi giao" });
                } else if (normalizedPreservation.includes("kho")) {
                    signals.push({ key: "fresh", icon: "bi-box-seam", label: "Bảo quản khô ráo" });
                } else if (preservation) {
                    signals.push({ key: "fresh", icon: "bi-shield-check", label: "Có hướng dẫn bảo quản" });
                }

                if (weight) {
                    signals.push({ key: "pack", icon: "bi-bag-check", label: "Quy cách rõ ràng" });
                }

                return signals.slice(0, 2);
            };

            const renderFreshnessBadges = (product) => {
                const signals = getFreshnessSignals(product);
                if (signals.length === 0) {
                    return "";
                }

                return `
                    <div class="search-freshness-signals">
                        ${signals.map((signal) => `
                            <span class="search-freshness-badge ${signal.key}">
                                <i class="bi ${signal.icon}"></i>
                                <span>${escapeHtml(signal.label)}</span>
                            </span>
                        `).join("")}
                    </div>
                `;
            };

            const getDeliveryScopeMeta = (product) => {
                const shop = getShopSummary(product?.primarySellerId);
                const addressText = normalizeText(shop?.addressSummary || "");

                if (!addressText || addressText.startsWith("chua cap nhat")) {
                    return null;
                }

                if (addressText.includes("phuong") || addressText.includes(" xa") || addressText.startsWith("xa ") || addressText.includes("thi tran") || addressText.includes("ap ") || addressText.includes("thon ")) {
                    return { key: "ward-level", icon: "bi-signpost-split", label: "Tới xã/phường" };
                }

                if (addressText.includes("quan ") || addressText.includes("huyen") || addressText.includes("thi xa")) {
                    return { key: "district-level", icon: "bi-map", label: "Tới quận/huyện" };
                }

                if (addressText.includes("tinh") || addressText.includes("thanh pho") || addressText.includes("tp ")) {
                    return { key: "province-level", icon: "bi-geo", label: "Tỉnh/thành rõ" };
                }

                return null;
            };

            const clearPresetEffects = () => {
                state.presetAvailabilityKeys.forEach((key) => state.selectedAvailability.delete(key));
                state.presetAvailabilityKeys.clear();

                state.presetDeliveryScopeKeys.forEach((key) => state.selectedDeliveryScopes.delete(key));
                state.presetDeliveryScopeKeys.clear();

                if (state.presetAppliedSort && state.sortBy === state.presetAppliedSort) {
                    state.sortBy = "related";
                }

                state.presetAppliedSort = "";
            };

            const applyPreset = (presetKey) => {
                clearPresetEffects();
                state.activePreset = presetKey;

                switch (presetKey) {
                    case "ready-today":
                        state.selectedAvailability.add("in-stock");
                        state.presetAvailabilityKeys = new Set(["in-stock"]);
                        state.presetAppliedSort = "related";
                        state.sortBy = "related";
                        break;
                    case "fresh-pick":
                        state.presetAppliedSort = "newest";
                        state.sortBy = "newest";
                        break;
                    case "trusted-shop":
                        state.presetAppliedSort = "related";
                        state.sortBy = "related";
                        break;
                    case "local-delivery":
                        state.selectedDeliveryScopes.add("district-level");
                        state.selectedDeliveryScopes.add("ward-level");
                        state.presetDeliveryScopeKeys = new Set(["district-level", "ward-level"]);
                        state.presetAppliedSort = "related";
                        state.sortBy = "related";
                        break;
                    case "same-day-urban":
                        state.selectedDeliveryScopes.add("ward-level");
                        state.presetDeliveryScopeKeys = new Set(["ward-level"]);
                        state.presetAppliedSort = "related";
                        state.sortBy = "related";
                        break;
                    case "recent-reply":
                        state.presetAppliedSort = "related";
                        state.sortBy = "related";
                        break;
                    case "seasonal-pick":
                        state.presetAppliedSort = "related";
                        state.sortBy = "related";
                        break;
                    case "newest":
                        state.presetAppliedSort = "newest";
                        state.sortBy = "newest";
                        break;
                    case "bestseller":
                        state.presetAppliedSort = "bestseller";
                        state.sortBy = "bestseller";
                        break;
                    case "budget":
                        state.presetAppliedSort = "price-asc";
                        state.sortBy = "price-asc";
                        break;
                    default:
                        state.activePreset = "";
                        break;
                }

                syncFilterInputsFromState();
            };

            const renderPresetPills = () => {
                if (!(presetPillWrap instanceof HTMLElement)) {
                    return;
                }

                presetPillWrap.innerHTML = searchPresets
                    .map((preset) => `
                        <button type="button"
                                class="search-preset-pill${state.activePreset === preset.key ? " active" : ""}"
                                data-action="apply-preset"
                                data-preset-key="${preset.key}">
                            <i class="bi ${preset.icon}"></i>
                            <span>${escapeHtml(preset.label)}</span>
                        </button>
                    `)
                    .join("");
            };

            const syncFilterInputsFromState = () => {
                if (minPriceInput instanceof HTMLInputElement) {
                    minPriceInput.value = typeof state.minPrice === "number" ? String(state.minPrice) : "";
                }

                if (maxPriceInput instanceof HTMLInputElement) {
                    maxPriceInput.value = typeof state.maxPrice === "number" ? String(state.maxPrice) : "";
                }

                document.querySelectorAll('input[name="ratingFilter"]').forEach((input) => {
                    if (input instanceof HTMLInputElement) {
                        input.checked = toNumber(input.value, 0) === state.minRating;
                    }
                });

                document.querySelectorAll(".btn-sort").forEach((btn) => {
                    const sort = btn.getAttribute("data-sort") || "related";
                    btn.classList.toggle("active", sort === state.sortBy);
                });
            };

            const renderActiveFilterSummary = () => {
                if (!activeFilterSummary) {
                    return;
                }

                const chips = [];
                const selectedCategories = getSelectedCategories();

                for (const category of selectedCategories) {
                    chips.push(`
                        <span class="filter-selection-chip active">
                            Danh mục: ${escapeHtml(category.categoryName)}
                            <button type="button" data-action="remove-category" data-category-id="${category.categoryId}" aria-label="Bỏ lọc ${escapeHtml(category.categoryName)}">
                                <i class="bi bi-x-lg"></i>
                            </button>
                        </span>
                    `);
                }

                for (const shop of getSelectedShops()) {
                    chips.push(`
                        <span class="filter-selection-chip active">
                            Shop: ${escapeHtml(shop.shopName)}
                            <button type="button" data-action="remove-shop" data-seller-id="${shop.sellerId}" aria-label="Bỏ lọc ${escapeHtml(shop.shopName)}">
                                <i class="bi bi-x-lg"></i>
                            </button>
                        </span>
                    `);
                }

                for (const availability of getSelectedAvailability()) {
                    chips.push(`
                        <span class="filter-selection-chip active">
                            Tồn kho: ${escapeHtml(availability.label)}
                            <button type="button" data-action="remove-availability" data-availability-key="${escapeHtml(availability.key)}" aria-label="Bỏ lọc ${escapeHtml(availability.label)}">
                                <i class="bi bi-x-lg"></i>
                            </button>
                        </span>
                    `);
                }

                for (const scope of getSelectedDeliveryScopes()) {
                    chips.push(`
                        <span class="filter-selection-chip active">
                            Giao: ${escapeHtml(scope.label)}
                            <button type="button" data-action="remove-delivery-scope" data-delivery-scope-key="${escapeHtml(scope.key)}" aria-label="Bỏ lọc ${escapeHtml(scope.label)}">
                                <i class="bi bi-x-lg"></i>
                            </button>
                        </span>
                    `);
                }

                Array.from(state.selectedLocations)
                    .sort((a, b) => a.localeCompare(b))
                    .forEach((origin) => {
                        chips.push(`
                            <span class="filter-selection-chip active">
                                Xuất xứ: ${escapeHtml(origin)}
                                <button type="button" data-action="remove-location" data-filter-value="${escapeHtml(origin)}" aria-label="Bỏ lọc ${escapeHtml(origin)}">
                                    <i class="bi bi-x-lg"></i>
                                </button>
                            </span>
                        `);
                    });

                Array.from(state.selectedUnits)
                    .sort((a, b) => a.localeCompare(b))
                    .forEach((unit) => {
                        chips.push(`
                            <span class="filter-selection-chip active">
                                Đơn vị: ${escapeHtml(unit)}
                                <button type="button" data-action="remove-unit" data-filter-value="${escapeHtml(unit)}" aria-label="Bỏ lọc ${escapeHtml(unit)}">
                                    <i class="bi bi-x-lg"></i>
                                </button>
                            </span>
                        `);
                    });

                Array.from(state.selectedCertifications)
                    .sort((a, b) => a.localeCompare(b))
                    .forEach((standard) => {
                        chips.push(`
                            <span class="filter-selection-chip active">
                                Chuẩn: ${escapeHtml(standard)}
                                <button type="button" data-action="remove-standard" data-filter-value="${escapeHtml(standard)}" aria-label="Bỏ lọc ${escapeHtml(standard)}">
                                    <i class="bi bi-x-lg"></i>
                                </button>
                            </span>
                        `);
                    });

                if (typeof state.minPrice === "number" || typeof state.maxPrice === "number") {
                    const minLabel = typeof state.minPrice === "number" ? state.minPrice.toLocaleString("vi-VN") : "0";
                    const maxLabel = typeof state.maxPrice === "number" ? state.maxPrice.toLocaleString("vi-VN") : "∞";
                    chips.push(`
                        <span class="filter-selection-chip active">
                            Giá: ${minLabel} - ${maxLabel} ₫
                            <button type="button" data-action="remove-price-range" aria-label="Bỏ lọc giá">
                                <i class="bi bi-x-lg"></i>
                            </button>
                        </span>
                    `);
                }

                if (state.minRating > 0) {
                    chips.push(`
                        <span class="filter-selection-chip active">
                            Đánh giá từ ${state.minRating.toFixed(1)}
                            <button type="button" data-action="remove-rating" aria-label="Bỏ lọc đánh giá">
                                <i class="bi bi-x-lg"></i>
                            </button>
                        </span>
                    `);
                }

                if (state.sortBy && state.sortBy !== "related") {
                    const sortLabel = ({
                        newest: "Mới nhất",
                        bestseller: "Bán chạy",
                        "price-asc": "Giá tăng dần",
                        "price-desc": "Giá giảm dần"
                    })[state.sortBy] || state.sortBy;

                    chips.push(`
                        <span class="filter-selection-chip active">
                            Sắp xếp: ${escapeHtml(sortLabel)}
                            <button type="button" data-action="remove-sort" aria-label="Bỏ sắp xếp">
                                <i class="bi bi-x-lg"></i>
                            </button>
                        </span>
                    `);
                }

                if (state.activePreset) {
                    const presetLabel = searchPresets.find((preset) => preset.key === state.activePreset)?.label || state.activePreset;
                    chips.push(`
                        <span class="filter-selection-chip active">
                            Nhanh: ${escapeHtml(presetLabel)}
                            <button type="button" data-action="remove-preset" aria-label="Bỏ preset ${escapeHtml(presetLabel)}">
                                <i class="bi bi-x-lg"></i>
                            </button>
                        </span>
                    `);
                }

                if (chips.length === 0) {
                    activeFilterSummary.classList.add("d-none");
                    activeFilterSummary.innerHTML = "";
                    return;
                }

                activeFilterSummary.classList.remove("d-none");
                activeFilterSummary.innerHTML = chips.join("");
            };

            const renderCategoryPills = () => {
                if (!categoryPillWrap) {
                    return;
                }

                if (state.availableCategories.length === 0) {
                    categoryPillWrap.innerHTML = "<div class='text-muted small'>Chưa có danh mục công khai.</div>";
                    return;
                }

                categoryPillWrap.innerHTML = state.availableCategories
                    .map((category) => {
                        const active = state.selectedCategoryIds.has(category.categoryId);
                        const countLabel = category.productCount > 0
                            ? `${category.productCount.toLocaleString("vi-VN")} SP`
                            : "Chưa có SP";

                        return `
                            <button type="button"
                                    class="search-category-pill${active ? " active" : ""}"
                                    data-action="toggle-category-pill"
                                    data-category-id="${category.categoryId}">
                                <span>${escapeHtml(category.categoryName)}</span>
                                <span class="small text-muted">${countLabel}</span>
                            </button>
                        `;
                    })
                    .join("");
            };

            const renderShopFilters = () => {
                if (!(shopWrap instanceof HTMLElement)) {
                    return;
                }

                if (state.availableShops.length === 0) {
                    shopWrap.innerHTML = "<div class='text-muted small'>Chưa có shop khả dụng.</div>";
                    return;
                }

                shopWrap.innerHTML = state.availableShops
                    .map((shop) => `
                        <label class="form-check">
                            <input class="form-check-input"
                                   type="checkbox"
                                   data-filter-type="shop"
                                   value="${shop.sellerId}"
                                   ${state.selectedSellerIds.has(shop.sellerId) ? "checked" : ""}>
                            <span class="form-check-label d-flex justify-content-between gap-2">
                                <span class="text-truncate">${escapeHtml(shop.shopName)}</span>
                                <span class="text-muted small">${toNumber(shop.productCount, 0).toLocaleString("vi-VN")}</span>
                            </span>
                            ${renderShopTrustBadges(shop)}
                        </label>
                    `)
                    .join("");
            };

            const renderAvailabilityFilters = () => {
                if (!(availabilityWrap instanceof HTMLElement)) {
                    return;
                }

                if (state.availableAvailability.length === 0) {
                    availabilityWrap.innerHTML = "<div class='text-muted small'>Chưa có dữ liệu tồn kho.</div>";
                    return;
                }

                availabilityWrap.innerHTML = state.availableAvailability
                    .map((entry) => `
                        <label class="form-check">
                            <input class="form-check-input"
                                   type="checkbox"
                                   data-filter-type="availability"
                                   value="${entry.key}"
                                   ${state.selectedAvailability.has(entry.key) ? "checked" : ""}>
                            <span class="form-check-label d-flex justify-content-between gap-2">
                                <span>${escapeHtml(entry.label)}</span>
                                <span class="text-muted small">${toNumber(entry.count, 0).toLocaleString("vi-VN")}</span>
                            </span>
                        </label>
                    `)
                    .join("");
            };

            const applyChatSummaries = (products) => {
                state.chatSummaries = new Map();

                if (!isAuthenticated || !isBuyerEligible) {
                    return;
                }

                products.forEach((product) => {
                    const sellerId = toNumber(product.primarySellerId, 0);
                    if (sellerId > 0 && !state.chatSummaries.has(sellerId)) {
                        state.chatSummaries.set(sellerId, null);
                    }
                });
            };

            const loadChatSummaries = async () => {
                if (!isAuthenticated || !isBuyerEligible || state.chatSummaries.size === 0) {
                    return;
                }

                const sellerIds = Array.from(state.chatSummaries.keys());
                const query = sellerIds.map((sellerId) => `sellerIds=${sellerId}`).join("&");
                const response = await fetch(`/bff/support-chat/summaries?${query}`, {
                    headers: { Accept: "application/json" }
                });

                const { payload } = appHelpers
                    ? await appHelpers.tryParsePayload(response)
                    : { payload: await response.json() };
                if (!response.ok) {
                    throw (appHelpers?.createHttpError(response, payload, "Không tải được tóm tắt chat.")
                        ?? new Error(payload?.message || `HTTP ${response.status}`));
                }

                state.chatSummaries = new Map(sellerIds.map((sellerId) => [sellerId, null]));
                for (const summary of (payload?.summaries || [])) {
                    const sellerId = toNumber(summary?.sellerId, 0);
                    if (sellerId > 0) {
                        state.chatSummaries.set(sellerId, summary);
                    }
                }
            };

            const refreshChatSummariesSilently = async () => {
                if (!isAuthenticated || !isBuyerEligible || document.hidden) {
                    return;
                }

                try {
                    await loadChatSummaries();
                    renderResultGrid(applyFilters());
                } catch {
                    // Keep the existing UI state if the lightweight refresh fails.
                }
            };

            const ensureChatSummaryPolling = () => {
                if (!isAuthenticated || !isBuyerEligible || state.chatSummaryRefreshTimer) {
                    return;
                }

                state.chatSummaryRefreshTimer = window.setInterval(() => {
                    void refreshChatSummariesSilently();
                }, 15000);
            };

            const showCartToast = (message, tone = "success") => {
                let toast = document.getElementById("searchCartToast");
                if (!(toast instanceof HTMLElement)) {
                    toast = document.createElement("div");
                    toast.id = "searchCartToast";
                    toast.style.position = "fixed";
                    toast.style.right = "20px";
                    toast.style.bottom = "20px";
                    toast.style.zIndex = "1085";
                    toast.style.maxWidth = "320px";
                    toast.style.padding = "12px 14px";
                    toast.style.borderRadius = "14px";
                    toast.style.color = "#fff";
                    toast.style.fontWeight = "700";
                    toast.style.lineHeight = "1.55";
                    toast.style.boxShadow = "0 16px 40px rgba(15, 23, 42, .18)";
                    toast.style.opacity = "0";
                    toast.style.pointerEvents = "none";
                    toast.style.transition = "opacity .2s ease, transform .2s ease";
                    toast.style.transform = "translateY(12px)";
                    document.body.appendChild(toast);
                }

                toast.textContent = message;
                toast.style.background = tone === "error" ? "#b91c1c" : "#166534";
                toast.style.opacity = "1";
                toast.style.transform = "translateY(0)";

                window.clearTimeout(state.cartToastTimer);
                state.cartToastTimer = window.setTimeout(() => {
                    toast.style.opacity = "0";
                    toast.style.transform = "translateY(12px)";
                }, 2200);
            };

            const submitAddToCart = async (product, qty = 1, triggerButton = null) => {
                if (!antiForgeryToken || toNumber(product.productId, 0) <= 0) {
                    return null;
                }

                const targetButton = triggerButton instanceof HTMLButtonElement ? triggerButton : null;
                const originalLabel = targetButton?.innerHTML ?? "";
                if (targetButton) {
                    targetButton.disabled = true;
                    targetButton.textContent = "Đang thêm...";
                }

                try {
                    const body = new URLSearchParams();
                    body.set("__RequestVerificationToken", antiForgeryToken);
                    body.set("ProductId", String(product.productId));
                    body.set("SellerId", String(toNumber(product.sellerId, product.primarySellerId || 0)));
                    body.set("SellerName", product.sellerName || "");
                    body.set("ProductName", product.productName || "");
                    body.set("ImageFileName", product.imageFileName || "");
                    body.set("UnitPrice", String(product.price ?? 0));
                    body.set("UnitSymbol", product.unitName || "");
                    body.set("Quantity", String(Math.max(1, toNumber(qty, 1))));

                    const response = await fetch("/cart/add", {
                        method: "POST",
                        credentials: "same-origin",
                        headers: {
                            "Accept": "application/json",
                            "Content-Type": "application/x-www-form-urlencoded; charset=UTF-8",
                            "X-Requested-With": "XMLHttpRequest"
                        },
                        body: body.toString()
                    });

                    if (response.redirected) {
                        window.location.href = response.url;
                        return null;
                    }

                    const { payload } = appHelpers
                        ? await appHelpers.tryParsePayload(response)
                        : { payload: null };

                    if (!response.ok) {
                        throw (appHelpers?.createHttpError(response, payload, "Không thể thêm sản phẩm vào giỏ hàng lúc này.")
                            ?? new Error(payload?.message || `HTTP ${response.status}`));
                    }

                    showCartToast(payload?.message || "Đã thêm sản phẩm vào giỏ hàng.");
                    return payload;
                } catch (error) {
                    showCartToast(error?.message || "Không thể thêm sản phẩm vào giỏ hàng lúc này.", "error");
                    return null;
                } finally {
                    if (targetButton) {
                        targetButton.disabled = false;
                        targetButton.innerHTML = originalLabel;
                    }
                }
            };

            const relevanceScore = (product, keyword) => {
                if (!keyword) return 0;
                const q = keyword.toLowerCase();
                const name = product.productName.toLowerCase();
                const category = product.categoryName.toLowerCase();
                let score = 0;
                if (name.startsWith(q)) score += 120;
                if (name.includes(q)) score += 60;
                if (category.includes(q)) score += 28;
                score += product.rating * 3;
                score += product.sold / 1000;
                return score;
            };

            const renderCategoryFilters = () => {
                if (state.availableCategories.length === 0) {
                    categoryWrap.innerHTML = "<div class='text-muted small'>Chưa có danh mục khả dụng.</div>";
                    return;
                }

                categoryWrap.innerHTML = state.availableCategories
                    .map((category) => `
                        <label class="form-check">
                            <input class="form-check-input"
                                   type="checkbox"
                                   data-filter-type="category"
                                   value="${category.categoryId}"
                                   ${state.selectedCategoryIds.has(category.categoryId) ? "checked" : ""}>
                            <span class="form-check-label d-flex justify-content-between gap-2">
                                <span>${escapeHtml(category.categoryName)}</span>
                                <span class="text-muted small">${category.productCount.toLocaleString("vi-VN")}</span>
                            </span>
                        </label>
                    `)
                    .join("");
            };

            const renderLocationFilters = () => {
                const locations = [...state.availableLocations];
                if (locations.length === 0) {
                    locationWrap.innerHTML = "<div class='text-muted small'>Chưa có dữ liệu xuất xứ.</div>";
                    return;
                }

                locationWrap.innerHTML = locations
                    .map((name) => `
                        <label class="form-check">
                            <input class="form-check-input" type="checkbox" data-filter-type="location" value="${escapeHtml(name)}" ${state.selectedLocations.has(name) ? "checked" : ""}>
                            <span class="form-check-label">${escapeHtml(name)}</span>
                        </label>
                    `)
                    .join("");
            };

            const renderDeliveryScopeFilters = () => {
                if (!(deliveryScopeWrap instanceof HTMLElement)) {
                    return;
                }

                if (state.availableDeliveryScopes.length === 0) {
                    deliveryScopeWrap.innerHTML = "<div class='text-muted small'>Chưa có dữ liệu địa bàn giao.</div>";
                    return;
                }

                deliveryScopeWrap.innerHTML = state.availableDeliveryScopes
                    .map((entry) => `
                        <label class="form-check">
                            <input class="form-check-input"
                                   type="checkbox"
                                   data-filter-type="delivery-scope"
                                   value="${escapeHtml(entry.key)}"
                                   ${state.selectedDeliveryScopes.has(entry.key) ? "checked" : ""}>
                            <span class="form-check-label d-flex justify-content-between gap-2">
                                <span>${escapeHtml(entry.label)}</span>
                                <span class="text-muted small">${toNumber(entry.count, 0).toLocaleString("vi-VN")}</span>
                            </span>
                        </label>
                    `)
                    .join("");
            };

            const renderCertificationFilters = () => {
                const certifications = [...state.availableStandards];
                if (certifications.length === 0) {
                    certWrap.innerHTML = "<div class='text-muted small'>Chưa có dữ liệu chuẩn nông sản.</div>";
                    return;
                }

                certWrap.innerHTML = certifications
                    .map((name) => `
                        <label class="form-check">
                            <input class="form-check-input" type="checkbox" data-filter-type="cert" value="${escapeHtml(name)}" ${state.selectedCertifications.has(name) ? "checked" : ""}>
                            <span class="form-check-label">${escapeHtml(name)}</span>
                        </label>
                    `)
                    .join("");
            };

            const renderUnitFilters = () => {
                const units = [...state.availableUnits];
                if (units.length === 0) {
                    unitWrap.innerHTML = "<div class='text-muted small'>Chưa có dữ liệu đơn vị.</div>";
                    return;
                }

                unitWrap.innerHTML = units
                    .map((name) => `
                        <label class="form-check">
                            <input class="form-check-input" type="checkbox" data-filter-type="unit" value="${escapeHtml(name)}" ${state.selectedUnits.has(name) ? "checked" : ""}>
                            <span class="form-check-label">${escapeHtml(name)}</span>
                        </label>
                    `)
                    .join("");
            };

            const renderRelatedSection = () => {
                const relatedItems = state.relatedProducts.slice(0, 4);
                if (relatedItems.length === 0) {
                    relatedWrap.innerHTML = "<div class='text-muted small'>Chưa có sản phẩm liên quan.</div>";
                    relatedImpressionIds.clear();
                    return;
                }

                relatedWrap.innerHTML = relatedItems
                    .map((product, index) => {
                        const availability = getAvailabilityMeta(product);
                        return `
                            <a href="${buildProductUrl(product)}"
                               class="shop-related-item"
                               data-action="related-recommendation-click"
                               data-product-id="${product.productId}"
                               data-rank="${index + 1}">
                                <img src="${product.image}" class="shop-related-thumb" alt="${escapeHtml(product.productName)}" loading="lazy" decoding="async" />
                                    <div class="shop-related-meta">
                                        <div class="shop-related-name">${escapeHtml(product.productName)}</div>
                                        ${renderSeasonalityBadge(product)}
                                        <div class="shop-related-price">${vnd(product.price)}</div>
                                        <div class="shop-related-stock ${availability.key}">${escapeHtml(availability.label)}</div>
                                    </div>
                            </a>
                        `;
                    })
                    .join("");

                registerRelatedImpressions(relatedItems);
            };

            const applyFilters = () => {
                let products = [...state.allProducts];

                if (state.activePreset === "recent-reply") {
                    products = products.filter((product) => {
                        const sellerId = toNumber(product?.primarySellerId, 0);
                        if (sellerId <= 0) {
                            return false;
                        }

                        const summary = state.chatSummaries.get(sellerId);
                        return summary?.hasUnread === true;
                    });
                }

                return products;
            };

            const renderResultGrid = (products) => {
                if (products.length === 0) {
                    grid.innerHTML = "";
                    emptyNode.classList.remove("d-none");
                    if (paginationNode instanceof HTMLElement) {
                        paginationNode.classList.add("d-none");
                    }
                    return;
                }

                emptyNode.classList.add("d-none");
                const totalPages = Math.max(1, state.totalPages || 1);
                const startIndex = (state.page - 1) * state.pageSize;

                grid.innerHTML = products
                    .map((product, index) => {
                        const availability = getAvailabilityMeta(product);
                        const deliveryScope = getDeliveryScopeMeta(product);
                        const shop = getShopSummary(product.primarySellerId);
                        const resultRank = startIndex + index + 1;
                        return `
                            <div class="col-6 col-md-4 col-xl-3">
                                <article class="search-product-card">
                                    <a href="${buildProductUrl(product)}"
                                       class="search-product-thumb-link"
                                       data-action="search-result-click"
                                       data-product-id="${product.productId}"
                                       data-seller-id="${toNumber(product.primarySellerId, 0)}"
                                       data-rank="${resultRank}">
                                        <img src="${product.image}" class="search-product-thumb" alt="${escapeHtml(product.productName)}" loading="lazy" decoding="async" />
                                    </a>
                                    <div class="search-product-body">
                                        <div class="search-product-top">
                                            <span class="search-badge">${escapeHtml(product.categoryName)}</span>
                                            <span class="search-unit">${escapeHtml(product.unitName)}</span>
                                        </div>
                                        <a href="${buildProductUrl(product)}"
                                           class="search-product-name search-product-name-link"
                                           data-action="search-result-click"
                                           data-product-id="${product.productId}"
                                           data-seller-id="${toNumber(product.primarySellerId, 0)}"
                                           data-rank="${resultRank}">${escapeHtml(product.productName)}</a>
                                        ${renderSeasonalityBadge(product)}
                                        <div class="small text-success fw-semibold">${escapeHtml(product.standard || product.origin || "Chưa có chuẩn nông sản")}</div>
                                        <div class="search-product-price">${vnd(product.price)}</div>
                                        <div class="search-product-stats">
                                            <span>${product.rating > 0 ? `<i class="bi bi-star-fill text-warning"></i> ${product.rating.toFixed(1)}` : "Chưa có đánh giá"}</span>
                                            <span>${product.sold > 0 ? `Đã bán ${product.sold.toLocaleString("vi-VN")}` : "Chưa có lượt bán"}</span>
                                        </div>
                                        <div class="search-product-location">
                                            <i class="bi bi-geo-alt"></i>
                                            <span>${escapeHtml(product.location || "Chưa có xuất xứ")}</span>
                                        </div>
                                        <div class="search-stock-badge ${availability.key}">
                                            <i class="bi ${availability.icon}"></i>
                                            <span>${escapeHtml(availability.label)} • ${escapeHtml(availability.detail)}</span>
                                        </div>
                                        ${deliveryScope ? `
                                            <div class="search-delivery-badge ${deliveryScope.key}">
                                                <i class="bi ${deliveryScope.icon}"></i>
                                                <span>${escapeHtml(deliveryScope.label)}</span>
                                            </div>
                                        ` : ""}
                                        ${renderFreshnessBadges(product)}
                                        ${shop ? `
                                            <div class="search-shop-meta">
                                                <div class="search-shop-name-row">
                                                    <i class="bi bi-shop"></i>
                                                    <span>${escapeHtml(shop.shopName || `Shop #${toNumber(product.primarySellerId, 0)}`)}</span>
                                                </div>
                                                ${renderShopTrustBadges(shop)}
                                            </div>
                                        ` : ""}
                                        ${renderRecommendationHints(product, 2)}
                                        ${(() => {
                                            if (!buildShopUrl(product)) {
                                                return "";
                                            }

                                            const sellerId = toNumber(product.primarySellerId, 0);
                                            const summary = state.chatSummaries.get(sellerId);
                                            const chatLabel = summary?.hasUnread
                                                ? "Xem tin nhắn mới"
                                                : (summary?.conversationId ? "Mở cuộc trò chuyện" : "Nhắn shop");
                                            const chatClass = summary?.hasUnread ? "search-shop-link chat unread" : "search-shop-link chat";
                                            const hint = summary?.hasUnread
                                                ? `<div class="search-chat-hint">Shop vừa phản hồi bạn</div>`
                                                : "";

                                            return `<div class="search-shop-links">
                                                    <a href="${buildShopUrl(product)}" class="search-shop-link shop">Xem shop đang bán</a>
                                                    <a href="${buildShopChatUrl(product)}" class="${chatClass}">${chatLabel}</a>
                                                </div>${hint}`;
                                        })()
                                        }
                                        <div class="search-product-actions">
                                            <a href="${buildProductUrl(product)}"
                                               class="btn-view-detail"
                                               data-action="search-result-click"
                                               data-product-id="${product.productId}"
                                               data-seller-id="${toNumber(product.primarySellerId, 0)}"
                                               data-rank="${resultRank}">Xem chi tiết</a>
                                            <button type="button"
                                                    class="btn-add-cart"
                                                    data-action="add-cart"
                                                    data-product-id="${product.productId}"
                                                    data-seller-id="${toNumber(product.primarySellerId, 0)}"
                                                    data-seller-name="${escapeHtml(shop?.shopName || `Shop #${toNumber(product.primarySellerId, 0)}`)}"
                                                    data-product-name="${escapeHtml(product.productName)}"
                                                    data-unit-price="${product.price}"
                                                    data-unit-symbol="${escapeHtml(product.unitName)}">
                                                Thêm giỏ
                                            </button>
                                            <a href="${buildCheckoutUrl(product)}" class="btn-buy-now">Mua ngay</a>
                                        </div>
                                    </div>
                                </article>
                            </div>
                        `;
                    })
                    .join("");

                if (!(paginationNode instanceof HTMLElement) ||
                    !(paginationMetaNode instanceof HTMLElement) ||
                    !(paginationActionsNode instanceof HTMLElement)) {
                    return;
                }

                if (totalPages <= 1) {
                    paginationNode.classList.add("d-none");
                    paginationMetaNode.textContent = "";
                    paginationActionsNode.innerHTML = "";
                    return;
                }

                paginationNode.classList.remove("d-none");
                paginationMetaNode.textContent = `Đang xem ${Math.min(startIndex + 1, state.totalCount).toLocaleString("vi-VN")} - ${Math.min(startIndex + products.length, state.totalCount).toLocaleString("vi-VN")} / ${state.totalCount.toLocaleString("vi-VN")} sản phẩm`;

                const pageItems = [];
                for (let page = Math.max(1, state.page - 1); page <= Math.min(totalPages, state.page + 1); page++) {
                    pageItems.push(page);
                }

                paginationActionsNode.innerHTML = `
                    <button type="button" class="search-page-btn" data-action="goto-page" data-page="${state.page - 1}" ${state.page <= 1 ? "disabled" : ""}>
                        <i class="bi bi-chevron-left"></i>
                    </button>
                    ${pageItems.map((page) => `
                        <button type="button" class="search-page-btn${page === state.page ? " active" : ""}" data-action="goto-page" data-page="${page}">
                            ${page}
                        </button>
                    `).join("")}
                    <button type="button" class="search-page-btn" data-action="goto-page" data-page="${state.page + 1}" ${state.page >= totalPages ? "disabled" : ""}>
                        <i class="bi bi-chevron-right"></i>
                    </button>
                `;
            };

            const updateSummary = (total) => {
                resultCountNode.textContent = `Tìm thấy ${total.toLocaleString("vi-VN")} sản phẩm`;
                const totalPages = Math.max(1, state.totalPages || 1);
                state.page = Math.min(Math.max(state.page, 1), totalPages);
                sortSummaryNode.textContent = `Hiển thị ${total.toLocaleString("vi-VN")} sản phẩm • Trang ${state.page}/${totalPages}`;
                keywordLabel.textContent = state.keyword || "Tất cả";
                relatedKeywordLabel.textContent = state.keyword || "Tất cả";
                if (searchInput instanceof HTMLInputElement) {
                    searchInput.value = state.keyword;
                }
            };

            const tryHydrateCategoryFromKeyword = () => {
                if (state.selectedCategoryIds.size > 0 || !state.keyword || state.availableCategories.length === 0) {
                    return false;
                }

                const normalizedKeyword = normalizeText(state.keyword);
                const matched = state.availableCategories.find((category) => {
                    const normalizedName = normalizeText(category.categoryName);
                    const normalizedSlug = normalizeText(category.slug);
                    return normalizedKeyword === normalizedName || normalizedKeyword === normalizedSlug;
                });

                if (!matched) {
                    return false;
                }

                state.selectedCategoryIds.add(matched.categoryId);
                return true;
            };

            const syncUrl = () => {
                const params = new URLSearchParams();
                if (state.keyword) {
                    params.set("q", state.keyword);
                }

                Array.from(state.selectedCategoryIds)
                    .sort((a, b) => a - b)
                    .forEach((categoryId) => params.append("category", String(categoryId)));

                Array.from(state.selectedSellerIds)
                    .sort((a, b) => a - b)
                    .forEach((sellerId) => params.append("seller", String(sellerId)));

                Array.from(state.selectedAvailability)
                    .sort((a, b) => a.localeCompare(b))
                    .forEach((value) => params.append("availability", value));

                Array.from(state.selectedDeliveryScopes)
                    .sort((a, b) => a.localeCompare(b))
                    .forEach((value) => params.append("deliveryScope", value));

                Array.from(state.selectedLocations)
                    .sort((a, b) => a.localeCompare(b))
                    .forEach((origin) => params.append("origin", origin));

                Array.from(state.selectedCertifications)
                    .sort((a, b) => a.localeCompare(b))
                    .forEach((standard) => params.append("standard", standard));

                Array.from(state.selectedUnits)
                    .sort((a, b) => a.localeCompare(b))
                    .forEach((unit) => params.append("unit", unit));

                if (typeof state.minPrice === "number") {
                    params.set("minPrice", String(state.minPrice));
                }

                if (typeof state.maxPrice === "number") {
                    params.set("maxPrice", String(state.maxPrice));
                }

                if (state.minRating > 0) {
                    params.set("minRating", String(state.minRating));
                }

                if (state.sortBy && state.sortBy !== "related") {
                    params.set("sort", state.sortBy);
                }

                if (state.activePreset) {
                    params.set("preset", state.activePreset);
                }

                if (state.page > 1) {
                    params.set("page", String(state.page));
                }

                const next = params.toString() ? `/search?${params.toString()}` : "/search";
                history.replaceState(null, "", next);
            };

            const buildProductRequestUrl = (options = {}) => {
                const omitKeyword = options?.omitKeyword === true;
                const params = new URLSearchParams();
                if (state.keyword && !omitKeyword) {
                    params.set("name", state.keyword);
                }

                Array.from(state.selectedCategoryIds)
                    .sort((a, b) => a - b)
                    .forEach((categoryId) => params.append("categoryIds", String(categoryId)));

                Array.from(state.selectedSellerIds)
                    .sort((a, b) => a - b)
                    .forEach((sellerId) => params.append("sellerIds", String(sellerId)));

                Array.from(state.selectedAvailability)
                    .sort((a, b) => a.localeCompare(b))
                    .forEach((value) => params.append("availability", value));

                Array.from(state.selectedDeliveryScopes)
                    .sort((a, b) => a.localeCompare(b))
                    .forEach((value) => params.append("deliveryScopes", value));

                Array.from(state.selectedLocations)
                    .sort((a, b) => a.localeCompare(b))
                    .forEach((origin) => params.append("origins", origin));

                Array.from(state.selectedCertifications)
                    .sort((a, b) => a.localeCompare(b))
                    .forEach((standard) => params.append("standards", standard));

                Array.from(state.selectedUnits)
                    .sort((a, b) => a.localeCompare(b))
                    .forEach((unit) => params.append("units", unit));

                if (typeof state.minPrice === "number") {
                    params.set("minPrice", String(state.minPrice));
                }

                if (typeof state.maxPrice === "number") {
                    params.set("maxPrice", String(state.maxPrice));
                }

                if (state.minRating > 0) {
                    params.set("minRating", String(state.minRating));
                }

                if (state.sortBy) {
                    params.set("sort", state.sortBy);
                }

                if (state.activePreset) {
                    params.set("preset", state.activePreset);
                }

                params.set("page", String(state.page));
                params.set("pageSize", String(state.pageSize));

                const query = params.toString();
                return query ? `${endpointBase}?${query}` : endpointBase;
            };

            const loadCategories = async () => {
                const response = await fetch(categoryEndpoint, {
                    method: "GET",
                    headers: { Accept: "application/json" }
                });

                const { payload } = appHelpers
                    ? await appHelpers.tryParsePayload(response)
                    : { payload: await response.json() };
                if (!response.ok) {
                    throw (appHelpers?.createHttpError(response, payload, "Không tải được danh mục.")
                        ?? new Error(`Không tải được danh mục (${response.status}).`));
                }
                state.availableCategories = Array.isArray(payload)
                    ? payload.map((raw) => ({
                        categoryId: toNumber(raw.categoryId ?? raw.CategoryId, 0),
                        categoryName: (raw.categoryName ?? raw.CategoryName ?? "Chưa phân loại").toString(),
                        slug: (raw.slug ?? raw.Slug ?? "").toString(),
                        productCount: toNumber(raw.productCount ?? raw.ProductCount, 0)
                    })).filter((category) => category.categoryId > 0)
                    : [];

                tryHydrateCategoryFromKeyword();
                renderCategoryFilters();
                renderCategoryPills();
                renderPresetPills();
                renderActiveFilterSummary();
            };

            const reloadProducts = async () => {
                try {
                    const requestUrl = buildProductRequestUrl({
                        omitKeyword: shouldOmitKeywordFromProductRequest()
                    });
                    const response = await fetch(requestUrl, {
                        method: "GET",
                        headers: { Accept: "application/json" }
                    });

                    const { payload } = appHelpers
                        ? await appHelpers.tryParsePayload(response)
                        : { payload: await response.json() };
                    if (!response.ok) {
                        throw (appHelpers?.createHttpError(response, payload, "Không tải được dữ liệu sản phẩm.")
                            ?? new Error(`Không tải được dữ liệu sản phẩm (${response.status}).`));
                    }
                    const items = Array.isArray(payload?.items) ? payload.items : [];
                    state.allProducts = items.map(normalizeProduct);
                    state.relatedProducts = Array.isArray(payload?.relatedItems)
                        ? payload.relatedItems.map(normalizeProduct)
                        : [...state.allProducts];
                    state.availableShops = Array.isArray(payload?.availableShops)
                        ? payload.availableShops
                            .map((raw) => ({
                                sellerId: toNumber(raw?.sellerId ?? raw?.SellerId, 0),
                                shopName: (raw?.shopName ?? raw?.ShopName ?? `Shop #${toNumber(raw?.sellerId ?? raw?.SellerId, 0)}`).toString(),
                                productCount: toNumber(raw?.productCount ?? raw?.ProductCount, 0),
                                addressSummary: (raw?.addressSummary ?? raw?.AddressSummary ?? "").toString(),
                                joinedAt: (raw?.joinedAt ?? raw?.JoinedAt ?? "").toString(),
                                avatar: (raw?.avatar ?? raw?.Avatar ?? "").toString()
                            }))
                            .filter((shop) => shop.sellerId > 0)
                        : [];
                    state.availableAvailability = Array.isArray(payload?.availableAvailability)
                        ? payload.availableAvailability
                            .map((raw) => ({
                                key: (raw?.key ?? raw?.Key ?? "").toString(),
                                label: (raw?.label ?? raw?.Label ?? "").toString(),
                                count: toNumber(raw?.count ?? raw?.Count, 0)
                            }))
                            .filter((entry) => entry.key.length > 0)
                        : [];
                    state.availableDeliveryScopes = Array.isArray(payload?.availableDeliveryScopes)
                        ? payload.availableDeliveryScopes
                            .map((raw) => ({
                                key: (raw?.key ?? raw?.Key ?? "").toString(),
                                label: (raw?.label ?? raw?.Label ?? "").toString(),
                                count: toNumber(raw?.count ?? raw?.Count, 0)
                            }))
                            .filter((entry) => entry.key.length > 0)
                        : [];
                    state.availableLocations = Array.isArray(payload?.availableLocations)
                        ? payload.availableLocations.filter((value) => typeof value === "string" && value.trim().length > 0)
                        : [];
                    state.availableUnits = Array.isArray(payload?.availableUnits)
                        ? payload.availableUnits.filter((value) => typeof value === "string" && value.trim().length > 0)
                        : [];
                    state.availableStandards = Array.isArray(payload?.availableStandards)
                        ? payload.availableStandards.filter((value) => typeof value === "string" && value.trim().length > 0)
                        : [];
                    state.totalCount = toNumber(payload?.totalCount, state.allProducts.length);
                    state.page = Math.max(1, toNumber(payload?.page, state.page));
                    state.pageSize = Math.max(1, toNumber(payload?.pageSize, state.pageSize));
                    state.totalPages = Math.max(1, toNumber(payload?.totalPages, 1));
                    state.latestSearchEventId = null;
                    applyChatSummaries(state.allProducts);

                    try {
                        await loadChatSummaries();
                    } catch {
                        state.chatSummaries = new Map();
                    }

                    renderCategoryFilters();
                    renderCategoryPills();
                    renderPresetPills();
                    renderShopFilters();
                    renderAvailabilityFilters();
                    renderDeliveryScopeFilters();
                    renderActiveFilterSummary();
                    renderLocationFilters();
                    renderUnitFilters();
                    renderCertificationFilters();
                    renderRelatedSection();

                    const filtered = applyFilters();
                    renderResultGrid(filtered);
                    updateSummary(state.totalCount);
                    ensureChatSummaryPolling();
                    void appHelpers?.trackSearch?.({
                        keyword: state.keyword || "__all_products__",
                        filters: buildSearchFilterPayload(),
                        resultCount: state.totalCount
                    }).then((eventId) => {
                        state.latestSearchEventId = eventId ?? null;
                    });
                } catch (error) {
                    console.error(error);
                    state.allProducts = [];
                    state.relatedProducts = [];
                    state.availableShops = [];
                    state.availableAvailability = [];
                    state.availableDeliveryScopes = [];
                    state.availableLocations = [];
                    state.availableUnits = [];
                    state.availableStandards = [];
                    state.totalCount = 0;
                    state.totalPages = 1;
                    state.latestSearchEventId = null;
                    renderCategoryFilters();
                    renderCategoryPills();
                    renderPresetPills();
                    renderShopFilters();
                    renderAvailabilityFilters();
                    renderDeliveryScopeFilters();
                    renderActiveFilterSummary();
                    renderLocationFilters();
                    renderUnitFilters();
                    renderCertificationFilters();
                    renderRelatedSection();
                    renderResultGrid([]);
                    updateSummary(0);
                }
            };

            const runFilterAndRender = () => {
                renderPresetPills();
                renderActiveFilterSummary();
                syncFilterInputsFromState();
                syncUrl();
                reloadProducts();
            };

            document.addEventListener("change", (event) => {
                const target = event.target;
                if (!(target instanceof HTMLInputElement)) return;

                const type = target.getAttribute("data-filter-type");
                if (type === "category") {
                    const categoryId = toNumber(target.value, 0);
                    if (categoryId <= 0) {
                        return;
                    }

                    if (target.checked) state.selectedCategoryIds.add(categoryId);
                    else state.selectedCategoryIds.delete(categoryId);

                    state.page = 1;
                    syncUrl();
                    reloadProducts();
                    return;
                }

                if (type === "location") {
                    if (target.checked) state.selectedLocations.add(target.value);
                    else state.selectedLocations.delete(target.value);
                    state.page = 1;
                    syncUrl();
                    reloadProducts();
                    return;
                }

                if (type === "delivery-scope") {
                    const scopeKey = (target.value || "").trim();
                    if (!scopeKey) {
                        return;
                    }

                    if (target.checked) state.selectedDeliveryScopes.add(scopeKey);
                    else state.selectedDeliveryScopes.delete(scopeKey);
                    state.page = 1;
                    syncUrl();
                    reloadProducts();
                    return;
                }

                if (type === "shop") {
                    const sellerId = toNumber(target.value, 0);
                    if (sellerId <= 0) {
                        return;
                    }

                    if (target.checked) state.selectedSellerIds.add(sellerId);
                    else state.selectedSellerIds.delete(sellerId);
                    state.page = 1;
                    syncUrl();
                    reloadProducts();
                    return;
                }

                if (type === "availability") {
                    const availabilityKey = (target.value || "").trim();
                    if (!availabilityKey) {
                        return;
                    }

                    if (target.checked) state.selectedAvailability.add(availabilityKey);
                    else state.selectedAvailability.delete(availabilityKey);
                    state.page = 1;
                    syncUrl();
                    reloadProducts();
                    return;
                }

                if (type === "unit") {
                    if (target.checked) state.selectedUnits.add(target.value);
                    else state.selectedUnits.delete(target.value);
                    state.page = 1;
                    syncUrl();
                    reloadProducts();
                    return;
                }

                if (type === "cert") {
                    if (target.checked) state.selectedCertifications.add(target.value);
                    else state.selectedCertifications.delete(target.value);
                    state.page = 1;
                    syncUrl();
                    reloadProducts();
                    return;
                }

                if (target.name === "ratingFilter") {
                    state.minRating = toNumber(target.value, 0);
                    state.page = 1;
                    runFilterAndRender();
                }
            });

            document.addEventListener("click", (event) => {
                const target = event.target;
                if (!(target instanceof HTMLElement)) return;

                const searchResultLink = target.closest("a[data-action='search-result-click']");
                if (searchResultLink instanceof HTMLAnchorElement) {
                    void appHelpers?.trackSearchClick?.({
                        searchEventId: state.latestSearchEventId,
                        productId: toNumber(searchResultLink.getAttribute("data-product-id"), 0),
                        sellerId: toNumber(searchResultLink.getAttribute("data-seller-id"), 0),
                        rank: toNumber(searchResultLink.getAttribute("data-rank"), 0)
                    });
                }

                const relatedLink = target.closest("a[data-action='related-recommendation-click']");
                if (relatedLink instanceof HTMLAnchorElement) {
                    const productId = toNumber(relatedLink.getAttribute("data-product-id"), 0);
                    const rank = toNumber(relatedLink.getAttribute("data-rank"), 0);
                    const impressionKey = buildRelatedImpressionKey(productId, rank);

                    void appHelpers?.trackRecommendationClick?.({
                        recommendationImpressionEventId: relatedImpressionIds.get(impressionKey) ?? null,
                        productId,
                        rank,
                        placement: relatedRecommendationPlacement,
                        algorithm: relatedRecommendationAlgorithm
                    });
                }

                const sortButton = target.closest(".btn-sort");
                if (sortButton instanceof HTMLButtonElement) {
                    document.querySelectorAll(".btn-sort").forEach((btn) => btn.classList.remove("active"));
                    sortButton.classList.add("active");
                    state.sortBy = sortButton.getAttribute("data-sort") || "related";
                    state.page = 1;
                    runFilterAndRender();
                    return;
                }

                const addCartBtn = target.closest("button[data-action='add-cart']");
                if (addCartBtn instanceof HTMLButtonElement) {
                    event.preventDefault();
                    const product = {
                        productId: toNumber(addCartBtn.getAttribute("data-product-id"), 0),
                        sellerId: toNumber(addCartBtn.getAttribute("data-seller-id"), 0),
                        sellerName: addCartBtn.getAttribute("data-seller-name") ?? "",
                        productName: addCartBtn.getAttribute("data-product-name") ?? "",
                        price: toNumber(addCartBtn.getAttribute("data-unit-price"), 0),
                        unitName: addCartBtn.getAttribute("data-unit-symbol") ?? "đơn vị"
                    };
                    void submitAddToCart(product, 1, addCartBtn);
                    return;
                }

                const removeCategoryBtn = target.closest("button[data-action='remove-category']");
                if (removeCategoryBtn instanceof HTMLButtonElement) {
                    const categoryId = toNumber(removeCategoryBtn.getAttribute("data-category-id"), 0);
                    if (categoryId > 0) {
                        state.selectedCategoryIds.delete(categoryId);
                        state.page = 1;
                        syncUrl();
                        reloadProducts();
                    }
                    return;
                }

                const removeShopBtn = target.closest("button[data-action='remove-shop']");
                if (removeShopBtn instanceof HTMLButtonElement) {
                    const sellerId = toNumber(removeShopBtn.getAttribute("data-seller-id"), 0);
                    if (sellerId > 0) {
                        state.selectedSellerIds.delete(sellerId);
                        state.page = 1;
                        syncUrl();
                        reloadProducts();
                    }
                    return;
                }

                const removeAvailabilityBtn = target.closest("button[data-action='remove-availability']");
                if (removeAvailabilityBtn instanceof HTMLButtonElement) {
                    const availabilityKey = (removeAvailabilityBtn.getAttribute("data-availability-key") || "").trim();
                    if (availabilityKey) {
                        state.selectedAvailability.delete(availabilityKey);
                        state.page = 1;
                        syncUrl();
                        reloadProducts();
                    }
                    return;
                }

                const removeDeliveryScopeBtn = target.closest("button[data-action='remove-delivery-scope']");
                if (removeDeliveryScopeBtn instanceof HTMLButtonElement) {
                    const value = (removeDeliveryScopeBtn.getAttribute("data-delivery-scope-key") || "").trim();
                    if (value) {
                        state.selectedDeliveryScopes.delete(value);
                        state.page = 1;
                        syncUrl();
                        reloadProducts();
                    }
                    return;
                }

                const removeLocationBtn = target.closest("button[data-action='remove-location']");
                if (removeLocationBtn instanceof HTMLButtonElement) {
                    const value = (removeLocationBtn.getAttribute("data-filter-value") || "").trim();
                    if (value) {
                        state.selectedLocations.delete(value);
                        state.page = 1;
                        syncUrl();
                        reloadProducts();
                    }
                    return;
                }

                const removeUnitBtn = target.closest("button[data-action='remove-unit']");
                if (removeUnitBtn instanceof HTMLButtonElement) {
                    const value = (removeUnitBtn.getAttribute("data-filter-value") || "").trim();
                    if (value) {
                        state.selectedUnits.delete(value);
                        state.page = 1;
                        syncUrl();
                        reloadProducts();
                    }
                    return;
                }

                const removeStandardBtn = target.closest("button[data-action='remove-standard']");
                if (removeStandardBtn instanceof HTMLButtonElement) {
                    const value = (removeStandardBtn.getAttribute("data-filter-value") || "").trim();
                    if (value) {
                        state.selectedCertifications.delete(value);
                        state.page = 1;
                        syncUrl();
                        reloadProducts();
                    }
                    return;
                }

                const removePriceBtn = target.closest("button[data-action='remove-price-range']");
                if (removePriceBtn instanceof HTMLButtonElement) {
                    state.minPrice = null;
                    state.maxPrice = null;
                    if (minPriceInput instanceof HTMLInputElement) minPriceInput.value = "";
                    if (maxPriceInput instanceof HTMLInputElement) maxPriceInput.value = "";
                    state.page = 1;
                    runFilterAndRender();
                    return;
                }

                const removeRatingBtn = target.closest("button[data-action='remove-rating']");
                if (removeRatingBtn instanceof HTMLButtonElement) {
                    state.minRating = 0;
                    const ratingAll = document.querySelector('input[name="ratingFilter"][value="0"]');
                    if (ratingAll instanceof HTMLInputElement) ratingAll.checked = true;
                    state.page = 1;
                    runFilterAndRender();
                    return;
                }

                const removeSortBtn = target.closest("button[data-action='remove-sort']");
                if (removeSortBtn instanceof HTMLButtonElement) {
                    state.sortBy = "related";
                    document.querySelectorAll(".btn-sort").forEach((btn) => {
                        const sort = btn.getAttribute("data-sort");
                        btn.classList.toggle("active", sort === "related");
                    });
                    state.page = 1;
                    runFilterAndRender();
                    return;
                }

                const removePresetBtn = target.closest("button[data-action='remove-preset']");
                if (removePresetBtn instanceof HTMLButtonElement) {
                    clearPresetEffects();
                    state.activePreset = "";
                    syncUrl();
                    reloadProducts();
                    return;
                }

                const categoryPillBtn = target.closest("button[data-action='toggle-category-pill']");
                if (categoryPillBtn instanceof HTMLButtonElement) {
                    const categoryId = toNumber(categoryPillBtn.getAttribute("data-category-id"), 0);
                    if (categoryId > 0) {
                        if (state.selectedCategoryIds.has(categoryId)) {
                            state.selectedCategoryIds.delete(categoryId);
                        } else {
                            state.selectedCategoryIds.add(categoryId);
                        }

                        state.page = 1;
                        syncUrl();
                        reloadProducts();
                    }
                    return;
                }

                const presetBtn = target.closest("button[data-action='apply-preset']");
                if (presetBtn instanceof HTMLButtonElement) {
                    const presetKey = (presetBtn.getAttribute("data-preset-key") || "").trim();
                    if (presetKey) {
                        applyPreset(presetKey);
                        state.page = 1;
                        syncUrl();
                        reloadProducts();
                    }
                    return;
                }

                const pageBtn = target.closest("button[data-action='goto-page']");
                if (pageBtn instanceof HTMLButtonElement) {
                    const nextPage = toNumber(pageBtn.getAttribute("data-page"), 0);
                    if (nextPage > 0 && nextPage !== state.page) {
                        state.page = nextPage;
                        runFilterAndRender();
                    }
                }
            });

            applyPriceBtn?.addEventListener("click", () => {
                const min = minPriceInput instanceof HTMLInputElement ? minPriceInput.value.trim() : "";
                const max = maxPriceInput instanceof HTMLInputElement ? maxPriceInput.value.trim() : "";
                state.minPrice = min === "" ? null : Math.max(0, toNumber(min, 0));
                state.maxPrice = max === "" ? null : Math.max(0, toNumber(max, 0));

                if (typeof state.minPrice === "number" && typeof state.maxPrice === "number" && state.minPrice > state.maxPrice) {
                    const tmp = state.minPrice;
                    state.minPrice = state.maxPrice;
                    state.maxPrice = tmp;
                    if (minPriceInput instanceof HTMLInputElement) minPriceInput.value = String(state.minPrice);
                    if (maxPriceInput instanceof HTMLInputElement) maxPriceInput.value = String(state.maxPrice);
                }

                state.page = 1;
                runFilterAndRender();
            });

            clearFilterBtn?.addEventListener("click", () => {
                state.minPrice = null;
                state.maxPrice = null;
                state.minRating = 0;
                state.selectedCategoryIds.clear();
                state.selectedSellerIds.clear();
                state.selectedAvailability.clear();
                state.selectedDeliveryScopes.clear();
                state.selectedLocations.clear();
                state.selectedUnits.clear();
                state.selectedCertifications.clear();
                state.presetAvailabilityKeys.clear();
                state.presetDeliveryScopeKeys.clear();
                state.presetAppliedSort = "";
                state.activePreset = "";
                state.sortBy = "related";
                state.page = 1;

                if (minPriceInput instanceof HTMLInputElement) minPriceInput.value = "";
                if (maxPriceInput instanceof HTMLInputElement) maxPriceInput.value = "";

                document.querySelectorAll("input[data-filter-type]").forEach((input) => {
                    if (input instanceof HTMLInputElement) input.checked = false;
                });
                const ratingAll = document.querySelector('input[name="ratingFilter"][value="0"]');
                if (ratingAll instanceof HTMLInputElement) ratingAll.checked = true;

                document.querySelectorAll(".btn-sort").forEach((btn) => {
                    const sort = btn.getAttribute("data-sort");
                    btn.classList.toggle("active", sort === "related");
                });

                syncUrl();
                reloadProducts();
            });

            searchForm?.addEventListener("submit", (event) => {
                event.preventDefault();
                const value = searchInput instanceof HTMLInputElement ? searchInput.value.trim() : "";
                state.keyword = value;
                state.page = 1;
                syncUrl();
                reloadProducts();
            });

            document.querySelectorAll("[data-header-keyword]").forEach((link) => {
                link.addEventListener("click", (event) => {
                    event.preventDefault();
                    const value = (link.getAttribute("data-header-keyword") || "").trim();
                    state.keyword = value;
                    state.page = 1;
                    if (searchInput instanceof HTMLInputElement) searchInput.value = value;
                    syncUrl();
                    reloadProducts();
                });
            });

            const initializeSearch = async () => {
                try {
                    await loadCategories();
                } catch (error) {
                    console.error(error);
                    state.availableCategories = [];
                    renderCategoryFilters();
                    renderCategoryPills();
                    renderPresetPills();
                    renderShopFilters();
                    renderAvailabilityFilters();
                    renderActiveFilterSummary();
                }

                syncFilterInputsFromState();
                if (state.activePreset) {
                    applyPreset(state.activePreset);
                }
                renderPresetPills();
                syncUrl();
                await reloadProducts();
            };

            void initializeSearch();
        })();
