// Please see documentation at https://learn.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

(function () {
    const toastId = "freshfarmGlobalToast";
    let toastTimer = 0;
    const recommendationImpressionRequests = new Map();

    const getRetryAfterSeconds = (response) => {
        const raw = response?.headers?.get?.("Retry-After");
        const parsed = Number(raw);
        return Number.isFinite(parsed) && parsed > 0 ? parsed : 0;
    };

    const getFriendlyMessage = (status, retryAfterSeconds = 0, payload = null, fallbackMessage = "") => {
        if (status === 429) {
            const waitHint = retryAfterSeconds > 0 ? ` sau ${retryAfterSeconds} giây` : " sau ít giây";
            return payload?.message || `Bạn thao tác quá nhanh. Vui lòng thử lại${waitHint}.`;
        }

        if (status === 401) {
            return payload?.message || "Phiên làm việc đã hết hạn. Vui lòng đăng nhập lại.";
        }

        if (status === 403) {
            return payload?.message || "Bạn không có quyền thực hiện thao tác này.";
        }

        return payload?.message || fallbackMessage || `HTTP ${status}`;
    };

    const tryParsePayload = async (response) => {
        const text = await response.text();
        if (!text) {
            return { payload: null, text: "" };
        }

        try {
            return { payload: JSON.parse(text), text };
        } catch {
            return { payload: null, text };
        }
    };

    const createHttpError = (response, payload = null, fallbackMessage = "") => {
        const error = new Error(
            getFriendlyMessage(response?.status ?? 0, getRetryAfterSeconds(response), payload, fallbackMessage)
        );
        error.status = response?.status ?? 0;
        error.payload = payload;
        error.retryAfterSeconds = getRetryAfterSeconds(response);
        return error;
    };

    const ensureToast = () => {
        let toast = document.getElementById(toastId);
        if (toast instanceof HTMLElement) {
            return toast;
        }

        toast = document.createElement("div");
        toast.id = toastId;
        toast.style.position = "fixed";
        toast.style.right = "20px";
        toast.style.bottom = "20px";
        toast.style.zIndex = "1100";
        toast.style.maxWidth = "340px";
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
        return toast;
    };

    const showToast = (message, tone = "error") => {
        const toast = ensureToast();
        toast.textContent = message;
        toast.style.background = tone === "success" ? "#166534" : "#b91c1c";
        toast.style.opacity = "1";
        toast.style.transform = "translateY(0)";

        window.clearTimeout(toastTimer);
        toastTimer = window.setTimeout(() => {
            toast.style.opacity = "0";
            toast.style.transform = "translateY(12px)";
        }, 3200);
    };

    const toPositiveIntOrNull = (value) => {
        const numeric = Number(value);
        return Number.isInteger(numeric) && numeric > 0 ? numeric : null;
    };

    const normalizeRequiredText = (value) => {
        if (value === null || value === undefined) {
            return "";
        }

        return String(value).trim();
    };

    const createRecommendationRunId = (placement = "recommendation") =>
        `${normalizeRequiredText(placement) || "recommendation"}-${Date.now()}-${Math.random().toString(36).slice(2, 10)}`;

    const postTrackingEvent = async (url, payload, options = {}) => {
        try {
            const response = await fetch(url, {
                method: "POST",
                credentials: "same-origin",
                keepalive: options.keepalive !== false,
                headers: {
                    Accept: "application/json",
                    "Content-Type": "application/json"
                },
                body: JSON.stringify(payload)
            });

            const { payload: responsePayload } = await tryParsePayload(response);
            if (!response.ok) {
                console.warn("Recommendation event tracking failed.", {
                    url,
                    status: response.status,
                    payload: responsePayload
                });
                return null;
            }

            return responsePayload;
        } catch (error) {
            console.warn("Recommendation event tracking errored.", { url, error });
            return null;
        }
    };

    const trackProductView = async (request) => {
        const productId = toPositiveIntOrNull(request?.productId);
        const sourcePage = normalizeRequiredText(request?.sourcePage);
        if (!productId || !sourcePage) {
            return null;
        }

        const responsePayload = await postTrackingEvent("/bff/events/product-view", {
            productId,
            sellerId: toPositiveIntOrNull(request?.sellerId),
            sourcePage,
            sourceModule: normalizeRequiredText(request?.sourceModule) || "unknown"
        });

        return toPositiveIntOrNull(responsePayload?.eventId);
    };

    const trackSearch = async (request) => {
        const keyword = normalizeRequiredText(request?.keyword);
        if (!keyword) {
            return null;
        }

        const responsePayload = await postTrackingEvent("/bff/events/search", {
            keyword,
            filters: request?.filters ?? null,
            resultCount: Math.max(0, Number(request?.resultCount) || 0)
        });

        return toPositiveIntOrNull(responsePayload?.eventId);
    };

    const trackSearchClick = async (request) => {
        const productId = toPositiveIntOrNull(request?.productId);
        if (!productId) {
            return null;
        }

        const responsePayload = await postTrackingEvent("/bff/events/search-click", {
            searchEventId: toPositiveIntOrNull(request?.searchEventId),
            productId,
            sellerId: toPositiveIntOrNull(request?.sellerId),
            rank: Math.max(0, Number(request?.rank) || 0)
        });

        return toPositiveIntOrNull(responsePayload?.eventId);
    };

    const buildRecommendationImpressionKey = (request) => {
        const placement = normalizeRequiredText(request?.placement) || "unknown";
        const runId = normalizeRequiredText(request?.recommendationRunId) || "single-run";
        const productId = toPositiveIntOrNull(request?.productId) || 0;
        const rank = Math.max(0, Number(request?.rank) || 0);
        const algorithm = normalizeRequiredText(request?.algorithm) || "unknown";
        return `${placement}|${runId}|${productId}|${rank}|${algorithm}`;
    };

    const trackRecommendationImpression = async (request) => {
        const productId = toPositiveIntOrNull(request?.productId);
        const placement = normalizeRequiredText(request?.placement);
        const algorithm = normalizeRequiredText(request?.algorithm);
        if (!productId || !placement || !algorithm) {
            return null;
        }

        const dedupeKey = buildRecommendationImpressionKey(request);
        if (recommendationImpressionRequests.has(dedupeKey)) {
            return recommendationImpressionRequests.get(dedupeKey);
        }

        const requestPromise = postTrackingEvent("/bff/events/recommendation-impression", {
            placement,
            recommendationRunId: normalizeRequiredText(request?.recommendationRunId) || null,
            productId,
            rank: Math.max(0, Number(request?.rank) || 0),
            algorithm
        }).then((responsePayload) => toPositiveIntOrNull(responsePayload?.eventId));

        recommendationImpressionRequests.set(dedupeKey, requestPromise);
        return requestPromise;
    };

    const trackRecommendationClick = async (request) => {
        const productId = toPositiveIntOrNull(request?.productId);
        const placement = normalizeRequiredText(request?.placement);
        const algorithm = normalizeRequiredText(request?.algorithm);
        if (!productId || !placement || !algorithm) {
            return null;
        }

        const rank = Math.max(0, Number(request?.position ?? request?.rank) || 0);
        const responsePayload = await postTrackingEvent("/bff/events/recommendation-click", {
            recommendationImpressionEventId: toPositiveIntOrNull(request?.recommendationImpressionEventId),
            productId,
            position: rank || null,
            rank: rank || null,
            placement,
            algorithm
        });

        return toPositiveIntOrNull(responsePayload?.eventId);
    };

    window.FreshFarmApp = {
        createHttpError,
        createRecommendationRunId,
        getFriendlyMessage,
        getRetryAfterSeconds,
        showToast,
        trackProductView,
        trackRecommendationClick,
        trackRecommendationImpression,
        trackSearch,
        trackSearchClick,
        tryParsePayload
    };
})();
