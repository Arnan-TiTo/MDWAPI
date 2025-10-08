// MDWAPI/Repos/IChannelTokenRepo.cs
using MDWAPI.Dtos;

namespace MDWAPI.Repos
{
    public interface IChannelTokenRepo
    {
        Task<ChannelTokenDtos?> GetValidAsync(
            string channel,
            string environment,
            long? partnerId,
            string? appKey,
            long? accountIdBig,
            string? accountIdStr,
            CancellationToken ct);

        Task<ChannelTokenDtos?> GetLatestForRefreshAsync(
            string channel,
            string environment,
            long? partnerId,
            long accountIdBig,
            CancellationToken ct);

        Task UpsertAsync(ChannelTokenDtos row, CancellationToken ct);
    }
}
