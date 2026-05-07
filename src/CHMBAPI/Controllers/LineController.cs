using CHMBAPI.Data;
using CHMBAPI.DTOs;
using CHMBAPI.Entities;
using CHMBAPI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CHMBAPI.Controllers;

[ApiController]
[Route("api/line")]
[Tags("LINE Integration")]
public class LineController : ControllerBase
{
    private readonly LineLoginService _lineLogin;
    private readonly LineWebhookService _lineWebhook;
    private readonly MemberService _memberService;
    private readonly AppDbContext _db;
    private readonly ILogger<LineController> _logger;

    public LineController(
        LineLoginService lineLogin,
        LineWebhookService lineWebhook,
        MemberService memberService,
        AppDbContext db,
        ILogger<LineController> logger)
    {
        _lineLogin = lineLogin;
        _lineWebhook = lineWebhook;
        _memberService = memberService;
        _db = db;
        _logger = logger;
    }

    [HttpGet("config")]
    public async Task<IActionResult> GetConfig(
        [FromQuery] int? companyId = null,
        [FromQuery] string? liffId = null,
        [FromQuery] string? domain = null)
    {
        var query = _db.LineOaConfigs.Where(c => c.IsActive);

        if (companyId.HasValue)
        {
            query = query.Where(c => c.CompanysId == companyId.Value);
        }
        else if (!string.IsNullOrWhiteSpace(liffId))
        {
            query = query.Where(c => c.LiffId == liffId);
        }
        else if (!string.IsNullOrWhiteSpace(domain))
        {
            query = query.Where(c => c.LoginCallbackUrl != null && c.LoginCallbackUrl.Contains(domain));
        }

        var config = await query
            .Select(c => new { c.LiffId, c.LoginChannelId, c.LineOaName, c.CompanysId })
            .FirstOrDefaultAsync();

        if (config == null)
        {
            return Ok(new { liffId = (string?)null, loginChannelId = (string?)null, companyId = (int?)null });
        }

        return Ok(new
        {
            liffId = config.LiffId,
            loginChannelId = config.LoginChannelId,
            oaName = config.LineOaName,
            companyId = config.CompanysId
        });
    }

    [HttpGet("login")]
    public IActionResult Login([FromQuery] string? redirectAfter = null)
    {
        var state = redirectAfter ?? "default";
        var url = _lineLogin.GetAuthorizationUrl(state);
        return Redirect(url);
    }

