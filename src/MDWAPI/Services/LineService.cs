using MDWAPI.Data;
using MDWAPI.DTOs;
using MDWAPI.Entities;
using MDWAPI.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MDWAPI.Services;

/// <summary>LINE Login OAuth 2.1 + OpenID Connect (reads config from DB)</summary>
public class LineLoginService
{
    private readonly AppDbContext _db;
    private readonly HttpClient _http;
    private readonly IConfiguration _config;

    // Cache config from DB (loaded on first use)
    private LineOaConfig? _cachedConfig;

    public LineLoginService(AppDbContext db, HttpClient http, IConfiguration config)
    {
        _db = db;
        _http = http;
        _config = config;
    }

    /// <summary>โหลด config จาก DB (ใช้ตัวแรกที่ Active)</summary>
    private async Task<LineOaConfig?> GetConfigAsync()
    {
        _cachedConfig ??= await _db.LineOaConfigs
            .Where(c => c.IsActive)
            .FirstOrDefaultAsync();
        return _cachedConfig;
    }

    private string GetChannelId()
        => _config["Line:Login:ChannelId"] ?? _cachedConfig?.LoginChannelId ?? "";
    private string GetChannelSecret()
        => _config["Line:Login:ChannelSecret"] ?? _cachedConfig?.LoginChannelSecret ?? "";
    private string GetCallbackUrl()
        => _config["Line:Login:CallbackUrl"] ?? _cachedConfig?.LoginCallbackUrl ?? "";

    /// <summary>สร้าง LINE Login URL (redirect ผู้ใช้ไปหน้า LINE)</summary>
    public async Task<string> GetAuthorizationUrlAsync(string? state = null)
    {
        await GetConfigAsync();
        state ??= Guid.NewGuid().ToString("N");
        var channelId = GetChannelId();
        var callbackUrl = GetCallbackUrl();
        if (string.IsNullOrWhiteSpace(channelId) || string.IsNullOrWhiteSpace(callbackUrl))
            return "";

        var url = "https://access.line.me/oauth2/v2.1/authorize"
            + $"?response_type=code"
            + $"&client_id={channelId}"
            + $"&redirect_uri={Uri.EscapeDataString(callbackUrl)}"
            + $"&state={state}"
            + $"&scope=profile%20openid%20email";
        return url;
    }

    // Sync version for backward compat (uses cached or config)
    public string GetAuthorizationUrl(string? state = null)
    {
        // Try load from DB synchronously using cached value
        if (_cachedConfig == null)
        {
            _cachedConfig = _db.LineOaConfigs
                .Where(c => c.IsActive)
                .FirstOrDefault();
        }
        state ??= Guid.NewGuid().ToString("N");
        var channelId = GetChannelId();
        var callbackUrl = GetCallbackUrl();
        if (string.IsNullOrWhiteSpace(channelId) || string.IsNullOrWhiteSpace(callbackUrl))
            return "";

        var url = "https://access.line.me/oauth2/v2.1/authorize"
            + $"?response_type=code"
            + $"&client_id={channelId}"
            + $"&redirect_uri={Uri.EscapeDataString(callbackUrl)}"
            + $"&state={state}"
            + $"&scope=profile%20openid%20email";
        return url;
    }

    /// <summary>แลก code → access_token + id_token</summary>
    public async Task<LineTokenResponse?> ExchangeCodeAsync(string code)
    {
        await GetConfigAsync();
        var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = GetCallbackUrl(),
            ["client_id"] = GetChannelId(),
            ["client_secret"] = GetChannelSecret()
        });

        var resp = await _http.PostAsync("https://api.line.me/oauth2/v2.1/token", content);
        if (!resp.IsSuccessStatusCode) return null;

        var json = await resp.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<LineTokenResponse>(json, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
        });
    }

    /// <summary>ดึง profile จาก access_token</summary>
    public async Task<LineProfileResponse?> GetProfileAsync(string accessToken)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, "https://api.line.me/v2/profile");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var resp = await _http.SendAsync(req);
        if (!resp.IsSuccessStatusCode) return null;

        var json = await resp.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<LineProfileResponse>(json, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
    }

    /// <summary>Verify id_token</summary>
    public async Task<LineIdTokenPayload?> VerifyIdTokenAsync(string idToken)
    {
        await GetConfigAsync();
        var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["id_token"] = idToken,
            ["client_id"] = GetChannelId()
        });

        var resp = await _http.PostAsync("https://api.line.me/oauth2/v2.1/verify", content);
        if (!resp.IsSuccessStatusCode) return null;

        var json = await resp.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<LineIdTokenPayload>(json, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
        });
    }
}

