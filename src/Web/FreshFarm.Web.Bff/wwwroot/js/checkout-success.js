(function () {
    const initCheckoutSuccessCountdown = () => {
        const host = document.querySelector("[data-checkout-success-countdown]");
        if (!(host instanceof HTMLElement)) {
            return;
        }

        const countdownEl = host.querySelector("#countdown");
        const redirectUrl = host.dataset.checkoutSuccessRedirect || "/?from=thankyou";
        let seconds = Number(host.dataset.checkoutSuccessSeconds || "8");
        if (!Number.isFinite(seconds) || seconds <= 0) {
            seconds = 8;
        }

        const timer = window.setInterval(() => {
            seconds -= 1;
            if (countdownEl instanceof HTMLElement) {
                countdownEl.textContent = seconds.toString();
            }

            if (seconds <= 0) {
                window.clearInterval(timer);
                window.location.href = redirectUrl;
            }
        }, 1000);
    };

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", initCheckoutSuccessCountdown);
    } else {
        initCheckoutSuccessCountdown();
    }
})();
