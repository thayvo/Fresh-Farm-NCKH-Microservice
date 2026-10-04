(function () {
    const host = document.getElementById("floatingBuyerChat");
    if (!(host instanceof HTMLElement)) {
        return;
    }

    const getData = (name, fallback = "") => host.dataset[name] || fallback;
    const config = {
        isAuthenticated: getData("isAuthenticated") === "true",
        isBuyerEligible: getData("isBuyerEligible") === "true",
        signInUrl: getData("signInUrl", "/account/signin"),
        summariesUrl: getData("summariesUrl", "/bff/support-chat/summaries"),
        conversationBySellerUrlTemplate: getData("conversationBySellerUrlTemplate", "/bff/support-chat/sellers/__SELLER_ID__/conversation"),
        messagesUrlTemplate: getData("messagesUrlTemplate", "/bff/support-chat/conversations/__CONVERSATION_ID__/messages"),
        sendMessageUrlTemplate: getData("sendMessageUrlTemplate", "/bff/support-chat/conversations/__CONVERSATION_ID__/messages"),
        markReadUrlTemplate: getData("markReadUrlTemplate", "/bff/support-chat/conversations/__CONVERSATION_ID__/mark-read"),
        hubUrl: getData("hubUrl", "/hubs/support-chat"),
        discoverShopsUrl: getData("discoverShopsUrl", "/shops")
    };

    const antiForgeryToken = document.querySelector("#floatingBuyerChatAntiForgeryForm input[name='__RequestVerificationToken']")?.value ?? "";
    const launcher = host.querySelector("[data-floating-chat-launcher]");
    const launcherBadge = host.querySelector("[data-floating-chat-launcher-badge]");
    const launcherSubtitle = host.querySelector("[data-floating-chat-launcher-subtitle]");
    const panel = host.querySelector("#floatingChatPanel");
    const panelSubtitle = host.querySelector("[data-floating-chat-panel-subtitle]");
    const searchInput = host.querySelector("[data-floating-chat-search]");
    const listNode = host.querySelector("[data-floating-chat-conversation-list]");
    const emptyStateNode = host.querySelector("[data-floating-chat-empty-state]");
    const guestStateNode = host.querySelector("[data-floating-chat-guest-state]");
    const threadNode = host.querySelector("[data-floating-chat-thread]");
    const threadAvatarNode = host.querySelector("[data-floating-chat-thread-avatar]");
    const threadNameNode = host.querySelector("[data-floating-chat-thread-name]");
    const threadMetaNode = host.querySelector("[data-floating-chat-thread-meta]");
    const threadStatusNode = host.querySelector("[data-floating-chat-thread-status]");
    const messagesNode = host.querySelector("[data-floating-chat-thread-messages]");
    const typingNode = host.querySelector("[data-floating-chat-typing]");
    const inputNode = host.querySelector("[data-floating-chat-input]");
    const composeHintNode = host.querySelector("[data-floating-chat-compose-hint]");
    const sendButton = host.querySelector("[data-floating-chat-send]");
    const refreshButton = host.querySelector("[data-floating-chat-refresh]");
    const closeButton = host.querySelector("[data-floating-chat-close]");
    const filterButtons = Array.from(host.querySelectorAll("[data-floating-chat-filter]"));
    const externalToggles = Array.from(document.querySelectorAll("[data-floating-buyer-chat-toggle]"));

    const state = {
        isOpen: false,
        summaries: [],
        selectedConversationId: 0,
        filterMode: "all",
        searchTerm: "",
        messages: [],
        isLoadingSummaries: false,
        isLoadingMessages: false,
        isSending: false,
        pollTimer: 0,
        typingTimer: 0,
        hub: null,
        hubReady: false,
        joinedConversationId: 0,
        signalRRequested: false
    };

    const appHelpers = window.FreshFarmApp || null;

    const escapeHtml = (value) => (value ?? "")
        .toString()
        .replaceAll("&", "&amp;")
        .replaceAll("<", "&lt;")
        .replaceAll(">", "&gt;")
        .replaceAll('"', "&quot;")
        .replaceAll("'", "&#39;");

    const toNumber = (value, fallback = 0) => {
        const parsed = Number(value);
        return Number.isFinite(parsed) ? parsed : fallback;
    };

    const parseServerDate = (value) => {
        if (!value) {
            return null;
        }

        if (value instanceof Date) {
            return Number.isNaN(value.getTime()) ? null : value;
        }

        if (typeof value === "string") {
            const normalized = value.trim();
            if (!normalized) {
                return null;
            }

            const hasTimezone = /(?:Z|[+\-]\d{2}:\d{2})$/i.test(normalized);
            const parsed = new Date(hasTimezone ? normalized : `${normalized}Z`);
            return Number.isNaN(parsed.getTime()) ? null : parsed;
        }

        const parsed = new Date(value);
        return Number.isNaN(parsed.getTime()) ? null : parsed;
    };

    const toDate = (value) => {
        return parseServerDate(value);
    };

    const formatRelativeTime = (value) => {
        const date = toDate(value);
        if (!date) {
            return "";
        }

        const diffSeconds = Math.max(0, Math.floor((Date.now() - date.getTime()) / 1000));
        if (diffSeconds < 60) {
            return "Vừa xong";
        }

        if (diffSeconds < 3600) {
            return `${Math.floor(diffSeconds / 60)} phút trước`;
        }

        if (diffSeconds < 86400) {
            return `${Math.floor(diffSeconds / 3600)} giờ trước`;
        }

        return `${Math.floor(diffSeconds / 86400)} ngày trước`;
    };

    const formatDateTime = (value) => {
        const date = toDate(value);
        if (!date) {
            return "Chưa có hoạt động";
        }

        return date.toLocaleString("vi-VN", {
            day: "2-digit",
            month: "2-digit",
            year: "numeric",
            hour: "2-digit",
            minute: "2-digit"
        });
    };

    const showToast = (message, tone = "error") => {
        if (appHelpers?.showToast) {
            appHelpers.showToast(message, tone);
            return;
        }

        window.alert(message);
    };

    const readJson = async (response) => {
        const text = await response.text();
        if (!text) {
            return null;
        }

        try {
            return JSON.parse(text);
        } catch {
            return null;
        }
    };

    const createHttpError = (response, payload, fallbackMessage) => {
        if (appHelpers?.createHttpError) {
            return appHelpers.createHttpError(response, payload, fallbackMessage);
        }

        return new Error(payload?.message || fallbackMessage || `HTTP ${response.status}`);
    };

    const buildUrl = (template, id, extraQuery = "") => {
        const base = template
            .replace("__CONVERSATION_ID__", String(id))
            .replace("__SELLER_ID__", String(id));
        return extraQuery ? `${base}${extraQuery}` : base;
    };

    const resolveAvatar = (summary) => (summary?.avatar ?? "").toString().trim();
    const getConversationSummary = (conversationId) => state.summaries.find((item) => item.conversationId === conversationId) || null;

    const normalizeSummary = (raw) => ({
        sellerId: toNumber(raw?.sellerId ?? raw?.SellerId, 0),
        conversationId: toNumber(raw?.conversationId ?? raw?.ConversationId, 0),
        status: (raw?.status ?? raw?.Status ?? "").toString().trim() || "Open",
        startedAt: raw?.startedAt ?? raw?.StartedAt ?? null,
        closedAt: raw?.closedAt ?? raw?.ClosedAt ?? null,
        lastContent: (raw?.lastContent ?? raw?.LastContent ?? "").toString(),
        lastTime: raw?.lastTime ?? raw?.LastTime ?? raw?.startedAt ?? raw?.StartedAt ?? null,
        hasUnread: Boolean(raw?.hasUnread ?? raw?.HasUnread),
        shopName: (raw?.shopName ?? raw?.ShopName ?? raw?.userName ?? raw?.UserName ?? "").toString().trim(),
        avatar: (raw?.avatar ?? raw?.Avatar ?? "").toString().trim(),
        addressSummary: (raw?.addressSummary ?? raw?.AddressSummary ?? "").toString().trim(),
        joinedAt: raw?.joinedAt ?? raw?.JoinedAt ?? null
    });

    const normalizeMessage = (raw) => ({
        messageId: toNumber(raw?.messageId ?? raw?.MessageId, 0),
        from: (raw?.from ?? raw?.From ?? "system").toString().trim().toLowerCase(),
        content: (raw?.content ?? raw?.Content ?? "").toString(),
        createdAt: raw?.createdAt ?? raw?.CreatedAt ?? null,
        isDeleted: Boolean(raw?.isDeleted ?? raw?.IsDeleted),
        replyTo: raw?.replyTo ?? raw?.ReplyTo ?? null
    });

    const sortSummaries = (items) => items
        .slice()
        .sort((left, right) => {
            const rightTime = toDate(right.lastTime)?.getTime() ?? 0;
            const leftTime = toDate(left.lastTime)?.getTime() ?? 0;
            if (rightTime !== leftTime) {
                return rightTime - leftTime;
            }

            return right.conversationId - left.conversationId;
        });

    const setPanelOpen = (open) => {
        state.isOpen = open;
        host.classList.toggle("is-open", open);

        if (launcher instanceof HTMLButtonElement) {
            launcher.setAttribute("aria-expanded", open ? "true" : "false");
        }

        if (panel instanceof HTMLElement) {
            panel.setAttribute("aria-hidden", open ? "false" : "true");
        }

        if (open && config.isBuyerEligible) {
            void fetchSummaries({ preserveSelection: true });
            void ensureSignalR();
        } else if (typingNode instanceof HTMLElement) {
            typingNode.classList.add("d-none");
        }
    };

    const updateLauncher = () => {
        const unreadCount = state.summaries.filter((item) => item.hasUnread).length;

        if (launcherBadge instanceof HTMLElement) {
            launcherBadge.textContent = String(unreadCount);
            launcherBadge.classList.toggle("d-none", unreadCount <= 0);
        }

        if (launcherSubtitle instanceof HTMLElement) {
            if (!config.isAuthenticated) {
                launcherSubtitle.textContent = "Đăng nhập để xem tin nhắn";
            } else if (!config.isBuyerEligible) {
                launcherSubtitle.textContent = "Chỉ dành cho tài khoản mua hàng";
            } else if (unreadCount > 0) {
                launcherSubtitle.textContent = `${unreadCount} shop có phản hồi mới`;
            } else if (state.summaries.length > 0) {
                launcherSubtitle.textContent = `${state.summaries.length} shop đã trò chuyện`;
            } else {
                launcherSubtitle.textContent = "Chưa có hội thoại nào";
            }
        }

        if (panelSubtitle instanceof HTMLElement) {
            if (!config.isAuthenticated) {
                panelSubtitle.textContent = "Đăng nhập để mở lại các cuộc trò chuyện với shop.";
            } else if (state.summaries.length > 0) {
                panelSubtitle.textContent = "Danh sách shop bạn đã chat gần đây, mở lại ngay tại đây.";
            } else {
                panelSubtitle.textContent = "Widget sẽ hiện các shop bạn từng nhắn trên FreshFarm.";
            }
        }
    };

    const renderDetailState = () => {
        const showGuest = !config.isAuthenticated;
        const hasSelection = state.selectedConversationId > 0 && getConversationSummary(state.selectedConversationId);

        if (guestStateNode instanceof HTMLElement) {
            guestStateNode.classList.toggle("d-none", !showGuest);
        }

        if (threadNode instanceof HTMLElement) {
            threadNode.classList.toggle("d-none", !config.isBuyerEligible || !hasSelection);
        }

        if (emptyStateNode instanceof HTMLElement) {
            emptyStateNode.classList.toggle("d-none", showGuest || (config.isBuyerEligible && hasSelection));
        }
    };

    const renderConversationList = () => {
        if (!(listNode instanceof HTMLElement)) {
            return;
        }

        if (!config.isAuthenticated) {
            listNode.innerHTML = "<div class='floating-chat-list-state'>Đăng nhập để xem các shop đã từng trò chuyện.</div>";
            updateLauncher();
            renderDetailState();
            return;
        }

        const normalizedSearch = state.searchTerm.trim().toLowerCase();
        const filtered = state.summaries.filter((summary) => {
            if (state.filterMode === "unread" && !summary.hasUnread) {
                return false;
            }

            if (!normalizedSearch) {
                return true;
            }

            const haystack = [
                summary.shopName,
                summary.addressSummary,
                summary.lastContent,
                summary.sellerId
            ]
                .join(" ")
                .toLowerCase();

            return haystack.includes(normalizedSearch);
        });

        if (filtered.length === 0) {
            listNode.innerHTML = `<div class="floating-chat-list-state">${
                state.summaries.length === 0
                    ? "Bạn chưa có hội thoại nào. Hãy vào một shop để bắt đầu chat."
                    : "Không tìm thấy shop nào khớp bộ lọc hiện tại."
            }</div>`;
            updateLauncher();
            renderDetailState();
            return;
        }

        listNode.innerHTML = filtered
            .map((summary) => {
                const avatarUrl = resolveAvatar(summary);
                const activeClass = summary.conversationId === state.selectedConversationId ? " is-active" : "";
                const preview = (summary.lastContent || "Chưa có nội dung xem trước").trim();
                const statusBadge = summary.status === "Closed"
                    ? '<span class="floating-chat-pill closed">Đã đóng</span>'
                    : "";
                const unreadBadge = summary.hasUnread
                    ? '<span class="floating-chat-pill unread">Tin mới</span>'
                    : "";

                return `
                    <button type="button"
                            class="floating-chat-conversation-item${activeClass}"
                            data-conversation-id="${summary.conversationId}">
                        ${avatarUrl
                            ? `<img class="floating-chat-conversation-avatar" src="${escapeHtml(avatarUrl)}" alt="${escapeHtml(summary.shopName || `Shop ${summary.sellerId}`)}" />`
                            : `<span class="floating-chat-conversation-avatar">${escapeHtml((summary.shopName || `S${summary.sellerId}`).slice(0, 1).toUpperCase())}</span>`}
                        <span class="floating-chat-conversation-main">
                            <span class="floating-chat-conversation-head">
                                <span class="floating-chat-conversation-name">${escapeHtml(summary.shopName || `FreshFarm Seller ${summary.sellerId}`)}</span>
                                <span class="floating-chat-conversation-time">${escapeHtml(formatRelativeTime(summary.lastTime))}</span>
                            </span>
                            <span class="floating-chat-conversation-preview">${escapeHtml(preview)}</span>
                            <span class="floating-chat-conversation-meta">
                                <span class="floating-chat-conversation-address">${escapeHtml(summary.addressSummary || `Seller ID #${summary.sellerId}`)}</span>
                                <span>${statusBadge}${unreadBadge}</span>
                            </span>
                        </span>
                    </button>
                `;
            })
            .join("");

        updateLauncher();
        renderDetailState();
    };

    const renderMessages = () => {
        if (!(messagesNode instanceof HTMLElement)) {
            return;
        }

        if (state.isLoadingMessages) {
            messagesNode.innerHTML = "<div class='floating-chat-message-state'>Đang tải tin nhắn...</div>";
            return;
        }

        if (state.messages.length === 0) {
            messagesNode.innerHTML = "<div class='floating-chat-message-state'>Chưa có tin nhắn nào trong hội thoại này.</div>";
            return;
        }

        messagesNode.innerHTML = state.messages
            .map((message) => {
                const from = message.from === "seller" ? "from-seller" : (message.from === "buyer" ? "from-buyer" : "system");
                const replyContent = message.replyTo?.content
                    ? `
                        <div class="floating-chat-message-reply">
                            <strong>${escapeHtml(message.replyTo.from === "seller" ? "Shop" : "Bạn")}</strong><br />
                            ${escapeHtml(message.replyTo.content)}
                        </div>
                    `
                    : "";
                const content = message.isDeleted
                    ? "<em>Tin nhắn đã bị thu hồi.</em>"
                    : escapeHtml(message.content || "");

                return `
                    <div class="floating-chat-message-row ${from}" data-message-id="${message.messageId}">
                        <div class="floating-chat-message-bubble">
                            ${replyContent}
                            <div class="floating-chat-message-content">${content}</div>
                            <div class="floating-chat-message-meta">${escapeHtml(formatDateTime(message.createdAt))}</div>
                        </div>
                    </div>
                `;
            })
            .join("");

        messagesNode.scrollTop = messagesNode.scrollHeight;
    };

    const renderThreadHeader = () => {
        const summary = getConversationSummary(state.selectedConversationId);
        if (!summary) {
            renderDetailState();
            return;
        }

        const avatarUrl = resolveAvatar(summary);
        if (threadAvatarNode instanceof HTMLElement) {
            if (avatarUrl) {
                threadAvatarNode.innerHTML = `<img class="floating-chat-thread-avatar" src="${escapeHtml(avatarUrl)}" alt="${escapeHtml(summary.shopName || `Shop ${summary.sellerId}`)}" />`;
            } else {
                threadAvatarNode.textContent = (summary.shopName || `S${summary.sellerId}`).slice(0, 1).toUpperCase();
            }
        }

        if (threadNameNode instanceof HTMLElement) {
            threadNameNode.textContent = summary.shopName || `FreshFarm Seller ${summary.sellerId}`;
        }

        if (threadMetaNode instanceof HTMLElement) {
            const lastTime = summary.lastTime ? `Lần gần nhất: ${formatDateTime(summary.lastTime)}` : "Chưa có lịch sử tin nhắn.";
            const address = summary.addressSummary ? ` · ${summary.addressSummary}` : "";
            threadMetaNode.textContent = `${lastTime}${address}`;
        }

        if (threadStatusNode instanceof HTMLElement) {
            const isClosed = summary.status === "Closed";
            threadStatusNode.textContent = isClosed ? "Cuộc trò chuyện đã đóng" : "Đang mở";
            threadStatusNode.classList.toggle("is-closed", isClosed);
        }

        if (inputNode instanceof HTMLTextAreaElement) {
            inputNode.disabled = !config.isBuyerEligible || state.isSending;
            inputNode.placeholder = summary.status === "Closed"
                ? "Gửi tin nhắn mới để mở lại hội thoại với shop..."
                : "Nhập tin nhắn cho shop...";
        }

        if (composeHintNode instanceof HTMLElement) {
            composeHintNode.textContent = summary.status === "Closed"
                ? "Thread cũ đã đóng. Khi bạn gửi tin mới, hệ thống sẽ tự mở hội thoại mới với shop này."
                : "Khi đang mở hội thoại này, tin nhắn từ shop sẽ cập nhật realtime.";
        }

        renderDetailState();
    };

    const upsertSummary = (summary) => {
        const normalized = normalizeSummary(summary);
        const existingIndex = state.summaries.findIndex((item) => item.conversationId === normalized.conversationId);
        if (existingIndex >= 0) {
            state.summaries[existingIndex] = {
                ...state.summaries[existingIndex],
                ...normalized
            };
        } else {
            state.summaries.push(normalized);
        }

        state.summaries = sortSummaries(state.summaries);
        renderConversationList();
        renderThreadHeader();
    };

    const upsertMessage = (message) => {
        const normalized = normalizeMessage(message);
        const existingIndex = state.messages.findIndex((item) => item.messageId === normalized.messageId && normalized.messageId > 0);
        if (existingIndex >= 0) {
            state.messages[existingIndex] = normalized;
        } else {
            state.messages.push(normalized);
            state.messages.sort((left, right) => {
                const leftTime = toDate(left.createdAt)?.getTime() ?? 0;
                const rightTime = toDate(right.createdAt)?.getTime() ?? 0;
                return leftTime - rightTime;
            });
        }

        renderMessages();
    };

    const markConversationAsReadLocally = (conversationId) => {
        const summary = getConversationSummary(conversationId);
        if (!summary || !summary.hasUnread) {
            return;
        }

        summary.hasUnread = false;
        renderConversationList();
    };

    const postMarkRead = async (conversationId) => {
        if (!config.isBuyerEligible || conversationId <= 0) {
            return;
        }

        try {
            await fetch(buildUrl(config.markReadUrlTemplate, conversationId), {
                method: "POST",
                headers: {
                    "RequestVerificationToken": antiForgeryToken,
                    "Accept": "application/json"
                }
            });
            markConversationAsReadLocally(conversationId);
        } catch {
            // polling will correct later
        }
    };

    const joinConversationGroup = async (conversationId) => {
        if (!state.hubReady || !state.hub || conversationId <= 0 || state.joinedConversationId === conversationId) {
            return;
        }

        try {
            if (state.joinedConversationId > 0) {
                await state.hub.invoke("LeaveConversation", state.joinedConversationId);
            }

            await state.hub.invoke("JoinConversation", conversationId);
            state.joinedConversationId = conversationId;
        } catch (error) {
            console.warn("[floating-buyer-chat] Could not join conversation group.", error);
        }
    };

    const ensureSignalRClientScript = async () => {
        if (window.signalR?.HubConnectionBuilder) {
            return true;
        }

        if (state.signalRRequested) {
            return false;
        }

        state.signalRRequested = true;
        return new Promise((resolve) => {
            const script = document.createElement("script");
            script.src = "https://cdn.jsdelivr.net/npm/@microsoft/signalr@8.0.7/dist/browser/signalr.min.js";
            script.async = true;
            script.onload = () => resolve(Boolean(window.signalR?.HubConnectionBuilder));
            script.onerror = () => resolve(false);
            document.head.appendChild(script);
        });
    };

    const ensureSignalR = async () => {
        if (!config.isBuyerEligible || state.hub || state.hubReady) {
            return;
        }

        const loaded = await ensureSignalRClientScript();
        if (!loaded || !window.signalR?.HubConnectionBuilder) {
            return;
        }

        const connection = new window.signalR.HubConnectionBuilder()
            .withUrl(config.hubUrl)
            .withAutomaticReconnect()
            .build();

        connection.on("receiveMessage", async (payload) => {
            const conversationId = toNumber(payload?.conversationId, 0);
            const message = payload?.message ?? payload;
            if (conversationId <= 0 || conversationId !== state.selectedConversationId) {
                return;
            }

            upsertMessage(message);
            if (state.isOpen) {
                await postMarkRead(conversationId);
            }

            void fetchSummaries({ preserveSelection: true, silent: true });
        });

        connection.on("userTyping", (payload) => {
            if (toNumber(payload?.conversationId, 0) !== state.selectedConversationId || !(typingNode instanceof HTMLElement)) {
                return;
            }

            typingNode.classList.remove("d-none");
            window.clearTimeout(state.typingTimer);
            state.typingTimer = window.setTimeout(() => {
                typingNode.classList.add("d-none");
            }, 2400);
        });

        connection.onclose(() => {
            state.hubReady = false;
            state.joinedConversationId = 0;
        });

        connection.onreconnected(async () => {
            state.hubReady = true;
            if (state.selectedConversationId > 0) {
                await joinConversationGroup(state.selectedConversationId);
            }
        });

        try {
            await connection.start();
            state.hub = connection;
            state.hubReady = true;
            if (state.selectedConversationId > 0) {
                await joinConversationGroup(state.selectedConversationId);
            }
        } catch (error) {
            console.warn("[floating-buyer-chat] SignalR start failed. Falling back to polling.", error);
            state.hub = null;
            state.hubReady = false;
        }
    };

    const fetchMessages = async (conversationId) => {
        if (!config.isBuyerEligible || conversationId <= 0) {
            return;
        }

        state.isLoadingMessages = true;
        renderMessages();

        try {
            const response = await fetch(`${buildUrl(config.messagesUrlTemplate, conversationId)}?take=100`, {
                headers: {
                    "Accept": "application/json"
                }
            });
            const payload = await readJson(response);
            if (!response.ok) {
                throw createHttpError(response, payload, "Không thể tải tin nhắn.");
            }

            state.messages = Array.isArray(payload?.messages)
                ? payload.messages.map(normalizeMessage)
                : [];

            renderMessages();
            await postMarkRead(conversationId);
        } catch (error) {
            state.messages = [];
            renderMessages();
            showToast(error?.message || "Không thể tải tin nhắn.", "error");
        } finally {
            state.isLoadingMessages = false;
            renderMessages();
        }
    };

    const selectConversation = async (conversationId) => {
        const summary = getConversationSummary(conversationId);
        if (!summary) {
            return;
        }

        state.selectedConversationId = conversationId;
        state.messages = [];
        renderConversationList();
        renderThreadHeader();
        renderMessages();

        await ensureSignalR();
        await joinConversationGroup(conversationId);
        await fetchMessages(conversationId);
    };

    const ensureConversationOpen = async (summary) => {
        if (!summary || summary.status !== "Closed" || summary.sellerId <= 0) {
            return summary;
        }

        const response = await fetch(`${buildUrl(config.conversationBySellerUrlTemplate, summary.sellerId)}?createIfMissing=true`, {
            headers: {
                "Accept": "application/json"
            }
        });
        const payload = await readJson(response);
        if (!response.ok) {
            throw createHttpError(response, payload, "Không thể mở lại hội thoại với shop.");
        }

        const conversation = payload?.conversation;
        if (!conversation) {
            throw new Error("Không thể mở lại hội thoại với shop.");
        }

        const reopenedSummary = normalizeSummary({
            ...summary,
            ...conversation,
            sellerId: summary.sellerId,
            shopName: summary.shopName,
            avatar: summary.avatar,
            addressSummary: summary.addressSummary,
            joinedAt: summary.joinedAt
        });

        const oldConversationId = state.selectedConversationId;
        upsertSummary(reopenedSummary);
        state.selectedConversationId = reopenedSummary.conversationId;

        if (oldConversationId !== reopenedSummary.conversationId) {
            state.messages = [];
            renderConversationList();
            renderThreadHeader();
            renderMessages();
            await joinConversationGroup(reopenedSummary.conversationId);
        }

        return getConversationSummary(reopenedSummary.conversationId) || reopenedSummary;
    };

    const fetchSummaries = async ({ preserveSelection = true, silent = false } = {}) => {
        if (!config.isBuyerEligible) {
            updateLauncher();
            renderConversationList();
            return;
        }

        if (state.isLoadingSummaries && !silent) {
            return;
        }

        state.isLoadingSummaries = !silent;
        if (!silent && listNode instanceof HTMLElement && state.summaries.length === 0) {
            listNode.innerHTML = "<div class='floating-chat-list-state'>Đang tải lịch sử chat...</div>";
        }

        try {
            const response = await fetch(config.summariesUrl, {
                headers: {
                    "Accept": "application/json"
                }
            });
            const payload = await readJson(response);
            if (!response.ok) {
                throw createHttpError(response, payload, "Không thể tải danh sách chat.");
            }

            const previousConversationId = preserveSelection ? state.selectedConversationId : 0;
            const summaries = Array.isArray(payload?.summaries)
                ? sortSummaries(payload.summaries.map(normalizeSummary))
                : [];

            state.summaries = summaries;

            if (previousConversationId > 0 && getConversationSummary(previousConversationId)) {
                state.selectedConversationId = previousConversationId;
            } else if (state.selectedConversationId > 0 && !getConversationSummary(state.selectedConversationId)) {
                state.selectedConversationId = 0;
                state.messages = [];
            }

            if (state.selectedConversationId <= 0 && state.summaries.length > 0 && state.isOpen) {
                state.selectedConversationId = state.summaries[0].conversationId;
                state.messages = [];
            }

            renderConversationList();
            renderThreadHeader();
            if (state.selectedConversationId > 0 && state.messages.length === 0 && state.isOpen) {
                await selectConversation(state.selectedConversationId);
            }
        } catch (error) {
            if (!silent) {
                showToast(error?.message || "Không thể tải danh sách chat.", "error");
            }
        } finally {
            state.isLoadingSummaries = false;
            updateLauncher();
            renderDetailState();
        }
    };

    const sendMessage = async () => {
        if (!(inputNode instanceof HTMLTextAreaElement) || !(sendButton instanceof HTMLButtonElement) || state.isSending) {
            return;
        }

        const rawContent = inputNode.value.trim();
        if (!rawContent) {
            inputNode.focus();
            return;
        }

        const selectedSummary = getConversationSummary(state.selectedConversationId);
        if (!selectedSummary) {
            showToast("Hãy chọn một shop trước khi gửi tin nhắn.", "error");
            return;
        }

        state.isSending = true;
        inputNode.disabled = true;
        sendButton.disabled = true;

        try {
            const activeSummary = await ensureConversationOpen(selectedSummary);
            const response = await fetch(buildUrl(config.sendMessageUrlTemplate, activeSummary.conversationId), {
                method: "POST",
                headers: {
                    "Content-Type": "application/json",
                    "RequestVerificationToken": antiForgeryToken,
                    "Accept": "application/json"
                },
                body: JSON.stringify({
                    content: rawContent,
                    sellerId: activeSummary.sellerId
                })
            });
            const payload = await readJson(response);
            if (!response.ok) {
                throw createHttpError(response, payload, "Không thể gửi tin nhắn.");
            }

            inputNode.value = "";
            if (payload?.message) {
                upsertMessage(payload.message);
            } else {
                await fetchMessages(activeSummary.conversationId);
            }

            upsertSummary({
                ...activeSummary,
                lastContent: rawContent,
                lastTime: new Date().toISOString(),
                status: "Open",
                hasUnread: false
            });

            await fetchSummaries({ preserveSelection: true, silent: true });
            inputNode.focus();
        } catch (error) {
            showToast(error?.message || "Không thể gửi tin nhắn.", "error");
        } finally {
            state.isSending = false;
            inputNode.disabled = false;
            sendButton.disabled = false;
            renderThreadHeader();
        }
    };

    const startPolling = () => {
        if (!config.isBuyerEligible || state.pollTimer) {
            return;
        }

        state.pollTimer = window.setInterval(() => {
            if (document.hidden) {
                return;
            }

            void fetchSummaries({ preserveSelection: true, silent: true });
        }, 12000);
    };

    if (launcher instanceof HTMLButtonElement) {
        launcher.addEventListener("click", () => {
            setPanelOpen(!state.isOpen);
        });
    }

    if (refreshButton instanceof HTMLButtonElement) {
        refreshButton.addEventListener("click", () => {
            void fetchSummaries({ preserveSelection: true });
            if (state.selectedConversationId > 0) {
                void fetchMessages(state.selectedConversationId);
            }
        });
    }

    if (closeButton instanceof HTMLButtonElement) {
        closeButton.addEventListener("click", () => {
            setPanelOpen(false);
        });
    }

    externalToggles.forEach((toggle) => {
        toggle.addEventListener("click", (event) => {
            event.preventDefault();
            setPanelOpen(true);
        });
    });

    if (searchInput instanceof HTMLInputElement) {
        searchInput.addEventListener("input", () => {
            state.searchTerm = searchInput.value || "";
            renderConversationList();
        });
    }

    filterButtons.forEach((button) => {
        button.addEventListener("click", () => {
            state.filterMode = button.getAttribute("data-floating-chat-filter") || "all";
            filterButtons.forEach((item) => item.classList.toggle("is-active", item === button));
            renderConversationList();
        });
    });

    if (listNode instanceof HTMLElement) {
        listNode.addEventListener("click", (event) => {
            const target = event.target instanceof HTMLElement
                ? event.target.closest("[data-conversation-id]")
                : null;

            if (!(target instanceof HTMLElement)) {
                return;
            }

            const conversationId = toNumber(target.getAttribute("data-conversation-id"), 0);
            if (conversationId > 0) {
                void selectConversation(conversationId);
            }
        });
    }

    if (sendButton instanceof HTMLButtonElement) {
        sendButton.addEventListener("click", () => {
            void sendMessage();
        });
    }

    if (inputNode instanceof HTMLTextAreaElement) {
        inputNode.addEventListener("keydown", (event) => {
            if (event.key === "Enter" && !event.shiftKey) {
                event.preventDefault();
                void sendMessage();
            }
        });

        inputNode.addEventListener("input", async () => {
            if (!state.hubReady || !state.hub || state.selectedConversationId <= 0) {
                return;
            }

            try {
                await state.hub.invoke("NotifyTyping", state.selectedConversationId);
            } catch {
                // ignore realtime typing failures
            }
        });
    }

    document.addEventListener("keydown", (event) => {
        if (event.key === "Escape" && state.isOpen) {
            setPanelOpen(false);
        }
    });

    updateLauncher();
    renderConversationList();
    renderDetailState();
    startPolling();

    if (config.isBuyerEligible) {
        void fetchSummaries({ preserveSelection: true, silent: true });
    }
})();
