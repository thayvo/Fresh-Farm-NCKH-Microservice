(function () {
    function formatRemaining(totalSeconds) {
        var minutes = Math.floor(totalSeconds / 60);
        var seconds = totalSeconds % 60;
        return minutes.toString().padStart(2, "0") + ":" + seconds.toString().padStart(2, "0");
    }

    function initLockoutCountdown() {
        var host = document.querySelector("[data-lockout-countdown='true']");
        if (!(host instanceof HTMLElement)) {
            return;
        }

        var messageNode = host.querySelector("[data-lockout-message]");
        if (!(messageNode instanceof HTMLElement)) {
            return;
        }

        var lockedUntilRaw = host.dataset.lockedUntilUtc;
        if (!lockedUntilRaw) {
            return;
        }

        var lockedUntilMs = Date.parse(lockedUntilRaw);
        if (Number.isNaN(lockedUntilMs)) {
            return;
        }

        function updateCountdown() {
            var remainingSeconds = Math.max(0, Math.ceil((lockedUntilMs - Date.now()) / 1000));
            if (remainingSeconds <= 0) {
                host.classList.remove("alert-danger");
                host.classList.add("alert-success");
                messageNode.textContent = "Bạn có thể thử đăng nhập lại.";
                if (timerId) {
                    window.clearInterval(timerId);
                }
                return;
            }

            messageNode.textContent = "Tài khoản đang bị khóa tạm thời. Vui lòng thử lại sau " + formatRemaining(remainingSeconds) + ".";
        }

        updateCountdown();
        var timerId = window.setInterval(updateCountdown, 1000);
    }

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", initLockoutCountdown);
    } else {
        initLockoutCountdown();
    }
})();
