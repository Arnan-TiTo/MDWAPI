// MDWAPI/Repos/PartnerRepo.cs
using Dapper;
using MDWAPI.Data;
using MDWAPI.Dtos;
using MDWAPI.Repos;
using Microsoft.EntityFrameworkCore;
using System.Data;

public class PartnerRepo : IPartnerRepo
{
    private readonly AppDbContext _db;
    public PartnerRepo(AppDbContext db) => _db = db;

    public async Task<PartnerConfigDtos?> GetConfigByPartnersIdAsync(int partnersId, CancellationToken ct)
    {
        const string sql = @"
                            SELECT TOP(1)
                                Id,
                                PartnerId,
                                Environment,
                                PartnerKey,     -- AppSecret ของ TikTok
                                AppKey          -- client_key ของ TikTok
                            FROM mdw.Partners
                            WHERE Id = @partnersId;";

        var conn = _db.Database.GetDbConnection();
        var needOpen = conn.State != ConnectionState.Open;
        if (needOpen) await conn.OpenAsync(ct);
        try
        {
            return await conn.QuerySingleOrDefaultAsync<PartnerConfigDtos>(
                new CommandDefinition(sql, new { partnersId }, cancellationToken: ct));
        }
        finally { if (needOpen) await conn.CloseAsync(); }
    }
}
