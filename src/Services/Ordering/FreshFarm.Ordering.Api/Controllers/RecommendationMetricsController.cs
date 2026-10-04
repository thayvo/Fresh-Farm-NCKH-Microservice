using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using FreshFarm.Ordering.Api.Dtos;
using FreshFarm.Ordering.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace FreshFarm.Ordering.Api.Controllers;

[ApiController]
[Route("api/orders/recommendation-metrics")]
public sealed class RecommendationMetricsController : ControllerBase
{
    private readonly IRecommendationMetricsService _metricsService;

    public RecommendationMetricsController(IRecommendationMetricsService metricsService)
    {
        _metricsService = metricsService;
    }

    [HttpPost("impressions")]
    public async Task<IActionResult> TrackImpressions(
        [FromBody] TrackRecommendationImpressionsRequest? request,
        CancellationToken cancellationToken)
    {
        if (request is null || request.Items.Count == 0)
        {
            return BadRequest(new { ok = false, message = "Items khong duoc rong." });
        }

        await _metricsService.TrackImpressionsAsync(
            request.UserId ?? TryGetUserIdFromToken(),
            request.Items,
            cancellationToken);

        return Accepted(new { ok = true });
    }

    [HttpPost("click")]
    public async Task<IActionResult> TrackClick(
        [FromBody] TrackRecommendationClickMetricRequest? request,
        CancellationToken cancellationToken)
    {
        if (request is null || request.ProductId <= 0)
        {
            return BadRequest(new { ok = false, message = "ProductId khong hop le." });
        }

        await _metricsService.TrackClickAsync(
            request.UserId ?? TryGetUserIdFromToken(),
            request.ProductId,
            request.Position,
            request.ExperimentGroup,
            cancellationToken);

        return Accepted(new { ok = true });
    }

    [HttpPost("add-to-cart")]
    public async Task<IActionResult> TrackAddToCart(
        [FromBody] TrackRecommendationAddToCartMetricRequest? request,
        CancellationToken cancellationToken)
    {
        if (request is null || request.ProductId <= 0)
        {
            return BadRequest(new { ok = false, message = "ProductId khong hop le." });
        }

        await _metricsService.TrackAddToCartAsync(
            request.UserId ?? TryGetUserIdFromToken(),
            request.ProductId,
            request.Position,
            request.ExperimentGroup,
            cancellationToken);

        return Accepted(new { ok = true });
    }

    [HttpPost("purchase")]
    public async Task<IActionResult> TrackPurchase(
        [FromBody] TrackRecommendationPurchaseMetricRequest? request,
        CancellationToken cancellationToken)
    {
        if (request is null || request.ProductId <= 0)
        {
            return BadRequest(new { ok = false, message = "ProductId khong hop le." });
        }

        await _metricsService.TrackPurchaseAsync(
            request.UserId ?? TryGetUserIdFromToken(),
            request.ProductId,
            request.Position,
            request.Revenue,
            request.ExperimentGroup,
            cancellationToken);

        return Accepted(new { ok = true });
    }

    [HttpGet("ctr")]
    [HttpGet("/api/recommendation-metrics/ctr")]
    public async Task<ActionResult<RecommendationCtrReportDto>> GetCtr(
        [FromQuery] DateTime? fromDate,
        [FromQuery] DateTime? toDate,
        CancellationToken cancellationToken)
    {
        var validationError = TryResolveCtrWindow(
            fromDate,
            toDate,
            out var normalizedFromDate,
            out var normalizedToDate);
        if (validationError is not null)
        {
            return validationError;
        }

        var metrics = await _metricsService.GetCtrAsync(normalizedFromDate, normalizedToDate, cancellationToken);
        return Ok(metrics);
    }

    [HttpGet("objective")]
    [HttpGet("/api/recommendation-metrics/objective")]
    public async Task<ActionResult<RecommendationObjectiveMetricsReportDto>> GetObjectiveMetrics(
        [FromQuery] int[] productIds,
        [FromQuery] DateTime? fromDate,
        [FromQuery] DateTime? toDate,
        CancellationToken cancellationToken)
    {
        var validationError = TryResolveCtrWindow(
            fromDate,
            toDate,
            out var normalizedFromDate,
            out var normalizedToDate);
        if (validationError is not null)
        {
            return validationError;
        }

        var metrics = await _metricsService.GetObjectiveMetricsAsync(
            normalizedFromDate,
            normalizedToDate,
            productIds,
            cancellationToken);
        return Ok(metrics);
    }

    [HttpGet("ctr-by-position")]
    [HttpGet("/api/recommendation-metrics/ctr-by-position")]
    public async Task<ActionResult<RecommendationCtrByPositionReportDto>> GetCtrByPosition(
        [FromQuery] DateTime? fromDate,
        [FromQuery] DateTime? toDate,
        CancellationToken cancellationToken)
    {
        var validationError = TryResolveCtrWindow(
            fromDate,
            toDate,
            out var normalizedFromDate,
            out var normalizedToDate);
        if (validationError is not null)
        {
            return validationError;
        }

        var metrics = await _metricsService.GetCtrByPositionAsync(
            normalizedFromDate,
            normalizedToDate,
            cancellationToken);
        return Ok(metrics);
    }

    [HttpGet("negative-feedback")]
    [HttpGet("/api/recommendation-metrics/negative-feedback")]
    public async Task<ActionResult<RecommendationNegativeFeedbackReportDto>> GetNegativeFeedback(
        [FromQuery] int? userId,
        [FromQuery] int[] productIds,
        [FromQuery] DateTime? fromDate,
        [FromQuery] DateTime? toDate,
        CancellationToken cancellationToken)
    {
        var validationError = TryResolveCtrWindow(
            fromDate,
            toDate,
            out var normalizedFromDate,
            out var normalizedToDate,
            defaultLookbackDays: 14);
        if (validationError is not null)
        {
            return validationError;
        }

        var metrics = await _metricsService.GetNegativeFeedbackAsync(
            userId ?? TryGetUserIdFromToken(),
            normalizedFromDate,
            normalizedToDate,
            productIds,
            cancellationToken);
        return Ok(metrics);
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

    private ActionResult? TryResolveCtrWindow(
        DateTime? fromDate,
        DateTime? toDate,
        out DateTime normalizedFromDate,
        out DateTime normalizedToDate,
        int defaultLookbackDays = 7)
    {
        normalizedToDate = toDate.HasValue ? NormalizeUtcBoundary(toDate.Value) : DateTime.UtcNow;
        normalizedFromDate = fromDate.HasValue
            ? NormalizeUtcBoundary(fromDate.Value)
            : normalizedToDate.AddDays(-Math.Max(1, defaultLookbackDays));
        if (normalizedFromDate >= normalizedToDate)
        {
            return BadRequest(new { ok = false, message = "fromDate phai nho hon toDate." });
        }

        return null;
    }

    private static DateTime NormalizeUtcBoundary(DateTime value)
    {
        return value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };
    }
}
