using Xunit;
using System.Runtime.CompilerServices;

namespace FreshFarm.Web.Bff.Tests;

public sealed class ShippingManagementUxContractTests
{
    [Fact]
    public void AdminSidebar_UsesSingleShippingOperationsEntry()
    {
        var sidebar = ReadSource("src", "Web", "FreshFarm.Web.Bff", "Areas", "Admin", "Views", "Shared", "_SideBar.cshtml");

        Assert.Contains("Quản lý vận chuyển", sidebar, StringComparison.Ordinal);
        Assert.DoesNotContain("Điều phối giao hàng", sidebar, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Delivery\"", sidebar, StringComparison.Ordinal);
    }

    [Fact]
    public void SellerShippingSurface_KeepsHandoffButRemovesFinanceAndInternalStaffControls()
    {
        var view = ReadSource("src", "Web", "FreshFarm.Web.Bff", "Areas", "Seller", "Views", "Shipping", "ManageShipping.cshtml");
        var controller = ReadSource("src", "Web", "FreshFarm.Web.Bff", "Areas", "Seller", "Controllers", "ShippingController.cs");

        Assert.Contains("Xác nhận sẵn sàng bàn giao", view, StringComparison.Ordinal);
        Assert.Contains("confirmReadyHandoffModal", view, StringComparison.Ordinal);
        Assert.Contains("btnConfirmReadyHandoff", view, StringComparison.Ordinal);
        Assert.Contains("MarkReadyForPickup", controller, StringComparison.Ordinal);
        Assert.Contains("normalizeText", view, StringComparison.Ordinal);
        Assert.Contains("Địa chỉ giao hàng chưa hợp lệ theo GHN", view, StringComparison.Ordinal);
        Assert.Contains("hệ thống không tự sửa địa chỉ khách", view, StringComparison.Ordinal);
        Assert.DoesNotContain("ghnCreateOrderPanel", view, StringComparison.Ordinal);
        Assert.DoesNotContain("btn-ghn-select", view, StringComparison.Ordinal);
        Assert.DoesNotContain("Chọn đơn để tạo vận đơn GHN", view, StringComparison.Ordinal);
        Assert.DoesNotContain("Tạo vận đơn GHN từ đơn đã chọn", view, StringComparison.Ordinal);
        Assert.DoesNotContain("btnGhnCreateOrder", view, StringComparison.Ordinal);
        Assert.DoesNotContain("btn-edit", view, StringComparison.Ordinal);
        Assert.DoesNotContain("editShippingModal", view, StringComparison.Ordinal);
        Assert.DoesNotContain("btnSaveEdit", view, StringComparison.Ordinal);
        Assert.DoesNotContain("btn-delete", view, StringComparison.Ordinal);
        Assert.DoesNotContain("btn-reconcile", view, StringComparison.Ordinal);
        Assert.DoesNotContain("btn-unreconcile", view, StringComparison.Ordinal);
        Assert.DoesNotContain("DeliveryStaffId", view, StringComparison.Ordinal);
        Assert.DoesNotContain("Nhân viên giao hàng", view, StringComparison.Ordinal);
        Assert.DoesNotContain("NV giao hàng", view, StringComparison.Ordinal);
        Assert.DoesNotContain("staffId", view, StringComparison.Ordinal);
        Assert.DoesNotContain("Nhận tại cửa hàng", view, StringComparison.Ordinal);
        Assert.DoesNotContain("IsStorePickup", view, StringComparison.Ordinal);
        Assert.DoesNotContain("StoreAddress", view, StringComparison.Ordinal);
        Assert.DoesNotContain("public async Task<JsonResult> Delete", controller, StringComparison.Ordinal);
        Assert.DoesNotContain("ReconcileCod", controller, StringComparison.Ordinal);
        Assert.DoesNotContain("UnreconcileCod", controller, StringComparison.Ordinal);
        Assert.DoesNotContain("public async Task<JsonResult> Edit", controller, StringComparison.Ordinal);
        Assert.DoesNotContain("PutAsJsonAsync($\"/api/orders/admin/shippings", controller, StringComparison.Ordinal);
        Assert.DoesNotContain("currentStaffId", controller, StringComparison.Ordinal);
        Assert.DoesNotContain("DeliveryStaffDto", controller, StringComparison.Ordinal);
    }

