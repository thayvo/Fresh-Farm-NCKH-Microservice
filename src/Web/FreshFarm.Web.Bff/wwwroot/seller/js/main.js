document.addEventListener("DOMContentLoaded", function () {
    // Sidebar
    document
        .getElementById("sidebarCollapse")
        ?.addEventListener("click", () =>
            document.getElementById("sidebar")?.classList.toggle("collapsed")
        );
});