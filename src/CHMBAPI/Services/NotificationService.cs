using CHMBAPI.Data;
using CHMBAPI.DTOs;
using CHMBAPI.Entities;
using Microsoft.EntityFrameworkCore;

namespace CHMBAPI.Services;

public class NotificationService
{
    private readonly AppDbContext _db;

    public NotificationService(AppDbContext db)
    {
        _db = db;
    }

    public async Task CreateNotificationAsync(long memberId, string type, string title, string message)
    {
        var notification = new MemberNotification
        {
            MemberId = memberId,
            NotificationType = type,
            Title = title,
            Message = message,
            IsRead = false,
            CreatedAt = DateTime.UtcNow
        };

        _db.MemberNotifications.Add(notification);
        await _db.SaveChangesAsync();
    }

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
}