/// <summary>LINE Messaging API — webhook + push messages (reads config from DB)</summary>
public class LineWebhookService
{
    private readonly AppDbContext _db;
    private readonly HttpClient _http;
    private readonly IConfiguration _config;

    private LineOaConfig? _cachedConfig;

    public LineWebhookService(AppDbContext db, HttpClient http, IConfiguration config)
    {
        _db = db;
        _http = http;
        _config = config;
    }

    private async Task<LineOaConfig?> GetConfigAsync()
    {
        _cachedConfig ??= await _db.LineOaConfigs
            .Where(c => c.IsActive)
            .FirstOrDefaultAsync();
        return _cachedConfig;
    }

    private string GetChannelSecret()
        => _config["Line:Messaging:ChannelSecret"] ?? _cachedConfig?.MsgChannelSecret ?? "";
    private string GetChannelAccessToken()
        => _config["Line:Messaging:ChannelAccessToken"] ?? _cachedConfig?.MsgChannelToken ?? "";

    /// <summary>Verify webhook signature (X-Line-Signature)</summary>
    public bool VerifySignature(string body, string signature)
    {
        // Sync load config
        if (_cachedConfig == null)
            _cachedConfig = _db.LineOaConfigs.Where(c => c.IsActive).FirstOrDefault();

        var secret = GetChannelSecret();
        if (string.IsNullOrEmpty(secret)) return false;

        var key = Encoding.UTF8.GetBytes(secret);
        using var hmac = new HMACSHA256(key);
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(body));
        var expected = Convert.ToBase64String(hash);
        return expected == signature;
    }

    /// <summary>Parse webhook events</summary>
    public LineWebhookBody? ParseEvents(string body)
    {
        return JsonSerializer.Deserialize<LineWebhookBody>(body, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
    }

    /// <summary>ส่ง reply message กลับไปหา user</summary>
    public async Task ReplyAsync(string replyToken, string message)
    {
        await GetConfigAsync();
        var token = GetChannelAccessToken();
        if (string.IsNullOrEmpty(token)) return;

        var payload = new
        {
            replyToken,
            messages = new[]
            {
                new { type = "text", text = message }
            }
        };

        var req = new HttpRequestMessage(HttpMethod.Post, "https://api.line.me/v2/bot/message/reply")
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        await _http.SendAsync(req);
    }

    /// <summary>ส่ง push message ไปหา user โดยตรง</summary>
    public async Task PushMessageAsync(string userId, string message)
    {
        await GetConfigAsync();
        var token = GetChannelAccessToken();
        if (string.IsNullOrEmpty(token)) return;

        var payload = new
        {
            to = userId,
            messages = new[]
            {
                new { type = "text", text = message }
            }
        };

        var req = new HttpRequestMessage(HttpMethod.Post, "https://api.line.me/v2/bot/message/push")
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        await _http.SendAsync(req);
    }
}

// ─── LINE DTOs ────────────────────────────────────
public class LineTokenResponse
{
    public string AccessToken { get; set; } = default!;
    public int ExpiresIn { get; set; }
    public string? IdToken { get; set; }
    public string? RefreshToken { get; set; }
    public string? Scope { get; set; }
    public string TokenType { get; set; } = default!;
}

public class LineProfileResponse
{
    public string UserId { get; set; } = default!;
    public string? DisplayName { get; set; }
    public string? PictureUrl { get; set; }
    public string? StatusMessage { get; set; }
}

public class LineIdTokenPayload
{
    public string Sub { get; set; } = default!;       // LINE userId
    public string? Name { get; set; }
    public string? Picture { get; set; }
    public string? Email { get; set; }
}

public class LineWebhookBody
{
    public string? Destination { get; set; }
    public List<LineWebhookEvent> Events { get; set; } = new();
}

public class LineWebhookEvent
{
    public string Type { get; set; } = default!;       // message / follow / unfollow / postback
    public string? ReplyToken { get; set; }
    public string? Mode { get; set; }
    public long Timestamp { get; set; }
    public LineWebhookSource? Source { get; set; }
    public LineWebhookMessage? Message { get; set; }
}

public class LineWebhookSource
{
    public string Type { get; set; } = default!;       // user / group / room
    public string? UserId { get; set; }
}

public class LineWebhookMessage
{
    public string Id { get; set; } = default!;
    public string Type { get; set; } = default!;       // text / image / sticker
    public string? Text { get; set; }
}
