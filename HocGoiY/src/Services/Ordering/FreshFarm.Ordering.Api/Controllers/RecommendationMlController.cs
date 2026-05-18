using System.Security.Cryptography;
using System.Text;
using FreshFarm.Ordering.Api.Options;
using FreshFarm.Ordering.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace FreshFarm.Ordering.Api.Controllers;

[ApiController]
[Route("api/orders/recommendation-ml")]
public sealed class RecommendationMlController : ControllerBase
{
    private readonly RecommendationMlTrainingService _trainingService;
    private readonly IOptionsMonitor<RecommendationMlOptions> _mlOptions;
    private readonly InternalServiceAuthOptions _internalServiceAuthOptions;

    public RecommendationMlController(
        RecommendationMlTrainingService trainingService,
        IOptionsMonitor<RecommendationMlOptions> mlOptions,
        IOptions<InternalServiceAuthOptions> internalServiceAuthOptions)
    {
        _trainingService = trainingService;
        _mlOptions = mlOptions;
        _internalServiceAuthOptions = internalServiceAuthOptions.Value;
    }

    [HttpGet("status")]
    public IActionResult GetStatus()
    {
        if (!IsValidInternalServiceRequest())
        {
            return Unauthorized(new { message = "Unauthorized internal recommendation ML request." });
        }

        var options = _mlOptions.CurrentValue;
        return Ok(new
        {
            enabled = options.Enabled,
            algorithm = "mlnet_matrix_factorization_v1",
            materializeUserProductScores = options.MaterializeUserProductScores,
            persistModelArtifact = options.PersistModelArtifact,
            modelOutputPath = options.ModelOutputPath,
            lookbackDays = options.LookbackDays,
            trainingIntervalMinutes = options.TrainingIntervalMinutes,
            topNPerUser = options.TopNPerUser,
            maxUsersPerRefresh = options.MaxUsersPerRefresh,
            maxCandidateProducts = options.MaxCandidateProducts,
            minInteractionRows = options.MinInteractionRows,
            minDistinctUsers = options.MinDistinctUsers,
            minDistinctProducts = options.MinDistinctProducts
        });
    }

    [HttpPost("rebuild-user-product")]
    public async Task<IActionResult> RebuildUserProductScores(
        [FromQuery] bool force = false,
        [FromQuery] bool previewOnly = false,
        CancellationToken cancellationToken = default)
    {
        if (!IsValidInternalServiceRequest())
        {
            return Unauthorized(new { message = "Unauthorized internal recommendation ML request." });
        }

        var result = await _trainingService.RebuildUserProductScoresAsync(
            force,
            materializeUserProductScoresOverride: previewOnly ? false : null,
            cancellationToken);
        return Ok(result);
    }

    private bool IsValidInternalServiceRequest()
    {
        var configuredKey = _internalServiceAuthOptions.InternalServiceKey?.Trim();
        var incomingKey = Request.Headers["X-Internal-Service-Key"].ToString().Trim();

        if (string.IsNullOrWhiteSpace(configuredKey) || string.IsNullOrWhiteSpace(incomingKey))
        {
            return false;
        }

        var configuredBytes = Encoding.UTF8.GetBytes(configuredKey);
        var incomingBytes = Encoding.UTF8.GetBytes(incomingKey);
        return CryptographicOperations.FixedTimeEquals(configuredBytes, incomingBytes);
    }
}
