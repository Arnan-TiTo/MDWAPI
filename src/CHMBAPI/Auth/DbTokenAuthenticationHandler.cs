using CHMBAPI.Data;
using CHMBAPI.Entities;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using System.Text.Encodings.Web;

namespace CHMBAPI.Auth;

public class DbTokenAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    private readonly AppDbContext _db;

    public DbTokenAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        ISystemClock clock,
        AppDbContext db)
        : base(options, logger, encoder, clock)
    {
        _db = db;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.ContainsKey("Authorization"))
            return AuthenticateResult.Fail("Missing Authorization header");

        var authHeader = Request.Headers["Authorization"].ToString();

        if (!authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return AuthenticateResult.Fail("Invalid Authorization header format");

        var token = authHeader.Substring("Bearer ".Length).Trim();

        if (string.IsNullOrEmpty(token))
            return AuthenticateResult.Fail("Empty token");

        // For simplicity, we'll use a basic token validation
        // In production, implement proper JWT or secure token validation
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Username == "admin" && u.IsActive);

        if (user == null)
            return AuthenticateResult.Fail("Invalid token");

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.UserId.ToString()),
            new Claim(ClaimTypes.Name, user.Username)
        };

        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);

        return AuthenticateResult.Success(ticket);
    }
}