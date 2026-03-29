(function () {
    "use strict";

    const appHelpers = window.FreshFarmApp;

    function escapeHtml(value) {
        return (value ?? "")
            .toString()
            .replaceAll("&", "&amp;")
            .replaceAll("<", "&lt;")
            .replaceAll(">", "&gt;")
            .replaceAll('"', "&quot;")
            .replaceAll("'", "&#39;");
    }

    function parseServerDate(value) {
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
    }

    function formatTime(value) {
        if (!value) {
            return "";
        }

        const date = parseServerDate(value);
        if (!date) {
            return "";
        }

        return date.toLocaleString("vi-VN", {
            day: "2-digit",
            month: "2-digit",
            year: "numeric",
            hour: "2-digit",
            minute: "2-digit"
        });
    }

    function buildUrl(template, value) {
        return template.replace("__id__", String(value ?? ""));
    }

    async function readJson(response) {
        const text = await response.text();
        if (!text) {
            return null;
        }

        try {
            return JSON.parse(text);
        } catch {
            return null;
        }
    }

    function renderMessages(messagesNode, messages) {
        if (!Array.isArray(messages) || messages.length === 0) {
            messagesNode.innerHTML = "<div class='shop-chat-empty'>Chưa có tin nhắn nào. Hãy bắt đầu cuộc trò chuyện với shop.</div>";
            return;
        }

        messagesNode.innerHTML = messages.map((message) => {
            const from = (message.from ?? "").toString().toLowerCase();
            const bubbleClass = from === "seller" ? "shop-chat-bubble seller" : "shop-chat-bubble buyer";
            const actorLabel = from === "seller" ? "Shop" : "Bạn";
            const replyMarkup = message.replyTo && message.replyTo.content
                ? `<div class="shop-chat-reply">
                        <div class="shop-chat-reply-label">${escapeHtml(message.replyTo.from === "seller" ? "Shop" : "Bạn")}</div>
                        <div>${escapeHtml(message.replyTo.content)}</div>
                   </div>`
                : "";

            return `
                <div class="shop-chat-message ${from === "seller" ? "seller" : "buyer"}">
                    <div class="${bubbleClass}">
                        <div class="shop-chat-actor">${escapeHtml(actorLabel)}</div>
                        ${replyMarkup}
                        <div>${message.isDeleted ? "<em>Tin nhắn đã bị thu hồi</em>" : escapeHtml(message.content ?? "")}</div>
                        <div class="shop-chat-time">${escapeHtml(formatTime(message.createdAt))}</div>
                    </div>
                </div>
            `;
        }).join("");

        messagesNode.scrollTop = messagesNode.scrollHeight;
    }

    window.PublicShopChat = {
        init: function init(config) {
            const root = document.getElementById(config.rootId || "publicShopChat");
            if (!(root instanceof HTMLElement)) {
                return;
            }

            if (!config.isAuthenticated) {
                root.innerHTML = `
                    <div class="shop-chat-prompt">
                        <div class="shop-chat-prompt-title">Đăng nhập để nhắn shop</div>
                        <p>FreshFarm sẽ gắn cuộc trò chuyện với tài khoản mua hàng của bạn để shop phản hồi thuận tiện hơn.</p>
                        <a class="shop-chat-signin" href="${escapeHtml(config.signInUrl || "/account/signin")}">Đăng nhập và bắt đầu chat</a>
                    </div>
                `;
                return;
            }

            if (!config.isBuyerEligible) {
                root.innerHTML = `
                    <div class="shop-chat-prompt">
                        <div class="shop-chat-prompt-title">Chat buyer-seller đang dành cho tài khoản mua hàng</div>
                        <p>Tài khoản Seller/Admin hiện không dùng lane chat công khai này.</p>
                    </div>
                `;
                return;
            }

            root.innerHTML = `
                <div class="shop-chat-shell">
                    <div class="shop-chat-toolbar">
                        <div>
                            <div class="shop-chat-title">Nhắn shop <span id="shopChatBadge" class="shop-chat-badge d-none"></span></div>
                            <div class="shop-chat-subtitle" id="shopChatStatus">Đang chuẩn bị hội thoại...</div>
                        </div>
                    </div>
                    <div class="shop-chat-messages" id="shopChatMessages">
                        <div class="shop-chat-empty">Đang tải cuộc trò chuyện...</div>
                    </div>
                    <div class="shop-chat-compose">
                        <textarea id="shopChatInput" class="shop-chat-input" rows="3" placeholder="Nhập nội dung bạn muốn hỏi shop..."></textarea>
                        <button type="button" id="shopChatSend" class="shop-chat-send">Gửi tin</button>
                    </div>
                </div>
            `;

            const statusNode = document.getElementById("shopChatStatus");
            const badgeNode = document.getElementById("shopChatBadge");
            const messagesNode = document.getElementById("shopChatMessages");
            const inputNode = document.getElementById("shopChatInput");
            const sendNode = document.getElementById("shopChatSend");

            const state = {
                conversationId: 0,
                conversationStatus: "Open",
                pollTimer: null,
                hub: null,
                isRealtimeReady: false,
                joinedConversationId: 0,
                isSending: false,
                typingTimeout: null
            };

            const setStatus = (value) => {
                if (statusNode instanceof HTMLElement) {
                    statusNode.textContent = value;
                }
            };

            const setBadge = (value) => {
                if (!(badgeNode instanceof HTMLElement)) {
                    return;
                }

                if (!value) {
                    badgeNode.classList.add("d-none");
                    badgeNode.textContent = "";
                    return;
                }

                badgeNode.classList.remove("d-none");
                badgeNode.textContent = value;
            };

            const notifyConversationChanged = (conversation) => {
                if (typeof config.onConversationChanged === "function") {
                    config.onConversationChanged(conversation);
                }
            };

            const setComposerEnabled = (enabled) => {
                if (inputNode instanceof HTMLTextAreaElement) {
                    inputNode.disabled = !enabled;
                }

                if (sendNode instanceof HTMLButtonElement) {
                    sendNode.disabled = !enabled;
                }
            };

            const setPollingEnabled = (enabled) => {
                if (enabled) {
                    if (state.pollTimer) {
                        return;
                    }

                    state.pollTimer = window.setInterval(() => {
                        if (document.hidden || state.conversationId <= 0) {
                            return;
                        }

                        void loadMessages();
                    }, 8000);
                    return;
                }

                if (state.pollTimer) {
                    window.clearInterval(state.pollTimer);
                    state.pollTimer = null;
                }
            };

            const joinConversationGroup = async (conversationId) => {
                if (!state.hub || !state.isRealtimeReady || conversationId <= 0) {
                    return;
                }

                try {
                    if (state.joinedConversationId > 0 && state.joinedConversationId !== conversationId) {
                        await state.hub.invoke("LeaveConversation", state.joinedConversationId);
                    }

                    if (state.joinedConversationId !== conversationId) {
                        await state.hub.invoke("JoinConversation", conversationId);
                        state.joinedConversationId = conversationId;
                    }
                } catch (error) {
                    console.warn("[public-shop-chat] Join conversation group failed.", error);
                }
            };

            const setupSignalR = () => {
                if (!window.signalR || !window.signalR.HubConnectionBuilder) {
                    console.warn("[public-shop-chat] ASP.NET Core SignalR client not loaded. Fallback to polling mode.");
                    state.isRealtimeReady = false;
                    return false;
                }

                const connection = new window.signalR.HubConnectionBuilder()
                    .withUrl(config.hubUrl || "/hubs/support-chat")
                    .withAutomaticReconnect()
                    .build();

                state.hub = connection;

                connection.on("receiveMessage", (payload) => {
                    const conversationId = Number(payload?.conversationId || payload?.message?.conversationId || 0);
                    if (conversationId <= 0 || conversationId !== state.conversationId) {
                        return;
                    }

                    void loadConversation(false)
                        .then(() => loadMessages())
                        .catch(() => { });
                });

                connection.on("newConversationOrMessage", (payload) => {
                    const conversationId = Number(payload?.conversationId || 0);
                    if (conversationId <= 0 || conversationId !== state.conversationId) {
                        return;
                    }

                    void loadConversation(false)
                        .then(() => loadMessages())
                        .catch(() => { });
                });

                connection.on("userTyping", (payload) => {
                    const conversationId = Number(payload?.conversationId || 0);
                    if (conversationId <= 0 || conversationId !== state.conversationId) {
                        return;
                    }

                    setStatus("Shop đang nhập tin nhắn...");
                    window.clearTimeout(state.typingTimeout);
                    state.typingTimeout = window.setTimeout(() => {
                        void loadConversation(false).catch(() => { });
                    }, 2500);
                });

                connection.onclose(() => {
                    state.isRealtimeReady = false;
                    state.joinedConversationId = 0;
                    setPollingEnabled(true);
                });

                connection.onreconnected(async () => {
                    state.isRealtimeReady = true;
                    setPollingEnabled(false);
                    await joinConversationGroup(state.conversationId);
                });

                connection.start()
                    .then(async () => {
                        state.isRealtimeReady = true;
                        setPollingEnabled(false);
                        await joinConversationGroup(state.conversationId);
                    })
                    .catch((error) => {
                        console.warn("[public-shop-chat] SignalR start failed. Fallback to polling mode.", error);
                        state.isRealtimeReady = false;
                        setPollingEnabled(true);
                    });

                return true;
            };

            const notifyTyping = () => {
                if (!state.hub || !state.isRealtimeReady || state.conversationId <= 0) {
                    return;
                }

                state.hub.invoke("NotifyTyping", state.conversationId).catch(() => { });
            };

            const loadConversation = async (createIfMissing) => {
                const response = await fetch(`${config.conversationUrl}?createIfMissing=${createIfMissing ? "true" : "false"}`, {
                    headers: { Accept: "application/json" }
                });

                const payload = await readJson(response);
                if (!response.ok) {
                    throw (appHelpers?.createHttpError(response, payload, "Không thể tải hội thoại với shop.")
                        ?? new Error(payload?.message || "Không thể tải hội thoại với shop."));
                }

                state.conversationId = Number(payload?.conversation?.conversationId || 0);
                const conversation = payload?.conversation || null;
                const status = (conversation?.status || "Open").toString();
                state.conversationStatus = status;

                if (state.conversationId <= 0) {
                    state.conversationStatus = "Open";
                    setStatus("Chưa có hội thoại nào. Hãy gửi câu hỏi đầu tiên cho shop.");
                    setBadge("");
                    renderMessages(messagesNode, []);
                    notifyConversationChanged(null);
                    return null;
                }

                await joinConversationGroup(state.conversationId);

                const lastTime = formatTime(conversation?.lastTime);
                const hasUnread = Boolean(conversation?.hasUnread);
                if (status === "Closed") {
                    setStatus(lastTime
                        ? `Hội thoại trước đã kết thúc lúc ${lastTime}. Gửi tin mới để mở cuộc trò chuyện mới với shop.`
                        : "Hội thoại trước đã kết thúc. Gửi tin mới để mở cuộc trò chuyện mới với shop.");
                } else if (hasUnread) {
                    setStatus(lastTime ? `Shop vừa phản hồi lúc ${lastTime}.` : "Shop có tin nhắn mới.");
                } else if (lastTime) {
                    setStatus(`Lần trao đổi gần nhất: ${lastTime}.`);
                } else {
                    setStatus("Shop sẽ phản hồi trong khung chat này.");
                }

                setBadge(hasUnread ? "Tin mới" : "");
                setComposerEnabled(true);
                notifyConversationChanged(conversation);
                return conversation;
            };

            const loadMessages = async () => {
                if (state.conversationId <= 0) {
                    return;
                }

                const response = await fetch(buildUrl(config.messagesUrlTemplate, state.conversationId), {
                    headers: { Accept: "application/json" }
                });

                const payload = await readJson(response);
                if (!response.ok) {
                    throw (appHelpers?.createHttpError(response, payload, "Không thể tải tin nhắn.")
                        ?? new Error(payload?.message || "Không thể tải tin nhắn."));
                }

                renderMessages(messagesNode, payload?.messages || []);

                fetch(buildUrl(config.markReadUrlTemplate, state.conversationId), {
                    method: "POST",
                    headers: {
                        "RequestVerificationToken": config.antiForgeryToken,
                        "Accept": "application/json"
                    }
                })
                    .then(function () {
                        setBadge("");
                    })
                    .catch(function () { });
            };

            const ensureConversation = async () => {
                if (state.conversationId > 0 && state.conversationStatus !== "Closed") {
                    return state.conversationId;
                }

                await loadConversation(true);
                return state.conversationId;
            };

            const sendMessage = async () => {
                if (!(inputNode instanceof HTMLTextAreaElement) || state.isSending) {
                    return;
                }

                const content = inputNode.value.trim();
                if (!content) {
                    inputNode.focus();
                    return;
                }

                state.isSending = true;
                setComposerEnabled(false);

                try {
                    const conversationId = await ensureConversation();
                    if (conversationId <= 0) {
                        throw new Error("Không thể khởi tạo hội thoại với shop.");
                    }

                    const response = await fetch(buildUrl(config.sendUrlTemplate, conversationId), {
                        method: "POST",
                        headers: {
                            "Content-Type": "application/json",
                            "RequestVerificationToken": config.antiForgeryToken,
                            "Accept": "application/json"
                        },
                        body: JSON.stringify({ content: content, sellerId: Number(config.sellerId || 0) })
                    });

                    const payload = await readJson(response);
                    if (!response.ok) {
                        throw (appHelpers?.createHttpError(response, payload, "Không thể gửi tin nhắn.")
                            ?? new Error(payload?.message || "Không thể gửi tin nhắn."));
                    }

                    inputNode.value = "";
                    await loadMessages();
                    await loadConversation(false);
                } catch (error) {
                    const message = error?.message || "Không thể gửi tin nhắn.";
                    appHelpers?.showToast(message, "error");
                    setStatus(message);
                } finally {
                    state.isSending = false;
                    setComposerEnabled(true);
                }
            };

            if (sendNode instanceof HTMLButtonElement) {
                sendNode.addEventListener("click", sendMessage);
            }

            if (inputNode instanceof HTMLTextAreaElement) {
                inputNode.addEventListener("keydown", (event) => {
                    if (event.key === "Enter" && !event.shiftKey) {
                        event.preventDefault();
                        void sendMessage();
                    }
                });
                inputNode.addEventListener("input", () => {
                    notifyTyping();
                });
            }

            setPollingEnabled(true);
            setupSignalR();

            Promise.resolve()
                .then(() => loadConversation(false))
                .then(() => loadMessages())
                .catch((error) => {
                    setStatus(error?.message || "Không thể tải chat.");
                    renderMessages(messagesNode, []);
                });
        }
    };
})();
