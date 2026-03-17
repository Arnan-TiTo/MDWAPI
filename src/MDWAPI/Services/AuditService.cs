using MDWAPI.Data;
using MDWAPI.Entities;

namespace MDWAPI.Services;

public class AuditService
{
    private readonly AppDbContext _db;

    public AuditService(AppDbContext db) => _db = db;

    public async Task LogAsync(int userId, string actionType, string? entityType = null,
        string? entityId = null, string? oldValue = null, string? newValue = null, string? ipAddress = null)
    {
        if (userId <= 0) return; // skip audit if no valid user

        _db.AdminAuditLogs.Add(new AdminAuditLog
        {
            UserId = userId,
            ActionType = actionType,
            EntityType = entityType,
            EntityId = entityId,
            OldValue = oldValue,
            NewValue = newValue,
            IpAddress = ipAddress,
            CreatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();
    }
}
