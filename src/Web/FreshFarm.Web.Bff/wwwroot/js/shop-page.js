(function () {
    const escapeHtml = (value) => (value ?? "").toString()
        .replaceAll("&", "&amp;")
        .replaceAll("<", "&lt;")
        .replaceAll(">", "&gt;")
        .replaceAll('"', "&quot;")
        .replaceAll("'", "&#39;");

    const resolveAvatar = (value) => {
        const raw = (value ?? "").toString().trim();
        if (!raw) {
            return "";
        }

        if (/^(https?:)?\/\//i.test(raw) || raw.startsWith("/")) {
            return raw;
        }

        return `/Images/${raw}`;
    };

    const formatDate = (value) => {
        if (!value) {
            return "Chưa rõ";
        }

        const date = new Date(value);
        return Number.isNaN(date.getTime())
            ? "Chưa rõ"
            : date.toLocaleDateString("vi-VN", { day: "2-digit", month: "2-digit", year: "numeric" });
    };

    const initShopPage = () => {
        const config = document.querySelector("[data-shop-page]");
        const appHelpers = window.FreshFarmApp;
        const loadingNode = document.getElementById("shopLoading");
        const errorNode = document.getElementById("shopError");
        const rootNode = document.getElementById("shopRoot");
        const antiForgeryInput = document.querySelector("#shopChatAntiForgeryForm input[name='__RequestVerificationToken']");

        if (!(config instanceof HTMLElement) || !(loadingNode instanceof HTMLElement) || !(errorNode instanceof HTMLElement) || !(rootNode instanceof HTMLElement)) {
            return;
        }

        const sellerId = Number(config.dataset.sellerId || "0");
        const isAuthenticated = config.dataset.isAuthenticated === "true";
        const isBuyerEligible = config.dataset.isBuyerEligible === "true";
        const signInUrl = config.dataset.signinUrl || `/account/signin?returnUrl=%2Fshop%2F${sellerId}%23shop-chat`;
        const shopEndpoint = config.dataset.shopEndpoint || `/bff/shops/${sellerId}`;
        const productsEndpoint = config.dataset.productsEndpoint || `/bff/products?sellerId=${sellerId}`;
        const productFallbackImage = config.dataset.productFallbackImage || "/images/no-image.png";
        const productUploadRoot = config.dataset.productUploadRoot || "/uploads/products/";
        const supportChatHubUrl = config.dataset.supportChatHubUrl || "/hubs/support-chat";
        const antiForgeryToken = antiForgeryInput instanceof HTMLInputElement ? antiForgeryInput.value : "";

        const resolveImage = (value) => {
            const raw = (value ?? "").toString().trim();
            if (!raw || raw.toLowerCase() === "no-image.png") {
                return productFallbackImage;
            }

            if (/^(https?:)?\/\//i.test(raw) || raw.startsWith("/")) {
                return raw;
            }

            return `${productUploadRoot}${raw}`;
        };

        const vnd = (value) => `${Number(value || 0).toLocaleString("vi-VN")} đ`;
        const showError = (message) => {
            loadingNode.classList.add("d-none");
            rootNode.classList.add("d-none");
            errorNode.classList.remove("d-none");
            errorNode.innerHTML = `<div class="fw-bold mb-2">Không tải được gian hàng</div><div>${escapeHtml(message)}</div>`;
        };

        Promise.all([
            window.fetch(shopEndpoint, { headers: { Accept: "application/json" } }),
            window.fetch(productsEndpoint, { headers: { Accept: "application/json" } })
        ])
            .then(async ([shopResp, productsResp]) => {
                const shopResult = appHelpers
                    ? await appHelpers.tryParsePayload(shopResp)
                    : { payload: await shopResp.json() };
                const productsResult = appHelpers
                    ? await appHelpers.tryParsePayload(productsResp)
                    : { payload: await productsResp.text().then((text) => {
                        if (!text) {
                            return null;
                        }

                        try {
                            return JSON.parse(text);
                        } catch {
                            return text;
                        }
                    }) };
                const shopPayload = shopResult.payload;
                const productsPayload = productsResult.payload;
                if (!shopResp.ok) {
                    throw (appHelpers?.createHttpError(shopResp, shopPayload, "Không tải được gian hàng.")
                        ?? new Error(shopPayload?.message || `HTTP ${shopResp.status}`));
                }

                let products = [];
                let productsWarning = "";
                if (productsResp.ok) {
                    products = Array.isArray(productsPayload) ? productsPayload : [];
                } else {
                    productsWarning = "Chưa tải được sản phẩm của shop. Bạn vẫn có thể xem thông tin shop và nhắn tin trực tiếp.";
                    console.warn("[shop-page] Could not load products for shop.", productsResp.status, productsPayload);
                }

                return {
                    shop: shopPayload,
                    products,
                    productsWarning
                };
            })
            .then(({ shop, products, productsWarning }) => {
                const avatar = resolveAvatar(shop.avatar ?? shop.Avatar);
                const shopName = (shop.shopName ?? shop.ShopName ?? "Shop chưa đặt tên").toString();
                const address = (shop.addressSummary ?? shop.AddressSummary ?? "Chưa cập nhật địa chỉ hoạt động").toString();
                const joinedAt = formatDate(shop.joinedAt ?? shop.JoinedAt);

                rootNode.innerHTML = `
                    <section class="shop-hero">
                        ${avatar ? `<img src="${avatar}" alt="${escapeHtml(shopName)}" class="shop-avatar" />` : `<div class="shop-fallback">${escapeHtml(shopName.slice(0, 1).toUpperCase())}</div>`}
                        <div>
                            <div class="shop-name">${escapeHtml(shopName)}</div>
                            <div class="shop-meta">
                                <span class="meta-chip"><i class="bi bi-person-badge"></i> @@${escapeHtml(shop.userName ?? shop.UserName ?? "")}</span>
                                <span class="meta-chip"><i class="bi bi-geo-alt"></i> ${escapeHtml(address)}</span>
                                <span class="meta-chip"><i class="bi bi-calendar-check"></i> Tham gia ${escapeHtml(joinedAt)}</span>
                                <span class="meta-chip"><i class="bi bi-shop"></i> Seller ID #${Number(shop.sellerId ?? shop.SellerId ?? sellerId)}</span>
                            </div>
                        </div>
                    </section>
                    <section class="shop-chat-card" id="shop-chat">
                        <div class="shop-chat-head">
                            <div>
                                <div class="shop-chat-kicker">Buyer-seller chat</div>
                                <h2>Trao đổi trực tiếp với shop</h2>
                            </div>
                            <div class="text-muted">Hỏi xuất xứ, lịch giao, đóng gói hoặc độ tươi trước khi mua.</div>
                        </div>
                        <div id="publicShopChat"></div>
                    </section>
                    <section>
                        <div class="grid-head">
                            <h2>Sản phẩm đang bán</h2>
                            <div class="text-muted">${products.length.toLocaleString("vi-VN")} sản phẩm công khai</div>
                        </div>
                        ${productsWarning ? `<div class="state-box mb-3">${escapeHtml(productsWarning)}</div>` : ""}
                        ${products.length === 0 ? `<div class="state-box">Shop này chưa có sản phẩm công khai.</div>` : `<div class="product-grid">
                            ${products.map((product) => `
                                <article class="product-card">
                                    <img src="${resolveImage(product.imageFileName ?? product.ImageFileName)}" alt="${escapeHtml(product.productName ?? product.ProductName ?? "")}" class="product-thumb" />
                                    <div class="product-body">
                                        <a class="product-name" href="/products/${Number(product.productId ?? product.ProductId ?? 0)}">${escapeHtml(product.productName ?? product.ProductName ?? "Sản phẩm")}</a>
                                        <div class="product-meta">${escapeHtml(product.categoryName ?? product.CategoryName ?? "Chưa phân loại")} · ${escapeHtml(product.unitName ?? product.UnitName ?? "đơn vị")}</div>
                                        <div class="product-meta">${escapeHtml(product.origin ?? product.Origin ?? "Chưa có xuất xứ")}</div>
                                        <div class="product-price">${vnd(product.price ?? product.Price ?? 0)}</div>
                                        <a class="product-link" href="/products/${Number(product.productId ?? product.ProductId ?? 0)}">Xem sản phẩm</a>
                                    </div>
                                </article>
                            `).join("")}
                        </div>`}
                    </section>
                `;

                loadingNode.classList.add("d-none");
                errorNode.classList.add("d-none");
                rootNode.classList.remove("d-none");

                if (window.PublicShopChat && typeof window.PublicShopChat.init === "function") {
                    window.PublicShopChat.init({
                        rootId: "publicShopChat",
                        sellerId,
                        isAuthenticated,
                        isBuyerEligible,
                        signInUrl,
                        antiForgeryToken,
                        hubUrl: supportChatHubUrl,
                        conversationUrl: `/bff/support-chat/sellers/${sellerId}/conversation`,
                        messagesUrlTemplate: "/bff/support-chat/conversations/__id__/messages",
                        sendUrlTemplate: "/bff/support-chat/conversations/__id__/messages",
                        markReadUrlTemplate: "/bff/support-chat/conversations/__id__/mark-read"
                    });
                }
            })
            .catch((error) => showError(error?.message || "Không thể tải dữ liệu shop."));
    };

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", initShopPage);
    } else {
        initShopPage();
    }
})();
