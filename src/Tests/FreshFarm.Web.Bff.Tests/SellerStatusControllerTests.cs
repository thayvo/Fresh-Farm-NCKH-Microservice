using System.Text.Json;
using FreshFarm.Web.Bff.Areas.Seller.Controllers;
using FreshFarm.Web.Bff.Areas.Seller.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Xunit;

namespace FreshFarm.Web.Bff.Tests;

public sealed class SellerStatusControllerTests
{
    [Fact]
    public void Status_RedirectsSellerToDashboard_WithDisabledMessage()
    {
        var controller = CreateController();

        var result = controller.Status();

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Dashboard", redirect.ActionName);
        Assert.Equal("Home", redirect.ControllerName);
        Assert.Equal("Seller", redirect.RouteValues?["area"]);
        Assert.Equal("Tinh nang quan ly trang thai he thong da bi khoa cho Seller.", controller.TempData["ErrorMessage"]);
    }

    [Fact]
    public void CreateStatusType_ReturnsForbiddenJsonPayload()
    {
        var controller = CreateController();

        var result = controller.CreateStatusType(new SellerStatusTypeViewModel
        {
            StatusTypeName = "Demo"
        });

        Assert.Equal(StatusCodes.Status403Forbidden, result.StatusCode);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(result.Value));
        Assert.False(json.RootElement.GetProperty("success").GetBoolean());
        Assert.Contains("da bi khoa cho Seller", json.RootElement.GetProperty("message").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    private static StatusController CreateController()
    {
        var httpContext = new DefaultHttpContext();
        var controller = new StatusController
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = httpContext
            },
            TempData = new TempDataDictionary(httpContext, new TestTempDataProvider())
        };

        return controller;
    }

    private sealed class TestTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>(StringComparer.Ordinal);

        public void SaveTempData(HttpContext context, IDictionary<string, object> values)
        {
        }
    }
}