    [Fact]
    public void SellerShippingSurface_MovesHandoffGuidanceIntoHelpPanel()
    {
        var view = ReadSource("src", "Web", "FreshFarm.Web.Bff", "Areas", "Seller", "Views", "Shipping", "ManageShipping.cshtml");

        Assert.Contains("shippingHelpButton", view, StringComparison.Ordinal);
        Assert.Contains("shippingHelpPanel", view, StringComparison.Ordinal);
        Assert.Contains("shipping-help-label", view, StringComparison.Ordinal);
        Assert.Contains("Hướng dẫn sử dụng", view, StringComparison.Ordinal);
        Assert.Contains("Bản ghi vận chuyển đã được tạo tự động theo đơn hàng", view, StringComparison.Ordinal);
        Assert.Contains("Không xử lý thanh toán, hủy đơn hoặc sửa địa chỉ tại đây", view, StringComparison.Ordinal);
        Assert.Contains("Seller cần làm gì", view, StringComparison.Ordinal);
        Assert.Contains("Bàn giao / GHN", view, StringComparison.Ordinal);
        Assert.Contains("Thời gian dự kiến", view, StringComparison.Ordinal);
        Assert.Contains("GhnExpectedDeliveryTime", view, StringComparison.Ordinal);
        Assert.Contains("GhnLastSyncedAt", view, StringComparison.Ordinal);
        Assert.DoesNotContain("shipping-scope-grid", view, StringComparison.Ordinal);
        Assert.DoesNotContain("shipping-scope-card", view, StringComparison.Ordinal);
        Assert.DoesNotContain("Theo dõi GHN", view, StringComparison.Ordinal);
        Assert.DoesNotContain("GHN public không cung cấp SĐT shipper hoặc GPS realtime", view, StringComparison.Ordinal);
        Assert.DoesNotContain("Chuẩn bị hàng, tạo vận đơn", view, StringComparison.Ordinal);
    }

