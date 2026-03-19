(function () {
    function initResendVerificationCountdown() {
        const form = document.getElementById('resend-verification-form');
        const button = document.getElementById('resend-verification-button');
        const hint = document.getElementById('resend-verification-hint');
        if (!form || !button || !hint) {
            return;
        }

        const identifier = (form.dataset.identifier || 'anonymous').toLowerCase();
        const cooldownSeconds = Number(form.dataset.cooldownSeconds || '60');
        const storageKey = `freshfarm-resend-verification:${identifier}`;
        let intervalId = null;

        function stopTimer() {
            if (intervalId !== null) {
                window.clearInterval(intervalId);
                intervalId = null;
            }
        }

        function setReadyState() {
            stopTimer();
            button.disabled = false;
            button.textContent = 'Gửi lại email xác minh';
            hint.textContent = 'Bạn có thể gửi lại email xác minh nếu chưa nhận được thư.';
            window.sessionStorage.removeItem(storageKey);
        }

        function updateCountdown() {
            const rawExpiry = window.sessionStorage.getItem(storageKey);
            const expiresAt = rawExpiry ? Number(rawExpiry) : 0;
            const remainingMs = expiresAt - Date.now();
            if (!expiresAt || Number.isNaN(expiresAt) || remainingMs <= 0) {
                setReadyState();
                return;
            }

            const remainingSeconds = Math.ceil(remainingMs / 1000);
            button.disabled = true;
            button.textContent = `Gửi lại sau ${remainingSeconds}s`;
            hint.textContent = `Để tránh spam, bạn có thể gửi lại email sau ${remainingSeconds} giây.`;
        }

        function startCountdown(expiresAt) {
            window.sessionStorage.setItem(storageKey, String(expiresAt));
            updateCountdown();
            stopTimer();
            intervalId = window.setInterval(updateCountdown, 1000);
        }

        form.addEventListener('submit', function () {
            startCountdown(Date.now() + (cooldownSeconds * 1000));
        });

        updateCountdown();
        if (window.sessionStorage.getItem(storageKey)) {
            intervalId = window.setInterval(updateCountdown, 1000);
        }
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', initResendVerificationCountdown);
    } else {
        initResendVerificationCountdown();
    }
})();
