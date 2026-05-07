namespace FreshFarm.Web.Bff.Services;

public interface ISessionSignalService
{
    Task TrackSearchAsync(
        int userId,
        string keyword,
        CancellationToken cancellationToken = default);

    Task TrackClickAsync(
        int userId,
        int productId,
        int sellerId,
        string? categoryName,
        CancellationToken cancellationToken = default);
}
