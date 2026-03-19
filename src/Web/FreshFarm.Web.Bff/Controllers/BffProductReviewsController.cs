using System.Net.Http.Headers;
using FreshFarm.Web.Bff.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FreshFarm.Web.Bff.Controllers;

[ApiController]
[Route("bff/reviews")]
public sealed class BffProductReviewsController : Controller
{
    private const string AccessTokenSessionKey = "ACCESS_TOKEN";

    private readonly IHttpClientFactory _httpClientFactory;

    public BffProductReviewsController(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    [HttpGet("products/{productId:int}")]
    [AllowAnonymous]
    [EnableRateLimiting("public-read")]
    public async Task<IActionResult> GetByProduct([FromRoute] int productId)
    {
        var client = _httpClientFactory.CreateClient("Ordering");
        AttachAccessToken(client);

        var response = await client.GetAsync($"/api/orders/product-reviews/products/{productId}");
        var content = await response.Content.ReadAsStringAsync();

        return new ContentResult
        {
            StatusCode = (int)response.StatusCode,
            Content = content,
            ContentType = "application/json"
        };
    }

    [HttpPost("products/{productId:int}")]
    [Authorize]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("review-write")]
    public async Task<IActionResult> Upsert([FromRoute] int productId, [FromForm] ProductReviewUpsertRequestDto request)
    {
        if (!ModelState.IsValid)
        {
            var errors = ModelState.Values
                .SelectMany(x => x.Errors)
                .Select(x => x.ErrorMessage)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToList();

            return BadRequest(new
            {
                message = errors.Count > 0 ? errors[0] : "Dữ liệu đánh giá chưa hợp lệ."
            });
        }

        var client = _httpClientFactory.CreateClient("Ordering");
        AttachAccessToken(client);

        var payload = new
        {
            request.Rating,
            Comment = request.Comment.Trim()
        };

        var response = await client.PostAsJsonAsync($"/api/orders/product-reviews/products/{productId}", payload);
        var content = await response.Content.ReadAsStringAsync();

        return new ContentResult
        {
            StatusCode = (int)response.StatusCode,
            Content = content,
            ContentType = "application/json"
        };
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
}
