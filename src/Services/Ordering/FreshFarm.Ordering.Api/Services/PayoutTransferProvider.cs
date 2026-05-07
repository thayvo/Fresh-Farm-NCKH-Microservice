using FreshFarm.Ordering.Api.Models;

namespace FreshFarm.Ordering.Api.Services;

public sealed record PayoutTransferResult(bool Success, string ReferenceCode, string Message);

public interface IPayoutTransferProvider
{
    Task<PayoutTransferResult> ReleaseAsync(Payout payout, string? note, CancellationToken cancellationToken);
}

public sealed class ManualPayoutTransferProvider : IPayoutTransferProvider
{
    public Task<PayoutTransferResult> ReleaseAsync(Payout payout, string? note, CancellationToken cancellationToken)
    {
        return Task.FromResult(new PayoutTransferResult(
            true,
            $"manual-payout-{payout.PayoutId}",
            "manual_payout_recorded"));
    }
}
