using MDWAPI.Services;
using Microsoft.AspNetCore.Mvc;

namespace MDWAPI.Controllers;

[ApiController]
[Route("api/notifications")]
[Tags("Notification")]
public class NotificationController : ControllerBase
{
    private readonly NotificationService _notificationService;

    public NotificationController(NotificationService notificationService)
    {
        _notificationService = notificationService;
    }

    /// <summary>ดึงรายการแจ้งเตือนของสมาชิก</summary>
    [HttpGet("member/{memberId:long}")]
    public async Task<IActionResult> GetMemberNotifications(long memberId, int limit = 50)
    {
        var result = await _notificationService.GetMemberNotificationsAsync(memberId, limit);
        return Ok(result);
    }

    /// <summary>ทำเครื่องหมายว่าอ่านแล้ว</summary>
    [HttpPost("{notificationId:long}/read")]
    public async Task<IActionResult> MarkAsRead(long notificationId)
    {
        await _notificationService.MarkAsReadAsync(notificationId);
        return Ok(new { success = true });
    }
}
