namespace MDWAPI.Repos
{
    public interface IShopRepo
    {
        Task<(int partnersId, long? accountIdBig, string? accountIdStr)> GetShopBindingAsync(
            long inputShopId,
            CancellationToken ct);
    }
}