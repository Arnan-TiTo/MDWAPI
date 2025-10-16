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

        // ✅ ใหม่: สำหรับแพลตฟอร์มที่ใช้ accountIdStr (เช่น TikTok)
        Task<ChannelTokenDtos?> GetLatestForRefreshByStrAsync(
            string channel,
            string environment,
            string? appKey,
            string accountIdStr,
            CancellationToken ct);

        Task UpsertAsync(ChannelTokenDtos row, CancellationToken ct);

        Task<ChannelTokenDtos?> GetLatestForTikTokShopAsync(string shopId, CancellationToken ct);
    }
}
