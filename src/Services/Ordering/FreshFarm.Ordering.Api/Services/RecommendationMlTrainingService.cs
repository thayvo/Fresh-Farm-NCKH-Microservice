using FreshFarm.Ordering.Api.Models; // Chứa các model/entity của hệ thống ordering
using FreshFarm.Ordering.Api.Options; // Chứa class cấu hình RecommendationMlOptions
using Microsoft.EntityFrameworkCore; // Entity Framework Core
using Microsoft.Extensions.Options; // Hỗ trợ đọc cấu hình bằng IOptionsMonitor
using Microsoft.ML; // ML.NET core
using Microsoft.ML.Trainers; // Các trainer của ML.NET
using System.Globalization; // Dùng để xử lý định dạng văn hóa, chuyển đổi số/string invariant culture

namespace FreshFarm.Ordering.Api.Services;

// Service huấn luyện recommendation bằng ML.NET Matrix Factorization.
// Phần này tạo hướng gợi ý user -> sản phẩm và ghi kết quả vào RecommendationUserProductScore.
public sealed class RecommendationMlTrainingService
{
    // Các trạng thái đơn hàng được xem là mua thành công
    private static readonly string[] SuccessfulOrderStatuses = ["delivered", "completed"];

    // Half-life decay cho tín hiệu positive (30 ngày)
    private const double PositiveSignalHalfLifeDays = 30d;

    // Giá trị label yếu cho negative feedback
    private const float WeakNegativeLabel = 0.15f;

    // DbContext thao tác database
    private readonly FreshFarmOrderingDBContext _db;

    // Theo dõi config recommendation runtime
    private readonly IOptionsMonitor<RecommendationMlOptions> _options;

    // Environment host
    private readonly IHostEnvironment _hostEnvironment;

    // Logger ghi log hệ thống
    private readonly ILogger<RecommendationMlTrainingService> _logger;

    // Constructor inject dependency
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

