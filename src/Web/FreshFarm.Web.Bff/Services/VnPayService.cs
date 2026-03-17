using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using FreshFarm.Web.Bff.Options;
using Microsoft.Extensions.Options;

namespace FreshFarm.Web.Bff.Services;

public sealed class VnPayService : IVnPayService
{
    private static readonly TimeZoneInfo VnPayTimeZone = ResolveVnPayTimeZone();
    private readonly VnPayOptions _options;

    public VnPayService(IOptions<VnPayOptions> options)
    {
        _options = options.Value;
    }

    public bool IsConfigured => _options.IsConfigured;

    public string CreatePaymentUrl(VnPayCreatePaymentRequest request)
    {
        if (!IsConfigured)
        {
            throw new InvalidOperationException("VNPay sandbox chưa được cấu hình.");
        }

        var createDate = TimeZoneInfo.ConvertTime(request.CreatedAtUtc, VnPayTimeZone)
            .ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
        var expireDate = TimeZoneInfo.ConvertTime(request.ExpireAtUtc, VnPayTimeZone)
            .ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
        var amountValue = decimal.ToInt64(Math.Round(request.Amount * 100m, MidpointRounding.AwayFromZero));

        var parameters = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["vnp_Version"] = _options.Version,
            ["vnp_Command"] = _options.Command,
            ["vnp_TmnCode"] = _options.TmnCode,
            ["vnp_Amount"] = amountValue.ToString(CultureInfo.InvariantCulture),
            ["vnp_CreateDate"] = createDate,
            ["vnp_CurrCode"] = _options.CurrCode,
            ["vnp_IpAddr"] = string.IsNullOrWhiteSpace(request.IpAddress) ? "127.0.0.1" : request.IpAddress,
            ["vnp_Locale"] = _options.Locale,
            ["vnp_OrderInfo"] = request.OrderInfo,
            ["vnp_OrderType"] = _options.OrderType,
            ["vnp_ReturnUrl"] = _options.ReturnUrl,
            ["vnp_TxnRef"] = request.TxnRef
        };

        if (!string.IsNullOrWhiteSpace(expireDate))
        {
            parameters["vnp_ExpireDate"] = expireDate;
        }

        if (!string.IsNullOrWhiteSpace(request.BankCode))
        {
            parameters["vnp_BankCode"] = request.BankCode;
        }

        var hashData = BuildHashData(parameters);
        var secureHash = ComputeHmacSha512(hashData, _options.HashSecret);
        var query = BuildQueryString(parameters);
        query = $"{query}&vnp_SecureHash={WebUtility.UrlEncode(secureHash)}";

