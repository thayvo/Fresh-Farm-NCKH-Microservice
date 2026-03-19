(function () {
    const escapeHtml = (value) => (value ?? "").toString()
        .replaceAll("&", "&amp;")
        .replaceAll("<", "&lt;")
        .replaceAll(">", "&gt;")
        .replaceAll('"', "&quot;")
        .replaceAll("'", "&#39;");

    const resolveAvatar = (value) => {
        const raw = (value ?? "").toString().trim();
        if (!raw || raw.toLowerCase() === "no-image.png") {
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

    const initShopsPage = () => {
        const form = document.querySelector("[data-shops-page]");
        const appHelpers = window.FreshFarmApp;
        const input = document.getElementById("shopSearchInput");
        const grid = document.getElementById("shopGrid");
        const summary = document.getElementById("shopSummary");
        const empty = document.getElementById("shopEmpty");

        if (!(form instanceof HTMLFormElement) || !(input instanceof HTMLInputElement) || !(grid instanceof HTMLElement) || !(summary instanceof HTMLElement) || !(empty instanceof HTMLElement)) {
            return;
        }

        const endpointBase = form.dataset.endpointBase || "/bff/shops";
        let state = { keyword: (form.dataset.initialKeyword || "").trim() };

        const syncUrl = () => window.history.replaceState(null, "", state.keyword ? `/shop?q=${encodeURIComponent(state.keyword)}` : "/shop");

        const render = (items, total) => {
            summary.textContent = `Tìm thấy ${total.toLocaleString("vi-VN")} shop`;
            if (!Array.isArray(items) || items.length === 0) {
                grid.innerHTML = "";
                empty.classList.remove("d-none");
                empty.textContent = state.keyword ? "Không tìm thấy shop phù hợp với từ khóa hiện tại." : "Chưa có shop công khai.";
                return;
            }

            empty.classList.add("d-none");
            grid.innerHTML = items.map((shop) => {
                const avatar = resolveAvatar(shop.avatar ?? shop.Avatar);
                const shopName = (shop.shopName ?? shop.ShopName ?? "Shop chưa đặt tên").toString();
                const sellerId = Number(shop.sellerId ?? shop.SellerId ?? 0);
                return `
                    <article class="shop-card">
                        <div class="shop-head">
                            ${avatar ? `<img src="${avatar}" alt="${escapeHtml(shopName)}" class="shop-avatar" />` : `<div class="shop-fallback">${escapeHtml(shopName.slice(0, 1).toUpperCase())}</div>`}
                            <div>
                                <div class="shop-name">${escapeHtml(shopName)}</div>
                                <div class="shop-user">@@${escapeHtml(shop.userName ?? shop.UserName ?? "")}</div>
                            </div>
                        </div>
                        <div class="shop-meta">
                            <div><i class="bi bi-geo-alt"></i> ${escapeHtml(shop.addressSummary ?? shop.AddressSummary ?? "Chưa cập nhật địa chỉ hoạt động")}</div>
                            <div><i class="bi bi-calendar-check"></i> Tham gia ${escapeHtml(formatDate(shop.joinedAt ?? shop.JoinedAt))}</div>
                            <div><i class="bi bi-shop"></i> Seller ID #${sellerId}</div>
                        </div>
                        <a href="/shop/${sellerId}" class="shop-link">Xem gian hàng</a>
                    </article>
                `;
            }).join("");
        };

        const load = async () => {
            const url = state.keyword ? `${endpointBase}?q=${encodeURIComponent(state.keyword)}` : endpointBase;
            const response = await window.fetch(url, { headers: { Accept: "application/json" } });
            const { payload } = appHelpers
                ? await appHelpers.tryParsePayload(response)
                : { payload: await response.json() };
            if (!response.ok) {
                throw (appHelpers?.createHttpError(response, payload, "Không thể tải danh sách shop.")
                    ?? new Error(payload?.message || `HTTP ${response.status}`));
            }
            render(payload.merchants ?? payload.Merchants ?? [], Number(payload.total ?? payload.Total ?? 0));
            syncUrl();
        };

        form.addEventListener("submit", (event) => {
            event.preventDefault();
            state.keyword = (input.value || "").trim();
            void load().catch((error) => {
                empty.classList.remove("d-none");
                empty.textContent = error?.message || "Không thể tải danh sách shop.";
                grid.innerHTML = "";
                summary.textContent = "Tìm thấy 0 shop";
            });
        });

        void load().catch((error) => {
            empty.classList.remove("d-none");
            empty.textContent = error?.message || "Không thể tải danh sách shop.";
            grid.innerHTML = "";
            summary.textContent = "Tìm thấy 0 shop";
        });
    };

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", initShopsPage);
    } else {
        initShopsPage();
    }
})();