    [HttpGet("callback")]
    public async Task<IActionResult> Callback([FromQuery] string code, [FromQuery] string? state)
    {
        try
        {
            var tokenResp = await _lineLogin.ExchangeCodeAsync(code);
            if (tokenResp == null)
            {
                return BadRequest(new { error = "Failed to exchange LINE code for token" });
            }

            var profile = await _lineLogin.GetProfileAsync(tokenResp.AccessToken);
            if (profile == null)
            {
                return BadRequest(new { error = "Failed to get LINE profile" });
            }

            string? email = null;
            if (!string.IsNullOrEmpty(tokenResp.IdToken))
            {
                var idPayload = await _lineLogin.VerifyIdTokenAsync(tokenResp.IdToken);
                email = idPayload?.Email;
            }

            var companysId = await ResolveCompanysIdAsync(null);
            var existingProfile = await _memberService.GetByLineUserIdAsync(profile.UserId);

            if (existingProfile != null)
            {
                await _memberService.EnsureCompanysIdAsync(profile.UserId, companysId);
                return Ok(new
                {
                    isNewMember = false,
                    member = await _memberService.GetByLineUserIdAsync(profile.UserId),
                    lineAccessToken = tokenResp.AccessToken,
                    state
                });
            }

            var newMember = await _memberService.RegisterAsync(new MemberRegisterRequest
            {
                DisplayName = profile.DisplayName,
                Email = email,
                ConsentAccepted = true,
                LineProviderType = "LINE_LOGIN",
                LineUserId = profile.UserId,
                LinePictureUrl = profile.PictureUrl,
                CompanysId = companysId
            });

            return Ok(new
            {
                isNewMember = true,
                member = newMember,
                lineAccessToken = tokenResp.AccessToken,
                state
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LINE callback error");
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("auth")]
    public async Task<IActionResult> AuthByToken([FromBody] LineAuthRequest req)
    {
        try
        {
            var profile = await _lineLogin.GetProfileAsync(req.AccessToken);
            if (profile == null)
            {
                return Unauthorized(new { error = "Invalid LINE access token" });
            }

            var companysId = await ResolveCompanysIdAsync(req.LiffId);
            var existing = await _memberService.GetByLineUserIdAsync(profile.UserId);

            if (existing != null)
            {
                await _memberService.EnsureCompanysIdAsync(profile.UserId, companysId);
                return Ok(new
                {
                    isNewMember = false,
                    member = await _memberService.GetByLineUserIdAsync(profile.UserId)
                });
            }

            var newMember = await _memberService.RegisterAsync(new MemberRegisterRequest
            {
                DisplayName = profile.DisplayName,
                ConsentAccepted = true,
                LineProviderType = "LINE_LOGIN",
                LineUserId = profile.UserId,
                LinePictureUrl = profile.PictureUrl,
                CompanysId = companysId
            });

            return Ok(new { isNewMember = true, member = newMember });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LINE auth by token error");
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("webhook")]
    public async Task<IActionResult> Webhook()
    {
        using var reader = new StreamReader(Request.Body);
        var body = await reader.ReadToEndAsync();

        var signature = Request.Headers["X-Line-Signature"].FirstOrDefault();
        if (string.IsNullOrEmpty(signature) || !_lineWebhook.VerifySignature(body, signature))
        {
            _logger.LogWarning("LINE webhook invalid signature");
            return Unauthorized();
        }

        var webhookBody = _lineWebhook.ParseEvents(body);
        if (webhookBody?.Events == null)
        {
            return Ok();
        }

        foreach (var evt in webhookBody.Events)
        {
            try
            {
                await ProcessEventAsync(evt, body);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing LINE event: {EventType}", evt.Type);
            }
        }

        return Ok();
    }

    private async Task ProcessEventAsync(LineWebhookEvent evt, string rawPayload)
    {
        var userId = evt.Source?.UserId;
        if (string.IsNullOrEmpty(userId))
        {
            return;
        }

        switch (evt.Type)
        {
            case "follow":
                await ProcessFollowAsync(evt, userId);
                break;
            case "unfollow":
                _logger.LogInformation("LINE unfollow: {UserId}", userId);
                break;
            case "message":
                await ProcessMessageAsync(evt, userId, rawPayload);
                break;
            default:
                _logger.LogInformation("LINE event: {EventType} from {UserId}", evt.Type, userId);
                break;
        }
    }

    private async Task ProcessFollowAsync(LineWebhookEvent evt, string userId)
    {
        _logger.LogInformation("LINE follow: {UserId}", userId);

        var member = await _memberService.GetByLineUserIdAsync(userId);
        if (string.IsNullOrEmpty(evt.ReplyToken))
        {
            return;
        }

        if (member != null)
        {
            await _lineWebhook.ReplyAsync(
                evt.ReplyToken,
                $"Welcome back {member.DisplayName}! Member code: {member.MemberCode}, points: {member.PointBalance?.AvailablePoints ?? 0}");
            return;
        }

        await _lineWebhook.ReplyAsync(
            evt.ReplyToken,
            "Welcome to our member system. Please register through the LIFF link to start earning points.");
    }

    private async Task ProcessMessageAsync(LineWebhookEvent evt, string userId, string rawPayload)
    {
        var messageType = evt.Message?.Type ?? "unknown";
        var messageText = evt.Message?.Text;

        var identity = await _db.MemberIdentities
            .FirstOrDefaultAsync(i => i.ProviderUserKey == userId && i.IsActive);

        _db.LineMessageInbox.Add(new LineMessageInboxEntry
        {
            MemberId = identity?.MemberId,
            LineUserId = userId,
            MessageType = messageType.ToUpperInvariant(),
            RawPayload = rawPayload,
            ProcessStatus = "New",
            CreatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        if (messageType == "text" && !string.IsNullOrEmpty(evt.ReplyToken))
        {
            var reply = await GetReplyTextAsync(identity?.MemberId, messageText);
            await _lineWebhook.ReplyAsync(evt.ReplyToken, reply);
        }

        _logger.LogInformation("LINE message from {UserId}: {Type} - {Text}", userId, messageType, messageText ?? "(no text)");
    }

    private async Task<string> GetReplyTextAsync(long? memberId, string? messageText)
    {
        return messageText?.Trim().ToLowerInvariant() switch
        {
            "แต้ม" or "คะแนน" or "points" => await GetPointsReplyAsync(memberId),
            "สมัคร" or "register" => "Please continue registration through the member LIFF page.",
            _ => "Thanks for your message. Send 'แต้ม' or 'points' to check your current balance."
        };
    }

    private async Task<string> GetPointsReplyAsync(long? memberId)
    {
        if (!memberId.HasValue)
        {
            return "Please register your member account first to check your points.";
        }

        var account = await _db.PointAccounts.FirstOrDefaultAsync(p => p.MemberId == memberId.Value);
        if (account == null)
        {
            return "You do not have any points yet.";
        }

        return $"Available: {account.AvailablePoints:N0} points, Reserved: {account.ReservedPoints:N0}, Total earned: {account.TotalEarned:N0}, Total burned: {account.TotalBurned:N0}";
    }

    private async Task<int?> ResolveCompanysIdAsync(string? liffId)
    {
        if (!string.IsNullOrWhiteSpace(liffId))
        {
            var config = await _db.LineOaConfigs
                .Where(c => c.IsActive && c.LiffId == liffId)
                .Select(c => (int?)c.CompanysId)
                .FirstOrDefaultAsync();

            if (config.HasValue)
            {
                return config;
            }
        }

        return await _db.LineOaConfigs
            .Where(c => c.IsActive)
            .Select(c => (int?)c.CompanysId)
            .FirstOrDefaultAsync();
    }
}

public class LineAuthRequest
{
    public string AccessToken { get; set; } = default!;
    public string? LiffId { get; set; }
}
