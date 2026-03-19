(function ($) {
    'use strict';

    const SupportConfig = $.extend({
        orderPageUrl: '/Seller/Order/ManageOrders',
        forcePolling: false
    }, window.supportChatAdminConfig || {});

    // ==========================================
    // Chat Data & State Management
    // ==========================================
    const ChatState = {
        currentConvId: null,
        currentReplyToId: null,
        filterMode: 'all',
        allConversations: [],
        hub: null,
        isRealtimeReady: false,
        pollTimer: null,
        lastMessageDate: null, // Track last message date for separator

        init: function () {
            this.hub = null;
            this.isRealtimeReady = false;
        }
    };

    const SupportRoutes = $.extend({
        conversations: '/Seller/SupportChat/Conversations',
        messages: '/Seller/SupportChat/Messages',
        conversationDetails: '/Seller/SupportChat/ConversationDetails',
        close: '/Seller/SupportChat/Close',
        markAsRead: '/Seller/SupportChat/MarkAsRead',
        sendMessage: '/Seller/SupportChat/SendMessage'
    }, window.supportChatAdminRoutes || {});

    function getAntiForgeryToken() {
        return $('input[name="__RequestVerificationToken"]').first().val() || '';
    }

    // ==========================================
    // UI Rendering & Interaction
    // ==========================================
    const ChatUI = {
        elements: {
            list: $('#supportConvListWrapper'),
            messages: $('#supportAdminMessages'),
            input: $('#supportAdminInput'),
            closeBtn: $('#supportChatCloseBtn'),
            search: $('#supportConvSearch'),
            typing: $('#supportAdminTyping'),
            replyPreview: $('#supportReplyPreview'),
            replyPreviewText: $('#supportReplyPreviewText'),
            replyCancel: $('#supportReplyCancel'),
            sendBtn: $('#supportAdminSend'),
            title: $('#supportChatTitle'),
            subTitle: $('#supportChatSubTitle'),
            profilePane: $('#pane-profile'),
            ordersPane: $('#pane-orders'),
            filterBtns: $('.btn-group .btn-outline-secondary')
        },

        init: function () {
            this.bindEvents();
        },

        bindEvents: function () {
            const self = this;

            // Filter buttons
            this.elements.filterBtns.on('click', function () {
                self.elements.filterBtns.removeClass('active');
                $(this).addClass('active');
                ChatState.filterMode = $(this).data('filter');
                self.applyFilterAndRender();
            });

            // Search input
            this.elements.search.on('keyup', function () {
                self.applyFilterAndRender();
            });

            // Conversation click
            this.elements.list.on('click', '.list-group-item', function () {
                const convId = $(this).data('conv-id');
                ChatController.selectConversation(convId);
            });

            // Send button
            this.elements.sendBtn.on('click', function () {
                ChatController.sendMessage();
            });

            // Input enter key
            this.elements.input.on('keypress', function (e) {
                if (e.which === 13 && !e.shiftKey) {
                    e.preventDefault();
                    ChatController.sendMessage();
                }
            });

            // Input typing
            this.elements.input.on('input', function () {
                ChatController.notifyTyping();
            });

            // Close button
            this.elements.closeBtn.on('click', function () {
                if (confirm('Bạn có chắc muốn kết thúc cuộc trò chuyện này?')) {
                    ChatController.closeConversation();
                }
            });

            // Message actions (delegate)
            this.elements.messages.on('click', '.msg-action-reply', function (e) {
                e.preventDefault();
                const $msg = $(this).closest('.support-chat-message');
                const id = $msg.data('id');
                const content = $msg.find('.bubble').text().trim();
                ChatUI.setReplyMode(id, content);
            });

            this.elements.messages.on('click', '.msg-action-recall', function (e) {
                e.preventDefault();
                const $msg = $(this).closest('.support-chat-message');
                const id = $msg.data('id');
                if (confirm('Bạn có chắc muốn thu hồi tin nhắn này?')) {
                    ChatController.recallMessage(id);
                }
            });

            // Cancel reply
            this.elements.replyCancel.on('click', function () {
                ChatUI.clearReplyMode();
            });

            // Keep only reply actions in hover menu.
        },

        renderConversations: function (convs) {
            const $list = this.elements.list;
            $list.empty();

            if (!convs || !convs.length) {
                $list.append('<div class="p-3 text-muted small text-center">Chưa có cuộc trò chuyện nào.</div>');
                return;
            }

            convs.forEach(function (c) {
                let displayName = c.UserName;
                if (!displayName) {
                    displayName = c.GuestId ? 'Khách vãng lai #' + c.ConversationId : 'Khách #' + c.ConversationId;
                }

                let preview = c.LastContent || '';
                if (preview.length > 40) preview = preview.substring(0, 37) + '...';

                const badge = c.HasUnread ? '<span class="badge bg-danger ms-auto">Mới</span>' : '';
                
                let statusBadge = '';
                if (c.Status === 'Closed') {
                    statusBadge = '<span class="badge bg-secondary ms-1">Closed</span>';
                } else if (c.Status === 'Open') {
                    statusBadge = '<span class="badge bg-success ms-1">Open</span>';
                }

                let avatarHtml = '';
                if (c.UserId) {
                    const avatarUrl = '/Account/AvatarById?id=' + c.UserId; // Hardcoded url for simplicity, can be improved
                    avatarHtml = `<img src="${avatarUrl}" class="rounded-circle me-2" style="width:32px;height:32px;object-fit:cover;" />`;
                } else {
                    avatarHtml = '<div class="rounded-circle bg-light border me-2 d-flex align-items-center justify-content-center" style="width:32px;height:32px;"><i class="bi bi-person text-muted"></i></div>';
                }

                const timeText = c.LastTime ? ChatUtils.formatRelativeTime(c.LastTime) : '';
                const activeClass = c.ConversationId === ChatState.currentConvId ? 'active' : '';

                const html = `
                    <button type="button" class="list-group-item list-group-item-action d-flex align-items-start ${activeClass}" data-conv-id="${c.ConversationId}">
                        ${avatarHtml}
                        <div class="flex-grow-1">
                            <div class="d-flex justify-content-between align-items-center">
                                <div class="fw-semibold">${ChatUtils.htmlEncode(displayName)}</div>
                                <small class="text-muted ms-2">${ChatUtils.htmlEncode(timeText)}</small>
                            </div>
                            <div class="d-flex justify-content-between align-items-center">
                                <small class="text-muted text-truncate me-2" style="max-width:140px;">${ChatUtils.htmlEncode(preview)}</small>
                                <div class="d-flex align-items-center">${statusBadge}${badge}</div>
                            </div>
                        </div>
                    </button>`;

                $list.append(html);
            });
        },

        applyFilterAndRender: function () {
            const term = this.elements.search.val().toLowerCase();
            const mode = ChatState.filterMode;

            const filtered = ChatState.allConversations.filter(function (c) {
                // Text search
                const name = (c.UserName || '').toLowerCase();
                const phone = (c.UserPhone || '').toLowerCase();
                const guest = (c.GuestId || '').toLowerCase();
                const matchText = name.includes(term) || phone.includes(term) || guest.includes(term) || c.ConversationId.toString().includes(term);
                if (!matchText) return false;

                // Mode filter
                if (mode === 'unread') return c.HasUnread;
                if (mode === 'open') return c.Status === 'Open';
                return true;
            });

            this.renderConversations(filtered);
        },

        renderMessages: function (messages) {
            const $msgs = this.elements.messages;
            $msgs.empty();
            ChatState.lastMessageDate = null; // Reset date separator tracker

            if (!messages || !messages.length) {
                $msgs.append('<p class="text-muted text-center">Chưa có tin nhắn nào.</p>');
                return;
            }

            messages.forEach(function (m) {
                ChatUI.appendMessage(m);
            });
            this.scrollToBottom();
        },

        appendMessage: function (m) {
            if (m && m.messageId) {
                const existed = this.elements.messages.find(`.support-chat-message[data-id="${m.messageId}"]`);
                if (existed.length) {
                    return;
                }
            }

            // Insert date separator if date changed
            var currentDateKey = ChatUtils.getDateKey(m.createdAt);
            if (currentDateKey && currentDateKey !== ChatState.lastMessageDate) {
                var separatorText = ChatUtils.formatDateSeparator(m.createdAt);
                if (separatorText) {
                    var separatorHtml = '<div class="support-chat-date-separator"><span>' + 
                        ChatUtils.htmlEncode(separatorText) + '</span></div>';
                    this.elements.messages.append(separatorHtml);
                }
                ChatState.lastMessageDate = currentDateKey;
            }

            const from = m.from || '';
            const cls = from === 'admin' ? 'support-chat-message me' :
                        from === 'user' ? 'support-chat-message other' :
                        'support-chat-message system';

            const contentHtml = m.isDeleted
                ? '<em class="text-muted">Tin nhắn đã bị thu hồi</em>'
                : ChatUtils.htmlEncode(m.content || '');

            let actionsHtml = '';
            if (from === 'admin' || from === 'user') {
                actionsHtml = `
                    <div class="support-msg-actions small text-muted">
                        <a href="#" class="text-decoration-none msg-action-reply"><i class="bi bi-reply me-1"></i>Trả lời</a>`;
                if (from === 'admin' && !m.isDeleted) {
                    actionsHtml += `<a href="#" class="text-decoration-none text-danger msg-action-recall"><i class="bi bi-trash me-1"></i>Thu hồi</a>`;
                }
                actionsHtml += '</div>';
            }

            let replyHtml = '';
            if (m.replyTo && m.replyTo.content) {
                var replyFromLabel = m.replyTo.from === 'admin' ? 'Admin' : 
                                     m.replyTo.from === 'user' ? 'Khách hàng' : 'Hệ thống';
                replyHtml = `
                    <div class="support-chat-reply-quote">
                        <div class="support-chat-reply-quote-from">${ChatUtils.htmlEncode(replyFromLabel)}</div>
                        <div class="support-chat-reply-quote-content">${ChatUtils.htmlEncode(m.replyTo.content || '')}</div>
                    </div>`;
            }

            // Bubble content (reaction badge removed by request)
            const bubbleHtml = `
                <span class="bubble d-inline-block px-2 py-1 rounded">
                    ${replyHtml}
                    ${contentHtml}
                </span>`;

            // Bubble luôn ở phía ngoài, menu hành động ở phía trong (theo hướng flex của hàng)
            const html = `
                <div class="support-chat-message ${cls} mb-2 position-relative" data-id="${m.messageId || ''}" data-from="${from}">
                    ${bubbleHtml}
                    ${actionsHtml}
                    <div class="support-msg-meta small mt-1 d-flex justify-content-end align-items-center">
                        <span class="support-msg-seen"></span>
                    </div>
                </div>`;

            this.elements.messages.append(html);
        },

        scrollToBottom: function () {
            const $msgs = this.elements.messages;
            $msgs.scrollTop($msgs.prop('scrollHeight'));
        },

        setReplyMode: function (id, content) {
            ChatState.currentReplyToId = id;
            this.elements.replyPreviewText.text(content);
            this.elements.replyPreview.removeClass('d-none');
            this.elements.input.focus();
        },

        clearReplyMode: function () {
            ChatState.currentReplyToId = null;
            this.elements.replyPreview.addClass('d-none');
            this.elements.replyPreviewText.text('');
        },

        updateReaction: function (messageId, reactionType, summary) {
            // Reaction UI removed; keep no-op to avoid runtime errors from legacy realtime events.
        },

        buildReactionBadgeHtml: function (reactionType) {
            return '';
        },

        buildReactionIndicatorHtml: function () {
            return ''; // dùng badge + chip, không cần indicator riêng
        },

        showReactionPicker: function ($msg) {
            // Reaction UI removed.
        },

        hideReactionPicker: function () {
            // Reaction UI removed.
        },

        markMessageRecalled: function (messageId) {
            const $msg = this.elements.messages.find(`.support-chat-message[data-id="${messageId}"]`);
            if ($msg.length) {
                $msg.find('.bubble').html('<em class="text-muted">Tin nhắn đã bị thu hồi</em>');
                $msg.find('.msg-action-recall').remove();
            }
        },

        renderProfile: function (profile, orders) {
            const $p = this.elements.profilePane;
            const $o = this.elements.ordersPane;

            if (!profile) {
                $p.html('<p class="text-muted small text-center">Không có thông tin.</p>');
                $o.html('<p class="text-muted small text-center">Không có đơn hàng.</p>');
                return;
            }

            // Render Profile
            const avatar = profile.avatarUrl || '/Content/Images/no-avatar.jpg';
            const htmlP = `
                <div class="text-center mb-3">
                    <img src="${avatar}" class="rounded-circle mb-2" style="width:80px;height:80px;object-fit:cover;" />
                    <h5 class="fw-bold mb-0">${ChatUtils.htmlEncode(profile.fullName)}</h5>
                    <div class="text-muted small">${ChatUtils.htmlEncode(profile.rankName || 'Thành viên')}</div>
                </div>
                <ul class="list-group list-group-flush small">
                    <li class="list-group-item px-0 d-flex justify-content-between">
                        <span class="text-muted">Email:</span>
                        <span class="fw-semibold text-break" style="max-width:60%;">${ChatUtils.htmlEncode(profile.email || '---')}</span>
                    </li>
                    <li class="list-group-item px-0 d-flex justify-content-between">
                        <span class="text-muted">SĐT:</span>
                        <span class="fw-semibold">${ChatUtils.htmlEncode(profile.phone || '---')}</span>
                    </li>
                    <li class="list-group-item px-0 d-flex justify-content-between">
                        <span class="text-muted">Điểm tích lũy:</span>
                        <span class="fw-semibold text-primary">${profile.totalPoints || 0}</span>
                    </li>
                </ul>`;
            $p.html(htmlP);

            // Render Orders
            if (!orders || !orders.length) {
                $o.html('<p class="text-muted small text-center">Chưa có đơn hàng nào.</p>');
            } else {
                let htmlO = '';
                orders.forEach(function (ord) {
                    let statusClass = 'text-secondary';
                    if (ord.status === 'Completed') statusClass = 'text-success';
                    else if (ord.status === 'Cancelled') statusClass = 'text-danger';
                    else if (ord.status === 'Pending') statusClass = 'text-warning';

                    htmlO += `
                        <div class="card mb-2 p-2">
                            <div class="d-flex justify-content-between mb-1">
                                <a href="${ChatUtils.htmlEncode(SupportConfig.orderPageUrl)}" target="_blank">${ord.orderCode}</a>
                                <span class="fw-bold text-primary">${ChatUtils.formatMoney(ord.totalAmount)}</span>
                            </div>
                            <div class="d-flex justify-content-between small">
                                <span class="text-muted">${ChatUtils.formatDate(ord.orderDate)}</span>
                                <span class="${statusClass} fw-semibold">${ord.status}</span>
                            </div>
                        </div>`;
                });
                $o.html(htmlO);
            }
        }
    };

    // ==========================================
    // Controller Logic
    // ==========================================
    const ChatController = {
        init: function () {
            ChatState.init();
            ChatUI.init();
            if (SupportConfig.forcePolling) {
                this.configureRealtimeUi(false);
                this.loadConversations();
                this.setPollingEnabled(true);
                return;
            }
            const realtimeConfigured = this.setupSignalR();
            this.configureRealtimeUi(realtimeConfigured);
            this.loadConversations();
            this.setPollingEnabled(!realtimeConfigured);
        },

        configureRealtimeUi: function (realtimeConfigured) {
            ChatUI.elements.input.prop('disabled', false);
            ChatUI.elements.sendBtn.prop('disabled', false);
            ChatUI.elements.typing.addClass('d-none');

            if (!realtimeConfigured) {
                ChatUI.elements.input.attr('placeholder', 'Realtime chua san sang. Dang cap nhat o che do polling.');
            }
        },

        setupSignalR: function () {
            if (!window.signalR || !window.signalR.HubConnectionBuilder) {
                console.warn('[support-chat-admin] ASP.NET Core SignalR client not loaded. Fallback to polling mode.');
                ChatState.isRealtimeReady = false;
                return false;
            }

            const connection = new window.signalR.HubConnectionBuilder()
                .withUrl('/hubs/support-chat')
                .withAutomaticReconnect()
                .build();

            ChatState.hub = connection;

            connection.on('newConversationOrMessage', function (payload) {
                ChatController.loadConversations();
                if (ChatState.currentConvId === (payload && payload.conversationId)) {
                    ChatController.reloadCurrentConversationMessages();
                $.post(SupportRoutes.markAsRead, {
                    conversationId: ChatState.currentConvId,
                    __RequestVerificationToken: getAntiForgeryToken()
                });
                }
            });

            connection.on('receiveMessage', function (payload) {
                const conversationId = payload && payload.conversationId;
                const message = payload && payload.message ? payload.message : payload;
                if (!message || ChatState.currentConvId !== conversationId) {
                    return;
                }

                const normalized = Object.assign({}, message, { conversationId: conversationId });
                ChatUI.appendMessage(normalized);
                ChatUI.scrollToBottom();
            });

            connection.on('userTyping', function (data) {
                if (!data || ChatState.currentConvId !== data.conversationId) {
                    return;
                }

                ChatUI.elements.typing.removeClass('d-none');
                clearTimeout(ChatState.typingTimeout);
                ChatState.typingTimeout = setTimeout(function () {
                    ChatUI.elements.typing.addClass('d-none');
                }, 3000);
            });

            connection.onclose(function () {
                ChatState.isRealtimeReady = false;
                ChatController.setPollingEnabled(true);
            });

            connection.onreconnected(async function () {
                ChatState.isRealtimeReady = true;
                ChatController.setPollingEnabled(false);
                await ChatController.registerConversationGroups();
            });

            connection.start().then(async function () {
                ChatState.isRealtimeReady = true;
                ChatController.setPollingEnabled(false);
                await ChatController.registerConversationGroups();
                console.log('[support-chat-admin] ASP.NET Core SignalR connected');
            }).catch(function (err) {
                console.warn('[support-chat-admin] SignalR start failed, fallback to polling mode.', err);
                ChatState.isRealtimeReady = false;
                ChatController.setPollingEnabled(true);
            });

            return true;
        },

        setPollingEnabled: function (enabled) {
            if (enabled) {
                if (ChatState.pollTimer) {
                    return;
                }

                ChatState.pollTimer = setInterval(function () {
                    if (document.hidden) {
                        return;
                    }

                    ChatController.loadConversations();
                    ChatController.reloadCurrentConversationMessages();
                }, 8000);
                return;
            }

            if (ChatState.pollTimer) {
                clearInterval(ChatState.pollTimer);
                ChatState.pollTimer = null;
            }
        },

        registerConversationGroups: async function () {
            if (!ChatState.hub || !ChatState.isRealtimeReady) {
                return;
            }

            try {
                await ChatState.hub.invoke('JoinSeller');
                if (ChatState.currentConvId) {
                    await ChatState.hub.invoke('JoinConversation', ChatState.currentConvId);
                }
            } catch (err) {
                console.warn('[support-chat-admin] Join groups failed', err);
            }
        },

        reloadCurrentConversationMessages: function () {
            if (!ChatState.currentConvId) {
                return;
            }

            $.getJSON(SupportRoutes.messages, { conversationId: ChatState.currentConvId }, function (res) {
                if (res && res.ok) {
                    ChatUI.renderMessages(res.messages);
                }
            });
        },

        loadConversations: function () {
            $.getJSON(SupportRoutes.conversations, function (res) {
                if (res && res.ok) {
                    ChatState.allConversations = (res.conversations || []).map(function (c) {
                        return {
                            ConversationId: c.ConversationId ?? c.conversationId,
                            UserId: c.UserId ?? c.userId,
                            UserName: c.UserName ?? c.userName,
                            UserPhone: c.UserPhone ?? c.userPhone,
                            GuestId: c.GuestId ?? c.guestId,
                            LastContent: c.LastContent ?? c.lastContent,
                            LastTime: c.LastTime ?? c.lastTime,
                            HasUnread: c.HasUnread ?? c.hasUnread,
                            Status: c.Status ?? c.status
                        };
                    });
                    ChatUI.applyFilterAndRender();
                }
            });
        },

        selectConversation: function (convId) {
            if (ChatState.currentConvId === convId) return;

            const previousConvId = ChatState.currentConvId;
            ChatState.currentConvId = convId;
            ChatUI.clearReplyMode();

            // UI updates
            ChatUI.elements.messages.html('<p class="text-muted text-center">Đang tải...</p>');
            ChatUI.elements.closeBtn.prop('disabled', false);
            
            // Find conv data
            const conv = ChatState.allConversations.find(c => c.ConversationId === convId);
            if (conv) {
                ChatUI.elements.title.text(conv.UserName || (conv.GuestId ? 'Khách vãng lai' : 'Khách hàng'));
                ChatUI.elements.subTitle.text(conv.UserPhone || '#' + conv.ConversationId);
                if (conv.Status === 'Closed') {
                    ChatUI.elements.closeBtn.prop('disabled', true);
                }
            }

            // Load messages
            this.reloadCurrentConversationMessages();

            // Load details
            $.getJSON(SupportRoutes.conversationDetails, { conversationId: convId }, function (res) {
                if (res && res.ok) {
                    ChatUI.renderProfile(res.profile, res.orders);
                }
            });

                $.post(SupportRoutes.markAsRead, {
                    conversationId: convId,
                    __RequestVerificationToken: getAntiForgeryToken()
                });

            // Join realtime group
            if (ChatState.hub && ChatState.isRealtimeReady) {
                if (previousConvId) {
                    ChatState.hub.invoke('LeaveConversation', previousConvId).catch(function () {});
                }
                ChatState.hub.invoke('JoinConversation', convId).catch(function (err) {
                    console.warn('[support-chat-admin] join conversation failed', err);
                });
            }

            // Update list UI
            ChatUI.applyFilterAndRender(); // Re-render to update active state
        },

        sendMessage: function () {
            const msg = ChatUI.elements.input.val().trim();
            if (!msg || !ChatState.currentConvId) return;
            ChatController.sendMessageViaHttp(msg);
        },

        sendMessageViaHttp: function (msg) {
            $.ajax({
                url: SupportRoutes.sendMessage,
                type: 'POST',
                dataType: 'json',
                data: {
                    conversationId: ChatState.currentConvId,
                    content: msg,
                    replyToMessageId: ChatState.currentReplyToId,
                    __RequestVerificationToken: getAntiForgeryToken()
                }
            }).done(function (res) {
                if (!res || !res.ok) {
                    alert((res && res.message) ? res.message : 'Khong the gui tin nhan.');
                    return;
                }

                if (res.message) {
                    ChatUI.appendMessage(res.message);
                    ChatUI.scrollToBottom();
                } else {
                    $.getJSON(SupportRoutes.messages, { conversationId: ChatState.currentConvId }, function (reloadRes) {
                        if (reloadRes && reloadRes.ok) {
                            ChatUI.renderMessages(reloadRes.messages);
                        }
                    });
                }

                ChatUI.clearReplyMode();
                ChatUI.elements.input.val('').focus();
                ChatController.loadConversations();
            }).fail(function () {
                alert('Khong the gui tin nhan. Vui long thu lai.');
            });
        },

        notifyTyping: function () {
            if (ChatState.currentConvId && ChatState.hub && ChatState.isRealtimeReady) {
                ChatState.hub.invoke('NotifyTyping', ChatState.currentConvId).catch(function () {});
            }
        },

        closeConversation: function () {
            if (!ChatState.currentConvId) return;
            $.post(SupportRoutes.close, {
                conversationId: ChatState.currentConvId,
                __RequestVerificationToken: getAntiForgeryToken()
            }, function (res) {
                if (res && res.ok) {
                    ChatController.loadConversations();
                    ChatUI.elements.closeBtn.prop('disabled', true);
                }
            });
        },

        recallMessage: function (messageId) {
            console.warn('[support-chat-admin] Thu hoi tin nhan chua duoc ho tro qua endpoint hien tai.', messageId);
        },

        reactToMessage: function () {}
    };

    // ==========================================
    // Utilities
    // ==========================================
    const ChatUtils = {
        htmlEncode: function (value) {
            return $('<div/>').text(value || '').html();
        },

        formatMoney: function (amount) {
            return new Intl.NumberFormat('vi-VN', { style: 'currency', currency: 'VND' }).format(amount);
        },

        formatDate: function (dateStr) {
            if (!dateStr) return '';
            const date = new Date(dateStr);
            return date.toLocaleDateString('vi-VN') + ' ' + date.toLocaleTimeString('vi-VN', { hour: '2-digit', minute: '2-digit' });
        },

        formatRelativeTime: function (dateStr) {
            if (!dateStr) return '';
            const date = new Date(dateStr);
            const now = new Date();
            const diff = (now - date) / 1000; // seconds

            if (diff < 60) return 'Vừa xong';
            if (diff < 3600) return Math.floor(diff / 60) + ' phút trước';
            if (diff < 86400) return Math.floor(diff / 3600) + ' giờ trước';
            return Math.floor(diff / 86400) + ' ngày trước';
        },

        formatDateSeparator: function (dateStr) {
            if (!dateStr) return '';
            
            var msgDate = new Date(dateStr);
            var today = new Date();
            var yesterday = new Date(today);
            yesterday.setDate(today.getDate() - 1);
            
            // Reset time to compare only dates
            today.setHours(0, 0, 0, 0);
            yesterday.setHours(0, 0, 0, 0);
            msgDate.setHours(0, 0, 0, 0);
            
            if (msgDate.getTime() === today.getTime()) {
                return 'Hôm nay';
            } else if (msgDate.getTime() === yesterday.getTime()) {
                return 'Hôm qua';
            } else {
                var dayNames = ['CN', 'T2', 'T3', 'T4', 'T5', 'T6', 'T7'];
                var dayName = dayNames[msgDate.getDay()];
                var day = ('0' + msgDate.getDate()).slice(-2);
                var month = ('0' + (msgDate.getMonth() + 1)).slice(-2);
                var year = msgDate.getFullYear();
                return dayName + ' ' + day + '/' + month + '/' + year;
            }
        },
        
        getDateKey: function (dateStr) {
            if (!dateStr) return '';
            var d = new Date(dateStr);
            return d.getFullYear() + '-' + (d.getMonth() + 1) + '-' + d.getDate();
        },

        getReactionEmoji: function () { return ''; }
    };

    // Initialize
    $(document).ready(function () {
        ChatController.init();
    });

})(jQuery);
