(function ($) {
    'use strict';

    // ==========================================
    // Chat Data & State Management
    // ==========================================
    const ChatState = {
        currentConvId: null,
        currentReplyToId: null,
        filterMode: 'all',
        allConversations: [],
        hub: null,
        lastMessageDate: null, // Track last message date for separator

        init: function () {
            this.hub = ($.connection && $.connection.supportChatHub) ? $.connection.supportChatHub : null;
        }
    };

    const SUPPORT_REACTIONS = [
        { type: 'Heart', emoji: '❤️' },
        { type: 'Like', emoji: '👍' },
        { type: 'Laugh', emoji: '😂' },
        { type: 'Wow', emoji: '😮' },
        { type: 'Sad', emoji: '😢' },
        { type: 'Angry', emoji: '😡' }
    ];

    const SupportRoutes = $.extend({
        conversations: '/Seller/SupportChat/Conversations',
        messages: '/Seller/SupportChat/Messages',
        conversationDetails: '/Seller/SupportChat/ConversationDetails',
        close: '/Seller/SupportChat/Close',
        markAsRead: '/Seller/SupportChat/MarkAsRead'
    }, window.supportChatAdminRoutes || {});

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

            this.elements.messages.on('click', '.msg-action-react', function (e) {
                e.preventDefault();
                const $msg = $(this).closest('.support-chat-message');
                ChatUI.showReactionPicker($msg);
            });

            // Reaction option click
            this.elements.messages.on('click', '.support-msg-reaction-option', function (e) {
                e.preventDefault();
                e.stopPropagation();
                const $btn = $(this);
                const type = $btn.data('type');
                const $msg = $btn.closest('.support-chat-message');
                const id = $msg.data('id');
                if (!id) return;
                const current = ($msg.data('reaction') || '').toString();
                const newType = current === type ? '' : type;
                ChatController.reactToMessage(id, newType);
                ChatUI.hideReactionPicker();
            });

            // Cancel reply
            this.elements.replyCancel.on('click', function () {
                ChatUI.clearReplyMode();
            });

            // Hide picker on outside click
            $(document).on('click.supportChatAdminReaction', function () {
                ChatUI.hideReactionPicker();
            });
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
                        <a href="#" class="text-decoration-none msg-action-reply"><i class="bi bi-reply me-1"></i>Trả lời</a>
                        <a href="#" class="text-decoration-none msg-action-react">❤️</a>`;
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

            // Bubble + reaction badge
            const bubbleHtml = `
                <span class="bubble d-inline-block px-2 py-1 rounded">
                    ${replyHtml}
                    ${contentHtml}
                    ${ChatUI.buildReactionBadgeHtml(m.reactionType)}
                </span>`;

            // Bubble luôn ở phía ngoài, menu hành động ở phía trong (theo hướng flex của hàng)
            const html = `
                <div class="support-chat-message ${cls} mb-2 position-relative" data-id="${m.messageId || ''}" data-from="${from}" data-reaction="${m.reactionType || ''}">
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
            const $msg = this.elements.messages.find(`.support-chat-message[data-id="${messageId}"]`);
            if ($msg.length) {
                $msg.attr('data-reaction', reactionType || '');
                const badgeHtml = ChatUI.buildReactionBadgeHtml(reactionType);
                const $badge = $msg.find('.bubble .support-msg-reaction-badge');
                if ($badge.length) {
                    $badge.replaceWith(badgeHtml);
                } else {
                    $msg.find('.bubble').append(badgeHtml);
                }
            }
        },

        buildReactionBadgeHtml: function (reactionType) {
            const emoji = ChatUtils.getReactionEmoji(reactionType);
            const cur = reactionType || '';
            return `<button type="button" class="support-msg-reaction-badge" data-current-type="${cur}">${emoji}</button>`;
        },

        buildReactionIndicatorHtml: function () {
            return ''; // dùng badge + chip, không cần indicator riêng
        },

        showReactionPicker: function ($msg) {
            this.hideReactionPicker();
            if (!$msg || !$msg.length) return;
            const current = $msg.data('reaction') || '';
            let html = '<div class="support-msg-reaction-picker">';
            SUPPORT_REACTIONS.forEach(function (r) {
                const active = r.type === current ? ' active' : '';
                html += `<button type="button" class="support-msg-reaction-option${active}" data-type="${r.type}">${r.emoji}</button>`;
            });
            html += '</div>';
            $msg.append(html);
        },

        hideReactionPicker: function () {
            this.elements.messages.find('.support-msg-reaction-picker').remove();
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
                                <a href="/Seller/Order/ManageOrders" target="_blank">${ord.orderCode}</a>
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
            const realtimeEnabled = this.setupSignalR();
            this.configureRealtimeUi(realtimeEnabled);
            this.loadConversations();
        },

        configureRealtimeUi: function (realtimeEnabled) {
            if (realtimeEnabled) return;

            ChatUI.elements.input.prop('disabled', true);
            ChatUI.elements.sendBtn.prop('disabled', true);
            ChatUI.elements.input.attr('placeholder', 'Realtime chat chua duoc bat. Dang o che do xem/quan ly.');
            ChatUI.elements.typing.addClass('d-none');
        },

        setupSignalR: function () {
            if (!$.connection || !$.connection.hub) {
                console.warn('[support-chat-admin] SignalR legacy not loaded. Fallback to polling mode.');
                return false;
            }

            const hub = ChatState.hub;
            if (!hub) {
                console.warn('[support-chat-admin] supportChatHub unavailable. Fallback to polling mode.');
                return false;
            }

            // Client methods
            hub.client.newConversationOrMessage = function (payload) {
                // Reload list or update specific item
                ChatController.loadConversations(); // Simple approach: reload all
                // Nếu đang mở đúng hội thoại thì KHÔNG append ở đây để tránh trùng (receiveMessage sẽ lo)
                if (ChatState.currentConvId === payload.conversationId) {
                    if (hub.server && hub.server.markAsRead) {
                        hub.server.markAsRead(ChatState.currentConvId);
                    }
                    return;
                }
            };

            hub.client.receiveMessage = function (payload) {
                if (ChatState.currentConvId === payload.conversationId) {
                    ChatUI.appendMessage(payload);
                    ChatUI.scrollToBottom();
                }
            };

            hub.client.userTyping = function (data) {
                if (ChatState.currentConvId === data.conversationId) {
                    ChatUI.elements.typing.removeClass('d-none');
                    clearTimeout(ChatState.typingTimeout);
                    ChatState.typingTimeout = setTimeout(function () {
                        ChatUI.elements.typing.addClass('d-none');
                    }, 3000);
                }
            };

            hub.client.updateReaction = function (data) {
                if (ChatState.currentConvId === data.conversationId) { // Note: data might not have convId if we didn't fix Hub. 
                    // But we fixed Hub to broadcast to group. So we receive it if we are in group.
                    // Actually, client side doesn't need to check convId if we only receive events for joined groups.
                    // But for safety/clarity:
                    ChatUI.updateReaction(data.messageId, data.reactionType, data.summary);
                }
            };

            hub.client.messageRecalled = function (data) {
                 ChatUI.markMessageRecalled(data.messageId);
            };

            hub.client.userSeen = function (data) {
                if (ChatState.currentConvId === data.conversationId && data.by === 'user') {
                    // Show "Seen" indicator (simplified)
                    $('.support-msg-seen').text(''); // Clear old
                    $('.support-chat-message.me:last .support-msg-seen').text('Đã xem');
                }
            };

            // Start connection
            $.connection.hub.start().done(function () {
                console.log('SignalR Connected');
                hub.server.joinAdmin();
            }).fail(function (err) {
                console.warn('[support-chat-admin] SignalR start failed, fallback to polling mode.', err);
                ChatUI.elements.input.prop('disabled', true);
                ChatUI.elements.sendBtn.prop('disabled', true);
                ChatUI.elements.input.attr('placeholder', 'Realtime chat chua duoc bat. Dang o che do xem/quan ly.');
            });

            return true;
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
            $.getJSON(SupportRoutes.messages, { conversationId: convId }, function (res) {
                if (res && res.ok) {
                    ChatUI.renderMessages(res.messages);
                }
            });

            // Load details
            $.getJSON(SupportRoutes.conversationDetails, { conversationId: convId }, function (res) {
                if (res && res.ok) {
                    ChatUI.renderProfile(res.profile, res.orders);
                }
            });

            $.post(SupportRoutes.markAsRead, { conversationId: convId });

            // Join group
            if (ChatState.hub && ChatState.hub.server) {
                ChatState.hub.server.joinConversation(convId);
                ChatState.hub.server.markAsRead(convId);
            }

            // Update list UI
            ChatUI.applyFilterAndRender(); // Re-render to update active state
        },

        sendMessage: function () {
            const msg = ChatUI.elements.input.val().trim();
            if (!msg || !ChatState.currentConvId) return;

            if (!(ChatState.hub && ChatState.hub.server)) {
                alert('Realtime chat chua duoc bat. Khong the gui tin nhan trong che do hien tai.');
                return;
            }

            console.log('[support-chat-admin] sendMessage', {
                convId: ChatState.currentConvId,
                state: $.connection.hub.state,
                hasHub: !!ChatState.hub,
                len: msg.length
            });

            if (ChatState.hub && ChatState.hub.server) {
                // Guard: ensure connection is alive to tránh nuốt lỗi im lặng
                if ($.connection.hub.state !== $.signalR.connectionState.connected) {
                    console.warn('[support-chat-admin] SignalR chưa sẵn sàng, state:', $.connection.hub.state);
                    return;
                }

                let promise;
                if (ChatState.currentReplyToId) {
                    promise = ChatState.hub.server.replyMessage(ChatState.currentConvId, msg, ChatState.currentReplyToId);
                    ChatUI.clearReplyMode();
                } else {
                    promise = ChatState.hub.server.sendAdminMessage(ChatState.currentConvId, msg);
                }

                // Log lỗi nếu có (nếu server trả về fail)
                if (promise && promise.fail) {
                    promise.fail(function (err) {
                        console.error('[support-chat-admin] Gửi tin thất bại', err);
                    });
                }
            }

            ChatUI.elements.input.val('').focus();
        },

        notifyTyping: function () {
            if (ChatState.currentConvId && ChatState.hub && ChatState.hub.server) {
                ChatState.hub.server.adminTyping(ChatState.currentConvId);
            }
        },

        closeConversation: function () {
            if (!ChatState.currentConvId) return;
            $.post(SupportRoutes.close, { conversationId: ChatState.currentConvId }, function (res) {
                if (res && res.ok) {
                    ChatController.loadConversations();
                    ChatUI.elements.closeBtn.prop('disabled', true);
                }
            });
        },

        recallMessage: function (messageId) {
            if (ChatState.hub && ChatState.hub.server) {
                ChatState.hub.server.recallMessage(messageId);
            }
        },

        reactToMessage: function (messageId, type) {
            if (ChatState.hub && ChatState.hub.server) {
                ChatState.hub.server.reactToMessage(messageId, type);
            }
        }
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

        getReactionEmoji: function (type) {
            switch (type) {
                case 'Heart': return '❤️';
                case 'Like': return '👍';
                case 'Laugh': return '😂';
                case 'Wow': return '😮';
                case 'Sad': return '😢';
                case 'Angry': return '😡';
                default: return '🙂';
            }
        }
    };

    // Initialize
    $(document).ready(function () {
        ChatController.init();
    });

})(jQuery);
