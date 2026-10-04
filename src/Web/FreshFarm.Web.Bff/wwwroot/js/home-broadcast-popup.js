(function () {
    const popup = document.getElementById("broadcastHomePopup");
    if (!(popup instanceof HTMLElement)) {
        return;
    }

    const imageWrap = popup.querySelector("[data-broadcast-popup-image-wrap]");
    const image = popup.querySelector("[data-broadcast-popup-image]");
    const textWrap = popup.querySelector("[data-broadcast-popup-text-wrap]");
    const title = popup.querySelector("[data-broadcast-popup-title]");
    const message = popup.querySelector("[data-broadcast-popup-message]");
    const typeLabel = popup.querySelector("[data-broadcast-popup-type]");

    const setHidden = (notificationId) => {
        if (notificationId) {
            sessionStorage.setItem(`freshfarm-home-popup-hidden-${notificationId}`, "1");
        }
    };

    const isHidden = (notificationId) => {
        return notificationId && sessionStorage.getItem(`freshfarm-home-popup-hidden-${notificationId}`) === "1";
    };

    const hide = () => {
        popup.classList.remove("show");
        popup.setAttribute("aria-hidden", "true");
        popup.style.display = "none";
        setHidden(popup.dataset.notificationId);
    };

    const show = () => {
        popup.classList.add("show");
        popup.setAttribute("aria-hidden", "false");
        popup.style.display = "flex";
    };

    const showTextPopup = (payload) => {
        if (imageWrap instanceof HTMLElement) {
            imageWrap.classList.add("d-none");
        }

        if (textWrap instanceof HTMLElement) {
            textWrap.classList.remove("d-none");
        }

        if (title instanceof HTMLElement) {
            title.textContent = payload.title || "Thông báo mới";
        }

        if (message instanceof HTMLElement) {
            message.textContent = payload.message || "";
        }

        if (typeLabel instanceof HTMLElement) {
            typeLabel.textContent = payload.notificationType || "Thông báo mới";
        }

        show();
    };

    const showImagePopup = (payload) => {
        if (!(image instanceof HTMLImageElement) || !(imageWrap instanceof HTMLElement)) {
            showTextPopup(payload);
            return;
        }

        if (textWrap instanceof HTMLElement) {
            textWrap.classList.add("d-none");
        }

        image.alt = payload.title || "Thông báo";
        image.onload = show;
        image.onerror = () => showTextPopup(payload);
        image.src = payload.popupImageUrl;
        imageWrap.classList.remove("d-none");
    };

    const render = (payload) => {
        if (!payload || !payload.notificationId || isHidden(payload.notificationId)) {
            return;
        }

        popup.dataset.notificationId = String(payload.notificationId);
        const popupType = String(payload.popupType || "text").toLowerCase();
        if (popupType === "image" && payload.popupImageUrl) {
            showImagePopup(payload);
            return;
        }

        showTextPopup(payload);
    };

    const init = async () => {
        popup.querySelectorAll("[data-broadcast-popup-close]").forEach((button) => {
            button.addEventListener("click", hide);
        });

        popup.addEventListener("click", (event) => {
            if (event.target === popup) {
                hide();
            }
        });

        try {
            const response = await fetch("/home/notification-popup", {
                method: "GET",
                credentials: "same-origin",
                headers: { "Accept": "application/json" }
            });

            if (response.status === 204 || !response.ok) {
                return;
            }

            render(await response.json());
        } catch {
        }
    };

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", init);
    } else {
        init();
    }
})();
