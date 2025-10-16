// MDWAPI/Repos/ChannelTokenRepo.cs
using Dapper;
using MDWAPI.Data;
using MDWAPI.Dtos;
using MDWAPI.Models;
using MDWAPI.Repos;
using Microsoft.EntityFrameworkCore;
using System;
using System.Data;
using System.Threading.Channels;

public class ChannelTokenRepo : IChannelTokenRepo
{
    private readonly AppDbContext _db;
    public ChannelTokenRepo(AppDbContext db) => _db = db;

    private const string BaseColumns = @"
        Id, Channel, Environment, AuthType,
        PartnerId, AppKey,
        AccountIdBig, AccountIdStr,
        AccessToken, RefreshToken,
        AccessTokenExpAt, RefreshTokenExpAt,
        Scope, Country, Region,
        CompanysId, PartnersId,
        TokenPayloadJson, ExtraJson,
        isActive";

    public async Task<ChannelTokenDtos?> GetValidAsync(
        string channel,
        string environment,
        long? partnerId,
        string? appKey,
        long? accountIdBig,
        string? accountIdStr,
        CancellationToken ct)
    {
        var sql = $@"
SELECT TOP(1) {BaseColumns}
FROM mdw.ChannelTokens
WHERE Channel=@channel
  AND Environment=@env
  AND isActive=1
  AND AccessTokenExpAt > SYSUTCDATETIME()
  AND (@partnerId IS NULL OR PartnerId=@partnerId)
  AND (@appKey    IS NULL OR AppKey=@appKey)
  AND (
       (@accountIdBig IS NOT NULL AND AccountIdBig=@accountIdBig) OR
       (@accountIdStr IS NOT NULL AND AccountIdStr=@accountIdStr)
  )
ORDER BY AccessTokenExpAt DESC, Id DESC;";

        var conn = _db.Database.GetDbConnection();
        var needClose = conn.State != ConnectionState.Open;
        if (needClose) await conn.OpenAsync(ct);
        try
        {
            return await conn.QuerySingleOrDefaultAsync<ChannelTokenDtos>(
                new CommandDefinition(sql,
                    new { channel, env = environment, partnerId, appKey, accountIdBig, accountIdStr },
                    cancellationToken: ct));
        }
        finally { if (needClose) await conn.CloseAsync(); }
    }

    public async Task<ChannelTokenDtos?> GetLatestForTikTokShopAsync(string shopId, CancellationToken ct)
    {
        const string sql = @"
SELECT TOP (1) *
FROM imw.mdw.ChannelTokens WITH (NOLOCK)
WHERE Channel = 'tiktok'
  AND (AccountIdStr = @shopId OR AccountIdBig = TRY_CAST(@shopId AS BIGINT)
       OR AccountIdStrNorm = @shopId OR AccountIdBigStr = @shopId)
  AND (isActive = 1 OR isActive IS NULL)
ORDER BY UpdatedAt DESC, CreatedAt DESC;";

        var conn = _db.Database.GetDbConnection();
        var needClose = conn.State != ConnectionState.Open;
        if (needClose) await conn.OpenAsync(ct);
        try
        {
            return await conn.QueryFirstOrDefaultAsync<ChannelTokenDtos>(sql, new { shopId });
        }
        finally { if (needClose) await conn.CloseAsync(); }

    }

    public async Task<ChannelTokenDtos?> GetLatestForRefreshAsync(
        string channel, string environment, long? partnerId, long accountIdBig, CancellationToken ct)
    {
        var sql = $@"
SELECT TOP(1) {BaseColumns}
FROM mdw.ChannelTokens
WHERE Channel=@channel
  AND Environment=@env
  AND isActive=1
  AND (@partnerId IS NULL OR PartnerId=@partnerId)
  AND AccountIdBig=@accountIdBig
  AND RefreshToken IS NOT NULL AND LEN(RefreshToken) > 0
ORDER BY UpdatedAt DESC, Id DESC;";

        var conn = _db.Database.GetDbConnection();
        var needClose = conn.State != ConnectionState.Open;
        if (needClose) await conn.OpenAsync(ct);
        try
        {
            return await conn.QuerySingleOrDefaultAsync<ChannelTokenDtos>(
                new CommandDefinition(sql,
                    new { channel, env = environment, partnerId, accountIdBig },
                    cancellationToken: ct));
        }
        finally { if (needClose) await conn.CloseAsync(); }
    }

