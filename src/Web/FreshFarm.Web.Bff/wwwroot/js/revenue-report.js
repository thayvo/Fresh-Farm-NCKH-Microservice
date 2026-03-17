(function () {
    const dataElement = document.getElementById("revenue-report-data");
    if (!dataElement) {
        return;
    }

    let reportData;

    try {
        reportData = JSON.parse(dataElement.textContent || "{}");
    } catch (error) {
        console.error("Khong doc duoc du lieu bao cao doanh thu.", error);
        return;
    }

    let currentPage = 1;
    const pageSize = 10;
    let sortColumn = "date";
    let sortDirection = "desc";
    let tableData = [];

    function formatCurrency(value) {
        return new Intl.NumberFormat("vi-VN").format(Number(value) || 0) + "₫";
    }

    function showLoadingState() {
        if (document.getElementById("reportsLoadingOverlay")) {
            return;
        }

        const overlay = document.createElement("div");
        overlay.className = "reports-loading-overlay";
        overlay.id = "reportsLoadingOverlay";
        overlay.innerHTML = '<div class="d-flex flex-column align-items-center gap-3 text-white"><div class="spinner-border" role="status" aria-hidden="true"></div><div class="fw-semibold">Dang tai du lieu bao cao...</div></div>';
        document.body.appendChild(overlay);
    }

    function hideLoadingState() {
        const overlay = document.getElementById("reportsLoadingOverlay");
        if (overlay) {
            overlay.remove();
        }
    }

    function initializeTableData() {
        tableData = (reportData.dailyRevenues || []).map(function (item) {
            return {
                Date: item.date || item.Date || null,
                DateFormatted: item.dateFormatted || item.DateFormatted || "",
                TotalOrders: item.totalOrders || item.TotalOrders || 0,
                TotalProducts: item.totalProducts || item.TotalProducts || 0,
                Revenue: item.revenue || item.Revenue || 0
            };
        });
    }

    function renderEmptyState(tbody) {
        tbody.innerHTML = '<tr><td colspan="4" class="text-center py-5"><div class="d-flex flex-column align-items-center"><i class="bi bi-inbox display-1 text-muted mb-3"></i><h5 class="text-muted">Khong co du lieu</h5><p class="text-muted">Chua co du lieu doanh thu trong khoang thoi gian nay</p></div></td></tr>';
    }

    function renderTable() {
        const tbody = document.getElementById("tableBody");
        if (!tbody) {
            return;
        }

        const startIndex = (currentPage - 1) * pageSize;
        const pageData = tableData.slice(startIndex, startIndex + pageSize);

        if (pageData.length === 0) {
            renderEmptyState(tbody);
            return;
        }

        tbody.innerHTML = pageData.map(function (row) {
            return '<tr><td><div class="d-flex align-items-center"><i class="bi bi-calendar3 text-primary me-2"></i>' + row.DateFormatted + '</div></td><td><span class="badge bg-info rounded-pill">' + row.TotalOrders + '</span></td><td><span class="badge bg-primary rounded-pill">' + row.TotalProducts + '</span></td><td><strong class="text-primary">' + formatCurrency(row.Revenue) + '</strong></td></tr>';
        }).join("");
    }

    function renderPagination() {
        const pagination = document.getElementById("pagination");
        if (!pagination) {
            return;
        }

        const totalPages = Math.ceil(tableData.length / pageSize);
        if (totalPages <= 1) {
            pagination.innerHTML = "";
            return;
        }

        let html = '';
        html += '<button class="page-link"' + (currentPage === 1 ? ' disabled' : '') + ' onclick="goToPage(' + (currentPage - 1) + ')"><i class="bi bi-chevron-left"></i></button>';

        if (totalPages <= 7) {
            for (let i = 1; i <= totalPages; i += 1) {
                html += '<button class="page-link ' + (i === currentPage ? 'active' : '') + '" onclick="goToPage(' + i + ')">' + i + '</button>';
            }
        } else {
            html += '<button class="page-link ' + (currentPage === 1 ? 'active' : '') + '" onclick="goToPage(1)">1</button>';
            if (currentPage > 4) {
                html += '<span class="page-ellipsis">...</span>';
            }

            const startPage = Math.max(2, currentPage - 1);
            const endPage = Math.min(totalPages - 1, currentPage + 1);
            for (let i = startPage; i <= endPage; i += 1) {
                html += '<button class="page-link ' + (i === currentPage ? 'active' : '') + '" onclick="goToPage(' + i + ')">' + i + '</button>';
            }

            if (currentPage < totalPages - 3) {
                html += '<span class="page-ellipsis">...</span>';
            }

            html += '<button class="page-link ' + (currentPage === totalPages ? 'active' : '') + '" onclick="goToPage(' + totalPages + ')">' + totalPages + '</button>';
        }

        html += '<button class="page-link"' + (currentPage === totalPages ? ' disabled' : '') + ' onclick="goToPage(' + (currentPage + 1) + ')"><i class="bi bi-chevron-right"></i></button>';
        pagination.innerHTML = html;
    }

    function sortTable(column) {
        if (sortColumn === column) {
            sortDirection = sortDirection === "asc" ? "desc" : "asc";
        } else {
            sortColumn = column;
            sortDirection = "desc";
        }

        tableData.sort(function (a, b) {
            let valueA;
            let valueB;

            switch (column) {
                case "date":
                    valueA = new Date(a.Date || a.DateFormatted || 0);
                    valueB = new Date(b.Date || b.DateFormatted || 0);
                    break;
                case "orders":
                    valueA = a.TotalOrders;
                    valueB = b.TotalOrders;
                    break;
                case "products":
                    valueA = a.TotalProducts;
                    valueB = b.TotalProducts;
                    break;
                case "revenue":
                    valueA = a.Revenue;
                    valueB = b.Revenue;
                    break;
                default:
                    valueA = 0;
                    valueB = 0;
                    break;
            }

            if (valueA < valueB) {
                return sortDirection === "asc" ? -1 : 1;
            }

            if (valueA > valueB) {
                return sortDirection === "asc" ? 1 : -1;
            }

            return 0;
        });

        currentPage = 1;
        renderTable();
        renderPagination();
    }

    function goToPage(page) {
        const totalPages = Math.ceil(tableData.length / pageSize);
        if (page < 1 || page > totalPages) {
            return;
        }

        currentPage = page;
        renderTable();
        renderPagination();

        const tableCard = document.querySelector(".chart-card");
        if (tableCard) {
            window.scrollTo({ top: tableCard.offsetTop - 100, behavior: "smooth" });
        }
    }

    function exportToCSV() {
        const headers = ["Ngay", "Tong don", "San pham", "Doanh thu"];
        const csvContent = [headers.join(",")].concat(tableData.map(function (row) {
            return [row.DateFormatted, row.TotalOrders, row.TotalProducts, row.Revenue].join(",");
        })).join("\n");

        const blob = new Blob([csvContent], { type: "text/csv;charset=utf-8;" });
        const link = document.createElement("a");
        const url = URL.createObjectURL(blob);
        link.setAttribute("href", url);
        link.setAttribute("download", "BaoCaoDoanhThu_" + new Date().toISOString().split("T")[0] + ".csv");
        link.style.visibility = "hidden";
        document.body.appendChild(link);
        link.click();
        document.body.removeChild(link);
    }

    function exportToPDF() {
        const fromDateInput = document.getElementById("fromDate");
        const toDateInput = document.getElementById("toDate");
        const fromDateVal = fromDateInput ? fromDateInput.value : "";
        const toDateVal = toDateInput ? toDateInput.value : "";
        const printWindow = window.open("", "_blank");

        if (!printWindow) {
            return;
        }

        const tableRows = tableData.map(function (row) {
            return '<tr><td>' + row.DateFormatted + '</td><td>' + row.TotalOrders + '</td><td>' + row.TotalProducts + '</td><td>' + formatCurrency(row.Revenue) + '</td></tr>';
        }).join("");

        const printContent = '<html><head><title>Bao Cao Doanh Thu</title><style>body{font-family:Arial,sans-serif;margin:20px;}table{width:100%;border-collapse:collapse;margin-top:20px;}th,td{border:1px solid #ddd;padding:8px;text-align:left;}th{background-color:#f2f2f2;}.header{text-align:center;margin-bottom:30px;}.kpi-summary{display:flex;justify-content:space-around;margin-bottom:30px;flex-wrap:wrap;gap:1rem;}.kpi-item{text-align:center;min-width:150px;}</style></head><body>' +
            '<div class="header"><h1>Bao Cao Doanh Thu</h1><p>Tu ngay: ' + (fromDateVal || '-') + ' den ' + (toDateVal || '-') + '</p></div>' +
            '<div class="kpi-summary">' +
            '<div class="kpi-item"><h3>' + formatCurrency(reportData.totalRevenue) + '</h3><p>Tong doanh thu</p></div>' +
            '<div class="kpi-item"><h3>' + (reportData.totalOrders || 0) + '</h3><p>Tong don hang</p></div>' +
            '<div class="kpi-item"><h3>' + formatCurrency(reportData.averageOrderValue) + '</h3><p>Gia tri don trung binh</p></div>' +
            '</div>' +
            '<table><thead><tr><th>Ngay</th><th>Tong don</th><th>San pham</th><th>Doanh thu</th></tr></thead><tbody>' + tableRows + '</tbody></table></body></html>';

        printWindow.document.write(printContent);
        printWindow.document.close();
        printWindow.print();
    }

    function initializeCharts() {
        if (typeof Chart === "undefined") {
            return;
        }

        const trendLabels = reportData.trendLabels || [];
        const trendData = reportData.trendData || [];
        const topProductNames = reportData.topProductNames || [];
        const topProductRevenues = reportData.topProductRevenues || [];

        const revenueTrendCtx = document.getElementById("revenueTrendChart");
        if (revenueTrendCtx) {
            new Chart(revenueTrendCtx, {
                type: "line",
                data: {
                    labels: trendLabels,
                    datasets: [{
                        label: "Doanh thu (trieu d)",
                        data: trendData,
                        borderColor: "#3b82f6",
                        backgroundColor: "rgba(59, 130, 246, 0.12)",
                        fill: true,
                        tension: 0.4,
                        pointBackgroundColor: "#1e40af",
                        pointBorderColor: "#fff",
                        pointBorderWidth: 2,
                        pointRadius: 5,
                        pointHoverRadius: 7
                    }]
                },
                options: {
                    responsive: true,
                    maintainAspectRatio: false,
                    plugins: {
                        legend: {
                            display: true,
                            position: "top"
                        }
                    },
                    scales: {
                        x: { grid: { display: false } },
                        y: { beginAtZero: true, grid: { color: "rgba(148, 163, 184, 0.3)" } }
                    }
                }
            });
        }

        const topProductsCtx = document.getElementById("topProductsChart");
        if (topProductsCtx) {
            new Chart(topProductsCtx, {
                type: "bar",
                data: {
                    labels: topProductNames,
                    datasets: [{
                        label: "Doanh thu (trieu d)",
                        data: topProductRevenues,
                        backgroundColor: [
                            "rgba(59, 130, 246, 0.9)",
                            "rgba(37, 99, 235, 0.9)",
                            "rgba(96, 165, 250, 0.9)",
                            "rgba(129, 140, 248, 0.9)",
                            "rgba(147, 197, 253, 0.9)"
                        ],
                        borderRadius: 8,
                        borderSkipped: false
                    }]
                },
                options: {
                    indexAxis: "y",
                    responsive: true,
                    maintainAspectRatio: false,
                    plugins: {
                        legend: { display: false }
                    },
                    scales: {
                        x: { beginAtZero: true, grid: { color: "rgba(148, 163, 184, 0.3)" } },
                        y: { grid: { display: false } }
                    }
                }
            });
        }
    }

    function setupEventListeners() {
        const filterForm = document.getElementById("filterForm");
        if (filterForm) {
            filterForm.addEventListener("submit", showLoadingState);
        }
    }

    window.goToPage = goToPage;
    window.sortTable = sortTable;
    window.exportData = function (format) {
        if (format === "csv") {
            exportToCSV();
            return;
        }

        if (format === "pdf") {
            exportToPDF();
        }
    };
    window.exportToPDF = exportToPDF;

    document.addEventListener("DOMContentLoaded", function () {
        initializeTableData();
        setupEventListeners();
        renderTable();
        renderPagination();
        initializeCharts();
        hideLoadingState();
    });
}());
