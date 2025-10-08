using MDWAPI.Repos;

namespace MDWAPI.Services;

public class ChannelTokenResolver
{
    private readonly IConfiguration _cfg;
    private readonly IChannelTokenRepo _repo;
    private readonly ILogger<ChannelTokenResolver> _log;

    public ChannelTokenResolver(IConfiguration cfg, IChannelTokenRepo repo, ILogger<ChannelTokenResolver> log)
    {
        _cfg = cfg;
        _repo = repo;
        _log = log;
    }

    public string HostFor(string channel, string environment) => (channel.ToLowerInvariant(), environment.ToLowerInvariant()) switch
    {
        ("shopee", "sandbox") => "https://openplatform.sandbox.test-stable.shopee.sg",
        ("shopee", _) => "https://partner.shopeemobile.com",

        ("lazada", "sandbox") => "https://api.lazada.test",
        ("lazada", _) => "https://api.lazada.com",

        ("tiktok", "sandbox") => "https://sandbox-open-api.tiktokglobalshop.com",
        ("tiktok", _) => "https://open-api.tiktokglobalshop.com",

        _ => throw new InvalidOperationException($"Unknown channel/env: {channel}/{environment}")
    };

    /// <summary>
    /// ดึง access token ที่ยังไม่หมดอายุจาก ChannelTokens
    /// </summary>
    public async Task<(string accessToken, string environment, long? partnerId, string? appKey)> GetAccessTokenAsync(
        string channel,
        string environment,
        long? partnerId,
        string? appKey,
        long? accountIdBig,
        string? accountIdStr,
        CancellationToken ct)
    {
        var row = await _repo.GetValidAsync(channel, environment, partnerId, appKey, accountIdBig, accountIdStr, ct);
        if (row is null)
            throw new InvalidOperationException($"No valid token for {channel}/{environment} (accountIdBig={accountIdBig}, accountIdStr={accountIdStr}).");

        return (row.AccessToken, row.Environment, row.PartnerId, row.AppKey);
    }
}