        return _options.BaseUrl.Contains('?', StringComparison.Ordinal)
            ? $"{_options.BaseUrl}&{query}"
            : $"{_options.BaseUrl}?{query}";
    }

    public VnPayReturnValidationResult ValidateReturn(IQueryCollection query)
    {
        if (!IsConfigured)
        {
            return VnPayReturnValidationResult.Fail("VNPay sandbox chưa được cấu hình.");
        }

        if (query.Count == 0)
        {
            return VnPayReturnValidationResult.Fail("Không nhận được dữ liệu phản hồi từ VNPay.");
        }

        var secureHash = query["vnp_SecureHash"].ToString();
        if (string.IsNullOrWhiteSpace(secureHash))
        {
            return VnPayReturnValidationResult.Fail("Thiếu chữ ký phản hồi từ VNPay.");
        }

        var parameters = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in query)
        {
            if (string.IsNullOrWhiteSpace(pair.Key) ||
                !pair.Key.StartsWith("vnp_", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(pair.Key, "vnp_SecureHash", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(pair.Key, "vnp_SecureHashType", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var value = pair.Value.ToString();
            if (!string.IsNullOrWhiteSpace(value))
            {
                parameters[pair.Key] = value;
            }
        }

        var computedHash = ComputeHmacSha512(BuildHashData(parameters), _options.HashSecret);
        if (!string.Equals(secureHash, computedHash, StringComparison.OrdinalIgnoreCase))
        {
            return VnPayReturnValidationResult.Fail("Chữ ký phản hồi VNPay không hợp lệ.");
        }

        var responseCode = query["vnp_ResponseCode"].ToString();
        var transactionStatus = query["vnp_TransactionStatus"].ToString();
        var txnRef = query["vnp_TxnRef"].ToString();

        if (!int.TryParse(txnRef, NumberStyles.Integer, CultureInfo.InvariantCulture, out var orderId) || orderId <= 0)
        {
            return VnPayReturnValidationResult.Fail("Mã đơn hàng VNPay không hợp lệ.");
        }

        decimal? amount = null;
        var amountRaw = query["vnp_Amount"].ToString();
        if (long.TryParse(amountRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedAmount) && parsedAmount >= 0)
        {
            amount = parsedAmount / 100m;
        }

        var paidAt = ParseVnPayDate(query["vnp_PayDate"].ToString());
        var succeeded = string.Equals(responseCode, "00", StringComparison.OrdinalIgnoreCase) &&
                        (string.IsNullOrWhiteSpace(transactionStatus) || string.Equals(transactionStatus, "00", StringComparison.OrdinalIgnoreCase));

        return VnPayReturnValidationResult.Ok(
            orderId,
            txnRef,
            responseCode,
            transactionStatus,
            amount,
            query["vnp_TransactionNo"].ToString(),
            query["vnp_BankCode"].ToString(),
            query["vnp_BankTranNo"].ToString(),
            query["vnp_OrderInfo"].ToString(),
            paidAt,
            succeeded);
    }

    private static string BuildHashData(IEnumerable<KeyValuePair<string, string>> parameters)
    {
        return string.Join("&", parameters.Select(pair =>
            $"{pair.Key}={WebUtility.UrlEncode(pair.Value)}"));
    }

    private static string BuildQueryString(IEnumerable<KeyValuePair<string, string>> parameters)
    {
        return string.Join("&", parameters.Select(pair =>
            $"{WebUtility.UrlEncode(pair.Key)}={WebUtility.UrlEncode(pair.Value)}"));
    }

    private static string ComputeHmacSha512(string data, string secret)
    {
        var keyBytes = Encoding.UTF8.GetBytes(secret);
        var dataBytes = Encoding.UTF8.GetBytes(data);

        using var hmac = new HMACSHA512(keyBytes);
        var hashBytes = hmac.ComputeHash(dataBytes);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    private static DateTimeOffset? ParseVnPayDate(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        if (DateTimeOffset.TryParseExact(
            raw.Trim(),
            "yyyyMMddHHmmss",
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeLocal,
            out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static TimeZoneInfo ResolveVnPayTimeZone()
    {
        var candidateIds = new[]
        {
            "SE Asia Standard Time",
            "Asia/Ho_Chi_Minh",
            "Asia/Bangkok"
        };

        foreach (var candidateId in candidateIds)
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(candidateId);
            }
            catch (TimeZoneNotFoundException)
            {
            }
            catch (InvalidTimeZoneException)
            {
            }
        }

        return TimeZoneInfo.Utc;
    }
}

public sealed record VnPayCreatePaymentRequest(
    int OrderId,
    decimal Amount,
    string OrderInfo,
    string TxnRef,
    string IpAddress,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset ExpireAtUtc,
    string? BankCode = null);

public sealed class VnPayReturnValidationResult
{
    public bool IsValid { get; private init; }
    public bool IsSuccess { get; private init; }
    public int OrderId { get; private init; }
    public string TxnRef { get; private init; } = string.Empty;
    public string ResponseCode { get; private init; } = string.Empty;
    public string TransactionStatus { get; private init; } = string.Empty;
    public decimal? Amount { get; private init; }
    public string TransactionNo { get; private init; } = string.Empty;
    public string BankCode { get; private init; } = string.Empty;
    public string BankTransactionNo { get; private init; } = string.Empty;
    public string OrderInfo { get; private init; } = string.Empty;
    public DateTimeOffset? PaidAt { get; private init; }
    public string Message { get; private init; } = string.Empty;

    public static VnPayReturnValidationResult Ok(
        int orderId,
        string txnRef,
        string responseCode,
        string transactionStatus,
        decimal? amount,
        string transactionNo,
        string bankCode,
        string bankTransactionNo,
        string orderInfo,
        DateTimeOffset? paidAt,
        bool isSuccess)
    {
        return new VnPayReturnValidationResult
        {
            IsValid = true,
            IsSuccess = isSuccess,
            OrderId = orderId,
            TxnRef = txnRef,
            ResponseCode = responseCode,
            TransactionStatus = transactionStatus,
            Amount = amount,
            TransactionNo = transactionNo,
            BankCode = bankCode,
            BankTransactionNo = bankTransactionNo,
            OrderInfo = orderInfo,
            PaidAt = paidAt,
            Message = isSuccess
                ? "Thanh toán VNPay thành công."
                : $"Thanh toán VNPay chưa thành công (Mã phản hồi: {responseCode}/{transactionStatus})."
        };
    }

    public static VnPayReturnValidationResult Fail(string message)
    {
        return new VnPayReturnValidationResult
        {
            Message = message
        };
    }
}