    [Fact]
    public void SellerShippingSurface_AddsSandboxGhnTrackingDetailModal()
    {
        var view = ReadSource("src", "Web", "FreshFarm.Web.Bff", "Areas", "Seller", "Views", "Shipping", "ManageShipping.cshtml");
        var controller = ReadSource("src", "Web", "FreshFarm.Web.Bff", "Areas", "Seller", "Controllers", "ShippingController.cs");
        var serviceContract = ReadSource("src", "Web", "FreshFarm.Web.Bff", "Services", "IGhnSandboxService.cs");
        var service = ReadSource("src", "Web", "FreshFarm.Web.Bff", "Services", "GhnSandboxService.cs");

        Assert.Contains("btn-ghn-tracking-row", view, StringComparison.Ordinal);
        Assert.Contains("ghnTrackingDetailModal", view, StringComparison.Ordinal);
        Assert.Contains("renderGhnTrackingDetail", view, StringComparison.Ordinal);
        Assert.Contains("tracking-timeline", view, StringComparison.Ordinal);
        Assert.Contains("Kho hiện tại", view, StringComparison.Ordinal);
        Assert.Contains("Khối lượng", view, StringComparison.Ordinal);
        Assert.Contains("Dài / rộng / cao", view, StringComparison.Ordinal);
        Assert.Contains("Log trạng thái GHN", view, StringComparison.Ordinal);
        Assert.DoesNotContain("shipper phone", view, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("GPS", view, StringComparison.Ordinal);

        Assert.Contains("currentWarehouseName", controller, StringComparison.Ordinal);
        Assert.Contains("weight", controller, StringComparison.Ordinal);
        Assert.Contains("length", controller, StringComparison.Ordinal);
        Assert.Contains("width", controller, StringComparison.Ordinal);
        Assert.Contains("height", controller, StringComparison.Ordinal);
        Assert.Contains("warehouseName", controller, StringComparison.Ordinal);

        Assert.Contains("CurrentWarehouseName", serviceContract, StringComparison.Ordinal);
        Assert.Contains("Weight", serviceContract, StringComparison.Ordinal);
        Assert.Contains("Length", serviceContract, StringComparison.Ordinal);
        Assert.Contains("Width", serviceContract, StringComparison.Ordinal);
        Assert.Contains("Height", serviceContract, StringComparison.Ordinal);
        Assert.Contains("WarehouseName", serviceContract, StringComparison.Ordinal);

        Assert.Contains("current_warehouse", service, StringComparison.Ordinal);
        Assert.Contains("current_warehouse_id", service, StringComparison.Ordinal);
        Assert.Contains("weight", service, StringComparison.Ordinal);
        Assert.Contains("length", service, StringComparison.Ordinal);
        Assert.Contains("width", service, StringComparison.Ordinal);
        Assert.Contains("height", service, StringComparison.Ordinal);
    }

    [Fact]
    public void AdminShippingSurface_IsMonitoringAndReconciliation_NotDeliveryStaffDispatch()
    {
        var view = ReadSource("src", "Web", "FreshFarm.Web.Bff", "Areas", "Admin", "Views", "Shipping", "ManageShipping.cshtml");
        var controller = ReadSource("src", "Web", "FreshFarm.Web.Bff", "Areas", "Admin", "Controllers", "ShippingController.cs");

        Assert.Contains("Giám sát vận chuyển", view, StringComparison.Ordinal);
        Assert.Contains("Chờ shipper lấy", view, StringComparison.Ordinal);
        Assert.Contains("ReconcileCod", controller, StringComparison.Ordinal);
        Assert.DoesNotContain("DeliveryStaffId", view, StringComparison.Ordinal);
        Assert.DoesNotContain("Nhân viên giao hàng", view, StringComparison.Ordinal);
        Assert.DoesNotContain("NV giao hàng", view, StringComparison.Ordinal);
        Assert.DoesNotContain("staffId", view, StringComparison.Ordinal);
        Assert.DoesNotContain("Nhận tại cửa hàng", view, StringComparison.Ordinal);
        Assert.DoesNotContain("IsStorePickup", view, StringComparison.Ordinal);
        Assert.DoesNotContain("StoreAddress", view, StringComparison.Ordinal);
        Assert.DoesNotContain("deliveryStaffId", controller, StringComparison.Ordinal);
        Assert.DoesNotContain("currentStaffId", controller, StringComparison.Ordinal);
        Assert.DoesNotContain("DeliveryStaffDto", controller, StringComparison.Ordinal);
        Assert.DoesNotContain("btn-delete", view, StringComparison.Ordinal);
        Assert.DoesNotContain("public async Task<JsonResult> Delete", controller, StringComparison.Ordinal);
    }

    [Fact]
    public void DeliveryDispatchSurface_IsNoLongerExposedAsASeparateBusinessFlow()
    {
        var adminDeliveryController = Path.Combine(
            WorkspaceRoot,
            "src",
            "Web",
            "FreshFarm.Web.Bff",
            "Areas",
            "Admin",
            "Controllers",
            "DeliveryController.cs");
        var sharedDeliveryView = Path.Combine(
            WorkspaceRoot,
            "src",
            "Web",
            "FreshFarm.Web.Bff",
            "Areas",
            "Seller",
            "Views",
            "Delivery",
            "Index.cshtml");

        Assert.False(File.Exists(adminDeliveryController));
        Assert.False(File.Exists(sharedDeliveryView));
    }

    private static string ReadSource(params string[] segments)
        => File.ReadAllText(Path.Combine([WorkspaceRoot, .. segments]));

    private static string WorkspaceRoot { get; } = FindWorkspaceRoot(SourceFilePath());

    private static string SourceFilePath([CallerFilePath] string path = "") => path;

    private static string FindWorkspaceRoot(string startPath)
    {
        var current = new FileInfo(startPath).Directory;
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, "src", "Web", "FreshFarm.Web.Bff", "FreshFarm.Web.Bff.csproj");
            if (File.Exists(candidate))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate FreshFarm workspace root.");
    }
}