    // Hàm rebuild toàn bộ recommendation score user-product.
    // Input là lịch sử view/click/mua/feedback; output là danh sách sản phẩm dự đoán phù hợp cho từng user.
    public async Task<RecommendationMlRefreshResult> RebuildUserProductScoresAsync(
        bool force,
        bool? materializeUserProductScoresOverride,
        CancellationToken cancellationToken)
    {
        // Lấy config hiện tại
        var options = _options.CurrentValue;

        // Xác định có materialize score xuống DB hay không
        var shouldMaterialize = materializeUserProductScoresOverride ?? options.MaterializeUserProductScores;

        // Nếu recommendation bị disable và không force => skip
        if (!options.Enabled && !force)
        {
            return RecommendationMlRefreshResult.CreateSkipped("recommendation_ml_disabled");
        }

        // Thời gian tính toán
        var computedAt = DateTime.UtcNow;

        // Build interaction dataset: gom nhiều loại hành vi thành một ma trận user-product để ML.NET học.
        var interactions = await BuildInteractionDatasetAsync(options, computedAt, cancellationToken);

        // Đếm số user unique
        var distinctUserCount = interactions.Select(item => item.UserId).Distinct().Count();

        // Đếm số product unique
        var distinctProductCount = interactions.Select(item => item.ProductId).Distinct().Count();

        // Kiểm tra dữ liệu tối thiểu để train
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

        // Chọn user có label mạnh nhất
        var selectedUserIds = interactions
            .GroupBy(item => item.UserId)
            .OrderByDescending(group => group.Sum(item => item.Label))
            .ThenBy(group => group.Key)
            .Take(Math.Clamp(options.MaxUsersPerRefresh, 1, 50_000))
            .Select(group => group.Key)
            .ToHashSet();

        // Chọn product candidate mạnh nhất
        var candidateProductIds = interactions
            .GroupBy(item => item.ProductId)
            .OrderByDescending(group => group.Sum(item => item.Label))
            .ThenBy(group => group.Key)
            .Take(Math.Clamp(options.MaxCandidateProducts, 1, 50_000))
            .Select(group => group.Key)
            .ToArray();

        // Build training rows cho ML.NET
        var trainingRows = interactions
            .Where(item => selectedUserIds.Contains(item.UserId) && candidateProductIds.Contains(item.ProductId))
            .OrderByDescending(item => item.Label)
            .ThenBy(item => item.UserId)
            .ThenBy(item => item.ProductId)
            .Take(Math.Clamp(options.MaxTrainingPairs, 1_000, 2_000_000))
            .Select(item => new RecommendationMlTrainingRow
            {
                // ML.NET MatrixFactorization dùng key string
                UserId = ToKey(item.UserId),

                // Product key
                ProductId = ToKey(item.ProductId),

                // Label score interaction
                Label = item.Label
            })
            .ToList();

        // Không có training row => skip
        if (trainingRows.Count == 0)
        {
            return RecommendationMlRefreshResult.CreateSkipped(
                "no_training_rows_after_limits",
                interactions.Count,
                distinctUserCount,
                distinctProductCount);
        }

        // Tạo MLContext
        var mlContext = new MLContext(seed: options.RandomSeed);

        // Load training data
        var trainingData = mlContext.Data.LoadFromEnumerable(trainingRows);

        // Config MatrixFactorization trainer
        var trainerOptions = new MatrixFactorizationTrainer.Options
        {
            // Cột user encoded
            MatrixColumnIndexColumnName = "UserIdEncoded",

            // Cột product encoded
            MatrixRowIndexColumnName = "ProductIdEncoded",

            // Label column
            LabelColumnName = nameof(RecommendationMlTrainingRow.Label),

            // Số iteration train
            NumberOfIterations = Math.Clamp(options.NumberOfIterations, 1, 500),

            // Rank approximation
            ApproximationRank = Math.Clamp(options.ApproximationRank, 1, 512),

            // Learning rate
            Alpha = options.Alpha,

            // Regularization
            Lambda = options.Lambda,

            // Confidence parameter
            C = options.C,

            // Loss function one-class recommendation
            LossFunction = MatrixFactorizationTrainer.LossFunctionType.SquareLossOneClass
        };

        // Pipeline encode key + train matrix factorization
        var pipeline = mlContext.Transforms.Conversion
            .MapValueToKey("UserIdEncoded", nameof(RecommendationMlTrainingRow.UserId))
            .Append(mlContext.Transforms.Conversion.MapValueToKey("ProductIdEncoded", nameof(RecommendationMlTrainingRow.ProductId)))
            .Append(mlContext.Recommendation().Trainers.MatrixFactorization(trainerOptions));

        // Train model
        var model = pipeline.Fit(trainingData);

        // Save model artifact nếu bật config
        var modelPath = options.PersistModelArtifact
            ? SaveModelArtifact(mlContext, model, trainingData.Schema, options.ModelOutputPath)
            : null;

        // Tạo prediction engine
        var predictionEngine = mlContext.Model.CreatePredictionEngine<RecommendationMlTrainingRow, RecommendationMlPrediction>(model);

        // Lookup interaction để map metadata
        var interactionLookup = interactions.ToDictionary(item => (item.UserId, item.ProductId));

        // Map các product đã mua theo user
        var purchasedProductsByUser = interactions
            .Where(item => item.PurchaseCount > 0)
            .GroupBy(item => item.UserId)
            .ToDictionary(
                group => group.Key,
                group => group.Select(item => item.ProductId).ToHashSet());

        // Danh sách score materialized
        var materializedRows = new List<RecommendationUserProductScore>();

        // Chuẩn hóa top N
        var normalizedTopN = Math.Clamp(options.TopNPerUser, 1, 500);

        // Loop từng user
        foreach (var userId in selectedUserIds.OrderBy(id => id))
        {
            // Product user đã mua
            var purchasedProducts = purchasedProductsByUser.TryGetValue(userId, out var products)
                ? products
                : new HashSet<int>();

            // Danh sách prediction
            var predictions = new List<RecommendationMlScoredProduct>(candidateProductIds.Length);

            // Predict từng product
            foreach (var productId in candidateProductIds)
            {
                // Skip nếu exclude purchased
                if (options.ExcludePreviouslyPurchasedProducts && purchasedProducts.Contains(productId))
                {
                    continue;
                }

                // Predict score
                var prediction = predictionEngine.Predict(new RecommendationMlTrainingRow
                {
                    UserId = ToKey(userId),
                    ProductId = ToKey(productId)
                });

                // Kiểm tra score hợp lệ
                if (float.IsFinite(prediction.Score))
                {
                    predictions.Add(new RecommendationMlScoredProduct(productId, prediction.Score));
                }
            }

            // Rank prediction
            var rankedPredictions = predictions
                .OrderByDescending(item => item.Score)
                .ThenBy(item => item.ProductId)
                .Take(normalizedTopN)
                .ToArray();

            // Không có prediction => continue
            if (rankedPredictions.Length == 0)
            {
                continue;
            }

            // Tìm min/max score để normalize
            var minScore = rankedPredictions.Min(item => item.Score);
            var maxScore = rankedPredictions.Max(item => item.Score);

            // Loop từng prediction ranked
            for (var index = 0; index < rankedPredictions.Length; index++)
            {
                var item = rankedPredictions[index];

                // Tìm interaction cũ
                interactionLookup.TryGetValue((userId, item.ProductId), out var observed);

                // Add score row
                materializedRows.Add(new RecommendationUserProductScore
                {
                    UserId = userId,
                    ProductId = item.ProductId,

                    // Metadata interaction
                    ViewCount = observed?.ViewCount ?? 0,
                    SearchClickCount = observed?.SearchClickCount ?? 0,
                    RecommendationClickCount = observed?.RecommendationClickCount ?? 0,
                    PurchaseCount = observed?.PurchaseCount ?? 0,

                    // Normalize score
                    UserProductScore = NormalizeModelScore(
                        item.Score,
                        minScore,
                        maxScore,
                        index + 1,
                        rankedPredictions.Length,
                        options),

                    // Last interaction
                    LastInteractedAtUtc = observed?.LastInteractedAtUtc,

                    // Thời gian compute
                    ComputedAt = computedAt
                });
            }
        }

        // Nếu cần ghi DB
        if (shouldMaterialize)
        {
            // Nếu DB relational => dùng transaction
            if (_db.Database.IsRelational())
            {
                await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

                // Materialize score
                await MaterializeUserProductScoresAsync(materializedRows, cancellationToken);

                // Commit
                await transaction.CommitAsync(cancellationToken);
            }
            else
            {
                // DB non-relational
                await MaterializeUserProductScoresAsync(materializedRows, cancellationToken);
            }
        }

        // Log kết quả train
        _logger.LogInformation(
            "Da rebuild ML.NET recommendation user-product scores. TrainingRows={TrainingRows}, Users={Users}, Products={Products}, Predictions={Predictions}, ModelPath={ModelPath}",
            trainingRows.Count,
            selectedUserIds.Count,
            candidateProductIds.Length,
            materializedRows.Count,
            modelPath);

        // Return result
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

    // Build interaction dataset từ hành vi user
    private async Task<List<RecommendationMlInteraction>> BuildInteractionDatasetAsync(
        RecommendationMlOptions options,
        DateTime computedAt,
        CancellationToken cancellationToken)
    {
        // Khoảng thời gian lookback
        var lookbackFromUtc = computedAt.AddDays(-Math.Clamp(options.LookbackDays, 1, 3650));

        // Dictionary interaction
        var interactions = new Dictionary<(int UserId, int ProductId), RecommendationMlInteraction>();

        // Hàm update interaction
        void Update(
            int userId,
            int productId,
            DateTime lastInteractedAtUtc,
            Action<RecommendationMlInteraction> apply)
        {
            // Validate
            if (userId <= 0 || productId <= 0)
            {
                return;
            }

            // Key composite
            var key = (userId, productId);

            // Nếu chưa có interaction => tạo mới
            if (!interactions.TryGetValue(key, out var interaction))
            {
                interaction = new RecommendationMlInteraction
                {
                    UserId = userId,
                    ProductId = productId
                };

                interactions[key] = interaction;
            }

            // Update last interaction
            interaction.LastInteractedAtUtc = MaxUtc(interaction.LastInteractedAtUtc, lastInteractedAtUtc);

            // Apply custom update
            apply(interaction);
        }

        // ================= VIEW SIGNAL =================

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

        // Update interaction từ view
        foreach (var signal in viewSignals)
        {
            Update(signal.UserId, signal.ProductId, signal.LastInteractedAtUtc, item =>
            {
                item.ViewCount += signal.Count;

                // Label weighted + decay
                item.Label += signal.Count
                    * options.ViewWeight
                    * (float)CalculatePositiveSignalDecay(signal.LastInteractedAtUtc, computedAt);
            });
        }

        // ================= SEARCH CLICK SIGNAL =================

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

        // Update interaction từ search click
        foreach (var signal in searchClickSignals)
        {
            Update(signal.UserId, signal.ProductId, signal.LastInteractedAtUtc, item =>
            {
                item.SearchClickCount += signal.Count;

                // Label weighted + decay
                item.Label += signal.Count
                    * options.SearchClickWeight
                    * (float)CalculatePositiveSignalDecay(signal.LastInteractedAtUtc, computedAt);
            });
        }

        // ================= RECOMMENDATION CLICK SIGNAL =================

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

        // Update interaction từ recommendation click
        foreach (var signal in recommendationClickSignals)
        {
            Update(signal.UserId, signal.ProductId, signal.LastInteractedAtUtc, item =>
            {
                item.RecommendationClickCount += signal.Count;

                // Label weighted + decay
                item.Label += signal.Count
                    * options.RecommendationClickWeight
                    * (float)CalculatePositiveSignalDecay(signal.LastInteractedAtUtc, computedAt);
            });
        }

        // ================= PURCHASE SIGNAL =================

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

                    // Tổng quantity purchased
                    Count = grouped.Sum(item => item.detail.Quantity),

                    // Last purchase
                    LastInteractedAtUtc = grouped.Max(item => item.order.OrderDate)
                })
            .ToListAsync(cancellationToken);

