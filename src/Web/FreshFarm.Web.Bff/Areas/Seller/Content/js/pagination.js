// Hàm phân trang dùng chung
//function setupPaginationSystem(tableBodyId, entityName, itemsPerPage = 5) {
//    const tableBody = document.getElementById(tableBodyId);
//    const paginationWrapper = document.getElementById("pagination-wrapper");
//    const paginationInfo = document.getElementById("pagination-info");

//    const allRows = Array.from(tableBody.querySelectorAll("tr"));
//    let currentPage = 1;

//    function setupPagination(rows) {
//        paginationWrapper.innerHTML = "";
//        const pageCount = Math.ceil(rows.length / itemsPerPage);
//        if (pageCount <= 1) {
//            paginationInfo.textContent = `Hiển thị ${rows.length} trên ${rows.length} ${entityName}`;
//            return;
//        }

//        // Nút trước
//        const prevLi = document.createElement("li");
//        prevLi.className = `page-item ${currentPage === 1 ? "disabled" : ""}`;
//        prevLi.innerHTML = `<a class="page-link" href="#" data-page="prev">Trước</a>`;
//        paginationWrapper.appendChild(prevLi);

//        // Các nút số trang
//        for (let i = 1; i <= pageCount; i++) {
//            const li = document.createElement("li");
//            li.className = `page-item ${i === currentPage ? "active" : ""}`;
//            li.innerHTML = `<a class="page-link" href="#" data-page="${i}">${i}</a>`;
//            paginationWrapper.appendChild(li);
//        }

//        // Nút sau
//        const nextLi = document.createElement("li");
//        nextLi.className = `page-item ${currentPage === pageCount ? "disabled" : ""}`;
//        nextLi.innerHTML = `<a class="page-link" href="#" data-page="next">Sau</a>`;
//        paginationWrapper.appendChild(nextLi);
//    }

//    function displayPage(rows, page) {
//        const start = (page - 1) * itemsPerPage;
//        const end = start + itemsPerPage;
//        rows.forEach(
//            (row, index) =>
//                (row.style.display = index >= start && index < end ? "" : "none")
//        );
//        paginationInfo.textContent = `Hiển thị ${start + 1}-${Math.min(
//            end,
//            rows.length
//        )} trên tổng số ${rows.length} đơn hàng`;
//    }

//    function updatePaginationAndDisplay() {
//        if ((currentPage - 1) * itemsPerPage >= allRows.length && currentPage > 1)
//            currentPage--;
//        setupPagination(allRows);
//        displayPage(allRows, currentPage);
//    }

//    // Lắng nghe sự kiện click
//    paginationWrapper.addEventListener("click", (e) => {
//        e.preventDefault();
//        const target = e.target;
//        if (target.tagName !== "A") return;
//        const page = target.dataset.page;
//        const pageCount = Math.ceil(allRows.length / itemsPerPage);
//        if (page === "prev" && currentPage > 1) currentPage--;
//        else if (page === "next" && currentPage < pageCount) currentPage++;
//        else if (!isNaN(page)) currentPage = parseInt(page);
//        updatePaginationAndDisplay();
//    });

//    // Khởi động ban đầu
//    updatePaginationAndDisplay();

//}
// Hàm phân trang dùng chung
function setupPaginationSystem(entityName) {
    const itemsPerPage = 3;
    const tableBody = document.getElementById("orders-table-body");
    const paginationWrapper = document.getElementById("pagination-wrapper");
    const paginationInfo = document.getElementById("pagination-info");

    const allRows = Array.from(tableBody.querySelectorAll("tr"));
    let currentPage = 1;

    function setupPagination(rows) {
        paginationWrapper.innerHTML = "";
        const pageCount = Math.ceil(rows.length / itemsPerPage);
        if (pageCount <= 1) {
            paginationInfo.textContent = `Hiển thị ${rows.length} trên ${rows.length} ${entityName}`;
            return;
        }

        // Nút trước
        const prevLi = document.createElement("li");
        prevLi.className = `page-item ${currentPage === 1 ? "disabled" : ""}`;
        prevLi.innerHTML = `<a class="page-link" href="#" data-page="prev">Trước</a>`;
        paginationWrapper.appendChild(prevLi);

        // Các nút số trang
        for (let i = 1; i <= pageCount; i++) {
            const li = document.createElement("li");
            li.className = `page-item ${i === currentPage ? "active" : ""}`;
            li.innerHTML = `<a class="page-link" href="#" data-page="${i}">${i}</a>`;
            paginationWrapper.appendChild(li);
        }

        // Nút sau
        const nextLi = document.createElement("li");
        nextLi.className = `page-item ${currentPage === pageCount ? "disabled" : ""}`;
        nextLi.innerHTML = `<a class="page-link" href="#" data-page="next">Sau</a>`;
        paginationWrapper.appendChild(nextLi);
    }

    function displayPage(rows, page) {
        const start = (page - 1) * itemsPerPage;
        const end = start + itemsPerPage;
        rows.forEach(
            (row, index) =>
                (row.style.display = index >= start && index < end ? "" : "none")
        );
        paginationInfo.textContent = `Hiển thị ${start + 1}-${Math.min(
            end,
            rows.length
        )} trên tổng số ${rows.length} đơn hàng`;
    }

    function updatePaginationAndDisplay() {
        if ((currentPage - 1) * itemsPerPage >= allRows.length && currentPage > 1)
            currentPage--;
        setupPagination(allRows);
        displayPage(allRows, currentPage);
    }

    // Lắng nghe sự kiện click
    paginationWrapper.addEventListener("click", (e) => {
        e.preventDefault();
        const target = e.target;
        if (target.tagName !== "A") return;
        const page = target.dataset.page;
        const pageCount = Math.ceil(allRows.length / itemsPerPage);
        if (page === "prev" && currentPage > 1) currentPage--;
        else if (page === "next" && currentPage < pageCount) currentPage++;
        else if (!isNaN(page)) currentPage = parseInt(page);
        updatePaginationAndDisplay();
    });

    // Khởi động ban đầu
    updatePaginationAndDisplay();

}
