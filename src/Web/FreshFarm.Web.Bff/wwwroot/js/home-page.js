(function () {
    const initHomePage = () => {
        const categoryWrapper = document.getElementById("categoryWrapper");
        const homeRecommendationSections = document.getElementById("homeRecommendationSections");
        const featuredGrid = document.getElementById("featuredGrid");
        const newArrivalsGrid = document.getElementById("newArrivalsGrid");
        const bestSellersGrid = document.getElementById("bestSellersGrid");
        const suggestionEmpty = document.getElementById("suggestionEmpty");
        const featuredEmpty = document.getElementById("featuredEmpty");
        const newArrivalsEmpty = document.getElementById("newArrivalsEmpty");
        const bestSellersEmpty = document.getElementById("bestSellersEmpty");
        const searchForm = document.getElementById("headerSearchForm");
        const searchInput = document.getElementById("headerSearchInput");
        const btnPrev = document.getElementById("btnPrev");
        const btnNext = document.getElementById("btnNext");
        const antiForgeryToken = document.querySelector("#homeAntiForgeryForm input[name='__RequestVerificationToken']")?.value ?? "";
        const homePageConfig = document.getElementById("homePageConfig");
        const fallbackImagePath = homePageConfig?.getAttribute("data-fallback-image") ?? "/Images/no-image.png";
        const uploadFolderPath = homePageConfig?.getAttribute("data-upload-folder") ?? "/uploads/products/";
        const appHelpers = window.FreshFarmApp;

        if (!(categoryWrapper instanceof HTMLElement)
            || !(homeRecommendationSections instanceof HTMLElement)
            || !(featuredGrid instanceof HTMLElement)
            || !(newArrivalsGrid instanceof HTMLElement)
            || !(bestSellersGrid instanceof HTMLElement)
            || !(suggestionEmpty instanceof HTMLElement)
            || !(featuredEmpty instanceof HTMLElement)
            || !(newArrivalsEmpty instanceof HTMLElement)
            || !(bestSellersEmpty instanceof HTMLElement)
            || !(searchForm instanceof HTMLFormElement)
            || !(searchInput instanceof HTMLInputElement)
            || !(btnPrev instanceof HTMLElement)
            || !(btnNext instanceof HTMLElement)) {
            return;
        }

        const suggestionRecommendationPlacement = "home_today";
        let suggestionRecommendationAlgorithm = "catalog_fallback";
        let suggestionRecommendationRunId = "";
        const suggestionImpressionIds = new Map();

        const endpointBase = "/bff/products";
        const recommendationEndpoint = "/bff/recommendations/home?limit=40";
        const recommendationInitialCount = 8;
        const recommendationLoadStep = 8;
        const categoryImages = [
            "https://images.pexels.com/photos/1435904/pexels-photo-1435904.jpeg",
            "https://images.pexels.com/photos/1656666/pexels-photo-1656666.jpeg",
            "https://images.pexels.com/photos/1327838/pexels-photo-1327838.jpeg",
            "https://images.pexels.com/photos/4198019/pexels-photo-4198019.jpeg",
            "https://images.pexels.com/photos/2090902/pexels-photo-2090902.jpeg"
        ];
        let cartToastTimer = 0;

        const escapeHtml = (value) => {
            const text = value ?? "";
            return text
                .replaceAll("&", "&amp;")
                .replaceAll("<", "&lt;")
                .replaceAll(">", "&gt;")
                .replaceAll('"', "&quot;")
                .replaceAll("'", "&#39;");
        };

        const toNumber = (value, fallback = 0) => {
            const n = Number(value);
            return Number.isFinite(n) ? n : fallback;
        };

        const toVnd = (value) => `${toNumber(value, 0).toLocaleString("vi-VN")} ₫`;

        const resolveProductImage = (value) => {
            const raw = (value ?? "").toString().trim();
            if (!raw || raw.toLowerCase() === "no-image.png") {
                return fallbackImagePath;
            }

            if (/^(https?:)?\/\//i.test(raw) || raw.startsWith("/")) {
                return raw;
            }

            return `${uploadFolderPath}${raw}`;
        };

        const normalizeProduct = (raw) => {
            const productId = raw.productID ?? raw.productId ?? raw.ProductID ?? raw.ProductId ?? 0;
            const productName = raw.productName ?? raw.ProductName ?? "Không rõ tên";
            const categoryName = raw.categoryName ?? raw.CategoryName ?? "Chưa phân loại";
            const unitName = raw.unitName ?? raw.UnitName ?? "đơn vị";
            const price = raw.price ?? raw.Price ?? 0;
            const sellerId = raw.sellerId ?? raw.SellerId ?? raw.primarySellerId ?? raw.PrimarySellerId ?? 0;
            const sellerName = raw.sellerName ?? raw.SellerName ?? raw.shopName ?? raw.ShopName ?? "";
            const imageFileName = raw.imageFileName ?? raw.ImageFileName ?? raw.imageUrl ?? raw.ImageUrl ?? "";
            const createdAt = raw.createdAt ?? raw.CreatedAt ?? raw.createdDate ?? raw.CreatedDate ?? null;
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
                categoryName,
                unitName,
                price,
                sellerId,
                sellerName,
                imageFileName,
                createdAt,
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
            ? `<div class="recommendation-badge-row seasonality-badge-row"><span class="recommendation-badge seasonality-badge">${escapeHtml(product.seasonalityBadgeLabel)}</span></div>`
            : "";

        const renderRecommendationMeta = (product, maxTags = 2) => {
            const tags = Array.isArray(product.recommendationTags)
                ? product.recommendationTags.filter((tag) => (tag || "").toString().trim().length > 0).slice(0, maxTags)
                : [];
            const reason = (product.recommendationReason || "").toString().trim();
            if (!reason && tags.length === 0) {
                return "";
            }

            return `
                <div class="recommendation-meta">
                    ${tags.length > 0 ? `
                        <div class="recommendation-badge-row">
                            ${tags.map((tag) => `
                                <span class="recommendation-badge">${escapeHtml(tag)}</span>
                            `).join("")}
                        </div>
                    ` : ""}
                    ${reason ? `<div class="recommendation-reason">${escapeHtml(reason)}</div>` : ""}
                </div>
            `;
        };

        const buildCheckoutUrl = (product, qty = 1) => {
            const query = new URLSearchParams({
                productId: String(product.productId),
                productName: product.productName,
                unitPrice: String(product.price),
                unitSymbol: product.unitName,
                quantity: String(qty)
            });

            const sellerId = toNumber(product.sellerId, 0);
            if (sellerId > 0) {
                query.set("sellerId", String(sellerId));
            }

            const sellerName = (product.sellerName ?? "").toString().trim();
            if (sellerName) {
                query.set("sellerName", sellerName);
            }

            return `/checkout?${query.toString()}`;
        };

        const buildProductUrl = (product) => {
            const productId = toNumber(product.productId, 0);
            return productId > 0 ? `/products/${productId}` : "/search";
        };

        const buildSuggestionImpressionKey = (productId, rank) =>
            `${toNumber(productId, 0)}:${Math.max(0, toNumber(rank, 0))}`;

        const registerSuggestionImpressions = (suggestions) => {
            if (!appHelpers?.trackRecommendationImpression || !Array.isArray(suggestions) || suggestions.length === 0) {
                return;
            }

            suggestionRecommendationRunId = appHelpers.createRecommendationRunId?.(suggestionRecommendationPlacement) ?? "";
            suggestionImpressionIds.clear();

            suggestions.forEach((product, index) => {
                const rank = index + 1;
                const impressionKey = buildSuggestionImpressionKey(product.productId, rank);

                void appHelpers.trackRecommendationImpression({
                    placement: suggestionRecommendationPlacement,
                    recommendationRunId: suggestionRecommendationRunId,
                    productId: product.productId,
                    rank,
                    algorithm: suggestionRecommendationAlgorithm
                }).then((eventId) => {
                    if (eventId) {
                        suggestionImpressionIds.set(impressionKey, eventId);
                    }
                });
            });
        };

        const showCartToast = (message, tone = "success") => {
            let toast = document.getElementById("homeCartToast");
            if (!(toast instanceof HTMLElement)) {
                toast = document.createElement("div");
                toast.id = "homeCartToast";
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

            window.clearTimeout(cartToastTimer);
            cartToastTimer = window.setTimeout(() => {
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
                body.set("SellerId", String(Math.max(0, toNumber(product.sellerId, 0))));
                body.set("SellerName", (product.sellerName ?? "").toString());
                body.set("ProductName", (product.productName ?? "").toString());
                body.set("ImageFileName", (product.imageFileName ?? "").toString());
                body.set("UnitPrice", String(product.price ?? 0));
                body.set("UnitSymbol", (product.unitName ?? "").toString());
                body.set("Quantity", String(Math.max(1, toNumber(qty, 1))));

                const response = await fetch("/cart/add", {
                    method: "POST",
                    credentials: "same-origin",
                    headers: {
                        Accept: "application/json",
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

        const buildCard = (product, sizeClass = "col-6 col-md-4 col-lg-3") => `
            <div class="${sizeClass}">
                <div class="card product-card h-100 shadow-sm border-0 rounded-3 position-relative">
                    <div class="product-image-wrapper">
                        <img src="${resolveProductImage(product.imageFileName)}" class="product-image" alt="${escapeHtml(product.productName)}" />
                    </div>
                    <div class="product-overlay">
                        <h5 class="h6 mb-2">${escapeHtml(product.productName)}</h5>
                        <div class="d-flex align-items-center justify-content-center gap-2 mb-2">
                            <span class="fw-bold fs-5">${toVnd(product.price)}</span>
                        </div>
                        <a href="${buildProductUrl(product)}"
                           class="view-details mb-3"
                           ${product.recommendationRank ? `data-action="suggestion-click" data-product-id="${product.productId}" data-rank="${product.recommendationRank}"` : ""}>
                            <i class="bi bi-eye me-1"></i> Xem chi tiết
                        </a>
                        <div class="d-flex flex-wrap gap-2 justify-content-center">
                            <button type="button"
                                    class="btn btn-outline-light btn-sm"
                                    data-action="add-cart"
                                    data-product-id="${product.productId}"
                                    data-seller-id="${toNumber(product.sellerId, 0)}"
                                    data-seller-name="${escapeHtml(product.sellerName)}"
                                    data-product-name="${escapeHtml(product.productName)}"
                                    data-unit-price="${toNumber(product.price, 0)}"
                                    data-unit-symbol="${escapeHtml(product.unitName)}">
                                <i class="bi bi-cart-plus me-1"></i> Thêm vào giỏ
                            </button>
                            <a href="${buildCheckoutUrl(product)}"
                               class="btn btn-success btn-sm"
                               ${product.recommendationRank ? `data-action="suggestion-click" data-product-id="${product.productId}" data-rank="${product.recommendationRank}"` : ""}>
                                <i class="bi bi-lightning-charge-fill me-1"></i> Mua ngay
                            </a>
                        </div>
                    </div>
                    <div class="card-body text-center">
                        <h5 class="card-title h6 mb-1">${escapeHtml(product.productName)}</h5>
                        <span class="fw-bold" style="color: var(--primary-green)">${toVnd(product.price)}</span>
                        ${renderSeasonalityBadge(product)}
                        ${renderRecommendationMeta(product, 2)}
                    </div>
                </div>
            </div>
        `;

        const renderCategories = (products) => {
            const categories = [];
            const seen = new Set();

            products.forEach((p) => {
                const key = (p.categoryName || "Chưa phân loại").trim();
                if (!seen.has(key)) {
                    seen.add(key);
                    categories.push(key);
                }
            });

            if (categories.length === 0) {
                categoryWrapper.innerHTML = "<div class='empty-state w-100'>Chưa có dữ liệu danh mục.</div>";
                return;
            }

            categoryWrapper.innerHTML = categories
                .map((name, index) => {
                    const image = categoryImages[index % categoryImages.length];
                    return `
                        <a href="#"
                           class="category-item d-flex align-items-end p-3 text-white text-decoration-none position-relative overflow-hidden rounded-4 shadow-sm"
                           data-category="${escapeHtml(name)}">
                            <img class="position-absolute top-0 start-0 w-100 h-100" src="${image}" alt="${escapeHtml(name)}" />
                            <div class="category-name position-relative fw-semibold z-2">${escapeHtml(name)}</div>
                        </a>
                    `;
                })
                .join("");

            categoryWrapper.querySelectorAll(".category-item").forEach((item) => {
                item.addEventListener("click", (event) => {
                    event.preventDefault();
                    const keyword = item.getAttribute("data-category") || "";
                    window.location.href = keyword ? `/search?q=${encodeURIComponent(keyword)}` : "/search";
                });
            });
        };

        const buildRecommendationSection = (section, defaultPillLabel) => {
            const items = Array.isArray(section.items) ? section.items : [];
            if (items.length === 0) {
                return "";
            }

            const pillLabel = (section.pillLabel || "").toString().trim() || defaultPillLabel;
            const sectionId = (section.id || "").toString().trim() || `section-${Math.random().toString(36).slice(2, 10)}`;
            const initialVisibleCount = Math.min(recommendationInitialCount, items.length);

            return `
                <section class="recommendation-section-card"
                         data-section-id="${escapeHtml(sectionId)}"
                         data-visible-count="${initialVisibleCount}"
                         data-total-count="${items.length}">
                    <div class="recommendation-section-header">
                        <div>
                            <h3 class="recommendation-section-title">${escapeHtml(section.title || "Gợi ý cho bạn")}</h3>
                            <p class="recommendation-section-subtitle">${escapeHtml(section.subtitle || "Những món hợp gu và dễ chọn cho hôm nay.")}</p>
                        </div>
                        <span class="recommendation-section-pill">${escapeHtml(pillLabel)}</span>
                    </div>
                    <div class="recommendation-section-grid">
                        ${items.map((product, index) => `
                            <div class="${index >= initialVisibleCount ? "recommendation-card-hidden" : ""}" data-recommendation-card>
                                ${buildCard(product, "")}
                            </div>
                        `).join("")}
                    </div>
                    ${items.length > initialVisibleCount ? `
                        <div class="recommendation-section-actions">
                            <button type="button"
                                    class="btn recommendation-load-more"
                                    data-action="recommendation-load-more">
                                <span>Xem thêm</span>
                                <i class="bi bi-chevron-down"></i>
                            </button>
                        </div>
                    ` : ""}
                </section>
            `;
        };

        const createRecommendationSections = (recommendationPayload, suggestions) => {
            return [
                {
                    id: "today_highlights",
                    title: "Gợi ý cho bạn hôm nay",
                    subtitle: "Những món được cá nhân hóa từ lịch sử xem, tìm kiếm, mua hàng và tín hiệu gần đây của bạn.",
                    pillLabel: "Cá nhân hóa",
                    items: suggestions.map((item, index) => ({
                        ...item,
                        recommendationRank: index + 1
                    }))
                }
            ].filter((section) => section.items.length > 0);
        };

        const renderSuggestions = (recommendationPayload, fallbackProducts) => {
            const recommendationItems = Array.isArray(recommendationPayload?.items)
                ? recommendationPayload.items.map(normalizeProduct)
                : [];
            const suggestions = recommendationItems.length > 0
                ? recommendationItems
                : (Array.isArray(fallbackProducts) ? fallbackProducts.slice(0, 40) : []);
            suggestionRecommendationAlgorithm = (recommendationPayload?.algorithm ?? "").toString().trim() || "catalog_fallback";
            const sections = createRecommendationSections(recommendationPayload, suggestions);

            if (suggestions.length === 0) {
                homeRecommendationSections.innerHTML = "";
                suggestionEmpty.classList.remove("d-none");
                suggestionImpressionIds.clear();
                return;
            }

            suggestionEmpty.classList.add("d-none");
            homeRecommendationSections.innerHTML = sections
                .map((section, index) => buildRecommendationSection(
                    section,
                    index === 0 ? "Gợi ý hợp gu" : section.id === "buy_again" ? "Ưu tiên mua lại" : "Theo mùa & địa phương"))
                .join("");

            registerSuggestionImpressions(suggestions);
        };

        const buildTrendingEndpoint = (excludedProductIds) => {
            const query = new URLSearchParams("limit=12");
            const uniqueIds = new Set(
                (Array.isArray(excludedProductIds) ? excludedProductIds : [])
                    .map((id) => toNumber(id, 0))
                    .filter((id) => id > 0));

            uniqueIds.forEach((id) => query.append("personalizedProductIds", String(id)));
            return `/bff/products/trending?${query.toString()}`;
        };

        const buildNewArrivalsEndpoint = (excludedProductIds) => {
            const query = new URLSearchParams("limit=12");
            const uniqueIds = new Set(
                (Array.isArray(excludedProductIds) ? excludedProductIds : [])
                    .map((id) => toNumber(id, 0))
                    .filter((id) => id > 0));

            uniqueIds.forEach((id) => query.append("excludeProductIds", String(id)));
            return `/bff/products/new-arrivals?${query.toString()}`;
        };

        const buildBestSellersEndpoint = (excludedProductIds) => {
            const query = new URLSearchParams("limit=12");
            const uniqueIds = new Set(
                (Array.isArray(excludedProductIds) ? excludedProductIds : [])
                    .map((id) => toNumber(id, 0))
                    .filter((id) => id > 0));

            uniqueIds.forEach((id) => query.append("excludeProductIds", String(id)));
            return `/bff/products/best-sellers?${query.toString()}`;
        };

        const revealMoreRecommendationCards = (sectionElement) => {
            if (!(sectionElement instanceof HTMLElement)) {
                return;
            }

            const cards = Array.from(sectionElement.querySelectorAll("[data-recommendation-card]"));
            const currentVisibleCount = Math.max(0, toNumber(sectionElement.getAttribute("data-visible-count"), recommendationInitialCount));
            const nextVisibleCount = Math.min(cards.length, currentVisibleCount + recommendationLoadStep);

            cards.forEach((card, index) => {
                if (!(card instanceof HTMLElement)) {
                    return;
                }

                card.classList.toggle("recommendation-card-hidden", index >= nextVisibleCount);
            });

            sectionElement.setAttribute("data-visible-count", String(nextVisibleCount));

            const loadMoreButton = sectionElement.querySelector("[data-action='recommendation-load-more']");
            if (!(loadMoreButton instanceof HTMLButtonElement)) {
                return;
            }

            if (nextVisibleCount >= cards.length) {
                loadMoreButton.closest(".recommendation-section-actions")?.remove();
            }
        };

        const renderTrending = (products, excludedProductIds = [], allowControlledOverlap = false) => {
            const excluded = new Set(
                (Array.isArray(excludedProductIds) ? excludedProductIds : [])
                    .map((id) => toNumber(id, 0))
                    .filter((id) => id > 0));
            const trending = products
                .filter((product) => allowControlledOverlap || !excluded.has(toNumber(product.productId, 0)))
                .slice(0, 12);
            if (trending.length === 0) {
                featuredGrid.innerHTML = "";
                featuredEmpty.classList.remove("d-none");
                return;
            }

            featuredEmpty.classList.add("d-none");
            featuredGrid.innerHTML = trending.map((p) => buildCard(p)).join("");
        };

        const renderNewArrivals = (products) => {
            const arrivals = products.slice(0, 12);
            if (arrivals.length === 0) {
                newArrivalsGrid.innerHTML = "";
                newArrivalsEmpty.classList.remove("d-none");
                return;
            }

            newArrivalsEmpty.classList.add("d-none");
            newArrivalsGrid.innerHTML = arrivals.map((p) => buildCard(p)).join("");
        };

        const renderBestSellers = (products) => {
            const bestSellers = products.slice(0, 12);
            if (bestSellers.length === 0) {
                bestSellersGrid.innerHTML = "";
                bestSellersEmpty.classList.remove("d-none");
                return;
            }

            bestSellersEmpty.classList.add("d-none");
            bestSellersGrid.innerHTML = bestSellers.map((p) => buildCard(p)).join("");
        };

        document.addEventListener("click", (event) => {
            const target = event.target;
            if (!(target instanceof HTMLElement)) {
                return;
            }

            const suggestionLink = target.closest("a[data-action='suggestion-click']");
            if (suggestionLink instanceof HTMLAnchorElement) {
                const productId = toNumber(suggestionLink.getAttribute("data-product-id"), 0);
                const rank = toNumber(suggestionLink.getAttribute("data-rank"), 0);
                const impressionKey = buildSuggestionImpressionKey(productId, rank);

                void appHelpers?.trackRecommendationClick?.({
                    recommendationImpressionEventId: suggestionImpressionIds.get(impressionKey) ?? null,
                    productId,
                    rank,
                    placement: suggestionRecommendationPlacement,
                    algorithm: suggestionRecommendationAlgorithm
                });
            }

            const addCartButton = target.closest("button[data-action='add-cart']");
            if (addCartButton instanceof HTMLButtonElement) {
                event.preventDefault();

                const product = {
                    productId: toNumber(addCartButton.getAttribute("data-product-id"), 0),
                    sellerId: toNumber(addCartButton.getAttribute("data-seller-id"), 0),
                    sellerName: addCartButton.getAttribute("data-seller-name") ?? "",
                    productName: addCartButton.getAttribute("data-product-name") ?? "",
                    price: toNumber(addCartButton.getAttribute("data-unit-price"), 0),
                    unitName: addCartButton.getAttribute("data-unit-symbol") ?? "đơn vị"
                };

                void submitAddToCart(product, 1, addCartButton);
                return;
            }

            const loadMoreButton = target.closest("button[data-action='recommendation-load-more']");
            if (!(loadMoreButton instanceof HTMLButtonElement)) {
                return;
            }

            event.preventDefault();
            revealMoreRecommendationCards(loadMoreButton.closest(".recommendation-section-card"));
        });

        const loadHomeProducts = async (keyword = "") => {
            try {
                const url = keyword
                    ? `${endpointBase}?name=${encodeURIComponent(keyword)}`
                    : endpointBase;

                const [productResponse, recommendationResult] = await Promise.all([
                    fetch(url, {
                        method: "GET",
                        headers: { Accept: "application/json" }
                    }),
                    fetch(recommendationEndpoint, {
                        method: "GET",
                        headers: { Accept: "application/json" }
                    }).catch(() => null)
                ]);

                const { payload: productPayload } = appHelpers
                    ? await appHelpers.tryParsePayload(productResponse)
                    : { payload: await productResponse.json() };
                if (!productResponse.ok) {
                    throw (appHelpers?.createHttpError(productResponse, productPayload, "Không tải được sản phẩm.")
                        ?? new Error(`Không tải được sản phẩm: ${productResponse.status}`));
                }

                const products = Array.isArray(productPayload)
                    ? productPayload.map(normalizeProduct)
                    : [];
                let recommendationData = null;
                if (recommendationResult instanceof Response) {
                    const { payload: recommendationPayload } = appHelpers
                        ? await appHelpers.tryParsePayload(recommendationResult)
                        : { payload: await recommendationResult.json() };
                    recommendationData = recommendationResult.ok ? recommendationPayload : null;
                }

                const personalizedProductIds = Array.isArray(recommendationData?.items)
                    ? recommendationData.items
                        .map((item) => toNumber(item.productId ?? item.ProductId, 0))
                        .filter((id) => id > 0)
                    : [];
                let trendingProducts = [];
                try {
                    const trendingResponse = await fetch(buildTrendingEndpoint(personalizedProductIds), {
                        method: "GET",
                        headers: { Accept: "application/json" }
                    });
                    const { payload: trendingPayload } = appHelpers
                        ? await appHelpers.tryParsePayload(trendingResponse)
                        : { payload: await trendingResponse.json() };
                    if (trendingResponse.ok && Array.isArray(trendingPayload?.items)) {
                        trendingProducts = trendingPayload.items.map(normalizeProduct);
                    }
                } catch {
                    trendingProducts = [];
                }

                const trendingProductIds = trendingProducts
                    .map((item) => toNumber(item.productId, 0))
                    .filter((id) => id > 0);
                let newArrivalProducts = [];
                try {
                    const newArrivalsResponse = await fetch(buildNewArrivalsEndpoint([
                        ...personalizedProductIds,
                        ...trendingProductIds
                    ]), {
                        method: "GET",
                        headers: { Accept: "application/json" }
                    });
                    const { payload: newArrivalsPayload } = appHelpers
                        ? await appHelpers.tryParsePayload(newArrivalsResponse)
                        : { payload: await newArrivalsResponse.json() };
                    if (newArrivalsResponse.ok && Array.isArray(newArrivalsPayload?.items)) {
                        newArrivalProducts = newArrivalsPayload.items.map(normalizeProduct);
                    }
                } catch {
                    newArrivalProducts = [];
                }
                const newArrivalProductIds = newArrivalProducts
                    .map((item) => toNumber(item.productId, 0))
                    .filter((id) => id > 0);
                let bestSellerProducts = [];
                try {
                    const bestSellersResponse = await fetch(buildBestSellersEndpoint([
                        ...personalizedProductIds,
                        ...trendingProductIds,
                        ...newArrivalProductIds
                    ]), {
                        method: "GET",
                        headers: { Accept: "application/json" }
                    });
                    const { payload: bestSellersPayload } = appHelpers
                        ? await appHelpers.tryParsePayload(bestSellersResponse)
                        : { payload: await bestSellersResponse.json() };
                    if (bestSellersResponse.ok && Array.isArray(bestSellersPayload?.items)) {
                        bestSellerProducts = bestSellersPayload.items.map(normalizeProduct);
                    }
                } catch {
                    bestSellerProducts = [];
                }

                renderCategories(products);
                renderSuggestions(recommendationData, products);
                renderTrending(
                    trendingProducts.length > 0 ? trendingProducts : products,
                    personalizedProductIds,
                    trendingProducts.length > 0);
                renderNewArrivals(newArrivalProducts);
                renderBestSellers(bestSellerProducts);
            } catch (error) {
                categoryWrapper.innerHTML = "<div class='empty-state w-100'>Không tải được danh mục.</div>";
                homeRecommendationSections.innerHTML = "";
                featuredGrid.innerHTML = "";
                newArrivalsGrid.innerHTML = "";
                bestSellersGrid.innerHTML = "";
                suggestionEmpty.classList.remove("d-none");
                featuredEmpty.classList.remove("d-none");
                newArrivalsEmpty.classList.remove("d-none");
                bestSellersEmpty.classList.remove("d-none");
                console.error(error);
            }
        };

        searchForm.addEventListener("submit", (event) => {
            event.preventDefault();
            const keyword = searchInput.value.trim();
            window.location.href = keyword ? `/search?q=${encodeURIComponent(keyword)}` : "/search";
        });

        document.querySelectorAll("[data-header-keyword]").forEach((link) => {
            link.addEventListener("click", (event) => {
                event.preventDefault();
                const keyword = (link.getAttribute("data-header-keyword") || "").trim();
                searchInput.value = keyword;
                window.location.href = keyword ? `/search?q=${encodeURIComponent(keyword)}` : "/search";
            });
        });

        const step = 260;
        btnPrev.addEventListener("click", () => categoryWrapper.scrollBy({ left: -step, behavior: "smooth" }));
        btnNext.addEventListener("click", () => categoryWrapper.scrollBy({ left: step, behavior: "smooth" }));

        void loadHomeProducts();
    };

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", initHomePage);
    } else {
        initHomePage();
    }
})();
