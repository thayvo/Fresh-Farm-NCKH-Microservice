using System.Globalization;
using FreshFarm.Ordering.Api.Models;
using FreshFarm.Ordering.Api.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.ML;
using Microsoft.ML.Trainers;

namespace FreshFarm.Ordering.Api.Services;

public sealed class RecommendationMlTrainingService
{
    private static readonly string[] SuccessfulOrderStatuses = ["delivered", "completed"];
    private const double PositiveSignalHalfLifeDays = 30d;
    private const float WeakNegativeLabel = 0.15f;

    private readonly FreshFarmOrderingDBContext _db;
    private readonly IOptionsMonitor<RecommendationMlOptions> _options;
    private readonly IHostEnvironment _hostEnvironment;
    private readonly ILogger<RecommendationMlTrainingService> _logger;

    public RecommendationMlTrainingService(
        FreshFarmOrderingDBContext db,
        IOptionsMonitor<RecommendationMlOptions> options,
        IHostEnvironment hostEnvironment,
        ILogger<RecommendationMlTrainingService> logger)
    {
        _db = db;
        _options = options;
        _hostEnvironment = hostEnvironment;
        _logger = logger;
    }

    public async Task<RecommendationMlRefreshResult> RebuildUserProductScoresAsync(
        bool force,
        bool? materializeUserProductScoresOverride,
        CancellationToken cancellationToken)
    {
        var options = _options.CurrentValue;
        var shouldMaterialize = materializeUserProductScoresOverride ?? options.MaterializeUserProductScores;
        if (!options.Enabled && !force)
        {
            return RecommendationMlRefreshResult.CreateSkipped("recommendation_ml_disabled");
        }

        var computedAt = DateTime.UtcNow;
        var interactions = await BuildInteractionDatasetAsync(options, computedAt, cancellationToken);
        var distinctUserCount = interactions.Select(item => item.UserId).Distinct().Count();
        var distinctProductCount = interactions.Select(item => item.ProductId).Distinct().Count();
        if (interactions.Count < Math.Max(1, options.MinInteractionRows)
            || distinctUserCount < Math.Max(1, options.MinDistinctUsers)
            || distinctProductCount < Math.Max(1, options.MinDistinctProducts))
        {
            return RecommendationMlRefreshResult.CreateSkipped(
                "insufficient_interaction_data",
                interactions.Count,
                distinctUserCount,
                distinctProductCount);
        }

        var selectedUserIds = interactions
            .GroupBy(item => item.UserId)
            .OrderByDescending(group => group.Sum(item => item.Label))
            .ThenBy(group => group.Key)
            .Take(Math.Clamp(options.MaxUsersPerRefresh, 1, 50_000))
            .Select(group => group.Key)
            .ToHashSet();
        var candidateProductIds = interactions
            .GroupBy(item => item.ProductId)
            .OrderByDescending(group => group.Sum(item => item.Label))
            .ThenBy(group => group.Key)
            .Take(Math.Clamp(options.MaxCandidateProducts, 1, 50_000))
            .Select(group => group.Key)
            .ToArray();

        var trainingRows = interactions
            .Where(item => selectedUserIds.Contains(item.UserId) && candidateProductIds.Contains(item.ProductId))
            .OrderByDescending(item => item.Label)
            .ThenBy(item => item.UserId)
            .ThenBy(item => item.ProductId)
            .Take(Math.Clamp(options.MaxTrainingPairs, 1_000, 2_000_000))
            .Select(item => new RecommendationMlTrainingRow
            {
                UserId = ToKey(item.UserId),
                ProductId = ToKey(item.ProductId),
                Label = item.Label
            })
            .ToList();

        if (trainingRows.Count == 0)
        {
            return RecommendationMlRefreshResult.CreateSkipped(
                "no_training_rows_after_limits",
                interactions.Count,
                distinctUserCount,
                distinctProductCount);
        }

        var mlContext = new MLContext(seed: options.RandomSeed);
        var trainingData = mlContext.Data.LoadFromEnumerable(trainingRows);
        var trainerOptions = new MatrixFactorizationTrainer.Options
        {
            MatrixColumnIndexColumnName = "UserIdEncoded",
            MatrixRowIndexColumnName = "ProductIdEncoded",
            LabelColumnName = nameof(RecommendationMlTrainingRow.Label),
            NumberOfIterations = Math.Clamp(options.NumberOfIterations, 1, 500),
            ApproximationRank = Math.Clamp(options.ApproximationRank, 1, 512),
            Alpha = options.Alpha,
            Lambda = options.Lambda,
            C = options.C,
            LossFunction = MatrixFactorizationTrainer.LossFunctionType.SquareLossOneClass
        };

        var pipeline = mlContext.Transforms.Conversion
            .MapValueToKey("UserIdEncoded", nameof(RecommendationMlTrainingRow.UserId))
            .Append(mlContext.Transforms.Conversion.MapValueToKey("ProductIdEncoded", nameof(RecommendationMlTrainingRow.ProductId)))
            .Append(mlContext.Recommendation().Trainers.MatrixFactorization(trainerOptions));

        var model = pipeline.Fit(trainingData);
        var modelPath = options.PersistModelArtifact
            ? SaveModelArtifact(mlContext, model, trainingData.Schema, options.ModelOutputPath)
            : null;

        var predictionEngine = mlContext.Model.CreatePredictionEngine<RecommendationMlTrainingRow, RecommendationMlPrediction>(model);
        var interactionLookup = interactions.ToDictionary(item => (item.UserId, item.ProductId));
        var purchasedProductsByUser = interactions
            .Where(item => item.PurchaseCount > 0)
            .GroupBy(item => item.UserId)
            .ToDictionary(
                group => group.Key,
                group => group.Select(item => item.ProductId).ToHashSet());

        var materializedRows = new List<RecommendationUserProductScore>();
        var normalizedTopN = Math.Clamp(options.TopNPerUser, 1, 500);
        foreach (var userId in selectedUserIds.OrderBy(id => id))
        {
            var purchasedProducts = purchasedProductsByUser.TryGetValue(userId, out var products)
                ? products
                : new HashSet<int>();
            var predictions = new List<RecommendationMlScoredProduct>(candidateProductIds.Length);
            foreach (var productId in candidateProductIds)
            {
                if (options.ExcludePreviouslyPurchasedProducts && purchasedProducts.Contains(productId))
                {
                    continue;
                }

                var prediction = predictionEngine.Predict(new RecommendationMlTrainingRow
                {
                    UserId = ToKey(userId),
                    ProductId = ToKey(productId)
                });
                if (float.IsFinite(prediction.Score))
                {
                    predictions.Add(new RecommendationMlScoredProduct(productId, prediction.Score));
                }
            }

            var rankedPredictions = predictions
                .OrderByDescending(item => item.Score)
                .ThenBy(item => item.ProductId)
                .Take(normalizedTopN)
                .ToArray();
            if (rankedPredictions.Length == 0)
            {
                continue;
            }

            var minScore = rankedPredictions.Min(item => item.Score);
            var maxScore = rankedPredictions.Max(item => item.Score);
            for (var index = 0; index < rankedPredictions.Length; index++)
            {
                var item = rankedPredictions[index];
                interactionLookup.TryGetValue((userId, item.ProductId), out var observed);
                materializedRows.Add(new RecommendationUserProductScore
                {
                    UserId = userId,
                    ProductId = item.ProductId,
                    ViewCount = observed?.ViewCount ?? 0,
                    SearchClickCount = observed?.SearchClickCount ?? 0,
                    RecommendationClickCount = observed?.RecommendationClickCount ?? 0,
                    PurchaseCount = observed?.PurchaseCount ?? 0,
                    UserProductScore = NormalizeModelScore(
                        item.Score,
                        minScore,
                        maxScore,
                        index + 1,
                        rankedPredictions.Length,
                        options),
                    LastInteractedAtUtc = observed?.LastInteractedAtUtc,
                    ComputedAt = computedAt
                });
            }
        }

        if (shouldMaterialize)
        {
            if (_db.Database.IsRelational())
            {
                await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
                await MaterializeUserProductScoresAsync(materializedRows, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            else
            {
                await MaterializeUserProductScoresAsync(materializedRows, cancellationToken);
            }
        }

        _logger.LogInformation(
            "Da rebuild ML.NET recommendation user-product scores. TrainingRows={TrainingRows}, Users={Users}, Products={Products}, Predictions={Predictions}, ModelPath={ModelPath}",
            trainingRows.Count,
            selectedUserIds.Count,
            candidateProductIds.Length,
            materializedRows.Count,
            modelPath);

        return new RecommendationMlRefreshResult
        {
            Succeeded = true,
            Algorithm = "mlnet_matrix_factorization_v1",
            InteractionRowCount = interactions.Count,
            TrainingRowCount = trainingRows.Count,
            DistinctUserCount = distinctUserCount,
            DistinctProductCount = distinctProductCount,
            SelectedUserCount = selectedUserIds.Count,
            CandidateProductCount = candidateProductIds.Length,
            MaterializedUserProductScoreRowCount = shouldMaterialize
                ? materializedRows.Count
                : 0,
            PreviewPredictionRowCount = shouldMaterialize
                ? 0
                : materializedRows.Count,
            ComputedAtUtc = computedAt,
            ModelPath = modelPath
        };
    }

    private async Task<List<RecommendationMlInteraction>> BuildInteractionDatasetAsync(
        RecommendationMlOptions options,
        DateTime computedAt,
        CancellationToken cancellationToken)
    {
        var lookbackFromUtc = computedAt.AddDays(-Math.Clamp(options.LookbackDays, 1, 3650));
        var interactions = new Dictionary<(int UserId, int ProductId), RecommendationMlInteraction>();

        void Update(
            int userId,
            int productId,
            DateTime lastInteractedAtUtc,
            Action<RecommendationMlInteraction> apply)
        {
            if (userId <= 0 || productId <= 0)
            {
                return;
            }

            var key = (userId, productId);
            if (!interactions.TryGetValue(key, out var interaction))
            {
                interaction = new RecommendationMlInteraction
                {
                    UserId = userId,
                    ProductId = productId
                };
                interactions[key] = interaction;
            }

            interaction.LastInteractedAtUtc = MaxUtc(interaction.LastInteractedAtUtc, lastInteractedAtUtc);
            apply(interaction);
        }

        var viewSignals = await _db.ProductViewEvents
            .AsNoTracking()
            .Where(item =>
                item.CreatedAt >= lookbackFromUtc
                && item.UserId.HasValue
                && item.UserId > 0
                && item.ProductId > 0)
            .GroupBy(item => new { UserId = item.UserId!.Value, item.ProductId })
            .Select(group => new
            {
                group.Key.UserId,
                group.Key.ProductId,
                Count = group.Count(),
                LastInteractedAtUtc = group.Max(item => item.CreatedAt)
            })
            .ToListAsync(cancellationToken);

        foreach (var signal in viewSignals)
        {
            Update(signal.UserId, signal.ProductId, signal.LastInteractedAtUtc, item =>
            {
                item.ViewCount += signal.Count;
                item.Label += signal.Count
                    * options.ViewWeight
                    * (float)CalculatePositiveSignalDecay(signal.LastInteractedAtUtc, computedAt);
            });
        }

        var searchClickSignals = await _db.SearchClickEvents
            .AsNoTracking()
            .Where(item =>
                item.CreatedAt >= lookbackFromUtc
                && item.UserId.HasValue
                && item.UserId > 0
                && item.ProductId > 0)
            .GroupBy(item => new { UserId = item.UserId!.Value, item.ProductId })
            .Select(group => new
            {
                group.Key.UserId,
                group.Key.ProductId,
                Count = group.Count(),
                LastInteractedAtUtc = group.Max(item => item.CreatedAt)
            })
            .ToListAsync(cancellationToken);

        foreach (var signal in searchClickSignals)
        {
            Update(signal.UserId, signal.ProductId, signal.LastInteractedAtUtc, item =>
            {
                item.SearchClickCount += signal.Count;
                item.Label += signal.Count
                    * options.SearchClickWeight
                    * (float)CalculatePositiveSignalDecay(signal.LastInteractedAtUtc, computedAt);
            });
        }

        var recommendationClickSignals = await _db.RecommendationClickEvents
            .AsNoTracking()
            .Where(item =>
                item.CreatedAt >= lookbackFromUtc
                && item.UserId.HasValue
                && item.UserId > 0
                && item.ProductId > 0)
            .GroupBy(item => new { UserId = item.UserId!.Value, item.ProductId })
            .Select(group => new
            {
                group.Key.UserId,
                group.Key.ProductId,
                Count = group.Count(),
                LastInteractedAtUtc = group.Max(item => item.CreatedAt)
            })
            .ToListAsync(cancellationToken);

        foreach (var signal in recommendationClickSignals)
        {
            Update(signal.UserId, signal.ProductId, signal.LastInteractedAtUtc, item =>
            {
                item.RecommendationClickCount += signal.Count;
                item.Label += signal.Count
                    * options.RecommendationClickWeight
                    * (float)CalculatePositiveSignalDecay(signal.LastInteractedAtUtc, computedAt);
            });
        }

        var purchaseSignals = await (
                from order in _db.Orders.AsNoTracking()
                join detail in _db.OrderDetails.AsNoTracking() on order.OrderId equals detail.OrderId
                where order.UserId > 0
                    && detail.ProductId > 0
                    && order.OrderDate >= lookbackFromUtc
                    && SuccessfulOrderStatuses.Contains((order.Status ?? string.Empty).Trim().ToLower())
                group new { order, detail } by new { order.UserId, detail.ProductId }
                into grouped
                select new
                {
                    grouped.Key.UserId,
                    grouped.Key.ProductId,
                    Count = grouped.Sum(item => item.detail.Quantity),
                    LastInteractedAtUtc = grouped.Max(item => item.order.OrderDate)
                })
            .ToListAsync(cancellationToken);

        foreach (var signal in purchaseSignals)
        {
            Update(signal.UserId, signal.ProductId, signal.LastInteractedAtUtc, item =>
            {
                item.PurchaseCount += signal.Count;
                item.Label += signal.Count
                    * options.PurchaseWeight
                    * (float)CalculatePositiveSignalDecay(signal.LastInteractedAtUtc, computedAt);
            });
        }

        var negativeFeedbackScores = await RecommendationNegativeFeedbackScoring.LoadUserProductScoreMapAsync(
            _db,
            lookbackFromUtc,
            computedAt,
            computedAt,
            cancellationToken);
        foreach (var (key, negativeFeedback) in negativeFeedbackScores)
        {
            if (negativeFeedback.ImpressionNoClickCount < RecommendationNegativeFeedbackScoring.MinimumImpressionBucketsForPenalty)
            {
                continue;
            }

            if (interactions.TryGetValue(key, out var interaction))
            {
                interaction.Label = Math.Max(WeakNegativeLabel, interaction.Label + (float)negativeFeedback.PenaltyScore);
                interaction.LastInteractedAtUtc = MaxUtc(interaction.LastInteractedAtUtc, negativeFeedback.LastSignalAtUtc);
                continue;
            }

            interactions[key] = new RecommendationMlInteraction
            {
                UserId = key.UserId,
                ProductId = key.ProductId,
                Label = WeakNegativeLabel,
                LastInteractedAtUtc = negativeFeedback.LastSignalAtUtc
            };
        }

        return interactions.Values
            .Where(item => item.Label > 0f)
            .OrderByDescending(item => item.Label)
            .ThenBy(item => item.UserId)
            .ThenBy(item => item.ProductId)
            .ToList();
    }

    private async Task MaterializeUserProductScoresAsync(
        IReadOnlyCollection<RecommendationUserProductScore> rows,
        CancellationToken cancellationToken)
    {
        _db.RecommendationUserProductScores.RemoveRange(_db.RecommendationUserProductScores);
        await _db.SaveChangesAsync(cancellationToken);
        await _db.RecommendationUserProductScores.AddRangeAsync(rows, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
    }

    private string? SaveModelArtifact(MLContext mlContext, ITransformer model, DataViewSchema schema, string configuredPath)
    {
        var modelPath = ResolveModelPath(configuredPath);
        Directory.CreateDirectory(Path.GetDirectoryName(modelPath)!);
        mlContext.Model.Save(model, schema, modelPath);
        return modelPath;
    }

    private string ResolveModelPath(string configuredPath)
    {
        var path = string.IsNullOrWhiteSpace(configuredPath)
            ? "App_Data/recommendation-user-product-ml.zip"
            : configuredPath.Trim();
        return Path.IsPathRooted(path)
            ? path
            : Path.GetFullPath(Path.Combine(_hostEnvironment.ContentRootPath, path));
    }

    private static double NormalizeModelScore(
        float score,
        float minScore,
        float maxScore,
        int rank,
        int count,
        RecommendationMlOptions options)
    {
        var floor = Math.Min(options.NormalizedScoreFloor, options.NormalizedScoreCeiling);
        var ceiling = Math.Max(options.NormalizedScoreFloor, options.NormalizedScoreCeiling);
        if (maxScore > minScore)
        {
            var normalized = floor + ((score - minScore) / (maxScore - minScore) * (ceiling - floor));
            return Math.Round(Math.Clamp(normalized, floor, ceiling), 2);
        }

        if (count <= 1)
        {
            return ceiling;
        }

        var rankScore = ceiling - ((rank - 1d) * (ceiling - floor) / (count - 1d));
        return Math.Round(Math.Clamp(rankScore, floor, ceiling), 2);
    }

    private static DateTime? MaxUtc(DateTime? current, DateTime candidate)
        => !current.HasValue || candidate > current.Value
            ? candidate
            : current;

    private static DateTime? MaxUtc(DateTime? current, DateTime? candidate)
        => candidate.HasValue
            ? MaxUtc(current, candidate.Value)
            : current;

    private static double CalculatePositiveSignalDecay(DateTime signalAtUtc, DateTime computedAtUtc)
    {
        var ageDays = Math.Max(0d, (computedAtUtc - signalAtUtc).TotalDays);
        return Math.Pow(0.5d, ageDays / PositiveSignalHalfLifeDays);
    }

    private static string ToKey(int id)
        => id.ToString(CultureInfo.InvariantCulture);

    private sealed class RecommendationMlTrainingRow
    {
        public string UserId { get; set; } = string.Empty;

        public string ProductId { get; set; } = string.Empty;

        public float Label { get; set; }
    }

    private sealed class RecommendationMlPrediction
    {
        public float Score { get; set; }
    }

    private sealed class RecommendationMlInteraction
    {
        public int UserId { get; set; }

        public int ProductId { get; set; }

        public int ViewCount { get; set; }

        public int SearchClickCount { get; set; }

        public int RecommendationClickCount { get; set; }

        public int PurchaseCount { get; set; }

        public float Label { get; set; }

        public DateTime? LastInteractedAtUtc { get; set; }
    }

    private sealed record RecommendationMlScoredProduct(int ProductId, float Score);
}

public sealed class RecommendationMlRefreshResult
{
    public bool Succeeded { get; set; }

    public bool Skipped { get; set; }

    public string? SkipReason { get; set; }

    public string Algorithm { get; set; } = "mlnet_matrix_factorization_v1";

    public int InteractionRowCount { get; set; }

    public int TrainingRowCount { get; set; }

    public int DistinctUserCount { get; set; }

    public int DistinctProductCount { get; set; }

    public int SelectedUserCount { get; set; }

    public int CandidateProductCount { get; set; }

    public int MaterializedUserProductScoreRowCount { get; set; }

    public int PreviewPredictionRowCount { get; set; }

    public DateTime? ComputedAtUtc { get; set; }

    public string? ModelPath { get; set; }

    public static RecommendationMlRefreshResult CreateSkipped(
        string reason,
        int interactionRows = 0,
        int distinctUsers = 0,
        int distinctProducts = 0)
        => new()
        {
            Skipped = true,
            SkipReason = reason,
            InteractionRowCount = interactionRows,
            DistinctUserCount = distinctUsers,
            DistinctProductCount = distinctProducts
        };
}