    // ✅ ใหม่: สำหรับ TikTok ที่เก็บ AccountIdStr (shop_id) และเราอยากได้แถว “ล่าสุดที่มี refresh_token”
    public async Task<ChannelTokenDtos?> GetLatestForRefreshByStrAsync(
        string channel,
        string environment,
        string? appKey,
        string accountIdStr,
        CancellationToken ct)
    {
        var sql = $@"
SELECT TOP(1) {BaseColumns}
FROM mdw.ChannelTokens
WHERE Channel=@channel
  AND Environment=@env
  AND isActive=1
  AND (@appKey IS NULL OR AppKey=@appKey)
  AND AccountIdStr=@accountIdStr
  AND RefreshToken IS NOT NULL AND LEN(RefreshToken) > 0
ORDER BY UpdatedAt DESC, Id DESC;";

        var conn = _db.Database.GetDbConnection();
        var needClose = conn.State != ConnectionState.Open;
        if (needClose) await conn.OpenAsync(ct);
        try
        {
            return await conn.QuerySingleOrDefaultAsync<ChannelTokenDtos>(
                new CommandDefinition(sql,
                    new { channel, env = environment, appKey, accountIdStr },
                    cancellationToken: ct));
        }
        finally { if (needClose) await conn.CloseAsync(); }
    }

    public async Task UpsertAsync(ChannelTokenDtos row, CancellationToken ct)
    {
        const string upsert = @"
MERGE mdw.ChannelTokens AS t
USING (
    SELECT
        @Channel AS Channel, @Environment AS Environment,
        ISNULL(@PartnerId, -1)       AS PartnerIdKey,
        ISNULL(@AppKey,   N'#')      AS AppKeyKey,
        ISNULL(@AccountIdBig, -1)    AS AccountIdBigKey,
        ISNULL(@AccountIdStr, N'#')  AS AccountIdStrKey
) AS s
ON (
    t.Channel = s.Channel
    AND t.Environment = s.Environment
    AND t.isActive = 1
    AND ISNULL(t.PartnerId,   -1) = s.PartnerIdKey
    AND ISNULL(t.AppKey,     N'#') = s.AppKeyKey
    AND ISNULL(t.AccountIdBig, -1) = s.AccountIdBigKey
    AND ISNULL(t.AccountIdStr, N'#') = s.AccountIdStrKey
)
WHEN MATCHED THEN UPDATE SET
    AuthType          = @AuthType,
    PartnerId         = @PartnerId,
    AppKey            = @AppKey,
    AccountIdBig      = @AccountIdBig,
    AccountIdStr      = @AccountIdStr,
    AccessToken       = @AccessToken,
    RefreshToken      = @RefreshToken,
    AccessTokenExpAt  = @AccessTokenExpAt,
    RefreshTokenExpAt = @RefreshTokenExpAt,
    Scope             = @Scope,
    Country           = @Country,
    Region            = @Region,
    CompanysId        = @CompanysId,
    PartnersId        = @PartnersId,
    TokenPayloadJson  = @TokenPayloadJson,
    ExtraJson         = @ExtraJson,
    UpdatedAt         = SYSUTCDATETIME(),
    isActive          = 1
WHEN NOT MATCHED THEN INSERT
(
    Channel, Environment, AuthType, PartnerId, AppKey,
    AccountIdBig, AccountIdStr, AccessToken, RefreshToken,
    AccessTokenExpAt, RefreshTokenExpAt, Scope, Country, Region,
    CompanysId, PartnersId, TokenPayloadJson, ExtraJson, isActive, CreatedAt
)
VALUES
(
    @Channel, @Environment, @AuthType, @PartnerId, @AppKey,
    @AccountIdBig, @AccountIdStr, @AccessToken, @RefreshToken,
    @AccessTokenExpAt, @RefreshTokenExpAt, @Scope, @Country, @Region,
    @CompanysId, @PartnersId, @TokenPayloadJson, @ExtraJson, 1, SYSUTCDATETIME()
);";

        var conn = _db.Database.GetDbConnection();
        var needClose = conn.State != ConnectionState.Open;
        if (needClose) await conn.OpenAsync(ct);
        try
        {
            await conn.ExecuteAsync(
                new CommandDefinition(
                    upsert,
                    new
                    {
                        row.Channel,
                        row.Environment,
                        row.AuthType,
                        row.PartnerId,
                        row.AppKey,
                        row.AccountIdBig,
                        row.AccountIdStr,
                        row.AccessToken,
                        row.RefreshToken,
                        row.AccessTokenExpAt,
                        row.RefreshTokenExpAt,
                        row.Scope,
                        row.Country,
                        row.Region,
                        row.CompanysId,
                        row.PartnersId,
                        row.TokenPayloadJson,
                        row.ExtraJson
                    },
                    cancellationToken: ct));
        }
        finally { if (needClose) await conn.CloseAsync(); }
    }
}
