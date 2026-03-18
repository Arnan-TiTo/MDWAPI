using MDWAPI.Data;
using MDWAPI.Entities;
using Microsoft.EntityFrameworkCore;

namespace MDWAPI.Services;

/// <summary>สร้าง notification ข้อความ LINE แล้วเขียนลง OutboxMessages</summary>
public class LineNotificationService
{
    private readonly AppDbContext _db;
    private readonly ILogger<LineNotificationService> _logger;

    public LineNotificationService(AppDbContext db, ILogger<LineNotificationService> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>แจ้งเตือน member ว่าได้แต้ม</summary>
    public async Task NotifyEarnAsync(long memberId, int points, string orderId, int totalPoints)
    {
        var info = await GetLineInfoAsync(memberId);
        if (info == null) return;

        var message = $"🎉 คุณได้รับ {points} แต้ม!\n"
                    + $"📦 จากออเดอร์ {orderId}\n"
                    + $"💰 แต้มคงเหลือ: {totalPoints} แต้ม\n"
                    + $"ขอบคุณที่ซื้อสินค้ากับเรา ❤️";

        await EnqueueAsync(memberId, "EARN_POINTS", info.Value.userId, info.Value.companysId, message);
    }

    /// <summary>แจ้งเตือน member ว่าถูกหักแต้มคืน (return/refund)</summary>
    public async Task NotifyReversalAsync(long memberId, int points, string orderId, int totalPoints)
    {
        var info = await GetLineInfoAsync(memberId);
        if (info == null) return;

        var message = $"📋 แจ้งเตือนการปรับแต้ม\n"
                    + $"ออเดอร์ {orderId} ถูกยกเลิก/คืนสินค้า\n"
                    + $"หักคืน {points} แต้ม\n"
                    + $"💰 แต้มคงเหลือ: {totalPoints} แต้ม";

        await EnqueueAsync(memberId, "EARN_REVERSAL", info.Value.userId, info.Value.companysId, message);
    }

    /// <summary>แจ้งเตือน member แลกรางวัลสำเร็จ</summary>
    public async Task NotifyRedemptionAsync(long memberId, string rewardName, string? code, int pointsSpent, int remainingPoints)
    {
        var info = await GetLineInfoAsync(memberId);
        if (info == null) return;

        var message = $"🎁 แลกรางวัลสำเร็จ!\n"
                    + $"รางวัล: {rewardName}\n"
                    + (code != null ? $"📋 โค้ด: {code}\n" : "")
                    + $"ใช้ไป: {pointsSpent} แต้ม\n"
                    + $"💰 แต้มคงเหลือ: {remainingPoints} แต้ม";

        await EnqueueAsync(memberId, "REDEMPTION", info.Value.userId, info.Value.companysId, message);
    }

    /// <summary>แจ้งเตือนแต้มใกล้หมดอายุ</summary>
    public async Task NotifyExpiryWarningAsync(long memberId, int expiringPoints, int daysLeft)
    {
        var info = await GetLineInfoAsync(memberId);
        if (info == null) return;

        var message = $"⏰ แจ้งเตือนแต้มใกล้หมดอายุ!\n"
                    + $"แต้ม {expiringPoints:N0} แต้มจะหมดอายุในอีก {daysLeft} วัน\n"
                    + $"ใช้แต้มแลกรางวัลก่อนหมดนะคะ 🎁";

        await EnqueueAsync(memberId, "EXPIRY_WARNING", info.Value.userId, info.Value.companysId, message);
    }

    /// <summary>แจ้งเตือนแต้มหมดอายุแล้ว</summary>
    public async Task NotifyExpiredAsync(long memberId, int expiredPoints, int remainingPoints)
    {
        var info = await GetLineInfoAsync(memberId);
        if (info == null) return;

        var message = $"⌛ แต้ม {expiredPoints:N0} แต้มหมดอายุแล้ว\n"
                    + $"💰 แต้มคงเหลือ: {remainingPoints:N0} แต้ม\n"
                    + $"สะสมแต้มเพิ่มจากการสั่งซื้อสินค้า ❤️";

        await EnqueueAsync(memberId, "POINTS_EXPIRED", info.Value.userId, info.Value.companysId, message);
    }

    /// <summary>แจ้งเตือนทั่วไป</summary>
    public async Task NotifyAsync(long memberId, string messageType, string message)
    {
        var info = await GetLineInfoAsync(memberId);
        if (info == null) return;

        await EnqueueAsync(memberId, messageType, info.Value.userId, info.Value.companysId, message);
    }

    /// <summary>หา LINE userId + CompanysId จาก MemberIdentities → Member</summary>
    private async Task<(string userId, int? companysId)?> GetLineInfoAsync(long memberId)
    {
        // หา LINE identity
        var identity = await _db.MemberIdentities
            .Where(i => i.MemberId == memberId
                && i.IsActive
                && (i.ProviderType == "LINE" || i.ProviderType == "LINE_OA" || i.ProviderType == "LINE_LOGIN"))
            .FirstOrDefaultAsync();

        if (identity == null)
        {
            _logger.LogDebug("Member {MemberId} has no LINE identity, skip notification", memberId);
            return null;
        }

        // หา CompanysId: identity → member → fallback platform chain
        int? companysId = identity.CompanysId;
        if (companysId == null)
        {
            var member = await _db.Members_Mbw.FindAsync(memberId);
            companysId = member?.CompanysId;
        }

        return (identity.ProviderUserKey, companysId);
    }

    /// <summary>เขียน message ลง OutboxMessages (Pending) พร้อม companysId</summary>
    private async Task EnqueueAsync(long memberId, string messageType, string lineUserId, int? companysId, string message)
    {
        var outbox = new OutboxMessage
        {
            MemberId = memberId,
            MessageType = messageType,
            Channel = "LINE",
            Payload = System.Text.Json.JsonSerializer.Serialize(new
            {
                to = lineUserId,
                text = message,
                companysId = companysId    // OutboxProcessor ใช้หา token
            }),
            Status = "Pending",
            CreatedAt = DateTime.UtcNow
        };

        _db.OutboxMessages.Add(outbox);
        await _db.SaveChangesAsync();

        _logger.LogInformation(
            "Enqueued LINE notification [{Type}] for Member {MemberId} (Company={CompanysId})",
            messageType, memberId, companysId);
    }
}
