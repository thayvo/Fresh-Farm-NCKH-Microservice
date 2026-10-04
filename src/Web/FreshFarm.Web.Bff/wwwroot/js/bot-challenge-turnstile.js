(function (global) {
    "use strict";

    function findForm() {
        return document.querySelector("[data-bot-challenge-form]");
    }

    function updatePresentation(form, completed) {
        if (!form) {
            return;
        }

        var widget = form.querySelector(".cf-turnstile");
        var status = form.querySelector("[data-bot-challenge-status]");
        if (widget) {
            widget.hidden = completed;
        }

        if (status) {
            status.hidden = false;
            status.classList.toggle("is-complete", completed);
            status.classList.toggle("is-error", !completed);
            status.textContent = completed
                ? "Đã xác minh bảo mật."
                : "Xác minh đã hết hạn hoặc gặp lỗi. Vui lòng thực hiện lại.";
        }
    }

    global.freshFarmBotChallengeCompleted = function (token) {
        var form = findForm();
        var tokenInput = form ? form.querySelector("[data-bot-challenge-token]") : null;
        if (tokenInput) {
            tokenInput.value = typeof token === "string" ? token : "";
        }

        updatePresentation(form, Boolean(tokenInput && tokenInput.value));
    };

    global.freshFarmBotChallengeReset = function () {
        var form = findForm();
        var tokenInput = form ? form.querySelector("[data-bot-challenge-token]") : null;
        if (tokenInput) {
            tokenInput.value = "";
        }

        updatePresentation(form, false);
    };
})(window);
