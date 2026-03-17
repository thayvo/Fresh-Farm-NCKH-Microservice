using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FreshFarm.Web.Bff.Areas.Admin.Models;
using FreshFarm.Web.Bff.Areas.Seller.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FreshFarm.Web.Bff.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Policy = "AdminOnly")]
public sealed class HomeController : LegacySellerControllerBase
{
    private const string AccessTokenSessionKey = "ACCESS_TOKEN";

    private readonly IHttpClientFactory _httpClientFactory;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public HomeController(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    [HttpGet]
    public async Task<IActionResult> Dashboard()
    {
        var model = new AdminDashboardViewModel();
        var errors = new List<string>();
        var dashboardUrl = Url.Action("Dashboard", "Home", new { area = "Admin" }) ?? "/Admin/Home/Dashboard";
        var accessToken = GetAccessToken(AccessTokenSessionKey);

        if (string.IsNullOrWhiteSpace(accessToken))
        {
            await ForceReLoginAsync();
            return RedirectToAction("Login", "AdminAccount", new
            {
                area = "Admin",
                returnUrl = dashboardUrl
            });
        }

        var orderingTask = GetApiAsync<PlatformDashboardApiDto>(
            clientName: "Ordering",
            path: "/api/orders/admin/platform/dashboard",
            accessToken: accessToken,
            fallbackError: "Khong the tai KPI giao dich toan san.");

        var identityTask = GetApiAsync<AdminUsersMetricsApiDto>(
            clientName: "Identity",
            path: "/auth/admin/users/metrics",
            accessToken: accessToken,
            fallbackError: "Khong the tai KPI nguoi dung.");

        await Task.WhenAll(orderingTask, identityTask);

        var (orderingPayload, orderingError, orderingAuthFailed) = orderingTask.Result;
        var (identityPayload, identityError, identityAuthFailed) = identityTask.Result;

        if (orderingAuthFailed || identityAuthFailed)
        {
            await ForceReLoginAsync();
            return RedirectToAction("Login", "AdminAccount", new
            {
                area = "Admin",
                returnUrl = dashboardUrl
            });
        }

        if (!string.IsNullOrWhiteSpace(orderingError))
        {
            errors.Add(orderingError);
        }

        if (!string.IsNullOrWhiteSpace(identityError))
        {
            errors.Add(identityError);
        }

        if (orderingPayload is not null)
        {
            model.Gmv = orderingPayload.Gmv;
            model.PlatformRevenue = orderingPayload.PlatformRevenue;
            model.TotalOrders = orderingPayload.TotalOrders;
            model.NewOrdersToday = orderingPayload.NewOrdersToday;
            model.CancelledOrders = orderingPayload.CancelledOrders;
            model.CancelRate = orderingPayload.CancelRate;
            model.RealtimeTransactions = orderingPayload.RealtimeTransactions;
            model.TrendLabels = orderingPayload.TrendLabels ?? new List<string>();
            model.TrendOrders = orderingPayload.TrendOrders ?? new List<int>();
            model.TrendGmv = orderingPayload.TrendGmv ?? new List<decimal>();
        }

        if (identityPayload is not null)
        {
            model.TotalUsers = identityPayload.TotalUsers;
            model.NewUsersToday = identityPayload.NewUsersToday;
            model.NewUsers7Days = identityPayload.NewUsers7Days;
            model.TotalSellers = identityPayload.TotalSellers;
            model.NewSellers7Days = identityPayload.NewSellers7Days;
            model.TotalBuyers = identityPayload.TotalBuyers;
            model.NewBuyers7Days = identityPayload.NewBuyers7Days;
            model.TrafficToday = identityPayload.TrafficToday;
            model.ActiveSessions = identityPayload.ActiveSessions;
        }

        EnsureTrendFallback(model);

        if (errors.Count > 0)
        {
            ViewBag.ErrorMessage = string.Join(" | ", errors.Distinct());
        }

        return View(model);
    }

    private async Task<(T? Payload, string? Error, bool AuthFailed)> GetApiAsync<T>(
        string clientName,
        string path,
        string accessToken,
        string fallbackError)
        where T : class
    {
        try
        {
            var client = CreateAuthorizedClient(clientName, accessToken);
            var response = await client.GetAsync(path);
            if (!response.IsSuccessStatusCode)
            {
                var authFailed =
                    response.StatusCode == System.Net.HttpStatusCode.Unauthorized
                    || response.StatusCode == System.Net.HttpStatusCode.Forbidden;

                return (null, await ReadApiErrorAsync(response, fallbackError), authFailed);
            }

            var payload = await response.Content.ReadFromJsonAsync<T>(JsonOptions);
            if (payload is null)
            {
                return (null, "Du lieu dashboard tra ve khong hop le.", false);
            }

            return (payload, null, false);
        }
        catch (Exception ex)
        {
            return (null, fallbackError + " " + ex.Message, false);
        }
    }

    private HttpClient CreateAuthorizedClient(string clientName, string accessToken)
    {
        var client = _httpClientFactory.CreateClient(clientName);
        client.DefaultRequestHeaders.Remove("Authorization");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        return client;
    }

    private static void EnsureTrendFallback(AdminDashboardViewModel model)
    {
        if (model.TrendLabels.Count == 7 && model.TrendOrders.Count == 7 && model.TrendGmv.Count == 7)
        {
            return;
        }

        model.TrendLabels = new List<string>();
        model.TrendOrders = new List<int>();
        model.TrendGmv = new List<decimal>();

        var start = DateTime.UtcNow.Date.AddDays(-6);
        for (var i = 0; i < 7; i++)
        {
            var day = start.AddDays(i);
            model.TrendLabels.Add(day.ToString("dd/MM"));
            model.TrendOrders.Add(0);
            model.TrendGmv.Add(0m);
        }
    }

    private static async Task<string> ReadApiErrorAsync(HttpResponseMessage response, string fallback)
    {
        var statusText = $"(HTTP {(int)response.StatusCode})";

        try
        {
            var json = await response.Content.ReadAsStringAsync();
            if (string.IsNullOrWhiteSpace(json))
            {
                return $"{fallback} {statusText}";
            }

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.ValueKind == JsonValueKind.Object)
            {
                if (root.TryGetProperty("message", out var messageProp) && messageProp.ValueKind == JsonValueKind.String)
                {
                    return $"{messageProp.GetString() ?? fallback} {statusText}";
                }

                if (root.TryGetProperty("detail", out var detailProp) && detailProp.ValueKind == JsonValueKind.String)
                {
                    return $"{detailProp.GetString() ?? fallback} {statusText}";
                }

                if (root.TryGetProperty("error", out var errorProp) && errorProp.ValueKind == JsonValueKind.String)
                {
                    return $"{errorProp.GetString() ?? fallback} {statusText}";
                }
            }

            return $"{fallback} {statusText}";
        }
        catch
        {
            return $"{fallback} {statusText}";
        }
    }

    private async Task ForceReLoginAsync()
    {
        HttpContext.Session.Remove(AccessTokenSessionKey);
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    }
}
