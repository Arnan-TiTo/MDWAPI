using MDWAPI.Data;
using MDWAPI.DTOs;
using MDWAPI.Entities;
using MDWAPI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MDWAPI.Controllers;

/// <summary>LINE Login OAuth + Webhook endpoints</summary>
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

    // ═══════════════════════════════════════════════
    // Public Config (no auth — LIFF ID is not secret)
    // ═══════════════════════════════════════════════

    /// <summary>
    /// คืน LIFF ID + Login Channel ID (ไม่ต้อง auth เพราะไม่ใช่ secret)
    /// GET /api/line/config
    /// GET /api/line/config?companyId=1
    /// GET /api/line/config?liffId=2009472836-xxx
    /// GET /api/line/config?domain=shop.vibeandchic.com
    /// ถ้าไม่ส่ง param = ใช้ตัวแรกที่ active (backward compat)
    /// </summary>
    [HttpGet("config")]
    public async Task<IActionResult> GetConfig(
        [FromQuery] int? companyId = null,
        [FromQuery] string? liffId = null,
        [FromQuery] string? domain = null)
    {
        var query = _db.LineOaConfigs.Where(c => c.IsActive);

        if (companyId.HasValue)
            query = query.Where(c => c.CompanysId == companyId.Value);
        else if (!string.IsNullOrEmpty(liffId))
            query = query.Where(c => c.LiffId == liffId);
        else if (!string.IsNullOrEmpty(domain))
            query = query.Where(c => c.LoginCallbackUrl != null && c.LoginCallbackUrl.Contains(domain));
        // else: ไม่มี param → ใช้ตัวแรกที่ active

        var config = await query
            .Select(c => new { c.LiffId, c.LoginChannelId, c.LineOaName, c.CompanysId })
            .FirstOrDefaultAsync();

        if (config == null)
            return Ok(new { liffId = (string?)null, loginChannelId = (string?)null, companyId = (int?)null });

        return Ok(new { liffId = config.LiffId, loginChannelId = config.LoginChannelId, oaName = config.LineOaName, companyId = config.CompanysId });
    }

    // ═══════════════════════════════════════════════
    // LINE Login OAuth
    // ═══════════════════════════════════════════════

    /// <summary>
    /// Step 1: redirect ผู้ใช้ไปหน้า LINE Login
    /// GET /api/line/login → redirect to LINE
    /// </summary>
    [HttpGet("login")]
    public IActionResult Login([FromQuery] string? redirectAfter = null)
    {
        var state = redirectAfter ?? "default";
        var url = _lineLogin.GetAuthorizationUrl(state);
        return Redirect(url);
    }

    /// <summary>
    /// Step 2: LINE redirect กลับมาพร้อม code
    /// GET /api/line/callback?code=xxx&state=xxx
    /// </summary>
    [HttpGet("callback")]
    public async Task<IActionResult> Callback([FromQuery] string code, [FromQuery] string? state)
    {
        try
        {
            // 1. แลก code → token
            var tokenResp = await _lineLogin.ExchangeCodeAsync(code);
            if (tokenResp == null)
                return BadRequest(new { error = "Failed to exchange LINE code for token" });

            // 2. ดึง profile
            var profile = await _lineLogin.GetProfileAsync(tokenResp.AccessToken);
            if (profile == null)
                return BadRequest(new { error = "Failed to get LINE profile" });

            // 3. ดึง email จาก id_token (ถ้ามี)
            string? email = null;
            if (!string.IsNullOrEmpty(tokenResp.IdToken))
            {
                var idPayload = await _lineLogin.VerifyIdTokenAsync(tokenResp.IdToken);
                email = idPayload?.Email;
            }

            // 4. หา CompanysId จาก LineLoginService config cache
            var companysId = await ResolveCompanysIdAsync(null);

            // 5. ค้นหา member ที่ผูก LINE userId นี้
            var existingProfile = await _memberService.GetByLineUserIdAsync(profile.UserId);

            if (existingProfile != null)
            {
                // member มีอยู่แล้ว → update CompanysId ถ้ายังไม่มี
                await _memberService.EnsureCompanysIdAsync(profile.UserId, companysId);
                return Ok(new
                {
                    isNewMember = false,
                    member = await _memberService.GetByLineUserIdAsync(profile.UserId),
                    lineAccessToken = tokenResp.AccessToken
                });
            }

            // 6. สมัครสมาชิกใหม่ + set CompanysId
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
                lineAccessToken = tokenResp.AccessToken
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LINE callback error");
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// สำหรับ LIFF/Frontend: ส่ง LINE access token มา → ดึง/สร้าง member
    /// POST /api/line/auth { accessToken: "xxx" }
    /// </summary>
    [HttpPost("auth")]
    public async Task<IActionResult> AuthByToken([FromBody] LineAuthRequest req)
    {
        try
        {
            // ดึง profile จาก access token
            var profile = await _lineLogin.GetProfileAsync(req.AccessToken);
            if (profile == null)
                return Unauthorized(new { error = "Invalid LINE access token" });

            // หา CompanysId จาก liffId ที่ frontend ส่งมา
            var companysId = await ResolveCompanysIdAsync(req.LiffId);

            // ค้นหา member
            var existing = await _memberService.GetByLineUserIdAsync(profile.UserId);
            if (existing != null)
            {
                // update CompanysId ถ้ายังไม่มี
                await _memberService.EnsureCompanysIdAsync(profile.UserId, companysId);
                return Ok(new { isNewMember = false, member = await _memberService.GetByLineUserIdAsync(profile.UserId) });
            }

            // สร้างใหม่ + set CompanysId
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
            return BadRequest(new { error = ex.Message });
        }
    }

    // ═══════════════════════════════════════════════
    // LINE Webhook (Messaging API)
    // ═══════════════════════════════════════════════

    /// <summary>
    /// LINE Messaging API Webhook endpoint
    /// POST /api/line/webhook
    /// </summary>
    [HttpPost("webhook")]
    public async Task<IActionResult> Webhook()
    {
        // 1. อ่าน body
        using var reader = new StreamReader(Request.Body);
        var body = await reader.ReadToEndAsync();

        // 2. Verify signature
        var signature = Request.Headers["X-Line-Signature"].FirstOrDefault();
        if (string.IsNullOrEmpty(signature) || !_lineWebhook.VerifySignature(body, signature))
        {
            _logger.LogWarning("LINE webhook: invalid signature");
            return Unauthorized();
        }

        // 3. Parse events
        var webhookBody = _lineWebhook.ParseEvents(body);
        if (webhookBody?.Events == null) return Ok();

        foreach (var evt in webhookBody.Events)
        {
            try
            {
                await ProcessEventAsync(evt, body);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing LINE event: {Type}", evt.Type);
            }
        }

        return Ok();
    }

    private async Task ProcessEventAsync(LineWebhookEvent evt, string rawPayload)
    {
        var userId = evt.Source?.UserId;
        if (string.IsNullOrEmpty(userId)) return;

        switch (evt.Type)
        {
            case "follow":
                _logger.LogInformation("LINE follow: {UserId}", userId);
                // ค้นหา member — ถ้ามี ส่งต้อนรับ
                var member = await _memberService.GetByLineUserIdAsync(userId);
                if (member != null && !string.IsNullOrEmpty(evt.ReplyToken))
                {
                    await _lineWebhook.ReplyAsync(evt.ReplyToken,
                        $"ยินดีต้อนรับกลับ {member.DisplayName}! 🎉\n" +
                        $"รหัสสมาชิก: {member.MemberCode}\n" +
                        $"แต้มคงเหลือ: {member.PointBalance?.AvailablePoints ?? 0} แต้ม");
                }
                else if (!string.IsNullOrEmpty(evt.ReplyToken))
                {
                    await _lineWebhook.ReplyAsync(evt.ReplyToken,
                        "สวัสดีค่ะ! 🎀\nยินดีต้อนรับสู่ระบบสมาชิกสะสมแต้ม\nกดลิงก์ด้านล่างเพื่อสมัครสมาชิกนะคะ");
                }
                break;

            case "unfollow":
                _logger.LogInformation("LINE unfollow: {UserId}", userId);
                break;

            case "message":
                await ProcessMessageAsync(evt, userId, rawPayload);
                break;

            default:
                _logger.LogInformation("LINE event: {Type} from {UserId}", evt.Type, userId);
                break;
        }
    }

    private async Task ProcessMessageAsync(LineWebhookEvent evt, string userId, string rawPayload)
    {
        var messageType = evt.Message?.Type ?? "unknown";
        var messageText = evt.Message?.Text;

        // บันทึกลง LineMessageInbox
        var member = await _db.MemberIdentities
            .FirstOrDefaultAsync(i => i.ProviderUserKey == userId && i.IsActive);

        _db.LineMessageInbox.Add(new LineMessageInboxEntry
        {
            MemberId = member?.MemberId,
            LineUserId = userId,
            MessageType = messageType.ToUpper(),
            RawPayload = rawPayload,
            ProcessStatus = "New",
            CreatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        // ตอบกลับอัตโนมัติ (ถ้าเป็น text)
        if (messageType == "text" && !string.IsNullOrEmpty(evt.ReplyToken))
        {
            var reply = messageText?.ToLower() switch
            {
                "แต้ม" or "คะแนน" or "points" => await GetPointsReplyAsync(member?.MemberId),
                "สมัคร" or "register" => "กดลิงก์ด้านล่างเพื่อสมัครสมาชิกค่ะ 🎀",
                _ => "ขอบคุณที่ทักมาค่ะ! 💕\nพิมพ์ \"แต้ม\" เพื่อเช็คแต้มของคุณ"
            };

            await _lineWebhook.ReplyAsync(evt.ReplyToken, reply);
        }

        _logger.LogInformation("LINE message from {UserId}: {Type} - {Text}", userId, messageType, messageText ?? "(no text)");
    }

    private async Task<string> GetPointsReplyAsync(long? memberId)
    {
        if (!memberId.HasValue)
            return "กรุณาสมัครสมาชิกก่อนนะคะ เพื่อเช็คแต้มของคุณ 🎉";

        var account = await _db.PointAccounts.FirstOrDefaultAsync(p => p.MemberId == memberId);
        if (account == null)
            return "ยังไม่มีแต้มสะสมค่ะ 😊 ช้อปเพิ่มเพื่อสะสมแต้มนะคะ!";

        return $"🏆 แต้มของคุณ\n" +
               $"───────────\n" +
               $"✨ แต้มคงเหลือ: {account.AvailablePoints:N0} แต้ม\n" +
               $"📦 แต้มที่จอง: {account.ReservedPoints:N0} แต้ม\n" +
               $"📊 รวมสะสม: {account.TotalEarned:N0} แต้ม\n" +
               $"🎁 ใช้ไปแล้ว: {account.TotalBurned:N0} แต้ม";
    }

    // ═══════════════════════════════════════════════
    // Helper: resolve CompanysId from LIFF ID
    // ═══════════════════════════════════════════════

    /// <summary>หา CompanysId จาก liffId (frontend ส่งมา) หรือ config ตัวแรกที่ active</summary>
    private async Task<int?> ResolveCompanysIdAsync(string? liffId)
    {
        if (!string.IsNullOrEmpty(liffId))
        {
            var config = await _db.LineOaConfigs
                .Where(c => c.IsActive && c.LiffId == liffId)
                .Select(c => (int?)c.CompanysId)
                .FirstOrDefaultAsync();
            if (config.HasValue) return config;
        }

        // Fallback: ใช้ config ตัวแรกที่ active
        return await _db.LineOaConfigs
            .Where(c => c.IsActive)
            .Select(c => (int?)c.CompanysId)
            .FirstOrDefaultAsync();
    }
}

// ─── Request DTOs ─────────────────────────────────
public class LineAuthRequest
{
    public string AccessToken { get; set; } = default!;
    /// <summary>LIFF ID ที่ frontend ใช้ — เพื่อ match กลับหา CompanysId</summary>
    public string? LiffId { get; set; }
}