        // Update interaction từ purchase
        foreach (var signal in purchaseSignals)
        {
            Update(signal.UserId, signal.ProductId, signal.LastInteractedAtUtc, item =>
            {
                item.PurchaseCount += signal.Count;

                // Purchase weight mạnh nhất
                item.Label += signal.Count
                    * options.PurchaseWeight
                    * (float)CalculatePositiveSignalDecay(signal.LastInteractedAtUtc, computedAt);
            });
        }

        // ================= NEGATIVE FEEDBACK =================

        var negativeFeedbackScores = await RecommendationNegativeFeedbackScoring.LoadUserProductScoreMapAsync(
            _db,
            lookbackFromUtc,
            computedAt,
            computedAt,
            cancellationToken);

        // Loop negative feedback
        foreach (var (key, negativeFeedback) in negativeFeedbackScores)
        {
            // Không đủ impression => bỏ qua
            if (negativeFeedback.ImpressionNoClickCount < RecommendationNegativeFeedbackScoring.MinimumImpressionBucketsForPenalty)
            {
                continue;
            }

            // Nếu interaction đã tồn tại
            if (interactions.TryGetValue(key, out var interaction))
            {
                // Giảm label nhưng không xuống dưới WeakNegativeLabel
                interaction.Label = Math.Max(WeakNegativeLabel, interaction.Label + (float)negativeFeedback.PenaltyScore);

                // Update last interaction
                interaction.LastInteractedAtUtc = MaxUtc(interaction.LastInteractedAtUtc, negativeFeedback.LastSignalAtUtc);

                continue;
            }

            // Nếu chưa tồn tại => tạo negative interaction yếu
            interactions[key] = new RecommendationMlInteraction
            {
                UserId = key.UserId,
                ProductId = key.ProductId,
                Label = WeakNegativeLabel,
                LastInteractedAtUtc = negativeFeedback.LastSignalAtUtc
            };
        }

