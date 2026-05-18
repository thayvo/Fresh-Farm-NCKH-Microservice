using System.Threading.Channels;

namespace FreshFarm.Ordering.Api.Services;

public sealed class RecommendationAffinityRefreshSignal
{
    private readonly Channel<bool> _signalChannel = Channel.CreateUnbounded<bool>(
        new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });
    private int _requestCount;

    public int RequestCount => Volatile.Read(ref _requestCount);

    public void RequestRefresh()
    {
        Interlocked.Increment(ref _requestCount);
        _signalChannel.Writer.TryWrite(true);
    }

    public async Task<bool> WaitForRefreshSignalAsync(TimeSpan pollInterval, CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(pollInterval);

        try
        {
            await _signalChannel.Reader.ReadAsync(timeoutCts.Token);
            while (_signalChannel.Reader.TryRead(out _))
            {
            }

            return true;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }
}
