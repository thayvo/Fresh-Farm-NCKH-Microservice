using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;
using FreshFarm.Ordering.Api.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FreshFarm.Ordering.Api.Controllers;

[ApiController]
[Route("api/orders/recommendation-events")]
public sealed class RecommendationEventsController : ControllerBase
{
    private readonly FreshFarmOrderingDBContext _db;

    public RecommendationEventsController(FreshFarmOrderingDBContext db)
    {
        _db = db;
    }

    [HttpPost("product-view")]
    public async Task<IActionResult> TrackProductView([FromBody] TrackProductViewRequest? request, CancellationToken cancellationToken)
    {
        if (request is null || request.ProductId <= 0)
        {
            return BadRequest(new { ok = false, message = "ProductId không hợp lệ." });
        }

        var sessionId = NormalizeRequiredText(request.SessionId, 120);
        if (sessionId is null)
        {
            return BadRequest(new { ok = false, message = "SessionId không hợp lệ." });
        }

        var sourcePage = NormalizeRequiredText(request.SourcePage, 50);
        if (sourcePage is null)
        {
            return BadRequest(new { ok = false, message = "SourcePage không hợp lệ." });
        }

        var sourceModule = NormalizeRequiredText(request.SourceModule, 80) ?? "unknown";

        var entity = new ProductViewEvent
        {
            UserId = TryGetUserIdFromToken(),
            SessionId = sessionId,
            ProductId = request.ProductId,
            SellerId = NormalizeNullablePositiveInt(request.SellerId),
            SourcePage = sourcePage,
            SourceModule = sourceModule,
            CreatedAt = DateTime.UtcNow
        };

        _db.ProductViewEvents.Add(entity);
        await _db.SaveChangesAsync(cancellationToken);

        return Ok(new { ok = true, eventId = entity.ProductViewEventId });
    }

    [HttpPost("search")]
    public async Task<IActionResult> TrackSearch([FromBody] TrackSearchRequest? request, CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return BadRequest(new { ok = false, message = "Payload không hợp lệ." });
        }

        var sessionId = NormalizeRequiredText(request.SessionId, 120);
        if (sessionId is null)
        {
            return BadRequest(new { ok = false, message = "SessionId không hợp lệ." });
        }

        var keyword = NormalizeRequiredText(request.Keyword, 200);
        if (keyword is null)
        {
            return BadRequest(new { ok = false, message = "Keyword không hợp lệ." });
        }

        var filtersJson = NormalizeJsonText(request.Filters, 4000);
        var resultCount = Math.Max(0, request.ResultCount);

        var entity = new SearchEvent
        {
            UserId = TryGetUserIdFromToken(),
            SessionId = sessionId,
            Keyword = keyword,
            FiltersJson = filtersJson,
            ResultCount = resultCount,
            CreatedAt = DateTime.UtcNow
        };

        _db.SearchEvents.Add(entity);
        await _db.SaveChangesAsync(cancellationToken);

