        (() => {
            const configNode = document.getElementById("productPageConfig");
            const productId = Number(configNode?.dataset.productId || "0");
            const searchKeyword = configNode?.dataset.searchKeyword || "";
            const isAuthenticated = (configNode?.dataset.isAuthenticated || "false") === "true";
            const isBuyerEligible = (configNode?.dataset.isBuyerEligible || "false") === "true";
            const signInUrl = configNode?.dataset.signInUrl || "/account/signin";
            const endpoint = configNode?.dataset.endpoint || `/bff/products/${productId}`;
            const offersEndpoint = configNode?.dataset.offersEndpoint || `/bff/products/${productId}/offers`;
            const reviewsEndpoint = configNode?.dataset.reviewsEndpoint || `/bff/reviews/products/${productId}`;
            const fallbackImage = configNode?.dataset.fallbackImage || "/uploads/products/no-image.png";
            const uploadRoot = configNode?.dataset.uploadRoot || "/uploads/products/";
            const antiForgeryToken = document.querySelector("#productAntiForgeryForm input[name='__RequestVerificationToken']")?.value ?? "";
            const loadingNode = document.getElementById("productLoading");
            const errorNode = document.getElementById("productError");
            const rootNode = document.getElementById("productDetailRoot");
            const headerSearchInput = document.getElementById("headerSearchInput");
            let chatSummaryTimer = 0;

            const escapeHtml = (value) => (value ?? "")
                .toString()
                .replaceAll("&", "&amp;")
                .replaceAll("<", "&lt;")
                .replaceAll(">", "&gt;")
                .replaceAll('"', "&quot;")
                .replaceAll("'", "&#39;");

            const toNumber = (value, fallback = 0) => {
                const numeric = Number(value);
                return Number.isFinite(numeric) ? numeric : fallback;
            };

            const vnd = (value) => `${toNumber(value, 0).toLocaleString("vi-VN")} đ`;
            const formatMultilineText = (value) => escapeHtml(value).replace(/\r?\n/g, "<br />");

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
                const infos = Array.isArray(raw.productInfos ?? raw.ProductInfos)
                    ? (raw.productInfos ?? raw.ProductInfos)
                    : [];
                const firstInfo = infos[0] ?? {};

                return {
                    productId: toNumber(raw.productId ?? raw.ProductId, 0),
                    productName: (raw.productName ?? raw.ProductName ?? "Nông sản chưa đặt tên").toString(),
                    primarySellerId: toNumber(raw.primarySellerId ?? raw.PrimarySellerId, 0),
                    sku: (raw.sku ?? raw.Sku ?? "").toString(),
                    price: toNumber(raw.price ?? raw.Price, 0),
                    status: Boolean(raw.status ?? raw.Status),
                    stockQuantity: toNumber(raw.availableStock ?? raw.AvailableStock ?? raw.stockQuantity ?? raw.StockQuantity, 0),
                    image: resolveImageUrl(raw),
                    categoryName: (raw.categoryName ?? raw.CategoryName ?? "Chưa phân loại").toString(),
                    unitName: (raw.unitName ?? raw.UnitName ?? "đơn vị").toString(),
                    unitSymbol: (raw.unitSymbol ?? raw.UnitSymbol ?? "").toString(),
                    shortDescription: (raw.shortDescription ?? raw.ShortDescription ?? "").toString().trim(),
                    longDescription: (raw.longDescription ?? raw.LongDescription ?? "").toString().trim(),
                    weight: (firstInfo.weight ?? firstInfo.Weight ?? "").toString().trim(),
                    origin: (firstInfo.origin ?? firstInfo.Origin ?? "").toString().trim(),
                    standard: (firstInfo.standard ?? firstInfo.Standard ?? "").toString().trim(),
                    preservation: (firstInfo.preservation ?? firstInfo.Preservation ?? "").toString().trim()
                };
            };

            const resolveAvailability = (stockQuantity) => {
                const stock = toNumber(stockQuantity, 0);
                if (stock <= 0) return "out-of-stock";
                if (stock <= 10) return "low-stock";
                return "in-stock";
            };

            const getAvailabilityMeta = (stockQuantity) => {
                const stock = toNumber(stockQuantity, 0);
                const key = resolveAvailability(stock);
                if (key === "out-of-stock") {
                    return {
                        key,
                        statusClass: "danger",
                        icon: "bi-x-circle",
                        label: "Hết hàng",
                        detail: "Sản phẩm này đang tạm hết hàng. Bạn vẫn có thể vào shop để xem thêm lựa chọn thay thế."
                    };
                }

                if (key === "low-stock") {
                    return {
                        key,
                        statusClass: "warn",
                        icon: "bi-exclamation-circle",
                        label: "Sắp hết hàng",
                        detail: `Hiện chỉ còn khoảng ${stock.toLocaleString("vi-VN")} ${stock === 1 ? "đơn vị" : "đơn vị"} khả dụng. Nếu phù hợp bạn nên đặt sớm.`
                    };
                }

                return {
                    key,
                    statusClass: "ok",
                    icon: "bi-check-circle",
                    label: "Còn hàng",
                    detail: `Hiện còn khoảng ${stock.toLocaleString("vi-VN")} đơn vị sẵn sàng cho đơn mới.`
                };
            };

            const getFreshnessSignals = (product) => {
                const signals = [];
                const preservation = (product?.preservation ?? "").toString().trim();
                const origin = (product?.origin ?? "").toString().trim();
                const weight = (product?.weight ?? "").toString().trim();
                const normalizedPreservation = preservation
                    .toLowerCase()
                    .normalize("NFD")
                    .replace(/[\u0300-\u036f]/g, "");

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

                return signals.slice(0, 3);
            };

            const renderFreshnessPanel = (product) => {
                const signals = getFreshnessSignals(product);
                if (signals.length === 0) {
                    return "";
                }

                return `
                    <div class="freshness-panel">
                        <div class="freshness-panel-title">
                            <i class="bi bi-truck"></i>
                            <span>Gợi ý giao và bảo quản</span>
                        </div>
                        <div class="freshness-panel-text">
                            FreshFarm đang hiển thị các tín hiệu thực tế từ dữ liệu sản phẩm để bạn dễ trao đổi với shop trước khi chốt đơn.
                        </div>
                        <div class="freshness-panel-badges">
                            ${signals.map((signal) => `
                                <span class="freshness-panel-badge ${signal.key}">
                                    <i class="bi ${signal.icon}"></i>
                                    <span>${escapeHtml(signal.label)}</span>
                                </span>
                            `).join("")}
                        </div>
                    </div>
                `;
            };

            const buildCheckoutUrl = (product, qty = 1, sellerId = null, sellerName = "") => {
                const resolvedSellerId = toNumber(sellerId, toNumber(product.primarySellerId, 0));
                const query = new URLSearchParams({
                    productId: String(product.productId),
                    sellerId: String(resolvedSellerId),
                    sellerName: (sellerName || "").toString(),
                    productName: product.productName,
                    unitPrice: String(product.price),
                    unitSymbol: product.unitName,
                    quantity: String(Math.max(1, qty))
                });
                return "/checkout?" + query.toString();
            };

            const buildShopUrl = (sellerId) => {
                const normalizedSellerId = toNumber(sellerId, 0);
                return normalizedSellerId > 0 ? `/shop/${normalizedSellerId}` : "";
            };

            const buildShopChatUrl = (sellerId) => {
                const shopUrl = buildShopUrl(sellerId);
                return shopUrl ? `${shopUrl}#shop-chat` : "";
            };

            const loadChatSummary = async (sellerId) => {
                if (!isAuthenticated || !isBuyerEligible || toNumber(sellerId, 0) <= 0) {
                    return null;
                }

                try {
                    const response = await fetch(`/bff/support-chat/sellers/${sellerId}/conversation`, {
                        headers: { "Accept": "application/json" }
                    });

                    const text = await response.text();
                    const payload = text ? JSON.parse(text) : null;
                    if (!response.ok) {
                        return null;
                    }

                    return payload?.conversation ?? null;
                } catch {
                    return null;
                }
            };

            const applyPrimaryChatState = (conversation) => {
                const primaryChatCta = document.getElementById("primaryChatCta");
                if (!(primaryChatCta instanceof HTMLAnchorElement) || !conversation) {
                    return;
                }

                primaryChatCta.classList.remove("has-unread");

                if (conversation.hasUnread) {
                    primaryChatCta.textContent = "Xem tin nhắn mới";
                    primaryChatCta.classList.add("has-unread");
                    return;
                }

                if (conversation.lastTime) {
                    primaryChatCta.textContent = "Mở cuộc trò chuyện";
                    return;
                }

                primaryChatCta.textContent = "Chat với shop";
            };

            const startPrimaryChatPolling = (sellerId) => {
                if (!isBuyerEligible || toNumber(sellerId, 0) <= 0 || chatSummaryTimer) {
                    return;
                }

                chatSummaryTimer = window.setInterval(() => {
                    if (document.hidden) {
                        return;
                    }

                    void loadChatSummary(sellerId).then((conversation) => {
                        applyPrimaryChatState(conversation);
                    });
                }, 12000);
            };

            const formatDate = (value) => {
                if (!value) {
                    return "Chưa rõ thời gian";
                }

                const date = new Date(value);
                return Number.isNaN(date.getTime())
                    ? "Chưa rõ thời gian"
                    : date.toLocaleDateString("vi-VN", { day: "2-digit", month: "2-digit", year: "numeric" });
            };

            const renderStarIcons = (rating) => {
                const resolved = Math.max(0, Math.min(5, toNumber(rating, 0)));
                return Array.from({ length: 5 }, (_, index) => index < resolved ? "★" : "☆").join("");
            };

            const setReviewNotice = (message, isError = false) => {
                const node = document.getElementById("productReviewSubmitNote");
                if (!(node instanceof HTMLElement)) {
                    return;
                }

                node.textContent = message || "";
                node.className = `review-submit-note${isError ? " is-error" : ""}`;
            };

            const renderReviewRows = (reviews) => {
                if (!Array.isArray(reviews) || reviews.length === 0) {
                    return `<div class="review-empty">Sản phẩm này chưa có đánh giá nào. Người chưa mua vẫn có thể vào đây để xem khi có khách hàng chia sẻ trải nghiệm.</div>`;
                }

                const renderReplyRows = (replies) => {
                    if (!Array.isArray(replies) || replies.length === 0) {
                        return "";
                    }

                    return `
                        <div class="review-replies">
                            ${replies.map((reply) => `
                                <div class="review-reply">
                                    <div class="review-reply-head">
                                        <div class="review-reply-author">${escapeHtml(reply.author?.displayName ?? reply.Author?.DisplayName ?? "Shop phản hồi")}</div>
                                        <div class="review-date">
                                            ${escapeHtml(formatDate(reply.updatedAt ?? reply.UpdatedAt ?? reply.createdAt ?? reply.CreatedAt))}
                                            ${(reply.isEdited ?? reply.IsEdited) ? " · Đã chỉnh sửa" : ""}
                                        </div>
                                    </div>
                                    <div class="review-reply-comment">${formatMultilineText(reply.comment ?? reply.Comment ?? "")}</div>
                                </div>
                            `).join("")}
                        </div>
                    `;
                };

                return `
                    <div class="review-list">
                        ${reviews.map((review) => `
                            <article class="review-card">
                                <div class="review-card-head">
                                    <div>
                                        <div class="review-author">${escapeHtml(review.author?.displayName ?? "Khách hàng")}</div>
                                        <div class="review-stars">${renderStarIcons(review.rating)}</div>
                                        <div class="review-date">
                                            ${escapeHtml(formatDate(review.updatedAt ?? review.createdAt))}
                                            ${review.isEdited ? " · Đã chỉnh sửa" : ""}
                                        </div>
                                    </div>
                                    ${review.isOwner ? `<span class="review-owner-badge"><i class="bi bi-person-check"></i> Đánh giá của bạn</span>` : ""}
                                </div>
                                <div class="review-comment">${formatMultilineText(review.comment ?? "")}</div>
                                ${renderReplyRows(review.replies ?? review.Replies)}
                            </article>
                        `).join("")}
                    </div>
                `;
            };

            const renderReviewSection = (payload) => {
                const panel = document.getElementById("productReviewPanel");
                if (!(panel instanceof HTMLElement)) {
                    return;
                }

                const summary = payload?.summary ?? {};
                const viewer = payload?.viewer ?? {};
                const reviews = Array.isArray(payload?.reviews) ? payload.reviews : [];
                const stars = Array.isArray(summary.stars) ? summary.stars : [];
                const totalReviews = toNumber(summary.totalReviews, 0);
                const averageRating = toNumber(summary.averageRating, 0);
                const existingReview = viewer.existingReview ?? null;
                const canSubmit = !!viewer.canSubmitReview;

                panel.innerHTML = `
                    <div class="review-shell">
                        <div class="review-summary-grid">
                            <div class="review-score-card">
                                <div class="review-score-value">${averageRating.toFixed(1)}</div>
                                <div class="review-score-meta">trên 5 · ${totalReviews.toLocaleString("vi-VN")} đánh giá</div>
                                <div class="review-stars mt-2">${renderStarIcons(Math.round(averageRating))}</div>
                            </div>
                            <div class="review-score-card">
                                <div class="review-bars">
                                    ${[5, 4, 3, 2, 1].map((star) => {
                                        const row = stars.find((item) => toNumber(item.rating, 0) === star) ?? {};
                                        const count = toNumber(row.count, 0);
                                        const width = totalReviews > 0 ? (count / totalReviews) * 100 : 0;
                                        return `
                                            <div class="review-bar-row">
                                                <span>${star} sao</span>
                                                <div class="review-bar-track"><div class="review-bar-fill" style="width:${width}%"></div></div>
                                                <strong>${count.toLocaleString("vi-VN")}</strong>
                                            </div>
                                        `;
                                    }).join("")}
                                </div>
                            </div>
                        </div>
                        <div class="review-form-card">
                            <div class="review-viewer-note">${escapeHtml(viewer.message ?? "")}</div>
                            ${canSubmit ? `
                                <div class="row g-3">
                                    <div class="col-md-3">
                                        <label for="productReviewRating">Số sao</label>
                                        <select id="productReviewRating" class="form-select">
                                            <option value="5">5 sao</option>
                                            <option value="4">4 sao</option>
                                            <option value="3">3 sao</option>
                                            <option value="2">2 sao</option>
                                            <option value="1">1 sao</option>
                                        </select>
                                    </div>
                                    <div class="col-md-9">
                                        <label for="productReviewComment">Nội dung đánh giá</label>
                                        <textarea id="productReviewComment" class="form-control" rows="4" maxlength="1000" placeholder="Chia sẻ cảm nhận thật của bạn về sản phẩm, đóng gói và trải nghiệm nhận hàng."></textarea>
                                    </div>
                                </div>
                                <div class="review-form-actions">
                                    <button type="button" id="productReviewSubmitBtn" class="btn-detail-primary">${existingReview ? "Cập nhật đánh giá" : "Gửi đánh giá"}</button>
                                    ${existingReview ? `<span class="text-muted small">Bạn đã có đánh giá trước đó và có thể chỉnh sửa lại nội dung.</span>` : ""}
                                </div>
                                <div id="productReviewSubmitNote" class="review-submit-note"></div>
                            ` : (!viewer.isAuthenticated
                                ? `<div class="review-form-actions"><a class="btn-detail-primary" href="${signInUrl}">Đăng nhập để đánh giá</a></div>`
                                : "")}
                        </div>
                        ${renderReviewRows(reviews)}
                    </div>
                `;

                if (!canSubmit) {
                    return;
                }

                const ratingInput = document.getElementById("productReviewRating");
                const commentInput = document.getElementById("productReviewComment");
                const submitButton = document.getElementById("productReviewSubmitBtn");
                if (ratingInput instanceof HTMLSelectElement && existingReview) {
                    ratingInput.value = String(toNumber(existingReview.rating, 5));
                }

                if (commentInput instanceof HTMLTextAreaElement && existingReview) {
                    commentInput.value = (existingReview.comment ?? "").toString();
                }

                if (submitButton instanceof HTMLButtonElement) {
                    submitButton.addEventListener("click", async () => {
                        if (!(ratingInput instanceof HTMLSelectElement) || !(commentInput instanceof HTMLTextAreaElement)) {
                            return;
                        }

                        const comment = commentInput.value.trim();
                        const rating = Math.max(1, Math.min(5, toNumber(ratingInput.value, 5)));
                        if (comment.length < 5) {
                            setReviewNotice("Nội dung đánh giá cần ít nhất 5 ký tự.", true);
                            commentInput.focus();
                            return;
                        }

                        const defaultLabel = existingReview ? "Cập nhật đánh giá" : "Gửi đánh giá";
                        submitButton.disabled = true;
                        submitButton.textContent = "Đang gửi...";
                        setReviewNotice("");

                        try {
                            const body = new URLSearchParams();
                            body.set("__RequestVerificationToken", antiForgeryToken);
                            body.set("Rating", String(rating));
                            body.set("Comment", comment);

                            const response = await fetch(reviewsEndpoint, {
                                method: "POST",
                                credentials: "same-origin",
                                headers: {
                                    "Accept": "application/json",
                                    "Content-Type": "application/x-www-form-urlencoded; charset=UTF-8",
                                    "X-Requested-With": "XMLHttpRequest"
                                },
                                body: body.toString()
                            });

                            const text = await response.text();
                            const result = text ? JSON.parse(text) : null;
                            if (!response.ok) {
                                throw new Error(result?.message || `HTTP ${response.status}`);
                            }

                            setReviewNotice(result?.message || "Đã lưu đánh giá của bạn.");
                            await loadReviews();
                        } catch (error) {
                            setReviewNotice(error?.message || "Chưa thể gửi đánh giá lúc này.", true);
                        } finally {
                            submitButton.disabled = false;
                            submitButton.textContent = defaultLabel;
                        }
                    });
                }
            };

            const loadReviews = async () => {
                const panel = document.getElementById("productReviewPanel");
                if (panel instanceof HTMLElement) {
                    panel.innerHTML = `<div class="offer-state">Đang tải đánh giá sản phẩm...</div>`;
                }

                try {
                    const response = await fetch(reviewsEndpoint, { headers: { "Accept": "application/json" } });
                    const text = await response.text();
                    const payload = text ? JSON.parse(text) : null;

                    if (!response.ok) {
                        throw new Error(payload?.message || `HTTP ${response.status}`);
                    }

                    renderReviewSection(payload);
                } catch (error) {
                    if (panel instanceof HTMLElement) {
                        panel.innerHTML = `<div class="review-empty">${escapeHtml(error?.message || "Chưa thể tải đánh giá sản phẩm.")}</div>`;
                    }
                }
            };

            const parseDate = (value) => {
                if (!value) {
                    return null;
                }

                const parsed = new Date(value);
                return Number.isNaN(parsed.getTime()) ? null : parsed;
            };

            const getOfferTrustSignals = (offer) => {
                const signals = [];
                const joinedAt = parseDate(offer.joinedAt ?? offer.JoinedAt);
                const now = new Date();
                const daysActive = joinedAt ? Math.floor((now.getTime() - joinedAt.getTime()) / 86400000) : null;
                const addressText = (offer.addressSummary ?? offer.AddressSummary ?? "").toString().trim();
                const hasAddress = !!addressText && !/^chưa cập nhật/i.test(addressText);

                if (typeof daysActive === "number" && daysActive >= 180) {
                    signals.push({ key: "stable", icon: "bi-patch-check", label: "Hoạt động lâu" });
                } else if (typeof daysActive === "number" && daysActive <= 90) {
                    signals.push({ key: "new", icon: "bi-stars", label: "Mới tham gia" });
                }

                if (hasAddress) {
                    signals.push({ key: "address", icon: "bi-geo-alt", label: "Có địa chỉ shop" });
                }

                return signals.slice(0, 2);
            };

            const renderOfferTrustBadges = (offer) => {
                const signals = getOfferTrustSignals(offer);
                if (signals.length === 0) {
                    return "";
                }

                return `
                    <div class="offer-trust-badges">
                        ${signals.map((signal) => `
                            <span class="offer-trust-badge ${signal.key}">
                                <i class="bi ${signal.icon}"></i>
                                <span>${escapeHtml(signal.label)}</span>
                            </span>
                        `).join("")}
                    </div>
                `;
            };

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

            const showDetailCartNotice = (message, tone = "success") => {
                const noticeNode = document.getElementById("detailCartNotice");
                if (!(noticeNode instanceof HTMLElement)) {
                    return;
                }

                noticeNode.textContent = message;
                noticeNode.className = `detail-cart-note is-visible ${tone === "error" ? "is-error" : "is-success"}`;
            };

            const clearDetailCartNotice = () => {
                const noticeNode = document.getElementById("detailCartNotice");
                if (!(noticeNode instanceof HTMLElement)) {
                    return;
                }

                noticeNode.textContent = "";
                noticeNode.className = "detail-cart-note";
            };

            const submitAddToCart = async (product, qty = 1, triggerButton = null) => {
                if (!antiForgeryToken || product.productId <= 0) {
                    return null;
                }

                const targetButton = triggerButton instanceof HTMLButtonElement ? triggerButton : null;
                const originalLabel = targetButton?.innerHTML ?? "";
                if (targetButton) {
                    targetButton.disabled = true;
                    targetButton.innerHTML = '<span class="spinner-border spinner-border-sm me-2" role="status" aria-hidden="true"></span>Đang thêm';
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

                    return payload;
                } catch (error) {
                    showDetailCartNotice(error?.message || "Không thể thêm sản phẩm vào giỏ hàng lúc này.", "error");
                    return null;
                } finally {
                    if (targetButton) {
                        targetButton.disabled = false;
                        targetButton.innerHTML = originalLabel;
                    }
                }
            };

            const redirectToSearch = (keyword) => {
                const normalizedKeyword = (keyword ?? "").toString().trim();
                const targetUrl = normalizedKeyword
                    ? `/search?q=${encodeURIComponent(normalizedKeyword)}`
                    : "/search";
                window.location.href = targetUrl;
            };

            const showError = (message) => {
                loadingNode.classList.add("d-none");
                rootNode.classList.add("d-none");
                errorNode.classList.remove("d-none");
                errorNode.innerHTML = `
                    <div class="fw-bold mb-2">Không tải được sản phẩm</div>
                    <div>${escapeHtml(message)}</div>
                `;
            };

            const renderOffers = (product, offers) => {
                const offersNode = document.getElementById("sellerOfferList");
                if (!(offersNode instanceof HTMLElement)) {
                    return;
                }

                const availability = getAvailabilityMeta(product.stockQuantity);
                const canPurchase = availability.key !== "out-of-stock";
                const guideHtml = `
                    <div class="offer-guide ${availability.key}">
                        <div class="offer-guide-title">
                            <i class="bi ${availability.icon}"></i>
                            <span>${escapeHtml(canPurchase ? "Chọn shop phù hợp để đặt mua" : "Sản phẩm chính đang tạm hết hàng" )}</span>
                        </div>
                        <div class="offer-guide-text">
                            ${escapeHtml(canPurchase
                                ? "FreshFarm đang hiển thị các shop công khai có bán cùng sản phẩm này. Bạn có thể ưu tiên shop chính, hoặc nhắn shop để hỏi thêm về thời gian giao và chất lượng lô hàng."
                                : "Bạn vẫn có thể vào shop hoặc nhắn shop để hỏi thời điểm nhập lại hàng, lô thay thế, hoặc các sản phẩm tương tự đang còn bán.")}
                        </div>
                        <ul class="offer-guide-list">
                            <li>${escapeHtml(canPurchase ? "Shop được gắn nhãn 'Shop chính của sản phẩm' là shop đang gắn trực tiếp với PDP hiện tại." : "Ưu tiên nhắn shop trước khi quyết định mua để xác nhận khả năng cung ứng.")}</li>
                            <li>Vào shop để xem thêm mặt hàng cùng seller nếu bạn muốn so sánh nhanh.</li>
                        </ul>
                    </div>
                `;

                if (!Array.isArray(offers) || offers.length === 0) {
                    offersNode.innerHTML = `${guideHtml}<div class='offer-state'>Chưa có thêm shop công khai nào khác cho sản phẩm này.</div>`;
                    return;
                }

                offersNode.innerHTML = guideHtml + offers
                    .map((offer) => {
                        const avatarUrl = resolveAvatar(offer.avatar ?? offer.Avatar);
                        const shopName = (offer.shopName ?? offer.ShopName ?? `Seller #${offer.sellerId ?? offer.SellerId}`).toString();
                        const address = (offer.addressSummary ?? offer.AddressSummary ?? "Chưa cập nhật địa chỉ hoạt động").toString();
                        const activeSince = formatDate(offer.activeSinceUtc ?? offer.ActiveSinceUtc);
                        const joinedAt = formatDate(offer.joinedAt ?? offer.JoinedAt);
                        const sellerIdValue = toNumber(offer.sellerId ?? offer.SellerId, 0);
                        const isPrimarySeller = sellerIdValue > 0 && sellerIdValue === toNumber(product.primarySellerId, 0);

                        return `
                            <article class="offer-card${isPrimarySeller ? " is-primary" : ""}">
                                ${avatarUrl
                                    ? `<img src="${avatarUrl}" alt="${escapeHtml(shopName)}" class="offer-avatar" />`
                                    : `<div class="offer-avatar-fallback">${escapeHtml(shopName.slice(0, 1).toUpperCase())}</div>`}
                                <div>
                                    <div class="offer-shop-line">
                                        <div class="offer-shop-name">${escapeHtml(shopName)}</div>
                                        ${isPrimarySeller ? `<span class="offer-shop-badge"><i class="bi bi-stars"></i> Shop chính của sản phẩm</span>` : ""}
                                    </div>
                                    <div class="offer-meta">
                                        <div><i class="bi bi-geo-alt"></i> ${escapeHtml(address)}</div>
                                        <div><i class="bi bi-shop"></i> Seller ID #${sellerIdValue}</div>
                                        <div><i class="bi bi-calendar-check"></i> Bán từ ${escapeHtml(activeSince)} · Tham gia ${escapeHtml(joinedAt)}</div>
                                    </div>
                                    ${renderOfferTrustBadges(offer)}
                                </div>
                                <div>
                                    <div class="offer-price">${vnd(product.price)}</div>
                                    ${canPurchase
                                        ? `<a href="${buildCheckoutUrl(product, 1, sellerIdValue, shopName)}" class="offer-action mb-2">${isPrimarySeller ? "Mua từ shop chính" : "Chọn mua"}</a>`
                                        : `<a href="${buildShopChatUrl(sellerIdValue)}" class="offer-action mb-2">${isPrimarySeller ? "Hỏi shop chính" : "Hỏi shop này"}</a>`}
                                    <a href="${buildShopChatUrl(sellerIdValue)}" class="offer-action mb-2">${isPrimarySeller ? "Nhắn shop chính" : "Nhắn shop"}</a>
                                    <a href="${buildShopUrl(sellerIdValue)}" class="offer-action">Vào shop</a>
                                </div>
                            </article>
                        `;
                    })
                    .join("");
            };

            const loadOffers = async (product) => {
                try {
                    const response = await fetch(offersEndpoint, { headers: { "Accept": "application/json" } });
                    const text = await response.text();
                    const payload = text ? JSON.parse(text) : [];

                    if (!response.ok) {
                        throw new Error(payload?.message || `HTTP ${response.status}`);
                    }

                    renderOffers(product, payload);
                } catch {
                    renderOffers(product, []);
                }
            };

            const renderProduct = (product) => {
                const summary = product.shortDescription || "Sản phẩm đang được hiển thị trên marketplace FreshFarm. Thông tin chi tiết đang được chuẩn hóa dần theo dữ liệu thật.";
                const longDescription = product.longDescription || product.shortDescription || "Chưa có mô tả chi tiết cho sản phẩm này.";
                const availability = getAvailabilityMeta(product.stockQuantity);
                const statusClass = availability.statusClass;
                const statusText = availability.label;
                const canPurchase = availability.key !== "out-of-stock";
                const backUrl = searchKeyword ? `/search?q=${encodeURIComponent(searchKeyword)}` : "/search";
                const primaryShopUrl = buildShopUrl(product.primarySellerId);
                const primaryChatUrl = buildShopChatUrl(product.primarySellerId);
                const primarySellerName = (product.sellerName ?? "").toString().trim() || (product.primarySellerId > 0 ? `Shop #${product.primarySellerId}` : "FreshFarm");
                const primaryChatLabel = primaryShopUrl
                    ? (isBuyerEligible ? "Chat với shop" : (isAuthenticated ? "Xem shop đang bán" : "Đăng nhập để chat"))
                    : "Quay lại tìm kiếm";

                rootNode.innerHTML = `
                    <section class="detail-shell">
                        <div class="detail-grid">
                            <div class="detail-media">
                                <div class="detail-image-frame">
                                    <img src="${product.image}" alt="${escapeHtml(product.productName)}" class="detail-image" />
                                </div>
                            </div>
                            <div class="detail-panel">
                                <div class="eyebrow"><i class="bi bi-flower1"></i> Nông sản công khai trên FreshFarm</div>
                                <h1 class="product-title">${escapeHtml(product.productName)}</h1>
                                <div class="meta-row">
                                    <span class="meta-chip"><i class="bi bi-grid"></i> ${escapeHtml(product.categoryName)}</span>
                                    <span class="meta-chip"><i class="bi bi-box-seam"></i> ${escapeHtml(product.unitName)}</span>
                                    <span class="meta-chip"><i class="bi bi-upc-scan"></i> ${escapeHtml(product.sku || "Chưa có SKU")}</span>
                                </div>
                                <div class="price-row">
                                    <div class="price-main">${vnd(product.price)}</div>
                                    <div class="unit-text">/ ${escapeHtml(product.unitName)}</div>
                                </div>
                                <div class="status-row">
                                    <span class="status-pill ${statusClass}"><i class="bi ${availability.icon}"></i> ${escapeHtml(statusText)}</span>
                                    <span class="status-pill ${product.status ? "ok" : "warn"}"><i class="bi bi-patch-check"></i> ${product.status ? "Đang hiển thị" : "Đang ẩn"}</span>
                                </div>
                                <div class="stock-panel ${availability.key}">
                                    <div class="stock-panel-title">
                                        <i class="bi ${availability.icon}"></i>
                                        <span>${escapeHtml(availability.label)}</span>
                                    </div>
                                    <div class="stock-panel-detail">${escapeHtml(availability.detail)}</div>
                                </div>
                                ${renderFreshnessPanel(product)}
                                <div class="summary-text">${escapeHtml(summary)}</div>
                                <div class="purchase-box">
                                    <div class="purchase-box-head">
                                        <div>
                                            <div class="purchase-box-title">Chọn số lượng trước khi mua</div>
                                            <div class="purchase-box-text">Trang chi tiết giờ sẽ để bạn chọn số lượng ngay tại chỗ. Thêm vào giỏ sẽ giữ nguyên trang này để bạn mua tiếp.</div>
                                        </div>
                                        <div class="purchase-box-store">
                                            <i class="bi bi-shop"></i>
                                            <span>${escapeHtml(primarySellerName)}</span>
                                        </div>
                                    </div>
                                    <div class="purchase-box-controls">
                                        <div class="qty-selector" aria-label="Bộ chọn số lượng">
                                            <button type="button" class="qty-btn" id="purchaseQtyDecrease" aria-label="Giảm số lượng">-</button>
                                            <input id="purchaseQtyInput" class="qty-input" type="number" min="1" step="1" value="1" inputmode="numeric" />
                                            <button type="button" class="qty-btn" id="purchaseQtyIncrease" aria-label="Tăng số lượng">+</button>
                                        </div>
                                        <div class="purchase-subtotal">
                                            <span class="purchase-subtotal-label">Tạm tính</span>
                                            <span class="purchase-subtotal-value" id="purchaseSubtotal">${vnd(product.price)}</span>
                                        </div>
                                    </div>
                                    <div id="detailCartNotice" class="detail-cart-note"></div>
                                </div>
                                <div class="cta-row">
                                    ${canPurchase
                                        ? `<a href="${buildCheckoutUrl(product)}" id="buyNowBtn" class="btn-detail-primary">Mua ngay</a>`
                                        : `<button type="button" class="btn-detail-primary is-disabled" disabled>Tạm hết hàng</button>`}
                                    <button type="button" id="addToCartBtn" class="btn-detail-secondary${canPurchase ? "" : " is-disabled"}" ${canPurchase ? "" : "disabled"}>Thêm vào giỏ</button>
                                    ${primaryChatUrl
                                        ? `<a href="${isBuyerEligible ? primaryChatUrl : (isAuthenticated ? primaryShopUrl : signInUrl)}" id="primaryChatCta" class="btn-detail-ghost">${primaryChatLabel}</a>`
                                        : `<a href="${backUrl}" class="btn-detail-ghost">Quay lại tìm kiếm</a>`}
                                </div>
                                <div class="section-card">
                                    <h2>Thông tin nông sản</h2>
                                    <div class="detail-facts">
                                        <div class="fact-tile">
                                            <div class="fact-label">Xuất xứ</div>
                                            <div class="fact-value">${escapeHtml(product.origin || "Chưa cập nhật")}</div>
                                        </div>
                                        <div class="fact-tile">
                                            <div class="fact-label">Chuẩn nông sản</div>
                                            <div class="fact-value">${escapeHtml(product.standard || "Chưa cập nhật")}</div>
                                        </div>
                                        <div class="fact-tile">
                                            <div class="fact-label">Khối lượng</div>
                                            <div class="fact-value">${escapeHtml(product.weight || "Chưa cập nhật")}</div>
                                        </div>
                                        <div class="fact-tile">
                                            <div class="fact-label">Bảo quản</div>
                                            <div class="fact-value">${escapeHtml(product.preservation || "Chưa cập nhật")}</div>
                                        </div>
                                    </div>
                                </div>
                            </div>
                        </div>
                    </section>
                    <section class="section-card">
                        <h2>Mô tả chi tiết</h2>
                        <div class="product-description">${escapeHtml(longDescription)}</div>
                    </section>
                    <section class="section-card">
                        <h2>Nhiều shop cùng bán</h2>
                        <div class="offer-state mb-3">FreshFarm đang gom các seller đang hoạt động cho cùng sản phẩm này.</div>
                        <div id="sellerOfferList" class="offer-list">
                            <div class="offer-state">Đang tải danh sách shop...</div>
                        </div>
                    </section>
                    <section class="section-card" id="product-reviews">
                        <h2>Đánh giá sản phẩm</h2>
                        <div id="productReviewPanel" class="offer-state">Đang tải đánh giá sản phẩm...</div>
                    </section>
                `;

                rootNode.classList.remove("d-none");
                loadingNode.classList.add("d-none");
                errorNode.classList.add("d-none");

                const qtyInput = document.getElementById("purchaseQtyInput");
                const qtyDecreaseBtn = document.getElementById("purchaseQtyDecrease");
                const qtyIncreaseBtn = document.getElementById("purchaseQtyIncrease");
                const subtotalNode = document.getElementById("purchaseSubtotal");
                const buyNowBtn = document.getElementById("buyNowBtn");
                const addToCartBtn = document.getElementById("addToCartBtn");

                const syncPurchaseState = () => {
                    const quantity = Math.max(1, toNumber(qtyInput instanceof HTMLInputElement ? qtyInput.value : 1, 1));
                    if (qtyInput instanceof HTMLInputElement) {
                        qtyInput.value = String(quantity);
                    }

                    if (qtyDecreaseBtn instanceof HTMLButtonElement) {
                        qtyDecreaseBtn.disabled = quantity <= 1;
                    }

                    if (subtotalNode instanceof HTMLElement) {
                        subtotalNode.textContent = vnd(Math.max(0, product.price) * quantity);
                    }

                    if (buyNowBtn instanceof HTMLAnchorElement) {
                        buyNowBtn.href = buildCheckoutUrl(product, quantity);
                    }

                    return quantity;
                };

                if (qtyInput instanceof HTMLInputElement) {
                    qtyInput.addEventListener("input", () => {
                        clearDetailCartNotice();
                        syncPurchaseState();
                    });
                    qtyInput.addEventListener("blur", syncPurchaseState);
                }

                if (qtyDecreaseBtn instanceof HTMLButtonElement) {
                    qtyDecreaseBtn.addEventListener("click", () => {
                        if (qtyInput instanceof HTMLInputElement) {
                            qtyInput.value = String(Math.max(1, toNumber(qtyInput.value, 1) - 1));
                        }

                        clearDetailCartNotice();
                        syncPurchaseState();
                    });
                }

                if (qtyIncreaseBtn instanceof HTMLButtonElement) {
                    qtyIncreaseBtn.addEventListener("click", () => {
                        if (qtyInput instanceof HTMLInputElement) {
                            qtyInput.value = String(Math.max(1, toNumber(qtyInput.value, 1) + 1));
                        }

                        clearDetailCartNotice();
                        syncPurchaseState();
                    });
                }

                syncPurchaseState();

                if (addToCartBtn instanceof HTMLButtonElement) {
                    addToCartBtn.addEventListener("click", async () => {
                        clearDetailCartNotice();
                        const quantity = syncPurchaseState();
                        const result = await submitAddToCart(product, quantity, addToCartBtn);
                        if (result?.success) {
                            showDetailCartNotice(`Đã thêm "${product.productName}" vào giỏ với số lượng ${quantity.toLocaleString("vi-VN")} ${product.unitName}.`, "success");
                        }
                    });
                }

                void loadOffers(product);
                void loadReviews();

                if (isBuyerEligible && product.primarySellerId > 0) {
                    void loadChatSummary(product.primarySellerId).then((conversation) => {
                        applyPrimaryChatState(conversation);
                    });
                    startPrimaryChatPolling(product.primarySellerId);
                }
            };

            if (!Number.isInteger(productId) || productId <= 0) {
                showError("ID sản phẩm không hợp lệ.");
                return;
            }

            if (headerSearchInput instanceof HTMLInputElement && searchKeyword && !headerSearchInput.value.trim()) {
                headerSearchInput.value = searchKeyword;
            }

            document.querySelectorAll("[data-header-keyword]").forEach((link) => {
                link.addEventListener("click", (event) => {
                    event.preventDefault();
                    redirectToSearch(link.getAttribute("data-header-keyword") || "");
                });
            });

            fetch(endpoint, { headers: { "Accept": "application/json" } })
                .then(async (response) => {
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

                    return payload;
                })
                .then((payload) => renderProduct(normalizeProduct(payload)))
                .catch((error) => showError(error?.message || "Không thể tải dữ liệu sản phẩm."));
        })();
