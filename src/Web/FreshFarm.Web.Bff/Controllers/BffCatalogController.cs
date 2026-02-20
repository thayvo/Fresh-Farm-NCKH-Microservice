using Microsoft.AspNetCore.Mvc;
using System.Net.Http.Headers;

namespace FreshFarm.Web.Bff.Controllers;

[ApiController]
[Route("bff")]
public sealed class BffCatalogController : ControllerBase
{
    private const string AccessTokenSessionKey = "ACCESS_TOKEN";
    private readonly IHttpClientFactory _httpClientFactory;

    public BffCatalogController(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    private void AttachAccessToken(HttpClient client)
    {
        var authHeader = Request.Headers.Authorization.ToString();
        if (!string.IsNullOrWhiteSpace(authHeader))
        {
            client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", authHeader);
            return;
        }

        var token = HttpContext.Session.GetString(AccessTokenSessionKey);
        if (!string.IsNullOrWhiteSpace(token))
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
    }

    [HttpGet("products")]
    public async Task<IActionResult> GetProducts([FromQuery] string? name)
    {
        var client = _httpClientFactory.CreateClient("Catalog");
        AttachAccessToken(client);

        var url = string.IsNullOrWhiteSpace(name)
            ? "/api/products"
            : $"/api/products?name={Uri.EscapeDataString(name)}";

        var resp = await client.GetAsync(url);
        var content = await resp.Content.ReadAsStringAsync();

        return new ContentResult
        {
            StatusCode = (int)resp.StatusCode,
            Content = content,
            ContentType = "application/json"
        };
    }

    [HttpPost("products")]
    public async Task<IActionResult> CreateProduct([FromBody] object request)
    {
        var client = _httpClientFactory.CreateClient("Catalog");
        AttachAccessToken(client);

        var resp = await client.PostAsJsonAsync("/api/products", request);
        var body = await resp.Content.ReadAsStringAsync();

        return new ContentResult
        {
            StatusCode = (int)resp.StatusCode,
            Content = body,
            ContentType = "application/json"
        };
    }
}
