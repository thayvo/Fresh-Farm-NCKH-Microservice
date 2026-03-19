using FreshFarm.Web.Bff.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FreshFarm.Web.Bff.Controllers;

[ApiController]
[Authorize(Policy = "AdminOnly")]
[Route("api/security/rate-limits")]
public sealed class RateLimitTelemetryController : ControllerBase
{
    private readonly IRateLimitTelemetryService _rateLimitTelemetryService;

    public RateLimitTelemetryController(IRateLimitTelemetryService rateLimitTelemetryService)
    {
        _rateLimitTelemetryService = rateLimitTelemetryService;
    }

    [HttpGet("summary")]
    public IActionResult GetSummary()
    {
        return Ok(_rateLimitTelemetryService.CreateSnapshot());
    }
}
