using MDWAPI.Data;
using Microsoft.EntityFrameworkCore;

namespace MDWAPI.Services;

public class EfAccessTokenProvider : IAccessTokenProvider
{
    private readonly AppDbContext _db;
    public EfAccessTokenProvider(AppDbContext db) => _db = db;

    public async Task<string> GetValidAccessTokenAsync(long shopId, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow.AddMinutes(1);
        var tok = await _db.MkpTokens
            .Where(t => t.ShopId == shopId && t.ExpiresAtUtc > now)
            .OrderByDescending(t => t.ExpiresAtUtc)
            .FirstOrDefaultAsync(ct);

        if (tok is null)
            throw new InvalidOperationException($"No valid access token for shop {shopId}.");

        return tok.AccessToken;
    }
}
