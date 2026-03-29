using MDWAPI.Data;
using MDWAPI.DTOs;
using MDWAPI.Entities;
using Microsoft.EntityFrameworkCore;

namespace MDWAPI.Services;

public class NotificationService
{
    private readonly AppDbContext _db;

    public NotificationService(AppDbContext db) => _db = db;

    /// <summary>ดึงรายการแจ้งเตือนของสมาชิก</summary>
    public async Task<List<MemberNotificationDto>> GetMemberNotificationsAsync(long memberId, int limit = 50)
    {
        return await _db.MemberNotifications
            .Where(n => n.MemberId == memberId)
            .OrderByDescending(n => n.CreatedAt)
            .Take(limit)
            .Select(n => new MemberNotificationDto
            {
                NotificationId = n.NotificationId,
                NotificationType = n.NotificationType,
                Title = n.Title,
                Message = n.Message,
                IsRead = n.IsRead,
                CreatedAt = n.CreatedAt
            })
            .ToListAsync();
    }

    /// <summary>ทำเครื่องหมายว่าอ่านแล้ว</summary>
    public async Task MarkAsReadAsync(long notificationId)
    {
        var n = await _db.MemberNotifications.FindAsync(notificationId);
        if (n != null && !n.IsRead)
        {
            n.IsRead = true;
            n.ReadAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        }
    }

    /// <summary>ส่งการแจ้งเตือนไปยังสมาชิก (Internal use)</summary>
    public async Task SendNotificationAsync(long memberId, string type, string title, string message, string? refType = null, string? refId = null)
    {
        _db.MemberNotifications.Add(new MemberNotification
        {
            MemberId = memberId,
            NotificationType = type,
            Title = title,
            Message = message,
            RefType = refType,
            RefId = refId,
            CreatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();
    }
}
