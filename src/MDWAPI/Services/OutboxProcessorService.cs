using MDWAPI.Data;
using MDWAPI.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace MDWAPI.Services;

/// <summary>Background job: อ่าน OutboxMessages (Pending) → ส่ง LINE push message จริง</summary>
public class OutboxProcessorService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OutboxProcessorService> _logger;
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(15);
    private const int MaxRetry = 3;
    private const int BatchSize = 20;

    public OutboxProcessorService(IServiceScopeFactory scopeFactory, ILogger<OutboxProcessorService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("OutboxProcessor started (interval={Interval}s)", Interval.TotalSeconds);

        // รอ app เริ่มเสร็จก่อน
        await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessBatchAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "OutboxProcessor error");
            }

            await Task.Delay(Interval, stoppingToken);
        }
    }

    private async Task ProcessBatchAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var httpFactory = scope.ServiceProvider.GetRequiredService<IHttpClientFactory>();

        // ดึง messages ที่รอส่ง
        var pending = await db.OutboxMessages
            .Where(m => m.Status == "Pending" && m.RetryCount < MaxRetry)
            .OrderBy(m => m.CreatedAt)
            .Take(BatchSize)
            .ToListAsync(ct);

        if (!pending.Any()) return;

        _logger.LogInformation("OutboxProcessor: processing {Count} messages", pending.Count);

        // Cache: CompanysId → MsgChannelToken
        var tokenCache = new Dictionary<int, string>();

        // Fallback token จาก config (กรณียังไม่มี CompanysId)
        var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var fallbackToken = config["Line:Messaging:ChannelAccessToken"] ?? "";

        foreach (var msg in pending)
        {
            try
            {
                if (msg.Channel != "LINE" || string.IsNullOrEmpty(msg.Payload))
                {
                    msg.Status = "Skipped";
                    msg.SentAt = DateTime.UtcNow;
                    continue;
                }

                // Parse payload
                var payload = JsonSerializer.Deserialize<OutboxPayload>(msg.Payload,
                    new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

                if (payload == null || string.IsNullOrEmpty(payload.To) || string.IsNullOrEmpty(payload.Text))
                {
                    msg.Status = "Failed";
                    msg.LastError = "Invalid payload";
                    continue;
                }

                // หา token จาก LineOaConfigs ตาม CompanysId
                var token = await ResolveTokenAsync(db, tokenCache, payload.CompanysId, fallbackToken, ct);

                if (string.IsNullOrEmpty(token))
                {
                    msg.Status = "Failed";
                    msg.LastError = "No LINE OA token found";
                    continue;
                }

                // ส่ง LINE push message
                await PushMessageAsync(httpFactory, token, payload.To, payload.Text);

                msg.Status = "Sent";
                msg.SentAt = DateTime.UtcNow;

                _logger.LogInformation(
                    "Sent LINE [{Type}] to Member {MemberId} (CompanysId={CompanysId}, OutboxId={OutboxId})",
                    msg.MessageType, msg.MemberId, payload.CompanysId, msg.OutboxId);
            }
            catch (Exception ex)
            {
                msg.RetryCount++;
                msg.LastError = ex.Message.Length > 1000 ? ex.Message[..1000] : ex.Message;

                if (msg.RetryCount >= MaxRetry)
                {
                    msg.Status = "Failed";
                    _logger.LogWarning(
                        "LINE message failed permanently (OutboxId={OutboxId}): {Error}",
                        msg.OutboxId, ex.Message);
                }
                else
                {
                    _logger.LogWarning(
                        "LINE message retry {Retry}/{Max} (OutboxId={OutboxId}): {Error}",
                        msg.RetryCount, MaxRetry, msg.OutboxId, ex.Message);
                }
            }
        }

        await db.SaveChangesAsync(ct);
    }

    /// <summary>หา ChannelAccessToken จาก LineOaConfigs ตาม CompanysId (cache)</summary>
    private static async Task<string> ResolveTokenAsync(
        AppDbContext db, Dictionary<int, string> cache, int? companysId, string fallbackToken, CancellationToken ct)
    {
        if (companysId == null || companysId == 0)
            return fallbackToken;

        if (cache.TryGetValue(companysId.Value, out var cached))
            return cached;

        var config = await db.LineOaConfigs
            .Where(c => c.CompanysId == companysId.Value && c.IsActive)
            .FirstOrDefaultAsync(ct);

        var token = config?.MsgChannelToken ?? fallbackToken;
        cache[companysId.Value] = token;
        return token;
    }

    /// <summary>ยิง LINE Messaging API push message โดยตรง (ไม่ผ่าน LineWebhookService เพราะต้องใช้ token ต่างกันแต่ละ company)</summary>
    private static async Task PushMessageAsync(IHttpClientFactory httpFactory, string channelAccessToken, string userId, string message)
    {
        var http = httpFactory.CreateClient();

        var payload = new
        {
            to = userId,
            messages = new[] { new { type = "text", text = message } }
        };

        var req = new HttpRequestMessage(HttpMethod.Post, "https://api.line.me/v2/bot/message/push")
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", channelAccessToken);

        var resp = await http.SendAsync(req);
        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync();
            throw new HttpRequestException($"LINE push failed ({resp.StatusCode}): {body}");
        }
    }
}

internal class OutboxPayload
{
    public string To { get; set; } = "";
    public string Text { get; set; } = "";
    public int? CompanysId { get; set; }
}
