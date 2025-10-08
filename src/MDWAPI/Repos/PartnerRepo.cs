using Dapper;
using MDWAPI.Data;
using MDWAPI.Dtos;
using Microsoft.EntityFrameworkCore;
using System.Data;

namespace MDWAPI.Repos
{
    public class PartnerRepo : IPartnerRepo
    {
        private readonly AppDbContext _db;
        public PartnerRepo(AppDbContext db) => _db = db;

        public async Task<PartnerConfigDtos?> GetConfigByPartnersIdAsync(int partnersId, CancellationToken ct)
        {
            const string sql = @"
                                SELECT 
                                    Id, 
                                    Name, 
                                    CompanysId,
                                    PartnerId,
                                    PartnerKey, 
                                    Environment
                                FROM mdw.Partners
                                WHERE Id = @partnersId;";

            var conn = _db.Database.GetDbConnection();
            var needClose = conn.State != ConnectionState.Open;
            if (needClose) await conn.OpenAsync(ct);

            try
            {
                return await conn.QuerySingleOrDefaultAsync<PartnerConfigDtos>(
                    new CommandDefinition(sql, new { partnersId }, cancellationToken: ct));
            }
            finally
            {
                if (needClose) await conn.CloseAsync();
            }
        }
    }
}