        // Return interaction hợp lệ
        return interactions.Values
            .Where(item => item.Label > 0f)
            .OrderByDescending(item => item.Label)
            .ThenBy(item => item.UserId)
            .ThenBy(item => item.ProductId)
            .ToList();
    }

    // Ghi recommendation score xuống DB
    private async Task MaterializeUserProductScoresAsync(
        IReadOnlyCollection<RecommendationUserProductScore> rows,
        CancellationToken cancellationToken)
    {
        // Xóa dữ liệu cũ
        _db.RecommendationUserProductScores.RemoveRange(_db.RecommendationUserProductScores);

        // Save delete
        await _db.SaveChangesAsync(cancellationToken);

        // Insert dữ liệu mới
        await _db.RecommendationUserProductScores.AddRangeAsync(rows, cancellationToken);

        // Save insert
        await _db.SaveChangesAsync(cancellationToken);
    }

    // Save model artifact xuống file
    private string? SaveModelArtifact(MLContext mlContext, ITransformer model, DataViewSchema schema, string configuredPath)
    {
        // Resolve path
        var modelPath = ResolveModelPath(configuredPath);

        // Tạo directory nếu chưa tồn tại
        Directory.CreateDirectory(Path.GetDirectoryName(modelPath)!);

        // Save model
        mlContext.Model.Save(model, schema, modelPath);

        return modelPath;
    }

    // Resolve đường dẫn model
    private string ResolveModelPath(string configuredPath)
    {
        var path = string.IsNullOrWhiteSpace(configuredPath)
            ? "App_Data/recommendation-user-product-ml.zip"
            : configuredPath.Trim();

        return Path.IsPathRooted(path)
            ? path
            : Path.GetFullPath(Path.Combine(_hostEnvironment.ContentRootPath, path));
    }

    // Normalize model score về khoảng floor-ceiling
    private static double NormalizeModelScore(
        float score,
        float minScore,
        float maxScore,
        int rank,
        int count,
        RecommendationMlOptions options)
    {
        // Xác định floor/ceiling
        var floor = Math.Min(options.NormalizedScoreFloor, options.NormalizedScoreCeiling);
        var ceiling = Math.Max(options.NormalizedScoreFloor, options.NormalizedScoreCeiling);

        // Normalize theo min-max
        if (maxScore > minScore)
        {
            var normalized = floor + ((score - minScore) / (maxScore - minScore) * (ceiling - floor));

            return Math.Round(Math.Clamp(normalized, floor, ceiling), 2);
        }

        // Nếu chỉ có 1 item
        if (count <= 1)
        {
            return ceiling;
        }

        // Normalize theo rank
        var rankScore = ceiling - ((rank - 1d) * (ceiling - floor) / (count - 1d));

        return Math.Round(Math.Clamp(rankScore, floor, ceiling), 2);
    }

    // Lấy DateTime lớn hơn
    private static DateTime? MaxUtc(DateTime? current, DateTime candidate)
        => !current.HasValue || candidate > current.Value
            ? candidate
            : current;

    // Overload nullable DateTime
    private static DateTime? MaxUtc(DateTime? current, DateTime? candidate)
        => candidate.HasValue
            ? MaxUtc(current, candidate.Value)
            : current;

    // Tính decay score theo thời gian
    private static double CalculatePositiveSignalDecay(DateTime signalAtUtc, DateTime computedAtUtc)
    {
        // Số ngày đã trôi qua
        var ageDays = Math.Max(0d, (computedAtUtc - signalAtUtc).TotalDays);

        // Exponential decay
        return Math.Pow(0.5d, ageDays / PositiveSignalHalfLifeDays);
    }

    // Convert int id thành string invariant
    private static string ToKey(int id)
        => id.ToString(CultureInfo.InvariantCulture);

    // Row training cho ML.NET
    private sealed class RecommendationMlTrainingRow
    {
        // User key
        public string UserId { get; set; } = string.Empty;

        // Product key
        public string ProductId { get; set; } = string.Empty;

        // Interaction label
        public float Label { get; set; }
    }

    // Prediction output
    private sealed class RecommendationMlPrediction
    {
        // Predicted score
        public float Score { get; set; }
    }

    // Interaction aggregate user-product
    private sealed class RecommendationMlInteraction
    {
        // User id
        public int UserId { get; set; }

        // Product id
        public int ProductId { get; set; }

        // Tổng view
        public int ViewCount { get; set; }

        // Tổng search click
        public int SearchClickCount { get; set; }

        // Tổng recommendation click
        public int RecommendationClickCount { get; set; }

        // Tổng purchase
        public int PurchaseCount { get; set; }

        // Label final
        public float Label { get; set; }

        // Last interaction
        public DateTime? LastInteractedAtUtc { get; set; }
    }

    // Record product prediction score
    private sealed record RecommendationMlScoredProduct(int ProductId, float Score);
}

// Kết quả rebuild recommendation
public sealed class RecommendationMlRefreshResult
{
    // Train thành công hay không
    public bool Succeeded { get; set; }

    // Có bị skip hay không
    public bool Skipped { get; set; }

    // Lý do skip
    public string? SkipReason { get; set; }

    // Tên thuật toán
    public string Algorithm { get; set; } = "mlnet_matrix_factorization_v1";

    // Tổng interaction row
    public int InteractionRowCount { get; set; }

    // Tổng training row
    public int TrainingRowCount { get; set; }

    // Tổng distinct user
    public int DistinctUserCount { get; set; }

    // Tổng distinct product
    public int DistinctProductCount { get; set; }

    // Tổng selected user
    public int SelectedUserCount { get; set; }

    // Tổng candidate product
    public int CandidateProductCount { get; set; }

    // Tổng row đã materialize
    public int MaterializedUserProductScoreRowCount { get; set; }

    // Tổng preview row
    public int PreviewPredictionRowCount { get; set; }

    // Thời gian compute
    public DateTime? ComputedAtUtc { get; set; }

    // Đường dẫn model
    public string? ModelPath { get; set; }

    // Factory method tạo skipped result
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
