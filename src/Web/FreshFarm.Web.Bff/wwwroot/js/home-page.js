(function () {
    const initHomePage = () => {
        const categoryWrapper = document.getElementById("categoryWrapper");
        const suggestionWrapper = document.getElementById("suggestionWrapper");
        const featuredGrid = document.getElementById("featuredGrid");
        const suggestionEmpty = document.getElementById("suggestionEmpty");
        const featuredEmpty = document.getElementById("featuredEmpty");
        const searchForm = document.getElementById("headerSearchForm");
        const searchInput = document.getElementById("headerSearchInput");
        const btnPrev = document.getElementById("btnPrev");
        const btnNext = document.getElementById("btnNext");
        const antiForgeryToken = document.querySelector("#homeAntiForgeryForm input[name='__RequestVerificationToken']")?.value ?? "";
        const homePageConfig = document.getElementById("homePageConfig");
        const fallbackImagePath = homePageConfig?.getAttribute("data-fallback-image") ?? "/Images/no-image.png";
        const uploadFolderPath = homePageConfig?.getAttribute("data-upload-folder") ?? "/uploads/products/";

        if (!(categoryWrapper instanceof HTMLElement)
            || !(suggestionWrapper instanceof HTMLElement)
            || !(featuredGrid instanceof HTMLElement)
            || !(suggestionEmpty instanceof HTMLElement)
            || !(featuredEmpty instanceof HTMLElement)
            || !(searchForm instanceof HTMLFormElement)
            || !(searchInput instanceof HTMLInputElement)
            || !(btnPrev instanceof HTMLElement)
            || !(btnNext instanceof HTMLElement)) {
            return;
        }

        let swiperInstance = null;

        const endpointBase = "/bff/products";
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
            return { productId, productName, categoryName, unitName, price, sellerId, sellerName, imageFileName };
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

                const text = await response.text();
                let payload = null;

                try {
                    payload = text ? JSON.parse(text) : null;
                } catch {
                    payload = null;
                }

                if (!response.ok) {
                    throw new Error(payload?.message || `HTTP ${response.status}`);
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
                        <a href="${buildProductUrl(product)}" class="view-details mb-3">
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
                            <a href="${buildCheckoutUrl(product)}" class="btn btn-success btn-sm">
                                <i class="bi bi-lightning-charge-fill me-1"></i> Mua ngay
                            </a>
                        </div>
                    </div>
                    <div class="card-body text-center">
                        <h5 class="card-title h6 mb-1">${escapeHtml(product.productName)}</h5>
                        <span class="fw-bold" style="color: var(--primary-green)">${toVnd(product.price)}</span>
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

        const renderSuggestions = (products) => {
            const suggestions = products.slice(0, 12);
            const swiperHost = document.querySelector(".mySwiper-suggest");
            const hasSwiperRuntime = typeof window.Swiper === "function";

            if (suggestions.length === 0) {
                suggestionWrapper.innerHTML = "";
                suggestionEmpty.classList.remove("d-none");
                if (swiperHost instanceof HTMLElement) {
                    swiperHost.classList.remove("suggestion-fallback-grid");
                }
                if (swiperInstance) {
                    swiperInstance.destroy(true, true);
                    swiperInstance = null;
                }
                return;
            }

            suggestionEmpty.classList.add("d-none");
            if (swiperHost instanceof HTMLElement) {
                swiperHost.classList.toggle("suggestion-fallback-grid", !hasSwiperRuntime);
            }

            suggestionWrapper.innerHTML = suggestions
                .map((p) => {
                    const cardMarkup = `
                        <div class="card product-card h-100 shadow-sm border-0 rounded-3 position-relative">
                            <div class="product-image-wrapper">
                                <img src="${resolveProductImage(p.imageFileName)}" class="product-image" alt="${escapeHtml(p.productName)}" />
                            </div>
                            <div class="product-overlay">
                                <h5 class="h6 mb-2">${escapeHtml(p.productName)}</h5>
                                <div class="d-flex align-items-center justify-content-center gap-2 mb-2">
                                    <span class="fw-bold fs-5">${toVnd(p.price)}</span>
                                </div>
                                <a href="${buildProductUrl(p)}" class="view-details mb-3">
                                    <i class="bi bi-eye me-1"></i> Xem chi tiết
                                </a>
                                <div class="d-flex flex-wrap gap-2 justify-content-center">
                                    <button type="button"
                                            class="btn btn-outline-light btn-sm"
                                            data-action="add-cart"
                                            data-product-id="${p.productId}"
                                            data-seller-id="${toNumber(p.sellerId, 0)}"
                                            data-seller-name="${escapeHtml(p.sellerName)}"
                                            data-product-name="${escapeHtml(p.productName)}"
                                            data-unit-price="${toNumber(p.price, 0)}"
                                            data-unit-symbol="${escapeHtml(p.unitName)}">
                                        <i class="bi bi-cart-plus me-1"></i> Thêm vào giỏ
                                    </button>
                                    <a href="${buildCheckoutUrl(p)}" class="btn btn-success btn-sm">
                                        <i class="bi bi-lightning-charge-fill me-1"></i> Mua ngay
                                    </a>
                                </div>
                            </div>
                            <div class="card-body text-center">
                                <h5 class="card-title h6 mb-1">${escapeHtml(p.productName)}</h5>
                                <span class="fw-bold" style="color: var(--primary-green)">${toVnd(p.price)}</span>
                            </div>
                        </div>
                    `;

                    return hasSwiperRuntime
                        ? `<div class="swiper-slide">${cardMarkup}</div>`
                        : `<div>${cardMarkup}</div>`;
                })
                .join("");

            if (swiperInstance) {
                swiperInstance.destroy(true, true);
                swiperInstance = null;
            }

            if (!hasSwiperRuntime) {
                console.warn("Swiper runtime unavailable. Rendering suggestions in static grid mode.");
                return;
            }

            swiperInstance = new window.Swiper(".mySwiper-suggest", {
                slidesPerView: 2,
                spaceBetween: 16,
                breakpoints: {
                    576: { slidesPerView: 2 },
                    768: { slidesPerView: 3 },
                    992: { slidesPerView: 4 },
                    1200: { slidesPerView: 5 }
                },
                loop: suggestions.length > 5,
                autoplay: {
                    delay: 3500,
                    disableOnInteraction: false
                },
                navigation: {
                    nextEl: ".swiper-button-next",
                    prevEl: ".swiper-button-prev"
                }
            });

            const swiperEl = document.querySelector(".mySwiper-suggest");
            if (swiperEl instanceof HTMLElement && swiperInstance?.autoplay) {
                swiperEl.addEventListener("mouseenter", () => swiperInstance.autoplay.stop());
                swiperEl.addEventListener("mouseleave", () => swiperInstance.autoplay.start());
            }
        };

        const renderFeatured = (products) => {
            const featured = products.slice(0, 12);
            if (featured.length === 0) {
                featuredGrid.innerHTML = "";
                featuredEmpty.classList.remove("d-none");
                return;
            }

            featuredEmpty.classList.add("d-none");
            featuredGrid.innerHTML = featured.map((p) => buildCard(p)).join("");
        };

        document.addEventListener("click", (event) => {
            const target = event.target;
            if (!(target instanceof HTMLElement)) {
                return;
            }

            const addCartButton = target.closest("button[data-action='add-cart']");
            if (!(addCartButton instanceof HTMLButtonElement)) {
                return;
            }

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
        });

        const loadHomeProducts = async (keyword = "") => {
            try {
                const url = keyword
                    ? `${endpointBase}?name=${encodeURIComponent(keyword)}`
                    : endpointBase;

                const response = await fetch(url, {
                    method: "GET",
                    headers: { Accept: "application/json" }
                });

                if (!response.ok) {
                    throw new Error(`Không tải được sản phẩm: ${response.status}`);
                }

                const payload = await response.json();
                const products = Array.isArray(payload)
                    ? payload.map(normalizeProduct)
                    : [];

                renderCategories(products);
                renderSuggestions(products);
                renderFeatured(products);
            } catch (error) {
                categoryWrapper.innerHTML = "<div class='empty-state w-100'>Không tải được danh mục.</div>";
                suggestionWrapper.innerHTML = "";
                featuredGrid.innerHTML = "";
                suggestionEmpty.classList.remove("d-none");
                featuredEmpty.classList.remove("d-none");
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
