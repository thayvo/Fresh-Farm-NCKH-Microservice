(function () {
    const configNode = document.getElementById("idleSessionConfig");
    const overlay = document.getElementById("idleSessionOverlay");
    const continueButton = document.getElementById("idleSessionContinueButton");
    const logoutButton = document.getElementById("idleSessionLogoutNowButton");
    const countdownNode = document.getElementById("idleSessionCountdown");
    const antiforgeryInput = document.querySelector("#idleSessionAntiForgeryForm input[name='__RequestVerificationToken']");

    if (!configNode || !overlay || !continueButton || !logoutButton || !countdownNode || !antiforgeryInput) {
        return;
    }

    const warningAfterMs = Number.parseInt(configNode.dataset.warningAfterMs || "", 10);
    const autoLogoutAfterMs = Number.parseInt(configNode.dataset.autoLogoutAfterMs || "", 10);
    const refreshUrl = configNode.dataset.refreshUrl || "/api/auth/refresh-session";
    const logoutUrl = configNode.dataset.logoutUrl || "/api/auth/logout";
    const loginUrl = configNode.dataset.loginUrl || "/account/signin";
    const antiforgeryToken = antiforgeryInput.value;

    if (!Number.isFinite(warningAfterMs) || !Number.isFinite(autoLogoutAfterMs) || warningAfterMs <= 0 || autoLogoutAfterMs <= 0) {
        return;
    }

    let warningTimerId = null;
    let logoutTimerId = null;
    let countdownIntervalId = null;
    let warningOpenedAt = null;
    let warningVisible = false;
    let logoutInFlight = false;
    let lastActivityAt = Date.now();

    const activityEvents = ["mousemove", "mousedown", "keydown", "scroll", "touchstart", "pointerdown"];

    function resetTimers() {
        lastActivityAt = Date.now();
        clearTimeout(warningTimerId);
        clearTimeout(logoutTimerId);
        clearInterval(countdownIntervalId);
        warningTimerId = window.setTimeout(showWarning, warningAfterMs);
    }

    function setOverlayVisible(visible) {
        warningVisible = visible;
        overlay.classList.toggle("is-visible", visible);
        overlay.setAttribute("aria-hidden", visible ? "false" : "true");
    }

    function formatRemaining(ms) {
        const totalSeconds = Math.max(0, Math.ceil(ms / 1000));
        const minutes = Math.floor(totalSeconds / 60).toString().padStart(2, "0");
        const seconds = (totalSeconds % 60).toString().padStart(2, "0");
        return `${minutes}:${seconds}`;
    }

    function updateCountdown() {
        if (!warningOpenedAt) {
            countdownNode.textContent = formatRemaining(autoLogoutAfterMs);
            return;
        }

        const elapsed = Date.now() - warningOpenedAt;
        countdownNode.textContent = formatRemaining(autoLogoutAfterMs - elapsed);
    }

    function showWarning() {
        warningOpenedAt = Date.now();
        updateCountdown();
        setOverlayVisible(true);

        clearTimeout(logoutTimerId);
        clearInterval(countdownIntervalId);

        logoutTimerId = window.setTimeout(() => {
            void performLogout();
        }, autoLogoutAfterMs);

        countdownIntervalId = window.setInterval(updateCountdown, 1000);
    }

    async function postWithAntiForgery(url) {
        const body = new URLSearchParams();
        body.set("__RequestVerificationToken", antiforgeryToken);

        const response = await fetch(url, {
            method: "POST",
            credentials: "same-origin",
            headers: {
                "Content-Type": "application/x-www-form-urlencoded; charset=UTF-8",
                "X-Requested-With": "XMLHttpRequest"
            },
            body
        });

        let payload = null;
        try {
            payload = await response.json();
        } catch {
            payload = null;
        }

        return { response, payload };
    }

    async function performLogout() {
        if (logoutInFlight) {
            return;
        }

        logoutInFlight = true;
        clearTimeout(logoutTimerId);
        clearInterval(countdownIntervalId);

        try {
            const { payload } = await postWithAntiForgery(logoutUrl);
            window.location.replace(payload?.loginUrl || loginUrl);
        } catch {
            window.location.replace(loginUrl);
        }
    }

    async function continueSession() {
        continueButton.disabled = true;

        try {
            const { response, payload } = await postWithAntiForgery(refreshUrl);
            if (!response.ok) {
                window.location.replace(payload?.loginUrl || loginUrl);
                return;
            }

            setOverlayVisible(false);
            warningOpenedAt = null;
            continueButton.disabled = false;
            resetTimers();
        } catch {
            window.location.replace(loginUrl);
        }
    }

    function handleActivity() {
        if (warningVisible || logoutInFlight) {
            return;
        }

        const now = Date.now();
        if (now - lastActivityAt < 1000) {
            return;
        }

        resetTimers();
    }

    continueButton.addEventListener("click", () => {
        void continueSession();
    });

    logoutButton.addEventListener("click", () => {
        void performLogout();
    });

    overlay.addEventListener("click", (event) => {
        if (event.target === overlay) {
            event.preventDefault();
        }
    });

    activityEvents.forEach((eventName) => {
        window.addEventListener(eventName, handleActivity, { passive: true });
    });

    document.addEventListener("visibilitychange", () => {
        if (!document.hidden && !warningVisible && !logoutInFlight) {
            resetTimers();
        }
    });

    resetTimers();
})();
