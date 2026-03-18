(function () {
    const closeWelcomePopup = () => {
        const popup = document.getElementById("welcomePopup");
        if (!(popup instanceof HTMLElement)) {
            return;
        }

        popup.classList.remove("show");
        popup.style.display = "none";
        sessionStorage.setItem("hideWelcomePopup", "1");
    };

    const initWelcomePopup = () => {
        const popup = document.getElementById("welcomePopup");
        if (!(popup instanceof HTMLElement)) {
            return;
        }

        document.querySelectorAll("[data-popup-close='welcome']").forEach((button) => {
            if (!(button instanceof HTMLElement) || button.dataset.popupCloseBound === "true") {
                return;
            }

            button.dataset.popupCloseBound = "true";
            button.addEventListener("click", closeWelcomePopup);
        });

        popup.addEventListener("click", function (event) {
            if (event.target === this) {
                closeWelcomePopup();
            }
        });

        try {
            const params = new URLSearchParams(window.location.search);
            const from = params.get("from");
            const ref = document.referrer || "";
            const isHidden = sessionStorage.getItem("hideWelcomePopup") === "1";
            const shouldHide = from === "thankyou"
                || ref.includes("/checkout/success")
                || ref.includes("/Checkout/Success")
                || isHidden;
            const shouldShow = params.get("showCoupon") === "1" && !shouldHide;

            if (shouldShow) {
                popup.classList.add("show");
                popup.style.display = "flex";
            } else {
                popup.classList.remove("show");
                popup.style.display = "none";
            }
        } catch {
            popup.classList.remove("show");
            popup.style.display = "none";
        }
    };

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", initWelcomePopup);
    } else {
        initWelcomePopup();
    }
})();
