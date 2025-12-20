using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace MDWAPI.Services;

public interface IAuthTokenProvider
{
    Task<string?> GetBearerAsync(CancellationToken ct);
}

public class AuthTokenProvider : IAuthTokenProvider
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<AuthTokenProvider> _logger;
    private readonly IConfiguration _cfg;

    private string? _token;
    private DateTimeOffset _expiresAtUtc;

    public AuthTokenProvider(IHttpClientFactory httpFactory, ILogger<AuthTokenProvider> logger, IConfiguration cfg)
    {
        _httpFactory = httpFactory;
        _logger = logger;
        _cfg = cfg;
    }

    public async Task<string?> GetBearerAsync(CancellationToken ct)
    {
        var skew = TimeSpan.FromMinutes(2);
        if (!string.IsNullOrEmpty(_token) && DateTimeOffset.UtcNow < _expiresAtUtc - skew)
            return _token;

        var baseUrl = _cfg["OrdersApi:BaseUrl"] ?? "https://localhost:7192";
        var username = _cfg["Jobs:Auth:Username"] ?? "admin";
        var password = _cfg["Jobs:Auth:Password"] ?? "123456yjm";
        var tokenTtl = _cfg.GetValue<int?>("Auth:TokenLifetimeMinutes") ?? 120;

        var client = _httpFactory.CreateClient("OrdersApi"); // BaseAddress ตั้งใน Program.cs
        var payload = new { username, password };

        try
        {
            using var resp = await client.PostAsJsonAsync("/api/Auth/login", payload, ct);
            var json = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogWarning("Login failed {Status} {Base} | {Body}", (int)resp.StatusCode, baseUrl, json);
                return null;
            }

            // รองรับ payload ที่เป็น { token: "..." } หรือ string token ตรง ๆ
            string? tok = null;
            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                    doc.RootElement.TryGetProperty("token", out var t) &&
                    t.ValueKind == JsonValueKind.String)
                {
                    tok = t.GetString();
                }
                else if (doc.RootElement.ValueKind == JsonValueKind.String)
                {
                    tok = doc.RootElement.GetString();
                }
            }
            catch
            {
                // เผื่อ API ส่งเป็น text/plain
                tok = json.Trim('"', ' ', '\n', '\r', '\t');
            }

            if (string.IsNullOrWhiteSpace(tok))
            {
                _logger.LogWarning("Login ok but token missing. Base={Base} Body={Body}", baseUrl, json);
                return null;
            }

            _token = tok;
            _expiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(tokenTtl); // ตามค่าใน appsettings:Auth:TokenLifetimeMinutes
            _logger.LogInformation("Login success. Token cached until {exp}", _expiresAtUtc);
            return _token;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Login exception {BaseUrl}", baseUrl);
            return null;
        }
    }
}