        return Ok(new { ok = true, eventId = entity.SearchEventId });
    }

    [HttpPost("search-click")]
    public async Task<IActionResult> TrackSearchClick([FromBody] TrackSearchClickRequest? request, CancellationToken cancellationToken)
    {
        if (request is null || request.ProductId <= 0)
        {
            return BadRequest(new { ok = false, message = "ProductId không hợp lệ." });
        }

        var sessionId = NormalizeRequiredText(request.SessionId, 120);
        if (sessionId is null)
        {
            return BadRequest(new { ok = false, message = "SessionId không hợp lệ." });
        }

        var normalizedSearchEventId = NormalizeNullablePositiveInt(request.SearchEventId);
        if (normalizedSearchEventId.HasValue &&
            !await _db.SearchEvents.AsNoTracking().AnyAsync(x => x.SearchEventId == normalizedSearchEventId.Value, cancellationToken))
        {
            return BadRequest(new { ok = false, message = "SearchEventId không tồn tại." });
        }

        var entity = new SearchClickEvent
        {
            SearchEventId = normalizedSearchEventId,
            UserId = TryGetUserIdFromToken(),
            SessionId = sessionId,
            ProductId = request.ProductId,
            SellerId = NormalizeNullablePositiveInt(request.SellerId),
            Rank = Math.Max(0, request.Rank),
            CreatedAt = DateTime.UtcNow
        };

        _db.SearchClickEvents.Add(entity);
        await _db.SaveChangesAsync(cancellationToken);

        return Ok(new { ok = true, eventId = entity.SearchClickEventId });
    }

    [HttpPost("recommendation-impression")]
    public async Task<IActionResult> TrackRecommendationImpression([FromBody] TrackRecommendationImpressionRequest? request, CancellationToken cancellationToken)
    {
        if (request is null || request.ProductId <= 0)
        {
            return BadRequest(new { ok = false, message = "ProductId không hợp lệ." });
        }

        var sessionId = NormalizeRequiredText(request.SessionId, 120);
        if (sessionId is null)
        {
            return BadRequest(new { ok = false, message = "SessionId không hợp lệ." });
        }

        var placement = NormalizeRequiredText(request.Placement, 60);
        if (placement is null)
        {
            return BadRequest(new { ok = false, message = "Placement không hợp lệ." });
        }

        var algorithm = NormalizeRequiredText(request.Algorithm, 30);
        if (algorithm is null)
        {
            return BadRequest(new { ok = false, message = "Algorithm không hợp lệ." });
        }

        var entity = new RecommendationImpressionEvent
        {
            UserId = TryGetUserIdFromToken(),
            SessionId = sessionId,
            Placement = placement,
            RecommendationRunId = NormalizeOptionalText(request.RecommendationRunId, 100),
            ProductId = request.ProductId,
            Rank = Math.Max(0, request.Rank),
            Algorithm = algorithm,
            CreatedAt = DateTime.UtcNow
        };

        _db.RecommendationImpressionEvents.Add(entity);
        await _db.SaveChangesAsync(cancellationToken);

        return Ok(new { ok = true, eventId = entity.RecommendationImpressionEventId });
    }

    [HttpPost("recommendation-click")]
    public async Task<IActionResult> TrackRecommendationClick([FromBody] TrackRecommendationClickRequest? request, CancellationToken cancellationToken)
    {
        if (request is null || request.ProductId <= 0)
        {
            return BadRequest(new { ok = false, message = "ProductId không hợp lệ." });
        }

        var sessionId = NormalizeRequiredText(request.SessionId, 120);
        if (sessionId is null)
        {
            return BadRequest(new { ok = false, message = "SessionId không hợp lệ." });
        }

        var placement = NormalizeRequiredText(request.Placement, 60);
        if (placement is null)
        {
            return BadRequest(new { ok = false, message = "Placement không hợp lệ." });
        }

        var algorithm = NormalizeRequiredText(request.Algorithm, 30);
        if (algorithm is null)
        {
            return BadRequest(new { ok = false, message = "Algorithm không hợp lệ." });
        }

        var normalizedImpressionEventId = NormalizeNullablePositiveInt(request.RecommendationImpressionEventId);
        if (normalizedImpressionEventId.HasValue &&
            !await _db.RecommendationImpressionEvents.AsNoTracking()
                .AnyAsync(x => x.RecommendationImpressionEventId == normalizedImpressionEventId.Value, cancellationToken))
        {
            return BadRequest(new { ok = false, message = "RecommendationImpressionEventId không tồn tại." });
        }

        var entity = new RecommendationClickEvent
        {
            RecommendationImpressionEventId = normalizedImpressionEventId,
            UserId = TryGetUserIdFromToken(),
            SessionId = sessionId,
            ProductId = request.ProductId,
            Placement = placement,
            Algorithm = algorithm,
            CreatedAt = DateTime.UtcNow
        };

        _db.RecommendationClickEvents.Add(entity);
        await _db.SaveChangesAsync(cancellationToken);

        return Ok(new { ok = true, eventId = entity.RecommendationClickEventId });
    }

    private int? TryGetUserIdFromToken()
    {
        var subject = User.FindFirstValue(JwtRegisteredClaimNames.Sub)
                      ?? User.FindFirstValue(ClaimTypes.NameIdentifier)
                      ?? User.FindFirstValue("sub");

        return int.TryParse(subject, out var userId) && userId > 0
            ? userId
            : null;
    }

    private static int? NormalizeNullablePositiveInt(int? value)
        => value is > 0 ? value.Value : null;

    private static string? NormalizeRequiredText(string? value, int maxLength)
    {
        var normalized = NormalizeOptionalText(value, maxLength);
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private static string? NormalizeOptionalText(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        return normalized.Length <= maxLength
            ? normalized
            : normalized[..maxLength];
    }

    private static string? NormalizeJsonText(JsonElement? value, int maxLength)
    {
        if (!value.HasValue || value.Value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            return null;
        }

        var json = value.Value.GetRawText();
        return json.Length <= maxLength
            ? json
            : json[..maxLength];
    }

    public sealed class TrackProductViewRequest
    {
        public string? SessionId { get; set; }

        public int ProductId { get; set; }

        public int? SellerId { get; set; }

        public string? SourcePage { get; set; }

        public string? SourceModule { get; set; }
    }

    public sealed class TrackSearchRequest
    {
        public string? SessionId { get; set; }

        public string? Keyword { get; set; }

        public JsonElement? Filters { get; set; }

        public int ResultCount { get; set; }
    }

    public sealed class TrackSearchClickRequest
    {
        public string? SessionId { get; set; }

        public int? SearchEventId { get; set; }

        public int ProductId { get; set; }

        public int? SellerId { get; set; }

        public int Rank { get; set; }
    }

    public sealed class TrackRecommendationImpressionRequest
    {
        public string? SessionId { get; set; }

        public string? Placement { get; set; }

        public string? RecommendationRunId { get; set; }

        public int ProductId { get; set; }

        public int Rank { get; set; }

        public string? Algorithm { get; set; }
    }

    public sealed class TrackRecommendationClickRequest
    {
        public string? SessionId { get; set; }

        public int? RecommendationImpressionEventId { get; set; }

        public int ProductId { get; set; }

        public string? Placement { get; set; }

        public string? Algorithm { get; set; }
    }
}
