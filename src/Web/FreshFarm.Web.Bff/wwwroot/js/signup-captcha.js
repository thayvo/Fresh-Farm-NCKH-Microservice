document.addEventListener("DOMContentLoaded", function () {
    var captchaImage = document.getElementById("signupCaptchaImage");
    var refreshButton = document.getElementById("refreshCaptchaButton");

    if (!captchaImage || !refreshButton) {
        return;
    }

    refreshButton.addEventListener("click", function () {
        var separator = captchaImage.src.indexOf("?") >= 0 ? "&" : "?";
        captchaImage.src = captchaImage.src.split("?")[0] + separator + "v=" + Date.now();
    });
});
