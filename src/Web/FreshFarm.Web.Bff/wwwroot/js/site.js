// Please see documentation at https://learn.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

(function () {
    const toastId = "freshfarmGlobalToast";
    let toastTimer = 0;

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

    window.FreshFarmApp = {
        createHttpError,
        getFriendlyMessage,
        getRetryAfterSeconds,
        showToast,
        tryParsePayload
    };
})();
