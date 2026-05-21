using MDWAPI.Data;
using Microsoft.Extensions.Logging;

namespace MDWAPI.Services;

/// <summary>
/// Internal authentication provider for background jobs.
/// Generates tokens directly using TokenService without HTTP calls.
/// </summary>
public class InternalAuthTokenProvider : IAuthTokenProvider
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _cfg;
    private readonly ILogger<InternalAuthTokenProvider> _logger;

    private string? _token;
    private DateTime _expiresAt;

    public InternalAuthTokenProvider(
        IServiceScopeFactory scopeFactory,
        IConfiguration cfg,
        ILogger<InternalAuthTokenProvider> logger)
    {
        _scopeFactory = scopeFactory;
        _cfg = cfg;
        _logger = logger;
    }

    public async Task<string?> GetBearerAsync(CancellationToken ct)
    {
        // Check if cached token is still valid (with 2-minute skew)
        var skew = TimeSpan.FromMinutes(2);
        if (!string.IsNullOrEmpty(_token) && DateTime.Now < _expiresAt - skew)
        {
            _logger.LogDebug("Using cached internal token (expires at {exp})", _expiresAt);
            return _token;
        }

        // Generate new token using TokenService directly
        using var scope = _scopeFactory.CreateScope();
        var tokenService = scope.ServiceProvider.GetRequiredService<TokenService>();

        var username = _cfg["Jobs:Auth:Username"] ?? "adminJob" ;
        var password = _cfg["Jobs:Auth:Password"] ?? "JobAdmin";
        var tokenTtl = _cfg.GetValue<int?>("Auth:TokenLifetimeMinutes") ?? 120;

        try
        {
            _token = await tokenService.IssueAsync(username, password, TimeSpan.FromMinutes(tokenTtl));
            _expiresAt = DateTime.Now.AddMinutes(tokenTtl);
            _logger.LogInformation("Internal token generated successfully, expires at {exp}", _expiresAt);
            return _token;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate internal token for user {username}", username);
            return null;
        }
    }
}
