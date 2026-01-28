using MDWAPI.Data;
using MDWAPI.Models;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;

namespace MDWAPI.Services;

public class TokenService
{
    private readonly AppDbContext _db;
    public TokenService(AppDbContext db) => _db = db;

    public async Task<string> IssueAsync(string username, string password, TimeSpan? life = null)
    {
        var user = await _db.Users.SingleOrDefaultAsync(u => u.Username == username);
        if (user == null || !BCrypt.Net.BCrypt.Verify(password, user.PasswordHash))
            throw new InvalidOperationException("Invalid credentials");

        var token = GenerateToken();
        var now = DateTime.Now;
        var tok = new UserToken
        {
            UserId = user.Id,
            Token = token,
            IssuedAt = now,
            ExpiresAt = now.Add(life ?? TimeSpan.FromHours(8)),
        };
        _db.UserTokens.Add(tok);
        await _db.SaveChangesAsync();
        return token;
    }

    public async Task RevokeAsync(string token)
    {
        var t = await _db.UserTokens.SingleOrDefaultAsync(x => x.Token == token);
        if (t != null)
        {
            t.RevokedAt = DateTime.Now;
            await _db.SaveChangesAsync();
        }
    }

    private static string GenerateToken(int bytes = 32)
    {
        var b = RandomNumberGenerator.GetBytes(bytes);
        return Convert.ToBase64String(b).Replace("+", "-").Replace("/", "_").Replace("=", "");
    }
}